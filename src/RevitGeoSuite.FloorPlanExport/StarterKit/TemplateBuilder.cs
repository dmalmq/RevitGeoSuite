using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using RevitGeoSuite.FloorPlanExport.GisTools;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>Configuration for <see cref="TemplateBuilder.BuildTemplate"/>.</summary>
public sealed class TemplateBuildOptions
{
    public TemplateBuildOptions(
        string outputRtePath,
        string? projectSeedTemplatePath = null,
        string? projectSeedTemplatesDirectory = null,
        string? builtFamiliesDirectory = null,
        string? sharedParametersFilePath = null,
        bool overwriteExisting = true,
        bool includeOptionalFamilies = true,
        bool preferJapaneseSeedTemplates = false)
    {
        if (string.IsNullOrWhiteSpace(outputRtePath))
        {
            throw new ArgumentException("Output .rte path is required.", nameof(outputRtePath));
        }

        OutputRtePath = Path.GetFullPath(outputRtePath.Trim());
        ProjectSeedTemplatePath = string.IsNullOrWhiteSpace(projectSeedTemplatePath)
            ? null
            : Path.GetFullPath(projectSeedTemplatePath!.Trim());
        ProjectSeedTemplatesDirectory = string.IsNullOrWhiteSpace(projectSeedTemplatesDirectory)
            ? null
            : Path.GetFullPath(projectSeedTemplatesDirectory!.Trim());
        BuiltFamiliesDirectory = string.IsNullOrWhiteSpace(builtFamiliesDirectory)
            ? null
            : Path.GetFullPath(builtFamiliesDirectory!.Trim());
        SharedParametersFilePath = string.IsNullOrWhiteSpace(sharedParametersFilePath)
            ? null
            : Path.GetFullPath(sharedParametersFilePath!.Trim());
        OverwriteExisting = overwriteExisting;
        IncludeOptionalFamilies = includeOptionalFamilies;
        PreferJapaneseSeedTemplates = preferJapaneseSeedTemplates;
    }

    public string OutputRtePath { get; }

    public string? ProjectSeedTemplatePath { get; }

    public string? ProjectSeedTemplatesDirectory { get; }

    public string? BuiltFamiliesDirectory { get; }

    public string? SharedParametersFilePath { get; }

    public bool OverwriteExisting { get; }

    public bool IncludeOptionalFamilies { get; }

    public bool PreferJapaneseSeedTemplates { get; }
}

/// <summary>Structured result of building and saving the Kanizu Starter Kit <c>.rte</c> template.</summary>
public sealed class TemplateBuildResult
{
    public TemplateBuildResult(
        string outputRtePath,
        string seedTemplatePath,
        long fileSizeBytes,
        TemplateBuildPlan plan,
        IReadOnlyList<string> loadedFamilyNames,
        IReadOnlyList<string> log,
        IReadOnlyList<string> warnings)
    {
        OutputRtePath = outputRtePath ?? string.Empty;
        SeedTemplatePath = seedTemplatePath ?? string.Empty;
        FileSizeBytes = fileSizeBytes;
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        LoadedFamilyNames = Array.AsReadOnly((loadedFamilyNames ?? Array.Empty<string>()).ToArray());
        Log = Array.AsReadOnly((log ?? Array.Empty<string>()).ToArray());
        Warnings = Array.AsReadOnly((warnings ?? Array.Empty<string>()).ToArray());
    }

    public string OutputRtePath { get; }

    public string SeedTemplatePath { get; }

    public long FileSizeBytes { get; }

    public TemplateBuildPlan Plan { get; }

    public IReadOnlyList<string> LoadedFamilyNames { get; }

