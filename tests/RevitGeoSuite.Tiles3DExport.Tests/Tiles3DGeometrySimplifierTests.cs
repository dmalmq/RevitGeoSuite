using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DGeometrySimplifierTests
{
    [Fact]
    public void Medium_lod_reduces_triangle_count_for_large_meshes()
    {
        Tiles3DMeshPrimitive mesh = new Tiles3DMeshPrimitive
        {
            Name = "Test",
            Triangles = Enumerable.Range(0, 24)
                .Select(index => new Tiles3DTriangle(
                    new Tiles3DPoint(index, 0d, 0d),
                    new Tiles3DPoint(index, 1d, 0d),
                    new Tiles3DPoint(index, 0d, 1d)))
                .ToList()
        };

        Tiles3DGeometrySimplifier simplifier = new Tiles3DGeometrySimplifier();
        Tiles3DMeshPrimitive result = Assert.Single(simplifier.Simplify(new[] { mesh }, Tiles3DLevelOfDetail.Medium));

        Assert.Equal(12, result.Triangles.Count);
    }

    [Fact]
    public void Small_meshes_are_preserved_even_in_coarse_lod()
    {
        Tiles3DMeshPrimitive mesh = new Tiles3DMeshPrimitive
        {
            Name = "Small",
            Triangles = Enumerable.Range(0, 8)
                .Select(index => new Tiles3DTriangle(
                    new Tiles3DPoint(index, 0d, 0d),
                    new Tiles3DPoint(index, 1d, 0d),
                    new Tiles3DPoint(index, 0d, 1d)))
                .ToList()
        };

        Tiles3DGeometrySimplifier simplifier = new Tiles3DGeometrySimplifier();
        Tiles3DMeshPrimitive result = Assert.Single(simplifier.Simplify(new[] { mesh }, Tiles3DLevelOfDetail.Coarse));

        Assert.Equal(8, result.Triangles.Count);
    }

    [Fact]
    public void Simplified_meshes_preserve_level_and_material_metadata()
    {
        Tiles3DMaterialColor materialColor = new Tiles3DMaterialColor(10, 20, 30, 255);
        Tiles3DMeshPrimitive mesh = new Tiles3DMeshPrimitive
        {
            Name = "Test",
            CategoryName = "Walls",
            LevelName = "Ground Floor",
            LevelElevationMeters = 1.25d,
            Metadata =
            {
                TypeName = "Basic Wall",
                HeightMeters = 3.2d
            },
            Triangles = Enumerable.Range(0, 24)
                .Select(index => new Tiles3DTriangle(
                    new Tiles3DPoint(index, 0d, 0d),
                    new Tiles3DPoint(index, 1d, 0d),
                    new Tiles3DPoint(index, 0d, 1d),
                    materialColor))
                .ToList()
        };

        Tiles3DGeometrySimplifier simplifier = new Tiles3DGeometrySimplifier();
        Tiles3DMeshPrimitive result = Assert.Single(simplifier.Simplify(new[] { mesh }, Tiles3DLevelOfDetail.Medium));

        Assert.Equal("Ground Floor", result.LevelName);
        Assert.Equal(1.25d, result.LevelElevationMeters);
        Assert.Equal("Basic Wall", result.Metadata.TypeName);
        Assert.Equal(3.2d, result.Metadata.HeightMeters);
        Assert.Equal(materialColor, result.Triangles[0].MaterialColor!.Value);
    }

    [Theory]
    [InlineData(Tiles3DLevelOfDetail.Medium, 256)]
    [InlineData(Tiles3DLevelOfDetail.Coarse, 128)]
    public void Simplified_plate_meets_its_budget_keeps_corners_and_has_no_holes(Tiles3DLevelOfDetail levelOfDetail, int budget)
    {
        const int cells = 16;
        List<Tiles3DTriangle> triangles = new List<Tiles3DTriangle>();
        for (int x = 0; x < cells; x++)
        {
            for (int y = 0; y < cells; y++)
            {
                Tiles3DPoint a = new Tiles3DPoint(x, y, 0d);
                Tiles3DPoint b = new Tiles3DPoint(x + 1, y, 0d);
                Tiles3DPoint c = new Tiles3DPoint(x + 1, y + 1, 0d);
                Tiles3DPoint d = new Tiles3DPoint(x, y + 1, 0d);
                triangles.Add(new Tiles3DTriangle(a, b, c));
                triangles.Add(new Tiles3DTriangle(a, c, d));
            }
        }

        Tiles3DMeshPrimitive mesh = new Tiles3DMeshPrimitive { Name = "Plate", Triangles = triangles };
        Tiles3DMeshPrimitive result = Assert.Single(new Tiles3DGeometrySimplifier().Simplify(new[] { mesh }, levelOfDetail));

        Assert.InRange(result.Triangles.Count, 1, budget);
        Tiles3DPoint[] vertices = result.Triangles.SelectMany(t => new[] { t.A, t.B, t.C }).ToArray();
        foreach ((double cornerX, double cornerY) in new[] { (0d, 0d), (16d, 0d), (0d, 16d), (16d, 16d) })
        {
            Assert.Contains(vertices, v => v.X == cornerX && v.Y == cornerY);
        }

        for (double x = 0.25d; x < cells; x += 0.5d)
        {
            for (double y = 0.25d; y < cells; y += 0.5d)
            {
                double sampleX = x;
                double sampleY = y;
                Assert.Contains(result.Triangles, t => Covers(t, sampleX, sampleY));
            }
        }
    }

    [Fact]
    public void Simplifying_twice_gives_identical_output()
    {
        Tiles3DMeshPrimitive mesh = new Tiles3DMeshPrimitive
        {
            Name = "Test",
            Triangles = Enumerable.Range(0, 24)
                .Select(index => new Tiles3DTriangle(
                    new Tiles3DPoint(index, 0d, 0d),
                    new Tiles3DPoint(index, 1d, 0d),
                    new Tiles3DPoint(index, 0d, 1d)))
                .ToList()
        };

        Tiles3DGeometrySimplifier simplifier = new Tiles3DGeometrySimplifier();
        Tiles3DMeshPrimitive first = Assert.Single(simplifier.Simplify(new[] { mesh }, Tiles3DLevelOfDetail.Coarse));
        Tiles3DMeshPrimitive second = Assert.Single(simplifier.Simplify(new[] { mesh }, Tiles3DLevelOfDetail.Coarse));

        Assert.Equal(first.Triangles, second.Triangles);
    }

    [Fact]
    public void Geometric_error_grows_as_detail_drops()
    {
        double fine = Tiles3DGeometrySimplifier.GetGeometricError(100d, Tiles3DLevelOfDetail.Fine);
        double medium = Tiles3DGeometrySimplifier.GetGeometricError(100d, Tiles3DLevelOfDetail.Medium);
        double coarse = Tiles3DGeometrySimplifier.GetGeometricError(100d, Tiles3DLevelOfDetail.Coarse);

        Assert.Equal(25d, fine);
        Assert.Equal(50d, medium);
        Assert.Equal(100d, coarse);
    }

    private static bool Covers(Tiles3DTriangle triangle, double x, double y)
    {
        double Edge(Tiles3DPoint from, Tiles3DPoint to) =>
            ((to.X - from.X) * (y - from.Y)) - ((to.Y - from.Y) * (x - from.X));

        double ab = Edge(triangle.A, triangle.B);
        double bc = Edge(triangle.B, triangle.C);
        double ca = Edge(triangle.C, triangle.A);
        return (ab >= 0d && bc >= 0d && ca >= 0d) || (ab <= 0d && bc <= 0d && ca <= 0d);
    }
}
