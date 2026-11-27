using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.Core.Text;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.Core.Federation;

/// <summary>
/// Pure-C# Kanizu Federation Checker evaluating all 9 canonical federation rules
/// (<c>KZ-FED-001</c> through <c>KZ-FED-009</c>) across both hub-and-spoke linked Revit models
/// and independent-georef station-folder multi-building layouts.
/// </summary>
public static class FederationChecker
{
    public const string RuleCrsAndPose = "KZ-FED-001";
    public const string RuleUniqueModelId = "KZ-FED-002";
    public const string RuleUniqueLevelId = "KZ-FED-003";
    public const string RuleNearMissLevelDrift = "KZ-FED-004";
    public const string RuleTilesLevelKeyCollision = "KZ-FED-005";
    public const string RuleStationFolderFederation = "KZ-FED-006";
    public const string RuleNoDoublePublishing = "KZ-FED-007";
    public const string RuleLinkAndRoleHygiene = "KZ-FED-008";
    public const string RuleShowcaseExclusion = "KZ-FED-009";

    public const string ItemCrsAndPose = "project-crs";
    public const string ItemUniqueModelId = "model-ids";
    public const string ItemUniqueLevelId = "level-metadata";
    public const string ItemNearMissLevelDrift = "federation-level-drift";
    public const string ItemTilesLevelKeyCollision = "federation-tiles-level-keys";
    public const string ItemStationFolderFederation = "federation-station-folder";
    public const string ItemNoDoublePublishing = "federation-double-publishing";
    public const string ItemLinkAndRoleHygiene = "federation-link-hygiene";
    public const string ItemShowcaseExclusion = "showcase-model-role";

    private const double BoundaryEpsilonMm = 1e-6d;

    /// <summary>Evaluates all <c>KZ-FED-001..009</c> rules against a host + links + optional station-folder snapshot.</summary>
    public static FederationCheckReport Evaluate(FederationCheckSnapshot snapshot)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        FederationCheckOptions options = snapshot.Options ?? FederationCheckOptions.Default;
        IReadOnlyList<FederationLinkSnapshot> allLinks = (snapshot.LinkedModels ?? Array.Empty<FederationLinkSnapshot>())
            .Where(link => link != null)
            .ToList();
        List<FederationLinkSnapshot> loadedAuthoringLinks = allLinks
            .Where(link => link.IsLoaded && !IsShowcaseLink(link))
            .ToList();
        IReadOnlyList<StationFolderModelEntry> stationModels = (snapshot.StationFolder?.Models ?? Array.Empty<StationFolderModelEntry>())
            .Where(model => model != null)
            .ToList();
        List<StationFolderModelEntry> stationAuthoringModels = stationModels
            .Where(model => !IsShowcaseStationModel(model))
            .ToList();

        List<LinkPoseCheckDetail> poseDetails = new();

        FederationRuleResult fed001 = EvaluateCrsAndPose(snapshot, loadedAuthoringLinks, stationAuthoringModels, options, poseDetails, isStationOnly: false);
        FederationRuleResult fed002 = EvaluateUniqueModelIds(snapshot, loadedAuthoringLinks, stationAuthoringModels, isStationOnly: false);
        FederationRuleResult fed003 = EvaluateLevelMetadataAndUniqueLevelIds(snapshot, loadedAuthoringLinks, stationAuthoringModels, options, isStationOnly: false);
        FederationRuleResult fed004 = EvaluateNearMissLevelDrift(snapshot, loadedAuthoringLinks, stationAuthoringModels, options, isStationOnly: false);
        FederationRuleResult fed005 = EvaluateTilesLevelKeyCollisions(snapshot, loadedAuthoringLinks, stationAuthoringModels, options, isStationOnly: false);
        FederationRuleResult fed006 = EvaluateStationFolderRule(snapshot.StationFolder, stationAuthoringModels, options);
        FederationRuleResult fed007 = EvaluateNoDoublePublishing(snapshot, loadedAuthoringLinks, stationAuthoringModels, isStationOnly: false);
        FederationRuleResult fed008 = EvaluateLinkAndRoleHygiene(snapshot, allLinks, stationAuthoringModels, isStationOnly: false);
        FederationRuleResult fed009 = EvaluateShowcaseIsolation(snapshot, allLinks, stationModels, isStationOnly: false);

