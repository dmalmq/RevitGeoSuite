namespace RevitGeoSuite.Core.Mesh;

public readonly struct MeshBounds
{
    public MeshBounds(double southLatitude, double westLongitude, double northLatitude, double eastLongitude)
    {
        SouthLatitude = southLatitude;
        WestLongitude = westLongitude;
        NorthLatitude = northLatitude;
        EastLongitude = eastLongitude;
    }

    public double SouthLatitude { get; }

    public double WestLongitude { get; }

    public double NorthLatitude { get; }

    public double EastLongitude { get; }

    public double LatitudeSpan => NorthLatitude - SouthLatitude;

    public double LongitudeSpan => EastLongitude - WestLongitude;

    public double CenterLatitude => SouthLatitude + (LatitudeSpan / 2.0);

    public double CenterLongitude => WestLongitude + (LongitudeSpan / 2.0);
}
