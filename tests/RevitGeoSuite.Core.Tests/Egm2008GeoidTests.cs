using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class Egm2008GeoidTests
{
    [Theory]
    [InlineData(0d, 0d, 17.2260d)]
    [InlineData(35.6895d, 139.6917d, 36.7117d)]
    [InlineData(10d, 78d, -95.9429d)]
    [InlineData(89.999d, 45d, 14.8991d)]
    public void GetUndulationMeters_matches_geographiclib_geoid_eval_reference_values(
        double latitude,
        double longitude,
        double expectedUndulationMeters)
    {
        double result = Egm2008Geoid.GetUndulationMeters(latitude, longitude);

        Assert.InRange(result, expectedUndulationMeters - 0.15d, expectedUndulationMeters + 0.15d);
    }

    [Fact]
    public void GetUndulationMeters_wraps_negative_longitude()
    {
        double positiveLongitude = Egm2008Geoid.GetUndulationMeters(35.6895d, 139.6917d);
        double negativeLongitude = Egm2008Geoid.GetUndulationMeters(35.6895d, -220.3083d);

        Assert.Equal(positiveLongitude, negativeLongitude, precision: 9);
    }

    [Fact]
    public void GetUndulationMeters_clamps_near_poles_without_throwing()
    {
        double north = Egm2008Geoid.GetUndulationMeters(90.5d, 45d);
        double south = Egm2008Geoid.GetUndulationMeters(-90.5d, 45d);

        Assert.False(double.IsNaN(north));
        Assert.False(double.IsInfinity(north));
        Assert.False(double.IsNaN(south));
        Assert.False(double.IsInfinity(south));
    }
}
