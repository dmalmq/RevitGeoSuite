using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Text;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.Core.Federation;

/// <summary>Immutable 3D point in meters (shared Easting, Northing, Elevation).</summary>
public readonly struct FederationPoint3D
{
    public FederationPoint3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    public bool IsFinite =>
        FederationNumbers.IsFinite(X)
        && FederationNumbers.IsFinite(Y)
        && FederationNumbers.IsFinite(Z);
}

/// <summary>
/// One control point evaluated in two coordinate chains:
/// (1) transformed through the host's <c>RevitLinkInstance.GetTotalTransform()</c> into the host's shared coordinates, and
/// (2) evaluated directly in the linked model's own shared coordinates.
/// Comparing 3 non-collinear points verifies shared-coordinate pose consistency even when Survey Points differ intentionally.
/// </summary>
public sealed class ControlPointPair
{
    public ControlPointPair(string label, FederationPoint3D hostSharedMeters, FederationPoint3D linkOwnSharedMeters)
    {
        Label = string.IsNullOrWhiteSpace(label) ? "ControlPoint" : label.Trim();
        HostSharedMeters = hostSharedMeters;
        LinkOwnSharedMeters = linkOwnSharedMeters;
    }

    public string Label { get; }

    public FederationPoint3D HostSharedMeters { get; }

    public FederationPoint3D LinkOwnSharedMeters { get; }
}

/// <summary>Detailed result of a 3-noncollinear-control-point shared-coordinate pose check.</summary>
public sealed class PoseConsistencyResult
{
    public PoseConsistencyResult(
        bool isEvaluated,
        bool isAligned,
        double originTranslationMm,
        double maxHorizontalTranslationMm,
        double maxVerticalDriftMm,
        double signedOriginVerticalOffsetMm,
        double rotationErrorDegrees,
        double rotationErrorArcSeconds,
        double shearDegrees,
        double maxScaleRatioErrorPpm,
        bool isReflected,
        bool isDegenerateBaseline,
        IReadOnlyList<string> failureReasonsEn,
        IReadOnlyList<string> failureReasonsJa)
    {
        IsEvaluated = isEvaluated;
        IsAligned = isAligned;
        OriginTranslationMm = originTranslationMm;
        MaxHorizontalTranslationMm = maxHorizontalTranslationMm;
        MaxVerticalDriftMm = maxVerticalDriftMm;
        SignedOriginVerticalOffsetMm = signedOriginVerticalOffsetMm;
        RotationErrorDegrees = rotationErrorDegrees;
        RotationErrorArcSeconds = rotationErrorArcSeconds;
        ShearDegrees = shearDegrees;
        MaxScaleRatioErrorPpm = maxScaleRatioErrorPpm;
        IsReflected = isReflected;
        IsDegenerateBaseline = isDegenerateBaseline;
        FailureReasonsEn = Array.AsReadOnly((failureReasonsEn ?? Array.Empty<string>()).ToArray());
        FailureReasonsJa = Array.AsReadOnly((failureReasonsJa ?? Array.Empty<string>()).ToArray());
    }

    public bool IsEvaluated { get; }

    public bool IsAligned { get; }

    public double OriginTranslationMm { get; }

    public double MaxHorizontalTranslationMm { get; }

    public double MaxVerticalDriftMm { get; }

    public double SignedOriginVerticalOffsetMm { get; }

    public double RotationErrorDegrees { get; }

    public double RotationErrorArcSeconds { get; }

    public double ShearDegrees { get; }

    public double MaxScaleRatioErrorPpm { get; }

    public bool IsReflected { get; }

    public bool IsDegenerateBaseline { get; }

    public IReadOnlyList<string> FailureReasonsEn { get; }

    public IReadOnlyList<string> FailureReasonsJa { get; }

