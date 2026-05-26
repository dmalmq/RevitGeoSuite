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

        int version = BitConverter.ToInt32(bytes, 4);
        if (version != 1) throw new InvalidOperationException($"Unsupported b3dm version {version}.");

        int byteLength = BitConverter.ToInt32(bytes, 8);
        if (byteLength > bytes.Length) throw new InvalidOperationException("b3dm byteLength exceeds available payload.");

        int featureTableJsonLength = BitConverter.ToInt32(bytes, 12);
        int featureTableBinaryLength = BitConverter.ToInt32(bytes, 16);
        int batchTableJsonLength = BitConverter.ToInt32(bytes, 20);
        int batchTableBinaryLength = BitConverter.ToInt32(bytes, 24);

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
        if (gltfLength < 0) throw new InvalidOperationException("Computed glTF length is negative; b3dm payload is malformed.");
        byte[] gltf = Slice(bytes, offset, gltfLength);

        return new B3dmContents(featureTableJson, featureTableBinary, batchTableJson, batchTableBinary, gltf);
    }

    private static JObject ReadJsonChunk(byte[] bytes, int offset, int length)
    {
        if (length <= 0) return new JObject();
        string text = Encoding.UTF8.GetString(bytes, offset, length).TrimEnd();
        if (string.IsNullOrEmpty(text)) return new JObject();
        return JObject.Parse(text);
    }

    private static byte[] Slice(byte[] source, int offset, int length)
    {
        if (length <= 0) return Array.Empty<byte>();
        byte[] result = new byte[length];
        Buffer.BlockCopy(source, offset, result, 0, length);
        return result;
    }
}
