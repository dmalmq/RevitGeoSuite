using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>Enforcement policy applied to Forma Companion triangle/element/size budget gates.</summary>
public enum ShowcaseBudgetEnforcementMode
{
    /// <summary>Compute and attach <see cref="ShowcaseBudgetReport"/> without emitting warnings or failing the build.</summary>
    ReportOnly = 0,

    /// <summary>Attach <see cref="ShowcaseBudgetReport"/> and record actionable warnings whenever soft or hard limits are exceeded.</summary>
    WarnOnly = 1,

    /// <summary>Fail immediately with <see cref="ShowcaseBudgetExceededException"/> whenever any hard budget limit is exceeded.</summary>
    FailOnHardLimit = 2,
}

/// <summary>Evaluation severity for a single budget metric or overall companion build.</summary>
public enum ShowcaseBudgetSeverity
{
    Ok = 0,
    Warning = 1,
    Exceeded = 2,
}

/// <summary>Configurable Forma-safe triangle, element, parameter, and file-size budget thresholds.</summary>
public sealed class ShowcaseBudgetLimits
{
    public static ShowcaseBudgetLimits Default { get; } = new();

    /// <summary>Soft warning threshold for total estimated triangles across the companion model.</summary>
    public int SoftTriangleLimit { get; set; } = 250_000;

    /// <summary>Hard gate threshold for total estimated triangles across the companion model.</summary>
    public int HardTriangleLimit { get; set; } = 500_000;

    /// <summary>Soft warning threshold for total showcase elements.</summary>
    public int SoftElementLimit { get; set; } = 15_000;

    /// <summary>Hard gate threshold for total showcase elements.</summary>
    public int HardElementLimit { get; set; } = 40_000;

    /// <summary>Hard triangle cap per instanced POI / node / finding marker placement.</summary>
    public int MaxPoiMarkerTriangles { get; set; } = ShowcaseMarkerPlacement.MaxAllowedMarkerTriangles;

    /// <summary>Soft warning threshold for any single showcase element's estimated triangle count.</summary>
    public int SingleElementTriangleWarningLimit { get; set; } = 5_000;

    /// <summary>Soft warning threshold for unique <c>ks_*</c> direct shared parameters (aligning with F3's 64-parameter target).</summary>
    public int SoftUniqueParameterLimit { get; set; } = 64;

    /// <summary>Hard gate threshold for unique <c>ks_*</c> direct shared parameters (matching <see cref="ShowcaseParameterRegistry.MaxDirectParameters"/> = 80).</summary>
    public int HardUniqueParameterLimit { get; set; } = ShowcaseParameterRegistry.MaxDirectParameters;

    /// <summary>Soft warning threshold for saved companion <c>.rvt</c> size in MiB.</summary>
    public double SoftFileSizeMiB { get; set; } = 100.0d;

    /// <summary>Hard gate threshold for saved companion <c>.rvt</c> size in MiB.</summary>
    public double HardFileSizeMiB { get; set; } = 250.0d;
}

/// <summary>Per-metric evaluation outcome within a <see cref="ShowcaseBudgetReport"/>.</summary>
public sealed class ShowcaseBudgetMetricResult
{
    public ShowcaseBudgetMetricResult(
        string metricId,
        string labelEn,
        string labelJa,
        double actualValue,
        double? softLimit,
        double? hardLimit,
        string unit,
        ShowcaseBudgetSeverity severity,
        string messageEn,
        string messageJa)
    {
        MetricId = metricId ?? throw new ArgumentNullException(nameof(metricId));
        LabelEn = labelEn ?? string.Empty;
        LabelJa = labelJa ?? string.Empty;
        ActualValue = actualValue;
        SoftLimit = softLimit;
        HardLimit = hardLimit;
        Unit = unit ?? string.Empty;
        Severity = severity;
        MessageEn = messageEn ?? string.Empty;
        MessageJa = messageJa ?? string.Empty;
    }

    public string MetricId { get; }

    public string LabelEn { get; }

    public string LabelJa { get; }

    public double ActualValue { get; }

    public double? SoftLimit { get; }

    public double? HardLimit { get; }

    public string Unit { get; }

    public ShowcaseBudgetSeverity Severity { get; }

    public string MessageEn { get; }