    public string FormatMetricsEn() => string.Format(
        CultureInfo.InvariantCulture,
        "translation {0:0.##} mm, rotation {1:0.##} arcsec ({2:0.####} deg), vertical Z drift {3:0.##} mm, scale error {4:0.#} ppm",
        MaxHorizontalTranslationMm,
        RotationErrorArcSeconds,
        RotationErrorDegrees,
        MaxVerticalDriftMm,
        MaxScaleRatioErrorPpm);

    public string FormatMetricsJa() => string.Format(
        CultureInfo.InvariantCulture,
        "平行移動誤差 {0:0.##} mm、回転誤差 {1:0.##} 秒 ({2:0.####}°)、鉛直 Z 誤差 {3:0.##} mm、スケール誤差 {4:0.#} ppm",
        MaxHorizontalTranslationMm,
        RotationErrorArcSeconds,
        RotationErrorDegrees,
        MaxVerticalDriftMm,
        MaxScaleRatioErrorPpm);
}

/// <summary>Pose consistency report entry associated with one linked model instance.</summary>
public sealed class LinkPoseCheckDetail
{
    public LinkPoseCheckDetail(string linkName, string linkModelId, string linkInstanceUniqueId, PoseConsistencyResult pose, string? sharedPositionName = null)
    {
        SharedPositionName = (sharedPositionName ?? string.Empty).Trim();
        LinkName = linkName ?? string.Empty;
        LinkModelId = linkModelId ?? string.Empty;
        LinkInstanceUniqueId = linkInstanceUniqueId ?? string.Empty;
        Pose = pose ?? throw new ArgumentNullException(nameof(pose));
    }

    public string LinkName { get; }

    public string LinkModelId { get; }

    public string LinkInstanceUniqueId { get; }

    public PoseConsistencyResult Pose { get; }

    public string SharedPositionName { get; }
}

/// <summary>Immutable snapshot of one Revit or exported level used across federation checks.</summary>
public sealed class FederationLevelSnapshot
{
    public FederationLevelSnapshot(
        long elementId,
        string? uniqueId,
        string name,
        string? levelId,
        int? ordinal,
        string? shortName,
        double? projectElevationMeters,
        bool hasFloors = true)
    {
        ElementId = elementId;
        UniqueId = (uniqueId ?? string.Empty).Trim();
        Name = (name ?? string.Empty).Trim();
        LevelId = (levelId ?? string.Empty).Trim();
        Ordinal = ordinal;
        ShortName = (shortName ?? string.Empty).Trim();
        ProjectElevationMeters = projectElevationMeters.HasValue && FederationNumbers.IsFinite(projectElevationMeters.Value)
            ? projectElevationMeters.Value
            : (double?)null;
        HasFloors = hasFloors;
        TilesLevelKey = LevelKey.Build(Name);
    }

    public long ElementId { get; }

    public string UniqueId { get; }

    public string Name { get; }

    public string LevelId { get; }

    public int? Ordinal { get; }

    public string ShortName { get; }

    /// <summary>
    /// Level elevation in meters derived from <c>Level.ProjectElevation</c> (in the host/T.P. shared vertical frame),
    /// or <c>null</c> when unavailable in a legacy manifest.
    /// </summary>
    public double? ProjectElevationMeters { get; }

    public bool HasElevation => ProjectElevationMeters.HasValue;

    public bool HasFloors { get; }

    /// <summary>Normalized 3D Tiles level group key matching <c>Tiles3DLevelGrouper</c>.</summary>
    public string TilesLevelKey { get; }
}

