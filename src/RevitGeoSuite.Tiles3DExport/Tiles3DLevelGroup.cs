using System.Collections.Generic;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DLevelGroup
{
    public string LevelKey { get; set; } = Tiles3DLevelMetadata.UnassignedLevelKey;

    public string LevelName { get; set; } = Tiles3DLevelMetadata.UnassignedLevelName;

    public double LevelElevationMeters { get; set; }

    public int ElementCount => Meshes.Count;

    public double MinZMeters { get; set; }

    public double MaxZMeters { get; set; }

    public List<Tiles3DMeshPrimitive> Meshes { get; set; } = new List<Tiles3DMeshPrimitive>();
}
