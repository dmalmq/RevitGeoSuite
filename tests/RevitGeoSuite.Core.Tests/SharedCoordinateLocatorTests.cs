using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class SharedCoordinateLocatorTests
{
    private const double TokyoStationLatitude = 35.681236;
    private const double TokyoStationLongitude = 139.767125;

    private static readonly CrsReference ZoneIx = new CrsReference
    {
        EpsgCode = 6677,
        NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX"
    };

    [Fact]
    public void Absolute_shared_coordinates_resolve_to_the_site_without_an_origin_offset()
    {
        CrsRegistry registry = new CrsRegistry();
        ProjectedCoordinate tokyo = new CoordinateTransformer(registry).Project(
            new GeographicCoordinate(TokyoStationLatitude, TokyoStationLongitude),
            ZoneIx);

        GeographicCoordinate? located = new SharedCoordinateLocator(registry).Locate(ZoneIx, tokyo.Easting, tokyo.Northing);

        Assert.True(located.HasValue);
        Assert.Equal(TokyoStationLatitude, located!.Value.Latitude, 6);
        Assert.Equal(TokyoStationLongitude, located.Value.Longitude, 6);
    }

    [Fact]
    public void Repeated_reads_of_the_same_position_do_not_drift()
    {
        CrsRegistry registry = new CrsRegistry();
        SharedCoordinateLocator locator = new SharedCoordinateLocator(registry);
        ProjectedCoordinate tokyo = new CoordinateTransformer(registry).Project(
            new GeographicCoordinate(TokyoStationLatitude, TokyoStationLongitude),
            ZoneIx);

        GeographicCoordinate? first = locator.Locate(ZoneIx, tokyo.Easting, tokyo.Northing);
        GeographicCoordinate? second = locator.Locate(ZoneIx, tokyo.Easting, tokyo.Northing);

        Assert.Equal(first!.Value.Latitude, second!.Value.Latitude);
        Assert.Equal(first.Value.Longitude, second.Value.Longitude);
    }

    [Fact]
    public void Missing_crs_reports_an_unknown_location()
    {
        SharedCoordinateLocator locator = new SharedCoordinateLocator(new CrsRegistry());

        Assert.Null(locator.Locate(null, -5992.92, -35363.238));
    }

    [Fact]
    public void Removed_crs_reports_an_unknown_location_instead_of_throwing()
    {
        SharedCoordinateLocator locator = new SharedCoordinateLocator(new CrsRegistry());
        CrsReference britishNationalGrid = new CrsReference
        {
            EpsgCode = 27700,
            NameSnapshot = "OSGB 1936 / British National Grid"
        };

        Assert.Null(locator.Locate(britishNationalGrid, 530000, 180000));
    }

    [Theory]
    [InlineData(double.NaN, 0d)]
    [InlineData(0d, double.PositiveInfinity)]
    public void Non_finite_positions_report_an_unknown_location(double easting, double northing)
    {
        SharedCoordinateLocator locator = new SharedCoordinateLocator(new CrsRegistry());

        Assert.Null(locator.Locate(ZoneIx, easting, northing));
    }
}
