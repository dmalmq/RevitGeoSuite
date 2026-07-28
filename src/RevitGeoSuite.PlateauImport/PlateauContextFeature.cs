using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public class PlateauContextFeature
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public PlateauFeatureType FeatureType { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string TileId { get; set; } = string.Empty;

    public int HighestLod { get; set; }

    public IReadOnlyCollection<PlateauCoordinate3D> ExteriorRing { get; set; } = new PlateauCoordinate3D[0];

    public IReadOnlyCollection<PlateauGeometrySurface> GeometrySurfaces { get; set; } = new PlateauGeometrySurface[0];

    public string ClassCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;
}
