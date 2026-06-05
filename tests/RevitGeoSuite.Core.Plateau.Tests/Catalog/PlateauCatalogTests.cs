using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Plateau.Catalog;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Catalog;

public sealed class PlateauCatalogTests
{
    private static PlateauCatalog LoadSampleCatalog()
    {
        string path = TestPathHelper.GetFixturePath("tests", "Fixtures", "plateau-catalog-sample.json");
        string text = File.ReadAllText(path);
        PlateauCatalogResponse response = JsonConvert.DeserializeObject<PlateauCatalogResponse>(text)!;
        return PlateauCatalog.Normalize(response);
    }

    [Fact]
    public void Normalize_excludes_non_3d_tiles_formats()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        Assert.DoesNotContain(catalog.Datasets, d => d.Format != "3D Tiles");
        Assert.DoesNotContain(catalog.Datasets, d => d.TypeEn == "luse");
    }

    [Fact]
    public void Normalize_retains_mvt_datasets_separately()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        PlateauDatasetEntry luse = Assert.Single(catalog.MvtDatasets, d => d.TypeEn == "luse");

        Assert.Equal("MVT", luse.Format);
        Assert.DoesNotContain(catalog.Datasets, d => ReferenceEquals(d, luse));
    }

    [Fact]
    public void Normalize_excludes_interior_datasets()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        Assert.DoesNotContain(catalog.Datasets, d => d.Interior == true);
    }

    [Fact]
    public void Normalize_excludes_entries_without_tileset_json_in_url()
    {
        PlateauCatalogResponse response = new PlateauCatalogResponse
        {
            Datasets = new System.Collections.Generic.List<PlateauDatasetEntry>
            {
                new PlateauDatasetEntry
                {
                    Format = "3D Tiles",
                    TypeEn = "bldg",
                    Url = "https://example.com/not-a-tileset.zip",
                    CityCode = "01100"
                }
            }
        };
        PlateauCatalog catalog = PlateauCatalog.Normalize(response);
        Assert.Empty(catalog.Datasets);
    }

    [Fact]
    public void Normalize_marks_latest_datasets_as_latest_source()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        PlateauDatasetEntry latest = catalog.Datasets.Single(d => d.Year == 2023 && d.WardCode == "13101");
        Assert.Equal(PlateauCatalogSource.Latest, latest.CatalogSource);
    }

    [Fact]
    public void AreaOptions_aggregate_by_ward_code_with_city_alias()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        PlateauAreaOption chiyoda = Assert.Single(catalog.AreaOptions, a => a.Code == "13101");
        Assert.Contains("13100", chiyoda.Aliases);
        // The latest_datasets entry (processed first) sets only `city`, hence no trailing ward token.
        Assert.Equal("東京都 千代田区", chiyoda.Label);
    }

    [Fact]
    public void AreaOptions_aggregate_by_city_code_when_no_ward()
    {
        PlateauCatalog catalog = LoadSampleCatalog();
        PlateauAreaOption sapporo = Assert.Single(catalog.AreaOptions, a => a.Code == "01100");
        Assert.Equal("北海道 札幌市", sapporo.Label);
    }
}
