using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Tiles3D;

public sealed class PlateauTilesetDownloaderTests
{
    [Fact]
    public async Task DownloadAsync_preserves_local_z_as_project_height_after_tile_transform()
    {
        Uri rootUrl = new Uri("https://example.test/area/tileset.json");
        Uri b3dmUrl = new Uri("https://example.test/area/leaf.b3dm");
        FakeHttpClient httpClient = new FakeHttpClient();
        httpClient.StringResponses[rootUrl] = BuildRootTilesetJson(BuildEquatorEastNorthUpTransform());
        httpClient.ByteResponses[b3dmUrl] = BuildSingleTriangleB3dm();

        EcefToProjectTransformer transformer = new EcefToProjectTransformer(
            new IdentityCoordinateTransformer(),
            new CrsReference { EpsgCode = 0 },
            altitudeAnchorMeters: 0,
            outputVerticalDatum: VerticalDatum.Ellipsoidal);
        PlateauTilesetDownloader downloader = new PlateauTilesetDownloader(
            httpClient,
            new GltfMeshDecoder(new MissingDracoMeshDecoder()),
            transformer,
            new PlateauTilesetCache(Path.Combine(Path.GetTempPath(), "RevitGeoSuiteTests", Guid.NewGuid().ToString("N"))));

        PlateauDatasetEntry entry = new PlateauDatasetEntry
        {
            Url = rootUrl.AbsoluteUri,
            TypeEn = "bldg",
            Lod = "2",
            Texture = false
        };

        PlateauTilesetModel model = await downloader.DownloadAsync(entry, "00000", progress: null, CancellationToken.None);

        PlateauTilesetTriangle triangle = Assert.Single(Assert.Single(model.Features).Triangles);
        double[] heights = { triangle.A.Z, triangle.B.Z, triangle.C.Z };
        Assert.Contains(heights, z => Math.Abs(z) < 0.01);
        Assert.Contains(heights, z => Math.Abs(z - 10.0) < 0.01);
    }

    [Fact]
    public async Task DownloadAsync_with_selected_mesh_ids_skips_non_intersecting_leaves()
    {
        Uri rootUrl = new Uri("https://example.test/area/tileset.json");
        Uri selectedUrl = new Uri("https://example.test/area/selected.b3dm");
        Uri skippedUrl = new Uri("https://example.test/area/skipped.b3dm");
        FakeHttpClient httpClient = new FakeHttpClient();
        httpClient.StringResponses[rootUrl] = BuildTwoLeafTilesetJson(
            selectedUri: "selected.b3dm",
            westDeg: 139.700,
            southDeg: 35.680,
            eastDeg: 139.705,
            northDeg: 35.685,
            skippedUri: "skipped.b3dm",
            westDeg2: 139.900,
            southDeg2: 35.680,
            eastDeg2: 139.905,
            northDeg2: 35.685);
        httpClient.ByteResponses[selectedUrl] = BuildSingleTriangleB3dm();
        httpClient.ByteResponses[skippedUrl] = BuildSingleTriangleB3dm();

        EcefToProjectTransformer transformer = new EcefToProjectTransformer(
            new IdentityCoordinateTransformer(),
            new CrsReference { EpsgCode = 0 },
            altitudeAnchorMeters: 0,
            outputVerticalDatum: VerticalDatum.Ellipsoidal);
        PlateauTilesetDownloader downloader = new PlateauTilesetDownloader(
            httpClient,
            new GltfMeshDecoder(new MissingDracoMeshDecoder()),
            transformer,
            new PlateauTilesetCache(Path.Combine(Path.GetTempPath(), "RevitGeoSuiteTests", Guid.NewGuid().ToString("N"))));

        PlateauDatasetEntry entry = new PlateauDatasetEntry
        {
            Url = rootUrl.AbsoluteUri,
            TypeEn = "bldg",
            Lod = "2",
            Texture = false
        };
        string selectedMeshId = new JapanMeshCalculator().Calculate(35.681, 139.701, JapanMeshLevel.Tertiary).Value;

        PlateauTilesetModel model = await downloader.DownloadAsync(entry, "00000", new[] { selectedMeshId }, progress: null, CancellationToken.None);

        Assert.Single(model.Features);
        Assert.Contains(selectedUrl, httpClient.ByteRequests);
        Assert.DoesNotContain(skippedUrl, httpClient.ByteRequests);
    }

