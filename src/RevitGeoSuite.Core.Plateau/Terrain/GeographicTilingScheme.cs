using System;

namespace RevitGeoSuite.Core.Plateau.Terrain;

/// <summary>A tile's geographic extent in degrees (WGS84).</summary>
public readonly struct GeoTileRectangle
{
    public GeoTileRectangle(double westDegrees, double southDegrees, double eastDegrees, double northDegrees)
    {
        WestDegrees = westDegrees;
        SouthDegrees = southDegrees;
        EastDegrees = eastDegrees;
        NorthDegrees = northDegrees;
    }

    public double WestDegrees { get; }

    public double SouthDegrees { get; }

    public double EastDegrees { get; }

    public double NorthDegrees { get; }

    /// <summary>Maps normalized tile coordinates (u,v in [0,1], origin south-west) to lon/lat degrees.</summary>
    public (double LonDegrees, double LatDegrees) ToLonLat(double u, double v)
    {
        return (
            WestDegrees + (EastDegrees - WestDegrees) * u,
            SouthDegrees + (NorthDegrees - SouthDegrees) * v);
    }
}

/// <summary>Inclusive tile index range covering a bounding box at one level.</summary>
public readonly struct TerrainTileRange
{
    public TerrainTileRange(int xStart, int xEnd, int yStart, int yEnd)
    {
        XStart = xStart;
        XEnd = xEnd;
        YStart = yStart;
        YEnd = yEnd;
    }

    public int XStart { get; }

    public int XEnd { get; }

    public int YStart { get; }

    public int YEnd { get; }

    public int TileCount => (XEnd - XStart + 1) * (YEnd - YStart + 1);
}

/// <summary>
/// The geographic (EPSG:4326) tiling scheme Cesium terrain uses: level 0 has two columns spanning
/// the globe in longitude and one row in latitude, doubling each level. Tile Y is TMS (origin south),
/// matching the <c>"scheme":"tms"</c> layer.json that PLATEAU/Cesium-Ion terrain advertises.
/// </summary>
public static class GeographicTilingScheme
{
    public static int TilesXAtLevel(int level) => 2 << level;  // 2^(level+1)

    public static int TilesYAtLevel(int level) => 1 << level;  // 2^level

    public static GeoTileRectangle TileRectangle(int level, int x, int y)
    {
        if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));

        double xStep = 360d / TilesXAtLevel(level);
        double yStep = 180d / TilesYAtLevel(level);
        double west = -180d + x * xStep;
        double south = -90d + y * yStep;
        return new GeoTileRectangle(west, south, west + xStep, south + yStep);
    }

    public static TerrainTileRange TileRange(int level, double westDegrees, double southDegrees, double eastDegrees, double northDegrees)
    {
        if (level < 0) throw new ArgumentOutOfRangeException(nameof(level));

        int xTiles = TilesXAtLevel(level);
        int yTiles = TilesYAtLevel(level);
        double xStep = 360d / xTiles;
        double yStep = 180d / yTiles;

        int xStart = Clamp((int)Math.Floor((westDegrees + 180d) / xStep), 0, xTiles - 1);
        int xEnd = Clamp((int)Math.Floor((eastDegrees + 180d) / xStep), 0, xTiles - 1);
        int yStart = Clamp((int)Math.Floor((southDegrees + 90d) / yStep), 0, yTiles - 1);
        int yEnd = Clamp((int)Math.Floor((northDegrees + 90d) / yStep), 0, yTiles - 1);
        return new TerrainTileRange(xStart, xEnd, yStart, yEnd);
    }

    private static int Clamp(int value, int min, int max)
    {
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }
}
