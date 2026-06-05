using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace RevitGeoSuite.Core.Plateau.Mvt;

/// <summary>
/// Hand-rolled decoder for Mapbox Vector Tiles (MVT 2.1 protobuf). Follows the same "write our own
/// binary parser" approach as <c>B3dmParser</c>/<c>GlbReader</c>/<c>GltfDocument</c> so no protobuf
/// NuGet (and no extra net48 dependency) is needed. Decodes layers → features → geometry paths in
/// tile-local integer coordinates; feature attributes/tags are intentionally skipped (the basemap
/// imports every feature of a layer). Convert tile-local coords to lon/lat with
/// <see cref="WebMercatorTileMath.TileLocalToLonLat"/>.
/// </summary>
public static class MapboxVectorTileReader
{
    private const int FieldTileLayer = 3;

    private const int FieldLayerName = 1;
    private const int FieldLayerFeatures = 2;
    private const int FieldLayerExtent = 5;

    private const int FieldFeatureType = 3;
    private const int FieldFeatureGeometry = 4;

    private const int CommandMoveTo = 1;
    private const int CommandLineTo = 2;
    private const int CommandClosePath = 7;

    private const uint DefaultExtent = 4096;

    /// <summary>Decodes a raw (uncompressed) MVT tile into its layers.</summary>
    public static IReadOnlyList<MvtLayer> Read(byte[] tileBytes)
    {
        if (tileBytes is null) throw new ArgumentNullException(nameof(tileBytes));

        List<MvtLayer> layers = new List<MvtLayer>();
        ProtoReader reader = new ProtoReader(tileBytes, 0, tileBytes.Length);
        while (reader.HasMore)
        {
            reader.ReadTag(out int field, out int wireType);
            if (field == FieldTileLayer && wireType == ProtoReader.WireLengthDelimited)
            {
                ProtoReader layerReader = reader.ReadSubReader();
                layers.Add(ReadLayer(layerReader));
            }
            else
            {
                reader.SkipField(wireType);
            }
        }

        return layers;
    }

