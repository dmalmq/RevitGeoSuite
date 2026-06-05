using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NetTopologySuite.Geometries;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.Plateau.Mvt;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class MvtFeatureDownloaderTests
{
    private const int Zoom = 16;
    private const int TileX = 58499;
    private const int TileY = 24066;
    private const int Extent = 4096;
    private const string UrlTemplate = "https://tiles.test/{z}/{x}/{y}.mvt";

    [Fact]
    public async Task DownloadAsync_polygon_with_hole_produces_holed_polygon()
    {
        // Exterior square + a reversed-winding inner square (a courtyard hole), well inside the tile.
        int[][] exterior = { new[] { 410, 410 }, new[] { 3686, 410 }, new[] { 3686, 3686 }, new[] { 410, 3686 } };
        int[][] hole = { new[] { 1600, 1600 }, new[] { 1600, 2400 }, new[] { 2400, 2400 }, new[] { 2400, 1600 } };
        byte[] tile = MvtTestEncoder.BuildTile("LandUse", Extent, MvtGeometryType.Polygon, new[] { exterior, hole });

        MvtFeatureDownloader downloader = new MvtFeatureDownloader(StubClient(tile));
        List<string> warnings = new List<string>();

        MvtProjectedFeatures result = await downloader.DownloadAsync(
            TileJson(), new[] { TileBounds(margin: 0.002) }, IdentityTransformer(), warnings);

        Polygon polygon = Assert.IsType<Polygon>(result.PolygonArea);
        Assert.Equal(1, polygon.NumInteriorRings);
        Assert.True(polygon.Area > 0d);
    }

    [Fact]
    public async Task DownloadAsync_decodes_linestring()
    {
        int[][] line = { new[] { 500, 500 }, new[] { 3500, 500 }, new[] { 3500, 3500 } };
        byte[] tile = MvtTestEncoder.BuildTile("Road", Extent, MvtGeometryType.LineString, new[] { line });

        MvtFeatureDownloader downloader = new MvtFeatureDownloader(StubClient(tile));
        List<string> warnings = new List<string>();

        MvtProjectedFeatures result = await downloader.DownloadAsync(
            TileJson(), new[] { TileBounds(margin: 0.002) }, IdentityTransformer(), warnings);

        Assert.NotEmpty(result.Lines);
        Assert.All(result.Lines, line => Assert.True(line.NumPoints >= 2));
    }

    [Fact]
    public async Task DownloadAsync_skips_missing_tiles_without_throwing()
    {
        // Empty stub → every tile request 404s; the download completes empty.
        MvtFeatureDownloader downloader = new MvtFeatureDownloader(StubClient(tiles: null));
        List<string> warnings = new List<string>();

        MvtProjectedFeatures result = await downloader.DownloadAsync(
            TileJson(), new[] { TileBounds(margin: 0.002) }, IdentityTransformer(), warnings);

        Assert.True(result.IsEmpty);
    }

    private static MvtTileJson TileJson() =>
        new MvtTileJson(new[] { UrlTemplate }, minZoom: 10, maxZoom: 16, bounds: null, vectorLayerIds: new[] { "Road" });

    private static MvtGridBounds TileBounds(double margin)
    {
        (double lonW, double latN) = WebMercatorTileMath.TileLocalToLonLat(Zoom, TileX, TileY, 0, 0, Extent);
        (double lonE, double latS) = WebMercatorTileMath.TileLocalToLonLat(Zoom, TileX, TileY, Extent, Extent, Extent);
        return new MvtGridBounds(lonW - margin, latS - margin, lonE + margin, latN + margin);
    }

    // Identity placement: project lon/lat straight to internal metres (degrees treated as metres) with an
    // identity basis, so the decoded geometry is easy to reason about in tests.
    private static EcefToProjectTransformer IdentityTransformer() =>
        new EcefToProjectTransformer(
            new IdentityCoordinateTransformer(),
            new CrsReference(),
            new ProjectedCoordinate(0, 0),
            anchorElevationMeters: 0,
            anchorXFeet: 0,
            anchorYFeet: 0,
            anchorZFeet: 0,
            sharedEastToLocalX: 1,
            sharedEastToLocalY: 0,
            sharedNorthToLocalX: 0,
            sharedNorthToLocalY: 1);

    private static StubHttpClient StubClient(byte[]? tiles)
    {
        Dictionary<string, byte[]> map = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (tiles is not null)
        {
            string url = UrlTemplate
                .Replace("{z}", Zoom.ToString())
                .Replace("{x}", TileX.ToString())
                .Replace("{y}", TileY.ToString());
            map[url] = tiles;
        }

        return new StubHttpClient(map);
    }

    private sealed class IdentityCoordinateTransformer : ICoordinateTransformer
    {
        public ProjectedCoordinate Project(GeographicCoordinate coordinate, CrsReference targetCrs)
            => new ProjectedCoordinate(coordinate.Longitude, coordinate.Latitude);

        public GeographicCoordinate Unproject(ProjectedCoordinate coordinate, CrsReference sourceCrs)
            => new GeographicCoordinate(coordinate.Northing, coordinate.Easting);
    }

    private sealed class StubHttpClient : IPlateauHttpClient
    {
        private readonly IReadOnlyDictionary<string, byte[]> tiles;

        public StubHttpClient(IReadOnlyDictionary<string, byte[]> tiles)
        {
            this.tiles = tiles;
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken)
        {
            if (tiles.TryGetValue(url.AbsoluteUri, out byte[]? bytes))
            {
                return Task.FromResult(bytes);
            }

            throw new HttpRequestException("404 (no tile)");
        }

        public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}

/// <summary>Minimal MVT (protobuf) encoder for tests: rings → command stream → Tile/Layer/Feature bytes.</summary>
internal static class MvtTestEncoder
{
    public static byte[] BuildTile(string layerName, uint extent, MvtGeometryType type, int[][][] rings)
    {
        uint[] geometry = EncodeGeometry(type, rings);

        ProtoWriter feature = new ProtoWriter();
        feature.WriteVarintField(3, (ulong)(int)type);
        feature.WritePackedField(4, geometry);

        ProtoWriter layer = new ProtoWriter();
        layer.WriteVarintField(15, 2);
        layer.WriteStringField(1, layerName);
        layer.WriteMessageField(2, feature.ToArray());
        layer.WriteVarintField(5, extent);

        ProtoWriter tile = new ProtoWriter();
        tile.WriteMessageField(3, layer.ToArray());
        return tile.ToArray();
    }

    private static uint[] EncodeGeometry(MvtGeometryType type, int[][][] rings)
    {
        List<uint> commands = new List<uint>();
        int cursorX = 0;
        int cursorY = 0;
        bool polygon = type == MvtGeometryType.Polygon;

        foreach (int[][] ring in rings)
        {
            int[] first = ring[0];
            commands.Add((1u) | (1u << 3)); // MoveTo, count 1
            commands.Add(ZigZag(first[0] - cursorX));
            commands.Add(ZigZag(first[1] - cursorY));
            cursorX = first[0];
            cursorY = first[1];

            int lineToCount = ring.Length - 1;
            commands.Add((2u) | ((uint)lineToCount << 3)); // LineTo
            for (int i = 1; i < ring.Length; i++)
            {
                commands.Add(ZigZag(ring[i][0] - cursorX));
                commands.Add(ZigZag(ring[i][1] - cursorY));
                cursorX = ring[i][0];
                cursorY = ring[i][1];
            }

            if (polygon)
            {
                commands.Add((7u) | (1u << 3)); // ClosePath
            }
        }

        return commands.ToArray();
    }

    private static uint ZigZag(int value) => (uint)((value << 1) ^ (value >> 31));

    private sealed class ProtoWriter
    {
        private readonly List<byte> bytes = new List<byte>();

        public void WriteVarintField(int field, ulong value)
        {
            WriteTag(field, 0);
            WriteVarint(value);
        }

        public void WriteStringField(int field, string value)
        {
            byte[] payload = Encoding.UTF8.GetBytes(value);
            WriteTag(field, 2);
            WriteVarint((ulong)payload.Length);
            bytes.AddRange(payload);
        }

        public void WriteMessageField(int field, byte[] payload)
        {
            WriteTag(field, 2);
            WriteVarint((ulong)payload.Length);
            bytes.AddRange(payload);
        }

        public void WritePackedField(int field, uint[] values)
        {
            ProtoWriter packed = new ProtoWriter();
            foreach (uint value in values)
            {
                packed.WriteVarint(value);
            }

            WriteMessageField(field, packed.ToArray());
        }

        public byte[] ToArray() => bytes.ToArray();

        private void WriteTag(int field, int wireType) => WriteVarint((ulong)((field << 3) | wireType));

        private void WriteVarint(ulong value)
        {
            while (value >= 0x80)
            {
                bytes.Add((byte)(value | 0x80));
                value >>= 7;
            }

            bytes.Add((byte)value);
        }
    }
}
