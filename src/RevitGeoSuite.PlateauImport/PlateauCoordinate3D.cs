namespace RevitGeoSuite.PlateauImport;

public readonly struct PlateauCoordinate3D
{
    public PlateauCoordinate3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }
}
