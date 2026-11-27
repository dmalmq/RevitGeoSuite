using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>Display language for New Kanizu Project prompts, validation messages, and summaries.</summary>
public enum NewKanizuProjectLanguage
{
    English = 0,
    Japanese = 1,
}

/// <summary>Multi-building federation topology recorded during New Kanizu Project initialization.</summary>
public enum KanizuFederationMode
{
    Standalone = 0,
    Hub = 1,
    Spoke = 2,
    Independent = 3,
}

/// <summary>Whether New Kanizu Project creates a fresh document from a Starter Kit <c>.rte</c> or initializes an open document.</summary>
public enum NewKanizuProjectInitializationMode
{
    CreateFromTemplate = 0,
    InitializeOpenDocument = 1,
}

/// <summary>Severity of a pure-C# validation or readiness finding produced by <see cref="NewKanizuProjectPlanner"/>.</summary>
public enum NewKanizuProjectValidationSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2,
}

/// <summary>Kind of stale/baked identity issue detected on an existing document before project initialization.</summary>
public enum StaleIdentityIssueKind
{
    ExistingModelId = 0,
    CopiedModelIdCollision = 1,
    ExistingStationFolder = 2,
    ExistingLevelId = 3,
    DuplicateLevelId = 4,
    ExistingElementIdentity = 5,
    ShowcaseModelRole = 6,
}

/// <summary>Kind of Revit view planned per level during New Kanizu Project bootstrap.</summary>
public enum PlannedKanizuViewKind
{
    GisExportPlan = 0,
    WorkingPlan = 1,
    GisAreaPlan = 2,
}

/// <summary>Pass/Warn/Fail status for a pre-flight or post-init readiness item.</summary>
public enum ProjectInitializationCheckStatus
{
    Pass = 0,
    Warn = 1,
    Fail = 2,
}

/// <summary>Input specification for one station floor level to initialize in a new Kanizu project.</summary>
public sealed class NewKanizuLevelSpec
{
    public const double MetersPerFoot = 0.3048;

    public NewKanizuLevelSpec(
        string name,
        int? ordinal,
        double elevationMeters,
        string? shortName = null)
    {
        Name = name ?? string.Empty;
        Ordinal = ordinal;
        ElevationMeters = elevationMeters;
        ShortName = string.IsNullOrWhiteSpace(shortName) ? (name ?? string.Empty).Trim() : shortName!.Trim();
    }

    public string Name { get; }

    public string ShortName { get; }

    public int? Ordinal { get; }

    /// <summary>Level elevation in meters (T.P. / project height).</summary>
    public double ElevationMeters { get; }

    /// <summary>Level elevation converted to Revit internal feet.</summary>
    [JsonIgnore]
    public double ElevationFeet => ElevationMeters / MetersPerFoot;
}

/// <summary>Declarative specification for one standard Kanizu discipline workset created on workshared projects.</summary>
public sealed class KanizuWorksetSpec
{
    public const string GisAreasName = "KNZ_GIS_Areas";
    public const string CirculationName = "KNZ_Circulation";
    public const string StructureName = "KNZ_Structure";
    public const string FixturesName = "KNZ_Fixtures";
    public const string LinksName = "KNZ_Links";

    public KanizuWorksetSpec(
        string name,
        string purposeEn,
        string purposeJa,
        IReadOnlyList<string> categories,
        bool visibleInGisExportViews = true,
        string qaOverrideColorHex = "#1E88E5")
    {
        Name = name ?? string.Empty;
        PurposeEn = purposeEn ?? string.Empty;
        PurposeJa = purposeJa ?? string.Empty;
        Categories = Array.AsReadOnly((categories ?? Array.Empty<string>()).ToArray());
        VisibleInGisExportViews = visibleInGisExportViews;
        QaOverrideColorHex = string.IsNullOrWhiteSpace(qaOverrideColorHex)
            ? "#1E88E5"
            : qaOverrideColorHex.Trim().ToUpperInvariant();
    }

    public string Name { get; }

    public string PurposeEn { get; }

    public string PurposeJa { get; }

    public IReadOnlyList<string> Categories { get; }

    /// <summary>
    /// Whether this workset MUST remain visible in <c>GIS_*</c> export views.
    /// Every Kanizu GIS authoring workset keeps <c>true</c> so <c>KZ-VIEW-004</c> is never violated.
    /// </summary>
    public bool VisibleInGisExportViews { get; }

