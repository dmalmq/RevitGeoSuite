using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>Options controlling <see cref="TemplateBuildPlanBuilder.Build"/>.</summary>
public sealed class TemplateBuildPlanOptions
{
    public static TemplateBuildPlanOptions Default => new();

    /// <summary>When true (default), includes optional families such as <c>KNZ_POI_FacilityMarker</c>.</summary>
    public bool IncludeOptionalFamilies { get; set; } = true;
}

/// <summary>Metric project units and Project Information defaults stamped on the starter <c>.rte</c>.</summary>
public sealed class TemplateUnitsAndDefaultsPlan
{
    public TemplateUnitsAndDefaultsPlan(
        string lengthUnit,
        double lengthAccuracyMm,
        string areaUnit,
        double areaAccuracySqM,
        string angleUnit,
        double angleAccuracyDeg,
        string modelRole,
        string kitVersion,
        int gisStandardVersion,
        bool requireZeroProjectIdentity,
        bool requireZeroWorksets,
        IReadOnlyList<string> forbiddenIdentityFields)
    {
        LengthUnit = lengthUnit ?? string.Empty;
        LengthAccuracyMm = lengthAccuracyMm;
        AreaUnit = areaUnit ?? string.Empty;
        AreaAccuracySqM = areaAccuracySqM;
        AngleUnit = angleUnit ?? string.Empty;
        AngleAccuracyDeg = angleAccuracyDeg;
        ModelRole = modelRole ?? string.Empty;
        KitVersion = kitVersion ?? string.Empty;
        GisStandardVersion = gisStandardVersion;
        RequireZeroProjectIdentity = requireZeroProjectIdentity;
        RequireZeroWorksets = requireZeroWorksets;
        ForbiddenIdentityFields = forbiddenIdentityFields ?? Array.Empty<string>();
    }

    public string LengthUnit { get; }

    public double LengthAccuracyMm { get; }

    public string AreaUnit { get; }

    public double AreaAccuracySqM { get; }

    public string AngleUnit { get; }

    public double AngleAccuracyDeg { get; }

    public string ModelRole { get; }

    public string KitVersion { get; }

    public int GisStandardVersion { get; }

    public bool RequireZeroProjectIdentity { get; }

    public bool RequireZeroWorksets { get; }

    public IReadOnlyList<string> ForbiddenIdentityFields { get; }
}

/// <summary>One frozen Kanizu shared parameter binding planned for the starter <c>.rte</c>.</summary>
public sealed class TemplateSharedParameterPlan
{
    public TemplateSharedParameterPlan(
        string name,
        Guid guid,
        string binding,
        string dataType,
        IReadOnlyList<string> categories,
        bool userModifiable,
        bool allowVaryBetweenGroups,
        string description)
    {
        Name = name ?? string.Empty;
        Guid = guid;
        Binding = binding ?? string.Empty;
        DataType = dataType ?? string.Empty;
        Categories = categories ?? Array.Empty<string>();
        UserModifiable = userModifiable;
        AllowVaryBetweenGroups = allowVaryBetweenGroups;
        Description = description ?? string.Empty;
    }

    public string Name { get; }

    public Guid Guid { get; }

    public string Binding { get; }

    public string DataType { get; }

    public IReadOnlyList<string> Categories { get; }

    public bool UserModifiable { get; }

    public bool AllowVaryBetweenGroups { get; }

    public string Description { get; }
}

/// <summary>One color entry inside the <c>GIS Area</c> color fill scheme plan.</summary>
public sealed class TemplateAreaColorEntry
{
    public TemplateAreaColorEntry(
        string areaKey,
        string materialName,
        string labelEn,
        string labelJa,
        string fillHex,
        byte red,
        byte green,
        byte blue)
    {
        AreaKey = areaKey ?? string.Empty;
        MaterialName = materialName ?? string.Empty;
        LabelEn = labelEn ?? string.Empty;
        LabelJa = labelJa ?? string.Empty;
        FillHex = (fillHex ?? string.Empty).ToUpperInvariant();
        Red = red;
        Green = green;
        Blue = blue;
    }

    public string AreaKey { get; }

    public string MaterialName { get; }

    public string LabelEn { get; }

    public string LabelJa { get; }

    public string FillHex { get; }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }
}

/// <summary>Area scheme (<c>GIS Area</c>) and <c>GIS_&lt;area&gt;</c> color fill scheme plan.</summary>
public sealed class TemplateAreaColorSchemePlan
{
    public const string DefaultAreaSchemeName = "GIS Area";
    public const string DefaultColorSchemeName = "GIS Area Colors";

    public TemplateAreaColorSchemePlan(
        string areaSchemeName,
        string colorSchemeName,
        string outlineHex,
        IReadOnlyList<TemplateAreaColorEntry> entries)
    {
        AreaSchemeName = areaSchemeName ?? string.Empty;
        ColorSchemeName = colorSchemeName ?? string.Empty;
        OutlineHex = (outlineHex ?? string.Empty).ToUpperInvariant();
        Entries = entries ?? Array.Empty<TemplateAreaColorEntry>();
    }

    public string AreaSchemeName { get; }

    public string ColorSchemeName { get; }

    public string OutlineHex { get; }

    public IReadOnlyList<TemplateAreaColorEntry> Entries { get; }
}

/// <summary>One material planned for the starter <c>.rte</c> (either a 3D <c>K3D_*</c>/<c>M3D_*</c> or a frozen 2D <c>GIS_*</c> material).</summary>
public sealed class TemplateMaterialPlan
{
    public TemplateMaterialPlan(
        string name,
        string role,
        bool is3DStarterMaterial,
        string hex,
        byte red,
        byte green,
        byte blue,
        int transparency,
        string alphaMode,
        double metallic,
        double roughness,
        bool applySolidDraftingFill,
        string description)
    {
        Name = name ?? string.Empty;
        Role = role ?? string.Empty;
        Is3DStarterMaterial = is3DStarterMaterial;
        Hex = (hex ?? string.Empty).ToUpperInvariant();
        Red = red;
        Green = green;
        Blue = blue;
        Transparency = transparency;
        AlphaMode = alphaMode ?? string.Empty;
        Metallic = metallic;
        Roughness = roughness;
        ApplySolidDraftingFill = applySolidDraftingFill;
        Description = description ?? string.Empty;
    }

    public string Name { get; }

    public string Role { get; }

    public bool Is3DStarterMaterial { get; }

    public string Hex { get; }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }

    public int Transparency { get; }

    public string AlphaMode { get; }

    public double Metallic { get; }

    public double Roughness { get; }

    public bool ApplySolidDraftingFill { get; }

    public string Description { get; }
}

/// <summary>One <c>GIS_*</c> line style planned for the starter <c>.rte</c>.</summary>
public sealed class TemplateLineStylePlan
{
    public TemplateLineStylePlan(
        string name,
        int lineWeight,
        string colorHex,
        byte red,
        byte green,
        byte blue,
        string linePattern,
        string purpose)
    {
        Name = name ?? string.Empty;
        LineWeight = lineWeight;
        ColorHex = (colorHex ?? string.Empty).ToUpperInvariant();
        Red = red;
        Green = green;
        Blue = blue;
        LinePattern = linePattern ?? string.Empty;
        Purpose = purpose ?? string.Empty;
    }

