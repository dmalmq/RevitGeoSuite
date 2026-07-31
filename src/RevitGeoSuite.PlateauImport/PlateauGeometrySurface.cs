using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauGeometrySurface
{
    public string SurfaceId { get; set; } = string.Empty;

    public int Lod { get; set; }

    public string SemanticSurfaceType { get; set; } = string.Empty;

    public IReadOnlyCollection<PlateauCoordinate3D> ExteriorRing { get; set; } = new PlateauCoordinate3D[0];

    public IReadOnlyCollection<IReadOnlyCollection<PlateauCoordinate3D>> InteriorRings { get; set; } = new IReadOnlyCollection<PlateauCoordinate3D>[0];
}
