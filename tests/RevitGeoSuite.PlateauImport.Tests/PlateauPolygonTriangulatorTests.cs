using System;
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

    [Fact]
    public void TryTriangulate_preserves_interior_holes()
    {
        bool triangulated = PlateauPolygonTriangulator.TryTriangulate(
            new[]
            {
                new ContextShapePoint3D(0, 0, 0),
                new ContextShapePoint3D(10, 0, 0),
                new ContextShapePoint3D(10, 10, 0),
                new ContextShapePoint3D(0, 10, 0)
            },
            new[]
            {
                new[]
                {
                    new ContextShapePoint3D(3, 3, 0),
                    new ContextShapePoint3D(7, 3, 0),
                    new ContextShapePoint3D(7, 7, 0),
                    new ContextShapePoint3D(3, 7, 0)
                }
            },
            out IReadOnlyCollection<ContextShapeTriangle> triangles);

        Assert.True(triangulated);
        Assert.True(triangles.Count > 0);
        Assert.Equal(84d, triangles.Sum(ComputeArea), precision: 6);
        Assert.DoesNotContain(triangles, triangle =>
        {
            ContextShapePoint3D centroid = ComputeCentroid(triangle);
            return centroid.XFeet > 3d && centroid.XFeet < 7d && centroid.YFeet > 3d && centroid.YFeet < 7d;
        });
    }

    private static double ComputeArea(ContextShapeTriangle triangle)
    {
        return Math.Abs(
            ((triangle.A.XFeet * (triangle.B.YFeet - triangle.C.YFeet)) +
             (triangle.B.XFeet * (triangle.C.YFeet - triangle.A.YFeet)) +
             (triangle.C.XFeet * (triangle.A.YFeet - triangle.B.YFeet))) * 0.5d);
    }

    private static ContextShapePoint3D ComputeCentroid(ContextShapeTriangle triangle)
    {
        return new ContextShapePoint3D(
            (triangle.A.XFeet + triangle.B.XFeet + triangle.C.XFeet) / 3d,
            (triangle.A.YFeet + triangle.B.YFeet + triangle.C.YFeet) / 3d,
            (triangle.A.ZFeet + triangle.B.ZFeet + triangle.C.ZFeet) / 3d);
    }
}

