using System.Collections.Generic;
using System.IO;
using System.Text;
using RevitGeoSuite.Core.Plateau.Mvt;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Mvt;

public sealed class MapboxVectorTileReaderTests
{
    // Canonical geometry examples from the Mapbox Vector Tile spec.

    [Fact]
    public void DecodeGeometry_decodes_single_point()
    {
        // MoveTo(25,17): [9, 50, 34]
        List<List<MvtPoint>> paths = MapboxVectorTileReader.DecodeGeometry(new uint[] { 9, 50, 34 });

        List<MvtPoint> path = Assert.Single(paths);
        MvtPoint point = Assert.Single(path);
        Assert.Equal(25, point.X);
        Assert.Equal(17, point.Y);
    }

    [Fact]
    public void DecodeGeometry_decodes_linestring()
    {
        // MoveTo(2,2) + LineTo (2,10),(10,10): [9,4,4, 18,0,16,16,0]
        List<List<MvtPoint>> paths = MapboxVectorTileReader.DecodeGeometry(new uint[] { 9, 4, 4, 18, 0, 16, 16, 0 });

        List<MvtPoint> path = Assert.Single(paths);
        Assert.Equal(3, path.Count);
        Assert.Equal((2, 2), (path[0].X, path[0].Y));
        Assert.Equal((2, 10), (path[1].X, path[1].Y));
        Assert.Equal((10, 10), (path[2].X, path[2].Y));
    }

    [Fact]
    public void DecodeGeometry_decodes_closed_polygon_ring()
    {
        // MoveTo(3,6) + LineTo (8,12),(20,34) + ClosePath: [9,6,12, 18,10,12,24,44, 15]
        List<List<MvtPoint>> paths = MapboxVectorTileReader.DecodeGeometry(new uint[] { 9, 6, 12, 18, 10, 12, 24, 44, 15 });

        List<MvtPoint> ring = Assert.Single(paths);
        Assert.Equal(4, ring.Count); // ClosePath appends the first point
        Assert.Equal((3, 6), (ring[0].X, ring[0].Y));
        Assert.Equal((8, 12), (ring[1].X, ring[1].Y));
        Assert.Equal((20, 34), (ring[2].X, ring[2].Y));
        Assert.Equal((3, 6), (ring[3].X, ring[3].Y));
    }

    [Fact]
    public void Read_parses_layer_feature_and_geometry()
    {
        byte[] tile = BuildSinglePolygonTile("Road", extent: 4096, new uint[] { 9, 6, 12, 18, 10, 12, 24, 44, 15 });

        IReadOnlyList<MvtLayer> layers = MapboxVectorTileReader.Read(tile);

        MvtLayer layer = Assert.Single(layers);
        Assert.Equal("Road", layer.Name);
        Assert.Equal(4096u, layer.Extent);

        MvtFeature feature = Assert.Single(layer.Features);
        Assert.Equal(MvtGeometryType.Polygon, feature.GeometryType);
        IReadOnlyList<MvtPoint> ring = Assert.Single(feature.Paths);
        Assert.Equal(4, ring.Count);
        Assert.Equal((3, 6), (ring[0].X, ring[0].Y));
    }

    // --- minimal MVT protobuf encoder for the framing test ---

    private static byte[] BuildSinglePolygonTile(string layerName, uint extent, uint[] geometryCommands)
    {
        ProtoWriter feature = new ProtoWriter();
        feature.WriteVarintField(3, 3);                 // type = POLYGON
        feature.WritePackedField(4, geometryCommands);  // geometry

        ProtoWriter layer = new ProtoWriter();
        layer.WriteVarintField(15, 2);                  // version
        layer.WriteStringField(1, layerName);           // name
        layer.WriteMessageField(2, feature.ToArray());  // features
        layer.WriteVarintField(5, extent);              // extent

        ProtoWriter tile = new ProtoWriter();
        tile.WriteMessageField(3, layer.ToArray());     // layers
        return tile.ToArray();
    }

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
