using System;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class GeospatialTransformBuilderTests
{
    [Fact]
    public void East_north_up_transform_uses_expected_origin_at_equator()
    {
        GeospatialTransformBuilder builder = new GeospatialTransformBuilder();
        double[] transform = builder.BuildEastNorthUpTransform(0d, 0d, 0d);

        Assert.Equal(16, transform.Length);
        Assert.Equal(0d, transform[0], 6);
        Assert.Equal(1d, transform[1], 6);
        Assert.Equal(0d, transform[4], 6);
        Assert.Equal(0d, transform[5], 6);
        Assert.Equal(1d, transform[6], 6);
        Assert.Equal(6378137d, transform[12], 3);
        Assert.Equal(0d, transform[13], 6);
        Assert.Equal(0d, transform[14], 6);
    }

    [Fact]
    public void East_north_up_transform_moves_origin_along_up_by_height_offset()
    {
        GeospatialTransformBuilder builder = new GeospatialTransformBuilder();
        double[] baseline = builder.BuildEastNorthUpTransform(35.681236d, 139.767125d, 12.25d);
        double[] corrected = builder.BuildEastNorthUpTransform(35.681236d, 139.767125d, 49.75d);

        double dx = corrected[12] - baseline[12];
        double dy = corrected[13] - baseline[13];
        double dz = corrected[14] - baseline[14];
        double upProjection = (dx * baseline[8]) + (dy * baseline[9]) + (dz * baseline[10]);
        double distance = Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));

        Assert.InRange(Math.Abs(upProjection - 37.5d), 0d, 0.000001d);
        Assert.InRange(Math.Abs(distance - 37.5d), 0d, 0.000001d);
    }
}
