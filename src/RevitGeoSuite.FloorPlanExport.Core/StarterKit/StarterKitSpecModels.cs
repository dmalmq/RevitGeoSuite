using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>Severity of a validation finding returned by <see cref="StarterKitSpecLoader"/>.</summary>
public enum StarterKitValidationSeverity
{
    Warning,
    Error,
}

/// <summary>One validation finding discovered while loading or cross-checking a Starter Kit specification.</summary>
public sealed class StarterKitValidationFinding
{
    public StarterKitValidationFinding(
        StarterKitValidationSeverity severity,
        string code,
        string path,
        string message)
    {
        Severity = severity;
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Path = path ?? string.Empty;
        Message = message ?? string.Empty;
    }

    public StarterKitValidationSeverity Severity { get; }

    public string Code { get; }

    public string Path { get; }

    public string Message { get; }

    public override string ToString() => $"[{Severity}] {Code} ({Path}): {Message}";
}

/// <summary>Aggregated result of validating a <see cref="StarterKitSpec"/> against Kanizu GIS standards.</summary>
public sealed class StarterKitValidationResult
{
    public StarterKitValidationResult(IEnumerable<StarterKitValidationFinding> findings)
    {
        List<StarterKitValidationFinding> list = (findings ?? Array.Empty<StarterKitValidationFinding>()).ToList();
        Findings = Array.AsReadOnly(list.ToArray());
        Errors = Array.AsReadOnly(list.Where(f => f.Severity == StarterKitValidationSeverity.Error).ToArray());
        Warnings = Array.AsReadOnly(list.Where(f => f.Severity == StarterKitValidationSeverity.Warning).ToArray());
    }

    public IReadOnlyList<StarterKitValidationFinding> Findings { get; }

    public IReadOnlyList<StarterKitValidationFinding> Errors { get; }

    public IReadOnlyList<StarterKitValidationFinding> Warnings { get; }

    public bool IsValid => Errors.Count == 0;
}

/// <summary>Baked anti-Z-fighting geometric clearances in millimetres.</summary>
public sealed class AntiZFightingClearances
{
    public const double MinimumSlabClearMm = 4.0;
    public const double MinimumSideClearMm = 8.0;
    public const double MinimumGlassTuckMm = 8.0;

    public AntiZFightingClearances(double slabClearMm, double sideClearMm, double glassTuckMm)
    {
        SlabClearMm = slabClearMm;
        SideClearMm = sideClearMm;
        GlassTuckMm = glassTuckMm;
    }

    public double SlabClearMm { get; }

    public double SideClearMm { get; }

    public double GlassTuckMm { get; }
}

/// <summary>File size and polygon budgets declared in <c>standards/starter-kit/kit.json</c>.</summary>
public sealed class StarterKitBudgets
{
    public StarterKitBudgets(
        int maxRfaFileSizeKb,
        int maxRteFileSizeMb,
        int maxCoarseTrianglesPerFamily,
        int maxMediumTrianglesPerFamily,
        int maxStairFlightTriangles,
        AntiZFightingClearances clearances)
    {
        MaxRfaFileSizeKb = maxRfaFileSizeKb;
        MaxRteFileSizeMb = maxRteFileSizeMb;
        MaxCoarseTrianglesPerFamily = maxCoarseTrianglesPerFamily;
        MaxMediumTrianglesPerFamily = maxMediumTrianglesPerFamily;
        MaxStairFlightTriangles = maxStairFlightTriangles;
        Clearances = clearances ?? throw new ArgumentNullException(nameof(clearances));
    }

    public int MaxRfaFileSizeKb { get; }

    public int MaxRteFileSizeMb { get; }

    public int MaxCoarseTrianglesPerFamily { get; }

    public int MaxMediumTrianglesPerFamily { get; }

    public int MaxStairFlightTriangles { get; }

    public AntiZFightingClearances Clearances { get; }
}

/// <summary>One family entry registered in <c>standards/starter-kit/kit.json</c>.</summary>
public sealed class StarterKitFamilyRegistryEntry
{
    public StarterKitFamilyRegistryEntry(
        string id,
        string specPath,
        string group,
        string phase,
        bool optional)
    {
        Id = id ?? string.Empty;
        SpecPath = specPath ?? string.Empty;
        Group = group ?? string.Empty;
        Phase = phase ?? string.Empty;
        Optional = optional;
    }

    public string Id { get; }

    public string SpecPath { get; }

    public string Group { get; }