/// <summary>Immutable snapshot of one <c>RevitLinkInstance</c> inside a host document.</summary>
public sealed class FederationLinkSnapshot
{
    public FederationLinkSnapshot(
        long linkInstanceElementId,
        string? linkInstanceUniqueId,
        string name,
        bool isLoaded,
        string? sourcePath = null,
        string? modelId = null,
        string? modelRole = null,
        KanizuFederationMode? federationMode = null,
        string? hubModelId = null,
        string? rawFederationMode = null,
        int? crsEpsg = null,
        string? crsName = null,
        bool isGeoreferenceVerified = false,
        string? heightDatum = null,
        string? geoidModel = null,
        double? geoidOffsetMeters = null,
        IReadOnlyList<ControlPointPair>? controlPoints = null,
        IReadOnlyList<FederationLevelSnapshot>? levels = null,
        bool isIncludedInHubExport = true,
        string? sharedPositionName = null,
        IReadOnlyList<string>? nestedLinkNames = null)
    {
        LinkInstanceElementId = linkInstanceElementId;
        LinkInstanceUniqueId = (linkInstanceUniqueId ?? string.Empty).Trim();
        Name = (name ?? string.Empty).Trim();
        IsLoaded = isLoaded;
        SourcePath = (sourcePath ?? string.Empty).Trim();
        ModelId = (modelId ?? string.Empty).Trim();
        ModelRole = (modelRole ?? string.Empty).Trim();
        FederationMode = federationMode;
        HubModelId = string.IsNullOrWhiteSpace(hubModelId) ? null : hubModelId!.Trim();
        RawFederationMode = string.IsNullOrWhiteSpace(rawFederationMode) ? null : rawFederationMode!.Trim();
        CrsEpsg = crsEpsg;
        CrsName = (crsName ?? string.Empty).Trim();
        IsGeoreferenceVerified = isGeoreferenceVerified;
        HeightDatum = (heightDatum ?? string.Empty).Trim();
        GeoidModel = (geoidModel ?? string.Empty).Trim();
        GeoidOffsetMeters = geoidOffsetMeters.HasValue && FederationNumbers.IsFinite(geoidOffsetMeters.Value)
            ? geoidOffsetMeters.Value
            : (double?)null;
        ControlPoints = Array.AsReadOnly((controlPoints ?? Array.Empty<ControlPointPair>()).Where(p => p != null).ToArray());
        Levels = Array.AsReadOnly((levels ?? Array.Empty<FederationLevelSnapshot>()).Where(l => l != null).ToArray());
        IsIncludedInHubExport = isIncludedInHubExport;
        SharedPositionName = (sharedPositionName ?? string.Empty).Trim();
        NestedLinkNames = Array.AsReadOnly((nestedLinkNames ?? Array.Empty<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray());
    }

    public long LinkInstanceElementId { get; }

    public string LinkInstanceUniqueId { get; }

    public string Name { get; }

    public bool IsLoaded { get; }

    public string SourcePath { get; }

    public string ModelId { get; }

    public string ModelRole { get; }

    public KanizuFederationMode? FederationMode { get; }

    public string? HubModelId { get; }

    public string? RawFederationMode { get; }

    public int? CrsEpsg { get; }

    public string CrsName { get; }

    public bool IsGeoreferenceVerified { get; }

    public string HeightDatum { get; }

    public string GeoidModel { get; }

    public double? GeoidOffsetMeters { get; }

    public IReadOnlyList<ControlPointPair> ControlPoints { get; }

    public IReadOnlyList<FederationLevelSnapshot> Levels { get; }

    public bool IsIncludedInHubExport { get; }

    /// <summary>The link file's named shared position this instance matched for the pose check.</summary>
    public string SharedPositionName { get; }

    /// <summary>Attached links inside this linked model, which the federation checks do not open.</summary>
    public IReadOnlyList<string> NestedLinkNames { get; }
}

/// <summary>One linked model entry recorded inside a station-folder <c>gis-model.json</c> or <c>cesium-package.json</c>.</summary>
public sealed class StationFolderPublishedLink
{
    public StationFolderPublishedLink(string? uniqueId, string? name, string? modelId = null)
    {
        UniqueId = (uniqueId ?? string.Empty).Trim();
        Name = (name ?? string.Empty).Trim();
        ModelId = (modelId ?? string.Empty).Trim();
    }

    public string UniqueId { get; }

    public string Name { get; }

