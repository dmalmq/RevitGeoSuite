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
}
