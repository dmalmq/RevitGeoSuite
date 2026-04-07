namespace RevitGeoSuite.Tiles3DExport;

public readonly struct Tiles3DTriangle
{
    public Tiles3DTriangle(Tiles3DPoint a, Tiles3DPoint b, Tiles3DPoint c)
    {
        A = a;
        B = b;
        C = c;
    }

    public Tiles3DPoint A { get; }

    public Tiles3DPoint B { get; }

    public Tiles3DPoint C { get; }
}