    public string ModelId { get; }
}

/// <summary>One <c>levelMap</c> entry read from a published <c>cesium-package.json</c>.</summary>
public sealed class StationFolderLevelMapEntry
{
    public StationFolderLevelMapEntry(string? gisLevelId, string? tilesLevelKey, string? name)
    {
        GisLevelId = (gisLevelId ?? string.Empty).Trim();
        TilesLevelKey = (tilesLevelKey ?? string.Empty).Trim();
        Name = (name ?? string.Empty).Trim();
    }

    public string GisLevelId { get; }

    public string TilesLevelKey { get; }

    public string Name { get; }
}

/// <summary>One model entry observed in a shared station folder (<c>gis-models/</c> and/or <c>revit/</c>).</summary>
public sealed class StationFolderModelEntry
{
    public StationFolderModelEntry(
        string? modelId,
        string? modelName,
        string? documentKey = null,
        string? sourceModelFile = null,
        string? modelRole = null,
        KanizuFederationMode? federationMode = null,
        string? hubModelId = null,
        string? rawFederationMode = null,
        int? crsEpsg = null,
        string? heightDatum = null,
        string? geoidModel = null,
        double? geoidOffsetMeters = null,
        bool hasPublishedPackage = false,
        bool hasGisModelSettings = false,
        string? publishedPackagePath = null,
        string? gisModelFilePath = null,
        IReadOnlyList<StationFolderPublishedLink>? includedLinks = null,
        IReadOnlyList<FederationLevelSnapshot>? levels = null,
        IReadOnlyList<StationFolderLevelMapEntry>? levelMapEntries = null)
    {
        ModelId = (modelId ?? string.Empty).Trim();
        ModelName = (modelName ?? string.Empty).Trim();
        DocumentKey = (documentKey ?? string.Empty).Trim();
        SourceModelFile = (sourceModelFile ?? string.Empty).Trim();
        ModelRole = (modelRole ?? string.Empty).Trim();
        FederationMode = federationMode;
        HubModelId = string.IsNullOrWhiteSpace(hubModelId) ? null : hubModelId!.Trim();
        RawFederationMode = string.IsNullOrWhiteSpace(rawFederationMode) ? null : rawFederationMode!.Trim();
        CrsEpsg = crsEpsg;
        HeightDatum = (heightDatum ?? string.Empty).Trim();
        GeoidModel = (geoidModel ?? string.Empty).Trim();
        GeoidOffsetMeters = geoidOffsetMeters.HasValue && FederationNumbers.IsFinite(geoidOffsetMeters.Value)
            ? geoidOffsetMeters.Value
            : (double?)null;
        HasPublishedPackage = hasPublishedPackage;
        HasGisModelSettings = hasGisModelSettings;
        PublishedPackagePath = (publishedPackagePath ?? string.Empty).Trim();
        GisModelFilePath = (gisModelFilePath ?? string.Empty).Trim();
        IncludedLinks = Array.AsReadOnly((includedLinks ?? Array.Empty<StationFolderPublishedLink>()).Where(l => l != null).ToArray());
        Levels = Array.AsReadOnly((levels ?? Array.Empty<FederationLevelSnapshot>()).Where(l => l != null).ToArray());
        LevelMapEntries = Array.AsReadOnly((levelMapEntries ?? Array.Empty<StationFolderLevelMapEntry>()).Where(l => l != null).ToArray());
    }

    public string ModelId { get; }

    public string ModelName { get; }

    public string DocumentKey { get; }

    public string SourceModelFile { get; }

    public string ModelRole { get; }

    public KanizuFederationMode? FederationMode { get; }

    public string? HubModelId { get; }

    public string? RawFederationMode { get; }

    public int? CrsEpsg { get; }

    public string HeightDatum { get; }

    public string GeoidModel { get; }

    public double? GeoidOffsetMeters { get; }

    public bool HasPublishedPackage { get; }

    public bool HasGisModelSettings { get; }

    public string PublishedPackagePath { get; }

    public string GisModelFilePath { get; }

    public IReadOnlyList<StationFolderPublishedLink> IncludedLinks { get; }

    public IReadOnlyList<FederationLevelSnapshot> Levels { get; }

