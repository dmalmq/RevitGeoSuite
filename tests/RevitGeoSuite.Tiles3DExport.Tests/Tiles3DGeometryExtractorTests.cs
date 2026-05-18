using System.Collections.Generic;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DGeometryExtractorTests
{
    [Fact]
    public void ResolveVerticalExtents_empty_triangles_leaves_defaults()
    {
        Tiles3DObjectMetadata metadata = new Tiles3DObjectMetadata();
        List<Tiles3DTriangle> triangles = new List<Tiles3DTriangle>();

        Tiles3DGeometryExtractor.ResolveVerticalExtents(triangles, metadata);

        Assert.Equal(0d, metadata.MinZMeters);
        Assert.Equal(0d, metadata.MaxZMeters);
        Assert.Equal(0d, metadata.HeightMeters);
    }

    [Fact]
    public void ResolveVerticalExtents_single_triangle_sets_min_max_and_height()
    {
        Tiles3DObjectMetadata metadata = new Tiles3DObjectMetadata();
        List<Tiles3DTriangle> triangles = new List<Tiles3DTriangle>
        {
            new Tiles3DTriangle(
                new Tiles3DPoint(0d, 0d, 1d),
                new Tiles3DPoint(1d, 0d, 3d),
                new Tiles3DPoint(0d, 1d, 2d))
        };

        Tiles3DGeometryExtractor.ResolveVerticalExtents(triangles, metadata);

        Assert.Equal(1d, metadata.MinZMeters);
        Assert.Equal(3d, metadata.MaxZMeters);
        Assert.Equal(2d, metadata.HeightMeters);
    }

    [Fact]
    public void ResolveVerticalExtents_multiple_triangles_finds_global_extents()
    {
        Tiles3DObjectMetadata metadata = new Tiles3DObjectMetadata();
        List<Tiles3DTriangle> triangles = new List<Tiles3DTriangle>
        {
            new Tiles3DTriangle(
                new Tiles3DPoint(0d, 0d, 0d),
                new Tiles3DPoint(1d, 0d, 0d),
                new Tiles3DPoint(0d, 1d, 0d)),
            new Tiles3DTriangle(
                new Tiles3DPoint(0d, 0d, 5d),
                new Tiles3DPoint(1d, 0d, 5d),
                new Tiles3DPoint(0d, 1d, 5d))
        };

        Tiles3DGeometryExtractor.ResolveVerticalExtents(triangles, metadata);

        Assert.Equal(0d, metadata.MinZMeters);
        Assert.Equal(5d, metadata.MaxZMeters);
        Assert.Equal(5d, metadata.HeightMeters);
    }

    [Fact]
    public void ResolveVerticalExtents_negative_z_values_work()
    {
        Tiles3DObjectMetadata metadata = new Tiles3DObjectMetadata();
        List<Tiles3DTriangle> triangles = new List<Tiles3DTriangle>
        {
            new Tiles3DTriangle(
                new Tiles3DPoint(0d, 0d, -2d),
                new Tiles3DPoint(1d, 0d, -1d),
                new Tiles3DPoint(0d, 1d, -3d))
        };

        Tiles3DGeometryExtractor.ResolveVerticalExtents(triangles, metadata);

        Assert.Equal(-3d, metadata.MinZMeters);
        Assert.Equal(-1d, metadata.MaxZMeters);
        Assert.Equal(2d, metadata.HeightMeters);
    }
}
