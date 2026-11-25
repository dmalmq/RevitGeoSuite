using System;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

public sealed class Tiles3DMaterialPaletteResolverTests
{
    private static readonly Tiles3DMaterialColor Concrete = new Tiles3DMaterialColor(120, 110, 100, 255);

    [Theory]
    [InlineData("GIS_free_area")]
    [InlineData("GIS_stairs_escalators")]
    [InlineData("K3D_handrail")]
    [InlineData("M3D_signage")]
    [InlineData("Concrete")]
    [InlineData("Glass")]
    [InlineData("")]
    [InlineData(null)]
    public void Source_passes_every_colour_through_untouched(string? materialName)
    {
        Tiles3DMaterialColor opaque = new Tiles3DMaterialColor(12, 34, 56, 255);
        Tiles3DMaterialColor glass = new Tiles3DMaterialColor(150, 200, 230, 77);

        Assert.Equal(opaque, Tiles3DMaterialPaletteResolver.Resolve(Tiles3DMaterialPalette.Source, materialName, opaque));
        Assert.Equal(glass, Tiles3DMaterialPaletteResolver.Resolve(Tiles3DMaterialPalette.Source, materialName, glass));
        Assert.Equal(
            Tiles3DMaterialColor.Default,
            Tiles3DMaterialPaletteResolver.Resolve(Tiles3DMaterialPalette.Source, materialName, Tiles3DMaterialColor.Default));
    }

