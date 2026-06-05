using System;
using System.Collections.Generic;

namespace RevitGeoSuite.Core.Plateau.Mvt;

/// <summary>One XYZ tile address.</summary>
public readonly struct MvtTileAddress
{
    public MvtTileAddress(int zoom, int x, int y)
    {
        Zoom = zoom;
        X = x;
        Y = y;
    }

    public int Zoom { get; }

    public int X { get; }

    public int Y { get; }
}

/// <summary>
/// Computes the set of XYZ tiles covering a lon/lat bounding box at a chosen zoom. Used to fetch only
/// the MVT tiles that overlap the user's selected PLATEAU grids, keeping the request bounded.
/// </summary>
public static class MvtTileCoverage
{
    /// <summary>
    /// Returns the tiles at <paramref name="zoom"/> covering the [west,south,east,north] box. Capped at
    /// <paramref name="maxTiles"/> (returns what it has so far rather than throwing) to guard against an
    /// over-large request.
    /// </summary>
    public static IReadOnlyList<MvtTileAddress> TilesForBounds(
        double westDeg,
        double southDeg,
        double eastDeg,
        double northDeg,
        int zoom,
        int maxTiles = 4096)
    {
        (int xWest, int yNorth) = WebMercatorTileMath.LonLatToTile(westDeg, northDeg, zoom);
        (int xEast, int ySouth) = WebMercatorTileMath.LonLatToTile(eastDeg, southDeg, zoom);

        int minX = Math.Min(xWest, xEast);
        int maxX = Math.Max(xWest, xEast);
        int minY = Math.Min(yNorth, ySouth);
        int maxY = Math.Max(yNorth, ySouth);

        List<MvtTileAddress> tiles = new List<MvtTileAddress>();
        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (tiles.Count >= maxTiles)
                {
                    return tiles;
                }

                tiles.Add(new MvtTileAddress(zoom, x, y));
            }
        }

        return tiles;
    }

    /// <summary>Chooses a fetch zoom: the dataset max, but never above <paramref name="zoomCap"/>.</summary>
    public static int ResolveZoom(int? datasetMaxZoom, int zoomCap = 16)
    {
        int max = datasetMaxZoom ?? zoomCap;
        return Math.Min(max, zoomCap);
    }
}
