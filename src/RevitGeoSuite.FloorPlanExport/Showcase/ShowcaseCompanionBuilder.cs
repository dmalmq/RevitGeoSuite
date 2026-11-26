using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Export;

namespace RevitGeoSuite.FloorPlanExport.Showcase;

/// <summary>
/// Builds a separate read-only Forma Companion <c>.rvt</c> document from a validated <c>kanizu.gis-showcase</c> v1
/// snapshot, stamping all 4 F2 export-loop guards:
/// <list type="number">
/// <item><c>gis_model_role = "showcase"</c> on Project Information (frozen GUID)</item>
/// <item><c>DirectShape.ApplicationId = "Kanizu.Showcase"</c> and stable <c>ApplicationDataId = "&lt;layer&gt;:&lt;key&gt;"</c></item>
/// <item><c>KNZ_FRM_*</c> element naming</item>
/// <item>Deterministic UUIDv5 <c>ks_*</c> companion-only shared parameters (+ <c>ks_attributes_json</c> overflow)</item>
/// </list>
/// Refuses to mutate any authoring <c>.rvt</c> document. Compatible with Revit 2024 (<c>net48</c>) and Revit 2027 (<c>net10.0-windows</c>).
/// </summary>
public sealed class ShowcaseCompanionBuilder
{
    public const string BuildTransactionName = "Kanizu Forma Showcase: Build companion elements";

    private static readonly BuiltInCategory[] CompanionDirectShapeCategories =
    {
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_SpecialityEquipment,
        BuiltInCategory.OST_GenericModel,
    };

    /// <summary>
    /// Creates a brand-new companion <c>.rvt</c> document from <paramref name="snapshot"/> without ever touching
    /// an active authoring document. When no <c>CoordinateTransformOverride</c> is provided and the new document's
    /// active project location is at <c>(0, 0, 0, 0)</c>, localizes the internal origin to the snapshot's
    /// bounding-box center and configures shared coordinates via <c>ActiveProjectLocation.SetProjectPosition</c>.
    /// </summary>
    public ShowcaseCompanionBuildResult BuildNewCompanion(
        Application application,
        ShowcaseSnapshot snapshot,
        ShowcaseCompanionBuildOptions? options = null)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        ShowcaseCompanionBuildOptions effectiveOptions = options ?? ShowcaseCompanionBuildOptions.Default;
        Document companionDocument = string.IsNullOrWhiteSpace(effectiveOptions.TemplateFilePath)
            ? application.NewProjectDocument(UnitSystem.Metric)
            : application.NewProjectDocument(effectiveOptions.TemplateFilePath);

