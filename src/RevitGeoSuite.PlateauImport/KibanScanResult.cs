using System;
using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Result of scanning a GSI/Kiban (terrain) folder. Kept as a top-level public type so the shared
/// <see cref="PlateauContextExportPipeline"/> and web shell handlers can consume Kiban scans without
/// UI-specific state.
/// </summary>
public sealed class KibanScanResult
{
    public KibanScanResult(
        IReadOnlyList<KibanParsedFeature> features,
        IReadOnlyList<KibanParsedPolygonFeature> polygonFeatures,
        int parsedFileCount,
        int skippedFileCount,
        bool isFromCache = false)
    {
        Features = features ?? throw new ArgumentNullException(nameof(features));
        PolygonFeatures = polygonFeatures ?? throw new ArgumentNullException(nameof(polygonFeatures));
        ParsedFileCount = parsedFileCount;
        SkippedFileCount = skippedFileCount;
        IsFromCache = isFromCache;
    }

    public IReadOnlyList<KibanParsedFeature> Features { get; }

    public IReadOnlyList<KibanParsedPolygonFeature> PolygonFeatures { get; }

    public int ParsedFileCount { get; }

    public int SkippedFileCount { get; }

    public bool IsFromCache { get; }
}
