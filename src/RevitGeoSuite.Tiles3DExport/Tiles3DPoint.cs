namespace RevitGeoSuite.Tiles3DExport;

public readonly struct Tiles3DPoint
{
    public Tiles3DPoint(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }
}
