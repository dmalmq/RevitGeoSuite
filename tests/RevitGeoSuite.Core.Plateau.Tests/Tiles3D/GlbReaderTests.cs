using System;
using System.Text;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Tiles3D;

public sealed class GlbReaderTests
{
    [Fact]
    public void Read_extracts_json_and_binary_chunks()
    {
        string json = "{\"asset\":{\"version\":\"2.0\"}}";
        byte[] bin = new byte[] { 0x10, 0x20, 0x30, 0x40 };
        byte[] glb = BuildGlb(json, bin);

        GlbContents contents = GlbReader.Read(glb);

        Assert.Equal(json.TrimEnd(), contents.Json);
        Assert.Equal(bin, contents.Binary);
    }

    [Fact]
    public void Read_handles_missing_binary_chunk()
    {
        string json = "{\"asset\":{\"version\":\"2.0\"}}";
        byte[] glb = BuildGlb(json, null);
        GlbContents contents = GlbReader.Read(glb);
        Assert.Equal(json, contents.Json);
        Assert.Empty(contents.Binary);
    }

    [Fact]
    public void Read_rejects_wrong_magic()
    {
        byte[] glb = new byte[12];
        Encoding.ASCII.GetBytes("XYZW", 0, 4, glb, 0);
        BitConverter.GetBytes(2).CopyTo(glb, 4);
        BitConverter.GetBytes(12).CopyTo(glb, 8);
        Assert.Throws<InvalidOperationException>(() => GlbReader.Read(glb));
    }

    [Theory]
    [InlineData(-8)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    public void Read_rejects_negative_or_overflow_unknown_chunk_length_without_hanging(int badChunkLength)
    {
        byte[] glb = new byte[12 + 8 + 4];
        Encoding.ASCII.GetBytes("glTF", 0, 4, glb, 0);
        BitConverter.GetBytes(2).CopyTo(glb, 4);
        BitConverter.GetBytes(glb.Length).CopyTo(glb, 8);
        BitConverter.GetBytes(badChunkLength).CopyTo(glb, 12);
        Encoding.ASCII.GetBytes("UNKN", 0, 4, glb, 16);

        Assert.Throws<InvalidOperationException>(() => GlbReader.Read(glb));
    }

    [Fact]
    public void Read_rejects_zero_length_json_chunk()
    {
        byte[] glb = new byte[12 + 8];
        Encoding.ASCII.GetBytes("glTF", 0, 4, glb, 0);
        BitConverter.GetBytes(2).CopyTo(glb, 4);
        BitConverter.GetBytes(glb.Length).CopyTo(glb, 8);
        BitConverter.GetBytes(0).CopyTo(glb, 12);
        Encoding.ASCII.GetBytes("JSON", 0, 4, glb, 16);

        Assert.Throws<InvalidOperationException>(() => GlbReader.Read(glb));
    }

    [Fact]
    public void Read_allows_zero_length_binary_chunk()
    {
        string json = "{\"asset\":{\"version\":\"2.0\"}}";
        byte[] glb = BuildGlb(json, Array.Empty<byte>());

        GlbContents contents = GlbReader.Read(glb);

        Assert.Equal(json, contents.Json);
        Assert.Empty(contents.Binary);
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(0)]
    [InlineData(8)]
    public void Read_rejects_negative_or_short_total_length(int badTotalLength)
    {
        byte[] glb = new byte[24];
        Encoding.ASCII.GetBytes("glTF", 0, 4, glb, 0);
        BitConverter.GetBytes(2).CopyTo(glb, 4);
        BitConverter.GetBytes(badTotalLength).CopyTo(glb, 8);

        Assert.Throws<InvalidOperationException>(() => GlbReader.Read(glb));
    }

    private static byte[] BuildGlb(string json, byte[]? bin)
    {
        byte[] jsonBytes = Encoding.UTF8.GetBytes(PadTo4(json));
        int total = 12 + 8 + jsonBytes.Length + (bin is null ? 0 : 8 + bin.Length);
        byte[] glb = new byte[total];
        Encoding.ASCII.GetBytes("glTF", 0, 4, glb, 0);
        BitConverter.GetBytes(2).CopyTo(glb, 4);
        BitConverter.GetBytes(total).CopyTo(glb, 8);

        int offset = 12;
        BitConverter.GetBytes(jsonBytes.Length).CopyTo(glb, offset);
        Encoding.ASCII.GetBytes("JSON", 0, 4, glb, offset + 4);
        Buffer.BlockCopy(jsonBytes, 0, glb, offset + 8, jsonBytes.Length);
        offset += 8 + jsonBytes.Length;

        if (bin is not null)
        {
            BitConverter.GetBytes(bin.Length).CopyTo(glb, offset);
            // "BIN\0" — but no trailing null in ASCII.GetBytes, so write directly.
            glb[offset + 4] = (byte)'B';
            glb[offset + 5] = (byte)'I';
            glb[offset + 6] = (byte)'N';
            glb[offset + 7] = 0x00;
            Buffer.BlockCopy(bin, 0, glb, offset + 8, bin.Length);
        }
        return glb;
    }

    private static string PadTo4(string text)
    {
        int padding = (4 - (text.Length % 4)) % 4;
        return text + new string(' ', padding);
    }
}