    public IReadOnlyList<StationFolderLevelMapEntry> LevelMapEntries { get; }

    public string DisplayName =>
        !string.IsNullOrWhiteSpace(ModelName) ? ModelName
        : !string.IsNullOrWhiteSpace(SourceModelFile) ? SourceModelFile
        : !string.IsNullOrWhiteSpace(ModelId) ? ModelId
        : "(unnamed station model)";
}

/// <summary>Pure-C# snapshot of all models discovered in a shared station folder.</summary>
public sealed class StationFolderSnapshot
{
    public StationFolderSnapshot(
        string? stationFolderPath,
        IReadOnlyList<StationFolderModelEntry>? models,
        IReadOnlyList<string>? loadWarnings = null)
    {
        StationFolderPath = (stationFolderPath ?? string.Empty).Trim();
        Models = Array.AsReadOnly((models ?? Array.Empty<StationFolderModelEntry>()).Where(m => m != null).ToArray());
        LoadWarnings = Array.AsReadOnly((loadWarnings ?? Array.Empty<string>()).Where(w => !string.IsNullOrWhiteSpace(w)).ToArray());
    }

    public string StationFolderPath { get; }

    public IReadOnlyList<StationFolderModelEntry> Models { get; }

    public IReadOnlyList<string> LoadWarnings { get; }
}

/// <summary>Tolerances and policy flags controlling <see cref="FederationChecker"/> evaluation.</summary>
public sealed class FederationCheckOptions
{
    public static FederationCheckOptions Default { get; } = new();

    /// <summary>Maximum allowed horizontal discrepancy across the 3 pose control points (default 5 mm).</summary>
    public double PoseHorizontalToleranceMm { get; set; } = 5.0d;

    /// <summary>Maximum allowed vertical Z discrepancy across the 3 pose control points (default 5 mm).</summary>
    public double PoseVerticalToleranceMm { get; set; } = 5.0d;

    /// <summary>Maximum allowed True-North / bearing rotation error across the long baseline (default 10 arcsec).</summary>
    public double PoseRotationToleranceArcSeconds { get; set; } = 10.0d;

    /// <summary>Maximum allowed scale error across the long baseline (default 50 ppm = 5 mm over 100 m).</summary>
    public double PoseScaleTolerancePpm { get; set; } = 50.0d;

    /// <summary>Default long baseline length used by collectors when sampling 3 non-collinear control points (default 100 m).</summary>
    public double ControlPointBaselineMeters { get; set; } = 100.0d;

    /// <summary>Maximum allowed difference between typed/reported geoid offsets within one station (default 0.05 m).</summary>
    public double GeoidOffsetToleranceMeters { get; set; } = 0.05d;

    /// <summary>Levels closer than this threshold (default 15 mm) are treated as aligned.</summary>
    public double LevelAlignedToleranceMm { get; set; } = 15.0d;

    /// <summary>
    /// Upper bound of the near-miss level elevation drift band (default 150 mm).
    /// Levels with <c>LevelAlignedToleranceMm &lt;= |deltaZ| &lt;= LevelNearMissMaxMm</c> explicitly FAIL <c>KZ-FED-004</c>
    /// (protecting <c>UnitExtractor.HostLevelToleranceFeet = 0.1 ft / 30.5 mm</c>).
    /// Levels farther apart than this bound are treated as genuinely distinct floors or mezzanines.
    /// </summary>
    public double LevelNearMissMaxMm { get; set; } = 150.0d;

    /// <summary>
    /// When <c>false</c> (default), any duplicate <c>level_id</c> across distinct models fails <c>KZ-FED-003</c>.
    /// Set <c>true</c> only when a project explicitly opts into sharing canonical <c>level_id</c> values on aligned levels.
    /// </summary>
    public bool AllowSharedCanonicalLevelIdsAcrossModels { get; set; }
}

/// <summary>Pure-C# immutable-friendly input snapshot for <see cref="FederationChecker.Evaluate"/>.</summary>
public sealed class FederationCheckSnapshot
{
    public string HostDocumentTitle { get; set; } = string.Empty;

