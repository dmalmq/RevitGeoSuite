using System;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class ProjectPositionSnapshot
{
    public double EastWestFeet { get; set; }

    public double NorthSouthFeet { get; set; }

    public double ElevationFeet { get; set; }

    public double AngleRadians { get; set; }

    public double AngleDegrees => AngleRadians * (180.0 / Math.PI);
}
