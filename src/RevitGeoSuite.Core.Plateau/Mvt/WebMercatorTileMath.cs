using System;

namespace RevitGeoSuite.Core.Plateau.Mvt;

/// <summary>
/// Web Mercator (EPSG:3857) slippy-map tile math for MVT: lon/lat ↔ z/x/y tile indices, and MVT
/// tile-local integer coordinates → lon/lat (WGS84 degrees). PLATEAU MVT tiles use the standard XYZ
/// scheme with the tile origin at the top-left (Y increasing southward).
/// </summary>
public static class WebMercatorTileMath
{
    /// <summary>Tile (x, y) containing the given lon/lat at <paramref name="zoom"/>, clamped to range.</summary>
    public static (int X, int Y) LonLatToTile(double longitudeDeg, double latitudeDeg, int zoom)
    {
        double n = Math.Pow(2, zoom);
        double latRad = DegreesToRadians(Clamp(latitudeDeg, -85.05112878, 85.05112878));
        double xFraction = (longitudeDeg + 180.0) / 360.0;
        double yFraction = (1.0 - (Math.Log(Math.Tan(latRad) + (1.0 / Math.Cos(latRad))) / Math.PI)) / 2.0;

        int x = (int)Math.Floor(xFraction * n);
        int y = (int)Math.Floor(yFraction * n);
        int max = (int)n - 1;
        return (ClampInt(x, 0, max), ClampInt(y, 0, max));
    }

    /// <summary>
    /// Converts a tile-local integer coordinate (0..<paramref name="extent"/>, Y down) within tile
    /// (<paramref name="tileX"/>, <paramref name="tileY"/>) at <paramref name="zoom"/> to lon/lat degrees.
    /// </summary>
    public static (double Longitude, double Latitude) TileLocalToLonLat(
        int zoom, int tileX, int tileY, double localX, double localY, double extent)
    {
        double n = Math.Pow(2, zoom);
        double worldX = tileX + (localX / extent);
        double worldY = tileY + (localY / extent);

        double longitude = (worldX / n * 360.0) - 180.0;
        double m = Math.PI - (2.0 * Math.PI * worldY / n);
        double latitude = RadiansToDegrees(Math.Atan(Math.Sinh(m)));
        return (longitude, latitude);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;

    private static double Clamp(double value, double min, double max) => value < min ? min : (value > max ? max : value);

    private static int ClampInt(int value, int min, int max) => value < min ? min : (value > max ? max : value);
}
