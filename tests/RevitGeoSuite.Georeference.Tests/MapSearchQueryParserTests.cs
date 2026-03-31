using RevitGeoSuite.SharedUI.Controls;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class MapSearchQueryParserTests
{
    [Theory]
    [InlineData("35.681236,139.767125", 35.681236, 139.767125)]
    [InlineData("35.681236 139.767125", 35.681236, 139.767125)]
    [InlineData("35.681236; 139.767125", 35.681236, 139.767125)]
    public void TryParseCoordinatePair_accepts_common_lat_lon_formats(string query, double expectedLatitude, double expectedLongitude)
    {
        bool success = MapSearchQueryParser.TryParseCoordinatePair(query, out double latitude, out double longitude);

        Assert.True(success);
        Assert.Equal(expectedLatitude, latitude, 6);
        Assert.Equal(expectedLongitude, longitude, 6);
    }

    [Theory]
    [InlineData("Tokyo Station")]
    [InlineData("95,139")]
    [InlineData("35.68")]
    public void TryParseCoordinatePair_rejects_non_coordinate_queries(string query)
    {
        bool success = MapSearchQueryParser.TryParseCoordinatePair(query, out _, out _);

        Assert.False(success);
    }
}
