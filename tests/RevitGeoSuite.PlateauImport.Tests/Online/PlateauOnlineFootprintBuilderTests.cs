using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class PlateauOnlineFootprintBuilderTests
{
    [Fact]
    public void Build_unions_projected_roof_triangles_and_skips_wall_triangles()
    {
        PlateauTilesetFeature feature = new PlateauTilesetFeature(
            "bldg-1",
            new Dictionary<string, object?>(),
            new List<PlateauTilesetTriangle>
            {
                Tri((0d, 0d, 10d), (10d, 0d, 10d), (10d, 10d, 10d)),
                Tri((0d, 0d, 10d), (10d, 10d, 10d), (0d, 10d, 10d)),
                // Vertical wall: XY projection is a line, so it should not affect the footprint.
                Tri((0d, 0d, 0d), (0d, 10d, 0d), (0d, 10d, 10d)),
            });

        PlateauOnlineFootprintBuilder builder = new PlateauOnlineFootprintBuilder(simplifyToleranceMeters: 0d, minimumTriangleAreaSquareMeters: 0.001d);
        List<string> warnings = new List<string>();

        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = builder.Build(Model(feature), warnings);

        PlateauContextOutlinesDxfWriter.OutlineFeature outline = Assert.Single(outlines);
        Assert.Equal("PLATEAU_BUILDINGS", outline.Layer);
        Assert.Equal("bldg-1", outline.SourceId);
        Assert.Equal(4, outline.VerticesMetres.Count);
        Assert.Empty(warnings);
    }

    [Fact]
    public void Build_emits_courtyard_rings_as_additional_building_outlines()
    {
        List<PlateauTilesetTriangle> triangles = new List<PlateauTilesetTriangle>();
        AddRectangle(triangles, 0d, 0d, 10d, 3d);
        AddRectangle(triangles, 0d, 7d, 10d, 10d);
        AddRectangle(triangles, 0d, 3d, 3d, 7d);
        AddRectangle(triangles, 7d, 3d, 10d, 7d);

        PlateauTilesetFeature feature = new PlateauTilesetFeature("bldg-courtyard", new Dictionary<string, object?>(), triangles);
        PlateauOnlineFootprintBuilder builder = new PlateauOnlineFootprintBuilder(simplifyToleranceMeters: 0d, minimumTriangleAreaSquareMeters: 0.001d);

        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = builder.Build(Model(feature), new List<string>());

        Assert.Equal(2, outlines.Count);
        Assert.Contains(outlines, outline => outline.SourceId == "bldg-courtyard");
        Assert.Contains(outlines, outline => outline.SourceId == "bldg-courtyard-courtyard-1");
        Assert.All(outlines, outline => Assert.Equal("PLATEAU_BUILDINGS", outline.Layer));
    }

    private static PlateauTilesetModel Model(PlateauTilesetFeature feature)
    {
        return new PlateauTilesetModel(
            "https://example.test/tileset.json",
            "bldg",
            "2",
            texture: false,
            areaCode: "13101",
            features: new[] { feature });
    }

    private static void AddRectangle(
        ICollection<PlateauTilesetTriangle> triangles,
        double minX,
        double minY,
        double maxX,
        double maxY)
    {
        triangles.Add(Tri((minX, minY, 10d), (maxX, minY, 10d), (maxX, maxY, 10d)));
        triangles.Add(Tri((minX, minY, 10d), (maxX, maxY, 10d), (minX, maxY, 10d)));
    }

    private static PlateauTilesetTriangle Tri((double X, double Y, double Z) a, (double X, double Y, double Z) b, (double X, double Y, double Z) c)
    {
        return new PlateauTilesetTriangle(
            new Vector3d(a.X, a.Y, a.Z),
            new Vector3d(b.X, b.Y, b.Z),
            new Vector3d(c.X, c.Y, c.Z));
    }
}
