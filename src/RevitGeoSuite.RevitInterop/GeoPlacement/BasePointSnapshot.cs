namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class BasePointSnapshot
{
    public string Name { get; set; } = string.Empty;

    public double XFeet { get; set; }

    public double YFeet { get; set; }

    public double ZFeet { get; set; }

    public double? SharedEastWestFeet { get; set; }

    public double? SharedNorthSouthFeet { get; set; }

    public double? SharedElevationFeet { get; set; }

    public double? EstimatedLatitudeDegrees { get; set; }

    public double? EstimatedLongitudeDegrees { get; set; }

    public bool HasSharedPosition => SharedEastWestFeet.HasValue && SharedNorthSouthFeet.HasValue && SharedElevationFeet.HasValue;

    public bool HasEstimatedLocation => EstimatedLatitudeDegrees.HasValue && EstimatedLongitudeDegrees.HasValue;
}
