using System.Linq;
using RevitGeoSuite.Core.Plateau.Schema;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauSchemaHelperTests
{
    [Fact]
    public void SelectLeafTileIds_drops_secondary_parent_when_tertiary_children_present()
    {
        var leaves = PlateauSchemaHelper.SelectLeafTileIds(new[] { "533945", "53394574", "53394575" });

        Assert.Equal(new[] { "53394574", "53394575" }, leaves.OrderBy(id => id).ToArray());
    }

    [Fact]
    public void SelectLeafTileIds_keeps_lone_secondary_tile_when_no_child_present()
    {
        var leaves = PlateauSchemaHelper.SelectLeafTileIds(new[] { "533945" });

        Assert.Equal(new[] { "533945" }, leaves.ToArray());
    }

    [Fact]
    public void SelectLeafTileIds_keeps_all_unrelated_tertiary_tiles()
    {
        string[] tiles = { "53394574", "53394564", "53394655" };

        var leaves = PlateauSchemaHelper.SelectLeafTileIds(tiles);

        Assert.Equal(tiles.OrderBy(id => id).ToArray(), leaves.OrderBy(id => id).ToArray());
    }

    [Fact]
    public void SelectLeafTileIds_ignores_blanks_duplicates_and_whitespace()
    {
        var leaves = PlateauSchemaHelper.SelectLeafTileIds(new[] { "53394574", " 53394574 ", "", "  ", null! });

        Assert.Equal(new[] { "53394574" }, leaves.ToArray());
    }
}
