using System.Collections.Generic;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DMeshPrimitive
{
    public Tiles3DObjectMetadata Metadata { get; set; } = new Tiles3DObjectMetadata();

    public string Name
    {
        get => Metadata.Name;
        set => Metadata.Name = value ?? string.Empty;
    }

    public string CategoryName
    {
        get => Metadata.Category;
        set => Metadata.Category = value ?? string.Empty;
    }

    public string LevelName
    {
        get => Metadata.LevelName;
        set
        {
            Metadata.LevelName = string.IsNullOrWhiteSpace(value) ? Tiles3DLevelMetadata.UnassignedLevelName : value;
            Metadata.LevelKey = Tiles3DLevelMetadata.BuildLevelKey(Metadata.LevelName);
        }
    }

    public double LevelElevationMeters
    {
        get => Metadata.LevelElevationMeters;
        set => Metadata.LevelElevationMeters = value;
    }

    public Tiles3DMaterialColor Color { get; set; } = Tiles3DMaterialColor.Default;

    public List<Tiles3DTriangle> Triangles { get; set; } = new List<Tiles3DTriangle>();
}