    public string HostDocumentPath { get; set; } = string.Empty;

    public string HostDocumentKey { get; set; } = string.Empty;

    public string HostModelId { get; set; } = string.Empty;

    public string HostModelRole { get; set; } = string.Empty;

    public KanizuFederationMode? HostFederationMode { get; set; }

    public string? HostHubModelId { get; set; }

    public string? HostRawFederationMode { get; set; }

    public int? HostCrsEpsg { get; set; }

    public string HostCrsName { get; set; } = string.Empty;

    public bool IsHostGeoreferenceVerified { get; set; }

    public string HostHeightDatum { get; set; } = string.Empty;

    public string HostGeoidModel { get; set; } = string.Empty;

    public double? HostGeoidOffsetMeters { get; set; }

    public string HostStationFolder { get; set; } = string.Empty;

    public IReadOnlyList<FederationLevelSnapshot> HostLevels { get; set; } = Array.Empty<FederationLevelSnapshot>();

    public IReadOnlyList<FederationLinkSnapshot> LinkedModels { get; set; } = Array.Empty<FederationLinkSnapshot>();

    public StationFolderSnapshot? StationFolder { get; set; }

    public FederationCheckOptions? Options { get; set; }

    public string HostDisplayName =>
        !string.IsNullOrWhiteSpace(HostDocumentTitle) ? HostDocumentTitle.Trim()
        : !string.IsNullOrWhiteSpace(HostModelId) ? HostModelId.Trim()
        : "Host";
}

/// <summary>One granular diagnostic issue emitted under a <c>KZ-FED-001..009</c> rule.</summary>
public sealed class FederationIssue
{
    public FederationIssue(
        string ruleId,
        ReadinessCheckStatus status,
        string code,
        string messageEn,
        string messageJa,
        string? modelA = null,
        string? modelB = null,
        IEnumerable<long>? elementIds = null,
        IReadOnlyDictionary<string, double>? metrics = null)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        Status = status;
        Code = code ?? string.Empty;
        MessageEn = messageEn ?? string.Empty;
        MessageJa = messageJa ?? string.Empty;
        ModelA = (modelA ?? string.Empty).Trim();
        ModelB = (modelB ?? string.Empty).Trim();
        ElementIds = Array.AsReadOnly((elementIds ?? Array.Empty<long>()).Where(id => id > 0).Distinct().ToArray());
        Dictionary<string, double> copy = new(StringComparer.Ordinal);
        if (metrics != null)
        {
            foreach (KeyValuePair<string, double> kv in metrics)
            {
                if (FederationNumbers.IsFinite(kv.Value))
                {
                    copy[kv.Key] = kv.Value;
                }
            }
        }

        Metrics = new ReadOnlyDictionary<string, double>(copy);
    }

    public string RuleId { get; }

    public ReadinessCheckStatus Status { get; }

    public string Code { get; }

    public string ModelA { get; }

    public string ModelB { get; }

    public string MessageEn { get; }

    public string MessageJa { get; }

    public IReadOnlyList<long> ElementIds { get; }

    public IReadOnlyDictionary<string, double> Metrics { get; }
}

/// <summary>Aggregated evaluation result for one canonical <c>KZ-FED-001..009</c> rule.</summary>
public sealed class FederationRuleResult
{
    public FederationRuleResult(
        string ruleId,
        string itemId,
        RuleSeverity severity,
        ReadinessCheckStatus status,
        string title,
        string titleJa,
        string summaryEn,
        string summaryJa,
        IReadOnlyList<FederationIssue>? issues = null,
        IEnumerable<long>? elementIds = null)
    {
        RuleId = ruleId ?? throw new ArgumentNullException(nameof(ruleId));
        ItemId = itemId ?? throw new ArgumentNullException(nameof(itemId));
        Severity = severity;
        Status = status;
        Title = title ?? string.Empty;
        TitleJa = titleJa ?? string.Empty;
        SummaryEn = summaryEn ?? string.Empty;
        SummaryJa = summaryJa ?? string.Empty;
        Issues = Array.AsReadOnly((issues ?? Array.Empty<FederationIssue>()).Where(i => i != null).ToArray());
        ElementIds = Array.AsReadOnly(
            (elementIds ?? Array.Empty<long>())
                .Concat(Issues.SelectMany(i => i.ElementIds))
                .Where(id => id > 0)
                .Distinct()
                .ToArray());
    }