    public string MessageJa { get; }
}

/// <summary>Per-layer / category triangle and element breakdown for budget diagnostics.</summary>
public sealed class ShowcaseLayerBudgetBreakdown
{
    public ShowcaseLayerBudgetBreakdown(
        string layerName,
        ShowcaseLayerKind layerKind,
        string categoryBucket,
        int elementCount,
        int estimatedTriangles,
        int maxElementTriangles,
        double shareOfTotalTriangles)
    {
        LayerName = layerName ?? string.Empty;
        LayerKind = layerKind;
        CategoryBucket = categoryBucket ?? string.Empty;
        ElementCount = elementCount;
        EstimatedTriangles = estimatedTriangles;
        MaxElementTriangles = maxElementTriangles;
        ShareOfTotalTriangles = shareOfTotalTriangles;
    }

    public string LayerName { get; }

    public ShowcaseLayerKind LayerKind { get; }

    /// <summary>Semantic budget bucket (<c>unit</c>, <c>opening</c>, <c>fixture</c>, <c>level_outline</c>, <c>point_facility</c>, <c>network_link</c>, <c>network_connector</c>, <c>net_junction</c>, etc.).</summary>
    public string CategoryBucket { get; }

    public int ElementCount { get; }

    public int EstimatedTriangles { get; }

    public int MaxElementTriangles { get; }

    public double ShareOfTotalTriangles { get; }
}

/// <summary>Structured Forma Companion geometry, element, parameter, and file-size budget evaluation report.</summary>
public sealed class ShowcaseBudgetReport
{
    public ShowcaseBudgetReport(
        ShowcaseBudgetSeverity severity,
        int totalElements,
        int totalEstimatedTriangles,
        int maxSingleElementTriangles,
        int maxPoiMarkerTrianglesObserved,
        int uniqueShowcaseParameterCount,
        long? outputFileSizeBytes,
        IReadOnlyList<ShowcaseBudgetMetricResult> metrics,
        IReadOnlyList<ShowcaseLayerBudgetBreakdown> layerBreakdowns,
        IReadOnlyList<string> recommendationsEn,
        IReadOnlyList<string> recommendationsJa)
    {
        Severity = severity;
        TotalElements = totalElements;
        TotalEstimatedTriangles = totalEstimatedTriangles;
        MaxSingleElementTriangles = maxSingleElementTriangles;
        MaxPoiMarkerTrianglesObserved = maxPoiMarkerTrianglesObserved;
        UniqueShowcaseParameterCount = uniqueShowcaseParameterCount;
        OutputFileSizeBytes = outputFileSizeBytes;
        Metrics = metrics ?? Array.Empty<ShowcaseBudgetMetricResult>();
        LayerBreakdowns = layerBreakdowns ?? Array.Empty<ShowcaseLayerBudgetBreakdown>();
        TopHeaviestLayers = LayerBreakdowns.Take(5).ToArray();
        RecommendationsEn = recommendationsEn ?? Array.Empty<string>();
        RecommendationsJa = recommendationsJa ?? Array.Empty<string>();
    }

    /// <summary>True when zero hard budget limits were exceeded (<see cref="Severity"/> != <see cref="ShowcaseBudgetSeverity.Exceeded"/>).</summary>
    public bool Passed => Severity != ShowcaseBudgetSeverity.Exceeded;

    public ShowcaseBudgetSeverity Severity { get; }

    public int TotalElements { get; }

    public int TotalEstimatedTriangles { get; }

    public int MaxSingleElementTriangles { get; }

    public int MaxPoiMarkerTrianglesObserved { get; }

    public int UniqueShowcaseParameterCount { get; }

    public long? OutputFileSizeBytes { get; }

    public double? OutputFileSizeMiB =>
        OutputFileSizeBytes.HasValue ? OutputFileSizeBytes.Value / (1024d * 1024d) : null;

    public IReadOnlyList<ShowcaseBudgetMetricResult> Metrics { get; }

    public IReadOnlyList<ShowcaseLayerBudgetBreakdown> LayerBreakdowns { get; }

    /// <summary>Top 5 heaviest layers ordered by estimated triangle count descending.</summary>
    public IReadOnlyList<ShowcaseLayerBudgetBreakdown> TopHeaviestLayers { get; }

