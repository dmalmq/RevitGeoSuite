using System;
using System.Text;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public sealed class GlbContents
{
    public GlbContents(string json, byte[] binary)
    {
        Json = json;
        Binary = binary;
    }

    public string Json { get; }

    public byte[] Binary { get; }
}

/// <summary>
/// Parses a glTF 2.0 binary container (GLB). Used downstream of <see cref="B3dmParser"/> on
/// the embedded glTF chunk of a .b3dm payload.
/// </summary>
public static class GlbReader
{
    public static GlbContents Read(byte[] glb)
    {
        if (glb is null) throw new ArgumentNullException(nameof(glb));
        if (glb.Length < 12) throw new InvalidOperationException("glTF binary header is shorter than 12 bytes.");

        string magic = Encoding.ASCII.GetString(glb, 0, 4);
        if (magic != "glTF") throw new InvalidOperationException($"Unexpected glTF magic '{magic}'.");

        int version = BitConverter.ToInt32(glb, 4);
        if (version != 2) throw new InvalidOperationException($"Unsupported glTF version {version}; expected 2.");

        int totalLength = BitConverter.ToInt32(glb, 8);
        if (totalLength > glb.Length) throw new InvalidOperationException("glTF declared length exceeds payload.");

        int offset = 12;
        string? json = null;
        byte[] binary = Array.Empty<byte>();

        while (offset + 8 <= totalLength)
        {
            int chunkLength = BitConverter.ToInt32(glb, offset);
            string chunkType = Encoding.ASCII.GetString(glb, offset + 4, 4);
            int chunkStart = offset + 8;

            if (chunkStart + chunkLength > totalLength)
            {
                throw new InvalidOperationException("glTF chunk exceeds declared length.");
            }

            switch (chunkType)
            {
                case "JSON":
                    json = Encoding.UTF8.GetString(glb, chunkStart, chunkLength).TrimEnd();
                    break;
                case "BIN\0":
                    binary = new byte[chunkLength];
                    Buffer.BlockCopy(glb, chunkStart, binary, 0, chunkLength);
                    break;
                // Unknown chunks are skipped per the glTF spec.
            }

            offset = chunkStart + chunkLength;
        }

        if (json is null) throw new InvalidOperationException("glTF JSON chunk was missing.");
        return new GlbContents(json, binary);
    }
}
