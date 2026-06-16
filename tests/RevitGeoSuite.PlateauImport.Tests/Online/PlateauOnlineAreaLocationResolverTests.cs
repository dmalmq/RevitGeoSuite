using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class PlateauOnlineAreaLocationResolverTests
{
    [Fact]
    public async Task ResolveAsync_returns_center_and_zoom_from_tileset_bounds()
    {
        const string url = "https://example.test/shinjuku/bldg/tileset.json";
        StubHttpClient httpClient = new((url, TilesetWithRegion(139.68, 35.67, 139.72, 35.71)));
        PlateauCatalog catalog = CreateCatalog(Dataset("13104", url));
        PlateauAreaGeometryService geometryService = new PlateauAreaGeometryService(httpClient);

        PlateauOnlineAreaLocation? location = await PlateauOnlineAreaLocationResolver.ResolveAsync(
            catalog,
            geometryService,
            "13104");

        Assert.NotNull(location);
        Assert.Equal("13104", location!.AreaCode);
        Assert.Equal(35.69, location.Latitude, 8);
        Assert.Equal(139.70, location.Longitude, 8);
        Assert.Equal(14, location.Zoom);
        Assert.Equal(url, httpClient.RequestedUris.Single().AbsoluteUri);
    }

    [Fact]
    public async Task ResolveAsync_accepts_area_alias_code()
    {
        const string url = "https://example.test/tokyo/shinjuku/bldg/tileset.json";
        StubHttpClient httpClient = new((url, TilesetWithRegion(139.68, 35.67, 139.72, 35.71)));
        PlateauCatalog catalog = CreateCatalog(Dataset(cityCode: "13100", wardCode: "13104", url: url));
        PlateauAreaGeometryService geometryService = new PlateauAreaGeometryService(httpClient);

        PlateauOnlineAreaLocation? location = await PlateauOnlineAreaLocationResolver.ResolveAsync(
            catalog,
            geometryService,
            "13100");

        Assert.NotNull(location);
        Assert.Equal("13104", location!.AreaCode);
    }

    [Fact]
    public async Task ResolveAsync_returns_null_when_bounds_are_unavailable()
    {
        const string url = "https://example.test/shibuya/bldg/tileset.json";
        StubHttpClient httpClient = new((url, "{\"root\":{\"boundingVolume\":{\"box\":[0,0,0,1,0,0,0,1,0,0,0,1]}}}"));
        PlateauCatalog catalog = CreateCatalog(Dataset("13113", url));
        PlateauAreaGeometryService geometryService = new PlateauAreaGeometryService(httpClient);

        PlateauOnlineAreaLocation? location = await PlateauOnlineAreaLocationResolver.ResolveAsync(
            catalog,
            geometryService,
            "13113");

        Assert.Null(location);
    }

    private static PlateauCatalog CreateCatalog(params PlateauDatasetEntry[] datasets)
    {
        return PlateauCatalog.Normalize(new PlateauCatalogResponse
        {
            Datasets = datasets.ToList()
        });
    }

    private static PlateauDatasetEntry Dataset(string cityCode, string url)
    {
        return Dataset(cityCode, wardCode: null, url);
    }

    private static PlateauDatasetEntry Dataset(string cityCode, string? wardCode, string url)
    {
        return new PlateauDatasetEntry
        {
            Format = "3D Tiles",
            TypeEn = "bldg",
            Url = url,
            Lod = "2",
            Texture = false,
            CityCode = cityCode,
            WardCode = wardCode,
            Pref = "東京都",
            City = "新宿区",
            Ward = wardCode is null ? string.Empty : "新宿区",
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

        public List<Uri> RequestedUris { get; } = new List<Uri>();

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken)
        {
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