    public IReadOnlyList<string> RecommendationsEn { get; }

    public IReadOnlyList<string> RecommendationsJa { get; }
}

/// <summary>Exception thrown when a Forma Companion build exceeds a hard budget gate under <see cref="ShowcaseBudgetEnforcementMode.FailOnHardLimit"/>.</summary>
public sealed class ShowcaseBudgetExceededException : InvalidOperationException
{
    public ShowcaseBudgetExceededException(ShowcaseBudgetReport report)
        : base(BuildMessage(report))
    {
        Report = report ?? throw new ArgumentNullException(nameof(report));
    }

    public ShowcaseBudgetReport Report { get; }

    private static string BuildMessage(ShowcaseBudgetReport report)
    {
        if (report is null)
        {
            return "Forma Companion showcase build exceeded hard budget limits.";
        }

        string[] exceeded = report.Metrics
            .Where(m => m.Severity == ShowcaseBudgetSeverity.Exceeded)
            .Select(m => m.MessageEn)
            .ToArray();

        return exceeded.Length > 0
            ? "Forma Companion showcase build exceeded hard budget limit(s): " + string.Join(" ", exceeded)
            : "Forma Companion showcase build exceeded hard budget limits.";
    }
}

/// <summary>
/// Pure-C# evaluator gating Forma Companion triangle counts, element counts, per-marker/per-element triangle
/// budgets, unique <c>ks_*</c> parameter count, and saved <c>.rvt</c> file size in MiB.
/// </summary>
public static class ShowcaseBudgetEvaluator
{
    public const string TotalTrianglesMetricId = "total_triangles";
    public const string TotalElementsMetricId = "total_elements";
    public const string PoiMarkerTrianglesMetricId = "poi_marker_triangles";
    public const string SingleElementTrianglesMetricId = "single_element_triangles";
    public const string UniqueParametersMetricId = "unique_parameters";
    public const string OutputFileSizeMetricId = "output_file_size_mib";

