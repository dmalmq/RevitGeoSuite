using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Plateau.Catalog;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Catalog;

public sealed class GeospatialCkanClientTests
{
    [Fact]
    public void GroupSlugs_groups_by_code_with_years_newest_first()
    {
        string[] slugs =
        {
            "plateau-13113-shibuya-ku-2020",
            "plateau-13113-shibuya-ku-2023",
            "plateau-13101-chiyoda-ku-2022",
            "fme-workspace-examples", // ignored — not a PLATEAU dataset
            "plateau",                // ignored — portal page, no code/year
            "plateau-13113-shibuya-ku-2022",
        };

        IReadOnlyList<CkanMunicipalityDatasets> result = GeospatialCkanClient.GroupSlugs(slugs);

        Assert.Equal(2, result.Count);
        CkanMunicipalityDatasets shibuya = result.Single(m => m.Code == "13113");
        Assert.Equal("shibuya-ku", shibuya.Romaji);
        Assert.Equal(new[] { "2023", "2022", "2020" }, shibuya.Datasets.Select(d => d.Year).ToArray());
        Assert.Equal("plateau-13113-shibuya-ku-2023", shibuya.Datasets[0].Slug);
    }

    [Fact]
    public async Task ListPlateauDatasetSlugsAsync_filters_to_plateau_pattern()
    {
        string body = JsonConvert.SerializeObject(new
        {
            success = true,
            result = new[]
            {
                "plateau-13113-shibuya-ku-2023",
                "abcdefg",
                "plateau",
                "plateau-01100-sapporo-shi-2022"
            }
        });

        FakeHttpClient fake = new();
        fake.StringResponses[new Uri("https://ckan.example/api/3/action/package_list")] = body;

        GeospatialCkanClient client = new(fake, new Uri("https://ckan.example/api/3/action/"));
        IReadOnlyList<string> slugs = await client.ListPlateauDatasetSlugsAsync(CancellationToken.None);

        Assert.Equal(
            new[] { "plateau-13113-shibuya-ku-2023", "plateau-01100-sapporo-shi-2022" },
            slugs.ToArray());
    }

    [Fact]
    public async Task GetCityGmlResourcesAsync_keeps_only_citygml_zips_and_parses_feature_lod_size()
    {
        string body = @"{ ""success"": true, ""result"": { ""name"": ""plateau-13113-shibuya-ku-2023"", ""resources"": [
            { ""id"": ""r1"", ""name"": ""13113_shibuya-ku_2023_citygml_building_lod2_op"", ""format"": ""CityGML"",
              ""url"": ""https://ex/dataset/plateau-13113/resource/r1/download/13113_shibuya-ku_2023_citygml_building_lod2_op.zip"", ""size"": 456234567 },
            { ""id"": ""r2"", ""name"": ""13113_shibuya-ku_2023_citygml_tran_lod1"", ""format"": ""CityGML"",
              ""url"": ""https://ex/dataset/plateau-13113/resource/r2/download/13113_shibuya-ku_2023_citygml_tran_lod1.zip"", ""size"": ""18000"" },
            { ""id"": ""r3"", ""name"": ""13113_shibuya-ku_2023_3dtiles_building_lod2"", ""format"": ""3DTiles"",
              ""url"": ""https://ex/dataset/plateau-13113/resource/r3/download/13113_3dtiles.zip"", ""size"": null }
        ] } }";

        FakeHttpClient fake = new();
        fake.AddPrefixResponse(new Uri("https://ckan.example/api/3/action/package_show"), body);

        GeospatialCkanClient client = new(fake, new Uri("https://ckan.example/api/3/action/"));
        IReadOnlyList<CkanCityGmlResource> resources =
            await client.GetCityGmlResourcesAsync("plateau-13113-shibuya-ku-2023", CancellationToken.None);

        Assert.Equal(2, resources.Count); // the 3D Tiles resource is excluded

        CkanCityGmlResource building = resources.Single(r => r.FeatureKey == "building");
        Assert.Equal("Buildings", building.FeatureLabel);
        Assert.Equal("2", building.Lod);
        Assert.Equal(456234567L, building.SizeBytes);
        Assert.EndsWith("13113_shibuya-ku_2023_citygml_building_lod2_op.zip", building.FileName);

        CkanCityGmlResource roads = resources.Single(r => r.FeatureKey == "tran");
        Assert.Equal("Roads", roads.FeatureLabel);
        Assert.Equal("1", roads.Lod);
        Assert.Equal(18000L, roads.SizeBytes); // numeric string parsed
    }

    [Theory]
    [InlineData("13113_shibuya-ku_2023_citygml_building_lod2_op", "building", "2")]
    [InlineData("13101_chiyoda-ku_2022_citygml_dem_lod1", "dem", "1")]
    [InlineData("13113_shibuya-ku_2023_citygml_tran_lod3", "tran", "3")]
    public void ParseFeatureAndLod_parses_known_names(string name, string feature, string lod)
    {
        (string parsedFeature, string parsedLod) = CkanResourceNaming.ParseFeatureAndLod(name);
        Assert.Equal(feature, parsedFeature);
        Assert.Equal(lod, parsedLod);
    }

    private sealed class FakeHttpClient : IPlateauHttpClient
    {
        public Dictionary<Uri, string> StringResponses { get; } = new();
        public List<(Uri Url, string Body)> PrefixResponses { get; } = new();

        public void AddPrefixResponse(Uri prefix, string body) => PrefixResponses.Add((prefix, body));

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
        {
            if (StringResponses.TryGetValue(url, out string? exact))
            {
                return Task.FromResult(exact);
            }

            foreach ((Uri prefix, string body) in PrefixResponses)
            {
                if (url.AbsoluteUri.StartsWith(prefix.AbsoluteUri, StringComparison.Ordinal))
                {
                    return Task.FromResult(body);
                }
            }

            throw new InvalidOperationException($"No fake response registered for {url}");
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken) =>
            throw new NotImplementedException();

        public Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }
}
