using System;
using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class KibanPolygonExportFeature
{
    public KibanPolygonExportFeature(
        string layer,
        IReadOnlyList<(double X, double Y)> exteriorRingMetres,
        IReadOnlyList<IReadOnlyList<(double X, double Y)>> interiorRingsMetres,
        string? sourceId,
        string meshCode,
        string sourcePath,
        string featureType,
        string visibility)
    {
        Layer = layer ?? throw new ArgumentNullException(nameof(layer));
        ExteriorRingMetres = exteriorRingMetres ?? throw new ArgumentNullException(nameof(exteriorRingMetres));
        InteriorRingsMetres = interiorRingsMetres ?? Array.Empty<IReadOnlyList<(double X, double Y)>>();
        SourceId = sourceId;
        MeshCode = meshCode ?? string.Empty;
        SourcePath = sourcePath ?? string.Empty;
        FeatureType = featureType ?? string.Empty;
        Visibility = visibility ?? string.Empty;
    }

    public string Layer { get; }

    public IReadOnlyList<(double X, double Y)> ExteriorRingMetres { get; }

    public IReadOnlyList<IReadOnlyList<(double X, double Y)>> InteriorRingsMetres { get; }

    public string? SourceId { get; }

    public string MeshCode { get; }

    public string SourcePath { get; }

    public string FeatureType { get; }

    public string Visibility { get; }
}