    public string Phase { get; }

    public bool Optional { get; }
}

/// <summary>Root manifest of <c>standards/starter-kit/kit.json</c>.</summary>
public sealed class StarterKitHeaderSpec
{
    public const string ExpectedSchema = "kanizu.starter-kit";

    public StarterKitHeaderSpec(
        string schema,
        int schemaVersion,
        string kitVersion,
        int gisStandardVersion,
        IReadOnlyList<string> supportedRevitVersions,
        string rteSpec,
        string materialsSpec,
        StarterKitBudgets budgets,
        IReadOnlyList<StarterKitFamilyRegistryEntry> families)
    {
        Schema = schema ?? string.Empty;
        SchemaVersion = schemaVersion;
        KitVersion = kitVersion ?? string.Empty;
        GisStandardVersion = gisStandardVersion;
        SupportedRevitVersions = supportedRevitVersions ?? Array.Empty<string>();
        RteSpec = rteSpec ?? string.Empty;
        MaterialsSpec = materialsSpec ?? string.Empty;
        Budgets = budgets ?? throw new ArgumentNullException(nameof(budgets));
        Families = families ?? Array.Empty<StarterKitFamilyRegistryEntry>();
    }

    public string Schema { get; }

    public int SchemaVersion { get; }

    public string KitVersion { get; }

    public int GisStandardVersion { get; }

    public IReadOnlyList<string> SupportedRevitVersions { get; }

    public string RteSpec { get; }

    public string MaterialsSpec { get; }

    public StarterKitBudgets Budgets { get; }

    public IReadOnlyList<StarterKitFamilyRegistryEntry> Families { get; }
}

/// <summary>One 3D material definition in <c>standards/starter-kit/materials-3d.json</c>.</summary>
public sealed class Material3DSpec
{
    public Material3DSpec(
        string name,
        string role,
        IReadOnlyList<int> srgb,
        string hex,
        int transparency,
        string alphaMode,
        double metallic,
        double roughness,
        string description)
    {
        Name = name ?? string.Empty;
        Role = role ?? string.Empty;
        Srgb = srgb ?? Array.Empty<int>();
        Hex = hex ?? string.Empty;
        Transparency = transparency;
        AlphaMode = alphaMode ?? string.Empty;
        Metallic = metallic;
        Roughness = roughness;
        Description = description ?? string.Empty;
    }

    public string Name { get; }

    public string Role { get; }

    public IReadOnlyList<int> Srgb { get; }

    public string Hex { get; }

    public int Transparency { get; }

    public string AlphaMode { get; }

    public double Metallic { get; }

    public double Roughness { get; }

    public string Description { get; }
}

/// <summary>Parsed <c>standards/starter-kit/materials-3d.json</c> specification.</summary>
public sealed class Materials3DSpec
{
    public const string ExpectedSchema = "kanizu.starter-kit.materials-3d";

    public Materials3DSpec(
        string schema,
        int schemaVersion,
        IReadOnlyDictionary<string, string> alignmentNotes,
        IReadOnlyList<Material3DSpec> materials)
    {
        Schema = schema ?? string.Empty;
        SchemaVersion = schemaVersion;
        AlignmentNotes = alignmentNotes ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        Materials = materials ?? Array.Empty<Material3DSpec>();
    }

    public string Schema { get; }

    public int SchemaVersion { get; }

    public IReadOnlyDictionary<string, string> AlignmentNotes { get; }

    public IReadOnlyList<Material3DSpec> Materials { get; }
}

/// <summary>Starting view declaration in <c>standards/starter-kit/rte.json</c>.</summary>
public sealed class StartingViewSpec
{
    public StartingViewSpec(
        string name,
        string viewType,
        bool bilingual,
        IReadOnlyList<string> languages,
        string titleEn,
        string titleJa)
    {
        Name = name ?? string.Empty;
        ViewType = viewType ?? string.Empty;
        Bilingual = bilingual;
        Languages = languages ?? Array.Empty<string>();
        TitleEn = titleEn ?? string.Empty;
        TitleJa = titleJa ?? string.Empty;
    }

    public string Name { get; }

    public string ViewType { get; }

    public bool Bilingual { get; }

    public IReadOnlyList<string> Languages { get; }

    public string TitleEn { get; }

    public string TitleJa { get; }
}