    /// <summary>Non-hiding QA/working view highlight color in <c>#RRGGBB</c> hex.</summary>
    public string QaOverrideColorHex { get; }

    /// <summary>The 5 standard Kanizu discipline worksets created only after worksharing is enabled (never baked into <c>.rte</c>).</summary>
    public static IReadOnlyList<KanizuWorksetSpec> StandardWorksets { get; } = Array.AsReadOnly(new[]
    {
        new KanizuWorksetSpec(
            GisAreasName,
            "GIS unit floors, rooms, and GIS Area scheme boundaries.",
            "GIS ユニット床・部屋・GIS エリア境界。",
            new[] { "OST_Floors", "OST_Rooms", "OST_Areas" },
            visibleInGisExportViews: true,
            qaOverrideColorHex: "#1E88E5"),
        new KanizuWorksetSpec(
            CirculationName,
            "Vertical and horizontal circulation (stairs, escalators, elevators, moving walkways).",
            "縦・横動線（階段・エスカレーター・エレベーター・動く歩道）。",
            new[] { "OST_Stairs", "OST_GenericModel", "OST_MechanicalEquipment" },
            visibleInGisExportViews: true,
            qaOverrideColorHex: "#FB8C00"),
        new KanizuWorksetSpec(
            StructureName,
            "Columns, structural columns, walls, and reference grids.",
            "柱・構造柱・壁・通り芯。",
            new[] { "OST_Columns", "OST_StructuralColumns", "OST_Walls", "OST_Grids" },
            visibleInGisExportViews: true,
            qaOverrideColorHex: "#546E7A"),
        new KanizuWorksetSpec(
            FixturesName,
            "Ticket gates, barrier fences, kiosks, doors, windows, and station fixtures.",
            "改札機・柵・券売機／キオスク・扉・窓・設備什器。",
            new[] { "OST_SpecialityEquipment", "OST_Doors", "OST_Windows" },
            visibleInGisExportViews: true,
            qaOverrideColorHex: "#8E24AA"),
        new KanizuWorksetSpec(
            LinksName,
            "Linked Revit building models, CAD references, and site links.",
            "リンクされた Revit 建物モデル・CAD 参照・敷地リンク。",
            new[] { "OST_RvtLinks" },
            visibleInGisExportViews: true,
            qaOverrideColorHex: "#00897B"),
    });
}

/// <summary>Input options for planning and bootstrapping a new Kanizu authoring project.</summary>
public sealed class NewKanizuProjectOptions
{
    public const string DefaultConfigEnvironmentVariable = "KANIZU_NEW_PROJECT_CONFIG";

    /// <summary>Default 3-level Japanese station starter stack: B1F (-1), 1F (0), 2F (1).</summary>
    public static IReadOnlyList<NewKanizuLevelSpec> DefaultLevels { get; } = Array.AsReadOnly(new[]
    {
        new NewKanizuLevelSpec("B1F", ordinal: -1, elevationMeters: -4.5, shortName: "B1F"),
        new NewKanizuLevelSpec("1F", ordinal: 0, elevationMeters: 0.0, shortName: "1F"),
        new NewKanizuLevelSpec("2F", ordinal: 1, elevationMeters: 4.5, shortName: "2F"),
    });

    public string StationName { get; set; } = string.Empty;

    public string? StationCode { get; set; }

    public string? BuildingSlug { get; set; }

    /// <summary>
    /// Model role for the initialized project. Locked to <c>"authoring"</c> for New Kanizu Project;
    /// supplying <c>"showcase"</c> or any other role is rejected by <see cref="NewKanizuProjectPlanner"/>.
    /// </summary>
    public string ModelRole { get; set; } = ShowcaseExportGuard.AuthoringModelRole;

    public KanizuFederationMode FederationMode { get; set; } = KanizuFederationMode.Standalone;

    /// <summary>Required when <see cref="FederationMode"/> is <see cref="KanizuFederationMode.Spoke"/>.</summary>
    public string? HubModelId { get; set; }

    /// <summary>
    /// Level list to initialize. Leave <c>null</c> to use <see cref="DefaultLevels"/> (B1F / 1F / 2F).
    /// Passing an explicitly empty list fails validation.
    /// </summary>
    public IReadOnlyList<NewKanizuLevelSpec>? Levels { get; set; }

