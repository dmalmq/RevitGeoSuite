using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauPolygonTriangulatorTests
{
    [Fact]
    public void TryTriangulate_returns_a_single_triangle_for_three_points()
    {
        bool triangulated = PlateauPolygonTriangulator.TryTriangulate(
            new[]
            {
                new ContextShapePoint3D(0, 0, 0),
                new ContextShapePoint3D(10, 0, 0),
                new ContextShapePoint3D(0, 10, 0)
            },
            out IReadOnlyCollection<ContextShapeTriangle> triangles);

        Assert.True(triangulated);
        Assert.Single(triangles);
    }

    [Fact]
    public void TryTriangulate_splits_a_convex_quad_into_two_triangles()
    {
        bool triangulated = PlateauPolygonTriangulator.TryTriangulate(
            new[]
            {
                new ContextShapePoint3D(0, 0, 0),
                new ContextShapePoint3D(10, 0, 0),
                new ContextShapePoint3D(10, 10, 0),
                new ContextShapePoint3D(0, 10, 0)
            },
            out IReadOnlyCollection<ContextShapeTriangle> triangles);

        Assert.True(triangulated);
        Assert.Equal(2, triangles.Count);
        Assert.Contains(triangles, triangle => triangle.A.XFeet == 0 && triangle.B.XFeet == 10 && triangle.C.XFeet == 10);
    }

    [Fact]
    public void TryTriangulate_uses_the_ear_clipping_fallback_for_concave_polygons()
    {
        bool triangulated = PlateauPolygonTriangulator.TryTriangulate(
            new[]
            {
                new ContextShapePoint3D(0, 0, 0),
                new ContextShapePoint3D(10, 0, 0),
                new ContextShapePoint3D(10, 4, 0),
                new ContextShapePoint3D(5, 2, 0),
                new ContextShapePoint3D(0, 10, 0)
            },
            out IReadOnlyCollection<ContextShapeTriangle> triangles);

        Assert.True(triangulated);
        Assert.Equal(3, triangles.Count);
        Assert.Equal(5, triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C }).Distinct().Count());
    }
}

