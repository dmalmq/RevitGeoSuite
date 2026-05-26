using System;
using System.Text;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Tiles3D;

public sealed class B3dmParserTests
{
    [Fact]
    public void Parse_extracts_feature_and_batch_tables_and_gltf_body()
    {
        string featureTableJson = "{\"BATCH_LENGTH\":3,\"RTC_CENTER\":[10,20,30]}";
        string batchTableJson = "{\"gml_id\":[\"a\",\"b\",\"c\"]}";
        byte[] gltf = Encoding.ASCII.GetBytes("glTFFAKE_BODY_BYTES");

        byte[] payload = BuildB3dm(featureTableJson, Array.Empty<byte>(), batchTableJson, Array.Empty<byte>(), gltf);

        B3dmContents contents = B3dmParser.Parse(payload);

        Assert.Equal(3, contents.BatchLength);
        Vector3d? rtc = contents.RtcCenter;
        Assert.True(rtc.HasValue);
        Assert.Equal(10, rtc!.Value.X);
        Assert.Equal(20, rtc.Value.Y);
        Assert.Equal(30, rtc.Value.Z);
        Assert.Equal("a", contents.BatchTableJson["gml_id"]![0]!.ToString());
        Assert.Equal(gltf, contents.GltfBytes);
    }

    [Fact]
    public void Parse_rejects_unknown_magic()
    {
        byte[] bad = new byte[28];
        Encoding.ASCII.GetBytes("BADM", 0, 4, bad, 0);
        Assert.Throws<InvalidOperationException>(() => B3dmParser.Parse(bad));
    }

    [Fact]
    public void Parse_handles_empty_batch_table()
    {
        byte[] gltf = new byte[] { 0x67, 0x6c, 0x54, 0x46 };
        byte[] payload = BuildB3dm("{\"BATCH_LENGTH\":0}", Array.Empty<byte>(), string.Empty, Array.Empty<byte>(), gltf);

        B3dmContents contents = B3dmParser.Parse(payload);

        Assert.Equal(0, contents.BatchLength);
        Assert.Equal(gltf, contents.GltfBytes);
        Assert.Empty(contents.BatchTableJson);
    }

    private static byte[] BuildB3dm(string featureTableJson, byte[] featureTableBinary, string batchTableJson, byte[] batchTableBinary, byte[] gltf)
    {
        byte[] featureTableJsonBytes = Encoding.UTF8.GetBytes(PadTo4(featureTableJson));
        byte[] batchTableJsonBytes = batchTableJson.Length == 0 ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(PadTo4(batchTableJson));

        int total = 28 + featureTableJsonBytes.Length + featureTableBinary.Length + batchTableJsonBytes.Length + batchTableBinary.Length + gltf.Length;
        byte[] payload = new byte[total];
        Encoding.ASCII.GetBytes("b3dm", 0, 4, payload, 0);
        BitConverter.GetBytes(1).CopyTo(payload, 4);
        BitConverter.GetBytes(total).CopyTo(payload, 8);
        BitConverter.GetBytes(featureTableJsonBytes.Length).CopyTo(payload, 12);
        BitConverter.GetBytes(featureTableBinary.Length).CopyTo(payload, 16);
        BitConverter.GetBytes(batchTableJsonBytes.Length).CopyTo(payload, 20);
        BitConverter.GetBytes(batchTableBinary.Length).CopyTo(payload, 24);

        int offset = 28;
        Buffer.BlockCopy(featureTableJsonBytes, 0, payload, offset, featureTableJsonBytes.Length);
        offset += featureTableJsonBytes.Length;
        Buffer.BlockCopy(featureTableBinary, 0, payload, offset, featureTableBinary.Length);
        offset += featureTableBinary.Length;
        Buffer.BlockCopy(batchTableJsonBytes, 0, payload, offset, batchTableJsonBytes.Length);
        offset += batchTableJsonBytes.Length;
        Buffer.BlockCopy(batchTableBinary, 0, payload, offset, batchTableBinary.Length);
        offset += batchTableBinary.Length;
        Buffer.BlockCopy(gltf, 0, payload, offset, gltf.Length);

        return payload;
    }

    private static string PadTo4(string text)
    {
        int padding = (4 - (text.Length % 4)) % 4;
        return text + new string(' ', padding);
    }
}
