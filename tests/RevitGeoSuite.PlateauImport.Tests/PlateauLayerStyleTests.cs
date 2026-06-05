using RevitGeoSuite.PlateauImport;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public class PlateauLayerStyleTests
{
    [Theory]
    [InlineData("PLATEAU_ROADS", "205,205,205", 8)]      // grey
    [InlineData("PLATEAU_BUILDINGS", "232,235,235", 7)]  // near-white
    [InlineData("PLATEAU_VEGETATION", "150,200,150", 3)] // green
    [InlineData("PLATEAU_LANDUSE", "200,220,160", 3)]    // green
    [InlineData("GSI_WATER", "175,210,235", 5)]          // blue
    public void ForLayer_returns_fill_rgb_and_recognizable_aci(string layer, string expectedFill, int expectedAci)
    {
        PlateauLayerStyle style = PlateauLayerStyle.ForLayer(layer);

        Assert.Equal(expectedFill, style.FillRgb);
        Assert.Equal(expectedAci, style.Aci);
    }

    [Fact]
    public void TrueColor_packs_fill_rgb_as_rrggbb()
    {
        // Buildings: 232,235,235 -> 0xE8EBEB
        PlateauLayerStyle buildings = PlateauLayerStyle.ForLayer("PLATEAU_BUILDINGS");
        Assert.Equal((232 << 16) | (235 << 8) | 235, buildings.TrueColor);

        // Vegetation: 150,200,150 -> 0x96C896
        PlateauLayerStyle vegetation = PlateauLayerStyle.ForLayer("PLATEAU_VEGETATION");
        Assert.Equal((150 << 16) | (200 << 8) | 150, vegetation.TrueColor);
    }

    [Fact]
    public void Unknown_layer_falls_back_to_neutral_grey_style()
    {
        PlateauLayerStyle style = PlateauLayerStyle.ForLayer("SOMETHING_ELSE");

        Assert.Equal("OTHER", style.Type);
        Assert.Equal("220,220,220", style.FillRgb);
        Assert.Equal(9, style.Aci);
    }

    [Theory]
    [InlineData("PLATEAU_ROADS")]
    [InlineData("PLATEAU_BUILDINGS")]
    [InlineData("PLATEAU_BRIDGES")]
    [InlineData("PLATEAU_SIDEWALKS")]
    [InlineData("PLATEAU_VEGETATION")]
    [InlineData("PLATEAU_LANDUSE")]
    [InlineData("PLATEAU_RELIEF")]
    [InlineData("GSI_SIDEWALKS")]
    [InlineData("GSI_RAILWAYS")]
    [InlineData("GSI_WATER")]
    [InlineData("GSI_LANDUSE")]
    public void Every_known_layer_has_a_valid_aci(string layer)
    {
        int aci = PlateauLayerStyle.AciForLayer(layer);
        Assert.InRange(aci, 1, 255);
    }
}