    public static ShowcaseBudgetReport Evaluate(
        IReadOnlyList<ShowcaseElementPlan> plannedElements,
        ShowcaseParameterRegistry? parameterRegistry = null,
        long? outputFileSizeBytes = null,
        ShowcaseBudgetLimits? limits = null)
    {
        if (plannedElements is null)
        {
            throw new ArgumentNullException(nameof(plannedElements));
        }

        ShowcaseBudgetLimits effective = limits ?? ShowcaseBudgetLimits.Default;

        int totalElements = plannedElements.Count;
        int totalTriangles = 0;
        int maxSingleElementTriangles = 0;
        string? heaviestElementId = null;
        int maxMarkerTriangles = 0;
        string? heaviestMarkerElementId = null;

        Dictionary<(string LayerName, ShowcaseLayerKind Kind, string Bucket), LayerAccumulator> layerBuckets = new();

        for (int i = 0; i < plannedElements.Count; i++)
        {
            ShowcaseElementPlan element = plannedElements[i];
            int tris = Math.Max(0, element.EstimatedTriangleCount);
            totalTriangles += tris;

            if (tris > maxSingleElementTriangles)
            {
                maxSingleElementTriangles = tris;
                heaviestElementId = element.ApplicationDataId;
            }

            for (int m = 0; m < element.MarkerPlacements.Count; m++)
            {
                int markerTris = Math.Max(0, element.MarkerPlacements[m].EstimatedTriangles);
                if (markerTris > maxMarkerTriangles)
                {
                    maxMarkerTriangles = markerTris;
                    heaviestMarkerElementId = element.ApplicationDataId;
                }
            }

            string bucket = ResolveCategoryBucket(element);
            (string, ShowcaseLayerKind, string) key = (element.SourceLayerName, element.LayerKind, bucket);
            if (!layerBuckets.TryGetValue(key, out LayerAccumulator? acc))
            {
                acc = new LayerAccumulator(element.SourceLayerName, element.LayerKind, bucket);
                layerBuckets[key] = acc;
            }

            acc.ElementCount++;
            acc.EstimatedTriangles += tris;
            if (tris > acc.MaxElementTriangles)
            {
                acc.MaxElementTriangles = tris;
            }
        }

        int uniqueParameterCount = parameterRegistry != null
            ? parameterRegistry.DirectParameters.Count
            : CountUniqueDirectParametersFromPlans(plannedElements);

        List<ShowcaseLayerBudgetBreakdown> breakdowns = layerBuckets.Values
            .OrderByDescending(b => b.EstimatedTriangles)
            .ThenByDescending(b => b.ElementCount)
            .ThenBy(b => b.LayerName, StringComparer.Ordinal)
            .Select(b => new ShowcaseLayerBudgetBreakdown(
                b.LayerName,
                b.LayerKind,
                b.CategoryBucket,
                b.ElementCount,
                b.EstimatedTriangles,
                b.MaxElementTriangles,
                totalTriangles > 0 ? (double)b.EstimatedTriangles / totalTriangles : 0d))
            .ToList();

        List<ShowcaseBudgetMetricResult> metrics = new();
        List<string> recEn = new();
        List<string> recJa = new();

        // 1. Total estimated triangles
        ShowcaseBudgetSeverity triSeverity = ClassifyRange(totalTriangles, effective.SoftTriangleLimit, effective.HardTriangleLimit);
        metrics.Add(new ShowcaseBudgetMetricResult(
            TotalTrianglesMetricId,
            "Total estimated triangles",
            "推定総三角形数",
            totalTriangles,
            effective.SoftTriangleLimit,
            effective.HardTriangleLimit,
            "triangles",
            triSeverity,
            BuildRangeMessageEn("Estimated triangles", totalTriangles.ToString("N0", CultureInfo.InvariantCulture), effective.SoftTriangleLimit.ToString("N0", CultureInfo.InvariantCulture), effective.HardTriangleLimit.ToString("N0", CultureInfo.InvariantCulture), triSeverity),
            BuildRangeMessageJa("推定総三角形数", totalTriangles.ToString("N0", CultureInfo.InvariantCulture), effective.SoftTriangleLimit.ToString("N0", CultureInfo.InvariantCulture), effective.HardTriangleLimit.ToString("N0", CultureInfo.InvariantCulture), triSeverity)));
        if (triSeverity != ShowcaseBudgetSeverity.Ok)
        {
            string topLayerHint = breakdowns.Count > 0 ? $" Top contributor: '{breakdowns[0].LayerName}' ({breakdowns[0].EstimatedTriangles:N0} tris)." : string.Empty;
            string topLayerHintJa = breakdowns.Count > 0 ? $" 最大寄与レイヤ: '{breakdowns[0].LayerName}' ({breakdowns[0].EstimatedTriangles:N0} 三角形)。" : string.Empty;
            recEn.Add($"Simplify high-vertex polygons or increase network chevron spacing to keep Forma viewer triangles below {effective.SoftTriangleLimit:N0}.{topLayerHint}");
            recJa.Add($"Forma ビューアの軽快さを維持するため、頂点数が多いポリゴンの簡略化やシェブロン間隔の拡大で三角形数を {effective.SoftTriangleLimit:N0} 未満に抑えてください。{topLayerHintJa}");
        }

        // 2. Total showcase elements
        ShowcaseBudgetSeverity elemSeverity = ClassifyRange(totalElements, effective.SoftElementLimit, effective.HardElementLimit);
        metrics.Add(new ShowcaseBudgetMetricResult(
            TotalElementsMetricId,
            "Total showcase elements",
            "ショーケース総要素数",
            totalElements,
            effective.SoftElementLimit,
            effective.HardElementLimit,
            "elements",
            elemSeverity,
            BuildRangeMessageEn("Showcase elements", totalElements.ToString("N0", CultureInfo.InvariantCulture), effective.SoftElementLimit.ToString("N0", CultureInfo.InvariantCulture), effective.HardElementLimit.ToString("N0", CultureInfo.InvariantCulture), elemSeverity),
            BuildRangeMessageJa("ショーケース総要素数", totalElements.ToString("N0", CultureInfo.InvariantCulture), effective.SoftElementLimit.ToString("N0", CultureInfo.InvariantCulture), effective.HardElementLimit.ToString("N0", CultureInfo.InvariantCulture), elemSeverity)));
        if (elemSeverity != ShowcaseBudgetSeverity.Ok)
        {
            recEn.Add($"Split oversized station scopes into per-building or per-zone companion snapshots to stay under {effective.SoftElementLimit:N0} elements.");
            recJa.Add($"大規模ステーションは建物単位・ゾーン単位でショーケーススナップショットを分割し、要素数を {effective.SoftElementLimit:N0} 未満に収めてください。");
        }

        // 3. Per-marker max triangles (hard cap <= 48)
        ShowcaseBudgetSeverity markerSeverity = maxMarkerTriangles > effective.MaxPoiMarkerTriangles
            ? ShowcaseBudgetSeverity.Exceeded
            : ShowcaseBudgetSeverity.Ok;
        metrics.Add(new ShowcaseBudgetMetricResult(
            PoiMarkerTrianglesMetricId,
            "Max instanced marker triangles",
            "インスタンスマーカー最大三角形数",
            maxMarkerTriangles,
            softLimit: null,
            hardLimit: effective.MaxPoiMarkerTriangles,
            "triangles",
            markerSeverity,
            markerSeverity == ShowcaseBudgetSeverity.Exceeded
                ? $"Marker '{heaviestMarkerElementId}' uses {maxMarkerTriangles} triangles, exceeding the hard cap of {effective.MaxPoiMarkerTriangles} triangles per marker."
                : $"Max marker triangle count is {maxMarkerTriangles} (limit <= {effective.MaxPoiMarkerTriangles}).",
            markerSeverity == ShowcaseBudgetSeverity.Exceeded
                ? $"マーカー '{heaviestMarkerElementId}' の三角形数 ({maxMarkerTriangles}) が上限 ({effective.MaxPoiMarkerTriangles}) を超過しています。"
                : $"マーカー最大三角形数は {maxMarkerTriangles} です（上限 {effective.MaxPoiMarkerTriangles} 以下）。"));
        if (markerSeverity == ShowcaseBudgetSeverity.Exceeded)
        {
            recEn.Add($"Keep instanced POI/node/finding marker prototypes at or below {effective.MaxPoiMarkerTriangles} planar triangles.");
            recJa.Add($"POI・ノード・検出マーカーのインスタンス形状は平面多角形 {effective.MaxPoiMarkerTriangles} 三角形以下に抑えてください。");
        }

        // 4. Single-element max triangles (soft warning > 5,000)
        ShowcaseBudgetSeverity singleElemSeverity = maxSingleElementTriangles > effective.SingleElementTriangleWarningLimit
            ? ShowcaseBudgetSeverity.Warning
            : ShowcaseBudgetSeverity.Ok;
        metrics.Add(new ShowcaseBudgetMetricResult(
            SingleElementTrianglesMetricId,
            "Heaviest single element triangles",
            "単一要素最大三角形数",
            maxSingleElementTriangles,
            softLimit: effective.SingleElementTriangleWarningLimit,
            hardLimit: null,
            "triangles",
            singleElemSeverity,
            singleElemSeverity == ShowcaseBudgetSeverity.Warning
                ? $"Element '{heaviestElementId}' has {maxSingleElementTriangles:N0} estimated triangles (warning threshold: {effective.SingleElementTriangleWarningLimit:N0})."
                : $"Heaviest single element has {maxSingleElementTriangles:N0} estimated triangles (warning threshold: {effective.SingleElementTriangleWarningLimit:N0}).",
            singleElemSeverity == ShowcaseBudgetSeverity.Warning
                ? $"要素 '{heaviestElementId}' の推定三角形数 ({maxSingleElementTriangles:N0}) が警告しきい値 ({effective.SingleElementTriangleWarningLimit:N0}) を超過しています。"
                : $"単一要素の最大推定三角形数は {maxSingleElementTriangles:N0} です（警告しきい値: {effective.SingleElementTriangleWarningLimit:N0}）。"));
        if (singleElemSeverity == ShowcaseBudgetSeverity.Warning)
        {
            recEn.Add($"Inspect complex geometry on '{heaviestElementId}' ({maxSingleElementTriangles:N0} tris) and simplify overly dense vertices before publishing to Forma.");
            recJa.Add($"複雑な形状を持つ '{heaviestElementId}' ({maxSingleElementTriangles:N0} 三角形) の頂点密度を見直してください。");
        }

        // 5. Unique ks_* direct shared parameters
        ShowcaseBudgetSeverity paramSeverity = ClassifyRange(uniqueParameterCount, effective.SoftUniqueParameterLimit, effective.HardUniqueParameterLimit);
        metrics.Add(new ShowcaseBudgetMetricResult(
            UniqueParametersMetricId,
            "Unique ks_* direct parameters",
            "一意な ks_* 共有パラメータ数",
            uniqueParameterCount,
            effective.SoftUniqueParameterLimit,
            effective.HardUniqueParameterLimit,
            "parameters",
            paramSeverity,
            BuildRangeMessageEn("Unique ks_* direct parameters", uniqueParameterCount.ToString(CultureInfo.InvariantCulture), effective.SoftUniqueParameterLimit.ToString(CultureInfo.InvariantCulture), effective.HardUniqueParameterLimit.ToString(CultureInfo.InvariantCulture), paramSeverity),
            BuildRangeMessageJa("一意な ks_* 共有パラメータ数", uniqueParameterCount.ToString(CultureInfo.InvariantCulture), effective.SoftUniqueParameterLimit.ToString(CultureInfo.InvariantCulture), effective.HardUniqueParameterLimit.ToString(CultureInfo.InvariantCulture), paramSeverity)));
        if (paramSeverity != ShowcaseBudgetSeverity.Ok)
        {
            recEn.Add($"Keep direct ks_* shared parameters lean (<= {effective.SoftUniqueParameterLimit}); low-priority attributes overflow automatically into ks_attributes_json.");
            recJa.Add($"直接バインドする ks_* 共有パラメータ数は {effective.SoftUniqueParameterLimit} 以下を推奨します（余剰属性は自動的に ks_attributes_json に格納されます）。");
        }

        // 6. Optional saved .rvt file size in MiB
        if (outputFileSizeBytes.HasValue)
        {
            double sizeMiB = Math.Max(0L, outputFileSizeBytes.Value) / (1024d * 1024d);
            ShowcaseBudgetSeverity sizeSeverity = ClassifyRange(sizeMiB, effective.SoftFileSizeMiB, effective.HardFileSizeMiB);
            metrics.Add(new ShowcaseBudgetMetricResult(
                OutputFileSizeMetricId,
                "Companion .rvt file size",
                "コンパニオン .rvt ファイルサイズ",
                Math.Round(sizeMiB, 2),
                effective.SoftFileSizeMiB,
                effective.HardFileSizeMiB,
                "MiB",
                sizeSeverity,
                BuildRangeMessageEn("Companion .rvt size", $"{sizeMiB:F2} MiB", $"{effective.SoftFileSizeMiB:F0} MiB", $"{effective.HardFileSizeMiB:F0} MiB", sizeSeverity),
                BuildRangeMessageJa("コンパニオン .rvt サイズ", $"{sizeMiB:F2} MiB", $"{effective.SoftFileSizeMiB:F0} MiB", $"{effective.HardFileSizeMiB:F0} MiB", sizeSeverity)));
            if (sizeSeverity != ShowcaseBudgetSeverity.Ok)
            {
                recEn.Add($"Compact the companion .rvt on SaveAs and verify instanced markers/DirectShapeTypes are not duplicated to stay under {effective.SoftFileSizeMiB:F0} MiB.");
                recJa.Add($"SaveAs 時の圧縮 (Compact) とマーカーのインスタンス共有を確認し、.rvt サイズを {effective.SoftFileSizeMiB:F0} MiB 未満に抑えてください。");
            }
        }

        ShowcaseBudgetSeverity overall = ShowcaseBudgetSeverity.Ok;
        foreach (ShowcaseBudgetMetricResult metric in metrics)
        {
            if (metric.Severity > overall)
            {
                overall = metric.Severity;
            }
        }

        return new ShowcaseBudgetReport(
            overall,
            totalElements,
            totalTriangles,
            maxSingleElementTriangles,
            maxMarkerTriangles,
            uniqueParameterCount,
            outputFileSizeBytes,
            metrics,
            breakdowns,
            recEn,
            recJa);
    }