    public string Name { get; }

    public int LineWeight { get; }

    public string ColorHex { get; }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }

    public string LinePattern { get; }

    public string Purpose { get; }
}

/// <summary>One pre-loaded system family type planned for the starter <c>.rte</c>.</summary>
public sealed class TemplateSystemFamilyPlan
{
    public TemplateSystemFamilyPlan(
        string id,
        string typeName,
        string systemKind,
        string category,
        string materialRef,
        string? gisCategory,
        int? triangleBudgetPerFlight,
        double? thicknessMm,
        double? defaultHeightMm,
        string description)
    {
        Id = id ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        SystemKind = systemKind ?? string.Empty;
        Category = category ?? string.Empty;
        MaterialRef = materialRef ?? string.Empty;
        GisCategory = gisCategory;
        TriangleBudgetPerFlight = triangleBudgetPerFlight;
        ThicknessMm = thicknessMm;
        DefaultHeightMm = defaultHeightMm;
        Description = description ?? string.Empty;
    }

    public string Id { get; }

    public string TypeName { get; }

    public string SystemKind { get; }

    public string Category { get; }

    public string MaterialRef { get; }

    public string? GisCategory { get; }

    public int? TriangleBudgetPerFlight { get; }

    public double? ThicknessMm { get; }

    public double? DefaultHeightMm { get; }

    public string Description { get; }
}

/// <summary>One QA parameter view filter planned for the starter <c>.rte</c>.</summary>
public sealed class TemplateQaFilterPlan
{
    public TemplateQaFilterPlan(
        string name,
        string severity,
        bool visible,
        string overrideColorHex,
        byte red,
        byte green,
        byte blue,
        string? secondaryColorHex,
        IReadOnlyList<string> categories,
        IReadOnlyList<ManifestFilterRuleEntry> rules,
        IReadOnlyDictionary<string, string> valueColors,
        string description)
    {
        Name = name ?? string.Empty;
        Severity = severity ?? string.Empty;
        Visible = visible;
        OverrideColorHex = (overrideColorHex ?? string.Empty).ToUpperInvariant();
        Red = red;
        Green = green;
        Blue = blue;
        SecondaryColorHex = string.IsNullOrWhiteSpace(secondaryColorHex)
            ? null
            : secondaryColorHex!.Trim().ToUpperInvariant();
        Categories = categories ?? Array.Empty<string>();
        Rules = rules ?? Array.Empty<ManifestFilterRuleEntry>();
        ValueColors = valueColors ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.Ordinal));
        Description = description ?? string.Empty;
    }

    public string Name { get; }

    public string Severity { get; }

    public bool Visible { get; }

    public string OverrideColorHex { get; }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }

    public string? SecondaryColorHex { get; }

    public IReadOnlyList<string> Categories { get; }

    public IReadOnlyList<ManifestFilterRuleEntry> Rules { get; }

    public IReadOnlyDictionary<string, string> ValueColors { get; }

    public string Description { get; }
}

/// <summary>One unified view template planned for the starter <c>.rte</c>.</summary>
public sealed class TemplateViewTemplatePlan
{
    public TemplateViewTemplatePlan(
        string name,
        string viewType,
        string detailLevel,
        bool isCanonical2DExport,
        bool allowVisibilityHidingFilters,
        bool hasVisibilityHidingFilters,
        IReadOnlyList<string> filterNames,
        IReadOnlyList<string> visibleCategories,
        IReadOnlyList<string> visibleLineStylePrefixes,
        bool hideNonGisLineStyles,
        IReadOnlyList<string> hideSubcategories)
    {
        Name = name ?? string.Empty;
        ViewType = viewType ?? string.Empty;
        DetailLevel = detailLevel ?? string.Empty;
        IsCanonical2DExport = isCanonical2DExport;
        AllowVisibilityHidingFilters = allowVisibilityHidingFilters;
        HasVisibilityHidingFilters = hasVisibilityHidingFilters;
        FilterNames = filterNames ?? Array.Empty<string>();
        VisibleCategories = visibleCategories ?? Array.Empty<string>();
        VisibleLineStylePrefixes = visibleLineStylePrefixes ?? Array.Empty<string>();
        HideNonGisLineStyles = hideNonGisLineStyles;
        HideSubcategories = hideSubcategories ?? Array.Empty<string>();
    }

    public string Name { get; }

    public string ViewType { get; }

    public string DetailLevel { get; }

    public bool IsCanonical2DExport { get; }

    public bool AllowVisibilityHidingFilters { get; }

    public bool HasVisibilityHidingFilters { get; }

    public IReadOnlyList<string> FilterNames { get; }

    public IReadOnlyList<string> VisibleCategories { get; }

    public IReadOnlyList<string> VisibleLineStylePrefixes { get; }

    public bool HideNonGisLineStyles { get; }

    public IReadOnlyList<string> HideSubcategories { get; }
}

/// <summary>One review or empty-means-pass exception schedule planned for the starter <c>.rte</c>.</summary>
public sealed class TemplateSchedulePlan
{
    public TemplateSchedulePlan(
        string id,
        string name,
        string kind,
        string category,
        bool emptyMeansPass,
        IReadOnlyList<string> fields,
        bool hasGisKindFilter,
        IReadOnlyList<ManifestFilterRuleEntry> filters)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        Kind = kind ?? string.Empty;
        Category = category ?? string.Empty;
        EmptyMeansPass = emptyMeansPass;
        Fields = fields ?? Array.Empty<string>();
        HasGisKindFilter = hasGisKindFilter;
        Filters = filters ?? Array.Empty<ManifestFilterRuleEntry>();
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    /// <summary>Revit BuiltInCategory name (e.g. <c>OST_Floors</c>), or empty for multi-category. Never <c>OST_Levels</c>.</summary>
    public string Category { get; }

    public bool EmptyMeansPass { get; }

    public IReadOnlyList<string> Fields { get; }

    public bool HasGisKindFilter { get; }

    public IReadOnlyList<ManifestFilterRuleEntry> Filters { get; }
}

/// <summary>One bilingual text section placed onto the <c>00_Start_Here</c> Drafting View.</summary>
public sealed class StartingViewTextSection
{
    public StartingViewTextSection(string headingEn, string headingJa, string bodyEn, string bodyJa)
    {
        HeadingEn = headingEn ?? string.Empty;
        HeadingJa = headingJa ?? string.Empty;
        BodyEn = bodyEn ?? string.Empty;
        BodyJa = bodyJa ?? string.Empty;
    }

    public string HeadingEn { get; }

    public string HeadingJa { get; }

    public string BodyEn { get; }

    public string BodyJa { get; }
}