    /// <summary>When true, enables worksharing (if supported) and creates the Kanizu discipline worksets + workset QA filters.</summary>
    public bool EnableWorksharing { get; set; }

    /// <summary>Optional custom workset list when <see cref="EnableWorksharing"/> is true; defaults to <see cref="KanizuWorksetSpec.StandardWorksets"/>.</summary>
    public IReadOnlyList<KanizuWorksetSpec>? Worksets { get; set; }

    public NewKanizuProjectInitializationMode InitializationMode { get; set; } = NewKanizuProjectInitializationMode.CreateFromTemplate;

    /// <summary>Optional path to <c>KanizuStationStarter.rte</c> (used when <see cref="InitializationMode"/> is <see cref="NewKanizuProjectInitializationMode.CreateFromTemplate"/>).</summary>
    public string? TemplatePath { get; set; }

    /// <summary>Optional path to save the newly created <c>.rvt</c> file in headless/batch runs.</summary>
    public string? SaveAsRvtPath { get; set; }

    /// <summary>Optional JSON output path where <c>NewKanizuProjectCommand</c> writes its structured run result in headless mode.</summary>
    public string? ResultOutputPath { get; set; }

    /// <summary>Optional override for <c>gis_kit_version</c>; defaults to <c>standards/starter-kit/kit.json</c>.</summary>
    public string? KitVersion { get; set; }

    /// <summary>Optional override for <c>gis_standard_version</c>; defaults to <c>kit.json</c> (3).</summary>
    public int? GisStandardVersion { get; set; }

    /// <summary>
    /// Explicit opt-in to clear stale <c>ModelId</c>, <c>GisStationFolder</c>, <c>level_id</c>s, and element
    /// <c>id</c>/<c>gis_key</c> values when initializing an already-open document copied from another <c>.rvt</c>.
    /// </summary>
    public bool ClearStaleIdentity { get; set; }

    public bool CreateExportPlanViews { get; set; } = true;

    public bool CreateWorkingPlanViews { get; set; } = true;

    public bool CreateAreaPlanViews { get; set; } = true;

    public NewKanizuProjectLanguage Language { get; set; } = NewKanizuProjectLanguage.English;

    public string ToJson(bool indented = true)
    {
        JsonSerializerSettings settings = CreateJsonSettings(indented);
        return JsonConvert.SerializeObject(this, settings).Replace("\r\n", "\n") + (indented ? "\n" : string.Empty);
    }

    public static NewKanizuProjectOptions FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new ArgumentException("Options JSON cannot be empty.", nameof(json));
        }

        JsonSerializerSettings settings = CreateJsonSettings(indented: false);
        NewKanizuProjectOptions? parsed = JsonConvert.DeserializeObject<NewKanizuProjectOptions>(json, settings);
        return parsed ?? throw new InvalidOperationException("Failed to deserialize NewKanizuProjectOptions.");
    }

    private static JsonSerializerSettings CreateJsonSettings(bool indented)
    {
        JsonSerializerSettings settings = new()
        {
            Formatting = indented ? Formatting.Indented : Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
        };
        settings.Converters.Add(new StringEnumConverter());
        return settings;
    }
}

/// <summary>One existing level's identity metadata read from a Revit document before or after initialization.</summary>
public sealed class ExistingLevelIdentityEntry
{
    public ExistingLevelIdentityEntry(
        long elementId,
        string levelName,
        string? levelId,
        int? ordinal = null,
        string? shortName = null,
        double? elevationMeters = null)
    {
        ElementId = elementId;
        LevelName = levelName ?? string.Empty;
        LevelId = (levelId ?? string.Empty).Trim();
        Ordinal = ordinal;
        ShortName = (shortName ?? string.Empty).Trim();
        ElevationMeters = elevationMeters;
    }

    public long ElementId { get; }

    public string LevelName { get; }

    public string LevelId { get; }

    public int? Ordinal { get; }

    public string ShortName { get; }

    public double? ElevationMeters { get; }
}

/// <summary>Pure-C# snapshot of an existing document's identity fields used to detect stale/copied identity.</summary>
public sealed class StaleIdentitySnapshot
{
    public string DocumentTitle { get; set; } = string.Empty;

    public string DocumentPath { get; set; } = string.Empty;

    public string ExistingModelId { get; set; } = string.Empty;

    public string ExistingStationFolder { get; set; } = string.Empty;

