using System.Collections.Generic;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DMeshPrimitive
{
    public string Name { get; set; } = string.Empty;

    public string CategoryName { get; set; } = string.Empty;

    public Tiles3DMaterialColor Color { get; set; } = Tiles3DMaterialColor.Default;

    public List<Tiles3DTriangle> Triangles { get; set; } = new List<Tiles3DTriangle>();
}