/// <summary>Bilingual EN/JA <c>00_Start_Here</c> Drafting View plan set as Revit's Starting View.</summary>
public sealed class TemplateStartingViewPlan
{
    public TemplateStartingViewPlan(
        string name,
        string viewType,
        bool bilingual,
        IReadOnlyList<string> languages,
        string titleEn,
        string titleJa,
        string kitVersion,
        int gisStandardVersion,
        IReadOnlyList<StartingViewTextSection> sections)
    {
        Name = name ?? string.Empty;
        ViewType = viewType ?? string.Empty;
        Bilingual = bilingual;
        Languages = languages ?? Array.Empty<string>();
        TitleEn = titleEn ?? string.Empty;
        TitleJa = titleJa ?? string.Empty;
        KitVersion = kitVersion ?? string.Empty;
        GisStandardVersion = gisStandardVersion;
        Sections = sections ?? Array.Empty<StartingViewTextSection>();
    }

    public string Name { get; }

    public string ViewType { get; }

    public bool Bilingual { get; }

    public IReadOnlyList<string> Languages { get; }

    public string TitleEn { get; }

    public string TitleJa { get; }

    public string KitVersion { get; }

    public int GisStandardVersion { get; }

    public IReadOnlyList<StartingViewTextSection> Sections { get; }

    /// <summary>Renders the full bilingual EN/JA plain-text note content for Drafting View notes and <c>START_HERE.txt</c>.</summary>
    public string RenderBilingualText()
    {
        List<string> lines = new()
        {
            $"{TitleEn}",
            $"{TitleJa}",
            $"Starter Kit v{KitVersion} | Kanizu GIS Standard v{GisStandardVersion}",
            new string('=', 72),
            string.Empty,
        };

        foreach (StartingViewTextSection section in Sections)
        {
            lines.Add($"[{section.HeadingEn} / {section.HeadingJa}]");
            lines.Add(section.BodyEn);
            lines.Add(section.BodyJa);
            lines.Add(string.Empty);
        }

        return string.Join("\n", lines);
    }
}

/// <summary>One Starter Kit <c>.rfa</c> family scheduled to be loaded into the starter <c>.rte</c>.</summary>
public sealed class TemplateLoadedFamilyPlan
{
    public TemplateLoadedFamilyPlan(
        string id,
        string familyName,
        string rfaFileName,
        string category,
        string seedKind,
        bool optional)
    {
        Id = id ?? string.Empty;
        FamilyName = familyName ?? string.Empty;
        RfaFileName = rfaFileName ?? string.Empty;
        Category = category ?? string.Empty;
        SeedKind = seedKind ?? string.Empty;
        Optional = optional;
    }

    public string Id { get; }

    public string FamilyName { get; }

    public string RfaFileName { get; }

    public string Category { get; }

    public string SeedKind { get; }

    public bool Optional { get; }
}

/// <summary>
/// Complete, deterministic, Revit-API-free build plan for the Kanizu Starter Kit <c>.rte</c> template.
/// Consumed directly by Revit's <c>TemplateBuilder</c> and verified in Linux CI against <see cref="ExpectedManifest"/>.
/// </summary>
public sealed class TemplateBuildPlan
{
    public TemplateBuildPlan(
        string templateName,
        string kitVersion,
        int gisStandardVersion,
        IReadOnlyList<string> supportedRevitVersions,
        string canonicalExportViewTemplate,
        bool includeOptionalFamilies,
        TemplateUnitsAndDefaultsPlan unitsAndDefaults,
        IReadOnlyList<TemplateSharedParameterPlan> sharedParameters,
        TemplateAreaColorSchemePlan areaColorScheme,
        IReadOnlyList<TemplateMaterialPlan> materials3D,
        IReadOnlyList<TemplateMaterialPlan> gis2DMaterials,
        IReadOnlyList<StandardFloorType> standardFloorTypes,
        IReadOnlyList<TemplateLineStylePlan> lineStyles,
        IReadOnlyList<TemplateSystemFamilyPlan> systemFamilies,
        IReadOnlyList<TemplateQaFilterPlan> qaViewFilters,
        IReadOnlyList<TemplateViewTemplatePlan> viewTemplates,
        IReadOnlyList<TemplateSchedulePlan> schedules,
        TemplateStartingViewPlan startingView,
        IReadOnlyList<TemplateLoadedFamilyPlan> loadedFamilies)
    {
        TemplateName = templateName ?? string.Empty;
        KitVersion = kitVersion ?? string.Empty;
        GisStandardVersion = gisStandardVersion;
        SupportedRevitVersions = supportedRevitVersions ?? Array.Empty<string>();
        CanonicalExportViewTemplate = canonicalExportViewTemplate ?? string.Empty;
        IncludeOptionalFamilies = includeOptionalFamilies;
        UnitsAndDefaults = unitsAndDefaults ?? throw new ArgumentNullException(nameof(unitsAndDefaults));
        SharedParameters = sharedParameters ?? Array.Empty<TemplateSharedParameterPlan>();
        AreaColorScheme = areaColorScheme ?? throw new ArgumentNullException(nameof(areaColorScheme));
        Materials3D = materials3D ?? Array.Empty<TemplateMaterialPlan>();
        Gis2DMaterials = gis2DMaterials ?? Array.Empty<TemplateMaterialPlan>();
        StandardFloorTypes = standardFloorTypes ?? Array.Empty<StandardFloorType>();
        LineStyles = lineStyles ?? Array.Empty<TemplateLineStylePlan>();
        SystemFamilies = systemFamilies ?? Array.Empty<TemplateSystemFamilyPlan>();
        QaViewFilters = qaViewFilters ?? Array.Empty<TemplateQaFilterPlan>();
        ViewTemplates = viewTemplates ?? Array.Empty<TemplateViewTemplatePlan>();
        Schedules = schedules ?? Array.Empty<TemplateSchedulePlan>();
        StartingView = startingView ?? throw new ArgumentNullException(nameof(startingView));
        LoadedFamilies = loadedFamilies ?? Array.Empty<TemplateLoadedFamilyPlan>();
    }

    public string TemplateName { get; }

    public string KitVersion { get; }

    public int GisStandardVersion { get; }

    public IReadOnlyList<string> SupportedRevitVersions { get; }

    public string CanonicalExportViewTemplate { get; }

    public bool IncludeOptionalFamilies { get; }

    public TemplateUnitsAndDefaultsPlan UnitsAndDefaults { get; }

    public IReadOnlyList<TemplateSharedParameterPlan> SharedParameters { get; }

    public TemplateAreaColorSchemePlan AreaColorScheme { get; }

    public IReadOnlyList<TemplateMaterialPlan> Materials3D { get; }

    public IReadOnlyList<TemplateMaterialPlan> Gis2DMaterials { get; }

    public IReadOnlyList<StandardFloorType> StandardFloorTypes { get; }

    public IReadOnlyList<TemplateLineStylePlan> LineStyles { get; }

    public IReadOnlyList<TemplateSystemFamilyPlan> SystemFamilies { get; }

    public IReadOnlyList<TemplateQaFilterPlan> QaViewFilters { get; }

    public IReadOnlyList<TemplateViewTemplatePlan> ViewTemplates { get; }

    public IReadOnlyList<TemplateSchedulePlan> Schedules { get; }

    public TemplateStartingViewPlan StartingView { get; }

    public IReadOnlyList<TemplateLoadedFamilyPlan> LoadedFamilies { get; }