    public string ExistingModelRole { get; set; } = string.Empty;

    public string ExistingKitVersion { get; set; } = string.Empty;

    public bool IsWorkshared { get; set; }

    public IReadOnlyList<ExistingLevelIdentityEntry> ExistingLevels { get; set; } = Array.Empty<ExistingLevelIdentityEntry>();

    public int ElementsWithIdentityCount { get; set; }

    public IReadOnlyList<long> ElementsWithIdentitySample { get; set; } = Array.Empty<long>();

    /// <summary>Optional known ModelIds from sibling/template projects to flag explicit Save-As collisions.</summary>
    public IReadOnlyList<string> KnownOtherProjectModelIds { get; set; } = Array.Empty<string>();
}

/// <summary>One stale-identity finding discovered by <see cref="NewKanizuProjectPlanner.InspectStaleIdentity"/>.</summary>
public sealed class StaleIdentityFinding
{
    public StaleIdentityFinding(
        StaleIdentityIssueKind kind,
        NewKanizuProjectValidationSeverity severity,
        string code,
        string messageEn,
        string messageJa,
        IEnumerable<long>? elementIds = null)
    {
        Kind = kind;
        Severity = severity;
        Code = code ?? string.Empty;
        MessageEn = messageEn ?? string.Empty;
        MessageJa = messageJa ?? string.Empty;
        ElementIds = Array.AsReadOnly((elementIds ?? Array.Empty<long>()).Distinct().ToArray());
    }

    public StaleIdentityIssueKind Kind { get; }

    public NewKanizuProjectValidationSeverity Severity { get; }

    public string Code { get; }

    public string MessageEn { get; }

    public string MessageJa { get; }

    public IReadOnlyList<long> ElementIds { get; }

    public string GetMessage(NewKanizuProjectLanguage language) =>
        language == NewKanizuProjectLanguage.Japanese ? MessageJa : MessageEn;
}

/// <summary>Aggregated stale-identity inspection report for an open document.</summary>
public sealed class StaleIdentityReport
{
    public StaleIdentityReport(IReadOnlyList<StaleIdentityFinding> findings)
    {
        Findings = Array.AsReadOnly((findings ?? Array.Empty<StaleIdentityFinding>()).ToArray());
        HasStaleIdentity = Findings.Any(f =>
            f.Kind == StaleIdentityIssueKind.ExistingModelId
            || f.Kind == StaleIdentityIssueKind.CopiedModelIdCollision
            || f.Kind == StaleIdentityIssueKind.ExistingStationFolder
            || f.Kind == StaleIdentityIssueKind.ExistingLevelId
            || f.Kind == StaleIdentityIssueKind.DuplicateLevelId
            || f.Kind == StaleIdentityIssueKind.ExistingElementIdentity);
        HasDuplicateLevelIds = Findings.Any(f => f.Kind == StaleIdentityIssueKind.DuplicateLevelId);
        HasCopiedModelIdCollision = Findings.Any(f => f.Kind == StaleIdentityIssueKind.CopiedModelIdCollision);
        IsShowcaseModel = Findings.Any(f => f.Kind == StaleIdentityIssueKind.ShowcaseModelRole);
    }

    public static StaleIdentityReport Clean { get; } = new(Array.Empty<StaleIdentityFinding>());

    public IReadOnlyList<StaleIdentityFinding> Findings { get; }

    public bool HasStaleIdentity { get; }

    public bool HasDuplicateLevelIds { get; }

    public bool HasCopiedModelIdCollision { get; }

    public bool IsShowcaseModel { get; }
}

/// <summary>One validation finding emitted by <see cref="NewKanizuProjectPlanner"/>.</summary>
public sealed class NewKanizuProjectValidationFinding
{
    public NewKanizuProjectValidationFinding(
        NewKanizuProjectValidationSeverity severity,
        string code,
        string field,
        string messageEn,
        string messageJa)
    {
        Severity = severity;
        Code = code ?? string.Empty;
        Field = field ?? string.Empty;
        MessageEn = messageEn ?? string.Empty;
        MessageJa = messageJa ?? string.Empty;
    }

    public NewKanizuProjectValidationSeverity Severity { get; }

    public string Code { get; }

    public string Field { get; }

    public string MessageEn { get; }

    public string MessageJa { get; }

    public string GetMessage(NewKanizuProjectLanguage language) =>
        language == NewKanizuProjectLanguage.Japanese ? MessageJa : MessageEn;

