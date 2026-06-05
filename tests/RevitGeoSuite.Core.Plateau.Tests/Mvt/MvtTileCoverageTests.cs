using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Plateau.Mvt;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Mvt;

public sealed class MvtTileCoverageTests
{
    [Fact]
    public void TilesForBounds_tiny_box_returns_single_tile()
    {
        // Build a sub-metre box around the centre of a known z16 tile so it can't straddle an edge.
        (double lon, double lat) = WebMercatorTileMath.TileLocalToLonLat(16, 58499, 24066, 2048, 2048, 4096);
        const double eps = 1e-5;

        IReadOnlyList<MvtTileAddress> tiles = MvtTileCoverage.TilesForBounds(
            westDeg: lon - eps, southDeg: lat - eps, eastDeg: lon + eps, northDeg: lat + eps, zoom: 16);

        MvtTileAddress tile = Assert.Single(tiles);
        Assert.Equal(16, tile.Zoom);
        Assert.Equal(58499, tile.X);
        Assert.Equal(24066, tile.Y);
    }

    [Fact]
    public void TilesForBounds_covers_a_rectangular_block_of_tiles()
    {
        // Span a few tiles in each direction; the count is (maxX-minX+1)*(maxY-minY+1).
        IReadOnlyList<MvtTileAddress> tiles = MvtTileCoverage.TilesForBounds(
            westDeg: 141.30, southDeg: 43.02, eastDeg: 141.40, northDeg: 43.10, zoom: 16);

        Assert.True(tiles.Count > 1);
        // Distinct addresses, all at the requested zoom.
        Assert.Equal(tiles.Count, tiles.Select(t => (t.X, t.Y)).Distinct().Count());
        Assert.All(tiles, t => Assert.Equal(16, t.Zoom));
    }

    [Fact]
    public void TilesForBounds_respects_max_tile_cap()
    {
        IReadOnlyList<MvtTileAddress> tiles = MvtTileCoverage.TilesForBounds(
            westDeg: 141.0, southDeg: 43.0, eastDeg: 141.5, northDeg: 43.5, zoom: 16, maxTiles: 10);

        Assert.True(tiles.Count <= 10);
    }

    [Theory]
    [InlineData(16, 16, 16)]
    [InlineData(18, 16, 16)]
    [InlineData(12, 16, 12)]
    public void ResolveZoom_caps_at_zoom_cap(int datasetMax, int cap, int expected)
    {
        Assert.Equal(expected, MvtTileCoverage.ResolveZoom(datasetMax, cap));
    }

    [Fact]
    public void ResolveZoom_uses_cap_when_dataset_max_unknown()
    {
        Assert.Equal(15, MvtTileCoverage.ResolveZoom(null, 15));
    }
}