    /// <summary>
    /// Projects this compiled <see cref="TemplateBuildPlan"/> plus a set of built <see cref="ManifestFamilyEntry"/>
    /// records into a canonical <see cref="ExpectedManifest"/> for pure-C# round-trip parity testing.
    /// </summary>
    public ExpectedManifest ToSimulatedManifest(IEnumerable<ManifestFamilyEntry> familyEntries)
    {
        if (familyEntries is null)
        {
            throw new ArgumentNullException(nameof(familyEntries));
        }

        return new ExpectedManifest(
            KitVersion,
            GisStandardVersion,
            SupportedRevitVersions,
            CanonicalExportViewTemplate,
            StartingView.Name,
            StartingView.ViewType,
            StartingView.Bilingual,
            UnitsAndDefaults.RequireZeroProjectIdentity,
            UnitsAndDefaults.RequireZeroWorksets,
            hasProjectIdentity: false,
            worksetCount: 0,
            ViewTemplates.Select(vt => new ManifestViewTemplateEntry(
                vt.Name,
                vt.ViewType,
                vt.DetailLevel,
                vt.IsCanonical2DExport,
                vt.HasVisibilityHidingFilters,
                vt.FilterNames,
                vt.HideNonGisLineStyles,
                vt.HideSubcategories)),
            QaViewFilters.Select(f => new ManifestQaFilterEntry(
                f.Name,
                f.Severity,
                f.Visible,
                f.OverrideColorHex,
                f.Categories,
                f.SecondaryColorHex,
                f.Rules,
                f.ValueColors)),
            Schedules.Select(s => new ManifestScheduleEntry(
                s.Id,
                s.Name,
                s.Kind,
                s.Category,
                s.EmptyMeansPass,
                s.Fields,
                s.HasGisKindFilter,
                s.Filters)),
            LineStyles.Select(ls => new ManifestLineStyleEntry(
                ls.Name,
                ls.LineWeight,
                ls.ColorHex,
                ls.LinePattern)),
            Materials3D.Select(m => new ManifestMaterialEntry(
                m.Name,
                m.Role,
                m.Hex,
                m.Transparency,
                m.AlphaMode)),
            SystemFamilies.Select(sf => new ManifestSystemFamilyEntry(
                sf.Id,
                sf.TypeName,
                sf.SystemKind,
                sf.Category,
                sf.MaterialRef)),
            SharedParameters.Select(p => new ManifestSharedParameterEntry(
                p.Name,
                p.Guid,
                p.Binding,
                p.DataType,
                p.Categories)),
            familyEntries);
    }
}

/// <summary>
/// Pure-C# deterministic planner consuming a validated <see cref="StarterKitSpec"/> and Kanizu GIS standards
/// to produce a complete <see cref="TemplateBuildPlan"/> for <c>TemplateBuilder</c>.
/// </summary>
public static class TemplateBuildPlanBuilder
{
    public const string ForbiddenScheduleCategory = "OST_Levels";
    public const string CanonicalExportTemplateName = TemplateStandardDefinitions.ExportPlanViewTemplateName;

    public static TemplateBuildPlan Build(
        StarterKitSpec spec,
        TemplateBuildPlanOptions? options = null,
        StarterKitReferenceStandards? standards = null,
        FloorTypeStandard? floorTypeStandard = null,
        KanizuColorTheme? colorTheme = null)
    {
        if (spec is null)
        {
            throw new ArgumentNullException(nameof(spec));
        }

        TemplateBuildPlanOptions opts = options ?? TemplateBuildPlanOptions.Default;
        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();
        FloorTypeStandard floorStd = floorTypeStandard ?? FloorTypeStandard.LoadEmbedded();
        KanizuColorTheme theme = colorTheme ?? KanizuColorTheme.LoadEmbedded();

        EnforceHardInvariants(spec);

        TemplateUnitsAndDefaultsPlan unitsAndDefaults = new(
            lengthUnit: "mm",
            lengthAccuracyMm: 1.0,
            areaUnit: "m2",
            areaAccuracySqM: 0.01,
            angleUnit: "deg",
            angleAccuracyDeg: 0.01,
            modelRole: ShowcaseExportGuard.AuthoringModelRole,
            kitVersion: spec.Header.KitVersion,
            gisStandardVersion: spec.Header.GisStandardVersion,
            requireZeroProjectIdentity: spec.Rte.Hygiene.RequireZeroProjectIdentity,
            requireZeroWorksets: spec.Rte.Hygiene.RequireZeroWorksets,
            forbiddenIdentityFields: Array.AsReadOnly(
                spec.Rte.Hygiene.ForbiddenIdentityFields
                    .OrderBy(f => f, StringComparer.Ordinal)
                    .ToArray()));

        IReadOnlyList<TemplateSharedParameterPlan> sharedParameters = BuildSharedParameters(refStandards);
        TemplateAreaColorSchemePlan areaColorScheme = BuildAreaColorScheme(theme, floorStd);
        IReadOnlyList<TemplateMaterialPlan> materials3D = BuildMaterials3D(spec.Materials3D);
        IReadOnlyList<TemplateMaterialPlan> gis2DMaterials = BuildGis2DMaterials(floorStd);
        IReadOnlyList<StandardFloorType> standardFloorTypes = spec.Rte.IncludeAllStandardFloorTypes
            ? Array.AsReadOnly(floorStd.FloorTypes.ToArray())
            : Array.AsReadOnly(Array.Empty<StandardFloorType>());
        IReadOnlyList<TemplateLineStylePlan> lineStyles = BuildLineStyles(spec.Rte.LineStyles);
        IReadOnlyList<TemplateSystemFamilyPlan> systemFamilies = BuildSystemFamilies(spec.Rte.SystemFamilies);
        IReadOnlyList<TemplateQaFilterPlan> qaFilters = BuildQaFilters(spec.Rte.QaViewFilters);
        IReadOnlyList<TemplateViewTemplatePlan> viewTemplates = BuildViewTemplates(spec.Rte.ViewTemplates, qaFilters);
        IReadOnlyList<TemplateSchedulePlan> schedules = BuildSchedules(spec.Rte.Schedules);
        IReadOnlyList<TemplateLoadedFamilyPlan> loadedFamilies = BuildLoadedFamilies(spec, opts.IncludeOptionalFamilies);
        TemplateStartingViewPlan startingView = BuildStartingView(
            spec,
            viewTemplates,
            qaFilters,
            schedules,
            loadedFamilies);

        return new TemplateBuildPlan(
            spec.Rte.TemplateName,
            spec.Header.KitVersion,
            spec.Header.GisStandardVersion,
            Array.AsReadOnly(spec.Header.SupportedRevitVersions.OrderBy(v => v, StringComparer.Ordinal).ToArray()),
            spec.Rte.CanonicalExportViewTemplate,
            opts.IncludeOptionalFamilies,
            unitsAndDefaults,
            sharedParameters,
            areaColorScheme,
            materials3D,
            gis2DMaterials,
            standardFloorTypes,
            lineStyles,
            systemFamilies,
            qaFilters,
            viewTemplates,
            schedules,
            startingView,
            loadedFamilies);
    }