    /// <summary>
    /// Returns a new <see cref="ShowcaseBudgetReport"/> derived from <paramref name="baseReport"/> with
    /// post-save companion <c>.rvt</c> file size evaluated against <paramref name="limits"/>.
    /// </summary>
    public static ShowcaseBudgetReport WithOutputFileSize(
        ShowcaseBudgetReport baseReport,
        long outputFileSizeBytes,
        ShowcaseBudgetLimits? limits = null)
    {
        if (baseReport is null)
        {
            throw new ArgumentNullException(nameof(baseReport));
        }

        ShowcaseBudgetLimits effective = limits ?? ShowcaseBudgetLimits.Default;
        double sizeMiB = Math.Max(0L, outputFileSizeBytes) / (1024d * 1024d);
        ShowcaseBudgetSeverity sizeSeverity = ClassifyRange(sizeMiB, effective.SoftFileSizeMiB, effective.HardFileSizeMiB);

        List<ShowcaseBudgetMetricResult> metrics = baseReport.Metrics
            .Where(m => !string.Equals(m.MetricId, OutputFileSizeMetricId, StringComparison.Ordinal))
            .ToList();
        metrics.Add(new ShowcaseBudgetMetricResult(
            OutputFileSizeMetricId,
            "Companion .rvt file size",
            "コンパニオン .rvt ファイルサイズ",
            Math.Round(sizeMiB, 2),
            effective.SoftFileSizeMiB,
            effective.HardFileSizeMiB,
            "MiB",
            sizeSeverity,
            BuildRangeMessageEn("Companion .rvt size", $"{sizeMiB:F2} MiB", $"{effective.SoftFileSizeMiB:F0} MiB", $"{effective.HardFileSizeMiB:F0} MiB", sizeSeverity),
            BuildRangeMessageJa("コンパニオン .rvt サイズ", $"{sizeMiB:F2} MiB", $"{effective.SoftFileSizeMiB:F0} MiB", $"{effective.HardFileSizeMiB:F0} MiB", sizeSeverity)));

        List<string> recEn = new(baseReport.RecommendationsEn);
        List<string> recJa = new(baseReport.RecommendationsJa);
        if (sizeSeverity != ShowcaseBudgetSeverity.Ok)
        {
            string en = $"Compact the companion .rvt on SaveAs and verify instanced markers/DirectShapeTypes are not duplicated to stay under {effective.SoftFileSizeMiB:F0} MiB.";
            string ja = $"SaveAs 時の圧縮 (Compact) とマーカーのインスタンス共有を確認し、.rvt サイズを {effective.SoftFileSizeMiB:F0} MiB 未満に抑えてください。";
            if (!recEn.Contains(en, StringComparer.Ordinal))
            {
                recEn.Add(en);
            }

            if (!recJa.Contains(ja, StringComparer.Ordinal))
            {
                recJa.Add(ja);
            }
        }

        ShowcaseBudgetSeverity overall = ShowcaseBudgetSeverity.Ok;
        foreach (ShowcaseBudgetMetricResult metric in metrics)
        {
            if (metric.Severity > overall)
            {
                overall = metric.Severity;
            }
        }

        return new ShowcaseBudgetReport(
            overall,
            baseReport.TotalElements,
            baseReport.TotalEstimatedTriangles,
            baseReport.MaxSingleElementTriangles,
            baseReport.MaxPoiMarkerTrianglesObserved,
            baseReport.UniqueShowcaseParameterCount,
            outputFileSizeBytes,
            metrics,
            baseReport.LayerBreakdowns,
            recEn,
            recJa);
    }