/// <summary>Zero-identity and zero-workset assertions for the starter <c>.rte</c> template.</summary>
public sealed class RteHygieneSpec
{
    public RteHygieneSpec(
        bool requireZeroProjectIdentity,
        bool requireZeroWorksets,
        IReadOnlyList<string> forbiddenIdentityFields,
        IReadOnlyList<string> forbiddenScheduleCategories)
    {
        RequireZeroProjectIdentity = requireZeroProjectIdentity;
        RequireZeroWorksets = requireZeroWorksets;
        ForbiddenIdentityFields = forbiddenIdentityFields ?? Array.Empty<string>();
        ForbiddenScheduleCategories = forbiddenScheduleCategories ?? Array.Empty<string>();
    }

    public bool RequireZeroProjectIdentity { get; }

    public bool RequireZeroWorksets { get; }

    public IReadOnlyList<string> ForbiddenIdentityFields { get; }

    public IReadOnlyList<string> ForbiddenScheduleCategories { get; }
}

/// <summary>One declarative view template in <c>standards/starter-kit/rte.json</c>.</summary>
public sealed class ViewTemplateSpec
{
    public ViewTemplateSpec(
        string name,
        string viewType,
        string detailLevel,
        bool isCanonical2DExport,
        bool allowVisibilityHidingFilters,
        IReadOnlyList<string> filters,
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
        Filters = filters ?? Array.Empty<string>();
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

    public IReadOnlyList<string> Filters { get; }

    public IReadOnlyList<string> VisibleCategories { get; }

    public IReadOnlyList<string> VisibleLineStylePrefixes { get; }

    public bool HideNonGisLineStyles { get; }

    public IReadOnlyList<string> HideSubcategories { get; }
}

/// <summary>One parameter predicate rule used by a QA view filter or schedule filter.</summary>
public sealed class FilterRuleSpec
{
    public FilterRuleSpec(string parameter, string @operator, string value)
    {
        Parameter = parameter ?? string.Empty;
        Operator = @operator ?? string.Empty;
        Value = value ?? string.Empty;
    }

    public string Parameter { get; }

    public string Operator { get; }

    public string Value { get; }
}

/// <summary>One traffic-light QA view filter in <c>standards/starter-kit/rte.json</c>.</summary>
public sealed class QaViewFilterSpec
{
    public QaViewFilterSpec(
        string name,
        string severity,
        bool visible,
        string overrideColorHex,
        string? secondaryColorHex,
        IReadOnlyList<string> categories,
        IReadOnlyList<FilterRuleSpec> rules,
        IReadOnlyDictionary<string, string> valueColors,
        string description)
    {
        Name = name ?? string.Empty;
        Severity = severity ?? string.Empty;
        Visible = visible;
        OverrideColorHex = overrideColorHex ?? string.Empty;
        SecondaryColorHex = secondaryColorHex;
        Categories = categories ?? Array.Empty<string>();
        Rules = rules ?? Array.Empty<FilterRuleSpec>();
        ValueColors = valueColors ?? new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());
        Description = description ?? string.Empty;
    }

    public string Name { get; }

    public string Severity { get; }

    public bool Visible { get; }

    public string OverrideColorHex { get; }

    public string? SecondaryColorHex { get; }

    public IReadOnlyList<string> Categories { get; }

    public IReadOnlyList<FilterRuleSpec> Rules { get; }

    public IReadOnlyDictionary<string, string> ValueColors { get; }

    public string Description { get; }
}

/// <summary>One review or exception schedule declared in <c>standards/starter-kit/rte.json</c>.</summary>
public sealed class ScheduleSpec
{
    public ScheduleSpec(
        string id,
        string name,
        string kind,
        string category,
        bool emptyMeansPass,
        IReadOnlyList<string> fields,
        IReadOnlyList<FilterRuleSpec> filters)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        Kind = kind ?? string.Empty;
        Category = category ?? string.Empty;
        EmptyMeansPass = emptyMeansPass;
        Fields = fields ?? Array.Empty<string>();
        Filters = filters ?? Array.Empty<FilterRuleSpec>();
    }

    public string Id { get; }

    public string Name { get; }

    public string Kind { get; }

    /// <summary>Revit BuiltInCategory name (such as <c>OST_Floors</c>), or empty for multi-category.</summary>
    public string Category { get; }

    public bool EmptyMeansPass { get; }

    public IReadOnlyList<string> Fields { get; }

    public IReadOnlyList<FilterRuleSpec> Filters { get; }
}