    private static void EnforceHardInvariants(StarterKitSpec spec)
    {
        if (!string.Equals(spec.Rte.CanonicalExportViewTemplate, CanonicalExportTemplateName, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Starter Kit canonical 2D export view template must be '{CanonicalExportTemplateName}', found '{spec.Rte.CanonicalExportViewTemplate}'.");
        }

        if (!spec.Rte.ViewTemplates.Any(vt =>
                string.Equals(vt.Name, CanonicalExportTemplateName, StringComparison.Ordinal) &&
                vt.IsCanonical2DExport))
        {
            throw new InvalidOperationException(
                $"Starter Kit rte.json must declare canonical 2D export view template '{CanonicalExportTemplateName}'.");
        }

        foreach (ScheduleSpec schedule in spec.Rte.Schedules)
        {
            if (string.Equals(schedule.Category, ForbiddenScheduleCategory, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Schedule '{schedule.Name}' targets forbidden unschedulable category '{ForbiddenScheduleCategory}'. " +
                    "Revit ViewSchedule.CreateSchedule does not support OST_Levels.");
            }
        }

        foreach (ViewTemplateSpec vt in spec.Rte.ViewTemplates)
        {
            if (vt.AllowVisibilityHidingFilters)
            {
                throw new InvalidOperationException(
                    $"View template '{vt.Name}' must keep allowVisibilityHidingFilters=false to satisfy KZ-VIEW-004.");
            }
        }

        foreach (QaViewFilterSpec filter in spec.Rte.QaViewFilters)
        {
            if (!filter.Visible)
            {
                throw new InvalidOperationException(
                    $"QA view filter '{filter.Name}' must have visible=true to satisfy KZ-VIEW-004.");
            }
        }
    }

    private static IReadOnlyList<TemplateSharedParameterPlan> BuildSharedParameters(StarterKitReferenceStandards refStandards)
    {
        HashSet<string> varyByGroup = new(
            GisSharedParameters.VaryByGroupInstance(refStandards.ParametersByName.Values),
            StringComparer.Ordinal);

        return Array.AsReadOnly(
            refStandards.ParametersByName.Values
                .OrderBy(p => p.Name, StringComparer.Ordinal)
                .Select(p => new TemplateSharedParameterPlan(
                    p.Name,
                    p.Guid,
                    p.Binding == GisParameterBinding.Type ? "type" : "instance",
                    p.DataType == GisParameterDataType.Integer ? "integer" : "text",
                    Array.AsReadOnly(p.Categories.OrderBy(c => c, StringComparer.Ordinal).ToArray()),
                    p.UserModifiable,
                    varyByGroup.Contains(p.Name),
                    p.Description))
                .ToArray());
    }

    private static TemplateAreaColorSchemePlan BuildAreaColorScheme(KanizuColorTheme theme, FloorTypeStandard floorStd)
    {
        Dictionary<string, KanizuThemeArea> areasByKey = theme.Areas
            .ToDictionary(a => a.Key, a => a, StringComparer.Ordinal);

        List<TemplateAreaColorEntry> entries = new();
        foreach (StandardMaterial mat in floorStd.Materials)
        {
            (byte r, byte g, byte b) = GisToolStandard.HexToRgb(mat.FillHex);
            string labelEn = areasByKey.TryGetValue(mat.Area, out KanizuThemeArea? area) && area != null
                ? area.NameEn
                : "Other";
            string labelJa = !string.IsNullOrWhiteSpace(mat.NameJa)
                ? mat.NameJa
                : (area?.NameJa ?? "その他");

            entries.Add(new TemplateAreaColorEntry(
                mat.Area,
                mat.Name,
                labelEn,
                labelJa,
                mat.FillHex,
                r,
                g,
                b));
        }

        return new TemplateAreaColorSchemePlan(
            TemplateAreaColorSchemePlan.DefaultAreaSchemeName,
            TemplateAreaColorSchemePlan.DefaultColorSchemeName,
            theme.OutlineHex,
            Array.AsReadOnly(entries.ToArray()));
    }

    private static IReadOnlyList<TemplateMaterialPlan> BuildMaterials3D(Materials3DSpec spec)
    {
        return Array.AsReadOnly(
            spec.Materials
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m =>
                {
                    (byte r, byte g, byte b) = m.Srgb.Count == 3
                        ? ((byte)m.Srgb[0], (byte)m.Srgb[1], (byte)m.Srgb[2])
                        : GisToolStandard.HexToRgb(m.Hex);
                    return new TemplateMaterialPlan(
                        m.Name,
                        m.Role,
                        is3DStarterMaterial: true,
                        m.Hex,
                        r,
                        g,
                        b,
                        m.Transparency,
                        m.AlphaMode,
                        m.Metallic,
                        m.Roughness,
                        applySolidDraftingFill: false,
                        m.Description);
                })
                .ToArray());
    }

