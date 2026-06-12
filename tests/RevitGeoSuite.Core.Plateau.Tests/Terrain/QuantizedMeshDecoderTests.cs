using System.IO;
using RevitGeoSuite.Core.Plateau.Terrain;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Terrain;

public sealed class QuantizedMeshDecoderTests
{
    [Fact]
    public void Decode_round_trips_a_synthetic_tile()
    {
        ushort[] u = { 0, 32767, 32767, 0, 16384 };
        ushort[] vv = { 0, 0, 32767, 32767, 16384 };
        ushort[] height = { 0, 8000, 16000, 24000, 32767 };
        int[] indices = { 0, 1, 2, 0, 2, 3, 0, 3, 4 }; // fan from vertex 0 (high-water-mark encodable)
        const float minHeight = 10f;
        const float maxHeight = 110f;

        byte[] encoded = Encode(u, vv, height, indices, minHeight, maxHeight);
        QuantizedMeshTile tile = QuantizedMeshDecoder.Decode(encoded);

        Assert.Equal(5, tile.VertexCount);
        Assert.Equal(3, tile.TriangleCount);
        Assert.Equal(indices, tile.TriangleIndices);

        // Normalized u/v come back as quantized/32767; height interpolates between min and max.
        for (int i = 0; i < u.Length; i++)
        {
            Assert.Equal(u[i] / 32767d, tile.U[i], 9);
            Assert.Equal(vv[i] / 32767d, tile.V[i], 9);
            Assert.Equal(minHeight + (height[i] / 32767d) * (maxHeight - minHeight), tile.HeightMeters[i], 5);
        }
    }

    [Fact]
    public void Decode_throws_on_a_truncated_tile()
    {
        Assert.Throws<InvalidDataException>(() => QuantizedMeshDecoder.Decode(new byte[10]));
    }

    // Minimal quantized-mesh-1.0 encoder for tests (16-bit indices, no extensions).
    private static byte[] Encode(ushort[] u, ushort[] v, ushort[] height, int[] indices, float minHeight, float maxHeight)
    {
        using MemoryStream stream = new MemoryStream();
        using BinaryWriter writer = new BinaryWriter(stream);

        writer.Write(0d); // CenterX
        writer.Write(0d); // CenterY
        writer.Write(0d); // CenterZ
        writer.Write(minHeight);
        writer.Write(maxHeight);
        for (int i = 0; i < 7; i++)
        {
            writer.Write(0d); // bounding sphere (4) + horizon occlusion point (3)
        }

        writer.Write((uint)u.Length);
        WriteZigZagAxis(writer, u);
        WriteZigZagAxis(writer, v);
        WriteZigZagAxis(writer, height);

        // 16-bit indices: align to 2 bytes (already even here) then triangle count + HWM codes.
        while (stream.Position % 2 != 0)
        {
            writer.Write((byte)0);
        }

        writer.Write((uint)(indices.Length / 3));
        int highest = 0;
        foreach (int index in indices)
        {
            int code = highest - index;
            writer.Write((ushort)code);
            if (code == 0)
            {
                highest++;
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void WriteZigZagAxis(BinaryWriter writer, ushort[] values)
    {
        int running = 0;
        foreach (ushort value in values)
        {
            int delta = value - running;
            running = value;
            writer.Write((ushort)((delta << 1) ^ (delta >> 31)));
        }
    }
}
