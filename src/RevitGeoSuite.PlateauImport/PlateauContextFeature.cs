using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public class PlateauContextFeature
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public PlateauFeatureType FeatureType { get; set; }

    public string SourcePath { get; set; } = string.Empty;

    public string TileId { get; set; } = string.Empty;

    public IReadOnlyCollection<PlateauCoordinate3D> ExteriorRing { get; set; } = new PlateauCoordinate3D[0];
}
