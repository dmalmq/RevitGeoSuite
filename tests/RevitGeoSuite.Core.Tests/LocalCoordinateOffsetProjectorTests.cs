using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class LocalCoordinateOffsetProjectorTests
{
    [Fact]
    public void Offset_returns_same_coordinate_when_offsets_are_zero()
    {
        GeographicCoordinate origin = new GeographicCoordinate(35.681236, 139.767125);

        GeographicCoordinate result = LocalCoordinateOffsetProjector.Offset(origin, 0d, 0d);

        Assert.Equal(origin.Latitude, result.Latitude, 6);
        Assert.Equal(origin.Longitude, result.Longitude, 6);
    }

    [Fact]
    public void Offset_moves_coordinate_east_and_north_by_expected_small_amounts()
    {
        GeographicCoordinate origin = new GeographicCoordinate(35d, 139d);

        GeographicCoordinate result = LocalCoordinateOffsetProjector.Offset(origin, 100d, 100d);

        Assert.Equal(35.000898, result.Latitude, 6);
        Assert.Equal(139.001097, result.Longitude, 6);
    }
}