    private static MvtLayer ReadLayer(ProtoReader reader)
    {
        string name = string.Empty;
        uint extent = DefaultExtent;
        List<MvtFeature> features = new List<MvtFeature>();

        while (reader.HasMore)
        {
            reader.ReadTag(out int field, out int wireType);
            switch (field)
            {
                case FieldLayerName when wireType == ProtoReader.WireLengthDelimited:
                    name = reader.ReadString();
                    break;
                case FieldLayerExtent when wireType == ProtoReader.WireVarint:
                    extent = (uint)reader.ReadVarint();
                    break;
                case FieldLayerFeatures when wireType == ProtoReader.WireLengthDelimited:
                    features.Add(ReadFeature(reader.ReadSubReader()));
                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        if (extent == 0)
        {
            extent = DefaultExtent;
        }

        return new MvtLayer(name, extent, features);
    }

    private static MvtFeature ReadFeature(ProtoReader reader)
    {
        MvtGeometryType type = MvtGeometryType.Unknown;
        List<IReadOnlyList<MvtPoint>> paths = new List<IReadOnlyList<MvtPoint>>();

        while (reader.HasMore)
        {
            reader.ReadTag(out int field, out int wireType);
            switch (field)
            {
                case FieldFeatureType when wireType == ProtoReader.WireVarint:
                    type = (MvtGeometryType)(int)reader.ReadVarint();
                    break;
                case FieldFeatureGeometry when wireType == ProtoReader.WireLengthDelimited:
                    ProtoReader geometryReader = reader.ReadSubReader();
                    List<uint> commands = new List<uint>();
                    while (geometryReader.HasMore)
                    {
                        commands.Add((uint)geometryReader.ReadVarint());
                    }

                    foreach (List<MvtPoint> path in DecodeGeometry(commands))
                    {
                        paths.Add(path);
                    }

                    break;
                default:
                    reader.SkipField(wireType);
                    break;
            }
        }

        return new MvtFeature(type, paths);
    }

    /// <summary>
    /// Decodes the MVT geometry command stream (MoveTo/LineTo/ClosePath with zig-zag deltas) into
    /// paths of tile-local points. A MoveTo starts a new path; ClosePath appends the path's first
    /// point so polygon rings come back explicitly closed (first == last). Exposed for unit testing.
    /// </summary>
    public static List<List<MvtPoint>> DecodeGeometry(IReadOnlyList<uint> commands)
    {
        if (commands is null) throw new ArgumentNullException(nameof(commands));

        List<List<MvtPoint>> paths = new List<List<MvtPoint>>();
        List<MvtPoint>? current = null;
        int cursorX = 0;
        int cursorY = 0;
        int index = 0;

        while (index < commands.Count)
        {
            uint commandInteger = commands[index++];
            int commandId = (int)(commandInteger & 0x7);
            int count = (int)(commandInteger >> 3);

            if (commandId == CommandMoveTo)
            {
                for (int k = 0; k < count; k++)
                {
                    if (index + 1 >= commands.Count)
                    {
                        return paths;
                    }

                    cursorX += DecodeZigZag(commands[index++]);
                    cursorY += DecodeZigZag(commands[index++]);
                    current = new List<MvtPoint> { new MvtPoint(cursorX, cursorY) };
                    paths.Add(current);
                }
            }
            else if (commandId == CommandLineTo)
            {
                for (int k = 0; k < count; k++)
                {
                    if (index + 1 >= commands.Count)
                    {
                        return paths;
                    }

                    cursorX += DecodeZigZag(commands[index++]);
                    cursorY += DecodeZigZag(commands[index++]);
                    current?.Add(new MvtPoint(cursorX, cursorY));
                }
            }
            else if (commandId == CommandClosePath)
            {
                if (current != null && current.Count > 0)
                {
                    current.Add(current[0]);
                }
            }
            else
            {
                break;
            }
        }

        return paths;
    }

    private static int DecodeZigZag(uint value)
    {
        return (int)(value >> 1) ^ -(int)(value & 1);
    }

    /// <summary>Minimal protobuf reader over a byte-range slice (varint, length-delimited, skip).</summary>
    private sealed class ProtoReader
    {
        public const int WireVarint = 0;
        public const int WireFixed64 = 1;
        public const int WireLengthDelimited = 2;
        public const int WireFixed32 = 5;

        private readonly byte[] buffer;
        private readonly int end;
        private int position;

        public ProtoReader(byte[] buffer, int start, int end)
        {
            this.buffer = buffer;
            position = start;
            this.end = end;
        }

        public bool HasMore => position < end;

        public void ReadTag(out int field, out int wireType)
        {
            ulong tag = ReadVarint();
            field = (int)(tag >> 3);
            wireType = (int)(tag & 0x7);
        }

        public ulong ReadVarint()
        {
            ulong result = 0;
            int shift = 0;
            while (true)
            {
                if (position >= end)
                {
                    throw new InvalidDataException("Truncated varint in MVT tile.");
                }

                byte b = buffer[position++];
                result |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    break;
                }

                shift += 7;
                if (shift > 63)
                {
                    throw new InvalidDataException("Varint too long in MVT tile.");
                }
            }

            return result;
        }

        public string ReadString()
        {
            int length = (int)ReadVarint();
            EnsureAvailable(length);
            string value = Encoding.UTF8.GetString(buffer, position, length);
            position += length;
            return value;
        }

        public ProtoReader ReadSubReader()
        {
            int length = (int)ReadVarint();
            EnsureAvailable(length);
            ProtoReader sub = new ProtoReader(buffer, position, position + length);
            position += length;
            return sub;
        }

        public void SkipField(int wireType)
        {
            switch (wireType)
            {
                case WireVarint:
                    ReadVarint();
                    break;
                case WireFixed64:
                    Advance(8);
                    break;
                case WireLengthDelimited:
                    int length = (int)ReadVarint();
                    Advance(length);
                    break;
                case WireFixed32:
                    Advance(4);
                    break;
                default:
                    throw new InvalidDataException($"Unsupported MVT protobuf wire type {wireType}.");
            }
        }

        private void Advance(int count)
        {
            EnsureAvailable(count);
            position += count;
        }

        private void EnsureAvailable(int count)
        {
            if (count < 0 || position + count > end)
            {
                throw new InvalidDataException("MVT tile field overruns its bounds.");
            }
        }
    }
}
