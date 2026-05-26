using System;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public readonly struct GeodeticCoordinate
{
    public GeodeticCoordinate(double latitudeDegrees, double longitudeDegrees, double altitudeMeters)
    {
        LatitudeDegrees = latitudeDegrees;
        LongitudeDegrees = longitudeDegrees;
        AltitudeMeters = altitudeMeters;
    }

    public double LatitudeDegrees { get; }
    public double LongitudeDegrees { get; }
    public double AltitudeMeters { get; }
}

/// <summary>
/// Earth-Centered Earth-Fixed (ECEF) coordinate conversions for the WGS84 ellipsoid.
/// PLATEAU 3D Tiles emit coordinates in this frame.
/// </summary>
public static class EcefGeodeticConverter
{
    public const double WgsSemiMajorMeters = 6378137.0;
    public const double WgsFlattening = 1.0 / 298.257223563;
    public static readonly double WgsSemiMinorMeters = WgsSemiMajorMeters * (1.0 - WgsFlattening);
    public static readonly double WgsEccentricitySquared = WgsFlattening * (2.0 - WgsFlattening);
    public static readonly double WgsSecondEccentricitySquared =
        (WgsSemiMajorMeters * WgsSemiMajorMeters - WgsSemiMinorMeters * WgsSemiMinorMeters) /
        (WgsSemiMinorMeters * WgsSemiMinorMeters);

    /// <summary>Geodetic (lat°, lon°, h m) -> WGS84 ECEF.</summary>
    public static Vector3d ToEcef(GeodeticCoordinate geodetic)
    {
        double latRad = geodetic.LatitudeDegrees * Math.PI / 180.0;
        double lonRad = geodetic.LongitudeDegrees * Math.PI / 180.0;
        double sinLat = Math.Sin(latRad);
        double cosLat = Math.Cos(latRad);
        double sinLon = Math.Sin(lonRad);
        double cosLon = Math.Cos(lonRad);
        double n = WgsSemiMajorMeters / Math.Sqrt(1.0 - WgsEccentricitySquared * sinLat * sinLat);
        double x = (n + geodetic.AltitudeMeters) * cosLat * cosLon;
        double y = (n + geodetic.AltitudeMeters) * cosLat * sinLon;
        double z = (n * (1.0 - WgsEccentricitySquared) + geodetic.AltitudeMeters) * sinLat;
        return new Vector3d(x, y, z);
    }

    /// <summary>WGS84 ECEF -> geodetic (lat°, lon°, h m) using Heikkinen's closed-form.</summary>
    public static GeodeticCoordinate ToGeodetic(Vector3d ecef)
    {
        double a = WgsSemiMajorMeters;
        double b = WgsSemiMinorMeters;
        double e2 = WgsEccentricitySquared;
        double ep2 = WgsSecondEccentricitySquared;

        double x = ecef.X;
        double y = ecef.Y;
        double z = ecef.Z;
        double p = Math.Sqrt(x * x + y * y);
        double theta = Math.Atan2(z * a, p * b);
        double sinTheta = Math.Sin(theta);
        double cosTheta = Math.Cos(theta);

        double latRad = Math.Atan2(
            z + ep2 * b * sinTheta * sinTheta * sinTheta,
            p - e2 * a * cosTheta * cosTheta * cosTheta);
        double lonRad = Math.Atan2(y, x);
        double sinLat = Math.Sin(latRad);
        double n = a / Math.Sqrt(1.0 - e2 * sinLat * sinLat);
        double altitude = (Math.Abs(p) > 1e-9) ? p / Math.Cos(latRad) - n : Math.Abs(z) - b;

        return new GeodeticCoordinate(
            latRad * 180.0 / Math.PI,
            lonRad * 180.0 / Math.PI,
            altitude);
    }
}
