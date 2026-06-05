using RevitGeoSuite.Core.Plateau.Mvt;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Mvt;

public sealed class WebMercatorTileMathTests
{
    [Fact]
    public void LonLatToTile_matches_known_sapporo_tile_at_z16()
    {
        // Sapporo (≈141.35, 43.06) at zoom 16 → tile 58499/24066 (verified against the live tile server).
        (int x, int y) = WebMercatorTileMath.LonLatToTile(141.35, 43.06, 16);

        Assert.Equal(58499, x);
        Assert.Equal(24066, y);
    }

    [Fact]
    public void TileLocalToLonLat_center_maps_back_to_same_tile()
    {
        const int zoom = 16;
        const int tileX = 58499;
        const int tileY = 24066;
        const double extent = 4096;

        (double lon, double lat) = WebMercatorTileMath.TileLocalToLonLat(zoom, tileX, tileY, extent / 2, extent / 2, extent);

        (int x, int y) = WebMercatorTileMath.LonLatToTile(lon, lat, zoom);
        Assert.Equal(tileX, x);
        Assert.Equal(tileY, y);
    }

    [Fact]
    public void TileLocalToLonLat_origin_is_north_west_corner()
    {
        const int zoom = 16;
        const int tileX = 58499;
        const int tileY = 24066;
        const double extent = 4096;

        (double nwLon, double nwLat) = WebMercatorTileMath.TileLocalToLonLat(zoom, tileX, tileY, 0, 0, extent);
        (double seLon, double seLat) = WebMercatorTileMath.TileLocalToLonLat(zoom, tileX, tileY, extent, extent, extent);

        // Local (0,0) is the tile's north-west corner: smaller longitude, larger latitude than (extent,extent).
        Assert.True(nwLon < seLon);
        Assert.True(nwLat > seLat);
    }
}
