using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class KibanParsedPolygonFeature
{
    public string Layer { get; set; } = string.Empty;

    public List<List<(double Latitude, double Longitude)>> ExteriorRings { get; set; } = new List<List<(double Latitude, double Longitude)>>();

    public List<List<(double Latitude, double Longitude)>> InteriorRings { get; set; } = new List<List<(double Latitude, double Longitude)>>();

    public string MeshCode { get; set; } = string.Empty;

    public string SourcePath { get; set; } = string.Empty;

    public string SourceId { get; set; } = string.Empty;

    public string Fid { get; set; } = string.Empty;

    public string FeatureType { get; set; } = string.Empty;

    public string Visibility { get; set; } = string.Empty;
}
