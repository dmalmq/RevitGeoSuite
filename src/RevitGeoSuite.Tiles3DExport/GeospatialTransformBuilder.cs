using System;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class GeospatialTransformBuilder
{
    private const double SemiMajorAxis = 6378137.0d;
    private const double FirstEccentricitySquared = 6.6943799901413165e-3d;

    public double[] BuildEastNorthUpTransform(double latitudeDegrees, double longitudeDegrees, double elevationMeters)
    {
        double latitude = DegreesToRadians(latitudeDegrees);
        double longitude = DegreesToRadians(longitudeDegrees);
        (double x, double y, double z) = ToEcef(latitude, longitude, elevationMeters);

        double sinLat = Math.Sin(latitude);
        double cosLat = Math.Cos(latitude);
        double sinLon = Math.Sin(longitude);
        double cosLon = Math.Cos(longitude);

        double eastX = -sinLon;
        double eastY = cosLon;
        double eastZ = 0d;

        double northX = -sinLat * cosLon;
        double northY = -sinLat * sinLon;
        double northZ = cosLat;

        double upX = cosLat * cosLon;
        double upY = cosLat * sinLon;
        double upZ = sinLat;

        return new[]
        {
            eastX, eastY, eastZ, 0d,
            northX, northY, northZ, 0d,
            upX, upY, upZ, 0d,
            x, y, z, 1d
        };
    }

    private static (double X, double Y, double Z) ToEcef(double latitudeRadians, double longitudeRadians, double elevationMeters)
    {
        double sinLat = Math.Sin(latitudeRadians);
        double cosLat = Math.Cos(latitudeRadians);
        double sinLon = Math.Sin(longitudeRadians);
        double cosLon = Math.Cos(longitudeRadians);
        double radius = SemiMajorAxis / Math.Sqrt(1d - (FirstEccentricitySquared * sinLat * sinLat));

        double x = (radius + elevationMeters) * cosLat * cosLon;
        double y = (radius + elevationMeters) * cosLat * sinLon;
        double z = ((radius * (1d - FirstEccentricitySquared)) + elevationMeters) * sinLat;
        return (x, y, z);
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * (Math.PI / 180d);
    }
}