    private static IReadOnlyList<TemplateMaterialPlan> BuildGis2DMaterials(FloorTypeStandard floorStd)
    {
        return Array.AsReadOnly(
            floorStd.Materials
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m =>
                {
                    (byte r, byte g, byte b) = GisToolStandard.HexToRgb(m.FillHex);
                    return new TemplateMaterialPlan(
                        m.Name,
                        m.Area,
                        is3DStarterMaterial: false,
                        m.FillHex,
                        r,
                        g,
                        b,
                        transparency: 0,
                        alphaMode: "opaque",
                        metallic: 0.0,
                        roughness: 0.9,
                        applySolidDraftingFill: true,
                        $"Frozen Kanizu 2D GIS floor material ({m.NameJa}).");
                })
                .ToArray());
    }

    private static IReadOnlyList<TemplateLineStylePlan> BuildLineStyles(IReadOnlyList<LineStyleSpec> specs)
    {
        return Array.AsReadOnly(
            specs
                .OrderBy(ls => ls.Name, StringComparer.Ordinal)
                .Select(ls =>
                {
                    (byte r, byte g, byte b) = GisToolStandard.HexToRgb(ls.ColorHex);
                    return new TemplateLineStylePlan(
                        ls.Name,
                        ls.LineWeight,
                        ls.ColorHex,
                        r,
                        g,
                        b,
                        ls.LinePattern,
                        ls.Purpose);
                })
                .ToArray());
    }

    private static IReadOnlyList<TemplateSystemFamilyPlan> BuildSystemFamilies(IReadOnlyList<SystemFamilySpec> specs)
    {
        return Array.AsReadOnly(
            specs
                .OrderBy(sf => sf.Id, StringComparer.Ordinal)
                .Select(sf => new TemplateSystemFamilyPlan(
                    sf.Id,
                    sf.TypeName,
                    sf.SystemKind,
                    sf.Category,
                    sf.MaterialRef,
                    sf.GisCategory,
                    sf.TriangleBudgetPerFlight,
                    sf.ThicknessMm,
                    sf.DefaultHeightMm,
                    sf.Description))
                .ToArray());
    }

    private static IReadOnlyList<TemplateQaFilterPlan> BuildQaFilters(IReadOnlyList<QaViewFilterSpec> specs)
    {
        return Array.AsReadOnly(
            specs
                .OrderBy(f => f.Name, StringComparer.Ordinal)
                .Select(f =>
                {
                    (byte r, byte g, byte b) = GisToolStandard.HexToRgb(f.OverrideColorHex);
                    IReadOnlyList<ManifestFilterRuleEntry> rules = ExpectedManifest.SortRules(
                        f.Rules.Select(rule => new ManifestFilterRuleEntry(rule.Parameter, rule.Operator, rule.Value)));
                    Dictionary<string, string> valueColors = new(StringComparer.Ordinal);
                    foreach (KeyValuePair<string, string> kv in f.ValueColors.OrderBy(e => e.Key, StringComparer.Ordinal))
                    {
                        valueColors[kv.Key] = (kv.Value ?? string.Empty).Trim().ToUpperInvariant();
                    }

                    return new TemplateQaFilterPlan(
                        f.Name,
                        f.Severity,
                        f.Visible,
                        f.OverrideColorHex,
                        r,
                        g,
                        b,
                        f.SecondaryColorHex,
                        ExpectedManifest.SortStrings(f.Categories),
                        rules,
                        new ReadOnlyDictionary<string, string>(valueColors),
                        f.Description);
                })
                .ToArray());
    }

    private static IReadOnlyList<TemplateViewTemplatePlan> BuildViewTemplates(
        IReadOnlyList<ViewTemplateSpec> specs,
        IReadOnlyList<TemplateQaFilterPlan> qaFilters)
    {
        Dictionary<string, TemplateQaFilterPlan> filtersByName = qaFilters
            .ToDictionary(f => f.Name, f => f, StringComparer.Ordinal);

        return Array.AsReadOnly(
            specs
                .OrderBy(vt => vt.Name, StringComparer.Ordinal)
                .Select(vt =>
                {
                    bool hasHideFilter = vt.Filters.Any(name =>
                        filtersByName.TryGetValue(name, out TemplateQaFilterPlan? f) && f != null && !f.Visible);

                    return new TemplateViewTemplatePlan(
                        vt.Name,
                        vt.ViewType,
                        vt.DetailLevel,
                        vt.IsCanonical2DExport,
                        vt.AllowVisibilityHidingFilters,
                        hasHideFilter,
                        ExpectedManifest.SortStrings(vt.Filters),
                        ExpectedManifest.SortStrings(vt.VisibleCategories),
                        ExpectedManifest.SortStrings(vt.VisibleLineStylePrefixes),
                        vt.HideNonGisLineStyles,
                        ExpectedManifest.SortStrings(vt.HideSubcategories));
                })
                .ToArray());
    }

    private static IReadOnlyList<TemplateSchedulePlan> BuildSchedules(IReadOnlyList<ScheduleSpec> specs)
    {
        return Array.AsReadOnly(
            specs
                .OrderBy(s => s.Name, StringComparer.Ordinal)
                .Select(s =>
                {
                    if (string.Equals(s.Category, ForbiddenScheduleCategory, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"Schedule '{s.Name}' targets forbidden category '{ForbiddenScheduleCategory}'.");
                    }

                    IReadOnlyList<ManifestFilterRuleEntry> filters = ExpectedManifest.SortRules(
                        s.Filters.Select(r => new ManifestFilterRuleEntry(r.Parameter, r.Operator, r.Value)));
                    bool hasGisKindFilter = filters.Any(r =>
                        string.Equals(r.Parameter, GisSharedParameters.GisKind, StringComparison.Ordinal) &&
                        string.Equals(r.Operator, "hasValue", StringComparison.Ordinal));

                    return new TemplateSchedulePlan(
                        s.Id,
                        s.Name,
                        s.Kind,
                        s.Category,
                        s.EmptyMeansPass,
                        ExpectedManifest.SortStrings(s.Fields),
                        hasGisKindFilter,
                        filters);
                })
                .ToArray());
    }

    private static IReadOnlyList<TemplateLoadedFamilyPlan> BuildLoadedFamilies(
        StarterKitSpec spec,
        bool includeOptionalFamilies)
    {
        Dictionary<string, StarterKitFamilyRegistryEntry> registryById = spec.Header.Families
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        IReadOnlyList<FamilyGeometrySpec> selected = StarterKitCatalogSelector.SelectCatalogSpecs(
            spec,
            includeOptionalFamilies);

        return Array.AsReadOnly(
            selected
                .Select(f =>
                {
                    bool optional = registryById.TryGetValue(f.Id, out StarterKitFamilyRegistryEntry? reg) && reg != null && reg.Optional;
                    return new TemplateLoadedFamilyPlan(
                        f.Id,
                        f.FamilyName,
                        f.FamilyName + ".rfa",
                        f.Category,
                        f.SeedKind,
                        optional);
                })
                .ToArray());
    }

    private static TemplateStartingViewPlan BuildStartingView(
        StarterKitSpec spec,
        IReadOnlyList<TemplateViewTemplatePlan> viewTemplates,
        IReadOnlyList<TemplateQaFilterPlan> qaFilters,
        IReadOnlyList<TemplateSchedulePlan> schedules,
        IReadOnlyList<TemplateLoadedFamilyPlan> loadedFamilies)
    {
        string templateNames = string.Join(", ", viewTemplates.Select(v => v.Name));
        string exceptionSchedules = string.Join(", ", schedules.Where(s => s.EmptyMeansPass).Select(s => s.Name));

        List<StartingViewTextSection> sections = new()
        {
            new StartingViewTextSection(
                "1. Quick-Start Checklist",
                "1. クイックスタートチェックリスト",
                "1) Create a new project from this .rte template (never Save-As an existing station .rvt).\n" +
                "2) Run Kanizu GIS -> Georeference Setup & New Kanizu Project to assign a unique ModelId, EPSG CRS, and level_id/ordinal/short_name.\n" +
                "3) Model floors using the pre-loaded 'j ..._床' floor types and place KNZ_* starter families.\n" +
                "4) Verify zero rows in the '✗' QA exception schedules and run Contractor Readiness Check before handover.",
                "1) 本 .rte テンプレートから新規プロジェクトを作成してください（既存駅の .rvt の別名保存は禁止です）。\n" +
                "2) Kanizu GIS の測地設定／新規プロジェクト初期化を実行し、固有の ModelId、EPSG 座標系、および各階の level_id・ordinal・short_name を設定してください。\n" +
                "3) 登録済みの標準床タイプ（j ..._床）と KNZ_* スターターファミリを使用してモデリングしてください。\n" +
                "4) 納品前に「✗」例外集計表が 0 件であることを確認し、レディネスチェックを実行してください。"),
            new StartingViewTextSection(
                "2. View Templates & Traffic-Light QA Filters",
                "2. ビューテンプレートと信号機 QA フィルタ",
                $"Included View Templates ({viewTemplates.Count}): {templateNames}.\n" +
                $"Canonical 2D Export Template: '{spec.Rte.CanonicalExportViewTemplate}' (Coarse detail, GIS_* line styles only, zero visibility-hiding filters).\n" +
                $"3D Export Template: 'Kanizu 3D Export' (Medium detail, K_Footprint subcategory hidden).\n" +
                $"QA Filters ({qaFilters.Count}): Red = blocking error, Amber = warning, Green = tagged & ready, Blue/Cream = KNZ_QA_INFO_GateSide_Check (rachi_nai / rachi_gai).",
                $"収録ビューテンプレート（{viewTemplates.Count} 種）: {templateNames}。\n" +
                $"2D エクスポート標準テンプレート: 「{spec.Rte.CanonicalExportViewTemplate}」（簡略詳細レベル、GIS_* 線種のみ表示、非表示フィルタ禁止）。\n" +
                $"3D エクスポート標準テンプレート: 「Kanizu 3D Export」（標準詳細レベル、K_Footprint サブカテゴリ非表示）。\n" +
                $"QA フィルタ（{qaFilters.Count} 種）: 赤＝必須エラー、橙＝警告、緑＝タグ済、青／クリーム＝改札内外チェック（rachi_nai / rachi_gai）。"),
            new StartingViewTextSection(
                "3. QA Schedules & Pre-Loaded Starter Families",
                "3. QA 集計表とプリロード済みスターターファミリ",
                $"Empty-means-pass exception schedules: {exceptionSchedules}.\n" +
                "Note: Revit does not support OST_Levels schedules; review level ordinal/short_name in the Kanizu GIS Readiness Panel.\n" +
                $"Pre-loaded KNZ_* starter families: {loadedFamilies.Count} (Coarse K_Footprint for 2D GIS + low-poly Medium/Fine for 3D Tiles).",
                $"0 件で合格となる例外集計表: {exceptionSchedules}。\n" +
                "注記: Revit の仕様上 OST_Levels は集計表にできないため、レベルの ordinal / short_name は Kanizu GIS レディネスパネルで確認してください。\n" +
                $"プリロード済み KNZ_* ファミリ: {loadedFamilies.Count} 種（簡略 K_Footprint＝2D GIS 用、標準・詳細＝軽量 3D Tiles 用）。"),
            new StartingViewTextSection(
                "4. Template Hygiene & Zero-Identity Policy",
                "4. テンプレート衛生ルール（ゼロ ID ポリシー）",
                "This .rte template carries ZERO project identity (no ModelId, no GisStationFolder, no level_id, no element id/gis_key) and ZERO worksets.\n" +
                "Enable worksharing only after creating your station .rvt project.",
                "本 .rte テンプレートにはプロジェクト固有 ID（ModelId・GisStationFolder・level_id・要素 id/gis_key）およびワークセットを一切含めていません。\n" +
                "ワークシェアリングは各駅の .rvt プロジェクト作成後に有効化してください。"),
        };

        return new TemplateStartingViewPlan(
            spec.Rte.StartingView.Name,
            spec.Rte.StartingView.ViewType,
            spec.Rte.StartingView.Bilingual,
            Array.AsReadOnly(spec.Rte.StartingView.Languages.ToArray()),
            spec.Rte.StartingView.TitleEn,
            spec.Rte.StartingView.TitleJa,
            spec.Header.KitVersion,
            spec.Header.GisStandardVersion,
            Array.AsReadOnly(sections.ToArray()));
    }
}

