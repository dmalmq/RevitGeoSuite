namespace RevitGeoSuite.Core.Coordinates;

public readonly struct GeographicCoordinate
{
    public GeographicCoordinate(double latitude, double longitude)
    {
        Latitude = latitude;
        Longitude = longitude;
    }

    public double Latitude { get; }

    public double Longitude { get; }
}
