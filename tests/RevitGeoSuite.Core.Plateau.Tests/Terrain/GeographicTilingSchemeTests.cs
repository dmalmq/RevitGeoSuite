using RevitGeoSuite.Core.Plateau.Terrain;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Terrain;

public sealed class GeographicTilingSchemeTests
{
    [Fact]
    public void Level0_has_two_columns_and_one_row_covering_the_globe()
    {
        Assert.Equal(2, GeographicTilingScheme.TilesXAtLevel(0));
        Assert.Equal(1, GeographicTilingScheme.TilesYAtLevel(0));

        GeoTileRectangle west = GeographicTilingScheme.TileRectangle(0, 0, 0);
        Assert.Equal(-180d, west.WestDegrees, 9);
        Assert.Equal(0d, west.EastDegrees, 9);
        Assert.Equal(-90d, west.SouthDegrees, 9);
        Assert.Equal(90d, west.NorthDegrees, 9);

        GeoTileRectangle east = GeographicTilingScheme.TileRectangle(0, 1, 0);
        Assert.Equal(0d, east.WestDegrees, 9);
        Assert.Equal(180d, east.EastDegrees, 9);
    }

    [Fact]
    public void TilesDoubleEachLevel()
    {
        Assert.Equal(4, GeographicTilingScheme.TilesXAtLevel(1));
        Assert.Equal(2, GeographicTilingScheme.TilesYAtLevel(1));
        Assert.Equal(65536, GeographicTilingScheme.TilesXAtLevel(15));
        Assert.Equal(32768, GeographicTilingScheme.TilesYAtLevel(15));
    }

    [Fact]
    public void ToLonLat_interpolates_within_the_tile_rectangle()
    {
        GeoTileRectangle rect = new GeoTileRectangle(139d, 35d, 140d, 36d);

        (double lon, double lat) = rect.ToLonLat(0.5d, 0.25d);

        Assert.Equal(139.5d, lon, 9);
        Assert.Equal(35.25d, lat, 9);
    }

    [Fact]
    public void TileRange_covers_a_tokyo_bounding_box()
    {
        // A small box around Tokyo at a high level should resolve to a handful of adjacent tiles.
        TerrainTileRange range = GeographicTilingScheme.TileRange(14, 139.74d, 35.65d, 139.78d, 35.69d);

        Assert.True(range.XStart <= range.XEnd);
        Assert.True(range.YStart <= range.YEnd);
        Assert.True(range.TileCount >= 1);
        // ~0.04° box / ~0.011° tiles ≈ 4×5 tiles; just assert it stays a small handful.
        Assert.True(range.TileCount <= 36);

        // Tokyo is east of the prime meridian and north of the equator → second-half X, upper-half Y.
        Assert.True(range.XStart >= GeographicTilingScheme.TilesXAtLevel(14) / 2);
        Assert.True(range.YStart >= GeographicTilingScheme.TilesYAtLevel(14) / 2);
    }
}
