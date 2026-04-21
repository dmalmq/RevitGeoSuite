namespace RevitGeoSuite.PlateauImport;

public readonly struct ContextShapeTriangle
{
    public ContextShapeTriangle(ContextShapePoint3D a, ContextShapePoint3D b, ContextShapePoint3D c)
    {
        A = a;
        B = b;
        C = c;
    }

    public ContextShapePoint3D A { get; }

    public ContextShapePoint3D B { get; }

    public ContextShapePoint3D C { get; }
}
