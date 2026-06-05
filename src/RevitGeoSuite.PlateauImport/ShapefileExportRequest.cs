using System;
using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Fully-resolved input for a PLATEAU context export (scan result + reference + selections + Kiban
/// source). Kept as a top-level public type so the shared <see cref="PlateauContextExportPipeline"/>
/// and web shell handlers can build and pass it without UI-specific state.
/// </summary>
public sealed class ShapefileExportRequest
{
    public ShapefileExportRequest(
        PlateauFolderScanResult scanResult,
        PlateauImportReferenceContext referenceContext,
        IReadOnlyCollection<PlateauFeatureType> selectedFeatureTypes,
        IReadOnlyCollection<string> selectedTileIds,
        string kibanFolderPath,
        IReadOnlyList<KibanParsedFeature>? kibanParsedFeatures,
        IReadOnlyList<KibanParsedPolygonFeature>? kibanParsedPolygonFeatures,
        IReadOnlyCollection<string> selectedKibanLayerNames,
        bool hasKibanLayerOptions,
        IReadOnlyCollection<string>? additionalGreenLandUseTokens = null)
    {
        ScanResult = scanResult ?? throw new ArgumentNullException(nameof(scanResult));
        ReferenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext));
        SelectedFeatureTypes = selectedFeatureTypes ?? throw new ArgumentNullException(nameof(selectedFeatureTypes));
        SelectedTileIds = selectedTileIds ?? throw new ArgumentNullException(nameof(selectedTileIds));
        KibanFolderPath = kibanFolderPath ?? string.Empty;
        KibanParsedFeatures = kibanParsedFeatures;
        KibanParsedPolygonFeatures = kibanParsedPolygonFeatures;
        SelectedKibanLayerNames = selectedKibanLayerNames ?? throw new ArgumentNullException(nameof(selectedKibanLayerNames));
        HasKibanLayerOptions = hasKibanLayerOptions;
        AdditionalGreenLandUseTokens = additionalGreenLandUseTokens;
    }

    public PlateauFolderScanResult ScanResult { get; }

    public PlateauImportReferenceContext ReferenceContext { get; }

    public IReadOnlyCollection<PlateauFeatureType> SelectedFeatureTypes { get; }

    public IReadOnlyCollection<string> SelectedTileIds { get; }

    public string KibanFolderPath { get; }

    public bool HasKibanFolder => !string.IsNullOrWhiteSpace(KibanFolderPath);

    public IReadOnlyList<KibanParsedFeature>? KibanParsedFeatures { get; }

    public IReadOnlyList<KibanParsedPolygonFeature>? KibanParsedPolygonFeatures { get; }

    public IReadOnlyCollection<string> SelectedKibanLayerNames { get; }

    public bool HasKibanLayerOptions { get; }

    public IReadOnlyCollection<string>? AdditionalGreenLandUseTokens { get; }
}
