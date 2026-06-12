using System;
using System.IO;

namespace RevitGeoSuite.Core.Plateau.Terrain;

/// <summary>
/// Decodes the quantized-mesh-1.0 binary terrain format (the tile payload Cesium / PLATEAU-on-Ion
/// serves at <c>{z}/{x}/{y}.terrain</c>). Only the header, the zig-zag-delta-encoded vertex arrays,
/// and the high-water-mark-encoded triangle indices are read — enough to reconstruct the surface for
/// elevation sampling. Trailing edge indices and extension chunks are ignored.
/// </summary>
public static class QuantizedMeshDecoder
{
    private const int HeaderByteLength = 88;
    private const double MaxQuantizedValue = 32767d;

    public static QuantizedMeshTile Decode(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));
        if (data.Length < HeaderByteLength + 4)
        {
            throw new InvalidDataException("Terrain tile is too small to be a quantized-mesh tile.");
        }

        using MemoryStream stream = new MemoryStream(data, writable: false);
        using BinaryReader reader = new BinaryReader(stream);

        // QuantizedMeshHeader: center (3 doubles), min/max height (2 floats), bounding sphere
        // (center 3 doubles + radius double), horizon occlusion point (3 doubles).
        reader.ReadDouble(); // CenterX
        reader.ReadDouble(); // CenterY
        reader.ReadDouble(); // CenterZ
        double minimumHeight = reader.ReadSingle();
        double maximumHeight = reader.ReadSingle();
        for (int i = 0; i < 7; i++)
        {
            reader.ReadDouble(); // bounding sphere (4) + horizon occlusion point (3)
        }

        int vertexCount = checked((int)reader.ReadUInt32());
        if (vertexCount < 0 || vertexCount > (data.Length / 2))
        {
            throw new InvalidDataException($"Quantized-mesh vertex count {vertexCount} is implausible for a {data.Length}-byte tile.");
        }

        double[] u = DecodeZigZagAxis(reader, vertexCount, MaxQuantizedValue);
        double[] v = DecodeZigZagAxis(reader, vertexCount, MaxQuantizedValue);
        double[] heightFraction = DecodeZigZagAxis(reader, vertexCount, MaxQuantizedValue);

        double[] heightMeters = new double[vertexCount];
        double heightRange = maximumHeight - minimumHeight;
        for (int i = 0; i < vertexCount; i++)
        {
            heightMeters[i] = minimumHeight + heightFraction[i] * heightRange;
        }

        int indexByteSize = vertexCount > 65536 ? 4 : 2;
        AlignTo(stream, indexByteSize);

        int triangleCount = checked((int)reader.ReadUInt32());
        long indexBytes = (long)triangleCount * 3 * indexByteSize;
        if (triangleCount < 0 || indexBytes > stream.Length - stream.Position)
        {
            throw new InvalidDataException($"Quantized-mesh triangle count {triangleCount} exceeds the tile's remaining bytes.");
        }

        int[] indices = DecodeHighWaterMarkIndices(reader, triangleCount * 3, indexByteSize);
        return new QuantizedMeshTile(u, v, heightMeters, indices, minimumHeight, maximumHeight);
    }

    private static double[] DecodeZigZagAxis(BinaryReader reader, int count, double maxValue)
    {
        double[] normalized = new double[count];
        int running = 0;
        for (int i = 0; i < count; i++)
        {
            running += DecodeZigZag(reader.ReadUInt16());
            normalized[i] = running / maxValue;
        }

        return normalized;
    }

    private static int DecodeZigZag(ushort value)
    {
        return (value >> 1) ^ -(value & 1);
    }

    private static int[] DecodeHighWaterMarkIndices(BinaryReader reader, int indexCount, int indexByteSize)
    {
        int[] indices = new int[indexCount];
        int highest = 0;
        for (int i = 0; i < indexCount; i++)
        {
            int code = indexByteSize == 4 ? checked((int)reader.ReadUInt32()) : reader.ReadUInt16();
            indices[i] = highest - code;
            if (code == 0)
            {
                highest++;
            }
        }

        return indices;
    }

    private static void AlignTo(Stream stream, int alignment)
    {
        long remainder = stream.Position % alignment;
        if (remainder != 0)
        {
            stream.Position += alignment - remainder;
        }
    }
}
