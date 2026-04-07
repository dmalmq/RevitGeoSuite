using RevitGeoSuite.RevitInterop.GeoPlacement;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class ProjectBasePointMoveMathTests
{
    [Fact]
    public void TrySolvePlanOffset_returns_identity_offsets_for_identity_basis()
    {
        bool success = ProjectBasePointMoveMath.TrySolvePlanOffset(
            10d,
            20d,
            1d,
            0d,
            0d,
            1d,
            out double deltaXFeet,
            out double deltaYFeet);

        Assert.True(success);
        Assert.Equal(10d, deltaXFeet, 6);
        Assert.Equal(20d, deltaYFeet, 6);
    }

    [Fact]
    public void TrySolvePlanOffset_handles_rotated_basis()
    {
        bool success = ProjectBasePointMoveMath.TrySolvePlanOffset(
            4d,
            3d,
            0d,
            1d,
            -1d,
            0d,
            out double deltaXFeet,
            out double deltaYFeet);

        Assert.True(success);
        Assert.Equal(3d, deltaXFeet, 6);
        Assert.Equal(-4d, deltaYFeet, 6);
    }

    [Fact]
    public void TrySolvePlanOffset_returns_false_for_singular_basis()
    {
        bool success = ProjectBasePointMoveMath.TrySolvePlanOffset(
            5d,
            8d,
            1d,
            2d,
            2d,
            4d,
            out double deltaXFeet,
            out double deltaYFeet);

        Assert.False(success);
        Assert.Equal(0d, deltaXFeet, 6);
        Assert.Equal(0d, deltaYFeet, 6);
    }

    [Fact]
    public void CalculatePlanDistance_returns_euclidean_distance()
    {
        double distance = ProjectBasePointMoveMath.CalculatePlanDistance(3d, 4d);

        Assert.Equal(5d, distance, 6);
    }
}
