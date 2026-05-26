using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Plateau.Catalog;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Catalog;

public sealed class PlateauDatasetSelectorTests
{
    private static PlateauCatalog LoadSampleCatalog()
    {
        string path = TestPathHelper.GetFixturePath("tests", "Fixtures", "plateau-catalog-sample.json");
        string text = File.ReadAllText(path);
        PlateauCatalogResponse response = JsonConvert.DeserializeObject<PlateauCatalogResponse>(text)!;
        return PlateauCatalog.Normalize(response);
    }

    [Fact]
    public void SelectBest_prefers_textured_when_PreferTextured()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        PlateauAreaOption sapporo = catalog.AreaOptions.Single(a => a.Code == "01100");
        PlateauDatasetSelector selector = new() { TexturePreference = PlateauTexturePreference.PreferTextured };

        PlateauDatasetEntry? best = selector.SelectByTypes(catalog, sapporo, new[] { "bldg" }).Single();

        Assert.Equal("2", best.Lod);
        Assert.True(best.Texture);
    }

    [Fact]
    public void SelectBest_prefers_untextured_when_PreferUntextured()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        PlateauAreaOption sapporo = catalog.AreaOptions.Single(a => a.Code == "01100");
        PlateauDatasetSelector selector = new() { TexturePreference = PlateauTexturePreference.PreferUntextured };

        PlateauDatasetEntry? best = selector.SelectByTypes(catalog, sapporo, new[] { "bldg" }).Single();

        Assert.Equal("2", best.Lod);
        Assert.False(best.Texture);
    }

    [Fact]
    public void SelectBest_prefers_higher_lod_after_texture()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        PlateauAreaOption sapporo = catalog.AreaOptions.Single(a => a.Code == "01100");
        PlateauDatasetSelector selector = new() { TexturePreference = PlateauTexturePreference.PreferUntextured };

        PlateauDatasetEntry? best = selector.SelectByTypes(catalog, sapporo, new[] { "bldg" }).Single();

        // sapporo has lod1+untextured and lod2+untextured; lod2 should win after texture tie.
        Assert.Equal("2", best.Lod);
    }

    [Fact]
    public void SelectBest_breaks_ties_using_latest_catalog_source()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        PlateauAreaOption chiyoda = catalog.AreaOptions.Single(a => a.Code == "13101");
        PlateauDatasetSelector selector = new() { TexturePreference = PlateauTexturePreference.PreferTextured };

        PlateauDatasetEntry? best = selector.SelectByTypes(catalog, chiyoda, new[] { "bldg" }).Single();

        // The latest_datasets entry (2023) should beat the regular dataset (2022).
        Assert.Equal(2023, best.Year);
        Assert.Equal(PlateauCatalogSource.Latest, best.CatalogSource);
    }

    [Fact]
    public void SelectByTypes_returns_one_per_requested_type()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        PlateauAreaOption sapporo = catalog.AreaOptions.Single(a => a.Code == "01100");
        PlateauDatasetSelector selector = new();

        var picks = selector.SelectByTypes(catalog, sapporo, new[] { "bldg", "dem" });

        Assert.Equal(2, picks.Count);
        Assert.Contains(picks, p => p.TypeEn == "bldg");
        Assert.Contains(picks, p => p.TypeEn == "dem");
    }
}