    public string RuleId { get; }

    public string ItemId { get; }

    public RuleSeverity Severity { get; }

    public ReadinessCheckStatus Status { get; }

    public string Title { get; }

    public string TitleJa { get; }

    public string SummaryEn { get; }

    public string SummaryJa { get; }

    public IReadOnlyList<FederationIssue> Issues { get; }

    public IReadOnlyList<long> ElementIds { get; }

    public ReadinessCheckItem ToReadinessCheckItem() =>
        new(ItemId, Title, Status, SummaryEn, ReadinessActionKind.None, ElementIds);

    public HandoverRuleRow ToHandoverRuleRow()
    {
        HandoverRuleSeverity handoverSeverity = Severity switch
        {
            RuleSeverity.Error => HandoverRuleSeverity.Error,
            RuleSeverity.Warning => HandoverRuleSeverity.Warning,
            _ => HandoverRuleSeverity.Info,
        };
        HandoverRuleStatus handoverStatus = Status switch
        {
            ReadinessCheckStatus.Pass => HandoverRuleStatus.Pass,
            ReadinessCheckStatus.Warn => HandoverRuleStatus.Warn,
            ReadinessCheckStatus.Fail => HandoverRuleStatus.Fail,
            _ => HandoverRuleStatus.NotApplicable,
        };
        int count = Math.Max(
            Math.Max(Issues.Count, ElementIds.Count),
            handoverStatus is HandoverRuleStatus.Fail or HandoverRuleStatus.Warn ? 1 : 0);
        return new HandoverRuleRow(RuleId, ItemId, handoverSeverity, handoverStatus, count, Title);
    }
}

/// <summary>Full evaluation report produced by <see cref="FederationChecker"/> covering <c>KZ-FED-001..009</c>.</summary>
public sealed class FederationCheckReport
{
    public const string SchemaName = "kanizu.federation-check";
    public const int SchemaVersion = 1;

    public FederationCheckReport(
        IReadOnlyList<FederationRuleResult> rules,
        IReadOnlyList<LinkPoseCheckDetail>? poseResults = null)
    {
        Rules = Array.AsReadOnly((rules ?? Array.Empty<FederationRuleResult>()).Where(r => r != null).ToArray());
        PoseResults = Array.AsReadOnly((poseResults ?? Array.Empty<LinkPoseCheckDetail>()).Where(p => p != null).ToArray());
        PassCount = Rules.Count(r => r.Status == ReadinessCheckStatus.Pass);
        WarnCount = Rules.Count(r => r.Status == ReadinessCheckStatus.Warn);
        FailCount = Rules.Count(r => r.Status == ReadinessCheckStatus.Fail);
        NotApplicableCount = Rules.Count(r => r.Status == ReadinessCheckStatus.NotApplicable);
    }

    public IReadOnlyList<FederationRuleResult> Rules { get; }

    public IReadOnlyList<LinkPoseCheckDetail> PoseResults { get; }

    public int PassCount { get; }

    public int WarnCount { get; }

    public int FailCount { get; }

    public int NotApplicableCount { get; }

    public bool HasBlockingFailures => FailCount > 0;

    public bool IsPassed => FailCount == 0 && WarnCount == 0;

    public FederationRuleResult GetRule(string ruleId)
    {
        FederationRuleResult? found = Rules.FirstOrDefault(r => string.Equals(r.RuleId, ruleId, StringComparison.Ordinal));
        return found ?? throw new KeyNotFoundException($"Federation rule '{ruleId}' was not found in report.");
    }