    /// <summary>Enforces <paramref name="mode"/> against <paramref name="report"/>, appending warnings or throwing <see cref="ShowcaseBudgetExceededException"/>.</summary>
    public static void ApplyEnforcement(
        ShowcaseBudgetReport report,
        ShowcaseBudgetEnforcementMode mode,
        ICollection<string>? warnings = null)
    {
        if (report is null)
        {
            throw new ArgumentNullException(nameof(report));
        }

        if (mode == ShowcaseBudgetEnforcementMode.FailOnHardLimit && !report.Passed)
        {
            throw new ShowcaseBudgetExceededException(report);
        }

        if (mode == ShowcaseBudgetEnforcementMode.ReportOnly || warnings == null)
        {
            return;
        }

        foreach (ShowcaseBudgetMetricResult metric in report.Metrics)
        {
            if (metric.Severity == ShowcaseBudgetSeverity.Warning || metric.Severity == ShowcaseBudgetSeverity.Exceeded)
            {
                warnings.Add($"[Budget:{metric.Severity}] {metric.MessageEn}");
            }
        }
    }

    /// <summary>Classifies a planned element into its canonical Forma budget category bucket.</summary>
    public static string ResolveCategoryBucket(ShowcaseElementPlan element)
    {
        if (element.ElementKind == ShowcaseElementKind.FindingPuck)
        {
            return "net_junction";
        }

        switch (element.LayerKind)
        {
            case ShowcaseLayerKind.Unit: return "floor_polygon";
            case ShowcaseLayerKind.Opening: return "opening_polygon";
            case ShowcaseLayerKind.Fixture: return "fixture_polygon";
            case ShowcaseLayerKind.Level: return "level_outline";
            case ShowcaseLayerKind.PointFacility: return "point_facility";
            case ShowcaseLayerKind.PointFacilityNetwork: return "point_facility_network";
            case ShowcaseLayerKind.NetworkLink:
                return element.ElementKind == ShowcaseElementKind.VerticalTube ? "network_connector" : "network_link";
            case ShowcaseLayerKind.NetworkConnector: return "network_connector";
            case ShowcaseLayerKind.NetworkNode: return "network_node";
            default:
                return ShowcaseSnapshotReader.ToContractString(element.LayerKind);
        }
    }

