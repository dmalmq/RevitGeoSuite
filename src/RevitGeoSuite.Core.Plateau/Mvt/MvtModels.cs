using System.Collections.Generic;

namespace RevitGeoSuite.Core.Plateau.Mvt;

/// <summary>MVT geometry type (Mapbox Vector Tile spec, Feature.type).</summary>
public enum MvtGeometryType
{
    Unknown = 0,
    Point = 1,
    LineString = 2,
    Polygon = 3
}

/// <summary>A vertex in MVT tile-local integer coordinates (origin top-left, Y down, 0..extent).</summary>
public readonly struct MvtPoint
{
    public MvtPoint(int x, int y)
    {
        X = x;
        Y = y;
    }

    public int X { get; }

    public int Y { get; }
}

/// <summary>
/// A decoded MVT feature: its geometry type plus the decoded paths. For points each path is a single
/// vertex (multipoint = many paths); for lines each path is a polyline; for polygons each path is a
/// closed ring (exterior/holes intermixed in encounter order, distinguished later by signed area).
/// Attributes/tags are not decoded — the basemap takes every feature in a layer.
/// </summary>
public sealed class MvtFeature
{
    public MvtFeature(MvtGeometryType geometryType, IReadOnlyList<IReadOnlyList<MvtPoint>> paths)
    {
        GeometryType = geometryType;
        Paths = paths;
    }

    public MvtGeometryType GeometryType { get; }

    public IReadOnlyList<IReadOnlyList<MvtPoint>> Paths { get; }
}

/// <summary>A decoded MVT layer (name, coordinate extent, features).</summary>
public sealed class MvtLayer
{
    public MvtLayer(string name, uint extent, IReadOnlyList<MvtFeature> features)
    {
        Name = name;
        Extent = extent;
        Features = features;
    }

    public string Name { get; }

    /// <summary>Tile coordinate extent (default 4096); local coords run 0..Extent across the tile.</summary>
    public uint Extent { get; }

    public IReadOnlyList<MvtFeature> Features { get; }
}