    public override string ToString() => $"[{Severity}] {Code} ({Field}): {MessageEn}";
}

/// <summary>One planned project level with its freshly minted unique <c>level_id</c> and planned view names.</summary>
public sealed class PlannedKanizuLevel
{
    public PlannedKanizuLevel(
        string name,
        string shortName,
        int ordinal,
        double elevationMeters,
        string levelId,
        string exportPlanViewName,
        string workingPlanViewName,
        string areaPlanViewName)
    {
        Name = name ?? string.Empty;
        ShortName = shortName ?? string.Empty;
        Ordinal = ordinal;
        ElevationMeters = elevationMeters;
        LevelId = levelId ?? string.Empty;
        ExportPlanViewName = exportPlanViewName ?? string.Empty;
        WorkingPlanViewName = workingPlanViewName ?? string.Empty;
        AreaPlanViewName = areaPlanViewName ?? string.Empty;
    }

    public string Name { get; }

    public string ShortName { get; }

    public int Ordinal { get; }

    public double ElevationMeters { get; }

    public double ElevationFeet => ElevationMeters / NewKanizuLevelSpec.MetersPerFoot;

    /// <summary>Freshly minted unique <c>level_id</c> (lowercase UUIDv4 <c>"D"</c>).</summary>
    public string LevelId { get; }

    public string ExportPlanViewName { get; }

    public string WorkingPlanViewName { get; }

    public string AreaPlanViewName { get; }
}

/// <summary>One view planned during New Kanizu Project bootstrap.</summary>
public sealed class PlannedKanizuView
{
    public PlannedKanizuView(
        string viewName,
        string levelName,
        string levelId,
        PlannedKanizuViewKind viewKind,
        string? viewTemplateName,
        string? areaSchemeName = null)
    {
        ViewName = viewName ?? string.Empty;
        LevelName = levelName ?? string.Empty;
        LevelId = levelId ?? string.Empty;
        ViewKind = viewKind;
        ViewTemplateName = viewTemplateName;
        AreaSchemeName = areaSchemeName;
    }

    public string ViewName { get; }

    public string LevelName { get; }

    public string LevelId { get; }

    public PlannedKanizuViewKind ViewKind { get; }

    public string? ViewTemplateName { get; }

    public string? AreaSchemeName { get; }
}

/// <summary>
/// One workset QA/working view filter planned ONLY when worksharing is enabled (never baked into <c>.rte</c>).
/// Always keeps <see cref="Visible"/> = <c>true</c> and never attaches hide rules to <c>GIS Export</c> (<c>KZ-VIEW-004</c>).
/// </summary>
public sealed class PlannedWorksetViewFilter
{
    public PlannedWorksetViewFilter(
        string filterName,
        string worksetName,
        IReadOnlyList<string> categories,
        IReadOnlyList<string> targetViewTemplateNames,
        string overrideColorHex,
        byte red,
        byte green,
        byte blue,
        bool visible = true)
    {
        FilterName = filterName ?? string.Empty;
        WorksetName = worksetName ?? string.Empty;
        Categories = Array.AsReadOnly((categories ?? Array.Empty<string>()).ToArray());
        TargetViewTemplateNames = Array.AsReadOnly((targetViewTemplateNames ?? Array.Empty<string>()).ToArray());
        OverrideColorHex = (overrideColorHex ?? string.Empty).Trim().ToUpperInvariant();
        Red = red;
        Green = green;
        Blue = blue;
        Visible = visible;
    }

    public string FilterName { get; }

    public string WorksetName { get; }

    public IReadOnlyList<string> Categories { get; }

    public IReadOnlyList<string> TargetViewTemplateNames { get; }

    public string OverrideColorHex { get; }

    public byte Red { get; }

    public byte Green { get; }

    public byte Blue { get; }

    public bool Visible { get; }
}

