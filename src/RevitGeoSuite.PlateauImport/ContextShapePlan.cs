using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class ContextShapePlan
{
    public string DisplayName { get; set; } = string.Empty;

    public string SourceFeatureId { get; set; } = string.Empty;

    public PlateauFeatureType FeatureType { get; set; }

    public string TileId { get; set; } = string.Empty;

    public string SourceFilePath { get; set; } = string.Empty;

    public PlateauGeometryImportMode GeometryMode { get; set; }

    public int SurfaceCount { get; set; }

    public IReadOnlyCollection<(double XFeet, double YFeet)> FootprintPointsFeet { get; set; } = new (double XFeet, double YFeet)[0];

    public double BaseElevationFeet { get; set; }

    public double HeightFeet { get; set; }

    public IReadOnlyCollection<ContextShapeTriangle> Triangles { get; set; } = new ContextShapeTriangle[0];
}