    [Fact]
    public async Task DownloadAsync_does_not_duplicate_buildings_across_REPLACE_parent_and_child_tiles()
    {
        Uri rootUrl = new Uri("https://example.test/chiyoda/tileset.json");
        Uri parentUrl = new Uri("https://example.test/chiyoda/parent.b3dm");
        Uri childUrl = new Uri("https://example.test/chiyoda/child.b3dm");
        string transformJson = string.Join(",", BuildEquatorEastNorthUpTransform().Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

        FakeHttpClient httpClient = new FakeHttpClient();
        httpClient.StringResponses[rootUrl] =
            "{\"root\":{\"refine\":\"REPLACE\",\"transform\":[" + transformJson + "],\"content\":{\"uri\":\"parent.b3dm\"}," +
            "\"children\":[{\"content\":{\"uri\":\"child.b3dm\"}}]}}";
        httpClient.ByteResponses[parentUrl] = BuildSingleTriangleB3dm();
        httpClient.ByteResponses[childUrl] = BuildSingleTriangleB3dm();

        EcefToProjectTransformer transformer = new EcefToProjectTransformer(
            new IdentityCoordinateTransformer(),
            new CrsReference { EpsgCode = 0 },
            altitudeAnchorMeters: 0,
            outputVerticalDatum: VerticalDatum.Ellipsoidal);
        PlateauTilesetDownloader downloader = new PlateauTilesetDownloader(
            httpClient,
            new GltfMeshDecoder(new MissingDracoMeshDecoder()),
            transformer,
            new PlateauTilesetCache(Path.Combine(Path.GetTempPath(), "RevitGeoSuiteTests", Guid.NewGuid().ToString("N"))));

        PlateauDatasetEntry entry = new PlateauDatasetEntry
        {
            Url = rootUrl.AbsoluteUri,
            TypeEn = "bldg",
            Lod = "2",
            Texture = false
        };

        PlateauTilesetModel model = await downloader.DownloadAsync(entry, "13101", progress: null, CancellationToken.None);

        PlateauTilesetFeature feature = Assert.Single(model.Features);
        Assert.Single(feature.Triangles);
        Assert.Contains(childUrl, httpClient.ByteRequests);
        Assert.DoesNotContain(parentUrl, httpClient.ByteRequests);
    }

    [Fact]
    public async Task DownloadAsync_scopes_fallback_batch_ids_per_tile_when_gml_id_is_missing()
    {
        Uri rootUrl = new Uri("https://example.test/osm/tileset.json");
        Uri tileA = new Uri("https://example.test/osm/a.b3dm");
        Uri tileB = new Uri("https://example.test/osm/b.b3dm");
        string transformJson = string.Join(",", BuildEquatorEastNorthUpTransform().Select(value => value.ToString("R", CultureInfo.InvariantCulture)));

        FakeHttpClient httpClient = new FakeHttpClient();
        httpClient.StringResponses[rootUrl] =
            "{\"root\":{\"refine\":\"REPLACE\",\"transform\":[" + transformJson + "],\"children\":[" +
            "{\"content\":{\"uri\":\"a.b3dm\"}}," +
            "{\"content\":{\"uri\":\"b.b3dm\"}}" +
            "]}}";
        httpClient.ByteResponses[tileA] = BuildSingleTriangleB3dm(batchTableJson: string.Empty);
        httpClient.ByteResponses[tileB] = BuildSingleTriangleB3dm(batchTableJson: string.Empty);

        EcefToProjectTransformer transformer = new EcefToProjectTransformer(
            new IdentityCoordinateTransformer(),
            new CrsReference { EpsgCode = 0 },
            altitudeAnchorMeters: 0,
            outputVerticalDatum: VerticalDatum.Ellipsoidal);
        PlateauTilesetDownloader downloader = new PlateauTilesetDownloader(
            httpClient,
            new GltfMeshDecoder(new MissingDracoMeshDecoder()),
            transformer,
            new PlateauTilesetCache(Path.Combine(Path.GetTempPath(), "RevitGeoSuiteTests", Guid.NewGuid().ToString("N"))));

        PlateauTilesetModel model = await downloader.DownloadAsync(
            rootUrl,
            "OSM Buildings",
            bbox: null,
            progress: null,
            CancellationToken.None);

        Assert.Equal(2, model.Features.Count);
        Assert.NotEqual(model.Features[0].Id, model.Features[1].Id);
        Assert.All(model.Features, f => Assert.False(string.IsNullOrWhiteSpace(f.GridId)));
    }

    [Fact]
    public async Task DownloadAsync_reuses_seeded_legacy_plateau_online_cache_without_network_download()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "RevitGeoSuiteTests", Guid.NewGuid().ToString("N"));
        string newCacheRoot = Path.Combine(tempRoot, "KanizuRevit", "PlateauOnline");
        string legacyCacheRoot = Path.Combine(tempRoot, "RevitGeoSuite", "PlateauOnline");
        try
        {
            Uri entryRootUrl = new Uri("https://example.test/area/tileset.json");
            Uri entryB3dmUrl = new Uri("https://example.test/area/leaf.b3dm");
            Uri ionRootUrl = new Uri("https://example.test/ion/tileset.json");
            Uri ionB3dmUrl = new Uri("https://example.test/ion/leaf.b3dm");

            // Seed both dataset variants in the legacy RevitGeoSuite\PlateauOnline cache.
            PlateauTilesetCache legacyCache = new PlateauTilesetCache(legacyCacheRoot);
            string legacyEntryFolder = legacyCache.GetDatasetFolder("13101", "bldg", "2", texture: false);
            legacyCache.Store(legacyCache.GetFilePath(legacyEntryFolder, entryB3dmUrl), BuildSingleTriangleB3dm());

            string legacyIonFolder = legacyCache.GetDatasetFolder("ion", "osm-buildings", null, null);
            legacyCache.Store(legacyCache.GetFilePath(legacyIonFolder, ionB3dmUrl), BuildSingleTriangleB3dm());

            FakeHttpClient httpClient = new FakeHttpClient();
            httpClient.StringResponses[entryRootUrl] = BuildRootTilesetJson(BuildEquatorEastNorthUpTransform());
            httpClient.StringResponses[ionRootUrl] = BuildRootTilesetJson(BuildEquatorEastNorthUpTransform());
            // Deliberately do not populate ByteResponses: any network fetch for the .b3dm fails.

            EcefToProjectTransformer transformer = new EcefToProjectTransformer(
                new IdentityCoordinateTransformer(),
                new CrsReference { EpsgCode = 0 },
                altitudeAnchorMeters: 0,
                outputVerticalDatum: VerticalDatum.Ellipsoidal);
            PlateauTilesetDownloader downloader = new PlateauTilesetDownloader(
                httpClient,
                new GltfMeshDecoder(new MissingDracoMeshDecoder()),
                transformer,
                new PlateauTilesetCache(newCacheRoot, legacyCacheRoot));

            PlateauDatasetEntry entry = new PlateauDatasetEntry
            {
                Url = entryRootUrl.AbsoluteUri,
                TypeEn = "bldg",
                Lod = "2",
                Texture = false
            };

            PlateauTilesetModel entryModel = await downloader.DownloadAsync(entry, "13101", progress: null, CancellationToken.None);
            Assert.Single(entryModel.Features);

            PlateauTilesetModel ionModel = await downloader.DownloadAsync(ionRootUrl, "OSM Buildings", bbox: null, progress: null, CancellationToken.None);
            Assert.Single(ionModel.Features);

            Assert.Empty(httpClient.ByteRequests);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string BuildRootTilesetJson(double[] transform)
    {
        string transformJson = string.Join(",", transform.Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        return "{\"root\":{\"transform\":[" + transformJson + "],\"content\":{\"uri\":\"leaf.b3dm\"}}}";
    }

    private static string BuildTwoLeafTilesetJson(
        string selectedUri,
        double westDeg,
        double southDeg,
        double eastDeg,
        double northDeg,
        string skippedUri,
        double westDeg2,
        double southDeg2,
        double eastDeg2,
        double northDeg2)
    {
        string transformJson = string.Join(",", BuildEquatorEastNorthUpTransform().Select(value => value.ToString("R", CultureInfo.InvariantCulture)));
        return "{\"root\":{\"children\":[" +
            BuildLeafJson(selectedUri, westDeg, southDeg, eastDeg, northDeg, transformJson) + "," +
            BuildLeafJson(skippedUri, westDeg2, southDeg2, eastDeg2, northDeg2, transformJson) +
            "]}}";
    }

    private static string BuildLeafJson(string uri, double westDeg, double southDeg, double eastDeg, double northDeg, string transformJson)
    {
        return string.Format(
            CultureInfo.InvariantCulture,
            "{{\"boundingVolume\":{{\"region\":[{0},{1},{2},{3},0,100]}},\"transform\":[{4}],\"content\":{{\"uri\":\"{5}\"}}}}",
            DegreesToRadians(westDeg),
            DegreesToRadians(southDeg),
            DegreesToRadians(eastDeg),
            DegreesToRadians(northDeg),
            transformJson,
            uri);
    }

    private static double[] BuildEquatorEastNorthUpTransform()
    {
        double a = EcefGeodeticConverter.WgsSemiMajorMeters;
        return new[]
        {
            0.0, 1.0, 0.0, 0.0,
            0.0, 0.0, 1.0, 0.0,
            1.0, 0.0, 0.0, 0.0,
            a, 0.0, 0.0, 1.0
        };
    }

    private static double DegreesToRadians(double degrees)
    {
        return degrees * Math.PI / 180.0;
    }

    private static byte[] BuildSingleTriangleB3dm(string batchTableJson = "{\"gml_id\":[\"feature-a\"]}")
    {
        // glTF positions are Y-up (per the 3D Tiles / glTF spec), so a 10 m "up" vertex
        // is (0, 10, 0). The decoder rotates Y-up -> Z-up before the tile transform, so
        // this becomes 10 m of local Z (height) in the project frame.
        float[] positions =
        {
            0, 0, 0,
            1, 0, 0,
            0, 10, 0,
        };
        ushort[] batchIds = { 0, 0, 0 };
        ushort[] indices = { 0, 1, 2 };

        byte[] bin = ConcatLE(positions, indices, batchIds);
        int bvPosLen = positions.Length * 4;
        int bvIdxLen = indices.Length * 2;
        int bvBatchLen = batchIds.Length * 2;

        string json = "{" +
            "\"asset\":{\"version\":\"2.0\"}," +
            "\"buffers\":[{\"byteLength\":" + bin.Length + "}]," +
            "\"bufferViews\":[" +
                "{\"buffer\":0,\"byteOffset\":0,\"byteLength\":" + bvPosLen + "}," +
                "{\"buffer\":0,\"byteOffset\":" + bvPosLen + ",\"byteLength\":" + bvIdxLen + "}," +
                "{\"buffer\":0,\"byteOffset\":" + (bvPosLen + bvIdxLen) + ",\"byteLength\":" + bvBatchLen + "}" +
            "]," +
            "\"accessors\":[" +
                "{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}," +
                "{\"bufferView\":1,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}," +
                "{\"bufferView\":2,\"componentType\":5123,\"count\":3,\"type\":\"SCALAR\"}" +
            "]," +
            "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"_BATCHID\":2},\"indices\":1,\"mode\":4}]}]," +
            "\"nodes\":[{\"mesh\":0}],\"scenes\":[{\"nodes\":[0]}],\"scene\":0" +
        "}";

        return BuildB3dm("{\"BATCH_LENGTH\":1}", batchTableJson, BuildGlb(json, bin));
    }

    private static byte[] ConcatLE(float[] positions, ushort[] indices, ushort[] batchIds)
    {
        byte[] result = new byte[positions.Length * 4 + indices.Length * 2 + batchIds.Length * 2];
        int offset = 0;
        Buffer.BlockCopy(positions, 0, result, offset, positions.Length * 4);
        offset += positions.Length * 4;
        Buffer.BlockCopy(indices, 0, result, offset, indices.Length * 2);
        offset += indices.Length * 2;
        Buffer.BlockCopy(batchIds, 0, result, offset, batchIds.Length * 2);
        return result;
    }

    private static byte[] BuildGlb(string json, byte[] bin)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(PadTo4(json));
        int binPadding = (4 - (bin.Length % 4)) % 4;
        int binPaddedLen = bin.Length + binPadding;
        int total = 12 + 8 + jsonBytes.Length + 8 + binPaddedLen;
        byte[] glb = new byte[total];
        Encoding.ASCII.GetBytes("glTF", 0, 4, glb, 0);
        BitConverter.GetBytes(2).CopyTo(glb, 4);
        BitConverter.GetBytes(total).CopyTo(glb, 8);

        int offset = 12;
        BitConverter.GetBytes(jsonBytes.Length).CopyTo(glb, offset);
        Encoding.ASCII.GetBytes("JSON", 0, 4, glb, offset + 4);
        Buffer.BlockCopy(jsonBytes, 0, glb, offset + 8, jsonBytes.Length);
        offset += 8 + jsonBytes.Length;

        BitConverter.GetBytes(binPaddedLen).CopyTo(glb, offset);
        glb[offset + 4] = (byte)'B';
        glb[offset + 5] = (byte)'I';
        glb[offset + 6] = (byte)'N';
        glb[offset + 7] = 0x00;
        Buffer.BlockCopy(bin, 0, glb, offset + 8, bin.Length);
        return glb;
    }

    private static byte[] BuildB3dm(string featureTableJson, string batchTableJson, byte[] gltf)
    {
        byte[] ftj = Encoding.UTF8.GetBytes(PadTo4(featureTableJson));
        byte[] btj = batchTableJson.Length == 0 ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(PadTo4(batchTableJson));
        int total = 28 + ftj.Length + btj.Length + gltf.Length;
        byte[] payload = new byte[total];
        Encoding.ASCII.GetBytes("b3dm", 0, 4, payload, 0);
        BitConverter.GetBytes(1).CopyTo(payload, 4);
        BitConverter.GetBytes(total).CopyTo(payload, 8);
        BitConverter.GetBytes(ftj.Length).CopyTo(payload, 12);
        BitConverter.GetBytes(0).CopyTo(payload, 16);
        BitConverter.GetBytes(btj.Length).CopyTo(payload, 20);
        BitConverter.GetBytes(0).CopyTo(payload, 24);
        int offset = 28;
        Buffer.BlockCopy(ftj, 0, payload, offset, ftj.Length);
        offset += ftj.Length;
        Buffer.BlockCopy(btj, 0, payload, offset, btj.Length);
        offset += btj.Length;
        Buffer.BlockCopy(gltf, 0, payload, offset, gltf.Length);
        return payload;
    }

    private static string PadTo4(string text)
    {
        int padding = (4 - (text.Length % 4)) % 4;
        return text + new string(' ', padding);
    }

    private sealed class FakeHttpClient : IPlateauHttpClient
    {
        public Dictionary<Uri, string> StringResponses { get; } = new Dictionary<Uri, string>();

        public Dictionary<Uri, byte[]> ByteResponses { get; } = new Dictionary<Uri, byte[]>();

        public List<Uri> ByteRequests { get; } = new List<Uri>();

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
        {
            if (StringResponses.TryGetValue(url, out string? body)) return Task.FromResult(body);
            throw new InvalidOperationException("No string response for " + url.AbsoluteUri);
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken)
        {
            ByteRequests.Add(url);
            if (ByteResponses.TryGetValue(url, out byte[]? body)) return Task.FromResult(body);
            throw new InvalidOperationException("No byte response for " + url.AbsoluteUri);
        }

        public Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task DownloadResumableAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken) =>
            throw new NotImplementedException();
    }

    private sealed class IdentityCoordinateTransformer : ICoordinateTransformer
    {
        public ProjectedCoordinate Project(GeographicCoordinate coordinate, CrsReference targetCrs)
        {
            return new ProjectedCoordinate(coordinate.Longitude, coordinate.Latitude);
        }

        public GeographicCoordinate Unproject(ProjectedCoordinate coordinate, CrsReference sourceCrs)
        {
            return new GeographicCoordinate(coordinate.Northing, coordinate.Easting);
        }
    }
}
