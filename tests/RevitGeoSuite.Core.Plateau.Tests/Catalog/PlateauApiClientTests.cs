using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Plateau.Catalog;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Catalog;

public sealed class PlateauApiClientTests
{
    [Fact]
    public async Task FetchCatalogAsync_calls_catalog_url_and_returns_normalized_catalog()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "plateau-catalog-sample.json");
        string fixture = File.ReadAllText(fixturePath);

        FakeHttpClient fake = new FakeHttpClient();
        fake.StringResponses[new Uri("https://example.com/catalog")] = fixture;

        PlateauApiClient client = new PlateauApiClient(
            fake,
            new Uri("https://example.com/catalog"),
            new Uri("https://example.com/reverse"));

        PlateauCatalog catalog = await client.FetchCatalogAsync();

        Assert.NotEmpty(catalog.Datasets);
        Assert.All(catalog.Datasets, d => Assert.Equal("3D Tiles", d.Format));
        Assert.Single(fake.GetStringCalls, c => c.AbsoluteUri == "https://example.com/catalog");
    }

    [Fact]
    public async Task ReverseGeocodeMunicipalityCodeAsync_calls_reverse_endpoint_and_returns_code()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri reverse = new Uri("https://example.com/reverse");
        fake.AddPrefixResponse(reverse, "{\"results\":{\"muniCd\":\"13101\"}}");

        PlateauApiClient client = new PlateauApiClient(
            fake,
            new Uri("https://example.com/catalog"),
            reverse);

        string? code = await client.ReverseGeocodeMunicipalityCodeAsync(35.6895, 139.6917);

        Assert.Equal("13101", code);
        Uri requested = Assert.Single(fake.GetStringCalls);
        Assert.Contains("lat=35.6895", requested.Query);
        Assert.Contains("lon=139.6917", requested.Query);
    }

    [Fact]
    public async Task ReverseGeocodeMunicipalityCodeAsync_returns_null_when_missing()
    {
        FakeHttpClient fake = new FakeHttpClient();
        Uri reverse = new Uri("https://example.com/reverse");
        fake.AddPrefixResponse(reverse, "{\"results\":{}}");

        PlateauApiClient client = new PlateauApiClient(
            fake,
            new Uri("https://example.com/catalog"),
            reverse);

        string? code = await client.ReverseGeocodeMunicipalityCodeAsync(0, 0);

        Assert.Null(code);
    }

    private sealed class FakeHttpClient : IPlateauHttpClient
    {
        public Dictionary<Uri, string> StringResponses { get; } = new();
        public List<(Uri Url, string Body)> PrefixResponses { get; } = new();
        public List<Uri> GetStringCalls { get; } = new();

        public void AddPrefixResponse(Uri prefix, string body) => PrefixResponses.Add((prefix, body));

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
        {
            GetStringCalls.Add(url);
            if (StringResponses.TryGetValue(url, out string? exact)) return Task.FromResult(exact);
            foreach (var (prefix, body) in PrefixResponses)
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