/// <summary>Worksharing and Kanizu discipline workset plan (created on project init, never inside <c>.rte</c>).</summary>
public sealed class PlannedWorksetSetup
{
    public PlannedWorksetSetup(
        bool enableWorksharing,
        string defaultSharedLevelsWorksetName,
        string defaultActiveWorksetName,
        IReadOnlyList<KanizuWorksetSpec> worksets,
        IReadOnlyList<PlannedWorksetViewFilter> worksetViewFilters,
        string fallbackNoteEn,
        string fallbackNoteJa)
    {
        EnableWorksharing = enableWorksharing;
        DefaultSharedLevelsWorksetName = defaultSharedLevelsWorksetName ?? string.Empty;
        DefaultActiveWorksetName = defaultActiveWorksetName ?? string.Empty;
        Worksets = Array.AsReadOnly((worksets ?? Array.Empty<KanizuWorksetSpec>()).ToArray());
        WorksetViewFilters = Array.AsReadOnly((worksetViewFilters ?? Array.Empty<PlannedWorksetViewFilter>()).ToArray());
        FallbackNoteEn = fallbackNoteEn ?? string.Empty;
        FallbackNoteJa = fallbackNoteJa ?? string.Empty;
    }

    public bool EnableWorksharing { get; }

    public string DefaultSharedLevelsWorksetName { get; }

    public string DefaultActiveWorksetName { get; }

    public IReadOnlyList<KanizuWorksetSpec> Worksets { get; }

    public IReadOnlyList<PlannedWorksetViewFilter> WorksetViewFilters { get; }

    public string FallbackNoteEn { get; }

    public string FallbackNoteJa { get; }

    public string GetFallbackNote(NewKanizuProjectLanguage language) =>
        language == NewKanizuProjectLanguage.Japanese ? FallbackNoteJa : FallbackNoteEn;
}

/// <summary>One pre-flight or post-init readiness assertion evaluated for a New Kanizu Project run.</summary>
public sealed class ProjectInitializationCheckItem
{
    public ProjectInitializationCheckItem(
        string code,
        ProjectInitializationCheckStatus status,
        string titleEn,
        string titleJa,
        string detailEn,
        string detailJa)
    {
        Code = code ?? string.Empty;
        Status = status;
        TitleEn = titleEn ?? string.Empty;
        TitleJa = titleJa ?? string.Empty;
        DetailEn = detailEn ?? string.Empty;
        DetailJa = detailJa ?? string.Empty;
    }

    public string Code { get; }

    public ProjectInitializationCheckStatus Status { get; }

    public string TitleEn { get; }

    public string TitleJa { get; }

    public string DetailEn { get; }

    public string DetailJa { get; }

    public string GetTitle(NewKanizuProjectLanguage language) =>
        language == NewKanizuProjectLanguage.Japanese ? TitleJa : TitleEn;

    public string GetDetail(NewKanizuProjectLanguage language) =>
        language == NewKanizuProjectLanguage.Japanese ? DetailJa : DetailEn;
}

/// <summary>Structured pre-flight or post-init readiness summary for New Kanizu Project initialization.</summary>
public sealed class ProjectInitializationReadinessSummary
{
    public ProjectInitializationReadinessSummary(
        string phase,
        IReadOnlyList<ProjectInitializationCheckItem> items,
        ProjectReadinessReport? templateReadinessReport = null)
    {
        Phase = phase ?? string.Empty;
        Items = Array.AsReadOnly((items ?? Array.Empty<ProjectInitializationCheckItem>()).ToArray());
        TemplateReadinessReport = templateReadinessReport;
        PassCount = Items.Count(i => i.Status == ProjectInitializationCheckStatus.Pass);
        WarnCount = Items.Count(i => i.Status == ProjectInitializationCheckStatus.Warn);
        FailCount = Items.Count(i => i.Status == ProjectInitializationCheckStatus.Fail);
    }

    public string Phase { get; }

    public IReadOnlyList<ProjectInitializationCheckItem> Items { get; }

    /// <summary>Optional full <see cref="ProjectReadinessReport"/> from <see cref="ProjectReadinessEvaluator"/>.</summary>
    public ProjectReadinessReport? TemplateReadinessReport { get; }

    public int PassCount { get; }

    public int WarnCount { get; }

    public int FailCount { get; }

    public bool IsReady => FailCount == 0;
}