/// <summary>One observed hideable subcategory status on a parent category inside a Revit view template.</summary>
public sealed class SubcategoryVisibilityObservation
{
    public SubcategoryVisibilityObservation(string parentCategoryName, string subcategoryName, bool isHidden)
    {
        ParentCategoryName = parentCategoryName ?? string.Empty;
        SubcategoryName = subcategoryName ?? string.Empty;
        IsHidden = isHidden;
    }

    public string ParentCategoryName { get; }

    public string SubcategoryName { get; }

    public bool IsHidden { get; }
}

/// <summary>One observed QA view filter state on a single Revit view template.</summary>
public sealed class QaFilterTemplateObservation
{
    public QaFilterTemplateObservation(string templateName, bool visible, string overrideColorHex)
    {
        TemplateName = templateName ?? string.Empty;
        Visible = visible;
        OverrideColorHex = (overrideColorHex ?? string.Empty).Trim().ToUpperInvariant();
    }

    public string TemplateName { get; }

    public bool Visible { get; }

    public string OverrideColorHex { get; }
}

/// <summary>Aggregated QA filter state across all view templates where the filter is applied.</summary>
public sealed class AggregatedQaFilterObservation
{
    public AggregatedQaFilterObservation(bool visible, string overrideColorHex, bool hasColorDrift)
    {
        Visible = visible;
        OverrideColorHex = overrideColorHex ?? string.Empty;
        HasColorDrift = hasColorDrift;
    }

    public bool Visible { get; }

    public string OverrideColorHex { get; }

    public bool HasColorDrift { get; }
}

/// <summary>Companion Revit parameter filter definition synthesized for secondary <c>valueColors</c> entries.</summary>
public sealed class CompanionQaFilterDescriptor
{
    public CompanionQaFilterDescriptor(
        string baseFilterName,
        string companionFilterName,
        string parameterName,
        string value,
        string overrideColorHex,
        byte red,
        byte green,
        byte blue)
    {
        BaseFilterName = baseFilterName ?? string.Empty;
        CompanionFilterName = companionFilterName ?? string.Empty;
        ParameterName = parameterName ?? string.Empty;
        Value = value ?? string.Empty;
        OverrideColorHex = (overrideColorHex ?? string.Empty).Trim().ToUpperInvariant();
        Red = red;
        Green = green;
        Blue = blue;
    }

    public string BaseFilterName { get; }

    public string CompanionFilterName { get; }

    public string ParameterName { get; }

    public string Value { get; }

    public string OverrideColorHex { get; }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }
}

/// <summary>
/// Pure-C# verification &amp; aggregation helpers shared by Revit's <c>TemplateBuilder</c>,
/// <c>ManifestDumper</c>, and Linux CI unit tests.
/// </summary>
public static class TemplateManifestAuditHelpers
{
    public const string CompanionFilterSeparator = "__";

    /// <summary>
    /// Filters standalone built <see cref="ManifestFamilyEntry"/> items against the actual <c>Family</c> names
    /// loaded inside the reopened <c>.rte</c> document. Families absent from the reopened <c>.rte</c> are
    /// omitted so <see cref="StarterKitManifestComparer.Compare"/> catches missing preloaded families as diffs.
    /// </summary>
    public static IReadOnlyList<ManifestFamilyEntry> FilterLoadedFamilyEntries(
        IEnumerable<ManifestFamilyEntry>? builtEntries,
        IEnumerable<string>? loadedFamilyNamesInDocument)
    {
        if (builtEntries is null)
        {
            return Array.AsReadOnly(Array.Empty<ManifestFamilyEntry>());
        }

        HashSet<string> loadedSet = new(
            (loadedFamilyNamesInDocument ?? Array.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim()),
            StringComparer.OrdinalIgnoreCase);

        List<ManifestFamilyEntry> present = new();
        foreach (ManifestFamilyEntry entry in builtEntries)
        {
            if (entry != null && loadedSet.Contains(entry.FamilyName))
            {
                present.Add(entry);
            }
        }

        return Array.AsReadOnly(present.ToArray());
    }