/// <summary>One <c>GIS_*</c> line style declared in <c>standards/starter-kit/rte.json</c>.</summary>
public sealed class LineStyleSpec
{
    public LineStyleSpec(string name, int lineWeight, string colorHex, string linePattern, string purpose)
    {
        Name = name ?? string.Empty;
        LineWeight = lineWeight;
        ColorHex = colorHex ?? string.Empty;
        LinePattern = linePattern ?? string.Empty;
        Purpose = purpose ?? string.Empty;
    }

    public string Name { get; }

    public int LineWeight { get; }

    public string ColorHex { get; }

    public string LinePattern { get; }

    public string Purpose { get; }
}

/// <summary>One pre-loaded architectural system family declared in <c>standards/starter-kit/rte.json</c>.</summary>
public sealed class SystemFamilySpec
{
    public SystemFamilySpec(
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

/// <summary>Full declarative <c>.rte</c> blueprint parsed from <c>standards/starter-kit/rte.json</c>.</summary>
public sealed class RteTemplateSpec
{
    public const string ExpectedSchema = "kanizu.starter-kit.rte";

    public RteTemplateSpec(
        string schema,
        int schemaVersion,
        string templateName,
        int gisStandardVersion,
        string canonicalExportViewTemplate,
        StartingViewSpec startingView,
        RteHygieneSpec hygiene,
        IReadOnlyList<ViewTemplateSpec> viewTemplates,
        IReadOnlyList<QaViewFilterSpec> qaViewFilters,
        IReadOnlyList<ScheduleSpec> schedules,
        IReadOnlyList<LineStyleSpec> lineStyles,
        IReadOnlyList<SystemFamilySpec> systemFamilies,
        bool includeAllStandardFloorTypes,
        bool includeAllStandardSharedParameters)
    {
        Schema = schema ?? string.Empty;
        SchemaVersion = schemaVersion;
        TemplateName = templateName ?? string.Empty;
        GisStandardVersion = gisStandardVersion;
        CanonicalExportViewTemplate = canonicalExportViewTemplate ?? string.Empty;
        StartingView = startingView ?? throw new ArgumentNullException(nameof(startingView));
        Hygiene = hygiene ?? throw new ArgumentNullException(nameof(hygiene));
        ViewTemplates = viewTemplates ?? Array.Empty<ViewTemplateSpec>();
        QaViewFilters = qaViewFilters ?? Array.Empty<QaViewFilterSpec>();
        Schedules = schedules ?? Array.Empty<ScheduleSpec>();
        LineStyles = lineStyles ?? Array.Empty<LineStyleSpec>();
        SystemFamilies = systemFamilies ?? Array.Empty<SystemFamilySpec>();
        IncludeAllStandardFloorTypes = includeAllStandardFloorTypes;
        IncludeAllStandardSharedParameters = includeAllStandardSharedParameters;
    }

    public string Schema { get; }

    public int SchemaVersion { get; }

    public string TemplateName { get; }

    public int GisStandardVersion { get; }

    public string CanonicalExportViewTemplate { get; }

    public StartingViewSpec StartingView { get; }

    public RteHygieneSpec Hygiene { get; }

    public IReadOnlyList<ViewTemplateSpec> ViewTemplates { get; }

    public IReadOnlyList<QaViewFilterSpec> QaViewFilters { get; }

    public IReadOnlyList<ScheduleSpec> Schedules { get; }

    public IReadOnlyList<LineStyleSpec> LineStyles { get; }

    public IReadOnlyList<SystemFamilySpec> SystemFamilies { get; }

    public bool IncludeAllStandardFloorTypes { get; }

    public bool IncludeAllStandardSharedParameters { get; }
}

/// <summary>Nominal default dimensions for a procedural Starter Kit family.</summary>
public sealed class FamilyDimensionsSpec
{
    public FamilyDimensionsSpec(double widthMm, double depthMm, double heightMm)
    {
        WidthMm = widthMm;
        DepthMm = depthMm;
        HeightMm = heightMm;
    }

    public double WidthMm { get; }

    public double DepthMm { get; }

    public double HeightMm { get; }
}

/// <summary>One shared parameter declared by a Starter Kit family spec.</summary>
public sealed class FamilySharedParameterSpec
{
    public FamilySharedParameterSpec(string name, string binding, bool required, string defaultValue)
    {
        Name = name ?? string.Empty;
        Binding = binding ?? string.Empty;
        Required = required;
        DefaultValue = defaultValue ?? string.Empty;
    }

    public string Name { get; }

    public string Binding { get; }

    public bool Required { get; }

    public string DefaultValue { get; }
}

/// <summary>Coarse and Medium/Fine triangle ceilings for one family.</summary>
public sealed class FamilyTriangleBudgetSpec
{
    public FamilyTriangleBudgetSpec(int coarseMax, int mediumFineMax)
    {
        CoarseMax = coarseMax;
        MediumFineMax = mediumFineMax;
    }

    public int CoarseMax { get; }

    public int MediumFineMax { get; }
}

/// <summary>
/// One planar procedural geometry operation (<c>box</c>, <c>nprism</c>, <c>slopedPrism</c>, <c>array</c>, or <c>void</c>).
/// </summary>
public sealed class GeometryOpSpec
{
    public const string OpBox = "box";
    public const string OpNPrism = "nprism";
    public const string OpSlopedPrism = "slopedPrism";
    public const string OpArray = "array";
    public const string OpVoid = "void";

    public GeometryOpSpec(
        string op,
        string name,
        string? subcategory = null,
        string? materialRef = null,
        IReadOnlyList<double>? originMm = null,
        IReadOnlyList<double>? sizeMm = null,
        IReadOnlyList<double>? centerMm = null,
        double? radiusMm = null,
        double? heightMm = null,
        int? sides = null,
        double? widthMm = null,
        double? runMm = null,
        double? riseMm = null,
        double? thicknessMm = null,
        int? count = null,
        IReadOnlyList<double>? stepMm = null,
        GeometryOpSpec? child = null,
        IReadOnlyList<GeometryOpSpec>? children = null,
        string? shape = null,
        bool? throughCut = null,
        string? targetOp = null)
    {
        Op = op ?? string.Empty;
        Name = name ?? string.Empty;
        Subcategory = subcategory;
        MaterialRef = materialRef;
        OriginMm = originMm;
        SizeMm = sizeMm;
        CenterMm = centerMm;
        RadiusMm = radiusMm;
        HeightMm = heightMm;
        Sides = sides;
        WidthMm = widthMm;
        RunMm = runMm;
        RiseMm = riseMm;
        ThicknessMm = thicknessMm;
        Count = count;
        StepMm = stepMm;
        Child = child;
        Children = children;
        Shape = shape;
        ThroughCut = throughCut;
        TargetOp = targetOp;
    }

    public string Op { get; }

    public string Name { get; }

    public string? Subcategory { get; }

    public string? MaterialRef { get; }

    public IReadOnlyList<double>? OriginMm { get; }

    public IReadOnlyList<double>? SizeMm { get; }

    public IReadOnlyList<double>? CenterMm { get; }

    public double? RadiusMm { get; }

    public double? HeightMm { get; }

    public int? Sides { get; }

    public double? WidthMm { get; }

    public double? RunMm { get; }

    public double? RiseMm { get; }

    public double? ThicknessMm { get; }

    public int? Count { get; }

    public IReadOnlyList<double>? StepMm { get; }

    public GeometryOpSpec? Child { get; }

    public IReadOnlyList<GeometryOpSpec>? Children { get; }

    public string? Shape { get; }

    public bool? ThroughCut { get; }

    public string? TargetOp { get; }
}

/// <summary>Coarse-level 2D footprint solid block (<c>K_Footprint</c>).</summary>
public sealed class CoarseGeometrySpec
{
    public const string FootprintSubcategory = "K_Footprint";

    public CoarseGeometrySpec(
        string subcategory,
        string materialRef,
        bool requiresDownwardPlanarBottomAtZero,
        IReadOnlyList<GeometryOpSpec> ops)
    {
        Subcategory = subcategory ?? string.Empty;
        MaterialRef = materialRef ?? string.Empty;
        RequiresDownwardPlanarBottomAtZero = requiresDownwardPlanarBottomAtZero;
        Ops = ops ?? Array.Empty<GeometryOpSpec>();
    }

    public string Subcategory { get; }

    public string MaterialRef { get; }

    public bool RequiresDownwardPlanarBottomAtZero { get; }

    public IReadOnlyList<GeometryOpSpec> Ops { get; }
}

/// <summary>Medium/Fine 3D architectural massing geometry block.</summary>
public sealed class MediumFineGeometrySpec
{
    public MediumFineGeometrySpec(IReadOnlyList<GeometryOpSpec> ops)
    {
        Ops = ops ?? Array.Empty<GeometryOpSpec>();
    }

    public IReadOnlyList<GeometryOpSpec> Ops { get; }
}

/// <summary>One Starter Kit procedural family specification (<c>standards/starter-kit/families/*.family.json</c>).</summary>
public sealed class FamilyGeometrySpec
{
    public const string ExpectedSchema = "kanizu.starter-kit.family";
    public const string SeedProcedural = "procedural";
    public const string SeedWallHostedDoor = "wallHostedDoor";

    public FamilyGeometrySpec(
        string schema,
        int schemaVersion,
        string id,
        string familyName,
        string displayNameEn,
        string displayNameJa,
        string group,
        string category,
        string seedKind,
        bool isPolygonal,
        IReadOnlyList<string> subcategories,
        AntiZFightingClearances clearancesMm,
        FamilyDimensionsSpec defaultDimensionsMm,
        IReadOnlyList<FamilySharedParameterSpec> sharedParameters,
        FamilyTriangleBudgetSpec triangleBudget,
        CoarseGeometrySpec? coarse,
        MediumFineGeometrySpec mediumFine)
    {
        Schema = schema ?? string.Empty;
        SchemaVersion = schemaVersion;
        Id = id ?? string.Empty;
        FamilyName = familyName ?? string.Empty;
        DisplayNameEn = displayNameEn ?? string.Empty;
        DisplayNameJa = displayNameJa ?? string.Empty;
        Group = group ?? string.Empty;
        Category = category ?? string.Empty;
        SeedKind = seedKind ?? string.Empty;
        IsPolygonal = isPolygonal;
        Subcategories = subcategories ?? Array.Empty<string>();
        ClearancesMm = clearancesMm ?? throw new ArgumentNullException(nameof(clearancesMm));
        DefaultDimensionsMm = defaultDimensionsMm ?? throw new ArgumentNullException(nameof(defaultDimensionsMm));
        SharedParameters = sharedParameters ?? Array.Empty<FamilySharedParameterSpec>();
        TriangleBudget = triangleBudget ?? throw new ArgumentNullException(nameof(triangleBudget));
        Coarse = coarse;
        MediumFine = mediumFine ?? throw new ArgumentNullException(nameof(mediumFine));
    }

    public string Schema { get; }

    public int SchemaVersion { get; }

    public string Id { get; }

    public string FamilyName { get; }

    public string DisplayNameEn { get; }

    public string DisplayNameJa { get; }

    public string Group { get; }

    public string Category { get; }

    public string SeedKind { get; }

    public bool IsPolygonal { get; }

    public IReadOnlyList<string> Subcategories { get; }

    public AntiZFightingClearances ClearancesMm { get; }

    public FamilyDimensionsSpec DefaultDimensionsMm { get; }

    public IReadOnlyList<FamilySharedParameterSpec> SharedParameters { get; }

    public FamilyTriangleBudgetSpec TriangleBudget { get; }

    public CoarseGeometrySpec? Coarse { get; }

    public MediumFineGeometrySpec MediumFine { get; }
}

/// <summary>Complete loaded Starter Kit specification bundle.</summary>
public sealed class StarterKitSpec
{
    public StarterKitSpec(
        StarterKitHeaderSpec header,
        RteTemplateSpec rte,
        Materials3DSpec materials3D,
        IReadOnlyList<FamilyGeometrySpec> families)
    {
        Header = header ?? throw new ArgumentNullException(nameof(header));
        Rte = rte ?? throw new ArgumentNullException(nameof(rte));
        Materials3D = materials3D ?? throw new ArgumentNullException(nameof(materials3D));
        Families = families ?? throw new ArgumentNullException(nameof(families));
    }

    public StarterKitHeaderSpec Header { get; }

    public RteTemplateSpec Rte { get; }

    public Materials3DSpec Materials3D { get; }

    public IReadOnlyList<FamilyGeometrySpec> Families { get; }

    public FamilyGeometrySpec? FindFamily(string id) =>
        Families.FirstOrDefault(f => string.Equals(f.Id, id, StringComparison.Ordinal));
}

/// <summary>Result of loading and validating a Starter Kit directory.</summary>
public sealed class StarterKitLoadResult
{
    public StarterKitLoadResult(StarterKitSpec? spec, StarterKitValidationResult validation)
    {
        Spec = spec;
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }

    public StarterKitSpec? Spec { get; }

    public StarterKitValidationResult Validation { get; }

    public bool IsValid => Spec != null && Validation.IsValid;
}