/// <summary>
/// Complete pure-C# project bootstrap plan minted by <see cref="NewKanizuProjectPlanner.Plan"/>
/// and executed inside Revit transactions by <c>NewKanizuProjectBootstrapper</c>.
/// </summary>
public sealed class NewKanizuProjectPlan
{
    public NewKanizuProjectPlan(
        string modelId,
        string stationName,
        string stationCode,
        string buildingSlug,
        string projectKey,
        string modelRole,
        string kitVersion,
        int gisStandardVersion,
        KanizuFederationMode federationMode,
        string? hubModelId,
        NewKanizuProjectInitializationMode initializationMode,
        string? templatePath,
        bool shouldClearStaleIdentity,
        IReadOnlyDictionary<string, string> projectInfoStamps,
        IReadOnlyList<PlannedKanizuLevel> levels,
        IReadOnlyList<PlannedKanizuView> plannedViews,
        PlannedWorksetSetup worksetPlan,
        StaleIdentityReport staleIdentity,
        ProjectInitializationReadinessSummary preFlightReadiness,
        ProjectInitializationReadinessSummary expectedPostInitReadiness,
        IReadOnlyList<NewKanizuProjectValidationFinding> validationFindings,
        string summaryEn,
        string summaryJa)
    {
        ModelId = modelId ?? string.Empty;
        StationName = stationName ?? string.Empty;
        StationCode = stationCode ?? string.Empty;
        BuildingSlug = buildingSlug ?? string.Empty;
        ProjectKey = projectKey ?? string.Empty;
        ModelRole = modelRole ?? string.Empty;
        KitVersion = kitVersion ?? string.Empty;
        GisStandardVersion = gisStandardVersion;
        FederationMode = federationMode;
        HubModelId = string.IsNullOrWhiteSpace(hubModelId) ? null : hubModelId!.Trim();
        InitializationMode = initializationMode;
        TemplatePath = string.IsNullOrWhiteSpace(templatePath) ? null : templatePath!.Trim();
        ShouldClearStaleIdentity = shouldClearStaleIdentity;
        Dictionary<string, string> stampsCopy = new(StringComparer.Ordinal);
        if (projectInfoStamps != null)
        {
            foreach (KeyValuePair<string, string> kv in projectInfoStamps)
            {
                stampsCopy[kv.Key] = kv.Value;
            }
        }

        ProjectInfoStamps = new ReadOnlyDictionary<string, string>(stampsCopy);
        Levels = Array.AsReadOnly((levels ?? Array.Empty<PlannedKanizuLevel>()).ToArray());
        PlannedViews = Array.AsReadOnly((plannedViews ?? Array.Empty<PlannedKanizuView>()).ToArray());
        WorksetPlan = worksetPlan ?? throw new ArgumentNullException(nameof(worksetPlan));
        StaleIdentity = staleIdentity ?? StaleIdentityReport.Clean;
        PreFlightReadiness = preFlightReadiness ?? throw new ArgumentNullException(nameof(preFlightReadiness));
        ExpectedPostInitReadiness = expectedPostInitReadiness ?? throw new ArgumentNullException(nameof(expectedPostInitReadiness));
        ValidationFindings = Array.AsReadOnly((validationFindings ?? Array.Empty<NewKanizuProjectValidationFinding>()).ToArray());
        Errors = Array.AsReadOnly(ValidationFindings.Where(f => f.Severity == NewKanizuProjectValidationSeverity.Error).ToArray());
        Warnings = Array.AsReadOnly(ValidationFindings.Where(f => f.Severity == NewKanizuProjectValidationSeverity.Warning).ToArray());
        SummaryEn = summaryEn ?? string.Empty;
        SummaryJa = summaryJa ?? string.Empty;
    }

    /// <summary>Freshly minted unique <c>GeoProjectInfo.ModelId</c> (lowercase RFC-4122 UUIDv4 <c>"D"</c>).</summary>
    public string ModelId { get; }

    public string StationName { get; }

    public string StationCode { get; }

    public string BuildingSlug { get; }

    public string ProjectKey { get; }

    /// <summary>Always <c>"authoring"</c> for every valid New Kanizu Project plan.</summary>
    public string ModelRole { get; }

    public string KitVersion { get; }

    public int GisStandardVersion { get; }

    public KanizuFederationMode FederationMode { get; }

    public string? HubModelId { get; }

    public NewKanizuProjectInitializationMode InitializationMode { get; }

    public string? TemplatePath { get; }

    public bool ShouldClearStaleIdentity { get; }

    public IReadOnlyDictionary<string, string> ProjectInfoStamps { get; }

    public IReadOnlyList<PlannedKanizuLevel> Levels { get; }

    public IReadOnlyList<PlannedKanizuView> PlannedViews { get; }

    public PlannedWorksetSetup WorksetPlan { get; }

    public StaleIdentityReport StaleIdentity { get; }