    public IReadOnlyList<string> Log { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Procedural Revit API template builder for the Kanizu Starter Kit (<c>KanizuStationStarter.rte</c>).
/// Opens an EN/JA metric project template seed via <see cref="Application.NewProjectTemplateDocument"/>,
/// applies the entire <see cref="TemplateBuildPlan"/> in clean transactions with RAII
/// <see cref="ScopedSharedParametersFile"/> restoration and UTF-16 LE BOM shared-parameter files,
/// loads all built <c>.rfa</c> families, enforces zero project identity &amp; zero worksets, and saves
/// the resulting <c>.rte</c> template cleanly.
/// </summary>
public static class TemplateBuilder
{
    private const string BuildTemplateTransactionName = "Kanizu Starter Kit: Build project template";
    private const string LoadFamiliesTransactionName = "Kanizu Starter Kit: Load starter families";
    private const string FinalizeTemplatesTransactionName = "Kanizu Starter Kit: Finalize view templates";

    private const double CutPlaneMm = 1200.0;
    private const double TopClipMm = 2300.0;

    /// <summary>Builds and saves the Starter Kit <c>.rte</c> template from <paramref name="kitSpec"/>.</summary>
    public static TemplateBuildResult BuildTemplate(
        Application application,
        StarterKitSpec kitSpec,
        TemplateBuildOptions options,
        StarterKitReferenceStandards? standards = null,
        IReadOnlyList<FamilyBuildResult>? builtFamilies = null)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
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
        TemplateBuildPlan plan = TemplateBuildPlanBuilder.Build(
            kitSpec,
            new TemplateBuildPlanOptions { IncludeOptionalFamilies = options.IncludeOptionalFamilies },
            refStandards);

        string seedPath = StarterKitProjectTemplateResolver.ResolveProjectTemplatePath(
            options.ProjectSeedTemplatesDirectory,
            options.ProjectSeedTemplatePath,
            options.PreferJapaneseSeedTemplates);

        string sharedParamsPath = ScopedSharedParametersFile.EnsureShippedFilePath(options.SharedParametersFilePath);
        string outputDir = Path.GetDirectoryName(options.OutputRtePath)
            ?? throw new InvalidOperationException($"Output path '{options.OutputRtePath}' has no parent directory.");
        Directory.CreateDirectory(outputDir);

        List<string> log = new()
        {
            $"Building Starter Kit template '{plan.TemplateName}' v{plan.KitVersion} from seed '{seedPath}'.",
        };
        List<string> warnings = new();
        List<string> loadedFamilyNames = new();

        Document? templateDoc = null;
        try
        {
            templateDoc = application.NewProjectTemplateDocument(seedPath);
            if (templateDoc is null || templateDoc.IsFamilyDocument)
            {
                throw new InvalidOperationException($"Failed to create project template document from seed '{seedPath}'.");
            }

            if (templateDoc.IsWorkshared)
            {
                throw new InvalidOperationException("Seed template is workshared; Starter Kit .rte must contain zero worksets.");
            }

            // Guarantee Application.SharedParametersFilename is always restored even if anything throws.
            using (new ScopedSharedParametersFile(application, sharedParamsPath))
            {
                RunTransaction(templateDoc, BuildTemplateTransactionName, () =>
                {
                    ConfigureMetricUnits(templateDoc, plan.UnitsAndDefaults, log);
                    BindAllSharedParameters(application, templateDoc, plan.SharedParameters, log, warnings);
                    templateDoc.Regenerate();

                    StampProjectInfoDefaults(templateDoc, plan.UnitsAndDefaults, log);
                    EnsureZeroBakedIdentity(templateDoc, log);

                    ElementId solidFillId = FindSolidDraftingFillPatternId(templateDoc);
                    Dictionary<string, ElementId> materialIds = EnsureAllMaterials(
                        templateDoc,
                        plan.Gis2DMaterials,
                        plan.Materials3D,
                        solidFillId,
                        log);

                    EnsureAreaAndColorFillScheme(templateDoc, plan.AreaColorScheme, solidFillId, log, warnings);
                    EnsureGisLineStyles(templateDoc, plan.LineStyles, log);
                    EnsureFootprintSubcategories(templateDoc, log);
                    EnsureStandardFloorTypes(templateDoc, plan.StandardFloorTypes, materialIds, log);
                    EnsureSystemFamilyTypes(templateDoc, plan.SystemFamilies, materialIds, log, warnings);
                    templateDoc.Regenerate();

                    Dictionary<string, ElementId> sharedParamIds = MapSharedParameterIdsByName(templateDoc);
                    Dictionary<string, ElementId> qaFilterIds = EnsureQaViewFilters(
                        templateDoc,
                        plan.QaViewFilters,
                        sharedParamIds,
                        log,
                        warnings);

                    EnsureViewTemplates(
                        templateDoc,
                        plan.ViewTemplates,
                        plan.QaViewFilters,
                        qaFilterIds,
                        solidFillId,
                        log,
                        warnings);

                    EnsureSchedules(templateDoc, plan.Schedules, sharedParamIds, log, warnings);
                    EnsureStartingView(templateDoc, plan.StartingView, log, warnings);

                    templateDoc.Regenerate();
                });

                List<string> familyPaths = ResolveBuiltFamilyPaths(plan, options, builtFamilies);
                if (familyPaths.Count > 0)
                {
                    RunTransaction(templateDoc, LoadFamiliesTransactionName, () =>
                    {
                        OverwriteFamilyLoadOptions loadOptions = new();
                        foreach (string rfaPath in familyPaths)
                        {
                            if (templateDoc.LoadFamily(rfaPath, loadOptions, out Family? loaded) && loaded != null)
                            {
                                loadedFamilyNames.Add(loaded.Name);
                                log.Add($"Loaded family '{loaded.Name}' from '{Path.GetFileName(rfaPath)}'.");
                            }
                            else
                            {
                                string expectedName = Path.GetFileNameWithoutExtension(rfaPath);
                                Family? existing = new FilteredElementCollector(templateDoc)
                                    .OfClass(typeof(Family))
                                    .Cast<Family>()
                                    .FirstOrDefault(f => string.Equals(f.Name, expectedName, StringComparison.OrdinalIgnoreCase));
                                if (existing != null)
                                {
                                    loadedFamilyNames.Add(existing.Name);
                                    log.Add($"Family '{existing.Name}' already up-to-date in template.");
                                }
                                else
                                {
                                    warnings.Add($"Could not load family '{rfaPath}' into template.");
                                }
                            }
                        }
                    });

                    // Second pass after loading families so K_Footprint subcategories registered by the
                    // loaded .rfa families are explicitly hidden on 3D view templates (GIS QA 3D, Kanizu 3D Export).
                    RunTransaction(templateDoc, FinalizeTemplatesTransactionName, () =>
                    {
                        foreach (TemplateViewTemplatePlan vtPlan in plan.ViewTemplates)
                        {
                            if (vtPlan.HideSubcategories.Count == 0)
                            {
                                continue;
                            }

                            View? vt = FindViewTemplateByName(templateDoc, vtPlan.Name);
                            if (vt != null)
                            {
                                HideSubcategoriesOnView(templateDoc, vt, vtPlan.HideSubcategories);
                            }
                        }

                        templateDoc.Regenerate();
                    });
                }
            }

            SaveAsOptions saveOptions = new()
            {
                OverwriteExistingFile = options.OverwriteExisting,
                Compact = true,
            };
            templateDoc.SaveAs(options.OutputRtePath, saveOptions);
            long fileSizeBytes = new FileInfo(options.OutputRtePath).Length;
            log.Add($"Saved Starter Kit template '{options.OutputRtePath}' ({fileSizeBytes} bytes).");

            return new TemplateBuildResult(
                options.OutputRtePath,
                seedPath,
                fileSizeBytes,
                plan,
                loadedFamilyNames,
                log,
                warnings);
        }
        finally
        {
            if (templateDoc != null && templateDoc.IsValidObject)
            {
                try
                {
                    templateDoc.Close(false);
                }
                catch (Exception)
                {
                    // Best-effort cleanup of template document.
                }
            }
        }
    }

    private static void ConfigureMetricUnits(Document doc, TemplateUnitsAndDefaultsPlan unitsPlan, List<string> log)
    {
        Units units = doc.GetUnits();

        FormatOptions lengthOptions = new(UnitTypeId.Millimeters)
        {
            Accuracy = unitsPlan.LengthAccuracyMm,
        };
        units.SetFormatOptions(SpecTypeId.Length, lengthOptions);

        FormatOptions areaOptions = new(UnitTypeId.SquareMeters)
        {
            Accuracy = unitsPlan.AreaAccuracySqM,
        };
        units.SetFormatOptions(SpecTypeId.Area, areaOptions);

        FormatOptions angleOptions = new(UnitTypeId.Degrees)
        {
            Accuracy = unitsPlan.AngleAccuracyDeg,
        };
        units.SetFormatOptions(SpecTypeId.Angle, angleOptions);

        doc.SetUnits(units);
        log.Add($"Configured metric units: Length={unitsPlan.LengthUnit}, Area={unitsPlan.AreaUnit}, Angle={unitsPlan.AngleUnit}.");
    }

    private static void BindAllSharedParameters(
        Application application,
        Document doc,
        IReadOnlyList<TemplateSharedParameterPlan> parameters,
        List<string> log,
        List<string> warnings)
    {
        DefinitionFile? defFile = application.OpenSharedParameterFile();
        DefinitionGroup? group = defFile?.Groups.get_Item(GisSharedParameters.GroupName);
        if (group is null)
        {
            throw new InvalidOperationException(
                $"Unable to open Kanizu shared parameter group '{GisSharedParameters.GroupName}' from '{application.SharedParametersFilename}'.");
        }

        BindingMap map = doc.ParameterBindings;
        foreach (TemplateSharedParameterPlan paramPlan in parameters)
        {
            ExternalDefinition? extDef = group.Definitions.get_Item(paramPlan.Name) as ExternalDefinition;
            if (extDef is null || extDef.GUID != paramPlan.Guid)
            {
                throw new InvalidOperationException(
                    $"Shared parameter file is missing frozen definition '{paramPlan.Name}' ({paramPlan.Guid:D}).");
            }

            CategorySet catSet = application.Create.NewCategorySet();
            foreach (string catName in paramPlan.Categories)
            {
                if (!Enum.TryParse(catName, ignoreCase: false, out BuiltInCategory bic))
                {
                    warnings.Add($"Unknown BuiltInCategory '{catName}' for shared parameter '{paramPlan.Name}'.");
                    continue;
                }

                Category? cat = Category.GetCategory(doc, bic);
                if (cat != null && cat.AllowsBoundParameters)
                {
                    catSet.Insert(cat);
                }
            }

            if (catSet.Size == 0)
            {
                warnings.Add($"No bindable categories found for shared parameter '{paramPlan.Name}'.");
                continue;
            }

            Binding binding = string.Equals(paramPlan.Binding, "type", StringComparison.OrdinalIgnoreCase)
                ? (Binding)application.Create.NewTypeBinding(catSet)
                : application.Create.NewInstanceBinding(catSet);

            if (!map.Insert(extDef, binding, GroupTypeId.General))
            {
                map.ReInsert(extDef, binding, GroupTypeId.General);
            }
        }

        // Enable VaryBetweenGroups on every groupable instance parameter (matching SharedParameterManager).
        HashSet<string> varyNames = new(
            parameters.Where(p => p.AllowVaryBetweenGroups).Select(p => p.Name),
            StringComparer.Ordinal);
        List<InternalDefinition> toVary = new();
        DefinitionBindingMapIterator iter = doc.ParameterBindings.ForwardIterator();
        iter.Reset();
        while (iter.MoveNext())
        {
            if (iter.Key is InternalDefinition internalDef &&
                iter.Current is InstanceBinding &&
                varyNames.Contains(internalDef.Name) &&
                !internalDef.VariesAcrossGroups)
            {
                toVary.Add(internalDef);
            }
        }

        foreach (InternalDefinition def in toVary)
        {
            try
            {
                def.SetAllowVaryBetweenGroups(doc, true);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                warnings.Add($"Could not set VaryBetweenGroups on '{def.Name}': {ex.Message}");
            }
        }

        log.Add($"Bound {parameters.Count} frozen Kanizu shared parameters.");
    }

    private static void StampProjectInfoDefaults(Document doc, TemplateUnitsAndDefaultsPlan defaults, List<string> log)
    {
        Element projectInfo = doc.ProjectInformation;
        SetSharedTextParameter(projectInfo, GisSharedParameters.GisStandardVersion, defaults.GisStandardVersion.ToString(CultureInfo.InvariantCulture));
        SetSharedTextParameter(projectInfo, GisSharedParameters.GisModelRole, defaults.ModelRole);
        SetSharedTextParameter(projectInfo, GisSharedParameters.GisKitVersion, defaults.KitVersion);
        log.Add($"Stamped ProjectInformation: gis_standard_version={defaults.GisStandardVersion}, gis_model_role={defaults.ModelRole}, gis_kit_version={defaults.KitVersion}.");
    }

    private static void EnsureZeroBakedIdentity(Document doc, List<string> log)
    {
        int clearedLevels = 0;
        foreach (Level level in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
        {
            if (ClearSharedTextIfPresent(level, GisSharedParameters.LevelId))
            {
                clearedLevels++;
            }
        }

        int clearedElements = 0;
        HashSet<BuiltInCategory> idCategories = new();
        foreach (string name in new[] { GisSharedParameters.Id, GisSharedParameters.GisKey })
        {
            if (GisSharedParameters.TryGet(name, out GisParameterDefinition def))
            {
                foreach (string catName in def.Categories)
                {
                    if (Enum.TryParse(catName, ignoreCase: false, out BuiltInCategory bic) && bic != BuiltInCategory.INVALID)
                    {
                        idCategories.Add(bic);
                    }
                }
            }
        }

        if (idCategories.Count > 0)
        {
            foreach (Element elem in new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(idCategories))
                .WhereElementIsNotElementType())
            {
                bool clearedId = ClearSharedTextIfPresent(elem, GisSharedParameters.Id);
                bool clearedKey = ClearSharedTextIfPresent(elem, GisSharedParameters.GisKey);
                if (clearedId || clearedKey)
                {
                    clearedElements++;
                }
            }
        }

        if (clearedLevels > 0 || clearedElements > 0)
        {
            log.Add($"Cleared baked-in identity on {clearedLevels} seed level(s) and {clearedElements} seed element(s).");
        }
    }

    private static void EnsureFootprintSubcategories(Document doc, List<string> log)
    {
        BuiltInCategory[] familyCategories =
        {
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_SpecialityEquipment,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Columns,
        };

        int created = 0;
        foreach (BuiltInCategory bic in familyCategories)
        {
            Category? parent = doc.Settings.Categories.get_Item(bic);
            if (parent is null)
            {
                continue;
            }

            bool exists = false;
            if (parent.SubCategories != null)
            {
                foreach (Category sub in parent.SubCategories)
                {
                    if (sub != null && string.Equals(sub.Name, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }
            }

            if (!exists)
            {
                try
                {
                    doc.Settings.Categories.NewSubcategory(parent, CoarseGeometrySpec.FootprintSubcategory);
                    created++;
                }
                catch (Exception)
                {
                    // Ignore categories that disallow project-level subcategories.
                }
            }
        }

        if (created > 0)
        {
            log.Add($"Pre-registered '{CoarseGeometrySpec.FootprintSubcategory}' subcategory on {created} model categories.");
        }
    }

    private static Dictionary<string, ElementId> EnsureAllMaterials(
        Document doc,
        IReadOnlyList<TemplateMaterialPlan> gis2DMaterials,
        IReadOnlyList<TemplateMaterialPlan> materials3D,
        ElementId solidFillId,
        List<string> log)
    {
        Dictionary<string, Material> existing = new FilteredElementCollector(doc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .GroupBy(m => m.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        Dictionary<string, ElementId> ids = new(StringComparer.Ordinal);
        foreach (TemplateMaterialPlan matPlan in gis2DMaterials.Concat(materials3D))
        {
            if (!existing.TryGetValue(matPlan.Name, out Material? material))
            {
                ElementId newId = Material.Create(doc, matPlan.Name);
                material = (Material)doc.GetElement(newId);
                existing[matPlan.Name] = material;
            }

            Color color = new(matPlan.Red, matPlan.Green, matPlan.Blue);
            if (color.IsValid)
            {
                material.Color = color;
            }

            material.Transparency = Math.Max(0, Math.Min(100, matPlan.Transparency));
            material.UseRenderAppearanceForShading = false;

            if (matPlan.ApplySolidDraftingFill && solidFillId != ElementId.InvalidElementId && color.IsValid)
            {
                material.SurfaceForegroundPatternId = solidFillId;
                material.SurfaceForegroundPatternColor = color;
                material.CutForegroundPatternId = solidFillId;
                material.CutForegroundPatternColor = color;
            }

            ids[matPlan.Name] = material.Id;
        }

        log.Add($"Ensured {gis2DMaterials.Count} GIS_* 2D materials and {materials3D.Count} K3D_*/M3D_* 3D materials.");
        return ids;
    }

    private static void EnsureAreaAndColorFillScheme(
        Document doc,
        TemplateAreaColorSchemePlan plan,
        ElementId solidFillId,
        List<string> log,
        List<string> warnings)
    {
        // 1. Ensure AreaScheme "GIS Area"
        AreaScheme? areaScheme = new FilteredElementCollector(doc)
            .OfClass(typeof(AreaScheme))
            .Cast<AreaScheme>()
            .FirstOrDefault(a => string.Equals(a.Name, plan.AreaSchemeName, StringComparison.OrdinalIgnoreCase));

        if (areaScheme is null)
        {
            AreaScheme? sourceScheme = new FilteredElementCollector(doc)
                .OfClass(typeof(AreaScheme))
                .Cast<AreaScheme>()
                .FirstOrDefault(a => !a.IsGrossBuildingArea)
                ?? new FilteredElementCollector(doc).OfClass(typeof(AreaScheme)).Cast<AreaScheme>().FirstOrDefault();

            if (sourceScheme != null)
            {
                try
                {
                    ICollection<ElementId> copied = ElementTransformUtils.CopyElements(
                        doc,
                        new[] { sourceScheme.Id },
                        doc,
                        Transform.Identity,
                        new CopyPasteOptions());
                    ElementId? copiedId = copied.FirstOrDefault();
                    if (copiedId != null && doc.GetElement(copiedId) is AreaScheme duplicated)
                    {
                        duplicated.Name = plan.AreaSchemeName;
                        areaScheme = duplicated;
                        log.Add($"Created AreaScheme '{plan.AreaSchemeName}'.");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not duplicate AreaScheme '{plan.AreaSchemeName}': {ex.Message}");
                }
            }
        }

        // 2. Ensure ColorFillScheme "GIS Area Colors"
        ColorFillScheme? colorScheme = new FilteredElementCollector(doc)
            .OfClass(typeof(ColorFillScheme))
            .Cast<ColorFillScheme>()
            .FirstOrDefault(s => string.Equals(s.Name, plan.ColorSchemeName, StringComparison.OrdinalIgnoreCase));

        if (colorScheme is null)
        {
            ColorFillScheme? baseScheme = null;
            if (areaScheme != null)
            {
                baseScheme = new FilteredElementCollector(doc)
                    .OfClass(typeof(ColorFillScheme))
                    .Cast<ColorFillScheme>()
                    .FirstOrDefault(s => s.AreaSchemeId == areaScheme.Id);
            }

            baseScheme ??= new FilteredElementCollector(doc)
                .OfClass(typeof(ColorFillScheme))
                .Cast<ColorFillScheme>()
                .FirstOrDefault(s => s.CategoryId == new ElementId(BuiltInCategory.OST_Rooms)
                                  || s.CategoryId == new ElementId(BuiltInCategory.OST_Areas));

            if (baseScheme != null)
            {
                try
                {
                    ElementId dupId = baseScheme.Duplicate(plan.ColorSchemeName);
                    colorScheme = doc.GetElement(dupId) as ColorFillScheme;
                    if (colorScheme != null)
                    {
                        colorScheme.Title = plan.ColorSchemeName;
                        log.Add($"Created ColorFillScheme '{plan.ColorSchemeName}'.");
                    }
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not duplicate ColorFillScheme '{plan.ColorSchemeName}': {ex.Message}");
                }
            }
        }

        if (colorScheme != null && colorScheme.StorageType == StorageType.String && solidFillId != ElementId.InvalidElementId)
        {
            foreach (TemplateAreaColorEntry entryPlan in plan.Entries)
            {
                try
                {
                    ColorFillSchemeEntry entry = new(StorageType.String)
                    {
                        Caption = entryPlan.MaterialName,
                        Color = new Color(entryPlan.Red, entryPlan.Green, entryPlan.Blue),
                        FillPatternId = solidFillId,
                        IsVisible = true,
                    };
                    entry.SetStringValue(entryPlan.MaterialName);
                    if (colorScheme.IsEntryConsistentWithScheme(entry) == EntryAndSchemeConsistency.Consistent)
                    {
                        colorScheme.AddEntry(entry);
                    }
                }
                catch (Exception)
                {
                    // Skip duplicate/incompatible entries cleanly across localized templates.
                }
            }
        }
    }

    private static void EnsureGisLineStyles(Document doc, IReadOnlyList<TemplateLineStylePlan> lineStyles, List<string> log)
    {
        Category linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines)
            ?? throw new InvalidOperationException("Document is missing BuiltInCategory.OST_Lines.");

        Dictionary<string, Category> existingStyles = new(StringComparer.Ordinal);
        if (linesCategory.SubCategories != null)
        {
            foreach (Category sub in linesCategory.SubCategories)
            {
                if (sub != null && !string.IsNullOrWhiteSpace(sub.Name) && !existingStyles.ContainsKey(sub.Name))
                {
                    existingStyles[sub.Name] = sub;
                }
            }
        }

        foreach (TemplateLineStylePlan ls in lineStyles)
        {
            if (!existingStyles.TryGetValue(ls.Name, out Category? styleCat))
            {
                styleCat = doc.Settings.Categories.NewSubcategory(linesCategory, ls.Name);
                existingStyles[ls.Name] = styleCat;
            }

            styleCat.SetLineWeight(ls.LineWeight, GraphicsStyleType.Projection);
            Color color = new(ls.Red, ls.Green, ls.Blue);
            if (color.IsValid)
            {
                styleCat.LineColor = color;
            }

            ElementId patternId = ResolveOrCreateLinePatternId(doc, ls.LinePattern);
            if (patternId != ElementId.InvalidElementId)
            {
                styleCat.SetLinePatternId(patternId, GraphicsStyleType.Projection);
            }
        }

        log.Add($"Ensured {lineStyles.Count} GIS_* line styles.");
    }

    private static ElementId ResolveOrCreateLinePatternId(Document doc, string linePatternName)
    {
        if (string.IsNullOrWhiteSpace(linePatternName) ||
            string.Equals(linePatternName, "Solid", StringComparison.OrdinalIgnoreCase))
        {
            return LinePatternElement.GetSolidPatternId();
        }

        LinePatternElement? existing = new FilteredElementCollector(doc)
            .OfClass(typeof(LinePatternElement))
            .Cast<LinePatternElement>()
            .FirstOrDefault(p => string.Equals(p.Name, linePatternName, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing.Id;
        }

        // Create deterministic Dash pattern if the seed template lacks an exact match.
        double dashFeet = UnitUtils.ConvertToInternalUnits(3.0, UnitTypeId.Millimeters);
        double spaceFeet = UnitUtils.ConvertToInternalUnits(2.0, UnitTypeId.Millimeters);
        LinePattern pattern = new(linePatternName);
        pattern.SetSegments(new List<LinePatternSegment>
        {
            new LinePatternSegment(LinePatternSegmentType.Dash, dashFeet),
            new LinePatternSegment(LinePatternSegmentType.Space, spaceFeet),
        });
        LinePatternElement created = LinePatternElement.Create(doc, pattern);
        return created.Id;
    }

    private static void EnsureStandardFloorTypes(
        Document doc,
        IReadOnlyList<StandardFloorType> floorTypes,
        IReadOnlyDictionary<string, ElementId> materialIds,
        List<string> log)
    {
        if (floorTypes.Count == 0)
        {
            return;
        }

        Dictionary<string, FloorType> existingByName = new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .GroupBy(f => f.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        FloorType baseFloorType = FindBaseFloorType(doc);
        int createdCount = 0;

        foreach (StandardFloorType std in floorTypes)
        {
            if (!existingByName.TryGetValue(std.Name, out FloorType? ft))
            {
                ft = (FloorType)baseFloorType.Duplicate(std.Name);
                existingByName[std.Name] = ft;
                createdCount++;
            }

            SetSharedTextParameter(ft, GisSharedParameters.GisCategory, std.GisCategory);
            SetSharedTextParameter(ft, GisSharedParameters.GisRestriction, std.GisRestriction ?? string.Empty);

            if (materialIds.TryGetValue(std.Material, out ElementId? matId) && matId != null)
            {
                SetTopLayerMaterial(ft, matId, thicknessMm: null);
            }
        }

        log.Add($"Ensured {floorTypes.Count} standard GIS floor types ({createdCount} created).");
    }

    private static void EnsureSystemFamilyTypes(
        Document doc,
        IReadOnlyList<TemplateSystemFamilyPlan> systemFamilies,
        IReadOnlyDictionary<string, ElementId> materialIds,
        List<string> log,
        List<string> warnings)
    {
        foreach (TemplateSystemFamilyPlan sf in systemFamilies)
        {
            materialIds.TryGetValue(sf.MaterialRef, out ElementId? materialId);

            if (string.Equals(sf.SystemKind, "Stair", StringComparison.OrdinalIgnoreCase))
            {
                EnsureMonolithicStairType(doc, sf, materialId, log, warnings);
            }
            else if (string.Equals(sf.SystemKind, "Wall", StringComparison.OrdinalIgnoreCase))
            {
                EnsureGlassGuardWallType(doc, sf, materialId, log, warnings);
            }
            else if (string.Equals(sf.SystemKind, "Floor", StringComparison.OrdinalIgnoreCase))
            {
                EnsureSystemFloorType(doc, sf, materialId, log);
            }
        }
    }

    private static void EnsureMonolithicStairType(
        Document doc,
        TemplateSystemFamilyPlan sf,
        ElementId? materialId,
        List<string> log,
        List<string> warnings)
    {
        List<StairsType> allStairTypes = new FilteredElementCollector(doc)
            .OfClass(typeof(StairsType))
            .Cast<StairsType>()
            .ToList();

        StairsType? target = allStairTypes.FirstOrDefault(s =>
            string.Equals(s.Name, sf.TypeName, StringComparison.Ordinal));

        if (target is null)
        {
            StairsType? baseStair = allStairTypes.FirstOrDefault(s =>
                    s.ConstructionMethod == StairsConstructionMethod.CastInPlace)
                ?? allStairTypes.FirstOrDefault(s =>
                    s.RunType != ElementId.InvalidElementId &&
                    doc.GetElement(s.RunType) is StairsRunType { IsMonolithic: true })
                ?? allStairTypes.FirstOrDefault();

            if (baseStair is null)
            {
                warnings.Add($"Seed template has no StairsType to duplicate '{sf.TypeName}'.");
                return;
            }

            target = (StairsType)baseStair.Duplicate(sf.TypeName);
        }

        // Remove side/middle supports for minimal triangle count where supported.
        try
        {
            target.HasMiddleSupports = false;
            target.LeftSideSupportType = ElementId.InvalidElementId;
            target.RightSideSupportType = ElementId.InvalidElementId;
        }
        catch (Exception)
        {
            // Ignore if stair construction method locks supports.
        }

        if (materialId != null && materialId != ElementId.InvalidElementId)
        {
            if (target.RunType != ElementId.InvalidElementId &&
                doc.GetElement(target.RunType) is StairsRunType currentRun)
            {
                string runTypeName = sf.TypeName + "_Run";
                StairsRunType? runType = new FilteredElementCollector(doc)
                    .OfClass(typeof(StairsRunType))
                    .Cast<StairsRunType>()
                    .FirstOrDefault(r => string.Equals(r.Name, runTypeName, StringComparison.Ordinal));
                if (runType is null)
                {
                    runType = (StairsRunType)currentRun.Duplicate(runTypeName);
                }

                try
                {
                    runType.MaterialId = materialId;
                }
                catch (Exception)
                {
                    // Non-monolithic run types use tread/riser material parameters instead.
                    SetElementIdBuiltInParameter(runType, BuiltInParameter.STAIRS_TRISERTYPE_TREAD_MATERIAL, materialId);
                    SetElementIdBuiltInParameter(runType, BuiltInParameter.STAIRS_TRISERTYPE_RISER_MATERIAL, materialId);
                }

                target.RunType = runType.Id;
            }

            if (target.LandingType != ElementId.InvalidElementId &&
                doc.GetElement(target.LandingType) is StairsLandingType currentLanding)
            {
                string landingTypeName = sf.TypeName + "_Landing";
                StairsLandingType? landingType = new FilteredElementCollector(doc)
                    .OfClass(typeof(StairsLandingType))
                    .Cast<StairsLandingType>()
                    .FirstOrDefault(l => string.Equals(l.Name, landingTypeName, StringComparison.Ordinal));
                if (landingType is null)
                {
                    landingType = (StairsLandingType)currentLanding.Duplicate(landingTypeName);
                }

                SetElementIdBuiltInParameter(landingType, BuiltInParameter.STAIRS_TRISERTYPE_TREAD_MATERIAL, materialId);
                target.LandingType = landingType.Id;
            }
        }

        log.Add($"Ensured system stair type '{sf.TypeName}' (material={sf.MaterialRef}).");
    }

    private static void EnsureGlassGuardWallType(
        Document doc,
        TemplateSystemFamilyPlan sf,
        ElementId? materialId,
        List<string> log,
        List<string> warnings)
    {
        List<WallType> basicWalls = new FilteredElementCollector(doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(w => w.Kind == WallKind.Basic)
            .ToList();

        WallType? target = basicWalls.FirstOrDefault(w =>
            string.Equals(w.Name, sf.TypeName, StringComparison.Ordinal));

        if (target is null)
        {
            WallType? baseWall = basicWalls.FirstOrDefault();
            if (baseWall is null)
            {
                warnings.Add($"Seed template has no Basic WallType to duplicate '{sf.TypeName}'.");
                return;
            }

            target = (WallType)baseWall.Duplicate(sf.TypeName);
        }

        double thicknessMm = sf.ThicknessMm ?? 20.0;
        double thicknessFeet = UnitUtils.ConvertToInternalUnits(thicknessMm, UnitTypeId.Millimeters);
        ElementId layerMatId = materialId ?? ElementId.InvalidElementId;
        CompoundStructure cs = CompoundStructure.CreateSingleLayerCompoundStructure(
            MaterialFunctionAssignment.Structure,
            thicknessFeet,
            layerMatId);
        target.SetCompoundStructure(cs);

        log.Add($"Ensured system wall type '{sf.TypeName}' ({thicknessMm} mm, material={sf.MaterialRef}).");
    }

    private static void EnsureSystemFloorType(
        Document doc,
        TemplateSystemFamilyPlan sf,
        ElementId? materialId,
        List<string> log)
    {
        FloorType? target = new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault(f => string.Equals(f.Name, sf.TypeName, StringComparison.Ordinal));

        if (target is null)
        {
            FloorType baseFloor = FindBaseFloorType(doc);
            target = (FloorType)baseFloor.Duplicate(sf.TypeName);
        }

        if (!string.IsNullOrWhiteSpace(sf.GisCategory))
        {
            SetSharedTextParameter(target, GisSharedParameters.GisCategory, sf.GisCategory!);
        }

        SetSharedTextParameter(target, GisSharedParameters.GisRestriction, string.Empty);

        if (materialId != null && materialId != ElementId.InvalidElementId)
        {
            SetTopLayerMaterial(target, materialId, sf.ThicknessMm);
        }

        log.Add($"Ensured system floor type '{sf.TypeName}' (gis_category={sf.GisCategory}, material={sf.MaterialRef}).");
    }

    private static Dictionary<string, ElementId> EnsureQaViewFilters(
        Document doc,
        IReadOnlyList<TemplateQaFilterPlan> qaFilters,
        IReadOnlyDictionary<string, ElementId> sharedParamIds,
        List<string> log,
        List<string> warnings)
    {
        Dictionary<string, ParameterFilterElement> existingByName = new FilteredElementCollector(doc)
            .OfClass(typeof(ParameterFilterElement))
            .Cast<ParameterFilterElement>()
            .GroupBy(f => f.Name ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        Dictionary<string, ElementId> result = new(StringComparer.Ordinal);

        foreach (TemplateQaFilterPlan filterPlan in qaFilters)
        {
            List<ElementId> categoryIds = ResolveBuiltInCategoryIds(filterPlan.Categories, warnings);
            if (categoryIds.Count == 0)
            {
                warnings.Add($"QA filter '{filterPlan.Name}' resolved zero valid categories.");
                continue;
            }

            ElementFilter? elementFilter = BuildElementFilter(filterPlan.Rules, sharedParamIds, filterPlan.Name, warnings);
            if (!existingByName.TryGetValue(filterPlan.Name, out ParameterFilterElement? filterElement))
            {
                filterElement = elementFilter != null
                    ? ParameterFilterElement.Create(doc, filterPlan.Name, categoryIds, elementFilter)
                    : ParameterFilterElement.Create(doc, filterPlan.Name, categoryIds);
                existingByName[filterPlan.Name] = filterElement;
            }
            else
            {
                filterElement.SetCategories(categoryIds);
                if (elementFilter != null)
                {
                    filterElement.SetElementFilter(elementFilter);
                }
                else
                {
                    filterElement.ClearRules();
                }
            }

            result[filterPlan.Name] = filterElement.Id;
        }

        log.Add($"Ensured {result.Count} QA ParameterFilterElements.");
        return result;
    }

    private static ElementFilter? BuildElementFilter(
        IReadOnlyList<ManifestFilterRuleEntry> rules,
        IReadOnlyDictionary<string, ElementId> sharedParamIds,
        string filterName,
        List<string> warnings)
    {
        if (rules.Count == 0)
        {
            return null;
        }

        List<ElementFilter> subFilters = new(rules.Count);
        foreach (ManifestFilterRuleEntry rule in rules)
        {
            if (!sharedParamIds.TryGetValue(rule.Parameter, out ElementId? paramId) || paramId is null)
            {
                warnings.Add($"QA filter '{filterName}' references unbound parameter '{rule.Parameter}'.");
                continue;
            }

            FilterRule? revitRule = CreateRevitFilterRule(paramId, rule.Operator, rule.Value);
            if (revitRule is null)
            {
                warnings.Add($"QA filter '{filterName}' uses unsupported operator '{rule.Operator}'.");
                continue;
            }

            subFilters.Add(new ElementParameterFilter(revitRule));
        }

        if (subFilters.Count == 0)
        {
            return null;
        }

        return subFilters.Count == 1 ? subFilters[0] : new LogicalAndFilter(subFilters);
    }

    private static FilterRule? CreateRevitFilterRule(ElementId paramId, string op, string value)
    {
        switch (op)
        {
            case "isEmpty":
                return ParameterFilterRuleFactory.CreateHasNoValueParameterRule(paramId);
            case "hasValue":
                return ParameterFilterRuleFactory.CreateHasValueParameterRule(paramId);
            case "equals":
                return ParameterFilterRuleFactory.CreateEqualsRule(paramId, value ?? string.Empty);
            case "notEquals":
                return ParameterFilterRuleFactory.CreateNotEqualsRule(paramId, value ?? string.Empty);
            case "contains":
                return ParameterFilterRuleFactory.CreateContainsRule(paramId, value ?? string.Empty);
            default:
                return null;
        }
    }

    private static void EnsureViewTemplates(
        Document doc,
        IReadOnlyList<TemplateViewTemplatePlan> viewTemplates,
        IReadOnlyList<TemplateQaFilterPlan> qaFilters,
        IReadOnlyDictionary<string, ElementId> qaFilterIds,
        ElementId solidFillId,
        List<string> log,
        List<string> warnings)
    {
        Dictionary<string, TemplateQaFilterPlan> qaByName = qaFilters
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        ElementId levelId = EnsureAtLeastOneLevel(doc);
        ElementId floorPlanTypeId = FindViewFamilyTypeId(doc, ViewFamily.FloorPlan);
        ElementId threeDTypeId = FindViewFamilyTypeId(doc, ViewFamily.ThreeDimensional);

        foreach (TemplateViewTemplatePlan vtPlan in viewTemplates)
        {
            View template;
            if (vtPlan.IsCanonical2DExport)
            {
                // Reuse ExportViewSetup for the canonical "GIS Export" template so TemplateBuilder
                // can never diverge from TemplateReadinessCheck.
                ExportViewSetup.EnsureNamedExportPlanTemplate(doc, vtPlan.Name, log);
                template = FindViewTemplateByName(doc, vtPlan.Name)
                    ?? throw new InvalidOperationException($"Failed to create canonical view template '{vtPlan.Name}'.");
            }
            else
            {
                View? existing = FindViewTemplateByName(doc, vtPlan.Name);
                if (existing is null)
                {
                    existing = CreateViewTemplateForType(doc, vtPlan.Name, vtPlan.ViewType, levelId, floorPlanTypeId, threeDTypeId);
                }

                template = existing;
                ConfigureViewTemplateCategoriesAndDetail(doc, template, vtPlan);
            }

            ApplyFiltersToViewTemplate(template, vtPlan, qaByName, qaFilterIds, solidFillId, warnings);
            if (vtPlan.HideSubcategories.Count > 0)
            {
                HideSubcategoriesOnView(doc, template, vtPlan.HideSubcategories);
            }
        }

        log.Add($"Ensured {viewTemplates.Count} unified view templates (canonical 2D: '{TemplateStandardDefinitions.ExportPlanViewTemplateName}').");
    }

    private static View CreateViewTemplateForType(
        Document doc,
        string name,
        string viewType,
        ElementId levelId,
        ElementId floorPlanTypeId,
        ElementId threeDTypeId)
    {
        View scratch = string.Equals(viewType, "ThreeD", StringComparison.OrdinalIgnoreCase)
            ? (View)View3D.CreateIsometric(doc, threeDTypeId)
            : ViewPlan.Create(doc, floorPlanTypeId, levelId);

        View template = scratch.CreateViewTemplate();
        doc.Delete(scratch.Id);
        template.Name = name;
        return template;
    }

    private static void ConfigureViewTemplateCategoriesAndDetail(
        Document doc,
        View template,
        TemplateViewTemplatePlan vtPlan)
    {
        HashSet<long> visibleCategoryIds = new();
        foreach (string catName in vtPlan.VisibleCategories)
        {
            if (Enum.TryParse(catName, ignoreCase: false, out BuiltInCategory bic))
            {
                visibleCategoryIds.Add((long)bic);
            }
        }

        // Keep stair sub-parts and links visible when Stairs are visible.
        if (visibleCategoryIds.Contains((long)BuiltInCategory.OST_Stairs))
        {
            visibleCategoryIds.Add((long)BuiltInCategory.OST_StairsRuns);
            visibleCategoryIds.Add((long)BuiltInCategory.OST_StairsLandings);
            visibleCategoryIds.Add((long)BuiltInCategory.OST_StairsSupports);
        }

        visibleCategoryIds.Add((long)BuiltInCategory.OST_RvtLinks);

        foreach (Category cat in doc.Settings.Categories)
        {
            if (cat.CategoryType is CategoryType.Model or CategoryType.Annotation &&
                template.CanCategoryBeHidden(cat.Id))
            {
                bool show = visibleCategoryIds.Contains(cat.Id.Value);
                template.SetCategoryHidden(cat.Id, !show);
            }
        }

        Category? linesCategory = doc.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        if (linesCategory?.SubCategories != null)
        {
            foreach (Category style in linesCategory.SubCategories)
            {
                if (!template.CanCategoryBeHidden(style.Id))
                {
                    continue;
                }

                if (vtPlan.HideNonGisLineStyles)
                {
                    bool matchesPrefix = vtPlan.VisibleLineStylePrefixes.Any(prefix =>
                        (style.Name ?? string.Empty).StartsWith(prefix, StringComparison.Ordinal));
                    template.SetCategoryHidden(style.Id, !matchesPrefix);
                }
                else
                {
                    template.SetCategoryHidden(style.Id, false);
                }
            }
        }

        template.AreImportCategoriesHidden = true;
        template.DetailLevel = ParseDetailLevel(vtPlan.DetailLevel);
        template.Discipline = ViewDiscipline.Coordination;

        if (template is ViewPlan planTemplate)
        {
            PlanViewRange range = planTemplate.GetViewRange();
            foreach (PlanViewPlane plane in new[]
            {
                PlanViewPlane.CutPlane,
                PlanViewPlane.BottomClipPlane,
                PlanViewPlane.ViewDepthPlane,
                PlanViewPlane.TopClipPlane,
            })
            {
                range.SetLevelId(plane, PlanViewRange.Current);
            }

            range.SetOffset(PlanViewPlane.TopClipPlane, UnitUtils.ConvertToInternalUnits(TopClipMm, UnitTypeId.Millimeters));
            range.SetOffset(PlanViewPlane.CutPlane, UnitUtils.ConvertToInternalUnits(CutPlaneMm, UnitTypeId.Millimeters));
            range.SetOffset(PlanViewPlane.BottomClipPlane, 0.0);
            range.SetOffset(PlanViewPlane.ViewDepthPlane, 0.0);
            planTemplate.SetViewRange(range);

            try
            {
                planTemplate.SetUnderlayBaseLevel(ElementId.InvalidElementId);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                // Ignore if template has no underlay control.
            }
        }
    }

    private static void ApplyFiltersToViewTemplate(
        View template,
        TemplateViewTemplatePlan vtPlan,
        IReadOnlyDictionary<string, TemplateQaFilterPlan> qaByName,
        IReadOnlyDictionary<string, ElementId> qaFilterIds,
        ElementId solidFillId,
        List<string> warnings)
    {
        HashSet<long> desiredIds = new();
        foreach (string filterName in vtPlan.FilterNames)
        {
            if (qaFilterIds.TryGetValue(filterName, out ElementId? id) && id != null)
            {
                desiredIds.Add(id.Value);
            }
        }

        // Remove any extraneous filters so the template matches rte.json deterministically.
        foreach (ElementId existingId in template.GetFilters().ToList())
        {
            if (!desiredIds.Contains(existingId.Value))
            {
                template.RemoveFilter(existingId);
            }
        }

        ICollection<ElementId> currentIds = template.GetFilters();
        HashSet<long> currentSet = new(currentIds.Select(id => id.Value));

        foreach (string filterName in vtPlan.FilterNames)
        {
            if (!qaFilterIds.TryGetValue(filterName, out ElementId? filterId) || filterId is null)
            {
                warnings.Add($"View template '{vtPlan.Name}' references missing QA filter '{filterName}'.");
                continue;
            }

            if (!currentSet.Contains(filterId.Value))
            {
                template.AddFilter(filterId);
                currentSet.Add(filterId.Value);
            }

            TemplateQaFilterPlan filterPlan = qaByName[filterName];
            // Enforce KZ-VIEW-004: QA filters never hide elements.
            template.SetFilterVisibility(filterId, filterPlan.Visible);
            template.SetIsFilterEnabled(filterId, true);

            Color color = new(filterPlan.Red, filterPlan.Green, filterPlan.Blue);
            if (color.IsValid)
            {
                OverrideGraphicSettings ogs = new();
                ogs.SetProjectionLineColor(color);
                if (solidFillId != ElementId.InvalidElementId)
                {
                    ogs.SetSurfaceForegroundPatternId(solidFillId);
                    ogs.SetSurfaceForegroundPatternColor(color);
                    ogs.SetSurfaceForegroundPatternVisible(true);
                }

                template.SetFilterOverrides(filterId, ogs);
            }
        }
    }

    private static void HideSubcategoriesOnView(Document doc, View view, IReadOnlyList<string> subcategoryNames)
    {
        HashSet<string> hideSet = new(subcategoryNames, StringComparer.Ordinal);
        foreach (Category parent in doc.Settings.Categories)
        {
            if (parent?.SubCategories is null)
            {
                continue;
            }

            foreach (Category sub in parent.SubCategories)
            {
                if (sub != null &&
                    hideSet.Contains(sub.Name ?? string.Empty) &&
                    view.CanCategoryBeHidden(sub.Id))
                {
                    view.SetCategoryHidden(sub.Id, true);
                }
            }
        }
    }

    private static void EnsureSchedules(
        Document doc,
        IReadOnlyList<TemplateSchedulePlan> schedules,
        IReadOnlyDictionary<string, ElementId> sharedParamIds,
        List<string> log,
        List<string> warnings)
    {
        foreach (TemplateSchedulePlan schedPlan in schedules)
        {
            if (string.Equals(schedPlan.Category, TemplateBuildPlanBuilder.ForbiddenScheduleCategory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Refused to create forbidden OST_Levels schedule '{schedPlan.Name}'.");
            }

            ElementId categoryId = ResolveScheduleCategoryId(schedPlan.Category);
            if (categoryId != ElementId.InvalidElementId && !ViewSchedule.IsValidCategoryForSchedule(categoryId))
            {
                throw new InvalidOperationException(
                    $"Category '{schedPlan.Category}' is not schedulable in Revit for schedule '{schedPlan.Name}'.");
            }

            ViewSchedule? schedule = new FilteredElementCollector(doc)
                .OfClass(typeof(ViewSchedule))
                .Cast<ViewSchedule>()
                .FirstOrDefault(s => !s.IsTemplate && string.Equals(s.Name, schedPlan.Name, StringComparison.Ordinal));

            if (schedule is null)
            {
                schedule = ViewSchedule.CreateSchedule(doc, categoryId);
                schedule.Name = schedPlan.Name;
            }

            ScheduleDefinition def = schedule.Definition;
            IList<SchedulableField> schedulable = def.GetSchedulableFields();

            Dictionary<string, ScheduleFieldId> fieldIdsByParamName = new(StringComparer.Ordinal);
            HashSet<long> existingParamIds = new();
            int existingCount = def.GetFieldCount();
            for (int i = 0; i < existingCount; i++)
            {
                ScheduleField f = def.GetField(i);
                if (f.ParameterId != ElementId.InvalidElementId)
                {
                    existingParamIds.Add(f.ParameterId.Value);
                    if (doc.GetElement(f.ParameterId) is SharedParameterElement shared)
                    {
                        string pName = shared.GetDefinition()?.Name ?? shared.Name ?? string.Empty;
                        if (pName.Length > 0 && !fieldIdsByParamName.ContainsKey(pName))
                        {
                            fieldIdsByParamName[pName] = f.FieldId;
                        }
                    }
                }
            }

            if (existingCount == 0)
            {
                if (string.IsNullOrEmpty(schedPlan.Category))
                {
                    TryAddBuiltInScheduleField(def, schedulable, BuiltInParameter.ELEM_CATEGORY_PARAM, existingParamIds);
                }

                TryAddBuiltInScheduleField(def, schedulable, BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM, existingParamIds);
            }

            foreach (string fieldName in schedPlan.Fields)
            {
                if (fieldIdsByParamName.ContainsKey(fieldName))
                {
                    continue;
                }

                if (!sharedParamIds.TryGetValue(fieldName, out ElementId? paramId) || paramId is null)
                {
                    warnings.Add($"Schedule '{schedPlan.Name}' skipped unbound parameter '{fieldName}'.");
                    continue;
                }

                SchedulableField? match = schedulable.FirstOrDefault(sf => sf.ParameterId == paramId);
                if (match is null)
                {
                    warnings.Add($"Schedule '{schedPlan.Name}' parameter '{fieldName}' is not schedulable on category '{schedPlan.Category}'.");
                    continue;
                }

                ScheduleField added = def.AddField(match);
                existingParamIds.Add(paramId.Value);
                fieldIdsByParamName[fieldName] = added.FieldId;
            }

            // Replace schedule filters deterministically to match rte.json.
            def.ClearFilters();
            foreach (ManifestFilterRuleEntry filterRule in schedPlan.Filters)
            {
                if (!fieldIdsByParamName.TryGetValue(filterRule.Parameter, out ScheduleFieldId? fieldId) || fieldId is null)
                {
                    warnings.Add($"Schedule '{schedPlan.Name}' filter references missing field '{filterRule.Parameter}'.");
                    continue;
                }

                ScheduleFilter? sf = CreateRevitScheduleFilter(fieldId, filterRule.Operator, filterRule.Value);
                if (sf != null)
                {
                    def.AddFilter(sf);
                }
                else
                {
                    warnings.Add($"Schedule '{schedPlan.Name}' uses unsupported filter operator '{filterRule.Operator}'.");
                }
            }
        }

        log.Add($"Ensured {schedules.Count} review and exception schedules (zero OST_Levels schedules).");
    }

    private static ScheduleFilter? CreateRevitScheduleFilter(ScheduleFieldId fieldId, string op, string value)
    {
        switch (op)
        {
            case "isEmpty":
                return new ScheduleFilter(fieldId, ScheduleFilterType.HasNoValue);
            case "hasValue":
                return new ScheduleFilter(fieldId, ScheduleFilterType.HasValue);
            case "equals":
                return new ScheduleFilter(fieldId, ScheduleFilterType.Equal, value ?? string.Empty);
            case "notEquals":
                return new ScheduleFilter(fieldId, ScheduleFilterType.NotEqual, value ?? string.Empty);
            case "contains":
                return new ScheduleFilter(fieldId, ScheduleFilterType.Contains, value ?? string.Empty);
            default:
                return null;
        }
    }

    private static void EnsureStartingView(
        Document doc,
        TemplateStartingViewPlan startPlan,
        List<string> log,
        List<string> warnings)
    {
        ViewDrafting? startView = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewDrafting))
            .Cast<ViewDrafting>()
            .FirstOrDefault(v => !v.IsTemplate && string.Equals(v.Name, startPlan.Name, StringComparison.Ordinal));

        if (startView is null)
        {
            ElementId draftingTypeId = FindViewFamilyTypeId(doc, ViewFamily.Drafting);
            startView = ViewDrafting.Create(doc, draftingTypeId);
            startView.Name = startPlan.Name;
            startView.Scale = 1;
        }

        // Remove any prior notes on this view so rebuilds are idempotent.
        List<ElementId> existingNotes = new FilteredElementCollector(doc, startView.Id)
            .OfClass(typeof(TextNote))
            .ToElementIds()
            .ToList();
        if (existingNotes.Count > 0)
        {
            doc.Delete(existingNotes);
        }

        ElementId textNoteTypeId = new FilteredElementCollector(doc)
            .OfClass(typeof(TextNoteType))
            .FirstElementId();

        if (textNoteTypeId != ElementId.InvalidElementId)
        {
            TextNoteOptions noteOptions = new(textNoteTypeId)
            {
                HorizontalAlignment = HorizontalTextAlignment.Left,
            };

            string banner = $"{startPlan.TitleEn}\n{startPlan.TitleJa}\nStarter Kit v{startPlan.KitVersion} | Kanizu GIS Standard v{startPlan.GisStandardVersion}";
            double yFeet = 0.0;
            double widthFeet = UnitUtils.ConvertToInternalUnits(260.0, UnitTypeId.Millimeters);
            double stepYFeet = UnitUtils.ConvertToInternalUnits(55.0, UnitTypeId.Millimeters);

            TextNote.Create(doc, startView.Id, new XYZ(0, yFeet, 0), widthFeet, banner, noteOptions);
            yFeet -= UnitUtils.ConvertToInternalUnits(30.0, UnitTypeId.Millimeters);

            foreach (StartingViewTextSection section in startPlan.Sections)
            {
                string block = $"{section.HeadingEn} / {section.HeadingJa}\n{section.BodyEn}\n\n{section.BodyJa}";
                TextNote.Create(doc, startView.Id, new XYZ(0, yFeet, 0), widthFeet, block, noteOptions);
                yFeet -= stepYFeet;
            }
        }
        else
        {
            warnings.Add("Seed template has no TextNoteType; 00_Start_Here Drafting View created without text notes.");
        }

        StartingViewSettings settings = StartingViewSettings.GetStartingViewSettings(doc);
        if (settings.IsAcceptableStartingView(startView.Id))
        {
            settings.ViewId = startView.Id;
            log.Add($"Set bilingual EN/JA Drafting View '{startPlan.Name}' as Starting View.");
        }
        else
        {
            warnings.Add($"Revit rejected '{startPlan.Name}' as Starting View.");
        }
    }

    private static List<string> ResolveBuiltFamilyPaths(
        TemplateBuildPlan plan,
        TemplateBuildOptions options,
        IReadOnlyList<FamilyBuildResult>? builtFamilies)
    {
        List<string> paths = new();
        if (builtFamilies != null && builtFamilies.Count > 0)
        {
            foreach (FamilyBuildResult result in builtFamilies)
            {
                if (!string.IsNullOrWhiteSpace(result.OutputPath) && File.Exists(result.OutputPath))
                {
                    paths.Add(Path.GetFullPath(result.OutputPath));
                }
            }

            return paths;
        }

        if (!string.IsNullOrWhiteSpace(options.BuiltFamiliesDirectory) && Directory.Exists(options.BuiltFamiliesDirectory))
        {
            foreach (TemplateLoadedFamilyPlan fam in plan.LoadedFamilies)
            {
                string candidate = Path.Combine(options.BuiltFamiliesDirectory!, fam.RfaFileName);
                if (File.Exists(candidate))
                {
                    paths.Add(Path.GetFullPath(candidate));
                }
            }
        }

        return paths;
    }

    private static ElementId EnsureAtLeastOneLevel(Document doc)
    {
        ElementId existing = new FilteredElementCollector(doc).OfClass(typeof(Level)).FirstElementId();
        if (existing != ElementId.InvalidElementId)
        {
            return existing;
        }

        Level created = Level.Create(doc, 0.0);
        created.Name = "1F";
        return created.Id;
    }

    private static ElementId FindViewFamilyTypeId(Document doc, ViewFamily family)
    {
        ViewFamilyType? vft = new FilteredElementCollector(doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(t => t.ViewFamily == family);

        return vft?.Id
            ?? throw new InvalidOperationException($"Seed template has no ViewFamilyType for '{family}'.");
    }

    private static View? FindViewTemplateByName(Document doc, string name) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .FirstOrDefault(v => v.IsTemplate && string.Equals(v.Name, name, StringComparison.Ordinal));

    private static FloorType FindBaseFloorType(Document doc)
    {
        List<FloorType> candidates = new FilteredElementCollector(doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Where(t => !t.IsFoundationSlab && t.GetCompoundStructure() != null)
            .ToList();

        return candidates.FirstOrDefault()
            ?? throw new InvalidOperationException("Seed template has no non-foundation FloorType with CompoundStructure.");
    }

    private static void SetTopLayerMaterial(FloorType floorType, ElementId materialId, double? thicknessMm)
    {
        CompoundStructure? structure = floorType.GetCompoundStructure();
        if (structure == null || structure.LayerCount == 0)
        {
            return;
        }

        if (thicknessMm.HasValue && thicknessMm.Value > 0)
        {
            double feet = UnitUtils.ConvertToInternalUnits(thicknessMm.Value, UnitTypeId.Millimeters);
            CompoundStructure single = CompoundStructure.CreateSingleLayerCompoundStructure(
                MaterialFunctionAssignment.Structure,
                feet,
                materialId);
            floorType.SetCompoundStructure(single);
            return;
        }

        structure.SetMaterialId(0, materialId);
        floorType.SetCompoundStructure(structure);
    }

    private static ElementId FindSolidDraftingFillPatternId(Document doc) =>
        new FilteredElementCollector(doc)
            .OfClass(typeof(FillPatternElement))
            .Cast<FillPatternElement>()
            .FirstOrDefault(p => p.GetFillPattern() is { IsSolidFill: true, Target: FillPatternTarget.Drafting })
            ?.Id ?? ElementId.InvalidElementId;

    private static Dictionary<string, ElementId> MapSharedParameterIdsByName(Document doc)
    {
        Dictionary<Guid, ElementId> byGuid = new();
        foreach (SharedParameterElement shared in new FilteredElementCollector(doc)
            .OfClass(typeof(SharedParameterElement))
            .Cast<SharedParameterElement>())
        {
            if (!byGuid.ContainsKey(shared.GuidValue))
            {
                byGuid[shared.GuidValue] = shared.Id;
            }
        }

        Dictionary<string, ElementId> byName = new(StringComparer.Ordinal);
        foreach (GisParameterDefinition def in GisSharedParameters.All)
        {
            if (byGuid.TryGetValue(def.Guid, out ElementId? id) && id != null)
            {
                byName[def.Name] = id;
            }
        }

        return byName;
    }

    private static List<ElementId> ResolveBuiltInCategoryIds(IReadOnlyList<string> categoryNames, List<string> warnings)
    {
        List<ElementId> ids = new();
        foreach (string name in categoryNames)
        {
            if (Enum.TryParse(name, ignoreCase: false, out BuiltInCategory bic) && bic != BuiltInCategory.INVALID)
            {
                ids.Add(new ElementId(bic));
            }
            else
            {
                warnings.Add($"Unknown BuiltInCategory '{name}'.");
            }
        }

        return ids;
    }

    private static ElementId ResolveScheduleCategoryId(string categoryName)
    {
        if (string.IsNullOrWhiteSpace(categoryName))
        {
            return ElementId.InvalidElementId;
        }

        if (Enum.TryParse(categoryName, ignoreCase: false, out BuiltInCategory bic) && bic != BuiltInCategory.INVALID)
        {
            return new ElementId(bic);
        }

        throw new InvalidOperationException($"Unknown Revit schedule category '{categoryName}'.");
    }

    private static ViewDetailLevel ParseDetailLevel(string detailLevel)
    {
        if (string.Equals(detailLevel, "Coarse", StringComparison.OrdinalIgnoreCase))
        {
            return ViewDetailLevel.Coarse;
        }

        if (string.Equals(detailLevel, "Fine", StringComparison.OrdinalIgnoreCase))
        {
            return ViewDetailLevel.Fine;
        }

        return ViewDetailLevel.Medium;
    }

    private static void TryAddBuiltInScheduleField(
        ScheduleDefinition def,
        IList<SchedulableField> schedulable,
        BuiltInParameter builtIn,
        HashSet<long> existingParamIds)
    {
        ElementId targetId = new(builtIn);
        if (existingParamIds.Contains(targetId.Value))
        {
            return;
        }

        SchedulableField? match = schedulable.FirstOrDefault(f => f.ParameterId == targetId);
        if (match != null)
        {
            def.AddField(match);
            existingParamIds.Add(targetId.Value);
        }
    }

    private static void SetSharedTextParameter(Element element, string parameterName, string value)
    {
        Parameter? param = GisSharedParameters.TryGet(parameterName, out GisParameterDefinition def)
            ? element.get_Parameter(def.Guid) ?? element.LookupParameter(parameterName)
            : element.LookupParameter(parameterName);

        if (param is { IsReadOnly: false, StorageType: StorageType.String })
        {
            param.Set(value ?? string.Empty);
        }
    }

    private static bool ClearSharedTextIfPresent(Element element, string parameterName)
    {
        Parameter? param = GisSharedParameters.TryGet(parameterName, out GisParameterDefinition def)
            ? element.get_Parameter(def.Guid) ?? element.LookupParameter(parameterName)
            : element.LookupParameter(parameterName);

        if (param is { IsReadOnly: false, StorageType: StorageType.String } &&
            !string.IsNullOrWhiteSpace(param.AsString()))
        {
            param.Set(string.Empty);
            return true;
        }

        return false;
    }

    private static void SetElementIdBuiltInParameter(Element element, BuiltInParameter builtIn, ElementId value)
    {
        Parameter? param = element.get_Parameter(builtIn);
        if (param is { IsReadOnly: false, StorageType: StorageType.ElementId })
        {
            param.Set(value);
        }
    }

    private static void RunTransaction(Document doc, string name, Action work)
    {
        using Transaction tx = new(doc, name);
        tx.Start();
        try
        {
            work();
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

    private sealed class OverwriteFamilyLoadOptions : IFamilyLoadOptions
    {
        public bool OnFamilyFound(bool familyInUse, out bool overwriteParameterValues)
        {
            overwriteParameterValues = true;
            return true;
        }

        public bool OnSharedFamilyFound(Family sharedFamily, bool familyInUse, out FamilySource source, out bool overwriteParameterValues)
        {
            source = FamilySource.Family;
            overwriteParameterValues = true;
            return true;
        }
    }
}
