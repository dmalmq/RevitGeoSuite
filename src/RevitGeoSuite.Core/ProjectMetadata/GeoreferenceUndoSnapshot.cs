using System;

namespace RevitGeoSuite.Core.ProjectMetadata;

public sealed class GeoreferenceUndoSnapshot
{
    public DateTime CapturedAtUtc { get; set; }

    public double SiteLatitudeRadians { get; set; }

    public double SiteLongitudeRadians { get; set; }

    public double EastWestFeet { get; set; }

    public double NorthSouthFeet { get; set; }

    public double ElevationFeet { get; set; }

    public double AngleRadians { get; set; }

    public GeoProjectInfo? PreviousGeoInfo { get; set; }

    public string Summary { get; set; } = string.Empty;
}