        bool succeeded = false;
        try
        {
            ShowcaseCompanionBuildResult buildResult = BuildIntoDocument(
                companionDocument,
                snapshot,
                effectiveOptions,
                isFreshNewDocument: true);

            string? savedPath = null;
            if (!string.IsNullOrWhiteSpace(effectiveOptions.OutputFilePath))
            {
                string targetPath = Path.GetFullPath(effectiveOptions.OutputFilePath!.Trim());
                string? targetDir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                SaveAsOptions saveAsOptions = new()
                {
                    OverwriteExistingFile = effectiveOptions.OverwriteExistingFile,
                };
                companionDocument.SaveAs(targetPath, saveAsOptions);
                savedPath = targetPath;
            }

            Document? returnedDocument = companionDocument;
            if (savedPath != null && effectiveOptions.CloseDocumentAfterSave)
            {
                companionDocument.Close(saveModified: false);
                returnedDocument = null;
            }

            succeeded = true;
            return new ShowcaseCompanionBuildResult(
                returnedDocument,
                savedPath,
                buildResult.SnapshotScope,
                buildResult.SnapshotContentHash,
                buildResult.CreatedElementCount,
                buildResult.SkippedElementCount,
                buildResult.BoundParameterCount,
                buildResult.OverflowFeatureCount,
                buildResult.CreatedApplicationDataIds,
                buildResult.Warnings,
                buildResult.UpdatedElementCount,
                buildResult.ReplacedElementCount,
                buildResult.DeletedElementCount);
        }
        finally
        {
            if (!succeeded)
            {
                try
                {
                    companionDocument.Close(saveModified: false);
                }
                catch
                {
                    // Best-effort cleanup of failed scratch document.
                }
            }
        }
    }

    /// <summary>
    /// Populates or refreshes an existing Revit companion document ONLY if it is explicitly stamped with
    /// <c>gis_model_role = "showcase"</c>. Reconciles existing <c>Kanizu.Showcase</c> DirectShapes by
    /// <c>ApplicationDataId</c> (updating in-place when category matches to preserve Revit <c>UniqueId</c>s,
    /// replacing when category changed, clearing nulled parameters, and deleting stale features).
    /// Refuses to run on any authoring document (including legacy models with an unset <c>gis_model_role</c>).
    /// </summary>
    public ShowcaseCompanionBuildResult PopulateExistingCompanion(
        Document companionDocument,
        ShowcaseSnapshot snapshot,
        ShowcaseCompanionBuildOptions? options = null)
    {
        if (companionDocument is null)
        {
            throw new ArgumentNullException(nameof(companionDocument));
        }

        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return BuildIntoDocument(
            companionDocument,
            snapshot,
            options ?? ShowcaseCompanionBuildOptions.Default,
            isFreshNewDocument: false);
    }

    /// <summary>
    /// Derives the inverse <see cref="ShowcaseCoordinateTransform"/> from a Revit document's active
    /// <see cref="ProjectLocation"/> using the exact basis measurement as <c>SharedCoordinateProjector</c>.
    /// </summary>
    public static ShowcaseCoordinateTransform CreateTransformFromProjectLocation(ProjectLocation? projectLocation)
    {
        if (projectLocation is null)
        {
            return ShowcaseCoordinateTransform.Identity;
        }

        ProjectPosition origin = projectLocation.GetProjectPosition(XYZ.Zero);
        ProjectPosition xAxis = projectLocation.GetProjectPosition(XYZ.BasisX);
        ProjectPosition yAxis = projectLocation.GetProjectPosition(XYZ.BasisY);

        double feetToMeters = CrsTransformer.FeetToMetersFactor;
        Point2D originMeters = new(
            origin.EastWest * feetToMeters,
            origin.NorthSouth * feetToMeters);
        Point2D xBasisMeters = new(
            (xAxis.EastWest - origin.EastWest) * feetToMeters,
            (xAxis.NorthSouth - origin.NorthSouth) * feetToMeters);
        Point2D yBasisMeters = new(
            (yAxis.EastWest - origin.EastWest) * feetToMeters,
            (yAxis.NorthSouth - origin.NorthSouth) * feetToMeters);
        double originElevationMeters = origin.Elevation * feetToMeters;

        AffineTransform2D forward = new(originMeters, xBasisMeters, yBasisMeters);
        return ShowcaseCoordinateTransform.FromForwardAffine(forward, originElevationMeters);
    }

    private ShowcaseCompanionBuildResult BuildIntoDocument(
        Document document,
        ShowcaseSnapshot snapshot,
        ShowcaseCompanionBuildOptions options,
        bool isFreshNewDocument)
    {
        if (document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Forma Companion showcase builder requires a Revit project document, not a family document.");
        }

        if (document.IsReadOnly)
        {
            throw new InvalidOperationException($"Target companion document '{document.Title}' is read-only.");
        }

        string existingModelRole = GisStandardStateReader.ReadModelRoleText(document);
        ShowcaseCompanionDocumentGuard.ValidateTargetRole(existingModelRole, isFreshNewDocument, document.Title);

        ShowcaseCoordinateTransform documentLocationTransform = CreateTransformFromProjectLocation(document.ActiveProjectLocation);
        ShowcaseCoordinateTransform transform = ShowcaseCoordinateTransform.ResolveTransformForCompanion(
            snapshot,
            options.CoordinateTransformOverride,
            documentLocationTransform,
            isFreshNewDocument,
            out bool shouldConfigureDocumentProjectPosition);

        ShowcaseParameterRegistry registry = ShowcaseParameterRegistry.Build(snapshot);
        ShowcaseGeometryPlanOptions geometryOptions = CloneGeometryOptionsWithDocumentTolerance(
            options.GeometryOptions,
            document.Application.ShortCurveTolerance);

        ShowcaseGeometryPlanResult plan = ShowcaseGeometryPlanner.Plan(snapshot, transform, registry, geometryOptions);

        List<string> warnings = new(plan.Warnings);
        List<string> activeAppDataIds = new();
        int createdCount = 0;
        int updatedCount = 0;
        int replacedCount = 0;
        int deletedCount = 0;
        int skippedCount = 0;
        int boundParamCount = 0;
        int overflowFeatureCount = 0;

        ExecuteInTransaction(document, () =>
        {
            if (shouldConfigureDocumentProjectPosition && document.ActiveProjectLocation != null)
            {
                ConfigureLocalizedProjectPosition(document, transform);
            }

            boundParamCount = BindCompanionSharedParameters(document, registry, warnings);
            document.Regenerate();

            StampShowcaseModelRole(document);

            double minSegmentFeet = Math.Max(document.Application.ShortCurveTolerance, geometryOptions.MinSegmentLengthFeet);
            SolidOptions solidOptions = new(ElementId.InvalidElementId, ElementId.InvalidElementId);

            // Reconcile existing Kanizu.Showcase DirectShapes by ApplicationDataId so refreshes update in-place,
            // replace when category changed, clear nulled parameters, and delete stale features.
            IReadOnlyList<ShowcaseExistingElementInfo> existingShowcase = CollectExistingShowcaseDirectShapes(document);
            Dictionary<string, ElementId> resolvedCategoryIdsByPref = new(StringComparer.Ordinal);
            string ResolveCategoryKey(ShowcaseElementPlan p)
            {
                if (!resolvedCategoryIdsByPref.TryGetValue(p.PreferredCategoryName, out ElementId? resolvedId))
                {
                    resolvedId = ResolveDirectShapeCategoryId(document, p.PreferredCategoryName, warnings);
                    resolvedCategoryIdsByPref[p.PreferredCategoryName] = resolvedId;
                }

                return FormatCategoryKey(resolvedId);
            }

            ShowcaseReconciliationPlan reconciliation = ShowcaseElementReconciler.BuildPlan(
                plan.Elements,
                existingShowcase,
                ResolveCategoryKey);

            List<ElementId> staleIdsToDelete = new();

            foreach (ShowcaseReconciliationStep step in reconciliation.Steps)
            {
                switch (step.Action)
                {
                    case ShowcaseReconciliationActionKind.Create:
                    {
                        ShowcaseElementPlan elementPlan = step.TargetPlan!;
                        ElementId categoryId = resolvedCategoryIdsByPref[elementPlan.PreferredCategoryName];
                        if (!TryCreateDirectShape(document, elementPlan, categoryId, registry, minSegmentFeet, solidOptions, warnings))
                        {
                            skippedCount++;
                            continue;
                        }

                        createdCount++;
                        activeAppDataIds.Add(elementPlan.ApplicationDataId);
                        if (!string.IsNullOrEmpty(elementPlan.OverflowAttributesJson))
                        {
                            overflowFeatureCount++;
                        }

                        break;
                    }

                    case ShowcaseReconciliationActionKind.UpdateInPlace:
                    {
                        ShowcaseElementPlan elementPlan = step.TargetPlan!;
                        ElementId existingId = new(step.ExistingElementId!.Value);
                        if (!TryUpdateDirectShapeInPlace(document, existingId, elementPlan, registry, minSegmentFeet, solidOptions, warnings))
                        {
                            skippedCount++;
                            continue;
                        }

                        updatedCount++;
                        activeAppDataIds.Add(elementPlan.ApplicationDataId);
                        if (!string.IsNullOrEmpty(elementPlan.OverflowAttributesJson))
                        {
                            overflowFeatureCount++;
                        }

                        break;
                    }

                    case ShowcaseReconciliationActionKind.ReplaceCategoryChanged:
                    {
                        ShowcaseElementPlan elementPlan = step.TargetPlan!;
                        ElementId existingId = new(step.ExistingElementId!.Value);
                        ElementId categoryId = resolvedCategoryIdsByPref[elementPlan.PreferredCategoryName];
                        if (!TryReplaceDirectShape(document, existingId, elementPlan, categoryId, registry, minSegmentFeet, solidOptions, warnings))
                        {
                            skippedCount++;
                            continue;
                        }

                        replacedCount++;
                        activeAppDataIds.Add(elementPlan.ApplicationDataId);
                        if (!string.IsNullOrEmpty(elementPlan.OverflowAttributesJson))
                        {
                            overflowFeatureCount++;
                        }

                        break;
                    }

                    case ShowcaseReconciliationActionKind.DeleteStale:
                    {
                        if (step.ExistingElementId.HasValue)
                        {
                            staleIdsToDelete.Add(new ElementId(step.ExistingElementId.Value));
                        }

                        break;
                    }
                }
            }

            if (staleIdsToDelete.Count > 0)
            {
                try
                {
                    ICollection<ElementId> deleted = document.Delete(staleIdsToDelete);
                    deletedCount += staleIdsToDelete.Count;
                    _ = deleted;
                }
                catch (Exception ex)
                {
                    warnings.Add($"Failed to delete {staleIdsToDelete.Count} stale showcase DirectShape(s): {ex.Message}");
                }
            }
        });

        return new ShowcaseCompanionBuildResult(
            document,
            savedFilePath: null,
            snapshot.Scope,
            snapshot.ContentHash,
            createdCount,
            skippedCount,
            boundParamCount,
            overflowFeatureCount,
            activeAppDataIds,
            warnings,
            updatedCount,
            replacedCount,
            deletedCount);
    }

    private static void ConfigureLocalizedProjectPosition(Document document, ShowcaseCoordinateTransform transform)
    {
        ProjectPosition projectPosition = new(
            transform.OriginEastingMeters * ShowcaseCoordinateTransform.MetersToFeetFactor,
            transform.OriginNorthingMeters * ShowcaseCoordinateTransform.MetersToFeetFactor,
            transform.OriginElevationMeters * ShowcaseCoordinateTransform.MetersToFeetFactor,
            transform.RotationRadians);
        document.ActiveProjectLocation.SetProjectPosition(XYZ.Zero, projectPosition);
    }

    private static IReadOnlyList<ShowcaseExistingElementInfo> CollectExistingShowcaseDirectShapes(Document document)
    {
        List<ShowcaseExistingElementInfo> existing = new();
        foreach (DirectShape ds in new FilteredElementCollector(document).OfClass(typeof(DirectShape)).Cast<DirectShape>())
        {
            if (!ShowcaseExportGuard.IsShowcaseApplicationId(ds.ApplicationId))
            {
                continue;
            }

            string categoryKey = ds.Category != null ? FormatCategoryKey(ds.Category.Id) : string.Empty;
            existing.Add(new ShowcaseExistingElementInfo(ds.Id.Value, ds.ApplicationDataId ?? string.Empty, categoryKey));
        }

        return existing;
    }

    private static string FormatCategoryKey(ElementId categoryId) =>
        categoryId.Value.ToString(CultureInfo.InvariantCulture);

    private static ShowcaseGeometryPlanOptions CloneGeometryOptionsWithDocumentTolerance(
        ShowcaseGeometryPlanOptions? source,
        double shortCurveToleranceFeet)
    {
        ShowcaseGeometryPlanOptions baseOpts = source ?? ShowcaseGeometryPlanOptions.Default;
        double safeMinFeet = Math.Max(baseOpts.MinSegmentLengthFeet, Math.Max(shortCurveToleranceFeet * 1.1d, 0.003d));
        return new ShowcaseGeometryPlanOptions
        {
            UnitExtrusionHeightMeters = baseOpts.UnitExtrusionHeightMeters,
            OpeningExtrusionHeightMeters = baseOpts.OpeningExtrusionHeightMeters,
            OpeningBaseOffsetMeters = baseOpts.OpeningBaseOffsetMeters,
            OpeningLineThicknessMeters = baseOpts.OpeningLineThicknessMeters,
            FixtureExtrusionHeightMeters = baseOpts.FixtureExtrusionHeightMeters,
            FixtureBaseOffsetMeters = baseOpts.FixtureBaseOffsetMeters,
            FixtureLineThicknessMeters = baseOpts.FixtureLineThicknessMeters,
            FixturePointSizeMeters = baseOpts.FixturePointSizeMeters,
            LevelBandWidthMeters = baseOpts.LevelBandWidthMeters,
            LevelBandHeightMeters = baseOpts.LevelBandHeightMeters,
            DefaultFloorHeightMeters = baseOpts.DefaultFloorHeightMeters,
            MinSegmentLengthFeet = safeMinFeet,
        };
    }

    /// <summary>
    /// Binds <c>gis_model_role</c> (frozen GUID) onto Project Information and all deterministic UUIDv5 <c>ks_*</c>
    /// parameters onto companion DirectShape categories, writing the temporary shared-parameter file as UTF-16 LE
    /// with BOM (Revit's native encoding so Japanese <c>aliasJa</c> descriptions never mojibake) and unconditionally
    /// restoring <see cref="Application.SharedParametersFilename"/> in a <c>finally</c> block.
    /// </summary>
    private static int BindCompanionSharedParameters(
        Document document,
        ShowcaseParameterRegistry registry,
        ICollection<string> warnings)
    {
        Application application = document.Application;
        string sharedParamText = registry.ToSharedParameterFileText(includeGisModelRoleParameter: true);
        string tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"KanizuShowcase.SharedParameters.{Guid.NewGuid():N}.txt");

        // Write UTF-16 LE with BOM (Encoding.Unicode) — Revit's native shared-parameter file encoding on Windows.
        File.WriteAllText(tempFilePath, sharedParamText, Encoding.Unicode);

        string originalSharedParametersFilename = application.SharedParametersFilename ?? string.Empty;
        int boundCount = 0;

        try
        {
            application.SharedParametersFilename = tempFilePath;
            DefinitionFile? defFile = application.OpenSharedParameterFile();
            if (defFile is null)
            {
                throw new InvalidOperationException($"Revit could not open the generated showcase shared parameter file at '{tempFilePath}'.");
            }

            // 1. Bind frozen gis_model_role onto OST_ProjectInformation.
            DefinitionGroup? gisGroup = defFile.Groups.get_Item(GisSharedParameters.GroupName);
            ExternalDefinition? roleDefinition = gisGroup?.Definitions.get_Item(GisSharedParameters.GisModelRole) as ExternalDefinition;
            if (roleDefinition is null || roleDefinition.GUID != ShowcaseExportGuard.GisModelRoleParameterGuid)
            {
                throw new InvalidOperationException(
                    $"Generated shared parameter file is missing frozen '{GisSharedParameters.GisModelRole}' ({ShowcaseExportGuard.GisModelRoleParameterGuid:D}).");
            }

            CategorySet projectInfoCategories = application.Create.NewCategorySet();
            Category? projectInfoCategory = Category.GetCategory(document, BuiltInCategory.OST_ProjectInformation);
            if (projectInfoCategory != null && projectInfoCategory.AllowsBoundParameters)
            {
                projectInfoCategories.Insert(projectInfoCategory);
                if (BindInstanceDefinition(document, application, roleDefinition, projectInfoCategories, GroupTypeId.General))
                {
                    boundCount++;
                }
                else
                {
                    warnings.Add($"Revit could not bind '{GisSharedParameters.GisModelRole}' onto Project Information.");
                }
            }

            // 2. Bind all deterministic UUIDv5 ks_* parameters (+ ks_attributes_json) onto DirectShape target categories.
            DefinitionGroup? showcaseGroup = defFile.Groups.get_Item(ShowcaseParameterRegistry.ParameterGroupName);
            if (showcaseGroup is null)
            {
                throw new InvalidOperationException(
                    $"Generated shared parameter file is missing group '{ShowcaseParameterRegistry.ParameterGroupName}'.");
            }

            CategorySet directShapeCategories = BuildCompanionDirectShapeCategorySet(document, application);
            if (directShapeCategories.Size == 0)
            {
                warnings.Add("No bindable DirectShape categories were found for ks_* showcase parameters.");
                return boundCount;
            }

            foreach (ShowcaseParameterDefinition paramDef in registry.AllParameters)
            {
                ExternalDefinition? externalDefinition = showcaseGroup.Definitions.get_Item(paramDef.Name) as ExternalDefinition;
                if (externalDefinition is null || externalDefinition.GUID != paramDef.Guid)
                {
                    warnings.Add($"Generated shared parameter file did not yield expected UUIDv5 definition '{paramDef.Name}' ({paramDef.Guid:D}).");
                    continue;
                }

                if (BindInstanceDefinition(document, application, externalDefinition, directShapeCategories, GroupTypeId.Data))
                {
                    boundCount++;
                }
                else
                {
                    warnings.Add($"Revit refused to bind showcase shared parameter '{paramDef.Name}' ({paramDef.Guid:D}).");
                }
            }
        }
        finally
        {
            application.SharedParametersFilename = originalSharedParametersFilename;
            TryDeleteTempFile(tempFilePath);
        }

        return boundCount;
    }

    private static CategorySet BuildCompanionDirectShapeCategorySet(Document document, Application application)
    {
        CategorySet categories = application.Create.NewCategorySet();
        foreach (BuiltInCategory bic in CompanionDirectShapeCategories)
        {
            Category? category = Category.GetCategory(document, bic);
            if (category != null && category.AllowsBoundParameters)
            {
                categories.Insert(category);
            }
        }

        return categories;
    }

    private static bool BindInstanceDefinition(
        Document document,
        Application application,
        ExternalDefinition definition,
        CategorySet categories,
        ForgeTypeId groupTypeId)
    {
        InstanceBinding binding = application.Create.NewInstanceBinding(categories);
        BindingMap map = document.ParameterBindings;
        return map.Insert(definition, binding, groupTypeId) ||
               map.ReInsert(definition, binding, groupTypeId);
    }

    private static void StampShowcaseModelRole(Document document)
    {
        Autodesk.Revit.DB.ProjectInfo? projectInfo = document.ProjectInformation;
        if (projectInfo is null)
        {
            throw new InvalidOperationException("Target companion document has no ProjectInformation element.");
        }

        Parameter? roleParam = projectInfo.get_Parameter(ShowcaseExportGuard.GisModelRoleParameterGuid);
        if (roleParam is null)
        {
            throw new InvalidOperationException(
                $"Parameter '{GisSharedParameters.GisModelRole}' ({ShowcaseExportGuard.GisModelRoleParameterGuid:D}) is not bound to Project Information.");
        }

        if (!roleParam.Set(ShowcaseExportGuard.ShowcaseModelRole))
        {
            throw new InvalidOperationException(
                $"Failed to stamp '{GisSharedParameters.GisModelRole}' = '{ShowcaseExportGuard.ShowcaseModelRole}' on Project Information.");
        }
    }

    private static bool TryBuildGeometryObjects(
        ShowcaseElementPlan plan,
        double minSegmentLengthFeet,
        SolidOptions solidOptions,
        ICollection<string> warnings,
        out List<GeometryObject> geometries)
    {
        geometries = new List<GeometryObject>(plan.Solids.Count);
        foreach (ShowcaseExtrusionSolid solidPlan in plan.Solids)
        {
            try
            {
                Solid? solid = BuildExtrusionSolid(solidPlan, minSegmentLengthFeet, solidOptions);
                if (solid != null && solid.Volume > 1e-9d)
                {
                    geometries.Add(solid);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped one solid of '{plan.ApplicationDataId}': {ex.Message}");
            }
        }

        if (geometries.Count == 0)
        {
            warnings.Add($"Skipped '{plan.ApplicationDataId}' because Revit could not create a valid extrusion solid.");
            return false;
        }

        return true;
    }

    private static bool TryCreateDirectShape(
        Document document,
        ShowcaseElementPlan plan,
        ElementId categoryId,
        ShowcaseParameterRegistry registry,
        double minSegmentLengthFeet,
        SolidOptions solidOptions,
        ICollection<string> warnings)
    {
        if (!TryBuildGeometryObjects(plan, minSegmentLengthFeet, solidOptions, warnings, out List<GeometryObject> geometries))
        {
            return false;
        }

        using SubTransaction subTransaction = new(document);
        subTransaction.Start();
        try
        {
            DirectShape directShape = DirectShape.CreateElement(document, categoryId);
            directShape.ApplicationId = plan.ApplicationId;
            directShape.ApplicationDataId = plan.ApplicationDataId;
            directShape.Name = plan.ElementName;
            directShape.SetShape(geometries);

            PopulateElementParameters(directShape, plan, registry, clearOmittedOrNullParameters: false, warnings);
            subTransaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            if (subTransaction.GetStatus() == TransactionStatus.Started)
            {
                subTransaction.RollBack();
            }

            warnings.Add($"Failed to create DirectShape '{plan.ApplicationDataId}': {ex.Message}");
            return false;
        }
    }

    private static bool TryUpdateDirectShapeInPlace(
        Document document,
        ElementId existingElementId,
        ShowcaseElementPlan plan,
        ShowcaseParameterRegistry registry,
        double minSegmentLengthFeet,
        SolidOptions solidOptions,
        ICollection<string> warnings)
    {
        if (!TryBuildGeometryObjects(plan, minSegmentLengthFeet, solidOptions, warnings, out List<GeometryObject> geometries))
        {
            return false;
        }

        using SubTransaction subTransaction = new(document);
        subTransaction.Start();
        try
        {
            if (document.GetElement(existingElementId) is not DirectShape directShape)
            {
                subTransaction.RollBack();
                warnings.Add($"Existing showcase DirectShape '{plan.ApplicationDataId}' (#{existingElementId.Value}) was not found for in-place update.");
                return false;
            }

            directShape.ApplicationId = plan.ApplicationId;
            directShape.ApplicationDataId = plan.ApplicationDataId;
            directShape.Name = plan.ElementName;
            directShape.SetShape(geometries);

            PopulateElementParameters(directShape, plan, registry, clearOmittedOrNullParameters: true, warnings);
            subTransaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            if (subTransaction.GetStatus() == TransactionStatus.Started)
            {
                subTransaction.RollBack();
            }

            warnings.Add($"Failed to update DirectShape '{plan.ApplicationDataId}' in-place: {ex.Message}");
            return false;
        }
    }

    private static bool TryReplaceDirectShape(
        Document document,
        ElementId existingElementId,
        ShowcaseElementPlan plan,
        ElementId newCategoryId,
        ShowcaseParameterRegistry registry,
        double minSegmentLengthFeet,
        SolidOptions solidOptions,
        ICollection<string> warnings)
    {
        if (!TryBuildGeometryObjects(plan, minSegmentLengthFeet, solidOptions, warnings, out List<GeometryObject> geometries))
        {
            return false;
        }

        using SubTransaction subTransaction = new(document);
        subTransaction.Start();
        try
        {
            DirectShape directShape = DirectShape.CreateElement(document, newCategoryId);
            directShape.ApplicationId = plan.ApplicationId;
            directShape.ApplicationDataId = plan.ApplicationDataId;
            directShape.Name = plan.ElementName;
            directShape.SetShape(geometries);

            PopulateElementParameters(directShape, plan, registry, clearOmittedOrNullParameters: false, warnings);
            document.Delete(existingElementId);
            subTransaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            if (subTransaction.GetStatus() == TransactionStatus.Started)
            {
                subTransaction.RollBack();
            }

            warnings.Add($"Failed to replace DirectShape '{plan.ApplicationDataId}' across category change: {ex.Message}");
            return false;
        }
    }

    private static ElementId ResolveDirectShapeCategoryId(
        Document document,
        string preferredCategoryName,
        ICollection<string> warnings)
    {
        if (Enum.TryParse(preferredCategoryName, ignoreCase: false, out BuiltInCategory preferred))
        {
            ElementId preferredId = new(preferred);
            if (DirectShape.IsValidCategoryId(preferredId, document))
            {
                return preferredId;
            }

            warnings.Add(
                $"Category '{preferredCategoryName}' is not valid for DirectShape in this document; falling back to OST_GenericModel.");
        }

        return new ElementId(BuiltInCategory.OST_GenericModel);
    }

    private static Solid? BuildExtrusionSolid(
        ShowcaseExtrusionSolid solidPlan,
        double minSegmentLengthFeet,
        SolidOptions solidOptions)
    {
        if (solidPlan.HeightFeet <= minSegmentLengthFeet)
        {
            return null;
        }

        CurveLoop? outerLoop = TryBuildCurveLoop(solidPlan.OuterRingFeet, solidPlan.BaseElevationFeet, minSegmentLengthFeet);
        if (outerLoop is null)
        {
            return null;
        }

        List<CurveLoop> loops = new(1 + solidPlan.InteriorRingsFeet.Count) { outerLoop };
        foreach (IReadOnlyList<Point2D> holeRing in solidPlan.InteriorRingsFeet)
        {
            CurveLoop? holeLoop = TryBuildCurveLoop(holeRing, solidPlan.BaseElevationFeet, minSegmentLengthFeet);
            if (holeLoop != null)
            {
                loops.Add(holeLoop);
            }
        }

        return GeometryCreationUtilities.CreateExtrusionGeometry(loops, XYZ.BasisZ, solidPlan.HeightFeet, solidOptions);
    }

    private static CurveLoop? TryBuildCurveLoop(
        IReadOnlyList<Point2D> openRingFeet,
        double elevationFeet,
        double minSegmentLengthFeet)
    {
        if (openRingFeet.Count < 3)
        {
            return null;
        }

        List<XYZ> vertices = new(openRingFeet.Count);
        for (int i = 0; i < openRingFeet.Count; i++)
        {
            XYZ pt = new(openRingFeet[i].X, openRingFeet[i].Y, elevationFeet);
            if (vertices.Count == 0 || vertices[vertices.Count - 1].DistanceTo(pt) >= minSegmentLengthFeet)
            {
                vertices.Add(pt);
            }
        }

        while (vertices.Count >= 2 && vertices[0].DistanceTo(vertices[vertices.Count - 1]) < minSegmentLengthFeet)
        {
            vertices.RemoveAt(vertices.Count - 1);
        }

        if (vertices.Count < 3)
        {
            return null;
        }

        CurveLoop loop = new();
        for (int i = 0; i < vertices.Count; i++)
        {
            XYZ start = vertices[i];
            XYZ end = vertices[(i + 1) % vertices.Count];
            if (start.DistanceTo(end) < minSegmentLengthFeet)
            {
                return null;
            }

            loop.Append(Line.CreateBound(start, end));
        }

        return loop;
    }

    private static void PopulateElementParameters(
        DirectShape directShape,
        ShowcaseElementPlan plan,
        ShowcaseParameterRegistry registry,
        bool clearOmittedOrNullParameters,
        ICollection<string> warnings)
    {
        foreach (ShowcaseParameterAssignment assignment in plan.ParameterAssignments)
        {
            // Leave explicit SQL/JSON NULL values unset on fresh shapes (in-place updates clear stale values below).
            if (assignment.IsNull)
            {
                continue;
            }

            Parameter? param = directShape.get_Parameter(assignment.Parameter.Guid);
            if (param is null || param.IsReadOnly)
            {
                continue;
            }

            try
            {
                switch (param.StorageType)
                {
                    case StorageType.String:
                        if (!param.Set(assignment.TextValue ?? string.Empty))
                        {
                            warnings.Add($"Revit refused to set text parameter '{assignment.Parameter.Name}' on '{plan.ApplicationDataId}'.");
                        }

                        break;

                    case StorageType.Integer:
                        if (assignment.IsTextFallback)
                        {
                            warnings.Add(
                                $"Skipped incompatible text value '{assignment.TextValue}' for integer parameter '{assignment.Parameter.Name}' on '{plan.ApplicationDataId}'.");
                            break;
                        }

                        if (!param.Set(assignment.Int32Value))
                        {
                            warnings.Add($"Revit refused to set integer parameter '{assignment.Parameter.Name}' on '{plan.ApplicationDataId}'.");
                        }

                        break;

                    case StorageType.Double:
                        if (assignment.IsTextFallback)
                        {
                            warnings.Add(
                                $"Skipped incompatible text value '{assignment.TextValue}' for numeric parameter '{assignment.Parameter.Name}' on '{plan.ApplicationDataId}'.");
                            break;
                        }

                        if (!param.Set(assignment.DoubleValue))
                        {
                            warnings.Add($"Revit refused to set numeric parameter '{assignment.Parameter.Name}' on '{plan.ApplicationDataId}'.");
                        }

                        break;
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Could not set parameter '{assignment.Parameter.Name}' on '{plan.ApplicationDataId}': {ex.Message}");
            }
        }

        if (clearOmittedOrNullParameters)
        {
            IReadOnlyList<ShowcaseParameterDefinition> toClear =
                ShowcaseElementReconciler.GetParametersToClearOnUpdate(registry, plan);
            foreach (ShowcaseParameterDefinition def in toClear)
            {
                ClearParameterIfPopulated(directShape, def, plan.ApplicationDataId, warnings);
            }
        }

        if (!string.IsNullOrEmpty(plan.OverflowAttributesJson))
        {
            Parameter? overflowParam = directShape.get_Parameter(registry.OverflowParameter.Guid);
            if (overflowParam is null || overflowParam.IsReadOnly || overflowParam.StorageType != StorageType.String)
            {
                warnings.Add(
                    $"Overflow parameter '{registry.OverflowParameter.Name}' is missing, read-only, or non-string on '{plan.ApplicationDataId}'.");
            }
            else
            {
                try
                {
                    if (!overflowParam.Set(plan.OverflowAttributesJson))
                    {
                        warnings.Add(
                            $"Revit refused to write overflow parameter '{registry.OverflowParameter.Name}' on '{plan.ApplicationDataId}'.");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add(
                        $"Could not set overflow parameter '{registry.OverflowParameter.Name}' on '{plan.ApplicationDataId}': {ex.Message}");
                }
            }
        }
    }

    private static void ClearParameterIfPopulated(
        DirectShape directShape,
        ShowcaseParameterDefinition def,
        string applicationDataId,
        ICollection<string> warnings)
    {
        Parameter? param = directShape.get_Parameter(def.Guid);
        if (param is null || param.IsReadOnly || !param.HasValue)
        {
            return;
        }

        try
        {
            switch (param.StorageType)
            {
                case StorageType.String:
                    if (!string.IsNullOrEmpty(param.AsString()))
                    {
                        param.Set(string.Empty);
                    }

                    break;

                case StorageType.Integer:
                    if (param.AsInteger() != 0)
                    {
                        param.Set(0);
                    }

                    break;

                case StorageType.Double:
                    if (param.AsDouble() != 0d)
                    {
                        param.Set(0d);
                    }

                    break;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not clear stale parameter '{def.Name}' on '{applicationDataId}': {ex.Message}");
        }
    }

    private static void ExecuteInTransaction(Document document, Action work)
    {
        if (document.IsModifiable)
        {
            work();
            return;
        }

        using Transaction transaction = new(document, BuildTransactionName);
        transaction.Start();
        try
        {
            work();
            transaction.Commit();
        }
        catch
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            throw;
        }
    }

    private static void TryDeleteTempFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Ignore temp file cleanup errors.
        }
    }
}
