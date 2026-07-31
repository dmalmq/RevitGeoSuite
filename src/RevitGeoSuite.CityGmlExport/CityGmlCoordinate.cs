using System.Globalization;

namespace RevitGeoSuite.CityGmlExport;

public readonly struct CityGmlCoordinate
{
    public CityGmlCoordinate(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    public string ToPosString()
    {
        return string.Format(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3}", X, Y, Z);
    }
}
