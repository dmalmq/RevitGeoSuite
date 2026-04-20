namespace RevitGeoSuite.PlateauImport;

public readonly struct ContextShapePoint3D
{
    public ContextShapePoint3D(double xFeet, double yFeet, double zFeet)
    {
        XFeet = xFeet;
        YFeet = yFeet;
        ZFeet = zFeet;
    }

    public double XFeet { get; }

    public double YFeet { get; }

    public double ZFeet { get; }
}
