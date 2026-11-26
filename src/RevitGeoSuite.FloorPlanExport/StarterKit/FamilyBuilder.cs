using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>Configuration for <see cref="FamilyBuilder"/> runs.</summary>
public sealed class FamilyBuildOptions
{
    public FamilyBuildOptions(
        string seedTemplatesDirectory,
        string outputDirectory,
        string? sharedParametersFilePath = null,
        bool overwriteExisting = true,
        bool lintBeforeReturn = true,
        IReadOnlyDictionary<string, string>? customSeedPaths = null)
    {
        if (string.IsNullOrWhiteSpace(seedTemplatesDirectory))
        {
            throw new ArgumentException("Seed templates directory is required.", nameof(seedTemplatesDirectory));
        }

        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("Output directory is required.", nameof(outputDirectory));
        }

        SeedTemplatesDirectory = Path.GetFullPath(seedTemplatesDirectory);
        OutputDirectory = Path.GetFullPath(outputDirectory);
        SharedParametersFilePath = sharedParametersFilePath;
        OverwriteExisting = overwriteExisting;
        LintBeforeReturn = lintBeforeReturn;
        CustomSeedPaths = customSeedPaths ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Directory containing Revit metric family templates (<c>.rft</c>) or custom starter seeds.</summary>
    public string SeedTemplatesDirectory { get; }

    /// <summary>Target directory where built <c>.rfa</c> files will be saved.</summary>
    public string OutputDirectory { get; }

    /// <summary>Optional path to <c>KanizuGIS.SharedParameters.txt</c>; defaults to the embedded standard file.</summary>
    public string? SharedParametersFilePath { get; }

    public bool OverwriteExisting { get; }

    public bool LintBeforeReturn { get; }

    /// <summary>Optional map from family ID or category (e.g. <c>OST_Doors</c>) to an explicit <c>.rft</c> seed path.</summary>
    public IReadOnlyDictionary<string, string> CustomSeedPaths { get; }
}

/// <summary>Structured result of building, saving, inspecting, and linting one Starter Kit <c>.rfa</c>.</summary>
public sealed class FamilyBuildResult
{
    public FamilyBuildResult(
        string familyId,
        string familyName,
        string outputPath,
        long fileSizeBytes,
        FamilyGeometryPlan plan,
        RfaInspectionSnapshot snapshot,
        RfaLintReport lintReport,
        ManifestFamilyEntry manifestEntry,
        IReadOnlyList<string> log)
    {
        FamilyId = familyId ?? string.Empty;
        FamilyName = familyName ?? string.Empty;
        OutputPath = outputPath ?? string.Empty;
        FileSizeBytes = fileSizeBytes;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        LintReport = lintReport ?? throw new ArgumentNullException(nameof(lintReport));
        ManifestEntry = manifestEntry ?? throw new ArgumentNullException(nameof(manifestEntry));
        Log = log ?? Array.Empty<string>();
    }

    public string FamilyId { get; }

    public string FamilyName { get; }

    public string OutputPath { get; }

    public long FileSizeBytes { get; }

    public FamilyGeometryPlan Plan { get; }

    public RfaInspectionSnapshot Snapshot { get; }

    public RfaLintReport LintReport { get; }

    public ManifestFamilyEntry ManifestEntry { get; }

    public IReadOnlyList<string> Log { get; }

    public bool Succeeded => LintReport.Passed;
}

/// <summary>
/// Procedural Revit API family builder for the Kanizu Starter Kit.
/// Compiles a declarative <see cref="FamilyGeometrySpec"/> via <see cref="FamilyGeometryPlanBuilder"/>,
/// builds subcategories, 3D materials, dimensional parameters, Kanizu shared parameters (with guaranteed
/// <see cref="ScopedSharedParametersFile"/> restoration in <c>finally</c>), Coarse <c>K_Footprint</c> and
/// Medium/Fine solid/void extrusions, saves the <c>.rfa</c>, and gates every output with
/// <see cref="RevitRfaInspector"/> + <see cref="RfaLinter"/>.
/// </summary>
public static class FamilyBuilder
{
    private const string BuildTransactionName = "Kanizu Starter Kit: Build procedural family";

