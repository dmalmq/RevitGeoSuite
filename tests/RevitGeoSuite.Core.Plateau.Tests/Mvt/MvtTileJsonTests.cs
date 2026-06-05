using RevitGeoSuite.Core.Plateau.Mvt;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Mvt;

public sealed class MvtTileJsonTests
{
    // Trimmed copy of the live PLATEAU Sapporo road TileJSON.
    private const string SampleJson = @"{
        ""tilejson"": ""3.0.0"",
        ""name"": ""交通"",
        ""scheme"": ""xyz"",
        ""tiles"": [""https://assets.example/01100_tran_mvt_lod1/{z}/{x}/{y}.mvt""],
        ""minzoom"": 10,
        ""maxzoom"": 16,
        ""vector_layers"": [ { ""id"": ""Road"", ""fields"": {} } ]
    }";

    [Fact]
    public void Parse_reads_zoom_range_layers_and_template()
    {
        MvtTileJson tileJson = MvtTileJson.Parse(SampleJson);

        Assert.Equal(10, tileJson.MinZoom);
        Assert.Equal(16, tileJson.MaxZoom);
        Assert.Single(tileJson.TileTemplates);
        Assert.Equal(new[] { "Road" }, tileJson.VectorLayerIds);
    }

    [Fact]
    public void BuildTileUrl_substitutes_z_x_y()
    {
        MvtTileJson tileJson = MvtTileJson.Parse(SampleJson);

        string url = tileJson.BuildTileUrl(16, 58499, 24066);

        Assert.Equal("https://assets.example/01100_tran_mvt_lod1/16/58499/24066.mvt", url);
    }
}
