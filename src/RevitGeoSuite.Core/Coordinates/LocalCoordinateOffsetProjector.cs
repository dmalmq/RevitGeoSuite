using System;

namespace RevitGeoSuite.Core.Coordinates;

public static class LocalCoordinateOffsetProjector
{
    private const double EarthRadiusMeters = 6378137d;

    public static GeographicCoordinate Offset(GeographicCoordinate origin, double eastMeters, double northMeters)
    {
        double latitudeRadians = DegreesToRadians(origin.Latitude);
        double latitudeDeltaDegrees = RadiansToDegrees(northMeters / EarthRadiusMeters);
        double longitudeScale = Math.Cos(latitudeRadians);
        if (Math.Abs(longitudeScale) < 1e-9)
        {
            longitudeScale = longitudeScale < 0 ? -1e-9 : 1e-9;
        }

        double longitudeDeltaDegrees = RadiansToDegrees(eastMeters / (EarthRadiusMeters * longitudeScale));
        return new GeographicCoordinate(origin.Latitude + latitudeDeltaDegrees, origin.Longitude + longitudeDeltaDegrees);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180d);
    }

    private static double RadiansToDegrees(double radians)
    {
        return radians * (180d / Math.PI);
    }
}
