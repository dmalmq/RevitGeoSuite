using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class KibanParsedFeature
{
    public string Layer { get; set; } = string.Empty;

    public List<(double Latitude, double Longitude)> Vertices { get; set; } = new List<(double, double)>();

    public string MeshCode { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string Fid { get; set; } = string.Empty;

    public string FeatureType { get; set; } = string.Empty;

    public string Visibility { get; set; } = string.Empty;
}
