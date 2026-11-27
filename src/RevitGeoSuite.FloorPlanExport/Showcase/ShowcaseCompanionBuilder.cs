using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
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
                    Compact = true,
                };
                companionDocument.SaveAs(targetPath, saveAsOptions);
                savedPath = targetPath;
            }

            ShowcaseBudgetReport? finalBudgetReport = buildResult.BudgetReport;
            List<string> finalWarnings = new(buildResult.Warnings);
            if (savedPath != null && File.Exists(savedPath) && finalBudgetReport != null)
            {
                long fileSizeBytes = new FileInfo(savedPath).Length;
                ShowcaseBudgetReport postSaveBudget = EvaluatePostSaveFileSizeBudget(
                    finalBudgetReport,
                    fileSizeBytes,
                    effectiveOptions.BudgetLimits);
                ShowcaseBudgetEnforcementMode enforcementMode = effectiveOptions.ResolveEffectiveBudgetEnforcementMode();
                if (enforcementMode == ShowcaseBudgetEnforcementMode.FailOnHardLimit && !postSaveBudget.Passed)
                {
                    throw new ShowcaseBudgetExceededException(postSaveBudget);
                }

                if (enforcementMode != ShowcaseBudgetEnforcementMode.ReportOnly)
                {
                    ShowcaseBudgetMetricResult? sizeMetric = postSaveBudget.Metrics
                        .FirstOrDefault(m => string.Equals(m.MetricId, ShowcaseBudgetEvaluator.OutputFileSizeMetricId, StringComparison.Ordinal));
                    if (sizeMetric != null && sizeMetric.Severity != ShowcaseBudgetSeverity.Ok)
                    {
                        finalWarnings.Add($"[Budget:{sizeMetric.Severity}] {sizeMetric.MessageEn}");
                    }
                }

                finalBudgetReport = postSaveBudget;
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
                finalWarnings,
                buildResult.UpdatedElementCount,
                buildResult.ReplacedElementCount,
                buildResult.DeletedElementCount,
                buildResult.PoiMarkerCount,
                buildResult.AttachmentMarkerCount,
                buildResult.NetworkLinkCount,
                buildResult.VerticalTubeCount,
                buildResult.FindingPuckCount,
                buildResult.EstimatedTriangleCount,
                buildResult.UnchangedElementCount,
                buildResult.RefreshSummary,
                buildResult.ViewSetPlan,
                buildResult.ConfiguredViewNames,
                buildResult.DeletedStaleViewCount,
                buildResult.PublishViewSetConfigured,
                finalBudgetReport);
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
        ShowcasePoiAndNetworkPlanOptions poiNetworkOptions = ClonePoiAndNetworkOptionsWithDocumentTolerance(
            options.PoiAndNetworkOptions,
            document.Application.ShortCurveTolerance);

        ShowcaseGeometryPlanResult f3Plan = ShowcaseGeometryPlanner.Plan(snapshot, transform, registry, geometryOptions);
        ShowcasePoiAndNetworkPlanResult f4Plan = ShowcasePoiAndNetworkPlanner.Plan(snapshot, transform, registry, poiNetworkOptions);

        List<ShowcaseElementPlan> allPlannedElements = new(f3Plan.Elements.Count + f4Plan.Elements.Count);
        allPlannedElements.AddRange(f3Plan.Elements);
        allPlannedElements.AddRange(f4Plan.Elements);

        // Assert global ApplicationDataId uniqueness across F3 + F4 plans before mutating Revit.
        ShowcaseElementReconciler.ValidateUniqueApplicationDataIds(allPlannedElements);

        List<string> warnings = new(f3Plan.Warnings.Count + f4Plan.Warnings.Count);
        warnings.AddRange(f3Plan.Warnings);
        warnings.AddRange(f4Plan.Warnings);

        // Evaluate Forma triangle / element / marker / parameter budgets BEFORE opening a Revit transaction
        // so FailOnHardLimit aborts cleanly without leaving partial mutations.
        ShowcaseBudgetReport preBuildBudget = ShowcaseBudgetEvaluator.Evaluate(
            allPlannedElements,
            registry,
            outputFileSizeBytes: null,
            options.BudgetLimits);
        ShowcaseBudgetEvaluator.ApplyEnforcement(
            preBuildBudget,
            options.ResolveEffectiveBudgetEnforcementMode(),
            warnings);

        FormaPublishViewSetPlan? viewSetPlan = options.ConfigureFormaPublishViews
            ? ShowcaseViewPlanner.Plan(snapshot, transform, allPlannedElements, options.ViewOptions)
            : null;
        if (viewSetPlan != null && viewSetPlan.Warnings.Count > 0)
        {
            warnings.AddRange(viewSetPlan.Warnings);
        }

        List<string> activeAppDataIds = new();
        List<string> preservedUniqueIds = new();
        List<ShowcaseReplacedUniqueIdEntry> replacedUniqueIds = new();
        int createdCount = 0;
        int unchangedCount = 0;
        int updatedCount = 0;
        int replacedCategoryChangedCount = 0;
        int replacedParametersClearedCount = 0;
        int replacedRepresentationChangedCount = 0;
        int deletedCount = 0;
        int skippedCount = 0;
        int boundParamCount = 0;
        int overflowFeatureCount = 0;
        int totalEstimatedTriangles = allPlannedElements.Sum(e => e.EstimatedTriangleCount);
        string? priorSnapshotHash = null;
        ShowcaseViewBuildResult? viewBuildResult = null;

        ExecuteInTransaction(document, () =>
        {
            if (shouldConfigureDocumentProjectPosition && document.ActiveProjectLocation != null)
            {
                ConfigureLocalizedProjectPosition(document, transform);
            }

            priorSnapshotHash = ReadSnapshotHash(document);

            boundParamCount = BindCompanionSharedParameters(document, registry, warnings);
            document.Regenerate();

            StampShowcaseModelRole(document);

            Dictionary<string, ElementId> materialIdsByName = EnsureShowcaseMaterials(document, allPlannedElements);
            Dictionary<string, FamilySymbol> poiMarkerSymbols = ShowcasePoiMarkerFamilyBuilder.TryEnsurePoiMarkerSymbols(
                document,
                options,
                warnings);

            double f3MinSegmentFeet = Math.Max(document.Application.ShortCurveTolerance, geometryOptions.MinSegmentLengthFeet);
            double f4MinSegmentFeet = Math.Max(document.Application.ShortCurveTolerance, poiNetworkOptions.MinSegmentLengthFeet);
            SolidOptions defaultSolidOptions = new(ElementId.InvalidElementId, ElementId.InvalidElementId);

            // Pre-index existing DirectShapeType elements in the document so session-transient DirectShapeLibrary
            // can re-register them on reopen/refresh without creating duplicate DirectShapeType elements.
            Dictionary<(string Name, long CategoryId), DirectShapeType> existingDirectShapeTypes =
                CollectExistingDirectShapeTypes(document);
            IReadOnlyList<Level> documentLevels = new FilteredElementCollector(document)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.ProjectElevation)
                .ToArray();

            // Reconcile existing Kanizu.Showcase DirectShapes & KNZ_FRM_PoiMarker FamilyInstances by ApplicationDataId
            // so unchanged elements are skipped (zero writes), edits update in-place preserving Revit UniqueIds &
            // Forma comments, category/parameter/representation changes recreate cleanly, and stale features are deleted.
            IReadOnlyList<ShowcaseExistingElementInfo> existingShowcase = CollectExistingShowcaseElements(document, registry);
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

            ShowcaseElementRepresentationKind ResolveRepresentationKind(ShowcaseElementPlan p) =>
                TryResolvePoiFamilySymbol(p, poiMarkerSymbols, out _)
                    ? ShowcaseElementRepresentationKind.FamilyInstance
                    : ShowcaseElementRepresentationKind.DirectShape;

            ShowcaseReconciliationPlan reconciliation = ShowcaseElementReconciler.BuildPlan(
                allPlannedElements,
                existingShowcase,
                ResolveCategoryKey,
                ResolveRepresentationKind);

            Dictionary<string, string> prototypeDefIdsByKey = new(StringComparer.Ordinal);
            DirectShapeLibrary directShapeLibrary = DirectShapeLibrary.GetDirectShapeLibrary(document);
            List<ElementId> staleIdsToDelete = new();

            foreach (ShowcaseReconciliationStep step in reconciliation.Steps)
            {
                switch (step.Action)
                {
                    case ShowcaseReconciliationActionKind.SkipUnchanged:
                    {
                        ShowcaseElementPlan elementPlan = step.TargetPlan!;
                        unchangedCount++;
                        activeAppDataIds.Add(elementPlan.ApplicationDataId);
                        if (!string.IsNullOrEmpty(step.ExistingUniqueId))
                        {
                            preservedUniqueIds.Add(step.ExistingUniqueId!);
                        }

                        if (!string.IsNullOrEmpty(elementPlan.OverflowAttributesJson))
                        {
                            overflowFeatureCount++;
                        }

                        break;
                    }

                    case ShowcaseReconciliationActionKind.Create:
                    {
                        ShowcaseElementPlan elementPlan = step.TargetPlan!;
                        ElementId categoryId = resolvedCategoryIdsByPref[elementPlan.PreferredCategoryName];
                        double elementMinSegmentFeet = elementPlan.ElementKind == ShowcaseElementKind.Extrusion
                            ? f3MinSegmentFeet
                            : f4MinSegmentFeet;
                        if (!TryCreateShowcaseElement(
                                document,
                                elementPlan,
                                categoryId,
                                registry,
                                elementMinSegmentFeet,
                                defaultSolidOptions,
                                materialIdsByName,
                                poiMarkerSymbols,
                                documentLevels,
                                directShapeLibrary,
                                existingDirectShapeTypes,
                                prototypeDefIdsByKey,
                                warnings,
                                out _))
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
                        ElementId categoryId = resolvedCategoryIdsByPref[elementPlan.PreferredCategoryName];
                        double elementMinSegmentFeet = elementPlan.ElementKind == ShowcaseElementKind.Extrusion
                            ? f3MinSegmentFeet
                            : f4MinSegmentFeet;
                        if (!TryUpdateShowcaseElementInPlace(
                                document,
                                existingId,
                                elementPlan,
                                categoryId,
                                registry,
                                elementMinSegmentFeet,
                                defaultSolidOptions,
                                materialIdsByName,
                                poiMarkerSymbols,
                                documentLevels,
                                directShapeLibrary,
                                existingDirectShapeTypes,
                                prototypeDefIdsByKey,
                                warnings,
                                out bool wasReplaced,
                                out string? resultingUniqueId))
                        {
                            skippedCount++;
                            continue;
                        }

                        if (wasReplaced)
                        {
                            replacedRepresentationChangedCount++;
                            replacedUniqueIds.Add(new ShowcaseReplacedUniqueIdEntry(
                                elementPlan.ApplicationDataId,
                                step.ExistingUniqueId,
                                resultingUniqueId,
                                ShowcaseReconciliationActionKind.ReplaceRepresentationChanged));
                        }
                        else
                        {
                            updatedCount++;
                            string? preservedUid = resultingUniqueId ?? step.ExistingUniqueId;
                            if (!string.IsNullOrEmpty(preservedUid))
                            {
                                preservedUniqueIds.Add(preservedUid!);
                            }
                        }

                        activeAppDataIds.Add(elementPlan.ApplicationDataId);
                        if (!string.IsNullOrEmpty(elementPlan.OverflowAttributesJson))
                        {
                            overflowFeatureCount++;
                        }

                        break;
                    }

                    case ShowcaseReconciliationActionKind.ReplaceCategoryChanged:
                    case ShowcaseReconciliationActionKind.ReplaceParametersCleared:
                    case ShowcaseReconciliationActionKind.ReplaceRepresentationChanged:
                    {
                        ShowcaseElementPlan elementPlan = step.TargetPlan!;
                        ElementId existingId = new(step.ExistingElementId!.Value);
                        ElementId categoryId = resolvedCategoryIdsByPref[elementPlan.PreferredCategoryName];
                        double elementMinSegmentFeet = elementPlan.ElementKind == ShowcaseElementKind.Extrusion
                            ? f3MinSegmentFeet
                            : f4MinSegmentFeet;
                        if (!TryReplaceShowcaseElement(
                                document,
                                existingId,
                                elementPlan,
                                categoryId,
                                registry,
                                elementMinSegmentFeet,
                                defaultSolidOptions,
                                materialIdsByName,
                                poiMarkerSymbols,
                                documentLevels,
                                directShapeLibrary,
                                existingDirectShapeTypes,
                                prototypeDefIdsByKey,
                                warnings,
                                out string? newUniqueId))
                        {
                            skippedCount++;
                            continue;
                        }

                        if (step.Action == ShowcaseReconciliationActionKind.ReplaceCategoryChanged)
                        {
                            replacedCategoryChangedCount++;
                        }
                        else if (step.Action == ShowcaseReconciliationActionKind.ReplaceParametersCleared)
                        {
                            replacedParametersClearedCount++;
                        }
                        else
                        {
                            replacedRepresentationChangedCount++;
                        }

                        replacedUniqueIds.Add(new ShowcaseReplacedUniqueIdEntry(
                            elementPlan.ApplicationDataId,
                            step.ExistingUniqueId,
                            newUniqueId,
                            step.Action));

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
                    warnings.Add($"Failed to delete {staleIdsToDelete.Count} stale showcase element(s): {ex.Message}");
                }
            }

            StampSnapshotHash(document, snapshot.ContentHash, warnings);

            if (viewSetPlan != null)
            {
                viewBuildResult = ShowcaseViewBuilder.ApplyPublishViews(document, viewSetPlan, warnings);
            }
        });

        int totalReplacedCount =
            replacedCategoryChangedCount +
            replacedParametersClearedCount +
            replacedRepresentationChangedCount;

        ShowcaseRefreshSummary refreshSummary = new(
            priorSnapshotHash,
            snapshot.ContentHash,
            createdCount,
            updatedCount,
            unchangedCount,
            replacedCategoryChangedCount,
            replacedParametersClearedCount,
            replacedRepresentationChangedCount,
            deletedCount,
            skippedCount,
            preservedUniqueIds,
            replacedUniqueIds);

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
            totalReplacedCount,
            deletedCount,
            f4Plan.PoiMarkerCount,
            f4Plan.AttachmentMarkerCount,
            f4Plan.HorizontalLinkCount,
            f4Plan.VerticalTubeCount,
            f4Plan.FindingPuckCount,
            totalEstimatedTriangles,
            unchangedCount,
            refreshSummary,
            viewSetPlan,
            viewBuildResult?.ConfiguredViewNames ?? Array.Empty<string>(),
            viewBuildResult?.DeletedStaleViewCount ?? 0,
            viewBuildResult?.PublishViewSetConfigured ?? false,
            preBuildBudget);
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

    private static ShowcaseBudgetReport EvaluatePostSaveFileSizeBudget(
        ShowcaseBudgetReport preSaveReport,
        long fileSizeBytes,
        ShowcaseBudgetLimits? limits) =>
        ShowcaseBudgetEvaluator.WithOutputFileSize(preSaveReport, fileSizeBytes, limits);

    private static string? ReadSnapshotHash(Document document)
    {
        Parameter? param = document.ProjectInformation?.get_Parameter(ShowcaseParameterRegistry.SnapshotHashParameterGuid);
        if (param is null || !param.HasValue || param.StorageType != StorageType.String)
        {
            return null;
        }

        string? value = param.AsString();
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private static void StampSnapshotHash(Document document, string snapshotContentHash, ICollection<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(snapshotContentHash))
        {
            return;
        }

        Parameter? param = document.ProjectInformation?.get_Parameter(ShowcaseParameterRegistry.SnapshotHashParameterGuid);
        if (param is null || param.IsReadOnly || param.StorageType != StorageType.String)
        {
            warnings.Add(
                $"Parameter '{ShowcaseParameterRegistry.SnapshotHashParameterName}' is not bound on Project Information; snapshot ledger hash was not persisted.");
            return;
        }

        if (!param.Set(snapshotContentHash))
        {
            warnings.Add($"Revit refused to write '{ShowcaseParameterRegistry.SnapshotHashParameterName}' on Project Information.");
        }
    }

    private static string? ReadElementSourceHash(Element element)
    {
        Parameter? param = element.get_Parameter(ShowcaseParameterRegistry.SourceHashParameterGuid);
        if (param is null || !param.HasValue || param.StorageType != StorageType.String)
        {
            return null;
        }

        string? value = param.AsString();
        return string.IsNullOrWhiteSpace(value) ? null : value!.Trim();
    }

    private static IReadOnlyList<ShowcaseExistingElementInfo> CollectExistingShowcaseElements(
        Document document,
        ShowcaseParameterRegistry registry)
    {
        List<ShowcaseExistingElementInfo> existing = new();
        foreach (DirectShape ds in new FilteredElementCollector(document).OfClass(typeof(DirectShape)).Cast<DirectShape>())
        {
            if (!ShowcaseExportGuard.IsShowcaseApplicationId(ds.ApplicationId))
            {
                continue;
            }

            string categoryKey = ds.Category != null ? FormatCategoryKey(ds.Category.Id) : string.Empty;
            List<Guid> populatedKsGuids = CollectPopulatedShowcaseParameterGuids(ds);
            string? sourceHash = ReadElementSourceHash(ds);
            existing.Add(new ShowcaseExistingElementInfo(
                ds.Id.Value,
                ds.ApplicationDataId ?? string.Empty,
                categoryKey,
                populatedKsGuids,
                ds.UniqueId,
                sourceHash,
                ShowcaseElementRepresentationKind.DirectShape));
        }

        Guid? layerGuid = registry.FindByName(ShowcaseParameterRegistry.LayerParameterName)?.Guid;
        Guid? keyGuid = registry.FindByName(ShowcaseParameterRegistry.KeyParameterName)?.Guid;
        foreach (FamilyInstance fi in new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
        {
            string? familyName = fi.Symbol?.FamilyName;
            if (!ShowcaseExportGuard.IsShowcaseFamilyOrTypeName(familyName))
            {
                continue;
            }

            string appDataId = ReconstructFamilyInstanceApplicationDataId(fi, layerGuid, keyGuid);
            string categoryKey = fi.Category != null ? FormatCategoryKey(fi.Category.Id) : string.Empty;
            List<Guid> populatedKsGuids = CollectPopulatedShowcaseParameterGuids(fi);
            string? sourceHash = ReadElementSourceHash(fi);
            existing.Add(new ShowcaseExistingElementInfo(
                fi.Id.Value,
                appDataId,
                categoryKey,
                populatedKsGuids,
                fi.UniqueId,
                sourceHash,
                ShowcaseElementRepresentationKind.FamilyInstance));
        }

        return existing;
    }

    private static string ReconstructFamilyInstanceApplicationDataId(FamilyInstance fi, Guid? layerGuid, Guid? keyGuid)
    {
        string? layer = layerGuid.HasValue ? fi.get_Parameter(layerGuid.Value)?.AsString() : null;
        string? key = keyGuid.HasValue ? fi.get_Parameter(keyGuid.Value)?.AsString() : null;
        if (!string.IsNullOrWhiteSpace(layer) && !string.IsNullOrWhiteSpace(key))
        {
            return ShowcaseGeometryPlanner.BuildApplicationDataId(layer!.Trim(), key!.Trim());
        }

        Parameter? comments = fi.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
        string? commentValue = comments?.AsString();
        return string.IsNullOrWhiteSpace(commentValue) ? string.Empty : commentValue!.Trim();
    }

    private static List<Guid> CollectPopulatedShowcaseParameterGuids(Element element)
    {
        List<Guid> guids = new();
        foreach (Parameter? param in element.Parameters)
        {
            if (param is null || !param.IsShared || !param.HasValue)
            {
                continue;
            }

            string? name = param.Definition?.Name;
            if (!ShowcaseExportGuard.IsShowcaseParameterName(name))
            {
                continue;
            }

            if (param.GUID == ShowcaseParameterRegistry.SourceHashParameterGuid ||
                param.GUID == ShowcaseParameterRegistry.SnapshotHashParameterGuid)
            {
                continue;
            }

            guids.Add(param.GUID);
        }

        return guids;
    }

    private static Dictionary<string, ElementId> EnsureShowcaseMaterials(
        Document document,
        IReadOnlyList<ShowcaseElementPlan> plannedElements)
    {
        HashSet<string> neededNames = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShowcaseElementPlan plan in plannedElements)
        {
            if (!string.IsNullOrWhiteSpace(plan.MaterialName))
            {
                neededNames.Add(plan.MaterialName!);
            }

            if (!string.IsNullOrWhiteSpace(plan.AccentMaterialName))
            {
                neededNames.Add(plan.AccentMaterialName!);
            }
        }

        Dictionary<string, Material> existing = new FilteredElementCollector(document)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .GroupBy(m => m.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        Dictionary<string, ElementId> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string name in neededNames)
        {
            if (!ShowcaseMaterialCatalog.TryGet(name, out ShowcaseMaterialSpec spec))
            {
                continue;
            }

            if (!existing.TryGetValue(spec.MaterialName, out Material? material))
            {
                ElementId createdId = Material.Create(document, spec.MaterialName);
                material = (Material)document.GetElement(createdId);
                existing[spec.MaterialName] = material;
            }

            material.Color = new Color(spec.Color.R, spec.Color.G, spec.Color.B);
            material.Transparency = spec.Color.TransparencyPercent;
            material.UseRenderAppearanceForShading = false;
            result[spec.MaterialName] = material.Id;
        }

        return result;
    }

    private static ShowcasePoiAndNetworkPlanOptions ClonePoiAndNetworkOptionsWithDocumentTolerance(
        ShowcasePoiAndNetworkPlanOptions? source,
        double shortCurveToleranceFeet)
    {
        ShowcasePoiAndNetworkPlanOptions baseOpts = source ?? ShowcasePoiAndNetworkPlanOptions.Default;
        double safeMinFeet = Math.Max(baseOpts.MinSegmentLengthFeet, Math.Max(shortCurveToleranceFeet * 1.1d, 0.003d));
        return new ShowcasePoiAndNetworkPlanOptions
        {
            PoiBaseElevationOffsetMeters = baseOpts.PoiBaseElevationOffsetMeters,
            PoiMarkerRadiusMeters = baseOpts.PoiMarkerRadiusMeters,
            PoiMarkerHeightMeters = baseOpts.PoiMarkerHeightMeters,
            AttachmentBaseElevationOffsetMeters = baseOpts.AttachmentBaseElevationOffsetMeters,
            AttachmentMarkerRadiusMeters = baseOpts.AttachmentMarkerRadiusMeters,
            AttachmentMarkerHeightMeters = baseOpts.AttachmentMarkerHeightMeters,
            AttachmentStemRadiusMeters = baseOpts.AttachmentStemRadiusMeters,
            NetworkRibbonElevationOffsetMeters = baseOpts.NetworkRibbonElevationOffsetMeters,
            NetworkRibbonWidthMeters = baseOpts.NetworkRibbonWidthMeters,
            NetworkRibbonThicknessMeters = baseOpts.NetworkRibbonThicknessMeters,
            ChevronSpacingMeters = baseOpts.ChevronSpacingMeters,
            MinChevronLinkLengthMeters = baseOpts.MinChevronLinkLengthMeters,
            ChevronLengthMeters = baseOpts.ChevronLengthMeters,
            ChevronHalfWidthMeters = baseOpts.ChevronHalfWidthMeters,
            ChevronHeightMeters = baseOpts.ChevronHeightMeters,
            ChevronElevationGapMeters = baseOpts.ChevronElevationGapMeters,
            MaxChevronsPerLink = baseOpts.MaxChevronsPerLink,
            VerticalTubeRadiusMeters = baseOpts.VerticalTubeRadiusMeters,
            SchematicVerticalTubeRadiusMeters = baseOpts.SchematicVerticalTubeRadiusMeters,
            VerticalTubeSides = baseOpts.VerticalTubeSides,
            VerticalTransitionMinDeltaMeters = baseOpts.VerticalTransitionMinDeltaMeters,
            NetworkNodeElevationOffsetMeters = baseOpts.NetworkNodeElevationOffsetMeters,
            NetworkNodeRadiusMeters = baseOpts.NetworkNodeRadiusMeters,
            NetworkNodeHeightMeters = baseOpts.NetworkNodeHeightMeters,
            FindingPuckElevationOffsetMeters = baseOpts.FindingPuckElevationOffsetMeters,
            FindingPuckRadiusMeters = baseOpts.FindingPuckRadiusMeters,
            FindingPuckHeightMeters = baseOpts.FindingPuckHeightMeters,
            DefaultFloorHeightMeters = baseOpts.DefaultFloorHeightMeters,
            MinSegmentLengthFeet = safeMinFeet,
            MinNetworkSegmentMeters = baseOpts.MinNetworkSegmentMeters,
        };
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
        string sharedParamText = registry.ToSharedParameterFileText(
            includeGisModelRoleParameter: true,
            includeCompanionTrackingParameters: true);
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

            // 2. Bind all deterministic UUIDv5 ks_* parameters (+ ks_attributes_json + ks_source_hash) onto DirectShape/marker categories,
            //    and ks_snapshot_hash onto Project Information for the companion refresh ledger.
            DefinitionGroup? showcaseGroup = defFile.Groups.get_Item(ShowcaseParameterRegistry.ParameterGroupName);
            if (showcaseGroup is null)
            {
                throw new InvalidOperationException(
                    $"Generated shared parameter file is missing group '{ShowcaseParameterRegistry.ParameterGroupName}'.");
            }

            if (projectInfoCategory != null && projectInfoCategory.AllowsBoundParameters)
            {
                ExternalDefinition? snapshotHashDef = showcaseGroup.Definitions.get_Item(ShowcaseParameterRegistry.SnapshotHashParameterName) as ExternalDefinition;
                if (snapshotHashDef != null && snapshotHashDef.GUID == ShowcaseParameterRegistry.SnapshotHashParameterGuid)
                {
                    if (BindInstanceDefinition(document, application, snapshotHashDef, projectInfoCategories, GroupTypeId.Data))
                    {
                        boundCount++;
                    }
                    else
                    {
                        warnings.Add($"Revit could not bind '{ShowcaseParameterRegistry.SnapshotHashParameterName}' onto Project Information.");
                    }
                }
            }

            CategorySet directShapeCategories = BuildCompanionDirectShapeCategorySet(document, application);
            if (directShapeCategories.Size == 0)
            {
                warnings.Add("No bindable DirectShape categories were found for ks_* showcase parameters.");
                return boundCount;
            }

            foreach (ShowcaseParameterDefinition paramDef in registry.AllParameters.Concat(new[] { ShowcaseParameterRegistry.CreateSourceHashDefinition() }))
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

    private static bool TryResolvePoiFamilySymbol(
        ShowcaseElementPlan plan,
        Dictionary<string, FamilySymbol> poiMarkerSymbols,
        out FamilySymbol symbol)
    {
        if (plan.ElementKind == ShowcaseElementKind.PoiMarker &&
            plan.MarkerPlacements.Count == 1 &&
            poiMarkerSymbols.Count > 0)
        {
            if (!string.IsNullOrWhiteSpace(plan.MaterialName) &&
                poiMarkerSymbols.TryGetValue(plan.MaterialName!, out FamilySymbol? exact))
            {
                symbol = exact;
                return true;
            }

            if (poiMarkerSymbols.TryGetValue(ShowcaseMaterialCatalog.PoiGeneral, out FamilySymbol? fallback))
            {
                symbol = fallback;
                return true;
            }

            symbol = poiMarkerSymbols.Values.First();
            return true;
        }

        symbol = null!;
        return false;
    }

    private static SolidOptions ResolveSolidOptions(
        string? materialName,
        Dictionary<string, ElementId> materialIdsByName,
        SolidOptions fallback)
    {
        if (!string.IsNullOrWhiteSpace(materialName) &&
            materialIdsByName.TryGetValue(materialName!, out ElementId? materialId) &&
            materialId != ElementId.InvalidElementId)
        {
            return new SolidOptions(materialId, ElementId.InvalidElementId);
        }

        return fallback;
    }

    private static Dictionary<(string Name, long CategoryId), DirectShapeType> CollectExistingDirectShapeTypes(Document document)
    {
        Dictionary<(string Name, long CategoryId), DirectShapeType> result = new();
        foreach (DirectShapeType dsType in new FilteredElementCollector(document).OfClass(typeof(DirectShapeType)).Cast<DirectShapeType>())
        {
            if (string.IsNullOrWhiteSpace(dsType.Name))
            {
                continue;
            }

            long catId = dsType.Category?.Id.Value ?? ElementId.InvalidElementId.Value;
            (string, long) key = (dsType.Name, catId);
            if (!result.ContainsKey(key))
            {
                result[key] = dsType;
            }
        }

        return result;
    }

    private static Level? ResolveNearestLevel(IReadOnlyList<Level> documentLevels, double targetZFeet)
    {
        if (documentLevels.Count == 0)
        {
            return null;
        }

        Level best = documentLevels[0];
        double bestDist = Math.Abs(best.ProjectElevation - targetZFeet);
        for (int i = 1; i < documentLevels.Count; i++)
        {
            double dist = Math.Abs(documentLevels[i].ProjectElevation - targetZFeet);
            if (dist < bestDist)
            {
                bestDist = dist;
                best = documentLevels[i];
            }
        }

        return best;
    }

    private static bool TryBuildGeometryObjects(
        Document document,
        ShowcaseElementPlan plan,
        ElementId categoryId,
        double minSegmentLengthFeet,
        SolidOptions defaultSolidOptions,
        Dictionary<string, ElementId> materialIdsByName,
        DirectShapeLibrary directShapeLibrary,
        Dictionary<(string Name, long CategoryId), DirectShapeType> existingDirectShapeTypes,
        Dictionary<string, string> prototypeDefIdsByKey,
        ICollection<string> warnings,
        out List<GeometryObject> geometries)
    {
        geometries = new List<GeometryObject>(
            plan.Solids.Count + plan.AccentSolids.Count + plan.MarkerPlacements.Count + plan.TubeSegments.Count);

        SolidOptions primaryOptions = ResolveSolidOptions(plan.MaterialName, materialIdsByName, defaultSolidOptions);
        SolidOptions accentOptions = ResolveSolidOptions(plan.AccentMaterialName ?? plan.MaterialName, materialIdsByName, primaryOptions);

        foreach (ShowcaseExtrusionSolid solidPlan in plan.Solids)
        {
            try
            {
                Solid? solid = BuildExtrusionSolid(solidPlan, minSegmentLengthFeet, primaryOptions);
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

        foreach (ShowcaseExtrusionSolid accentPlan in plan.AccentSolids)
        {
            try
            {
                Solid? solid = BuildExtrusionSolid(accentPlan, minSegmentLengthFeet, accentOptions);
                if (solid != null && solid.Volume > 1e-9d)
                {
                    geometries.Add(solid);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped one accent solid of '{plan.ApplicationDataId}': {ex.Message}");
            }
        }

        foreach (ShowcaseSweptTubeSegment tubePlan in plan.TubeSegments)
        {
            try
            {
                Solid? tube = BuildSweptTubeSolid(tubePlan, minSegmentLengthFeet, primaryOptions);
                if (tube != null && tube.Volume > 1e-9d)
                {
                    geometries.Add(tube);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped one 3D tube segment of '{plan.ApplicationDataId}': {ex.Message}");
            }
        }

        foreach (ShowcaseMarkerPlacement placement in plan.MarkerPlacements)
        {
            try
            {
                string? defId = EnsureInstancedMarkerPrototype(
                    document,
                    categoryId,
                    placement,
                    plan.MaterialName,
                    primaryOptions,
                    minSegmentLengthFeet,
                    directShapeLibrary,
                    existingDirectShapeTypes,
                    prototypeDefIdsByKey);
                if (!string.IsNullOrEmpty(defId))
                {
                    Transform placementTransform = Transform.CreateTranslation(
                        new XYZ(placement.LocationFeet.XFeet, placement.LocationFeet.YFeet, placement.LocationFeet.ZFeet));
                    if (Math.Abs(placement.RotationRadians) > 1e-6d)
                    {
                        placementTransform = placementTransform.Multiply(
                            Transform.CreateRotation(XYZ.BasisZ, placement.RotationRadians));
                    }

                    IList<GeometryObject> instanced = DirectShape.CreateGeometryInstance(document, defId, placementTransform);
                    foreach (GeometryObject obj in instanced)
                    {
                        geometries.Add(obj);
                    }
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped one instanced marker of '{plan.ApplicationDataId}': {ex.Message}");
            }
        }

        if (geometries.Count == 0)
        {
            warnings.Add($"Skipped '{plan.ApplicationDataId}' because Revit could not create any valid showcase geometry.");
            return false;
        }

        return true;
    }

    private static string? EnsureInstancedMarkerPrototype(
        Document document,
        ElementId categoryId,
        ShowcaseMarkerPlacement placement,
        string? materialName,
        SolidOptions solidOptions,
        double minSegmentLengthFeet,
        DirectShapeLibrary library,
        Dictionary<(string Name, long CategoryId), DirectShapeType> existingDirectShapeTypes,
        Dictionary<string, string> prototypeDefIdsByKey)
    {
        int radiusMm = (int)Math.Round(ShowcaseCoordinateTransform.FeetToMeters(placement.BaseRadiusFeet) * 1000d);
        int heightMm = (int)Math.Round(ShowcaseCoordinateTransform.FeetToMeters(placement.HeightFeet) * 1000d);
        string matToken = string.IsNullOrWhiteSpace(materialName) ? "Default" : materialName!.Trim();
        string defId = $"KNZ_FRM_Proto_{placement.ShapeKind}_{matToken}_{radiusMm}_{heightMm}";

        if (prototypeDefIdsByKey.TryGetValue(defId, out string? cached))
        {
            return cached;
        }

        if (library.ContainsType(defId))
        {
            prototypeDefIdsByKey[defId] = defId;
            return defId;
        }

        List<GeometryObject> prototypeSolids = BuildMarkerPrototypeSolids(placement, solidOptions, minSegmentLengthFeet);
        if (prototypeSolids.Count == 0)
        {
            return null;
        }

        // Re-register any persisted DirectShapeType from an earlier session instead of duplicating it.
        (string, long) lookupKey = (defId, categoryId.Value);
        if (existingDirectShapeTypes.TryGetValue(lookupKey, out DirectShapeType? existingType))
        {
            existingType.SetShape(prototypeSolids);
            library.AddDefinitionType(defId, existingType.Id);
            prototypeDefIdsByKey[defId] = defId;
            return defId;
        }

        DirectShapeType dsType = DirectShapeType.Create(document, defId, categoryId);
        dsType.SetShape(prototypeSolids);
        library.AddDefinitionType(defId, dsType.Id);
        existingDirectShapeTypes[lookupKey] = dsType;
        prototypeDefIdsByKey[defId] = defId;
        return defId;
    }

    /// <summary>
    /// Builds compact planar marker solids at local origin (0, 0, 0), strictly &lt;= 48 triangles per marker.
    /// </summary>
    private static List<GeometryObject> BuildMarkerPrototypeSolids(
        ShowcaseMarkerPlacement placement,
        SolidOptions solidOptions,
        double minSegmentLengthFeet)
    {
        List<GeometryObject> solids = new();
        double r = placement.BaseRadiusFeet;
        double h = placement.HeightFeet;
        if (r <= minSegmentLengthFeet || h <= minSegmentLengthFeet)
        {
            return solids;
        }

        switch (placement.ShapeKind)
        {
            case ShowcaseMarkerShapeKind.PoiPin:
            {
                // 6-sided pedestal (20 tris) + 4-sided lower frustum (12 tris) + 4-sided upper cap (12 tris) = 44 tris
                double pedH = Math.Max(minSegmentLengthFeet * 2d, h * 0.12d);
                double midZ = Math.Max(pedH + (minSegmentLengthFeet * 2d), h * 0.72d);
                TryAddRegularPrism(solids, r, sides: 6, zBaseFeet: 0d, heightFeet: pedH, angleOffset: 0d, solidOptions, minSegmentLengthFeet);
                TryAddRegularFrustum(solids, r * 0.28d, r * 0.90d, sides: 4, zBottomFeet: pedH, zTopFeet: midZ, angleOffset: Math.PI * 0.25d, solidOptions, minSegmentLengthFeet);
                TryAddRegularFrustum(solids, r * 0.90d, r * 0.22d, sides: 4, zBottomFeet: midZ, zTopFeet: h, angleOffset: Math.PI * 0.25d, solidOptions, minSegmentLengthFeet);
                break;
            }

            case ShowcaseMarkerShapeKind.FindingPuck:
            {
                // 8-sided alert puck (28 tris) + 4-sided raised alert spire (12 tris) = 40 tris
                double puckH = Math.Max(minSegmentLengthFeet * 2d, h * 0.25d);
                TryAddRegularPrism(solids, r, sides: 8, zBaseFeet: 0d, heightFeet: puckH, angleOffset: 0d, solidOptions, minSegmentLengthFeet);
                TryAddRegularPrism(solids, Math.Max(minSegmentLengthFeet * 2d, r * 0.24d), sides: 4, zBaseFeet: puckH, heightFeet: Math.Max(minSegmentLengthFeet * 2d, h - puckH), angleOffset: Math.PI * 0.25d, solidOptions, minSegmentLengthFeet);
                break;
            }

            case ShowcaseMarkerShapeKind.PoiAttachment:
            case ShowcaseMarkerShapeKind.NetworkNode:
            default:
            {
                // 6-sided compact disc/collar = 20 tris
                TryAddRegularPrism(solids, r, sides: 6, zBaseFeet: 0d, heightFeet: h, angleOffset: 0d, solidOptions, minSegmentLengthFeet);
                break;
            }
        }

        return solids;
    }

    private static void TryAddRegularPrism(
        List<GeometryObject> target,
        double radiusFeet,
        int sides,
        double zBaseFeet,
        double heightFeet,
        double angleOffset,
        SolidOptions solidOptions,
        double minSegmentLengthFeet)
    {
        if (radiusFeet <= minSegmentLengthFeet || heightFeet <= minSegmentLengthFeet)
        {
            return;
        }

        CurveLoop? loop = BuildRegularHorizontalCurveLoop(radiusFeet, sides, zBaseFeet, angleOffset, minSegmentLengthFeet);
        if (loop == null)
        {
            return;
        }

        Solid solid = GeometryCreationUtilities.CreateExtrusionGeometry(new[] { loop }, XYZ.BasisZ, heightFeet, solidOptions);
        if (solid != null && solid.Volume > 1e-9d)
        {
            target.Add(solid);
        }
    }

    private static void TryAddRegularFrustum(
        List<GeometryObject> target,
        double bottomRadiusFeet,
        double topRadiusFeet,
        int sides,
        double zBottomFeet,
        double zTopFeet,
        double angleOffset,
        SolidOptions solidOptions,
        double minSegmentLengthFeet)
    {
        if (bottomRadiusFeet <= minSegmentLengthFeet ||
            topRadiusFeet <= minSegmentLengthFeet ||
            (zTopFeet - zBottomFeet) <= minSegmentLengthFeet)
        {
            return;
        }

        CurveLoop? bottomLoop = BuildRegularHorizontalCurveLoop(bottomRadiusFeet, sides, zBottomFeet, angleOffset, minSegmentLengthFeet);
        CurveLoop? topLoop = BuildRegularHorizontalCurveLoop(topRadiusFeet, sides, zTopFeet, angleOffset, minSegmentLengthFeet);
        if (bottomLoop == null || topLoop == null)
        {
            return;
        }

        Solid solid = GeometryCreationUtilities.CreateBlendGeometry(bottomLoop, topLoop, vertexPairs: null, solidOptions);
        if (solid != null && solid.Volume > 1e-9d)
        {
            target.Add(solid);
        }
    }

    private static CurveLoop? BuildRegularHorizontalCurveLoop(
        double radiusFeet,
        int sides,
        double zFeet,
        double angleOffset,
        double minSegmentLengthFeet)
    {
        XYZ[] pts = new XYZ[sides];
        double step = (2d * Math.PI) / sides;
        for (int i = 0; i < sides; i++)
        {
            double angle = angleOffset + (i * step);
            pts[i] = new XYZ(radiusFeet * Math.Cos(angle), radiusFeet * Math.Sin(angle), zFeet);
        }

        CurveLoop loop = new();
        for (int i = 0; i < sides; i++)
        {
            XYZ a = pts[i];
            XYZ b = pts[(i + 1) % sides];
            if (a.DistanceTo(b) < minSegmentLengthFeet)
            {
                return null;
            }

            loop.Append(Line.CreateBound(a, b));
        }

        return loop;
    }

    private static Solid? BuildSweptTubeSolid(
        ShowcaseSweptTubeSegment tubePlan,
        double minSegmentLengthFeet,
        SolidOptions solidOptions)
    {
        XYZ start = new(tubePlan.StartFeet.XFeet, tubePlan.StartFeet.YFeet, tubePlan.StartFeet.ZFeet);
        XYZ end = new(tubePlan.EndFeet.XFeet, tubePlan.EndFeet.YFeet, tubePlan.EndFeet.ZFeet);
        XYZ delta = end - start;
        double lengthFeet = delta.GetLength();
        if (lengthFeet < minSegmentLengthFeet || tubePlan.RadiusFeet <= minSegmentLengthFeet)
        {
            return null;
        }

        XYZ dir = delta / lengthFeet;
        XYZ refAxis = Math.Abs(dir.Z) < 0.9d ? XYZ.BasisZ : XYZ.BasisX;
        XYZ u = refAxis.CrossProduct(dir).Normalize();
        XYZ v = dir.CrossProduct(u).Normalize();

        int sides = Math.Max(3, tubePlan.Sides);
        XYZ[] ring = new XYZ[sides];
        double step = (2d * Math.PI) / sides;
        for (int i = 0; i < sides; i++)
        {
            double angle = i * step;
            double cos = Math.Cos(angle) * tubePlan.RadiusFeet;
            double sin = Math.Sin(angle) * tubePlan.RadiusFeet;
            ring[i] = start + (u * cos) + (v * sin);
        }

        CurveLoop profile = new();
        for (int i = 0; i < sides; i++)
        {
            XYZ a = ring[i];
            XYZ b = ring[(i + 1) % sides];
            if (a.DistanceTo(b) < minSegmentLengthFeet)
            {
                return null;
            }

            profile.Append(Line.CreateBound(a, b));
        }

        return GeometryCreationUtilities.CreateExtrusionGeometry(new[] { profile }, dir, lengthFeet, solidOptions);
    }

    private static bool TryCreateShowcaseElement(
        Document document,
        ShowcaseElementPlan plan,
        ElementId categoryId,
        ShowcaseParameterRegistry registry,
        double minSegmentLengthFeet,
        SolidOptions defaultSolidOptions,
        Dictionary<string, ElementId> materialIdsByName,
        Dictionary<string, FamilySymbol> poiMarkerSymbols,
        IReadOnlyList<Level> documentLevels,
        DirectShapeLibrary directShapeLibrary,
        Dictionary<(string Name, long CategoryId), DirectShapeType> existingDirectShapeTypes,
        Dictionary<string, string> prototypeDefIdsByKey,
        ICollection<string> warnings,
        out string? createdUniqueId)
    {
        createdUniqueId = null;
        if (TryResolvePoiFamilySymbol(plan, poiMarkerSymbols, out FamilySymbol symbol))
        {
            return TryCreatePoiFamilyInstance(document, plan, symbol, documentLevels, registry, warnings, out createdUniqueId);
        }

        if (!TryBuildGeometryObjects(
                document,
                plan,
                categoryId,
                minSegmentLengthFeet,
                defaultSolidOptions,
                materialIdsByName,
                directShapeLibrary,
                existingDirectShapeTypes,
                prototypeDefIdsByKey,
                warnings,
                out List<GeometryObject> geometries))
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

            PopulateElementParameters(directShape, plan, registry, warnings);
            subTransaction.Commit();
            createdUniqueId = directShape.UniqueId;
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

    private static FamilyInstance PlacePoiFamilyInstanceAtExactElevation(
        Document document,
        FamilySymbol symbol,
        ShowcaseMarkerPlacement placement,
        IReadOnlyList<Level> documentLevels,
        string applicationDataId,
        ICollection<string> warnings)
    {
        if (!symbol.IsActive)
        {
            symbol.Activate();
        }

        XYZ targetLocation = new(placement.LocationFeet.XFeet, placement.LocationFeet.YFeet, placement.LocationFeet.ZFeet);
        Level? hostLevel = ResolveNearestLevel(documentLevels, targetLocation.Z);

        FamilyInstance instance = hostLevel != null
            ? document.Create.NewFamilyInstance(
                new XYZ(targetLocation.X, targetLocation.Y, hostLevel.ProjectElevation),
                symbol,
                hostLevel,
                StructuralType.NonStructural)
            : document.Create.NewFamilyInstance(targetLocation, symbol, StructuralType.NonStructural);

        ApplyFamilyInstanceElevationAndRotation(
            document,
            instance,
            targetLocation,
            hostLevel,
            placement.RotationRadians,
            applicationDataId,
            warnings);

        return instance;
    }

    /// <summary>
    /// Enforces exact target Z elevation and Z-axis rotation on a <c>KNZ_FRM_PoiMarker</c> <see cref="FamilyInstance"/>
    /// (including <c>OneLevelBased</c> families built from <c>Metric Generic Model.rft</c>) by writing host-level
    /// elevation offset parameters and verifying <see cref="LocationPoint.Point"/> Z afterward.
    /// </summary>
    private static void ApplyFamilyInstanceElevationAndRotation(
        Document document,
        FamilyInstance instance,
        XYZ targetLocation,
        Level? explicitHostLevel,
        double targetRotationRadians,
        string applicationDataId,
        ICollection<string> warnings)
    {
        Level? hostLevel = explicitHostLevel;
        if (hostLevel == null && instance.LevelId != null && instance.LevelId != ElementId.InvalidElementId)
        {
            hostLevel = document.GetElement(instance.LevelId) as Level;
        }

        double hostElevation = hostLevel?.ProjectElevation ?? 0d;
        double requiredOffset = targetLocation.Z - hostElevation;

        bool setElevationParam = TrySetWritableDoubleParameter(instance, BuiltInParameter.INSTANCE_ELEVATION_PARAM, requiredOffset);
        bool setFreeHostParam = TrySetWritableDoubleParameter(instance, BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, requiredOffset);

        if (instance.Location is LocationPoint locPt)
        {
            double currentZ = locPt.Point.Z;
            double zError = targetLocation.Z - currentZ;
            if (Math.Abs(zError) > 1e-4d && (!setElevationParam && !setFreeHostParam))
            {
                try
                {
                    ElementTransformUtils.MoveElement(document, instance.Id, new XYZ(0d, 0d, zError));
                }
                catch
                {
                    // Fall through to verification warning below.
                }
            }

            if (instance.Location is LocationPoint verifiedLoc)
            {
                double remainingError = Math.Abs(targetLocation.Z - verifiedLoc.Point.Z);
                if (remainingError > 1e-3d)
                {
                    // One final offset retry using observed delta if Revit shifted elevation relative to host.
                    if (TrySetWritableDoubleParameter(instance, BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM, requiredOffset) ||
                        TrySetWritableDoubleParameter(instance, BuiltInParameter.INSTANCE_ELEVATION_PARAM, requiredOffset))
                    {
                        remainingError = instance.Location is LocationPoint retryLoc
                            ? Math.Abs(targetLocation.Z - retryLoc.Point.Z)
                            : remainingError;
                    }

                    if (remainingError > 1e-3d)
                    {
                        warnings.Add(
                            $"KNZ_FRM_PoiMarker FamilyInstance '{applicationDataId}' Z elevation differed from target by {ShowcaseCoordinateTransform.FeetToMeters(remainingError):F3} m.");
                    }
                }

                double deltaRot = targetRotationRadians - verifiedLoc.Rotation;
                if (Math.Abs(deltaRot) > 1e-6d)
                {
                    XYZ axisOrigin = verifiedLoc.Point;
                    Line axis = Line.CreateBound(axisOrigin, axisOrigin + XYZ.BasisZ);
                    ElementTransformUtils.RotateElement(document, instance.Id, axis, deltaRot);
                }
            }
        }
    }

    private static bool TrySetWritableDoubleParameter(Element element, BuiltInParameter builtInParam, double value)
    {
        Parameter? param = element.get_Parameter(builtInParam);
        if (param is null || param.IsReadOnly || param.StorageType != StorageType.Double)
        {
            return false;
        }

        try
        {
            return param.Set(value);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryCreatePoiFamilyInstance(
        Document document,
        ShowcaseElementPlan plan,
        FamilySymbol symbol,
        IReadOnlyList<Level> documentLevels,
        ShowcaseParameterRegistry registry,
        ICollection<string> warnings,
        out string? createdUniqueId)
    {
        createdUniqueId = null;
        using SubTransaction subTransaction = new(document);
        subTransaction.Start();
        try
        {
            ShowcaseMarkerPlacement placement = plan.MarkerPlacements[0];
            FamilyInstance instance = PlacePoiFamilyInstanceAtExactElevation(
                document,
                symbol,
                placement,
                documentLevels,
                plan.ApplicationDataId,
                warnings);

            Parameter? comments = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (comments != null && !comments.IsReadOnly)
            {
                comments.Set(plan.ApplicationDataId);
            }

            PopulateElementParameters(instance, plan, registry, warnings);
            subTransaction.Commit();
            createdUniqueId = instance.UniqueId;
            return true;
        }
        catch (Exception ex)
        {
            if (subTransaction.GetStatus() == TransactionStatus.Started)
            {
                subTransaction.RollBack();
            }

            warnings.Add($"Failed to create KNZ_FRM_PoiMarker FamilyInstance '{plan.ApplicationDataId}': {ex.Message}");
            return false;
        }
    }

    private static bool TryUpdateShowcaseElementInPlace(
        Document document,
        ElementId existingElementId,
        ShowcaseElementPlan plan,
        ElementId categoryId,
        ShowcaseParameterRegistry registry,
        double minSegmentLengthFeet,
        SolidOptions defaultSolidOptions,
        Dictionary<string, ElementId> materialIdsByName,
        Dictionary<string, FamilySymbol> poiMarkerSymbols,
        IReadOnlyList<Level> documentLevels,
        DirectShapeLibrary directShapeLibrary,
        Dictionary<(string Name, long CategoryId), DirectShapeType> existingDirectShapeTypes,
        Dictionary<string, string> prototypeDefIdsByKey,
        ICollection<string> warnings,
        out bool wasReplaced,
        out string? resultingUniqueId)
    {
        wasReplaced = false;
        resultingUniqueId = null;
        Element? existingElement = document.GetElement(existingElementId);
        if (existingElement is null)
        {
            warnings.Add($"Existing showcase element '{plan.ApplicationDataId}' (#{existingElementId.Value}) was not found for in-place update.");
            return false;
        }

        bool wantFamilyInstance = TryResolvePoiFamilySymbol(plan, poiMarkerSymbols, out FamilySymbol targetSymbol);
        if (existingElement is FamilyInstance existingFamilyInstance && wantFamilyInstance)
        {
            using SubTransaction subTransaction = new(document);
            subTransaction.Start();
            try
            {
                if (!targetSymbol.IsActive)
                {
                    targetSymbol.Activate();
                }

                if (existingFamilyInstance.Symbol.Id != targetSymbol.Id)
                {
                    existingFamilyInstance.Symbol = targetSymbol;
                }

                ShowcaseMarkerPlacement placement = plan.MarkerPlacements[0];
                XYZ targetLoc = new(placement.LocationFeet.XFeet, placement.LocationFeet.YFeet, placement.LocationFeet.ZFeet);
                if (existingFamilyInstance.Location is LocationPoint locPt)
                {
                    XYZ xyDelta = new(targetLoc.X - locPt.Point.X, targetLoc.Y - locPt.Point.Y, 0d);
                    if (xyDelta.GetLength() > 1e-6d)
                    {
                        ElementTransformUtils.MoveElement(document, existingFamilyInstance.Id, xyDelta);
                    }
                }

                ApplyFamilyInstanceElevationAndRotation(
                    document,
                    existingFamilyInstance,
                    targetLoc,
                    explicitHostLevel: null,
                    placement.RotationRadians,
                    plan.ApplicationDataId,
                    warnings);

                Parameter? comments = existingFamilyInstance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comments != null && !comments.IsReadOnly)
                {
                    comments.Set(plan.ApplicationDataId);
                }

                PopulateElementParameters(existingFamilyInstance, plan, registry, warnings);
                subTransaction.Commit();
                resultingUniqueId = existingFamilyInstance.UniqueId;
                return true;
            }
            catch (Exception ex)
            {
                if (subTransaction.GetStatus() == TransactionStatus.Started)
                {
                    subTransaction.RollBack();
                }

                warnings.Add($"Failed to update KNZ_FRM_PoiMarker FamilyInstance '{plan.ApplicationDataId}' in-place: {ex.Message}");
                return false;
            }
        }

        if (existingElement is not DirectShape directShape || wantFamilyInstance)
        {
            // Representation switched between FamilyInstance and DirectShape -> recreate cleanly and report as replaced.
            wasReplaced = true;
            return TryReplaceShowcaseElement(
                document,
                existingElementId,
                plan,
                categoryId,
                registry,
                minSegmentLengthFeet,
                defaultSolidOptions,
                materialIdsByName,
                poiMarkerSymbols,
                documentLevels,
                directShapeLibrary,
                existingDirectShapeTypes,
                prototypeDefIdsByKey,
                warnings,
                out resultingUniqueId);
        }

        if (!TryBuildGeometryObjects(
                document,
                plan,
                categoryId,
                minSegmentLengthFeet,
                defaultSolidOptions,
                materialIdsByName,
                directShapeLibrary,
                existingDirectShapeTypes,
                prototypeDefIdsByKey,
                warnings,
                out List<GeometryObject> geometries))
        {
            return false;
        }

        using SubTransaction dsTx = new(document);
        dsTx.Start();
        try
        {
            directShape.ApplicationId = plan.ApplicationId;
            directShape.ApplicationDataId = plan.ApplicationDataId;
            directShape.Name = plan.ElementName;
            directShape.SetShape(geometries);

            PopulateElementParameters(directShape, plan, registry, warnings);
            dsTx.Commit();
            resultingUniqueId = directShape.UniqueId;
            return true;
        }
        catch (Exception ex)
        {
            if (dsTx.GetStatus() == TransactionStatus.Started)
            {
                dsTx.RollBack();
            }

            warnings.Add($"Failed to update DirectShape '{plan.ApplicationDataId}' in-place: {ex.Message}");
            return false;
        }
    }

    private static bool TryReplaceShowcaseElement(
        Document document,
        ElementId existingElementId,
        ShowcaseElementPlan plan,
        ElementId newCategoryId,
        ShowcaseParameterRegistry registry,
        double minSegmentLengthFeet,
        SolidOptions defaultSolidOptions,
        Dictionary<string, ElementId> materialIdsByName,
        Dictionary<string, FamilySymbol> poiMarkerSymbols,
        IReadOnlyList<Level> documentLevels,
        DirectShapeLibrary directShapeLibrary,
        Dictionary<(string Name, long CategoryId), DirectShapeType> existingDirectShapeTypes,
        Dictionary<string, string> prototypeDefIdsByKey,
        ICollection<string> warnings,
        out string? newUniqueId)
    {
        newUniqueId = null;
        if (TryResolvePoiFamilySymbol(plan, poiMarkerSymbols, out FamilySymbol symbol))
        {
            using SubTransaction famTx = new(document);
            famTx.Start();
            try
            {
                ShowcaseMarkerPlacement placement = plan.MarkerPlacements[0];
                FamilyInstance instance = PlacePoiFamilyInstanceAtExactElevation(
                    document,
                    symbol,
                    placement,
                    documentLevels,
                    plan.ApplicationDataId,
                    warnings);

                Parameter? comments = instance.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
                if (comments != null && !comments.IsReadOnly)
                {
                    comments.Set(plan.ApplicationDataId);
                }

                PopulateElementParameters(instance, plan, registry, warnings);
                document.Delete(existingElementId);
                famTx.Commit();
                newUniqueId = instance.UniqueId;
                return true;
            }
            catch (Exception ex)
            {
                if (famTx.GetStatus() == TransactionStatus.Started)
                {
                    famTx.RollBack();
                }

                warnings.Add($"Failed to replace showcase FamilyInstance '{plan.ApplicationDataId}': {ex.Message}");
                return false;
            }
        }

        if (!TryBuildGeometryObjects(
                document,
                plan,
                newCategoryId,
                minSegmentLengthFeet,
                defaultSolidOptions,
                materialIdsByName,
                directShapeLibrary,
                existingDirectShapeTypes,
                prototypeDefIdsByKey,
                warnings,
                out List<GeometryObject> geometries))
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

            PopulateElementParameters(directShape, plan, registry, warnings);
            document.Delete(existingElementId);
            subTransaction.Commit();
            newUniqueId = directShape.UniqueId;
            return true;
        }
        catch (Exception ex)
        {
            if (subTransaction.GetStatus() == TransactionStatus.Started)
            {
                subTransaction.RollBack();
            }

            warnings.Add($"Failed to replace DirectShape '{plan.ApplicationDataId}': {ex.Message}");
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
        Element element,
        ShowcaseElementPlan plan,
        ShowcaseParameterRegistry registry,
        ICollection<string> warnings)
    {
        foreach (ShowcaseParameterAssignment assignment in plan.ParameterAssignments)
        {
            // Leave explicit SQL/JSON NULL values unset (reconciliation recreates the element whenever
            // a previously populated ks_* parameter becomes null, omitted, removed from schema, or retyped).
            if (assignment.IsNull)
            {
                continue;
            }

            Parameter? param = element.get_Parameter(assignment.Parameter.Guid);
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

        if (!string.IsNullOrEmpty(plan.OverflowAttributesJson))
        {
            Parameter? overflowParam = element.get_Parameter(registry.OverflowParameter.Guid);
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
        Parameter? sourceHashParam = element.get_Parameter(ShowcaseParameterRegistry.SourceHashParameterGuid);
        if (sourceHashParam != null && !sourceHashParam.IsReadOnly && sourceHashParam.StorageType == StorageType.String)
        {
            try
            {
                sourceHashParam.Set(plan.ComputeContentDigest());
            }
            catch (Exception ex)
            {
                warnings.Add(
                    $"Could not write '{ShowcaseParameterRegistry.SourceHashParameterName}' on '{plan.ApplicationDataId}': {ex.Message}");
            }
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
