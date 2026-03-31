using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauBuildingFeature
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public IReadOnlyCollection<PlateauCoordinate3D> ExteriorRing { get; set; } = new PlateauCoordinate3D[0];
}