    private static ShowcaseBudgetSeverity ClassifyRange(double actual, double softLimit, double hardLimit)
    {
        if (actual > hardLimit)
        {
            return ShowcaseBudgetSeverity.Exceeded;
        }

        if (actual > softLimit)
        {
            return ShowcaseBudgetSeverity.Warning;
        }

        return ShowcaseBudgetSeverity.Ok;
    }

    private static string BuildRangeMessageEn(
        string metricName,
        string actualText,
        string softText,
        string hardText,
        ShowcaseBudgetSeverity severity)
    {
        switch (severity)
        {
            case ShowcaseBudgetSeverity.Exceeded:
                return $"{metricName} = {actualText} exceeds hard limit ({hardText}).";
            case ShowcaseBudgetSeverity.Warning:
                return $"{metricName} = {actualText} exceeds soft warning threshold ({softText}; hard limit {hardText}).";
            default:
                return $"{metricName} = {actualText} (within soft limit {softText}, hard limit {hardText}).";
        }
    }

    private static string BuildRangeMessageJa(
        string metricName,
        string actualText,
        string softText,
        string hardText,
        ShowcaseBudgetSeverity severity)
    {
        switch (severity)
        {
            case ShowcaseBudgetSeverity.Exceeded:
                return $"{metricName} ({actualText}) がハード上限 ({hardText}) を超過しています。";
            case ShowcaseBudgetSeverity.Warning:
                return $"{metricName} ({actualText}) が警告しきい値 ({softText} / ハード上限 {hardText}) を超過しています。";
            default:
                return $"{metricName} ({actualText}) は許容範囲内です（警告: {softText} / 上限: {hardText}）。";
        }
    }

    private static int CountUniqueDirectParametersFromPlans(IReadOnlyList<ShowcaseElementPlan> plannedElements)
    {
        HashSet<Guid> guids = new();
        for (int i = 0; i < plannedElements.Count; i++)
        {
            IReadOnlyList<ShowcaseParameterAssignment> assignments = plannedElements[i].ParameterAssignments;
            for (int j = 0; j < assignments.Count; j++)
            {
                guids.Add(assignments[j].Parameter.Guid);
            }
        }

        return guids.Count;
    }

    private sealed class LayerAccumulator
    {
        public LayerAccumulator(string layerName, ShowcaseLayerKind layerKind, string categoryBucket)
        {
            LayerName = layerName;
            LayerKind = layerKind;
            CategoryBucket = categoryBucket;
        }

        public string LayerName { get; }
        public ShowcaseLayerKind LayerKind { get; }
        public string CategoryBucket { get; }
        public int ElementCount { get; set; }
        public int EstimatedTriangles { get; set; }
        public int MaxElementTriangles { get; set; }
    }
}
