namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public readonly struct BoundingBoxDegrees
{
    public BoundingBoxDegrees(double westLongitude, double southLatitude, double eastLongitude, double northLatitude)
    {
        WestLongitude = westLongitude;
        SouthLatitude = southLatitude;
        EastLongitude = eastLongitude;
        NorthLatitude = northLatitude;
    }

    public double WestLongitude { get; }

    public double SouthLatitude { get; }

    public double EastLongitude { get; }

    public double NorthLatitude { get; }
}
