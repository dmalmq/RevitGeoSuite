using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Plateau.Catalog;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Catalog;

public sealed class PlateauAreaGeometryServiceTests
{
    [Fact]
    public async Task GetBoundsAsync_reads_region_and_converts_radians_to_degrees()
    {
        const string url = "https://example.test/tokyo/bldg/tileset.json";
        StubHttpClient httpClient = new((url, TilesetWithRegion(139.75, 35.66, 139.77, 35.68)));
        PlateauAreaGeometryService service = new PlateauAreaGeometryService(httpClient);
        PlateauCatalog catalog = CreateCatalog(Dataset("13101", url, lod: "2", texture: false));
        PlateauAreaOption area = catalog.AreaOptions.Single();

        PlateauAreaBounds? bounds = await service.GetBoundsAsync(area, catalog);

        Assert.NotNull(bounds);
        Assert.Equal(139.75, bounds!.WestDeg, 8);
        Assert.Equal(35.66, bounds.SouthDeg, 8);
        Assert.Equal(139.77, bounds.EastDeg, 8);
        Assert.Equal(35.68, bounds.NorthDeg, 8);
    }

    [Fact]
    public async Task GetBoundsAsync_caches_result_by_area_code()
    {
        const string url = "https://example.test/tokyo/bldg/tileset.json";
        StubHttpClient httpClient = new((url, TilesetWithRegion(139.75, 35.66, 139.77, 35.68)));
        PlateauAreaGeometryService service = new PlateauAreaGeometryService(httpClient);
        PlateauCatalog catalog = CreateCatalog(Dataset("13101", url, lod: "2", texture: false));
        PlateauAreaOption area = catalog.AreaOptions.Single();

        PlateauAreaBounds? first = await service.GetBoundsAsync(area, catalog);
        PlateauAreaBounds? second = await service.GetBoundsAsync(area, catalog);

        Assert.NotNull(first);
        Assert.Same(first, second);
        Assert.Equal(1, httpClient.ByteRequestCount);
    }

    [Fact]
    public async Task GetBoundsAsync_returns_null_for_box_only_tileset()
    {
        const string url = "https://example.test/tokyo/bldg/tileset.json";
        StubHttpClient httpClient = new((url, "{\"root\":{\"boundingVolume\":{\"box\":[0,0,0,1,0,0,0,1,0,0,0,1]}}}"));
        PlateauAreaGeometryService service = new PlateauAreaGeometryService(httpClient);
        PlateauCatalog catalog = CreateCatalog(Dataset("13101", url, lod: "2", texture: false));
        PlateauAreaOption area = catalog.AreaOptions.Single();

        PlateauAreaBounds? bounds = await service.GetBoundsAsync(area, catalog);

        Assert.Null(bounds);
        Assert.Equal(1, httpClient.ByteRequestCount);
    }

    [Fact]
    public async Task GetBoundsAsync_returns_null_without_http_when_no_dataset_matches_area()
    {
        const string url = "https://example.test/tokyo/bldg/tileset.json";
        StubHttpClient httpClient = new((url, TilesetWithRegion(139.75, 35.66, 139.77, 35.68)));
        PlateauAreaGeometryService service = new PlateauAreaGeometryService(httpClient);
        PlateauCatalog catalog = CreateCatalog(Dataset("13101", url, lod: "2", texture: false));
        PlateauAreaOption missingArea = new PlateauAreaOption("14100", Array.Empty<string>(), "神奈川県 横浜市", "神奈川県", "横浜市", string.Empty);

        PlateauAreaBounds? bounds = await service.GetBoundsAsync(missingArea, catalog);

        Assert.Null(bounds);
        Assert.Equal(0, httpClient.ByteRequestCount);
    }

    [Fact]
    public async Task GetBoundsAsync_prefers_lod2_untextured_building_dataset()
    {
        const string lod1Url = "https://example.test/tokyo/bldg-lod1/tileset.json";
        const string lod2TexturedUrl = "https://example.test/tokyo/bldg-lod2-textured/tileset.json";
        const string lod2UntexturedUrl = "https://example.test/tokyo/bldg-lod2-untextured/tileset.json";
        StubHttpClient httpClient = new(
            (lod1Url, TilesetWithRegion(139.70, 35.60, 139.71, 35.61)),
            (lod2TexturedUrl, TilesetWithRegion(139.72, 35.62, 139.73, 35.63)),
            (lod2UntexturedUrl, TilesetWithRegion(139.74, 35.64, 139.75, 35.65)));
        PlateauAreaGeometryService service = new PlateauAreaGeometryService(httpClient);
        PlateauCatalog catalog = CreateCatalog(
            Dataset("13101", lod1Url, lod: "1", texture: false),
            Dataset("13101", lod2TexturedUrl, lod: "2", texture: true),
            Dataset("13101", lod2UntexturedUrl, lod: "2", texture: false));
        PlateauAreaOption area = catalog.AreaOptions.Single();

        PlateauAreaBounds? bounds = await service.GetBoundsAsync(area, catalog);

        Assert.NotNull(bounds);
        Assert.Equal(lod2UntexturedUrl, httpClient.RequestedUris.Single().AbsoluteUri);
    }

    private static PlateauCatalog CreateCatalog(params PlateauDatasetEntry[] datasets)
    {
        PlateauCatalogResponse response = new PlateauCatalogResponse
        {
            Datasets = datasets.ToList()
        };
        return PlateauCatalog.Normalize(response);
    }

    private static PlateauDatasetEntry Dataset(string cityCode, string url, string lod, bool? texture)
    {
        return new PlateauDatasetEntry
        {
            Format = "3D Tiles",
            TypeEn = "bldg",
            Url = url,
            Lod = lod,
            Texture = texture,
            CityCode = cityCode,
            Pref = "東京都",
            City = "千代田区",
            Year = 2023
        };
    }

    private static string TilesetWithRegion(double westDeg, double southDeg, double eastDeg, double northDeg)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"root\":{{\"boundingVolume\":{{\"region\":[{0},{1},{2},{3},0,100]}}}}}}",
            DegreesToRadians(westDeg),
            DegreesToRadians(southDeg),
            DegreesToRadians(eastDeg),
            DegreesToRadians(northDeg));
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private sealed class StubHttpClient : IPlateauHttpClient
    {
        private readonly Dictionary<string, byte[]> responses;

        public StubHttpClient(params (string Url, string Body)[] responses)
        {
            this.responses = responses.ToDictionary(
                response => response.Url,
                response => Encoding.UTF8.GetBytes(response.Body),
                StringComparer.Ordinal);
        }

        public int ByteRequestCount { get; private set; }

        public List<Uri> RequestedUris { get; } = new List<Uri>();

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken)
        {
            ByteRequestCount++;
            RequestedUris.Add(url);
            if (responses.TryGetValue(url.AbsoluteUri, out byte[]? bytes))
            {
                return Task.FromResult(bytes);
            }

            throw new InvalidOperationException("Unexpected request: " + url.AbsoluteUri);
        }

        public Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }
    }
}