    public IReadOnlyList<ReadinessCheckItem> ToReadinessCheckItems() =>
        Rules.Select(rule => rule.ToReadinessCheckItem()).ToList();

    public IReadOnlyList<HandoverRuleRow> ToHandoverRuleRows() =>
        Rules.Select(rule => rule.ToHandoverRuleRow()).ToList();

    public string ToJson(bool indented = true)
    {
        JObject root = new()
        {
            ["schema"] = SchemaName,
            ["schemaVersion"] = SchemaVersion,
            ["isPassed"] = IsPassed,
            ["passCount"] = PassCount,
            ["warnCount"] = WarnCount,
            ["failCount"] = FailCount,
            ["notApplicableCount"] = NotApplicableCount,
            ["rules"] = new JArray(Rules.Select(rule => new JObject
            {
                ["ruleId"] = rule.RuleId,
                ["itemId"] = rule.ItemId,
                ["severity"] = rule.Severity.ToString().ToLowerInvariant(),
                ["status"] = rule.Status.ToString().ToLowerInvariant(),
                ["title"] = rule.Title,
                ["titleJa"] = rule.TitleJa,
                ["summaryEn"] = rule.SummaryEn,
                ["summaryJa"] = rule.SummaryJa,
                ["elementIds"] = new JArray(rule.ElementIds),
                ["issues"] = new JArray(rule.Issues.Select(issue =>
                {
                    JObject obj = new()
                    {
                        ["code"] = issue.Code,
                        ["status"] = issue.Status.ToString().ToLowerInvariant(),
                        ["modelA"] = issue.ModelA,
                        ["modelB"] = issue.ModelB,
                        ["messageEn"] = issue.MessageEn,
                        ["messageJa"] = issue.MessageJa,
                        ["elementIds"] = new JArray(issue.ElementIds),
                    };
                    if (issue.Metrics.Count > 0)
                    {
                        JObject metricsObj = new();
                        foreach (KeyValuePair<string, double> kv in issue.Metrics.OrderBy(k => k.Key, StringComparer.Ordinal))
                        {
                            metricsObj[kv.Key] = Math.Round(kv.Value, 6);
                        }

                        obj["metrics"] = metricsObj;
                    }

                    return obj;
                })),
            })),
            ["poseResults"] = new JArray(PoseResults.Select(p => new JObject
            {
                ["linkName"] = p.LinkName,
                ["linkModelId"] = p.LinkModelId,
                ["linkInstanceUniqueId"] = p.LinkInstanceUniqueId,
                ["sharedPositionName"] = p.SharedPositionName,
                ["isEvaluated"] = p.Pose.IsEvaluated,
                ["isAligned"] = p.Pose.IsAligned,
                ["originTranslationMm"] = Math.Round(p.Pose.OriginTranslationMm, 3),
                ["maxHorizontalTranslationMm"] = Math.Round(p.Pose.MaxHorizontalTranslationMm, 3),
                ["maxVerticalDriftMm"] = Math.Round(p.Pose.MaxVerticalDriftMm, 3),
                ["signedOriginVerticalOffsetMm"] = Math.Round(p.Pose.SignedOriginVerticalOffsetMm, 3),
                ["rotationErrorDegrees"] = Math.Round(p.Pose.RotationErrorDegrees, 6),
                ["rotationErrorArcSeconds"] = Math.Round(p.Pose.RotationErrorArcSeconds, 3),
                ["shearDegrees"] = Math.Round(p.Pose.ShearDegrees, 6),
                ["maxScaleRatioErrorPpm"] = Math.Round(p.Pose.MaxScaleRatioErrorPpm, 3),
                ["isReflected"] = p.Pose.IsReflected,
                ["isDegenerateBaseline"] = p.Pose.IsDegenerateBaseline,
            })),
        };

        return root.ToString(indented ? Formatting.Indented : Formatting.None).Replace("\r\n", "\n") + (indented ? "\n" : string.Empty);
    }
}

internal static class FederationNumbers
{
    internal static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
