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
}