    // Every colour is copied from kanizu-3d's massing/palette.rs.
    [Theory]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft, "GIS_free_area", 0xFB, 0xF9, 0xF4)]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft, "GIS_paid_area", 0xE6, 0xF0, 0xF7)]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft, "GIS_paid_area_shinkansen", 0xDC, 0xE8, 0xF4)]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft, "GIS_facilities", 0xD8, 0xEA, 0xE8)]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft, "GIS_restricted", 0xEC, 0xEB, 0xE6)]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft, "GIS_stairs_escalators", 0xFF, 0xFF, 0xFF)]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft, "GIS_unlisted", 0xEA, 0xEC, 0xE9)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite, "GIS_free_area", 0xF6, 0xF5, 0xF1)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite, "GIS_paid_area", 0xF6, 0xF5, 0xF1)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite, "GIS_paid_area_shinkansen", 0xF2, 0xF2, 0xEE)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite, "GIS_facilities", 0xF6, 0xF5, 0xF1)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite, "GIS_restricted", 0xF0, 0xEF, 0xEB)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite, "GIS_stairs_escalators", 0xFF, 0xFF, 0xFF)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite, "GIS_unlisted", 0xF0, 0xEF, 0xEB)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull, "GIS_free_area", 0xFF, 0xFF, 0xFF)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull, "GIS_paid_area", 0xF2, 0xF7, 0xFB)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull, "GIS_paid_area_shinkansen", 0xE5, 0xEF, 0xF7)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull, "GIS_facilities", 0xDD, 0xEB, 0xEC)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull, "GIS_restricted", 0xF2, 0xF2, 0xF2)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull, "GIS_stairs_escalators", 0xFF, 0xFF, 0xFF)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull, "GIS_unlisted", 0xD9, 0xD9, 0xD9)]
    public void Kanizu_palettes_remap_each_gis_area_material(Tiles3DMaterialPalette palette, string materialName, int red, int green, int blue)
    {
        Tiles3DMaterialColor expected = new Tiles3DMaterialColor((byte)red, (byte)green, (byte)blue, 255);

        Assert.Equal(expected, Tiles3DMaterialPaletteResolver.Resolve(palette, materialName, Concrete));
        // Matching is case-insensitive and ignores surrounding blanks, like the rest of the standard.
        Assert.Equal(expected, Tiles3DMaterialPaletteResolver.Resolve(palette, "  " + materialName.ToUpperInvariant() + " ", Concrete));
    }

    [Theory]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull)]
    public void Kanizu_palettes_keep_k3d_and_m3d_materials_as_authored(Tiles3DMaterialPalette palette)
    {
        Tiles3DMaterialColor authored = new Tiles3DMaterialColor(200, 40, 30, 255);
        Tiles3DMaterialColor authoredTranslucent = new Tiles3DMaterialColor(10, 20, 30, 90);

        Assert.Equal(authored, Tiles3DMaterialPaletteResolver.Resolve(palette, "K3D_escalator_handrail", authored));
        Assert.Equal(authored, Tiles3DMaterialPaletteResolver.Resolve(palette, "M3D_ticket_gate", authored));
        Assert.Equal(authored, Tiles3DMaterialPaletteResolver.Resolve(palette, "k3d_lowercase", authored));
        Assert.Equal(authoredTranslucent, Tiles3DMaterialPaletteResolver.Resolve(palette, "M3D_screen", authoredTranslucent));
    }

    [Theory]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull)]
    public void Kanizu_palettes_keep_glass_transparency_and_tint(Tiles3DMaterialPalette palette)
    {
        Tiles3DMaterialColor glass = new Tiles3DMaterialColor(150, 200, 230, 77);

        Assert.Equal(glass, Tiles3DMaterialPaletteResolver.Resolve(palette, "Glass", glass));
        Assert.Equal(glass, Tiles3DMaterialPaletteResolver.Resolve(palette, null, glass));
    }

    [Theory]
    [InlineData(Tiles3DMaterialPalette.KanizuSoft, 0xFF, 0xFF, 0xFF)]
    [InlineData(Tiles3DMaterialPalette.KanizuWhite, 0xFF, 0xFF, 0xFF)]
    [InlineData(Tiles3DMaterialPalette.KanizuFull, 0xFD, 0xFD, 0xFC)]
    public void Kanizu_palettes_map_other_opaque_materials_to_the_matte_wall_colour(Tiles3DMaterialPalette palette, int red, int green, int blue)
    {
        Tiles3DMaterialColor wall = new Tiles3DMaterialColor((byte)red, (byte)green, (byte)blue, 255);

        Assert.Equal(wall, Tiles3DMaterialPaletteResolver.GetWallColor(palette));
        Assert.Equal(wall, Tiles3DMaterialPaletteResolver.Resolve(palette, "Concrete", Concrete));
        Assert.Equal(wall, Tiles3DMaterialPaletteResolver.Resolve(palette, "Paint - Red", new Tiles3DMaterialColor(255, 0, 0, 255)));
        Assert.Equal(wall, Tiles3DMaterialPaletteResolver.Resolve(palette, null, Tiles3DMaterialColor.Default));
        // GIS-looking names that are not GIS_ materials are ordinary materials.
        Assert.Equal(wall, Tiles3DMaterialPaletteResolver.Resolve(palette, "XGIS_free_area", Concrete));
    }

    [Theory]
    [InlineData(null, Tiles3DMaterialPalette.Source)]
    [InlineData("", Tiles3DMaterialPalette.Source)]
    [InlineData("source", Tiles3DMaterialPalette.Source)]
    [InlineData("kanizuSoft", Tiles3DMaterialPalette.KanizuSoft)]
    [InlineData("kanizuWhite", Tiles3DMaterialPalette.KanizuWhite)]
    [InlineData("KanizuFull", Tiles3DMaterialPalette.KanizuFull)]
    [InlineData(" soft ", Tiles3DMaterialPalette.KanizuSoft)]
    public void Parse_reads_palette_names(string? text, Tiles3DMaterialPalette expected)
    {
        Assert.Equal(expected, Tiles3DMaterialPaletteResolver.Parse(text));
    }

    [Fact]
    public void Parse_rejects_unknown_palettes()
    {
        Assert.False(Tiles3DMaterialPaletteResolver.TryParse("neon", out Tiles3DMaterialPalette palette));
        Assert.Equal(Tiles3DMaterialPalette.Source, palette);
        Assert.Throws<InvalidOperationException>(() => Tiles3DMaterialPaletteResolver.Parse("neon"));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 1)]
    [InlineData(128, 55)]
    [InlineData(0xD9, 177)]
    [InlineData(0xE6, 202)]
    [InlineData(0xFB, 246)]
    [InlineData(0xFD, 250)]
    [InlineData(255, 255)]
    public void Linear_channels_are_quantized_like_kanizu_3d(int srgb, int expectedLinear)
    {
        Assert.Equal((byte)expectedLinear, Tiles3DMaterialColor.SrgbByteToLinearByte((byte)srgb));
    }

    [Fact]
    public void Linear_base_colour_factor_keeps_alpha_linear()
    {
        double[] factor = new Tiles3DMaterialColor(0xFB, 0xFF, 0, 128).ToLinearBaseColorFactor();

        Assert.Equal(new[] { 246d / 255d, 1d, 0d, 128d / 255d }, factor);
    }
}