        return new FederationCheckReport(
            new[] { fed001, fed002, fed003, fed004, fed005, fed006, fed007, fed008, fed009 },
            poseDetails);
    }

    /// <summary>Evaluates a station folder independently (no active Revit host required).</summary>
    public static FederationCheckReport EvaluateStationFolder(
        StationFolderSnapshot stationFolder,
        FederationCheckOptions? options = null)
    {
        if (stationFolder is null)
        {
            throw new ArgumentNullException(nameof(stationFolder));
        }

        FederationCheckOptions effective = options ?? FederationCheckOptions.Default;
        FederationCheckSnapshot synthetic = new()
        {
            HostStationFolder = stationFolder.StationFolderPath,
            StationFolder = stationFolder,
            Options = effective,
        };

        IReadOnlyList<StationFolderModelEntry> stationModels = (stationFolder.Models ?? Array.Empty<StationFolderModelEntry>())
            .Where(m => m != null)
            .ToList();
        List<StationFolderModelEntry> authoringModels = stationModels
            .Where(m => !IsShowcaseStationModel(m))
            .ToList();

        List<LinkPoseCheckDetail> poseDetails = new();
        FederationRuleResult fed001 = EvaluateCrsAndPose(synthetic, Array.Empty<FederationLinkSnapshot>(), authoringModels, effective, poseDetails, isStationOnly: true);
        FederationRuleResult fed002 = EvaluateUniqueModelIds(synthetic, Array.Empty<FederationLinkSnapshot>(), authoringModels, isStationOnly: true);
        FederationRuleResult fed003 = EvaluateLevelMetadataAndUniqueLevelIds(synthetic, Array.Empty<FederationLinkSnapshot>(), authoringModels, effective, isStationOnly: true);
        FederationRuleResult fed004 = EvaluateNearMissLevelDrift(synthetic, Array.Empty<FederationLinkSnapshot>(), authoringModels, effective, isStationOnly: true);
        FederationRuleResult fed005 = EvaluateTilesLevelKeyCollisions(synthetic, Array.Empty<FederationLinkSnapshot>(), authoringModels, effective, isStationOnly: true);
        FederationRuleResult fed006 = EvaluateStationFolderRule(stationFolder, authoringModels, effective);
        FederationRuleResult fed007 = EvaluateNoDoublePublishing(synthetic, Array.Empty<FederationLinkSnapshot>(), authoringModels, isStationOnly: true);
        FederationRuleResult fed008 = EvaluateLinkAndRoleHygiene(synthetic, Array.Empty<FederationLinkSnapshot>(), authoringModels, isStationOnly: true);
        FederationRuleResult fed009 = EvaluateShowcaseIsolation(synthetic, Array.Empty<FederationLinkSnapshot>(), stationModels, isStationOnly: true);

        return new FederationCheckReport(
            new[] { fed001, fed002, fed003, fed004, fed005, fed006, fed007, fed008, fed009 },
            poseDetails);
    }

    private static FederationRuleResult EvaluateCrsAndPose(
        FederationCheckSnapshot snapshot,
        IReadOnlyList<FederationLinkSnapshot> loadedAuthoringLinks,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        FederationCheckOptions options,
        List<LinkPoseCheckDetail> poseDetails,
        bool isStationOnly)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(RuleCrsAndPose);
        List<FederationIssue> issues = new();
        bool hasWarningOnly = false;

        int? referenceEpsg = null;
        string referenceDatum = string.Empty;
        string referenceGeoidModel = string.Empty;
        double? referenceGeoidOffset = null;
        string referenceLabel = string.Empty;

        if (!isStationOnly)
        {
            referenceLabel = snapshot.HostDisplayName;
            if (!snapshot.HostCrsEpsg.HasValue || !ProjectReadinessEvaluator.IsJapanPlaneRectangularEpsg(snapshot.HostCrsEpsg))
            {
                string currentEn = snapshot.HostCrsEpsg.HasValue
                    ? $"Host EPSG:{snapshot.HostCrsEpsg.Value} is outside Japan Plane Rectangular CRS (EPSG:6669-6687)."
                    : "Host model has no georeference CRS configured.";
                string currentJa = snapshot.HostCrsEpsg.HasValue
                    ? $"ホストモデルの EPSG:{snapshot.HostCrsEpsg.Value} は平面直角座標系（EPSG:6669〜6687）の範囲外です。"
                    : "ホストモデルに測地参照 CRS が設定されていません。";
                issues.Add(new FederationIssue(
                    RuleCrsAndPose,
                    ReadinessCheckStatus.Fail,
                    "HOST-CRS-INVALID",
                    currentEn,
                    currentJa,
                    modelA: snapshot.HostDisplayName));
            }
            else
            {
                referenceEpsg = snapshot.HostCrsEpsg;
                if (!snapshot.IsHostGeoreferenceVerified)
                {
                    hasWarningOnly = true;
                    issues.Add(new FederationIssue(
                        RuleCrsAndPose,
                        ReadinessCheckStatus.Warn,
                        "HOST-CRS-UNVERIFIED",
                        $"Host EPSG:{snapshot.HostCrsEpsg.Value} is configured, but georeference confidence is not yet Verified.",
                        $"ホストモデルの EPSG:{snapshot.HostCrsEpsg.Value} は設定済みですが、測地参照の信頼度が Verified ではありません。",
                        modelA: snapshot.HostDisplayName));
                }
            }

            referenceDatum = NormalizeDatum(snapshot.HostHeightDatum);
            referenceGeoidModel = NormalizeGeoidModel(snapshot.HostGeoidModel);
            referenceGeoidOffset = snapshot.HostGeoidOffsetMeters;

            foreach (FederationLinkSnapshot link in loadedAuthoringLinks)
            {
                string linkLabel = LinkLabel(link);
                long[] ids = link.LinkInstanceElementId > 0 ? new[] { link.LinkInstanceElementId } : Array.Empty<long>();

                if (referenceEpsg.HasValue)
                {
                    if (!link.CrsEpsg.HasValue)
                    {
                        issues.Add(new FederationIssue(
                            RuleCrsAndPose,
                            ReadinessCheckStatus.Fail,
                            "LINK-CRS-MISSING",
                            $"Linked model '{linkLabel}' has no configured CRS (expected EPSG:{referenceEpsg.Value} matching host).",
                            $"リンクモデル '{linkLabel}' に CRS が設定されていません（ホストと一致する EPSG:{referenceEpsg.Value} が必要です）。",
                            modelA: snapshot.HostDisplayName,
                            modelB: linkLabel,
                            elementIds: ids));
                    }
                    else if (link.CrsEpsg.Value != referenceEpsg.Value)
                    {
                        issues.Add(new FederationIssue(
                            RuleCrsAndPose,
                            ReadinessCheckStatus.Fail,
                            "LINK-CRS-MISMATCH",
                            $"CRS mismatch: host uses EPSG:{referenceEpsg.Value} but link '{linkLabel}' uses EPSG:{link.CrsEpsg.Value}.",
                            $"CRS 不一致: ホストは EPSG:{referenceEpsg.Value} ですが、リンク '{linkLabel}' は EPSG:{link.CrsEpsg.Value} です。",
                            modelA: snapshot.HostDisplayName,
                            modelB: linkLabel,
                            elementIds: ids));
                    }
                }

                string linkDatum = NormalizeDatum(link.HeightDatum);
                if (referenceDatum.Length > 0 && linkDatum.Length > 0
                    && !string.Equals(referenceDatum, linkDatum, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new FederationIssue(
                        RuleCrsAndPose,
                        ReadinessCheckStatus.Fail,
                        "LINK-HEIGHT-DATUM-MISMATCH",
                        $"Height datum mismatch: host uses '{referenceDatum}' but link '{linkLabel}' uses '{linkDatum}'.",
                        $"標高基準（heightDatum）不一致: ホストは '{referenceDatum}' ですが、リンク '{linkLabel}' は '{linkDatum}' です。",
                        modelA: snapshot.HostDisplayName,
                        modelB: linkLabel,
                        elementIds: ids));
                }

                string linkGeoid = NormalizeGeoidModel(link.GeoidModel);
                if (referenceGeoidModel.Length > 0 && linkGeoid.Length > 0
                    && !string.Equals(referenceGeoidModel, linkGeoid, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new FederationIssue(
                        RuleCrsAndPose,
                        ReadinessCheckStatus.Fail,
                        "LINK-GEOID-MODEL-MISMATCH",
                        $"Geoid model mismatch: host uses '{referenceGeoidModel}' but link '{linkLabel}' uses '{linkGeoid}'.",
                        $"ジオイドモデル不一致: ホストは '{referenceGeoidModel}' ですが、リンク '{linkLabel}' は '{linkGeoid}' です。",
                        modelA: snapshot.HostDisplayName,
                        modelB: linkLabel,
                        elementIds: ids));
                }

                if (referenceGeoidOffset.HasValue && link.GeoidOffsetMeters.HasValue)
                {
                    double diffMeters = Math.Abs(referenceGeoidOffset.Value - link.GeoidOffsetMeters.Value);
                    if (diffMeters > options.GeoidOffsetToleranceMeters + 1e-9d)
                    {
                        issues.Add(new FederationIssue(
                            RuleCrsAndPose,
                            ReadinessCheckStatus.Fail,
                            "LINK-GEOID-OFFSET-MISMATCH",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Geoid offset mismatch between host ({0:0.###} m) and link '{1}' ({2:0.###} m; delta {3:0.#} mm).",
                                referenceGeoidOffset.Value,
                                linkLabel,
                                link.GeoidOffsetMeters.Value,
                                diffMeters * 1000.0d),
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "ジオイド高オフセット不一致: ホスト ({0:0.###} m) とリンク '{1}' ({2:0.###} m、差 {3:0.#} mm)。",
                                referenceGeoidOffset.Value,
                                linkLabel,
                                link.GeoidOffsetMeters.Value,
                                diffMeters * 1000.0d),
                            modelA: snapshot.HostDisplayName,
                            modelB: linkLabel,
                            elementIds: ids,
                            metrics: new Dictionary<string, double> { ["geoidOffsetDiffMm"] = diffMeters * 1000.0d }));
                    }
                }

                if (link.ControlPoints.Count > 0)
                {
                    PoseConsistencyResult pose = PoseConsistencyEvaluator.Evaluate(link.ControlPoints, options);
                    poseDetails.Add(new LinkPoseCheckDetail(linkLabel, link.ModelId, link.LinkInstanceUniqueId, pose));
                    if (!pose.IsAligned)
                    {
                        string reasonEn = pose.IsDegenerateBaseline
                            ? string.Join("; ", pose.FailureReasonsEn)
                            : $"{pose.FormatMetricsEn()} ({string.Join("; ", pose.FailureReasonsEn)})";
                        string reasonJa = pose.IsDegenerateBaseline
                            ? string.Join("；", pose.FailureReasonsJa)
                            : $"{pose.FormatMetricsJa()}（{string.Join("；", pose.FailureReasonsJa)}）";
                        issues.Add(new FederationIssue(
                            RuleCrsAndPose,
                            ReadinessCheckStatus.Fail,
                            pose.IsDegenerateBaseline ? "LINK-POSE-DEGENERATE" : "LINK-POSE-MISMATCH",
                            $"Link '{linkLabel}' shared-coordinate 3-point pose mismatch: {reasonEn}.",
                            $"リンク '{linkLabel}' の 3 制御点共有座標姿勢不一致: {reasonJa}。",
                            modelA: snapshot.HostDisplayName,
                            modelB: linkLabel,
                            elementIds: ids,
                            metrics: new Dictionary<string, double>
                            {
                                ["originTranslationMm"] = pose.OriginTranslationMm,
                                ["maxHorizontalTranslationMm"] = pose.MaxHorizontalTranslationMm,
                                ["maxVerticalDriftMm"] = pose.MaxVerticalDriftMm,
                                ["rotationErrorDegrees"] = pose.RotationErrorDegrees,
                                ["rotationErrorArcSeconds"] = pose.RotationErrorArcSeconds,
                                ["maxScaleRatioErrorPpm"] = pose.MaxScaleRatioErrorPpm,
                            }));
                    }
                }
            }
        }

        AppendStationFolderCrsIssues(
            RuleCrsAndPose,
            stationAuthoringModels,
            referenceEpsg,
            referenceDatum,
            referenceGeoidModel,
            referenceGeoidOffset,
            referenceLabel,
            options,
            issues);

        bool anyFail = issues.Any(i => i.Status == ReadinessCheckStatus.Fail);
        bool anyWarn = issues.Any(i => i.Status == ReadinessCheckStatus.Warn) || hasWarningOnly;

        if (anyFail)
        {
            return BuildRuleResult(
                rule,
                ItemCrsAndPose,
                ReadinessCheckStatus.Fail,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        if (anyWarn)
        {
            return BuildRuleResult(
                rule,
                ItemCrsAndPose,
                ReadinessCheckStatus.Warn,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        string passEn = isStationOnly
            ? $"All {stationAuthoringModels.Count} station-folder model(s) share consistent Japan Plane Rectangular CRS, height datum, and geoid model."
            : poseDetails.Count > 0
                ? $"Verified EPSG:{snapshot.HostCrsEpsg} and 3-control-point shared-coordinate pose across {poseDetails.Count} linked model(s)."
                : $"Verified on EPSG:{snapshot.HostCrsEpsg}" + (string.IsNullOrWhiteSpace(snapshot.HostCrsName) ? "." : $" ({snapshot.HostCrsName}).");
        string passJa = isStationOnly
            ? $"駅フォルダ内の全 {stationAuthoringModels.Count} モデルで平面直角座標系・標高基準・ジオイドモデルが一致しています。"
            : poseDetails.Count > 0
                ? $"EPSG:{snapshot.HostCrsEpsg} および {poseDetails.Count} 件のリンクモデルの 3 制御点共有座標姿勢が一致しています。"
                : $"EPSG:{snapshot.HostCrsEpsg} で検証済みです。";

        return BuildRuleResult(rule, ItemCrsAndPose, ReadinessCheckStatus.Pass, passEn, passJa, issues);
    }

    private static void AppendStationFolderCrsIssues(
        string ruleId,
        IReadOnlyList<StationFolderModelEntry> stationModels,
        int? refEpsg,
        string refDatum,
        string refGeoid,
        double? refGeoidOffset,
        string refLabel,
        FederationCheckOptions options,
        List<FederationIssue> issues)
    {
        if (stationModels.Count == 0)
        {
            return;
        }

        int? baselineEpsg = refEpsg;
        string baselineEpsgModel = refLabel;
        string baselineDatum = refDatum;
        string baselineDatumModel = refLabel;
        string baselineGeoid = refGeoid;
        string baselineGeoidModel = refLabel;
        double? baselineGeoidOffset = refGeoidOffset;
        string baselineGeoidOffsetModel = refLabel;

        foreach (StationFolderModelEntry model in stationModels)
        {
            string label = model.DisplayName;
            if (!model.CrsEpsg.HasValue)
            {
                issues.Add(new FederationIssue(
                    ruleId,
                    ReadinessCheckStatus.Fail,
                    "STATION-CRS-MISSING",
                    $"Station-folder model '{label}' has no CRS EPSG configured.",
                    $"駅フォルダモデル '{label}' に CRS EPSG が設定されていません。",
                    modelA: label));
            }
            else if (!ProjectReadinessEvaluator.IsJapanPlaneRectangularEpsg(model.CrsEpsg))
            {
                issues.Add(new FederationIssue(
                    ruleId,
                    ReadinessCheckStatus.Fail,
                    "STATION-CRS-OUT-OF-RANGE",
                    $"Station-folder model '{label}' uses EPSG:{model.CrsEpsg.Value}, outside Japan Plane Rectangular CRS (EPSG:6669-6687).",
                    $"駅フォルダモデル '{label}' の EPSG:{model.CrsEpsg.Value} は平面直角座標系（EPSG:6669〜6687）の範囲外です。",
                    modelA: label));
            }
            else if (!baselineEpsg.HasValue)
            {
                baselineEpsg = model.CrsEpsg;
                baselineEpsgModel = label;
            }
            else if (model.CrsEpsg.Value != baselineEpsg.Value)
            {
                issues.Add(new FederationIssue(
                    ruleId,
                    ReadinessCheckStatus.Fail,
                    "STATION-CRS-MISMATCH",
                    $"Station-folder CRS mismatch: '{baselineEpsgModel}' uses EPSG:{baselineEpsg.Value} but '{label}' uses EPSG:{model.CrsEpsg.Value}.",
                    $"駅フォルダ内の CRS 不一致: '{baselineEpsgModel}' は EPSG:{baselineEpsg.Value}、'{label}' は EPSG:{model.CrsEpsg.Value} です。",
                    modelA: baselineEpsgModel,
                    modelB: label));
            }

            string datum = NormalizeDatum(model.HeightDatum);
            if (datum.Length > 0)
            {
                if (baselineDatum.Length == 0)
                {
                    baselineDatum = datum;
                    baselineDatumModel = label;
                }
                else if (!string.Equals(datum, baselineDatum, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new FederationIssue(
                        ruleId,
                        ReadinessCheckStatus.Fail,
                        "STATION-HEIGHT-DATUM-MISMATCH",
                        $"Station-folder heightDatum mismatch: '{baselineDatumModel}' uses '{baselineDatum}' but '{label}' uses '{datum}'.",
                        $"駅フォルダ内の heightDatum 不一致: '{baselineDatumModel}' は '{baselineDatum}'、'{label}' は '{datum}' です。",
                        modelA: baselineDatumModel,
                        modelB: label));
                }
            }

            string geoid = NormalizeGeoidModel(model.GeoidModel);
            if (geoid.Length > 0)
            {
                if (baselineGeoid.Length == 0)
                {
                    baselineGeoid = geoid;
                    baselineGeoidModel = label;
                }
                else if (!string.Equals(geoid, baselineGeoid, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new FederationIssue(
                        ruleId,
                        ReadinessCheckStatus.Fail,
                        "STATION-GEOID-MODEL-MISMATCH",
                        $"Station-folder geoidModel mismatch: '{baselineGeoidModel}' uses '{baselineGeoid}' but '{label}' uses '{geoid}'.",
                        $"駅フォルダ内の geoidModel 不一致: '{baselineGeoidModel}' は '{baselineGeoid}'、'{label}' は '{geoid}' です。",
                        modelA: baselineGeoidModel,
                        modelB: label));
                }
            }

            if (model.GeoidOffsetMeters.HasValue)
            {
                if (!baselineGeoidOffset.HasValue)
                {
                    baselineGeoidOffset = model.GeoidOffsetMeters;
                    baselineGeoidOffsetModel = label;
                }
                else
                {
                    double diffMeters = Math.Abs(model.GeoidOffsetMeters.Value - baselineGeoidOffset.Value);
                    if (diffMeters > options.GeoidOffsetToleranceMeters + 1e-9d)
                    {
                        issues.Add(new FederationIssue(
                            ruleId,
                            ReadinessCheckStatus.Fail,
                            "STATION-GEOID-OFFSET-MISMATCH",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "Station-folder geoid offset mismatch: '{0}' ({1:0.###} m) vs '{2}' ({3:0.###} m; delta {4:0.#} mm).",
                                baselineGeoidOffsetModel,
                                baselineGeoidOffset.Value,
                                label,
                                model.GeoidOffsetMeters.Value,
                                diffMeters * 1000.0d),
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "駅フォルダ内のジオイド高オフセット不一致: '{0}' ({1:0.###} m) と '{2}' ({3:0.###} m、差 {4:0.#} mm)。",
                                baselineGeoidOffsetModel,
                                baselineGeoidOffset.Value,
                                label,
                                model.GeoidOffsetMeters.Value,
                                diffMeters * 1000.0d),
                            modelA: baselineGeoidOffsetModel,
                            modelB: label,
                            metrics: new Dictionary<string, double> { ["geoidOffsetDiffMm"] = diffMeters * 1000.0d }));
                    }
                }
            }
        }
    }

    private static FederationRuleResult EvaluateUniqueModelIds(
        FederationCheckSnapshot snapshot,
        IReadOnlyList<FederationLinkSnapshot> loadedAuthoringLinks,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        bool isStationOnly)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(RuleUniqueModelId);
        List<FederationIssue> issues = new();
        List<ModelIdentityHolder> holders = new();

        if (!isStationOnly)
        {
            string hostId = (snapshot.HostModelId ?? string.Empty).Trim();
            if (hostId.Length == 0)
            {
                issues.Add(new FederationIssue(
                    RuleUniqueModelId,
                    ReadinessCheckStatus.Fail,
                    "HOST-MODEL-ID-MISSING",
                    $"Host model '{snapshot.HostDisplayName}' has no stable GeoProjectInfo.ModelId.",
                    $"ホストモデル '{snapshot.HostDisplayName}' に GeoProjectInfo.ModelId が設定されていません。",
                    modelA: snapshot.HostDisplayName));
            }
            else
            {
                holders.Add(new ModelIdentityHolder(
                    "host",
                    snapshot.HostDisplayName,
                    hostId,
                    NormalizePath(snapshot.HostDocumentPath),
                    snapshot.HostDocumentKey.Trim(),
                    0));
            }

            // Group loaded links by normalized source path so placing the SAME linked .rvt twice
            // does not false-fail as two Save-As clones, while two distinct files with the same ModelId DO fail.
            HashSet<string> seenLinkPaths = new(StringComparer.OrdinalIgnoreCase);
            foreach (FederationLinkSnapshot link in loadedAuthoringLinks)
            {
                string linkLabel = LinkLabel(link);
                string normalizedPath = NormalizePath(link.SourcePath);
                if (normalizedPath.Length > 0 && !seenLinkPaths.Add(normalizedPath))
                {
                    continue;
                }

                long[] ids = link.LinkInstanceElementId > 0 ? new[] { link.LinkInstanceElementId } : Array.Empty<long>();
                if (string.IsNullOrWhiteSpace(link.ModelId))
                {
                    issues.Add(new FederationIssue(
                        RuleUniqueModelId,
                        ReadinessCheckStatus.Fail,
                        "LINK-MODEL-ID-MISSING",
                        $"Loaded link '{linkLabel}' has no stable GeoProjectInfo.ModelId.",
                        $"読み込み済みリンク '{linkLabel}' に GeoProjectInfo.ModelId が設定されていません。",
                        modelA: linkLabel,
                        elementIds: ids));
                }
                else
                {
                    holders.Add(new ModelIdentityHolder(
                        "link",
                        linkLabel,
                        link.ModelId.Trim(),
                        normalizedPath,
                        string.Empty,
                        link.LinkInstanceElementId));
                }
            }
        }

        AppendStationFolderModelIdIssues(RuleUniqueModelId, snapshot, stationAuthoringModels, holders, isStationOnly, issues);

        foreach (IGrouping<string, ModelIdentityHolder> group in holders
            .GroupBy(h => h.ModelId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            List<ModelIdentityHolder> members = group.ToList();
            string names = string.Join(", ", members.Select(m => m.Label));
            long[] elementIds = members.Where(m => m.ElementId > 0).Select(m => m.ElementId).Distinct().ToArray();
            issues.Add(new FederationIssue(
                RuleUniqueModelId,
                ReadinessCheckStatus.Fail,
                "DUPLICATE-MODEL-ID",
                $"Duplicate GeoProjectInfo.ModelId '{group.Key}' shared by {members.Count} models ({names}); regenerate a fresh ModelId to fix Save-As identity cloning.",
                $"GeoProjectInfo.ModelId '{group.Key}' が {members.Count} 個のモデル（{names}）で重複しています。名前を付けて保存による ID 複製を解消するため新規 ModelId を発行してください。",
                modelA: members[0].Label,
                modelB: members[1].Label,
                elementIds: elementIds));
        }

        if (issues.Count > 0)
        {
            return BuildRuleResult(
                rule,
                ItemUniqueModelId,
                ReadinessCheckStatus.Fail,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        string passEn = isStationOnly
            ? $"All {stationAuthoringModels.Count} station-folder model(s) have unique stable ModelIds."
            : loadedAuthoringLinks.Count == 0
                ? $"Host ModelId is present ({snapshot.HostModelId}); no linked models."
                : $"Host ModelId and all {loadedAuthoringLinks.Count} loaded authoring link(s) carry unique stable ModelIds.";
        string passJa = isStationOnly
            ? $"駅フォルダ内の全 {stationAuthoringModels.Count} モデルが一意の ModelId を保持しています。"
            : loadedAuthoringLinks.Count == 0
                ? $"ホストの ModelId（{snapshot.HostModelId}）が設定されています（リンクなし）。"
                : $"ホストおよび全 {loadedAuthoringLinks.Count} 件の読み込み済みリンクが一意の ModelId を保持しています。";

        return BuildRuleResult(rule, ItemUniqueModelId, ReadinessCheckStatus.Pass, passEn, passJa, issues);
    }

    private static void AppendStationFolderModelIdIssues(
        string ruleId,
        FederationCheckSnapshot snapshot,
        IReadOnlyList<StationFolderModelEntry> stationModels,
        List<ModelIdentityHolder> holders,
        bool isStationOnly,
        List<FederationIssue> issues)
    {
        string hostModelId = isStationOnly ? string.Empty : (snapshot.HostModelId ?? string.Empty).Trim();
        string hostDocKey = isStationOnly ? string.Empty : (snapshot.HostDocumentKey ?? string.Empty).Trim();
        HashSet<string> knownLinkModelIds = isStationOnly
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(
                (snapshot.LinkedModels ?? Array.Empty<FederationLinkSnapshot>())
                    .Where(l => l != null && l.IsLoaded && !string.IsNullOrWhiteSpace(l.ModelId))
                    .Select(l => l.ModelId.Trim()),
                StringComparer.OrdinalIgnoreCase);

        foreach (StationFolderModelEntry model in stationModels)
        {
            if (string.IsNullOrWhiteSpace(model.ModelId))
            {
                issues.Add(new FederationIssue(
                    ruleId,
                    ReadinessCheckStatus.Fail,
                    "STATION-MODEL-ID-MISSING",
                    $"Station-folder model '{model.DisplayName}' has no stable ModelId.",
                    $"駅フォルダモデル '{model.DisplayName}' に ModelId が設定されていません。",
                    modelA: model.DisplayName));
                continue;
            }

            string id = model.ModelId.Trim();

            // Skip adding a station-folder entry that simply mirrors the active host (unless DocumentKey proves it's a Save-As clone)
            // or mirrors an already-tracked loaded link.
            if (!isStationOnly && hostModelId.Length > 0 && string.Equals(id, hostModelId, StringComparison.OrdinalIgnoreCase))
            {
                bool isDifferentSourceFile = hostDocKey.Length > 0
                    && model.DocumentKey.Length > 0
                    && !string.Equals(hostDocKey, model.DocumentKey, StringComparison.OrdinalIgnoreCase);
                if (!isDifferentSourceFile)
                {
                    continue;
                }
            }

            if (!isStationOnly && knownLinkModelIds.Contains(id))
            {
                // Same link already tracked in holders; still keep a second station entry if two station folders share this ID.
                bool alreadyAddedForStation = holders.Any(h => h.Kind == "station" && string.Equals(h.ModelId, id, StringComparison.OrdinalIgnoreCase));
                if (!alreadyAddedForStation)
                {
                    continue;
                }
            }

            holders.Add(new ModelIdentityHolder(
                "station",
                model.DisplayName,
                id,
                NormalizePath(string.IsNullOrWhiteSpace(model.PublishedPackagePath) ? model.GisModelFilePath : model.PublishedPackagePath),
                model.DocumentKey,
                0));
        }
    }

    private static FederationRuleResult EvaluateLevelMetadataAndUniqueLevelIds(
        FederationCheckSnapshot snapshot,
        IReadOnlyList<FederationLinkSnapshot> loadedAuthoringLinks,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        FederationCheckOptions options,
        bool isStationOnly)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(RuleUniqueLevelId);
        List<FederationIssue> issues = new();
        List<ModelLevelScope> scopes = CollectModelLevelScopes(snapshot, loadedAuthoringLinks, stationAuthoringModels, isStationOnly);

        if (!isStationOnly)
        {
            IReadOnlyList<FederationLevelSnapshot> hostLevels = snapshot.HostLevels ?? Array.Empty<FederationLevelSnapshot>();
            if (hostLevels.Count == 0)
            {
                issues.Add(new FederationIssue(
                    RuleUniqueLevelId,
                    ReadinessCheckStatus.Fail,
                    "HOST-NO-LEVELS",
                    "Host model has no levels.",
                    "ホストモデルにレベルが存在しません。",
                    modelA: snapshot.HostDisplayName));
            }
            else
            {
                CheckIntraModelLevelMetadata(RuleUniqueLevelId, snapshot.HostDisplayName, hostLevels, requireMetadata: true, issues);
            }
        }

        foreach (ModelLevelScope scope in scopes)
        {
            if (!isStationOnly && scope.IsHost)
            {
                CheckIntraModelDuplicateLevelIds(RuleUniqueLevelId, scope.ModelLabel, scope.Levels, issues);
                continue;
            }

            CheckIntraModelLevelMetadata(RuleUniqueLevelId, scope.ModelLabel, scope.Levels, requireMetadata: false, issues);
            CheckIntraModelDuplicateLevelIds(RuleUniqueLevelId, scope.ModelLabel, scope.Levels, issues);
        }

        AppendCrossModelDuplicateLevelIdIssues(RuleUniqueLevelId, scopes, options, issues);

        if (issues.Count > 0)
        {
            return BuildRuleResult(
                rule,
                ItemUniqueLevelId,
                ReadinessCheckStatus.Fail,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        string passEn = isStationOnly
            ? $"All levels across {stationAuthoringModels.Count} station-folder model(s) have unique level_id values."
            : $"All levels across host and {loadedAuthoringLinks.Count} loaded link(s) have valid metadata and unique level_id values.";
        string passJa = isStationOnly
            ? $"駅フォルダ内の全 {stationAuthoringModels.Count} モデルのレベルに重複のない level_id が設定されています。"
            : $"ホストおよび {loadedAuthoringLinks.Count} 件の読み込み済みリンクの全レベルでメタデータと level_id 一意性が満たされています。";

        return BuildRuleResult(rule, ItemUniqueLevelId, ReadinessCheckStatus.Pass, passEn, passJa, issues);
    }

    private static void CheckIntraModelLevelMetadata(
        string ruleId,
        string modelLabel,
        IReadOnlyList<FederationLevelSnapshot> levels,
        bool requireMetadata,
        List<FederationIssue> issues)
    {
        if (levels.Count == 0)
        {
            return;
        }

        foreach (IGrouping<int, FederationLevelSnapshot> dupOrd in levels
            .Where(l => l.Ordinal.HasValue)
            .GroupBy(l => l.Ordinal!.Value)
            .Where(g => g.Count() > 1))
        {
            string names = string.Join("/", dupOrd.Select(l => l.Name));
            issues.Add(new FederationIssue(
                ruleId,
                ReadinessCheckStatus.Fail,
                "DUPLICATE-ORDINAL",
                $"Model '{modelLabel}' has duplicate ordinal {dupOrd.Key} ({names}).",
                $"モデル '{modelLabel}' 内で ordinal {dupOrd.Key} が重複しています（{names}）。",
                modelA: modelLabel,
                elementIds: dupOrd.Select(l => l.ElementId)));
        }

        foreach (IGrouping<string, FederationLevelSnapshot> dupShort in levels
            .Where(l => !string.IsNullOrWhiteSpace(l.ShortName))
            .GroupBy(l => l.ShortName.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            string names = string.Join("/", dupShort.Select(l => l.Name));
            issues.Add(new FederationIssue(
                ruleId,
                ReadinessCheckStatus.Fail,
                "DUPLICATE-SHORT-NAME",
                $"Model '{modelLabel}' has duplicate short_name '{dupShort.Key}' ({names}).",
                $"モデル '{modelLabel}' 内で short_name '{dupShort.Key}' が重複しています（{names}）。",
                modelA: modelLabel,
                elementIds: dupShort.Select(l => l.ElementId)));
        }

        if (requireMetadata)
        {
            List<FederationLevelSnapshot> checkedLevels = levels.Any(l => l.HasFloors)
                ? levels.Where(l => l.HasFloors).ToList()
                : levels.ToList();
            List<FederationLevelSnapshot> missing = checkedLevels
                .Where(l => !l.Ordinal.HasValue || string.IsNullOrWhiteSpace(l.ShortName))
                .ToList();
            if (missing.Count > 0)
            {
                string names = DescribeList(missing.Select(l => l.Name).ToList());
                issues.Add(new FederationIssue(
                    ruleId,
                    ReadinessCheckStatus.Fail,
                    "MISSING-LEVEL-METADATA",
                    $"Model '{modelLabel}' has {missing.Count} level(s) missing ordinal or short_name ({names}).",
                    $"モデル '{modelLabel}' で {missing.Count} 件のレベルに ordinal または short_name が未設定です（{names}）。",
                    modelA: modelLabel,
                    elementIds: missing.Select(l => l.ElementId)));
            }
        }
    }

    private static void CheckIntraModelDuplicateLevelIds(
        string ruleId,
        string modelLabel,
        IReadOnlyList<FederationLevelSnapshot> levels,
        List<FederationIssue> issues)
    {
        foreach (IGrouping<string, FederationLevelSnapshot> dup in levels
            .Where(l => !string.IsNullOrWhiteSpace(l.LevelId))
            .GroupBy(l => l.LevelId.Trim(), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            string names = string.Join("/", dup.Select(l => l.Name));
            issues.Add(new FederationIssue(
                ruleId,
                ReadinessCheckStatus.Fail,
                "DUPLICATE-LEVEL-ID-INTRA",
                $"Model '{modelLabel}' has duplicate level_id '{dup.Key}' on levels ({names}).",
                $"モデル '{modelLabel}' 内で level_id '{dup.Key}' が複数レベル（{names}）に重複しています。",
                modelA: modelLabel,
                elementIds: dup.Select(l => l.ElementId)));
        }
    }

    private static void AppendCrossModelDuplicateLevelIdIssues(
        string ruleId,
        IReadOnlyList<ModelLevelScope> scopes,
        FederationCheckOptions options,
        List<FederationIssue> issues)
    {
        List<(ModelLevelScope Scope, FederationLevelSnapshot Level)> allEntries = new();
        foreach (ModelLevelScope scope in scopes)
        {
            foreach (FederationLevelSnapshot level in scope.Levels)
            {
                if (!string.IsNullOrWhiteSpace(level.LevelId))
                {
                    allEntries.Add((scope, level));
                }
            }
        }

        foreach (IGrouping<string, (ModelLevelScope Scope, FederationLevelSnapshot Level)> group in allEntries
            .GroupBy(e => e.Level.LevelId.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            List<(ModelLevelScope Scope, FederationLevelSnapshot Level)> distinctModels = group
                .GroupBy(e => e.Scope.ScopeKey, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();
            if (distinctModels.Count <= 1)
            {
                continue;
            }

            if (options.AllowSharedCanonicalLevelIdsAcrossModels && AreAllSharedLevelsAligned(distinctModels, options))
            {
                continue;
            }

            string summary = string.Join(", ", distinctModels.Select(item => $"{item.Scope.ModelLabel}:{item.Level.Name}"));
            issues.Add(new FederationIssue(
                ruleId,
                ReadinessCheckStatus.Fail,
                "DUPLICATE-LEVEL-ID-CROSS",
                $"Duplicate level_id '{group.Key}' shared across {distinctModels.Count} distinct models ({summary}); each model must mint unique level_id values.",
                $"level_id '{group.Key}' が {distinctModels.Count} 個の異なるモデル（{summary}）間で重複しています。各モデルで一意の level_id を発行してください。",
                modelA: distinctModels[0].Scope.ModelLabel,
                modelB: distinctModels[1].Scope.ModelLabel,
                elementIds: distinctModels.Select(d => d.Level.ElementId)));
        }
    }

    private static bool AreAllSharedLevelsAligned(
        IReadOnlyList<(ModelLevelScope Scope, FederationLevelSnapshot Level)> items,
        FederationCheckOptions options)
    {
        FederationLevelSnapshot baseline = items[0].Level;
        for (int i = 1; i < items.Count; i++)
        {
            FederationLevelSnapshot other = items[i].Level;
            if (!baseline.HasElevation || !other.HasElevation)
            {
                return false;
            }

            double deltaMm = Math.Abs(baseline.ProjectElevationMeters!.Value - other.ProjectElevationMeters!.Value) * 1000.0d;
            if (deltaMm >= options.LevelAlignedToleranceMm - BoundaryEpsilonMm)
            {
                return false;
            }

            if (baseline.Ordinal.HasValue && other.Ordinal.HasValue && baseline.Ordinal.Value != other.Ordinal.Value)
            {
                return false;
            }

            if (baseline.ShortName.Length > 0 && other.ShortName.Length > 0
                && !string.Equals(baseline.ShortName, other.ShortName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static FederationRuleResult EvaluateNearMissLevelDrift(
        FederationCheckSnapshot snapshot,
        IReadOnlyList<FederationLinkSnapshot> loadedAuthoringLinks,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        FederationCheckOptions options,
        bool isStationOnly)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(RuleNearMissLevelDrift);
        List<FederationIssue> issues = new();
        HashSet<string> reportedPairs = new(StringComparer.OrdinalIgnoreCase);
        List<ModelLevelScope> scopes = CollectModelLevelScopes(snapshot, loadedAuthoringLinks, stationAuthoringModels, isStationOnly);

        for (int i = 0; i < scopes.Count; i++)
        {
            for (int j = i + 1; j < scopes.Count; j++)
            {
                CompareLevelSetsForDrift(
                    RuleNearMissLevelDrift,
                    scopes[i].ModelLabel,
                    scopes[i].Levels,
                    scopes[j].ModelLabel,
                    scopes[j].Levels,
                    options,
                    reportedPairs,
                    issues);
            }
        }

        if (issues.Count > 0)
        {
            return BuildRuleResult(
                rule,
                ItemNearMissLevelDrift,
                ReadinessCheckStatus.Fail,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        string passEn = string.Format(
            CultureInfo.InvariantCulture,
            "No near-miss level elevation drift ({0:0.#}-{1:0.#} mm) or aligned-level metadata mismatch detected across federated models.",
            options.LevelAlignedToleranceMm,
            options.LevelNearMissMaxMm);
        string passJa = string.Format(
            CultureInfo.InvariantCulture,
            "フェデレーションモデル間で {0:0.#}〜{1:0.#} mm のレベル標高ニアミス誤差および一致レベルのメタデータ不一致は検出されませんでした。",
            options.LevelAlignedToleranceMm,
            options.LevelNearMissMaxMm);

        return BuildRuleResult(rule, ItemNearMissLevelDrift, ReadinessCheckStatus.Pass, passEn, passJa, issues);
    }

    private static void CompareLevelSetsForDrift(
        string ruleId,
        string modelALabel,
        IReadOnlyList<FederationLevelSnapshot> levelsA,
        string modelBLabel,
        IReadOnlyList<FederationLevelSnapshot> levelsB,
        FederationCheckOptions options,
        HashSet<string> reportedPairs,
        List<FederationIssue> issues)
    {
        List<FederationLevelSnapshot> elevA = levelsA.Where(l => l.HasElevation).ToList();
        List<FederationLevelSnapshot> elevB = levelsB.Where(l => l.HasElevation).ToList();
        if (elevA.Count == 0 || elevB.Count == 0)
        {
            return;
        }

        foreach (FederationLevelSnapshot levelB in elevB)
        {
            CheckNearestLevelAgainstSet(ruleId, modelBLabel, levelB, modelALabel, elevA, options, reportedPairs, issues);
        }

        foreach (FederationLevelSnapshot levelA in elevA)
        {
            CheckNearestLevelAgainstSet(ruleId, modelALabel, levelA, modelBLabel, elevB, options, reportedPairs, issues);
        }
    }

    private static void CheckNearestLevelAgainstSet(
        string ruleId,
        string sourceModel,
        FederationLevelSnapshot sourceLevel,
        string targetModel,
        IReadOnlyList<FederationLevelSnapshot> targetLevels,
        FederationCheckOptions options,
        HashSet<string> reportedPairs,
        List<FederationIssue> issues)
    {
        FederationLevelSnapshot? nearest = null;
        double minDeltaMm = double.MaxValue;

        foreach (FederationLevelSnapshot candidate in targetLevels)
        {
            double deltaMm = Math.Abs(sourceLevel.ProjectElevationMeters!.Value - candidate.ProjectElevationMeters!.Value) * 1000.0d;
            if (deltaMm < minDeltaMm)
            {
                minDeltaMm = deltaMm;
                nearest = candidate;
            }
        }

        if (nearest is null)
        {
            return;
        }

        string pairKey = CanonicalPairKey(
            $"{sourceModel}|{sourceLevel.Name}|{sourceLevel.ElementId}|{sourceLevel.LevelId}",
            $"{targetModel}|{nearest.Name}|{nearest.ElementId}|{nearest.LevelId}");

        if (minDeltaMm >= options.LevelAlignedToleranceMm - BoundaryEpsilonMm
            && minDeltaMm <= options.LevelNearMissMaxMm + BoundaryEpsilonMm)
        {
            if (!reportedPairs.Add("DRIFT:" + pairKey))
            {
                return;
            }

            issues.Add(new FederationIssue(
                ruleId,
                ReadinessCheckStatus.Fail,
                "NEAR-MISS-LEVEL-DRIFT",
                string.Format(
                    CultureInfo.InvariantCulture,
                    "Near-miss level elevation drift of {0:0.##} mm (within {1:0.#}-{2:0.#} mm fail band) between '{3}:{4}' ({5:0.####} m) and '{6}:{7}' ({8:0.####} m); align within < {1:0.#} mm so UnitExtractor to_level matching never drops to null.",
                    minDeltaMm,
                    options.LevelAlignedToleranceMm,
                    options.LevelNearMissMaxMm,
                    sourceModel,
                    sourceLevel.Name,
                    sourceLevel.ProjectElevationMeters!.Value,
                    targetModel,
                    nearest.Name,
                    nearest.ProjectElevationMeters!.Value),
                string.Format(
                    CultureInfo.InvariantCulture,
                    "レベル標高のニアミスずれ {0:0.##} mm（エラー帯域 {1:0.#}〜{2:0.#} mm）を検出: '{3}:{4}' ({5:0.####} m) と '{6}:{7}' ({8:0.####} m)。UnitExtractor の to_level 解決が欠落しないよう {1:0.#} mm 未満に揃えてください。",
                    minDeltaMm,
                    options.LevelAlignedToleranceMm,
                    options.LevelNearMissMaxMm,
                    sourceModel,
                    sourceLevel.Name,
                    sourceLevel.ProjectElevationMeters!.Value,
                    targetModel,
                    nearest.Name,
                    nearest.ProjectElevationMeters!.Value),
                modelA: sourceModel,
                modelB: targetModel,
                elementIds: new[] { sourceLevel.ElementId, nearest.ElementId },
                metrics: new Dictionary<string, double>
                {
                    ["deltaZMm"] = minDeltaMm,
                    ["elevationAMeters"] = sourceLevel.ProjectElevationMeters!.Value,
                    ["elevationBMeters"] = nearest.ProjectElevationMeters!.Value,
                }));
            return;
        }

        if (minDeltaMm < options.LevelAlignedToleranceMm - BoundaryEpsilonMm)
        {
            bool ordinalMismatch = sourceLevel.Ordinal.HasValue
                && nearest.Ordinal.HasValue
                && sourceLevel.Ordinal.Value != nearest.Ordinal.Value;
            bool shortNameMismatch = sourceLevel.ShortName.Length > 0
                && nearest.ShortName.Length > 0
                && !string.Equals(sourceLevel.ShortName, nearest.ShortName, StringComparison.OrdinalIgnoreCase);

            if ((ordinalMismatch || shortNameMismatch) && reportedPairs.Add("META:" + pairKey))
            {
                List<string> diffsEn = new();
                List<string> diffsJa = new();
                if (ordinalMismatch)
                {
                    diffsEn.Add($"ordinal {sourceLevel.Ordinal!.Value} vs {nearest.Ordinal!.Value}");
                    diffsJa.Add($"ordinal {sourceLevel.Ordinal!.Value} と {nearest.Ordinal!.Value}");
                }

                if (shortNameMismatch)
                {
                    diffsEn.Add($"short_name '{sourceLevel.ShortName}' vs '{nearest.ShortName}'");
                    diffsJa.Add($"short_name '{sourceLevel.ShortName}' と '{nearest.ShortName}'");
                }

                issues.Add(new FederationIssue(
                    ruleId,
                    ReadinessCheckStatus.Fail,
                    "ALIGNED-LEVEL-METADATA-MISMATCH",
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "Aligned levels '{0}:{1}' and '{2}:{3}' (deltaZ {4:0.##} mm) have mismatched {5}.",
                        sourceModel,
                        sourceLevel.Name,
                        targetModel,
                        nearest.Name,
                        minDeltaMm,
                        string.Join(" and ", diffsEn)),
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "標高が一致するレベル '{0}:{1}' と '{2}:{3}'（差 {4:0.##} mm）の間で {5} が一致していません。",
                        sourceModel,
                        sourceLevel.Name,
                        targetModel,
                        nearest.Name,
                        minDeltaMm,
                        string.Join(" および ", diffsJa)),
                    modelA: sourceModel,
                    modelB: targetModel,
                    elementIds: new[] { sourceLevel.ElementId, nearest.ElementId },
                    metrics: new Dictionary<string, double> { ["deltaZMm"] = minDeltaMm }));
            }
        }
    }

    private static FederationRuleResult EvaluateTilesLevelKeyCollisions(
        FederationCheckSnapshot snapshot,
        IReadOnlyList<FederationLinkSnapshot> loadedAuthoringLinks,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        FederationCheckOptions options,
        bool isStationOnly)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(RuleTilesLevelKeyCollision);
        List<FederationIssue> issues = new();
        List<ModelLevelScope> scopes = CollectModelLevelScopes(snapshot, loadedAuthoringLinks, stationAuthoringModels, isStationOnly);

        AppendTilesLevelKeyCollisionIssues(RuleTilesLevelKeyCollision, scopes, stationAuthoringModels, options, issues);

        if (issues.Count > 0)
        {
            return BuildRuleResult(
                rule,
                ItemTilesLevelKeyCollision,
                ReadinessCheckStatus.Fail,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        string passEn = "No Tiles3DLevelGrouper normalized level-key collisions detected across federated levels.";
        string passJa = "フェデレーション対象のレベル間で Tiles3DLevelGrouper の正規化レベルキー衝突は検出されませんでした。";
        return BuildRuleResult(rule, ItemTilesLevelKeyCollision, ReadinessCheckStatus.Pass, passEn, passJa, issues);
    }

    private static void AppendTilesLevelKeyCollisionIssues(
        string ruleId,
        IReadOnlyList<ModelLevelScope> scopes,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        FederationCheckOptions options,
        List<FederationIssue> issues)
    {
        foreach (ModelLevelScope scope in scopes)
        {
            foreach (IGrouping<string, FederationLevelSnapshot> dup in scope.Levels
                .GroupBy(l => l.TilesLevelKey, StringComparer.Ordinal)
                .Where(g => g.Count() > 1))
            {
                string names = string.Join(", ", dup.Select(l => $"'{l.Name}'"));
                issues.Add(new FederationIssue(
                    ruleId,
                    ReadinessCheckStatus.Fail,
                    "TILES-KEY-COLLISION-INTRA",
                    $"Model '{scope.ModelLabel}' has {dup.Count()} distinct levels ({names}) that collide onto the same Tiles3DLevelGrouper key '{dup.Key}'.",
                    $"モデル '{scope.ModelLabel}' 内の {dup.Count()} 個のレベル（{names}）が同一の Tiles3DLevelGrouper 正規化キー '{dup.Key}' に衝突しています。",
                    modelA: scope.ModelLabel,
                    elementIds: dup.Select(l => l.ElementId)));
            }
        }

        foreach (StationFolderModelEntry model in stationAuthoringModels)
        {
            foreach (IGrouping<string, StationFolderLevelMapEntry> dup in model.LevelMapEntries
                .Where(e => !string.IsNullOrWhiteSpace(e.TilesLevelKey))
                .GroupBy(e => LevelKey.Build(e.TilesLevelKey), StringComparer.Ordinal))
            {
                List<StationFolderLevelMapEntry> distinctIds = dup
                    .Where(e => !string.IsNullOrWhiteSpace(e.GisLevelId))
                    .GroupBy(e => e.GisLevelId, StringComparer.OrdinalIgnoreCase)
                    .Select(g => g.First())
                    .ToList();
                if (distinctIds.Count > 1)
                {
                    string desc = string.Join(", ", distinctIds.Select(e => $"{e.Name} ({e.GisLevelId})"));
                    issues.Add(new FederationIssue(
                        ruleId,
                        ReadinessCheckStatus.Fail,
                        "TILES-LEVELMAP-COLLISION",
                        $"Station package '{model.DisplayName}' maps {distinctIds.Count} distinct GIS level_ids ({desc}) onto the same 3D Tiles key '{dup.Key}'.",
                        $"駅フォルダパッケージ '{model.DisplayName}' で {distinctIds.Count} 個の異なる GIS level_id（{desc}）が同一の 3D Tiles キー '{dup.Key}' に衝突しています。",
                        modelA: model.DisplayName));
                }
            }
        }

        List<(ModelLevelScope Scope, FederationLevelSnapshot Level)> allLevels = scopes
            .SelectMany(scope => scope.Levels.Select(level => (scope, level)))
            .ToList();

        HashSet<string> reportedCross = new(StringComparer.OrdinalIgnoreCase);

        foreach (IGrouping<string, (ModelLevelScope Scope, FederationLevelSnapshot Level)> keyGroup in allLevels
            .GroupBy(item => item.Level.TilesLevelKey, StringComparer.Ordinal))
        {
            List<(ModelLevelScope Scope, FederationLevelSnapshot Level)> items = keyGroup.ToList();
            for (int i = 0; i < items.Count; i++)
            {
                for (int j = i + 1; j < items.Count; j++)
                {
                    (ModelLevelScope scopeA, FederationLevelSnapshot levelA) = items[i];
                    (ModelLevelScope scopeB, FederationLevelSnapshot levelB) = items[j];
                    if (string.Equals(scopeA.ScopeKey, scopeB.ScopeKey, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string? collisionReasonEn = null;
                    string? collisionReasonJa = null;
                    Dictionary<string, double>? metrics = null;

                    if (string.Equals(keyGroup.Key, LevelKey.Unassigned, StringComparison.Ordinal))
                    {
                        collisionReasonEn = "both level names normalize to the reserved 'unassigned' key";
                        collisionReasonJa = "双方のレベル名が予約キー 'unassigned' に正規化されます";
                    }
                    else if (levelA.HasElevation && levelB.HasElevation)
                    {
                        double deltaMm = Math.Abs(levelA.ProjectElevationMeters!.Value - levelB.ProjectElevationMeters!.Value) * 1000.0d;
                        if (deltaMm >= options.LevelAlignedToleranceMm - BoundaryEpsilonMm)
                        {
                            collisionReasonEn = string.Format(
                                CultureInfo.InvariantCulture,
                                "elevations differ by {0:0.##} mm ({1:0.###} m vs {2:0.###} m)",
                                deltaMm,
                                levelA.ProjectElevationMeters!.Value,
                                levelB.ProjectElevationMeters!.Value);
                            collisionReasonJa = string.Format(
                                CultureInfo.InvariantCulture,
                                "標高が {0:0.##} mm 異なります（{1:0.###} m と {2:0.###} m）",
                                deltaMm,
                                levelA.ProjectElevationMeters!.Value,
                                levelB.ProjectElevationMeters!.Value);
                            metrics = new Dictionary<string, double> { ["deltaZMm"] = deltaMm };
                        }
                    }

                    if (collisionReasonEn == null && levelA.Ordinal.HasValue && levelB.Ordinal.HasValue
                        && levelA.Ordinal.Value != levelB.Ordinal.Value)
                    {
                        collisionReasonEn = $"ordinals differ ({levelA.Ordinal.Value} vs {levelB.Ordinal.Value})";
                        collisionReasonJa = $"ordinal が異なります（{levelA.Ordinal.Value} と {levelB.Ordinal.Value}）";
                    }

                    if (collisionReasonEn == null && levelA.ShortName.Length > 0 && levelB.ShortName.Length > 0
                        && !string.Equals(levelA.ShortName, levelB.ShortName, StringComparison.OrdinalIgnoreCase))
                    {
                        collisionReasonEn = $"short_name values differ ('{levelA.ShortName}' vs '{levelB.ShortName}')";
                        collisionReasonJa = $"short_name が異なります（'{levelA.ShortName}' と '{levelB.ShortName}'）";
                    }

                    if (collisionReasonEn == null
                        && !string.Equals(levelA.Name.Trim(), levelB.Name.Trim(), StringComparison.OrdinalIgnoreCase))
                    {
                        collisionReasonEn = $"distinct names '{levelA.Name}' and '{levelB.Name}' collapse onto the same normalized key";
                        collisionReasonJa = $"異なるレベル名 '{levelA.Name}' と '{levelB.Name}' が同一の正規化キーへ縮約されます";
                    }

                    if (collisionReasonEn == null)
                    {
                        continue;
                    }

                    string pairKey = CanonicalPairKey(
                        $"{scopeA.ScopeKey}|{levelA.Name}|{levelA.LevelId}",
                        $"{scopeB.ScopeKey}|{levelB.Name}|{levelB.LevelId}");
                    if (!reportedCross.Add(keyGroup.Key + "|" + pairKey))
                    {
                        continue;
                    }

                    issues.Add(new FederationIssue(
                        ruleId,
                        ReadinessCheckStatus.Fail,
                        "TILES-KEY-COLLISION-CROSS",
                        $"3D Tiles level-key collision on '{keyGroup.Key}' between '{scopeA.ModelLabel}:{levelA.Name}' and '{scopeB.ModelLabel}:{levelB.Name}': {collisionReasonEn}.",
                        $"3D Tiles レベルキー '{keyGroup.Key}' の衝突（'{scopeA.ModelLabel}:{levelA.Name}' と '{scopeB.ModelLabel}:{levelB.Name}'）: {collisionReasonJa}。",
                        modelA: scopeA.ModelLabel,
                        modelB: scopeB.ModelLabel,
                        elementIds: new[] { levelA.ElementId, levelB.ElementId },
                        metrics: metrics));
                }
            }
        }
    }

    private static FederationRuleResult EvaluateStationFolderRule(
        StationFolderSnapshot? stationFolder,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        FederationCheckOptions options)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(RuleStationFolderFederation);

        if (stationFolder == null)
        {
            return BuildRuleResult(
                rule,
                ItemStationFolderFederation,
                ReadinessCheckStatus.NotApplicable,
                "No shared station folder configured or provided for cross-building inspection.",
                "共有駅フォルダが設定・指定されていないため、駅フォルダ横断チェックは対象外です。",
                Array.Empty<FederationIssue>());
        }

        List<FederationIssue> issues = new();

        foreach (string loadWarning in stationFolder.LoadWarnings)
        {
            issues.Add(new FederationIssue(
                RuleStationFolderFederation,
                ReadinessCheckStatus.Warn,
                "STATION-LOAD-WARNING",
                loadWarning,
                $"駅フォルダ読込警告: {loadWarning}"));
        }

        if (stationAuthoringModels.Count == 0)
        {
            ReadinessCheckStatus status = issues.Count > 0 ? ReadinessCheckStatus.Warn : ReadinessCheckStatus.Pass;
            string en = issues.Count > 0
                ? SummarizeIssuesEn(issues)
                : "Station folder contains no exported authoring building models yet.";
            string ja = issues.Count > 0
                ? SummarizeIssuesJa(issues)
                : "駅フォルダ内にエクスポート済みのオーサリング建物モデルはまだありません。";
            return BuildRuleResult(rule, ItemStationFolderFederation, status, en, ja, issues);
        }

        AppendStationFolderCrsIssues(
            RuleStationFolderFederation,
            stationAuthoringModels,
            refEpsg: null,
            refDatum: string.Empty,
            refGeoid: string.Empty,
            refGeoidOffset: null,
            refLabel: string.Empty,
            options,
            issues);

        List<ModelIdentityHolder> holders = new();
        foreach (StationFolderModelEntry model in stationAuthoringModels)
        {
            if (string.IsNullOrWhiteSpace(model.ModelId))
            {
                issues.Add(new FederationIssue(
                    RuleStationFolderFederation,
                    ReadinessCheckStatus.Fail,
                    "STATION-MODEL-ID-MISSING",
                    $"Station-folder model '{model.DisplayName}' has no ModelId.",
                    $"駅フォルダモデル '{model.DisplayName}' に ModelId がありません。",
                    modelA: model.DisplayName));
            }
            else
            {
                holders.Add(new ModelIdentityHolder("station", model.DisplayName, model.ModelId.Trim(), string.Empty, model.DocumentKey, 0));
            }
        }

        foreach (IGrouping<string, ModelIdentityHolder> dup in holders
            .GroupBy(h => h.ModelId, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1))
        {
            List<ModelIdentityHolder> list = dup.ToList();
            string names = string.Join(", ", list.Select(h => h.Label));
            issues.Add(new FederationIssue(
                RuleStationFolderFederation,
                ReadinessCheckStatus.Fail,
                "STATION-DUPLICATE-MODEL-ID",
                $"Station folder contains duplicate ModelId '{dup.Key}' across models ({names}).",
                $"駅フォルダ内で ModelId '{dup.Key}' が複数モデル（{names}）に重複しています。",
                modelA: list[0].Label,
                modelB: list[1].Label));
        }

        List<ModelLevelScope> stationScopes = stationAuthoringModels
            .Select((model, index) => new ModelLevelScope(
                $"station:{index}:{model.ModelId}:{model.DisplayName}",
                model.DisplayName,
                isHost: false,
                model.Levels))
            .ToList();

        foreach (ModelLevelScope scope in stationScopes)
        {
            CheckIntraModelLevelMetadata(RuleStationFolderFederation, scope.ModelLabel, scope.Levels, requireMetadata: false, issues);
            CheckIntraModelDuplicateLevelIds(RuleStationFolderFederation, scope.ModelLabel, scope.Levels, issues);
        }

        AppendCrossModelDuplicateLevelIdIssues(RuleStationFolderFederation, stationScopes, options, issues);

        HashSet<string> reportedDriftPairs = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < stationAuthoringModels.Count; i++)
        {
            for (int j = i + 1; j < stationAuthoringModels.Count; j++)
            {
                CompareLevelSetsForDrift(
                    RuleStationFolderFederation,
                    stationAuthoringModels[i].DisplayName,
                    stationAuthoringModels[i].Levels,
                    stationAuthoringModels[j].DisplayName,
                    stationAuthoringModels[j].Levels,
                    options,
                    reportedDriftPairs,
                    issues);
            }
        }

        AppendTilesLevelKeyCollisionIssues(RuleStationFolderFederation, stationScopes, stationAuthoringModels, options, issues);

        bool anyFail = issues.Any(i => i.Status == ReadinessCheckStatus.Fail);
        bool anyWarn = issues.Any(i => i.Status == ReadinessCheckStatus.Warn);
        if (anyFail)
        {
            return BuildRuleResult(
                rule,
                ItemStationFolderFederation,
                ReadinessCheckStatus.Fail,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        if (anyWarn)
        {
            return BuildRuleResult(
                rule,
                ItemStationFolderFederation,
                ReadinessCheckStatus.Warn,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        string passEn = $"Station-folder federation check passed across {stationAuthoringModels.Count} authoring building model(s).";
        string passJa = $"駅フォルダフェデレーションチェックに合格しました（オーサリング建物モデル {stationAuthoringModels.Count} 件）。";
        return BuildRuleResult(rule, ItemStationFolderFederation, ReadinessCheckStatus.Pass, passEn, passJa, issues);
    }

    private static FederationRuleResult EvaluateNoDoublePublishing(
        FederationCheckSnapshot snapshot,
        IReadOnlyList<FederationLinkSnapshot> loadedAuthoringLinks,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        bool isStationOnly)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(RuleNoDoublePublishing);
        List<FederationIssue> issues = new();
        HashSet<string> reported = new(StringComparer.OrdinalIgnoreCase);

        List<PublishedPackageIdentity> publishedPackages = new();
        foreach (StationFolderModelEntry model in stationAuthoringModels.Where(m => m.HasPublishedPackage))
        {
            publishedPackages.Add(new PublishedPackageIdentity(
                model.DisplayName,
                model.ModelId,
                NormalizeModelFileStem(FirstNonEmpty(model.SourceModelFile, model.ModelName)),
                model.FederationMode,
                model.HubModelId,
                model.IncludedLinks));
        }

        if (!isStationOnly)
        {
            List<StationFolderPublishedLink> activeLinks = loadedAuthoringLinks
                .Where(l => l.IsIncludedInHubExport)
                .Select(l => new StationFolderPublishedLink(l.LinkInstanceUniqueId, FirstNonEmpty(l.Name, l.SourcePath), l.ModelId))
                .ToList();
            if (activeLinks.Count > 0)
            {
                bool alreadyPresent = !string.IsNullOrWhiteSpace(snapshot.HostModelId)
                    && publishedPackages.Any(p => string.Equals(p.ModelId, snapshot.HostModelId.Trim(), StringComparison.OrdinalIgnoreCase));
                if (!alreadyPresent)
                {
                    publishedPackages.Add(new PublishedPackageIdentity(
                        snapshot.HostDisplayName,
                        snapshot.HostModelId.Trim(),
                        NormalizeModelFileStem(FirstNonEmpty(snapshot.HostDocumentPath, snapshot.HostDocumentTitle)),
                        snapshot.HostFederationMode,
                        snapshot.HostHubModelId,
                        activeLinks));
                }
            }
        }

        foreach (PublishedPackageIdentity hub in publishedPackages.Where(p => p.IncludedLinks.Count > 0))
        {
            foreach (StationFolderPublishedLink link in hub.IncludedLinks)
            {
                string linkStem = NormalizeModelFileStem(link.Name);
                foreach (PublishedPackageIdentity standalone in publishedPackages)
                {
                    if (ReferenceEquals(hub, standalone))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(hub.ModelId)
                        && !string.IsNullOrWhiteSpace(standalone.ModelId)
                        && string.Equals(hub.ModelId, standalone.ModelId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    bool matchByModelId = link.ModelId.Length > 0
                        && standalone.ModelId.Length > 0
                        && string.Equals(link.ModelId, standalone.ModelId, StringComparison.OrdinalIgnoreCase);
                    bool matchByFileStem = linkStem.Length > 0
                        && standalone.FileStem.Length > 0
                        && string.Equals(linkStem, standalone.FileStem, StringComparison.OrdinalIgnoreCase);

                    if (!matchByModelId && !matchByFileStem)
                    {
                        continue;
                    }

                    string dedupe = "HUB-STANDALONE|" + CanonicalPairKey(hub.Label, standalone.Label);
                    if (!reported.Add(dedupe))
                    {
                        continue;
                    }

                    string matchedBy = matchByModelId ? $"ModelId '{standalone.ModelId}'" : $"source file '{standalone.FileStem}'";
                    issues.Add(new FederationIssue(
                        RuleNoDoublePublishing,
                        ReadinessCheckStatus.Fail,
                        "DOUBLE-PUBLISH-HUB-AND-STANDALONE",
                        $"Double-publishing detected: building '{standalone.Label}' ({matchedBy}) is published BOTH standalone and inside hub package '{hub.Label}'. Publish either via the hub OR as an independent building, never both.",
                        $"二重パブリッシュ検出: 建物 '{standalone.Label}'（{matchedBy}）が単独パッケージとハブパッケージ '{hub.Label}' 内のリンクの両方で出力されています。ハブ経由または独立建物のいずれか一方のみで出力してください。",
                        modelA: hub.Label,
                        modelB: standalone.Label));
                }
            }
        }

        foreach (PublishedPackageIdentity spoke in publishedPackages.Where(p =>
            p.FederationMode == KanizuFederationMode.Spoke && !string.IsNullOrWhiteSpace(p.HubModelId)))
        {
            PublishedPackageIdentity? hub = publishedPackages.FirstOrDefault(p =>
                !ReferenceEquals(p, spoke)
                && string.Equals(p.ModelId, spoke.HubModelId, StringComparison.OrdinalIgnoreCase));
            if (hub is null)
            {
                continue;
            }

            string dedupe = "HUB-STANDALONE|" + CanonicalPairKey(hub.Label, spoke.Label);
            if (!reported.Add(dedupe))
            {
                continue;
            }

            issues.Add(new FederationIssue(
                RuleNoDoublePublishing,
                ReadinessCheckStatus.Fail,
                "DOUBLE-PUBLISH-SPOKE-STANDALONE",
                $"Double-publishing detected: spoke building '{spoke.Label}' (hubModelId '{spoke.HubModelId}') is published standalone while its hub '{hub.Label}' is also published in the same station folder.",
                $"二重パブリッシュ検出: スポーク建物 '{spoke.Label}'（hubModelId '{spoke.HubModelId}'）が、同じ駅フォルダ内でハブ '{hub.Label}' と同時に単独出力されています。",
                modelA: hub.Label,
                modelB: spoke.Label));
        }

        List<PublishedPackageIdentity> hubs = publishedPackages.Where(p => p.IncludedLinks.Count > 0).ToList();
        for (int i = 0; i < hubs.Count; i++)
        {
            for (int j = i + 1; j < hubs.Count; j++)
            {
                PublishedPackageIdentity hubA = hubs[i];
                PublishedPackageIdentity hubB = hubs[j];
                foreach (StationFolderPublishedLink linkA in hubA.IncludedLinks)
                {
                    string stemA = NormalizeModelFileStem(linkA.Name);
                    foreach (StationFolderPublishedLink linkB in hubB.IncludedLinks)
                    {
                        string stemB = NormalizeModelFileStem(linkB.Name);
                        bool sameModelId = linkA.ModelId.Length > 0 && linkB.ModelId.Length > 0
                            && string.Equals(linkA.ModelId, linkB.ModelId, StringComparison.OrdinalIgnoreCase);
                        bool sameStem = stemA.Length > 0 && stemB.Length > 0
                            && string.Equals(stemA, stemB, StringComparison.OrdinalIgnoreCase);

                        if (!sameModelId && !sameStem)
                        {
                            continue;
                        }

                        string targetId = sameModelId ? linkA.ModelId : stemA;
                        string dedupe = "OVERLAP-HUBS|" + CanonicalPairKey(hubA.Label, hubB.Label) + "|" + targetId;
                        if (!reported.Add(dedupe))
                        {
                            continue;
                        }

                        string linkDisplay = FirstNonEmpty(linkA.Name, linkB.Name, targetId);
                        issues.Add(new FederationIssue(
                            RuleNoDoublePublishing,
                            ReadinessCheckStatus.Fail,
                            "DOUBLE-PUBLISH-OVERLAPPING-HUBS",
                            $"Double-publishing detected: linked building '{linkDisplay}' is exported by two overlapping hub packages ('{hubA.Label}' and '{hubB.Label}') in the same station folder.",
                            $"二重パブリッシュ検出: リンク建物 '{linkDisplay}' が同じ駅フォルダ内の 2 つのハブパッケージ（'{hubA.Label}' と '{hubB.Label}'）から重複出力されています。",
                            modelA: hubA.Label,
                            modelB: hubB.Label));
                    }
                }
            }
        }

        if (issues.Count > 0)
        {
            return BuildRuleResult(
                rule,
                ItemNoDoublePublishing,
                ReadinessCheckStatus.Fail,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        string passEn = "No double-publishing overlap detected between hub packages and standalone station-folder models.";
        string passJa = "ハブパッケージと単独駅フォルダモデル間に二重パブリッシュの重複は検出されませんでした。";
        return BuildRuleResult(rule, ItemNoDoublePublishing, ReadinessCheckStatus.Pass, passEn, passJa, issues);
    }

    private static FederationRuleResult EvaluateLinkAndRoleHygiene(
        FederationCheckSnapshot snapshot,
        IReadOnlyList<FederationLinkSnapshot> allLinks,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        bool isStationOnly)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(RuleLinkAndRoleHygiene);
        List<FederationIssue> issues = new();

        if (!isStationOnly)
        {
            foreach (FederationLinkSnapshot link in allLinks)
            {
                if (link.IsLoaded || IsShowcaseLink(link))
                {
                    continue;
                }

                string label = LinkLabel(link);
                long[] ids = link.LinkInstanceElementId > 0 ? new[] { link.LinkInstanceElementId } : Array.Empty<long>();
                issues.Add(new FederationIssue(
                    RuleLinkAndRoleHygiene,
                    ReadinessCheckStatus.Warn,
                    "UNLOADED-LINK",
                    $"Revit link '{label}' is unloaded or missing and cannot be verified for CRS, pose, ModelId, or level drift; reload before export.",
                    $"Revit リンク '{label}' が未読込または見つからないため、CRS・姿勢・ModelId・レベルずれを検証できません。出力前に再読み込みしてください。",
                    modelA: snapshot.HostDisplayName,
                    modelB: label,
                    elementIds: ids));
            }
        }

        List<FederationRoleRecord> roles = new();
        if (!isStationOnly)
        {
            roles.Add(new FederationRoleRecord(
                snapshot.HostDisplayName,
                snapshot.HostModelId.Trim(),
                snapshot.HostFederationMode,
                snapshot.HostHubModelId,
                snapshot.HostRawFederationMode,
                isHost: true,
                isLoadedLink: false,
                elementId: 0));

            foreach (FederationLinkSnapshot link in allLinks.Where(l => l.IsLoaded && !IsShowcaseLink(l)))
            {
                roles.Add(new FederationRoleRecord(
                    LinkLabel(link),
                    link.ModelId.Trim(),
                    link.FederationMode,
                    link.HubModelId,
                    link.RawFederationMode,
                    isHost: false,
                    isLoadedLink: true,
                    elementId: link.LinkInstanceElementId));
            }
        }

        HashSet<string> knownActiveIds = new(
            roles.Where(r => r.ModelId.Length > 0).Select(r => r.ModelId),
            StringComparer.OrdinalIgnoreCase);
        foreach (StationFolderModelEntry stationModel in stationAuthoringModels)
        {
            if (stationModel.ModelId.Length > 0 && knownActiveIds.Contains(stationModel.ModelId))
            {
                continue;
            }

            roles.Add(new FederationRoleRecord(
                stationModel.DisplayName,
                stationModel.ModelId.Trim(),
                stationModel.FederationMode,
                stationModel.HubModelId,
                stationModel.RawFederationMode,
                isHost: false,
                isLoadedLink: false,
                elementId: 0));
        }

        Dictionary<string, FederationRoleRecord> byModelId = new(StringComparer.OrdinalIgnoreCase);
        foreach (FederationRoleRecord record in roles)
        {
            if (record.ModelId.Length > 0 && !byModelId.ContainsKey(record.ModelId))
            {
                byModelId[record.ModelId] = record;
            }
        }

        bool hasMultiModelContext = roles.Count > 1;

        foreach (FederationRoleRecord record in roles)
        {
            long[] ids = record.ElementId > 0 ? new[] { record.ElementId } : Array.Empty<long>();

            if (!string.IsNullOrWhiteSpace(record.RawMode) && !record.Mode.HasValue)
            {
                issues.Add(new FederationIssue(
                    RuleLinkAndRoleHygiene,
                    ReadinessCheckStatus.Fail,
                    "INVALID-FEDERATION-MODE",
                    $"Model '{record.Label}' has unrecognized federation.mode '{record.RawMode}'. Allowed values: standalone, hub, spoke, independent.",
                    $"モデル '{record.Label}' の federation.mode '{record.RawMode}' は無効です。許可値: standalone, hub, spoke, independent。",
                    modelA: record.Label,
                    elementIds: ids));
                continue;
            }

            if (record.Mode == KanizuFederationMode.Spoke)
            {
                if (string.IsNullOrWhiteSpace(record.HubModelId))
                {
                    issues.Add(new FederationIssue(
                        RuleLinkAndRoleHygiene,
                        ReadinessCheckStatus.Fail,
                        "SPOKE-MISSING-HUB-ID",
                        $"Spoke model '{record.Label}' is missing a required federation.hubModelId.",
                        $"スポークモデル '{record.Label}' に必須の federation.hubModelId が設定されていません。",
                        modelA: record.Label,
                        elementIds: ids));
                }
                else if (record.ModelId.Length > 0 && string.Equals(record.HubModelId, record.ModelId, StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new FederationIssue(
                        RuleLinkAndRoleHygiene,
                        ReadinessCheckStatus.Fail,
                        "SPOKE-SELF-HUB-ID",
                        $"Spoke model '{record.Label}' references its own ModelId '{record.ModelId}' as federation.hubModelId.",
                        $"スポークモデル '{record.Label}' の federation.hubModelId が自身の ModelId '{record.ModelId}' を自己参照しています。",
                        modelA: record.Label,
                        elementIds: ids));
                }
                else if (record.IsLoadedLink && !isStationOnly && snapshot.HostModelId.Trim().Length > 0
                    && !string.Equals(record.HubModelId, snapshot.HostModelId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    issues.Add(new FederationIssue(
                        RuleLinkAndRoleHygiene,
                        ReadinessCheckStatus.Fail,
                        "SPOKE-WRONG-HUB-ID",
                        $"Linked spoke '{record.Label}' declares hubModelId '{record.HubModelId}', which does not match host ModelId '{snapshot.HostModelId}'.",
                        $"リンクされたスポーク '{record.Label}' の hubModelId '{record.HubModelId}' がホストの ModelId '{snapshot.HostModelId}' と一致しません。",
                        modelA: snapshot.HostDisplayName,
                        modelB: record.Label,
                        elementIds: ids));
                }
                else if (hasMultiModelContext && !byModelId.ContainsKey(record.HubModelId!))
                {
                    issues.Add(new FederationIssue(
                        RuleLinkAndRoleHygiene,
                        ReadinessCheckStatus.Fail,
                        "SPOKE-UNKNOWN-HUB-ID",
                        $"Spoke model '{record.Label}' references hubModelId '{record.HubModelId}', which is not present in the federated model set.",
                        $"スポークモデル '{record.Label}' が参照する hubModelId '{record.HubModelId}' がフェデレーション内に見つかりません。",
                        modelA: record.Label,
                        elementIds: ids));
                }
                else if (byModelId.TryGetValue(record.HubModelId!, out FederationRoleRecord? targetHub)
                    && targetHub.Mode.HasValue
                    && targetHub.Mode.Value != KanizuFederationMode.Hub)
                {
                    issues.Add(new FederationIssue(
                        RuleLinkAndRoleHygiene,
                        ReadinessCheckStatus.Fail,
                        "SPOKE-TARGET-NOT-HUB",
                        $"Spoke model '{record.Label}' references '{targetHub.Label}' ({record.HubModelId}) as its hub, but that model's federation.mode is '{GisModelFederation.FormatMode(targetHub.Mode.Value)}' instead of 'hub'.",
                        $"スポークモデル '{record.Label}' がハブとして参照している '{targetHub.Label}'（{record.HubModelId}）の federation.mode が 'hub' ではなく '{GisModelFederation.FormatMode(targetHub.Mode.Value)}' です。",
                        modelA: record.Label,
                        modelB: targetHub.Label,
                        elementIds: ids));
                }
            }
            else if (!string.IsNullOrWhiteSpace(record.HubModelId))
            {
                string modeName = record.Mode.HasValue ? GisModelFederation.FormatMode(record.Mode.Value) : "unset";
                issues.Add(new FederationIssue(
                    RuleLinkAndRoleHygiene,
                    ReadinessCheckStatus.Fail,
                    "NON-SPOKE-HAS-HUB-ID",
                    $"Model '{record.Label}' has federation.mode '{modeName}' but sets federation.hubModelId '{record.HubModelId}'; hubModelId is valid only for 'spoke' models.",
                    $"モデル '{record.Label}' は federation.mode '{modeName}' にもかかわらず federation.hubModelId '{record.HubModelId}' を設定しています（spoke 以外は不可）。",
                    modelA: record.Label,
                    elementIds: ids));
            }

            if (record.IsLoadedLink && !isStationOnly && snapshot.HostFederationMode == KanizuFederationMode.Hub
                && record.Mode is KanizuFederationMode.Hub or KanizuFederationMode.Independent)
            {
                issues.Add(new FederationIssue(
                    RuleLinkAndRoleHygiene,
                    ReadinessCheckStatus.Fail,
                    "LINK-ROLE-CONFLICT-IN-HUB",
                    $"Linked model '{record.Label}' inside hub '{snapshot.HostDisplayName}' declares contradictory federation.mode '{GisModelFederation.FormatMode(record.Mode.Value)}' (expected 'spoke').",
                    $"ハブ '{snapshot.HostDisplayName}' 内にリンクされた '{record.Label}' の federation.mode が矛盾する '{GisModelFederation.FormatMode(record.Mode.Value)}' に設定されています（'spoke' が必要です）。",
                    modelA: snapshot.HostDisplayName,
                    modelB: record.Label,
                    elementIds: ids));
            }

            if (record.IsHost && record.Mode == KanizuFederationMode.Spoke
                && allLinks.Any(l => !IsShowcaseLink(l) && l.IsIncludedInHubExport))
            {
                issues.Add(new FederationIssue(
                    RuleLinkAndRoleHygiene,
                    ReadinessCheckStatus.Fail,
                    "SPOKE-HOST-WITH-LINKS",
                    $"Model '{record.Label}' is marked as federation.mode 'spoke' but aggregates linked authoring models like a hub.",
                    $"モデル '{record.Label}' は federation.mode 'spoke' に設定されていますが、ハブのようにリンクされたオーサリングモデルを集約しています。",
                    modelA: record.Label));
            }
        }

        if (!isStationOnly && snapshot.HostFederationMode == KanizuFederationMode.Hub)
        {
            int authoringLinkCount = allLinks.Count(l => !IsShowcaseLink(l));
            int stationSpokeCount = stationAuthoringModels.Count(m =>
                m.FederationMode == KanizuFederationMode.Spoke
                && !string.IsNullOrWhiteSpace(snapshot.HostModelId)
                && string.Equals(m.HubModelId, snapshot.HostModelId.Trim(), StringComparison.OrdinalIgnoreCase));
            if (authoringLinkCount == 0 && stationSpokeCount == 0 && snapshot.StationFolder != null)
            {
                issues.Add(new FederationIssue(
                    RuleLinkAndRoleHygiene,
                    ReadinessCheckStatus.Warn,
                    "HUB-WITHOUT-SPOKES",
                    $"Model '{snapshot.HostDisplayName}' is marked as federation.mode 'hub' but has no authoring Revit links or spokes referencing it.",
                    $"モデル '{snapshot.HostDisplayName}' は federation.mode 'hub' に設定されていますが、オーサリングリンクおよび参照スポークが存在しません。",
                    modelA: snapshot.HostDisplayName));
            }
        }

        bool anyFail = issues.Any(i => i.Status == ReadinessCheckStatus.Fail);
        bool anyWarn = issues.Any(i => i.Status == ReadinessCheckStatus.Warn);
        if (anyFail)
        {
            return BuildRuleResult(rule, ItemLinkAndRoleHygiene, ReadinessCheckStatus.Fail, SummarizeIssuesEn(issues), SummarizeIssuesJa(issues), issues);
        }

        if (anyWarn)
        {
            return BuildRuleResult(rule, ItemLinkAndRoleHygiene, ReadinessCheckStatus.Warn, SummarizeIssuesEn(issues), SummarizeIssuesJa(issues), issues);
        }

        string passEn = "All non-showcase Revit links are loaded and federation role metadata is consistent.";
        string passJa = "ショーケース以外の全 Revit リンクが読み込み済みであり、フェデレーション役割メタデータに矛盾はありません。";
        return BuildRuleResult(rule, ItemLinkAndRoleHygiene, ReadinessCheckStatus.Pass, passEn, passJa, issues);
    }

    private static FederationRuleResult EvaluateShowcaseIsolation(
        FederationCheckSnapshot snapshot,
        IReadOnlyList<FederationLinkSnapshot> allLinks,
        IReadOnlyList<StationFolderModelEntry> stationModels,
        bool isStationOnly)
    {
        RuleCatalogEntry rule = RuleCatalog.Lookup(RuleShowcaseExclusion);
        List<FederationIssue> issues = new();

        if (!isStationOnly)
        {
            List<LinkedModelReadinessInfo> linkInfos = allLinks
                .Select(l => new LinkedModelReadinessInfo(
                    LinkLabel(l),
                    l.IsLoaded,
                    l.ModelId,
                    EffectiveShowcaseRole(l)))
                .ToList();

            ShowcaseRuleEvaluationResult f2Result = ShowcaseReadinessEvaluator.EvaluateFederationGuard(
                snapshot.HostModelRole,
                linkInfos);

            if (f2Result.Status == ReadinessCheckStatus.Fail)
            {
                long[] showcaseLinkIds = allLinks
                    .Where(l => l.IsLoaded && IsShowcaseLink(l) && l.LinkInstanceElementId > 0)
                    .Select(l => l.LinkInstanceElementId)
                    .ToArray();
                issues.Add(new FederationIssue(
                    RuleShowcaseExclusion,
                    ReadinessCheckStatus.Fail,
                    "SHOWCASE-FEDERATION-CONFLICT",
                    f2Result.MessageEn,
                    f2Result.MessageJa,
                    modelA: snapshot.HostDisplayName,
                    elementIds: showcaseLinkIds));
            }
        }

        HashSet<string> showcaseIds = new(
            stationModels
                .Where(IsShowcaseStationModel)
                .Where(m => !string.IsNullOrWhiteSpace(m.ModelId))
                .Select(m => m.ModelId.Trim()),
            StringComparer.OrdinalIgnoreCase);

        foreach (StationFolderModelEntry model in stationModels)
        {
            if (IsShowcaseStationModel(model))
            {
                if (model.FederationMode is KanizuFederationMode.Hub or KanizuFederationMode.Spoke)
                {
                    issues.Add(new FederationIssue(
                        RuleShowcaseExclusion,
                        ReadinessCheckStatus.Fail,
                        "SHOWCASE-STATION-ROLE-CONFLICT",
                        $"Forma Companion showcase model '{model.DisplayName}' (gis_model_role = 'showcase') must not declare authoring federation.mode '{GisModelFederation.FormatMode(model.FederationMode.Value)}'.",
                        $"Forma Companion ショーケースモデル '{model.DisplayName}'（gis_model_role = 'showcase'）にオーサリング用の federation.mode '{GisModelFederation.FormatMode(model.FederationMode.Value)}' を設定しないでください。",
                        modelA: model.DisplayName));
                }

                continue;
            }

            foreach (StationFolderPublishedLink link in model.IncludedLinks)
            {
                bool targetsShowcase = (link.ModelId.Length > 0 && showcaseIds.Contains(link.ModelId))
                    || ShowcaseExportGuard.IsShowcaseFamilyOrTypeName(link.Name);
                if (targetsShowcase)
                {
                    issues.Add(new FederationIssue(
                        RuleShowcaseExclusion,
                        ReadinessCheckStatus.Fail,
                        "SHOWCASE-INCLUDED-IN-HUB-EXPORT",
                        $"Authoring hub '{model.DisplayName}' includes showcase companion '{FirstNonEmpty(link.Name, link.ModelId)}' in its exported links.",
                        $"オーサリングハブ '{model.DisplayName}' の出力リンクにショーケースモデル '{FirstNonEmpty(link.Name, link.ModelId)}' が含まれています。",
                        modelA: model.DisplayName,
                        modelB: FirstNonEmpty(link.Name, link.ModelId)));
                }
            }
        }

        if (issues.Count > 0)
        {
            return BuildRuleResult(
                rule,
                ItemShowcaseExclusion,
                ReadinessCheckStatus.Fail,
                SummarizeIssuesEn(issues),
                SummarizeIssuesJa(issues),
                issues);
        }

        string passEn = isStationOnly
            ? "No Forma Companion showcase models conflict with authoring station-folder federation."
            : ShowcaseReadinessEvaluator.EvaluateFederationGuard(snapshot.HostModelRole, Array.Empty<LinkedModelReadinessInfo>()).MessageEn;
        string passJa = isStationOnly
            ? "駅フォルダ内のオーサリングフェデレーションに Forma Companion ショーケースモデルの競合はありません。"
            : ShowcaseReadinessEvaluator.EvaluateFederationGuard(snapshot.HostModelRole, Array.Empty<LinkedModelReadinessInfo>()).MessageJa;

        return BuildRuleResult(rule, ItemShowcaseExclusion, ReadinessCheckStatus.Pass, passEn, passJa, issues);
    }

    private static List<ModelLevelScope> CollectModelLevelScopes(
        FederationCheckSnapshot snapshot,
        IReadOnlyList<FederationLinkSnapshot> loadedAuthoringLinks,
        IReadOnlyList<StationFolderModelEntry> stationAuthoringModels,
        bool isStationOnly)
    {
        List<ModelLevelScope> scopes = new();
        HashSet<string> representedModelIds = new(StringComparer.OrdinalIgnoreCase);

        if (!isStationOnly)
        {
            scopes.Add(new ModelLevelScope(
                "host:" + (string.IsNullOrWhiteSpace(snapshot.HostModelId) ? snapshot.HostDisplayName : snapshot.HostModelId.Trim()),
                snapshot.HostDisplayName,
                isHost: true,
                snapshot.HostLevels ?? Array.Empty<FederationLevelSnapshot>()));
            if (!string.IsNullOrWhiteSpace(snapshot.HostModelId))
            {
                representedModelIds.Add(snapshot.HostModelId.Trim());
            }

            int linkIndex = 0;
            HashSet<string> seenLinkPaths = new(StringComparer.OrdinalIgnoreCase);
            foreach (FederationLinkSnapshot link in loadedAuthoringLinks)
            {
                string normPath = NormalizePath(link.SourcePath);
                if (normPath.Length > 0 && !seenLinkPaths.Add(normPath))
                {
                    continue;
                }

                string label = LinkLabel(link);
                scopes.Add(new ModelLevelScope(
                    $"link:{linkIndex++}:{link.ModelId}:{label}",
                    label,
                    isHost: false,
                    link.Levels));
                if (!string.IsNullOrWhiteSpace(link.ModelId))
                {
                    representedModelIds.Add(link.ModelId.Trim());
                }
            }
        }

        int stationIndex = 0;
        foreach (StationFolderModelEntry model in stationAuthoringModels)
        {
            if (!isStationOnly && !string.IsNullOrWhiteSpace(model.ModelId) && representedModelIds.Contains(model.ModelId.Trim()))
            {
                continue;
            }

            scopes.Add(new ModelLevelScope(
                $"station:{stationIndex++}:{model.ModelId}:{model.DisplayName}",
                model.DisplayName,
                isHost: false,
                model.Levels));
        }

        return scopes;
    }

    internal static bool IsShowcaseLink(FederationLinkSnapshot link) =>
        ShowcaseExportGuard.IsShowcaseModelRole(link.ModelRole)
        || ShowcaseExportGuard.IsShowcaseFamilyOrTypeName(link.Name)
        || ShowcaseExportGuard.IsShowcaseFamilyOrTypeName(ExtractFileName(link.SourcePath));

    private static string EffectiveShowcaseRole(FederationLinkSnapshot link) =>
        IsShowcaseLink(link) ? ShowcaseExportGuard.ShowcaseModelRole : link.ModelRole;

    internal static bool IsShowcaseStationModel(StationFolderModelEntry model) =>
        ShowcaseExportGuard.IsShowcaseModelRole(model.ModelRole)
        || CesiumModelRoles.IsShowcase(model.ModelRole)
        || ShowcaseExportGuard.IsShowcaseFamilyOrTypeName(model.ModelName)
        || ShowcaseExportGuard.IsShowcaseFamilyOrTypeName(model.SourceModelFile);

    internal static string NormalizeModelFileStem(string? nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            return string.Empty;
        }

        string text = nameOrPath!.Trim();
        int colonSep = text.IndexOf(" : ", StringComparison.Ordinal);
        if (colonSep > 0)
        {
            text = text.Substring(0, colonSep).Trim();
        }

        text = ExtractFileName(text);
        if (text.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
        {
            text = text.Substring(0, text.Length - 4);
        }

        return text.Trim().ToLowerInvariant();
    }

    private static string ExtractFileName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string text = path!.Trim();
        int lastSlash = Math.Max(text.LastIndexOf('/'), text.LastIndexOf('\\'));
        return lastSlash >= 0 && lastSlash < text.Length - 1 ? text.Substring(lastSlash + 1) : text;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        string trimmed = path!.Trim();
        try
        {
            return Path.GetFullPath(trimmed);
        }
        catch (Exception)
        {
            return trimmed;
        }
    }

    private static string NormalizeDatum(string? datum)
    {
        if (string.IsNullOrWhiteSpace(datum))
        {
            return string.Empty;
        }

        string trimmed = datum!.Trim();
        return string.Equals(trimmed, CesiumHeightDatums.Tp, StringComparison.OrdinalIgnoreCase)
            ? CesiumHeightDatums.Tp
            : trimmed;
    }

    private static string NormalizeGeoidModel(string? geoidModel) =>
        string.IsNullOrWhiteSpace(geoidModel) ? string.Empty : geoidModel!.Trim();

    private static string LinkLabel(FederationLinkSnapshot link) =>
        !string.IsNullOrWhiteSpace(link.Name) ? link.Name.Trim()
        : !string.IsNullOrWhiteSpace(link.SourcePath) ? ExtractFileName(link.SourcePath)
        : !string.IsNullOrWhiteSpace(link.ModelId) ? link.ModelId.Trim()
        : $"Link {link.LinkInstanceElementId}";

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        return string.Empty;
    }

    private static string CanonicalPairKey(string a, string b) =>
        string.Compare(a, b, StringComparison.OrdinalIgnoreCase) <= 0 ? $"{a}||{b}" : $"{b}||{a}";

    private static FederationRuleResult BuildRuleResult(
        RuleCatalogEntry rule,
        string itemId,
        ReadinessCheckStatus status,
        string summaryEn,
        string summaryJa,
        IReadOnlyList<FederationIssue> issues) =>
        new(
            rule.RuleId,
            itemId,
            rule.Severity,
            status,
            rule.Title,
            rule.TitleJa,
            summaryEn,
            summaryJa,
            issues);

    private static string SummarizeIssuesEn(IReadOnlyList<FederationIssue> issues)
    {
        const int maxShown = 3;
        string shown = string.Join(" ", issues.Take(maxShown).Select(i => i.MessageEn));
        return issues.Count > maxShown ? $"{shown} (+{issues.Count - maxShown} more issue(s).)" : shown;
    }

    private static string SummarizeIssuesJa(IReadOnlyList<FederationIssue> issues)
    {
        const int maxShown = 3;
        string shown = string.Join(" ", issues.Take(maxShown).Select(i => i.MessageJa));
        return issues.Count > maxShown ? $"{shown}（他 {issues.Count - maxShown} 件）" : shown;
    }

    private static string DescribeList(IReadOnlyList<string> names)
    {
        const int maxShown = 4;
        string shown = string.Join(", ", names.Take(maxShown));
        return names.Count > maxShown ? $"{shown} and {names.Count - maxShown} more" : shown;
    }

    private sealed class ModelIdentityHolder
    {
        public ModelIdentityHolder(string kind, string label, string modelId, string normalizedPath, string documentKey, long elementId)
        {
            Kind = kind;
            Label = label;
            ModelId = modelId;
            NormalizedPath = normalizedPath;
            DocumentKey = documentKey;
            ElementId = elementId;
        }

        public string Kind { get; }

        public string Label { get; }

        public string ModelId { get; }

        public string NormalizedPath { get; }

        public string DocumentKey { get; }

        public long ElementId { get; }
    }

    private sealed class ModelLevelScope
    {
        public ModelLevelScope(string scopeKey, string modelLabel, bool isHost, IReadOnlyList<FederationLevelSnapshot> levels)
        {
            ScopeKey = scopeKey;
            ModelLabel = modelLabel;
            IsHost = isHost;
            Levels = levels;
        }

        public string ScopeKey { get; }

        public string ModelLabel { get; }

        public bool IsHost { get; }

        public IReadOnlyList<FederationLevelSnapshot> Levels { get; }
    }

    private sealed class PublishedPackageIdentity
    {
        public PublishedPackageIdentity(
            string label,
            string modelId,
            string fileStem,
            KanizuFederationMode? federationMode,
            string? hubModelId,
            IReadOnlyList<StationFolderPublishedLink> includedLinks)
        {
            Label = label;
            ModelId = modelId;
            FileStem = fileStem;
            FederationMode = federationMode;
            HubModelId = hubModelId;
            IncludedLinks = includedLinks;
        }

        public string Label { get; }

        public string ModelId { get; }

        public string FileStem { get; }

        public KanizuFederationMode? FederationMode { get; }

        public string? HubModelId { get; }

        public IReadOnlyList<StationFolderPublishedLink> IncludedLinks { get; }
    }

    private sealed class FederationRoleRecord
    {
        public FederationRoleRecord(
            string label,
            string modelId,
            KanizuFederationMode? mode,
            string? hubModelId,
            string? rawMode,
            bool isHost,
            bool isLoadedLink,
            long elementId)
        {
            Label = label;
            ModelId = modelId;
            Mode = mode;
            HubModelId = hubModelId;
            RawMode = rawMode;
            IsHost = isHost;
            IsLoadedLink = isLoadedLink;
            ElementId = elementId;
        }

        public string Label { get; }

        public string ModelId { get; }

        public KanizuFederationMode? Mode { get; }

        public string? HubModelId { get; }

        public string? RawMode { get; }

        public bool IsHost { get; }

        public bool IsLoadedLink { get; }

        public long ElementId { get; }
    }
}
