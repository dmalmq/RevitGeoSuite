using System;
using System.Text;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

public sealed class B3dmContents
{
    public B3dmContents(JObject featureTableJson, byte[] featureTableBinary, JObject batchTableJson, byte[] batchTableBinary, byte[] gltfBytes)
    {
        FeatureTableJson = featureTableJson;
        FeatureTableBinary = featureTableBinary;
        BatchTableJson = batchTableJson;
        BatchTableBinary = batchTableBinary;
        GltfBytes = gltfBytes;
    }

    public JObject FeatureTableJson { get; }

    public byte[] FeatureTableBinary { get; }

    public JObject BatchTableJson { get; }

    public byte[] BatchTableBinary { get; }

    public byte[] GltfBytes { get; }

    public int? BatchLength
    {
        get
        {
            JToken? token = FeatureTableJson["BATCH_LENGTH"];
            if (token is null) return null;
            return token.Type switch
            {
                JTokenType.Integer => (int)token,
                _ => null
            };
        }
    }

    public Vector3d? RtcCenter
    {
        get
        {
            JToken? token = FeatureTableJson["RTC_CENTER"];
            if (token is JArray arr && arr.Count == 3)
            {
                return new Vector3d((double)arr[0], (double)arr[1], (double)arr[2]);
            }
            return null;
        }
    }
}

public static class B3dmParser
{
    private const int HeaderSize = 28;

    public static B3dmContents Parse(byte[] bytes)
    {
        if (bytes is null) throw new ArgumentNullException(nameof(bytes));
        if (bytes.Length < HeaderSize) throw new InvalidOperationException("b3dm payload is shorter than the header.");

        string magic = Encoding.ASCII.GetString(bytes, 0, 4);
        if (magic != "b3dm") throw new InvalidOperationException($"Unexpected b3dm magic '{magic}'.");

        uint version = BitConverter.ToUInt32(bytes, 4);
        if (version != 1u) throw new InvalidOperationException($"Unsupported b3dm version {version}.");

        uint byteLengthU32 = BitConverter.ToUInt32(bytes, 8);
        if (byteLengthU32 < (uint)HeaderSize || byteLengthU32 > (uint)bytes.Length)
        {
            throw new InvalidOperationException("b3dm byteLength is out of range.");
        }

        uint featureTableJsonLengthU32 = BitConverter.ToUInt32(bytes, 12);
        uint featureTableBinaryLengthU32 = BitConverter.ToUInt32(bytes, 16);
        uint batchTableJsonLengthU32 = BitConverter.ToUInt32(bytes, 20);
        uint batchTableBinaryLengthU32 = BitConverter.ToUInt32(bytes, 24);

        long totalSectionBytes =
            (long)featureTableJsonLengthU32
            + featureTableBinaryLengthU32
            + batchTableJsonLengthU32
            + batchTableBinaryLengthU32;
        if (HeaderSize + totalSectionBytes > byteLengthU32)
        {
            throw new InvalidOperationException("b3dm section lengths exceed declared byteLength.");
        }

        int byteLength = (int)byteLengthU32;
        int featureTableJsonLength = (int)featureTableJsonLengthU32;
        int featureTableBinaryLength = (int)featureTableBinaryLengthU32;
        int batchTableJsonLength = (int)batchTableJsonLengthU32;
        int batchTableBinaryLength = (int)batchTableBinaryLengthU32;

        int offset = HeaderSize;

        JObject featureTableJson = ReadJsonChunk(bytes, offset, featureTableJsonLength);
        offset += featureTableJsonLength;

        byte[] featureTableBinary = Slice(bytes, offset, featureTableBinaryLength);
        offset += featureTableBinaryLength;

        JObject batchTableJson = ReadJsonChunk(bytes, offset, batchTableJsonLength);
        offset += batchTableJsonLength;

        byte[] batchTableBinary = Slice(bytes, offset, batchTableBinaryLength);
        offset += batchTableBinaryLength;

        int gltfLength = byteLength - offset;
        byte[] gltf = Slice(bytes, offset, gltfLength);

        return new B3dmContents(featureTableJson, featureTableBinary, batchTableJson, batchTableBinary, gltf);
    }

    private static JObject ReadJsonChunk(byte[] bytes, int offset, int length)
    {
        if (length < 0 || offset < 0 || (long)offset + length > bytes.Length)
        {
            throw new InvalidOperationException("b3dm JSON chunk bounds are invalid.");
        }

        if (length == 0) return new JObject();
        string text = Encoding.UTF8.GetString(bytes, offset, length).TrimEnd();
        if (string.IsNullOrEmpty(text)) return new JObject();
        return JObject.Parse(text);
    }

    private static byte[] Slice(byte[] source, int offset, int length)
    {
        if (length < 0 || offset < 0 || (long)offset + length > source.Length)
        {
            throw new InvalidOperationException("b3dm binary chunk bounds are invalid.");
        }

        if (length == 0) return Array.Empty<byte>();
        byte[] result = new byte[length];
        Buffer.BlockCopy(source, offset, result, 0, length);
        return result;
    }
}