    /// <summary>Builds, saves, inspects, and lints a single Starter Kit <c>.rfa</c> family.</summary>
    public static FamilyBuildResult BuildFamily(
        Application application,
        FamilyGeometrySpec familySpec,
        StarterKitSpec kitSpec,
        FamilyBuildOptions options,
        StarterKitReferenceStandards? standards = null)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (familySpec is null)
        {
            throw new ArgumentNullException(nameof(familySpec));
        }

        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();
        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(familySpec, kitSpec, refStandards);

        string seedPath = ResolveSeedTemplatePath(familySpec, options);
        string sharedParamsPath = ScopedSharedParametersFile.EnsureShippedFilePath(options.SharedParametersFilePath);

        Directory.CreateDirectory(options.OutputDirectory);
        string outputPath = Path.Combine(options.OutputDirectory, familySpec.FamilyName + ".rfa");
        List<string> log = new()
        {
            $"Building {familySpec.FamilyName} ({familySpec.Category}, seedKind={familySpec.SeedKind}) from '{seedPath}'.",
        };

        Document? familyDoc = null;
        try
        {
            familyDoc = application.NewFamilyDocument(seedPath);
            if (familyDoc is null || !familyDoc.IsFamilyDocument)
            {
                throw new InvalidOperationException($"Failed to open family template '{seedPath}' for '{familySpec.FamilyName}'.");
            }

            // Guarantee Application.SharedParametersFilename is always restored even if anything throws.
            using (new ScopedSharedParametersFile(application, sharedParamsPath))
            {
                using Transaction tx = new(familyDoc, BuildTransactionName);
                tx.Start();
                try
                {
                    EnsureFamilyCategory(familyDoc, familySpec, log);
                    Dictionary<string, Category> subcategories = EnsureSubcategories(familyDoc, plan.Subcategories, log);
                    Dictionary<string, ElementId> materialIds = EnsureMaterials(familyDoc, plan.RequiredMaterials, log);
                    EnsureDefaultFamilyType(familyDoc, familySpec.FamilyName);
                    EnsureDimensionParameters(familyDoc, familySpec, plan.DimensionParameters, log);
                    BindSharedParameters(application, familyDoc, plan.SharedParameters, log);
                    RemoveSeedGenericForms(familyDoc, log);
                    SynchronizeWallHostedDoorSeed(familyDoc, plan, log);
                    CreateGeometryPrimitives(familyDoc, plan, subcategories, materialIds, log);

                    familyDoc.Regenerate();
                    tx.Commit();
                }
                catch
                {
                    if (tx.GetStatus() == TransactionStatus.Started)
                    {
                        tx.RollBack();
                    }

                    throw;
                }
            }

            SaveAsOptions saveOptions = new()
            {
                OverwriteExistingFile = options.OverwriteExisting,
                Compact = true,
            };
            familyDoc.SaveAs(outputPath, saveOptions);
            long fileSizeBytes = new FileInfo(outputPath).Length;
            log.Add($"Saved '{outputPath}' ({fileSizeBytes} bytes).");

            RfaInspectionSnapshot snapshot = RevitRfaInspector.Inspect(familyDoc, fileSizeBytes);
            RfaLintReport report = options.LintBeforeReturn
                ? RfaLinter.Lint(snapshot, familySpec, kitSpec, refStandards)
                : new RfaLintReport(
                    familySpec.Id,
                    snapshot.FamilyName,
                    Array.Empty<RfaLintRuleResult>(),
                    snapshot.CoarseTriangles,
                    snapshot.MediumFineTriangles,
                    plan.TriangleEstimate.CoarseTriangles,
                    plan.TriangleEstimate.MediumFineTriangles,
                    fileSizeBytes);
            ManifestFamilyEntry manifestEntry = snapshot.ToManifestFamilyEntry(familySpec);

            log.Add(report.SummaryEn);
            return new FamilyBuildResult(
                familySpec.Id,
                familySpec.FamilyName,
                outputPath,
                fileSizeBytes,
                plan,
                snapshot,
                report,
                manifestEntry,
                Array.AsReadOnly(log.ToArray()));
        }
        finally
        {
            if (familyDoc != null && familyDoc.IsValidObject)
            {
                try
                {
                    familyDoc.Close(false);
                }
                catch (Exception)
                {
                    // Best-effort cleanup of family document.
                }
            }
        }
    }

    /// <summary>Resolves the <c>.rft</c> seed template path for a Starter Kit family spec.</summary>
    public static string ResolveSeedTemplatePath(FamilyGeometrySpec familySpec, FamilyBuildOptions options)
    {
        if (options.CustomSeedPaths.TryGetValue(familySpec.Id, out string? byId) && !string.IsNullOrWhiteSpace(byId))
        {
            return VerifySeedExists(byId!, familySpec.Id);
        }

        if (options.CustomSeedPaths.TryGetValue(familySpec.Category, out string? byCategory) && !string.IsNullOrWhiteSpace(byCategory))
        {
            return VerifySeedExists(byCategory!, familySpec.Id);
        }

        string[] candidateNames = CandidateTemplateFileNames(familySpec);
        foreach (string fileName in candidateNames)
        {
            string candidate = Path.Combine(options.SeedTemplatesDirectory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not find a seed .rft template for '{familySpec.Id}' ({familySpec.Category}) in '{options.SeedTemplatesDirectory}'. Tried: {string.Join(", ", candidateNames)}.");
    }

    private static string[] CandidateTemplateFileNames(FamilyGeometrySpec familySpec)
    {
        if (string.Equals(familySpec.SeedKind, FamilyGeometrySpec.SeedWallHostedDoor, StringComparison.Ordinal) ||
            string.Equals(familySpec.Category, nameof(BuiltInCategory.OST_Doors), StringComparison.Ordinal))
        {
            return new[]
            {
                "Metric Door.rft",
                "Door.rft",
                "ドア(メートル単位).rft",
            };
        }

        if (string.Equals(familySpec.Category, nameof(BuiltInCategory.OST_SpecialityEquipment), StringComparison.Ordinal))
        {
            return new[]
            {
                "Metric Specialty Equipment.rft",
                "Specialty Equipment.rft",
                "Metric Generic Model.rft",
                "Generic Model.rft",
            };
        }

        if (string.Equals(familySpec.Category, nameof(BuiltInCategory.OST_MechanicalEquipment), StringComparison.Ordinal))
        {
            return new[]
            {
                "Metric Mechanical Equipment.rft",
                "Mechanical Equipment.rft",
                "Metric Generic Model.rft",
                "Generic Model.rft",
            };
        }

        if (string.Equals(familySpec.Category, nameof(BuiltInCategory.OST_Columns), StringComparison.Ordinal))
        {
            return new[]
            {
                "Metric Column.rft",
                "Column.rft",
                "Metric Generic Model.rft",
                "Generic Model.rft",
            };
        }

        return new[]
        {
            "Metric Generic Model.rft",
            "Generic Model.rft",
            "一般モデル(メートル単位).rft",
        };
    }

    private static string VerifySeedExists(string path, string familyId)
    {
        string full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Configured seed .rft for '{familyId}' was not found at '{full}'.", full);
        }

        return full;
    }

    private static void EnsureFamilyCategory(Document familyDoc, FamilyGeometrySpec familySpec, List<string> log)
    {
        if (!Enum.TryParse(familySpec.Category, ignoreCase: false, out BuiltInCategory targetCategory))
        {
            throw new InvalidOperationException($"Unknown BuiltInCategory '{familySpec.Category}' on family '{familySpec.Id}'.");
        }

        Category? current = familyDoc.OwnerFamily?.FamilyCategory;
        if (current != null && current.BuiltInCategory == targetCategory)
        {
            return;
        }

        Category? resolved = Category.GetCategory(familyDoc, targetCategory);
        if (resolved != null && familyDoc.OwnerFamily != null)
        {
            familyDoc.OwnerFamily.FamilyCategory = resolved;
            log.Add($"Assigned family category '{familySpec.Category}'.");
        }
    }

    private static Dictionary<string, Category> EnsureSubcategories(
        Document familyDoc,
        IReadOnlyList<string> subcategoryNames,
        List<string> log)
    {
        Category parentCategory = familyDoc.OwnerFamily?.FamilyCategory
            ?? throw new InvalidOperationException("Family document has no OwnerFamily.FamilyCategory.");

        Dictionary<string, Category> existing = new(StringComparer.Ordinal);
        if (parentCategory.SubCategories != null)
        {
            foreach (Category sub in parentCategory.SubCategories)
            {
                if (sub != null && !string.IsNullOrWhiteSpace(sub.Name) && !existing.ContainsKey(sub.Name))
                {
                    existing[sub.Name] = sub;
                }
            }
        }

        foreach (string name in subcategoryNames)
        {
            if (existing.ContainsKey(name))
            {
                continue;
            }

            Category created = familyDoc.Settings.Categories.NewSubcategory(parentCategory, name);
            existing[name] = created;
            log.Add($"Created subcategory '{name}'.");
        }

        return existing;
    }

    private static Dictionary<string, ElementId> EnsureMaterials(
        Document familyDoc,
        IReadOnlyList<Material3DSpec> requiredMaterials,
        List<string> log)
    {
        Dictionary<string, Material> existing = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .GroupBy(m => m.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        Dictionary<string, ElementId> ids = new(StringComparer.Ordinal);
        foreach (Material3DSpec matSpec in requiredMaterials)
        {
            if (!existing.TryGetValue(matSpec.Name, out Material? material))
            {
                ElementId newId = Material.Create(familyDoc, matSpec.Name);
                material = (Material)familyDoc.GetElement(newId);
                existing[matSpec.Name] = material;
                log.Add($"Created material '{matSpec.Name}' ({matSpec.Hex}).");
            }

            if (matSpec.Srgb.Count == 3)
            {
                material.Color = new Color((byte)matSpec.Srgb[0], (byte)matSpec.Srgb[1], (byte)matSpec.Srgb[2]);
            }

            material.Transparency = matSpec.Transparency;
            material.UseRenderAppearanceForShading = false;
            ids[matSpec.Name] = material.Id;
        }

        return ids;
    }

    private static void EnsureDefaultFamilyType(Document familyDoc, string defaultTypeName)
    {
        FamilyManager manager = familyDoc.FamilyManager;
        if (manager.CurrentType == null || string.IsNullOrWhiteSpace(manager.CurrentType.Name))
        {
            manager.CurrentType = manager.NewType(defaultTypeName);
        }
    }

    private static void EnsureDimensionParameters(
        Document familyDoc,
        FamilyGeometrySpec familySpec,
        IReadOnlyList<CompiledDimensionParameter> dimensionParameters,
        List<string> log)
    {
        FamilyManager manager = familyDoc.FamilyManager;
        Dictionary<string, FamilyParameter> existingByName = new(StringComparer.Ordinal);
        Dictionary<BuiltInParameter, FamilyParameter> existingByBuiltIn = new();
        foreach (FamilyParameter fp in manager.Parameters)
        {
            if (fp?.Definition == null)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(fp.Definition.Name) && !existingByName.ContainsKey(fp.Definition.Name))
            {
                existingByName[fp.Definition.Name] = fp;
            }

            if (fp.Definition is InternalDefinition internalDef &&
                internalDef.BuiltInParameter != BuiltInParameter.INVALID &&
                !existingByBuiltIn.ContainsKey(internalDef.BuiltInParameter))
            {
                existingByBuiltIn[internalDef.BuiltInParameter] = fp;
            }
        }

        bool isWallHostedDoor = string.Equals(
            familySpec.SeedKind,
            FamilyGeometrySpec.SeedWallHostedDoor,
            StringComparison.Ordinal);

        foreach (CompiledDimensionParameter dim in dimensionParameters)
        {
            double internalFeet = UnitUtils.ConvertToInternalUnits(dim.ValueMm, UnitTypeId.Millimeters);

            // For wall-hosted door seeds (including localized Metric Door.rft templates), synchronize the
            // built-in door Width/Height parameters first so the seed's wall-cut Opening resizes accurately.
            if (isWallHostedDoor)
            {
                if (string.Equals(dim.Name, "Width", StringComparison.Ordinal))
                {
                    TrySetBuiltInLengthParameter(manager, existingByBuiltIn, BuiltInParameter.DOOR_WIDTH, internalFeet);
                    TrySetBuiltInLengthParameter(manager, existingByBuiltIn, BuiltInParameter.FAMILY_WIDTH_PARAM, internalFeet);
                    TrySetBuiltInLengthParameter(manager, existingByBuiltIn, BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM, internalFeet);
                }
                else if (string.Equals(dim.Name, "Height", StringComparison.Ordinal))
                {
                    TrySetBuiltInLengthParameter(manager, existingByBuiltIn, BuiltInParameter.DOOR_HEIGHT, internalFeet);
                    TrySetBuiltInLengthParameter(manager, existingByBuiltIn, BuiltInParameter.FAMILY_HEIGHT_PARAM, internalFeet);
                    TrySetBuiltInLengthParameter(manager, existingByBuiltIn, BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM, internalFeet);
                }
            }

            if (!existingByName.TryGetValue(dim.Name, out FamilyParameter? fp))
            {
                fp = manager.AddParameter(dim.Name, GroupTypeId.Geometry, SpecTypeId.Length, dim.IsInstance);
                existingByName[dim.Name] = fp;
                log.Add($"Added dimension parameter '{dim.Name}' = {dim.ValueMm} mm.");
            }

            if (!fp.IsReadOnly && !fp.IsDeterminedByFormula && fp.StorageType == StorageType.Double)
            {
                manager.Set(fp, internalFeet);
            }
        }
    }

    private static void TrySetBuiltInLengthParameter(
        FamilyManager manager,
        Dictionary<BuiltInParameter, FamilyParameter> existingByBuiltIn,
        BuiltInParameter builtInParam,
        double valueFeet)
    {
        if (existingByBuiltIn.TryGetValue(builtInParam, out FamilyParameter? fp) &&
            fp != null &&
            !fp.IsReadOnly &&
            !fp.IsDeterminedByFormula &&
            fp.StorageType == StorageType.Double)
        {
            manager.Set(fp, valueFeet);
        }
    }

    private static void SynchronizeWallHostedDoorSeed(
        Document familyDoc,
        FamilyGeometryPlan plan,
        List<string> log)
    {
        if (!string.Equals(plan.Spec.SeedKind, FamilyGeometrySpec.SeedWallHostedDoor, StringComparison.Ordinal))
        {
            return;
        }

        int hostWallCount = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(Wall))
            .GetElementCount();
        int openingCutCount = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(Opening))
            .GetElementCount();
        int wallCuttingVoidCount = new FilteredElementCollector(familyDoc)
            .OfClass(typeof(GenericForm))
            .Cast<GenericForm>()
            .Count(f => !f.IsSolid && CutsAnyHostWall(familyDoc, f));

        if (hostWallCount == 0 || (openingCutCount == 0 && wallCuttingVoidCount == 0))
        {
            throw new InvalidOperationException(
                $"Wall-hosted door family '{plan.Spec.FamilyName}' requires a wall-hosted door seed (.rft) with a host Wall and wall-cut Opening (found {hostWallCount} Wall(s), {openingCutCount} Opening(s), {wallCuttingVoidCount} wall-cutting void(s)).");
        }

        // Regenerate after syncing Width/Height and stripping seed trim so the host wall-cut Opening
        // reflects defaultDimensionsMm before procedural door frames, leaves, and K_Footprint are created.
        familyDoc.Regenerate();
        log.Add(
            $"Synchronized wallHostedDoor seed ({hostWallCount} host Wall, {openingCutCount} Opening cut(s), {wallCuttingVoidCount} wall-cutting void(s)) to {plan.Spec.DefaultDimensionsMm.WidthMm} x {plan.Spec.DefaultDimensionsMm.HeightMm} mm.");
    }

    private static void BindSharedParameters(
        Application application,
        Document familyDoc,
        IReadOnlyList<CompiledSharedParameter> sharedParameters,
        List<string> log)
    {
        DefinitionFile definitionFile = application.OpenSharedParameterFile()
            ?? throw new InvalidOperationException(
                $"Unable to open Kanizu shared parameter file '{application.SharedParametersFilename}'.");
        DefinitionGroup group = definitionFile.Groups.get_Item(GisSharedParameters.GroupName)
            ?? throw new InvalidOperationException(
                $"Shared parameter group '{GisSharedParameters.GroupName}' not found in '{application.SharedParametersFilename}'.");

        FamilyManager manager = familyDoc.FamilyManager;
        Dictionary<string, FamilyParameter> existing = new(StringComparer.Ordinal);
        foreach (FamilyParameter fp in manager.Parameters)
        {
            if (fp?.Definition != null && !string.IsNullOrWhiteSpace(fp.Definition.Name) && !existing.ContainsKey(fp.Definition.Name))
            {
                existing[fp.Definition.Name] = fp;
            }
        }

        foreach (CompiledSharedParameter sp in sharedParameters)
        {
            ExternalDefinition extDef = group.Definitions.get_Item(sp.Name) as ExternalDefinition
                ?? throw new InvalidOperationException(
                    $"Shared parameter '{sp.Name}' (GUID {sp.Guid:D}) is missing from '{application.SharedParametersFilename}'.");

            if (extDef.GUID != sp.Guid)
            {
                throw new InvalidOperationException(
                    $"Shared parameter '{sp.Name}' GUID mismatch in file: found {extDef.GUID:D}, expected {sp.Guid:D}.");
            }

            if (!existing.TryGetValue(sp.Name, out FamilyParameter? fp))
            {
                fp = manager.AddParameter(extDef, GroupTypeId.General, sp.IsInstance);
                existing[sp.Name] = fp;
                log.Add($"Bound shared parameter '{sp.Name}' ({sp.Guid:D}, {(sp.IsInstance ? "instance" : "type")}).");
            }

            // Never bake identity values; only assign explicit non-empty defaults from spec.
            if (!string.IsNullOrWhiteSpace(sp.DefaultValue) && !fp.IsReadOnly)
            {
                if (fp.StorageType == StorageType.String)
                {
                    manager.Set(fp, sp.DefaultValue);
                }
                else if (fp.StorageType == StorageType.Integer &&
                         int.TryParse(sp.DefaultValue, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int intVal))
                {
                    manager.Set(fp, intVal);
                }
            }
        }
    }

    private static void RemoveSeedGenericForms(Document familyDoc, List<string> log)
    {
        List<ElementId> idsToDelete = new();
        foreach (GenericForm form in new FilteredElementCollector(familyDoc).OfClass(typeof(GenericForm)).Cast<GenericForm>())
        {
            // Preserve any void GenericForm that cuts a host Wall in wall-hosted door seeds;
            // delete pre-existing seed trim/frame extrusions while keeping host Wall, Opening, and reference planes.
            if (!form.IsSolid && CutsAnyHostWall(familyDoc, form))
            {
                continue;
            }

            idsToDelete.Add(form.Id);
        }

        if (idsToDelete.Count > 0)
        {
            familyDoc.Delete(idsToDelete);
            log.Add($"Removed {idsToDelete.Count} pre-existing seed GenericForm element(s) while preserving host Wall, Opening, and reference planes.");
        }
    }

    internal static bool CutsAnyHostWall(Document familyDoc, GenericForm voidForm)
    {
        try
        {
            ICollection<ElementId> cutIds = InstanceVoidCutUtils.GetElementsBeingCut(voidForm);
            if (cutIds != null)
            {
                foreach (ElementId cutId in cutIds)
                {
                    if (familyDoc.GetElement(cutId) is Wall)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Not an instance-void cutting element; check solid-solid cuts below.
        }

        try
        {
            ICollection<ElementId> solidCutIds = SolidSolidCutUtils.GetSolidsBeingCut(voidForm);
            if (solidCutIds != null)
            {
                foreach (ElementId cutId in solidCutIds)
                {
                    if (familyDoc.GetElement(cutId) is Wall)
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Ignore forms not participating in solid-solid cuts.
        }

        return false;
    }

    private static void CreateGeometryPrimitives(
        Document familyDoc,
        FamilyGeometryPlan plan,
        Dictionary<string, Category> subcategories,
        Dictionary<string, ElementId> materialIds,
        List<string> log)
    {
        Dictionary<string, Extrusion> solidsByOpName = new(StringComparer.Ordinal);
        List<Extrusion> createdSolids = new();
        PlanPoint3D placementOffset = FamilyGeometryPlanBuilder.ComputeSeedPlacementOffsetMm(plan.Spec);

        foreach (CompiledExtrusionPrimitive primitive in plan.AllPrimitives)
        {
            Plane revitPlane = Plane.CreateByOriginAndBasis(
                ToXyz(primitive.SketchPlane.Origin, placementOffset),
                new XYZ(primitive.SketchPlane.XAxisX, primitive.SketchPlane.XAxisY, primitive.SketchPlane.XAxisZ),
                new XYZ(primitive.SketchPlane.YAxisX, primitive.SketchPlane.YAxisY, primitive.SketchPlane.YAxisZ));

            SketchPlane sketchPlane = SketchPlane.Create(familyDoc, revitPlane);
            CurveArrArray profile = BuildProfile(primitive.ProfileVertices, placementOffset);

            Extrusion extrusion = familyDoc.FamilyCreate.NewExtrusion(
                primitive.IsSolid,
                profile,
                sketchPlane,
                primitive.ExtrusionDepthFeet);

            if (primitive.IsSolid)
            {
                if (subcategories.TryGetValue(primitive.Subcategory, out Category? subcat) && subcat != null)
                {
                    extrusion.Subcategory = subcat;
                }

                if (materialIds.TryGetValue(primitive.MaterialRef, out ElementId? matId) && matId != null)
                {
                    Parameter? matParam = extrusion.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM);
                    if (matParam != null && !matParam.IsReadOnly)
                    {
                        matParam.Set(matId);
                    }
                }

                FamilyElementVisibility visibility = new(FamilyElementVisibilityType.Model)
                {
                    IsShownInCoarse = primitive.VisibleInCoarse,
                    IsShownInMedium = primitive.VisibleInMedium,
                    IsShownInFine = primitive.VisibleInFine,
                };
                extrusion.SetVisibility(visibility);

                createdSolids.Add(extrusion);
                if (!string.IsNullOrWhiteSpace(primitive.SourceOpName) && !solidsByOpName.ContainsKey(primitive.SourceOpName))
                {
                    solidsByOpName[primitive.SourceOpName] = extrusion;
                }
            }
            else if (!string.IsNullOrWhiteSpace(primitive.TargetOpName) &&
                     solidsByOpName.TryGetValue(primitive.TargetOpName!, out Extrusion? targetSolid) &&
                     targetSolid != null)
            {
                // Defense-in-depth: if Revit's family-editor auto-cut carved any other already-created solid
                // that is not the designated targetOp, remove that unintended cut immediately.
                foreach (Extrusion otherSolid in createdSolids)
                {
                    if (otherSolid.Id == targetSolid.Id)
                    {
                        continue;
                    }

                    if (SolidSolidCutUtils.CutExistsBetweenElements(otherSolid, extrusion, out _))
                    {
                        SolidSolidCutUtils.RemoveCutBetweenSolids(familyDoc, otherSolid, extrusion);
                    }
                }

                // Note parameter order: CanElementCutElement(cuttingElement, cutElement, out reason)
                // is opposite to AddCutBetweenSolids(doc, solidToBeCut, cuttingSolid).
                if (!SolidSolidCutUtils.CutExistsBetweenElements(targetSolid, extrusion, out _) &&
                    SolidSolidCutUtils.CanElementCutElement(extrusion, targetSolid, out _))
                {
                    SolidSolidCutUtils.AddCutBetweenSolids(familyDoc, targetSolid, extrusion);
                }
            }
        }

        log.Add($"Created {plan.AllPrimitives.Count} procedural extrusion primitive(s) ({plan.CoarsePrimitives.Count} Coarse, {plan.MediumFinePrimitives.Count} Medium/Fine).");
    }

    private static CurveArrArray BuildProfile(IReadOnlyList<PlanPoint3D> vertices, PlanPoint3D offset)
    {
        if (vertices.Count < 3)
        {
            throw new InvalidOperationException("Extrusion profile requires at least 3 vertices.");
        }

        CurveArray loop = new();
        for (int i = 0; i < vertices.Count; i++)
        {
            XYZ start = ToXyz(vertices[i], offset);
            XYZ end = ToXyz(vertices[(i + 1) % vertices.Count], offset);
            loop.Append(Line.CreateBound(start, end));
        }

        CurveArrArray profile = new();
        profile.Append(loop);
        return profile;
    }

    private static XYZ ToXyz(PlanPoint3D point, PlanPoint3D offset) =>
        new(point.XFeet + offset.XFeet, point.YFeet + offset.YFeet, point.ZFeet + offset.ZFeet);
}
