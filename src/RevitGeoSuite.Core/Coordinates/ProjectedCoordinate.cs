using System;

namespace RevitGeoSuite.Core.Coordinates;

public readonly struct ProjectedCoordinate
{
    public ProjectedCoordinate(double easting, double northing)
    {
        Easting = easting;
        Northing = northing;
    }

    public double Easting { get; }

    public double Northing { get; }

    public double DistanceFromOriginMeters => Math.Sqrt((Easting * Easting) + (Northing * Northing));

    public bool IsFinite => !double.IsNaN(Easting) && !double.IsInfinity(Easting) && !double.IsNaN(Northing) && !double.IsInfinity(Northing);
}