    /// <summary>
    /// Returns only subcategory names that are hidden on EVERY hideable parent category that defines them.
    /// Partial hiding (e.g. hidden on 1 of 5 categories) returns empty so manifest verification fails.
    /// </summary>
    public static IReadOnlyList<string> ResolveFullyHiddenSubcategories(
        IEnumerable<SubcategoryVisibilityObservation>? observations)
    {
        if (observations is null)
        {
            return Array.AsReadOnly(Array.Empty<string>());
        }

        Dictionary<string, (int Total, int Hidden)> counts = new(StringComparer.Ordinal);
        foreach (SubcategoryVisibilityObservation obs in observations)
        {
            if (obs is null || string.IsNullOrWhiteSpace(obs.SubcategoryName))
            {
                continue;
            }

            string key = obs.SubcategoryName;
            counts.TryGetValue(key, out (int Total, int Hidden) current);
            counts[key] = (current.Total + 1, current.Hidden + (obs.IsHidden ? 1 : 0));
        }

        return Array.AsReadOnly(
            counts
                .Where(kv => kv.Value.Total > 0 && kv.Value.Hidden == kv.Value.Total)
                .Select(kv => kv.Key)
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToArray());
    }

    /// <summary>Builds the deterministic Revit companion filter name for a secondary value-color override.</summary>
    public static string GetCompanionFilterName(string baseFilterName, string value) =>
        $"{baseFilterName}{CompanionFilterSeparator}{value}";

    /// <summary>
    /// Parses a companion filter name (<c>&lt;BaseFilterName&gt;__&lt;value&gt;</c>) back into its base filter
    /// name and secondary value key.
    /// </summary>
    public static bool TryParseCompanionFilterName(string? filterName, out string baseFilterName, out string value)
    {
        if (!string.IsNullOrWhiteSpace(filterName))
        {
            int idx = filterName!.IndexOf(CompanionFilterSeparator, StringComparison.Ordinal);
            if (idx > 0 && idx + CompanionFilterSeparator.Length < filterName.Length)
            {
                baseFilterName = filterName.Substring(0, idx);
                value = filterName.Substring(idx + CompanionFilterSeparator.Length);
                return true;
            }
        }

        baseFilterName = string.Empty;
        value = string.Empty;
        return false;
    }

    /// <summary>
    /// Expands any secondary entries in <see cref="TemplateQaFilterPlan.ValueColors"/> (whose value differs
    /// from the primary <c>equals</c> rule value) into concrete Revit companion filter descriptors.
    /// </summary>
    public static IReadOnlyList<CompanionQaFilterDescriptor> BuildCompanionFilters(TemplateQaFilterPlan filterPlan)
    {
        if (filterPlan is null || filterPlan.ValueColors.Count <= 1)
        {
            return Array.AsReadOnly(Array.Empty<CompanionQaFilterDescriptor>());
        }

        ManifestFilterRuleEntry? primaryEquals = filterPlan.Rules.FirstOrDefault(r =>
            string.Equals(r.Operator, "equals", StringComparison.Ordinal));
        if (primaryEquals is null || string.IsNullOrWhiteSpace(primaryEquals.Parameter))
        {
            return Array.AsReadOnly(Array.Empty<CompanionQaFilterDescriptor>());
        }

        List<CompanionQaFilterDescriptor> companions = new();
        foreach (KeyValuePair<string, string> kv in filterPlan.ValueColors.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (string.Equals(kv.Key, primaryEquals.Value, StringComparison.Ordinal))
            {
                continue;
            }

            (byte r, byte g, byte b) = GisToolStandard.HexToRgb(kv.Value);
            companions.Add(new CompanionQaFilterDescriptor(
                filterPlan.Name,
                GetCompanionFilterName(filterPlan.Name, kv.Key),
                primaryEquals.Parameter,
                kv.Key,
                kv.Value,
                r,
                g,
                b));
        }

        return Array.AsReadOnly(companions.ToArray());
    }

    /// <summary>
    /// Aggregates a QA filter's visibility and override color across ALL view templates where it is applied.
    /// Visibility is <c>false</c> if ANY template hides the filter; color drift across templates is flagged
    /// explicitly so <see cref="StarterKitManifestComparer"/> catches drift on secondary templates.
    /// </summary>
    public static AggregatedQaFilterObservation AggregateFilterAcrossTemplates(
        IEnumerable<QaFilterTemplateObservation>? observations)
    {
        List<QaFilterTemplateObservation> list = (observations ?? Array.Empty<QaFilterTemplateObservation>())
            .Where(o => o != null)
            .ToList();
        if (list.Count == 0)
        {
            return new AggregatedQaFilterObservation(visible: true, overrideColorHex: string.Empty, hasColorDrift: false);
        }

        bool allVisible = list.All(o => o.Visible);
        List<string> distinctColors = list
            .Select(o => o.OverrideColorHex)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        if (distinctColors.Count == 1)
        {
            return new AggregatedQaFilterObservation(allVisible, distinctColors[0], hasColorDrift: false);
        }

        string driftReport = "INCONSISTENT:" + string.Join("|",
            list.OrderBy(o => o.TemplateName, StringComparer.Ordinal)
                .Select(o => $"{o.TemplateName}={(o.OverrideColorHex.Length > 0 ? o.OverrideColorHex : "<none>")}"));
        return new AggregatedQaFilterObservation(allVisible, driftReport, hasColorDrift: true);
    }

    /// <summary>
    /// Reconstructs <c>secondaryColorHex</c> and <c>valueColors</c> purely from inspected Revit filter rules
    /// and overrides (primary filter + any inspected companion filters), with zero echoing from <c>rte.json</c>.
    /// </summary>
    public static (string? SecondaryColorHex, IReadOnlyDictionary<string, string> ValueColors) ReconstructInspectedValueColors(
        IReadOnlyList<ManifestFilterRuleEntry> primaryRules,
        string primaryOverrideHex,
        IReadOnlyList<(string ValueKey, string OverrideHex, bool Visible, bool AppliedOnAllBaseTemplates)> inspectedCompanions)
    {
        Dictionary<string, string> valueColors = new(StringComparer.Ordinal);
        string? secondaryHex = null;

        if (inspectedCompanions != null && inspectedCompanions.Count > 0)
        {
            ManifestFilterRuleEntry? primaryEquals = primaryRules.FirstOrDefault(r =>
                string.Equals(r.Operator, "equals", StringComparison.Ordinal));
            if (primaryEquals != null && !string.IsNullOrWhiteSpace(primaryEquals.Value) && !string.IsNullOrWhiteSpace(primaryOverrideHex))
            {
                valueColors[primaryEquals.Value] = primaryOverrideHex.Trim().ToUpperInvariant();
            }

            foreach ((string valueKey, string overrideHex, bool visible, bool appliedOnAll) in inspectedCompanions
                .OrderBy(c => c.ValueKey, StringComparer.Ordinal))
            {
                if (!appliedOnAll || !visible || string.IsNullOrWhiteSpace(overrideHex))
                {
                    continue;
                }

                string normalizedHex = overrideHex.Trim().ToUpperInvariant();
                valueColors[valueKey] = normalizedHex;
                if (secondaryHex is null)
                {
                    secondaryHex = normalizedHex;
                }
            }
        }

        return (secondaryHex, new ReadOnlyDictionary<string, string>(valueColors));
    }
}
