using System;
using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class KibanLineExportFeature
{
    public KibanLineExportFeature(
        string layer,
        IReadOnlyList<(double X, double Y)> verticesMetres,
        string? sourceId,
        string meshCode,
        string sourcePath,
        string featureType,
        string visibility)
    {
        Layer = layer ?? throw new ArgumentNullException(nameof(layer));
        VerticesMetres = verticesMetres ?? throw new ArgumentNullException(nameof(verticesMetres));
        SourceId = sourceId;
        MeshCode = meshCode ?? string.Empty;
        SourcePath = sourcePath ?? string.Empty;
        FeatureType = featureType ?? string.Empty;
        Visibility = visibility ?? string.Empty;
    }

    public string Layer { get; }

    public IReadOnlyList<(double X, double Y)> VerticesMetres { get; }

    public string? SourceId { get; }

    public string MeshCode { get; }

    public string SourcePath { get; }

    public string FeatureType { get; }

    public string Visibility { get; }
}