    public ProjectInitializationReadinessSummary PreFlightReadiness { get; }

    public ProjectInitializationReadinessSummary ExpectedPostInitReadiness { get; }

    public IReadOnlyList<NewKanizuProjectValidationFinding> ValidationFindings { get; }

    public IReadOnlyList<NewKanizuProjectValidationFinding> Errors { get; }

    public IReadOnlyList<NewKanizuProjectValidationFinding> Warnings { get; }

    public bool IsValid => Errors.Count == 0;

    public string SummaryEn { get; }

    public string SummaryJa { get; }

    public string GetSummary(NewKanizuProjectLanguage language) =>
        language == NewKanizuProjectLanguage.Japanese ? SummaryJa : SummaryEn;

    /// <summary>Serializes the plan to deterministic LF-normalized JSON for audit logs and headless runs.</summary>
    public string ToCanonicalJson(bool indented = true)
    {
        JObject root = new()
        {
            ["schema"] = "kanizu.new-project-plan",
            ["schemaVersion"] = 1,
            ["isValid"] = IsValid,
            ["modelId"] = ModelId,
            ["stationName"] = StationName,
            ["stationCode"] = StationCode,
            ["buildingSlug"] = BuildingSlug,
            ["projectKey"] = ProjectKey,
            ["modelRole"] = ModelRole,
            ["kitVersion"] = KitVersion,
            ["gisStandardVersion"] = GisStandardVersion,
            ["federation"] = new JObject
            {
                ["mode"] = FederationMode.ToString().ToLowerInvariant(),
                ["hubModelId"] = HubModelId,
            },
            ["initializationMode"] = InitializationMode.ToString(),
            ["shouldClearStaleIdentity"] = ShouldClearStaleIdentity,
            ["projectInfoStamps"] = new JObject(
                ProjectInfoStamps
                    .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                    .Select(kv => new JProperty(kv.Key, kv.Value))),
            ["levels"] = new JArray(Levels.Select(l => new JObject
            {
                ["name"] = l.Name,
                ["shortName"] = l.ShortName,
                ["ordinal"] = l.Ordinal,
                ["elevationMeters"] = Math.Round(l.ElevationMeters, 4),
                ["levelId"] = l.LevelId,
                ["exportPlanViewName"] = l.ExportPlanViewName,
                ["workingPlanViewName"] = l.WorkingPlanViewName,
                ["areaPlanViewName"] = l.AreaPlanViewName,
            })),
            ["views"] = new JArray(PlannedViews.Select(v => new JObject
            {
                ["viewName"] = v.ViewName,
                ["levelName"] = v.LevelName,
                ["levelId"] = v.LevelId,
                ["viewKind"] = v.ViewKind.ToString(),
                ["viewTemplateName"] = v.ViewTemplateName,
                ["areaSchemeName"] = v.AreaSchemeName,
            })),
            ["worksets"] = new JObject
            {
                ["enableWorksharing"] = WorksetPlan.EnableWorksharing,
                ["defaultSharedLevelsWorksetName"] = WorksetPlan.DefaultSharedLevelsWorksetName,
                ["defaultActiveWorksetName"] = WorksetPlan.DefaultActiveWorksetName,
                ["items"] = new JArray(WorksetPlan.Worksets.Select(w => new JObject
                {
                    ["name"] = w.Name,
                    ["visibleInGisExportViews"] = w.VisibleInGisExportViews,
                    ["qaOverrideColorHex"] = w.QaOverrideColorHex,
                    ["categories"] = new JArray(w.Categories),
                })),
                ["filters"] = new JArray(WorksetPlan.WorksetViewFilters.Select(f => new JObject
                {
                    ["filterName"] = f.FilterName,
                    ["worksetName"] = f.WorksetName,
                    ["visible"] = f.Visible,
                    ["overrideColorHex"] = f.OverrideColorHex,
                    ["targetViewTemplates"] = new JArray(f.TargetViewTemplateNames),
                })),
            },
            ["preFlightReady"] = PreFlightReadiness.IsReady,
            ["expectedPostInitReady"] = ExpectedPostInitReadiness.IsReady,
            ["errorCount"] = Errors.Count,
            ["warningCount"] = Warnings.Count,
        };

        return root.ToString(indented ? Formatting.Indented : Formatting.None).Replace("\r\n", "\n") + (indented ? "\n" : string.Empty);
    }
}
