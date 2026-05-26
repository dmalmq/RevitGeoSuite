using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

/// <summary>
/// Reads per-batch attributes out of a b3dm batch table. The PLATEAU pipeline stores
/// most attributes inline as JSON arrays (one entry per batchId); binary chunks are
/// handled for typed-array references.
/// </summary>
public static class BatchTableReader
{
    public static IReadOnlyDictionary<string, object?> ReadAttributesForBatch(JObject batchTableJson, byte[] batchTableBinary, int batchId, int batchLength)
    {
        Dictionary<string, object?> result = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (batchTableJson is null) return result;

        foreach (var property in batchTableJson.Properties())
        {
            JToken token = property.Value;
            switch (token.Type)
            {
                case JTokenType.Array:
                    JArray array = (JArray)token;
                    if (batchId >= 0 && batchId < array.Count)
                    {
                        result[property.Name] = ConvertToken(array[batchId]);
                    }
                    break;
                case JTokenType.Object:
                    object? value = ReadBinaryReference((JObject)token, batchTableBinary, batchId, batchLength);
                    if (value is not null) result[property.Name] = value;
                    break;
                default:
                    result[property.Name] = ConvertToken(token);
                    break;
            }
        }
        return result;
    }

    private static object? ConvertToken(JToken token) => token.Type switch
    {
        JTokenType.Integer => (long)token,
        JTokenType.Float => (double)token,
        JTokenType.Boolean => (bool)token,
        JTokenType.Null => null,
        _ => token.ToString()
    };

    private static object? ReadBinaryReference(JObject reference, byte[] binary, int batchId, int batchLength)
    {
        int byteOffset = reference["byteOffset"]?.Value<int>() ?? 0;
        string componentType = reference["componentType"]?.Value<string>() ?? string.Empty;
        string accessorType = reference["type"]?.Value<string>() ?? "SCALAR";
        int components = accessorType switch
        {
            "VEC2" => 2,
            "VEC3" => 3,
            "VEC4" => 4,
            _ => 1,
        };

        if (batchId < 0 || batchId >= batchLength) return null;
        int elementSize = SizeOfComponent(componentType) * components;
        if (elementSize <= 0) return null;
        int start = byteOffset + batchId * elementSize;
        if (start + elementSize > binary.Length) return null;

        switch (componentType)
        {
            case "BYTE": return (sbyte)binary[start];
            case "UNSIGNED_BYTE": return binary[start];
            case "SHORT": return BitConverter.ToInt16(binary, start);
            case "UNSIGNED_SHORT": return BitConverter.ToUInt16(binary, start);
            case "INT": return BitConverter.ToInt32(binary, start);
            case "UNSIGNED_INT": return BitConverter.ToUInt32(binary, start);
            case "FLOAT": return BitConverter.ToSingle(binary, start);
            case "DOUBLE": return BitConverter.ToDouble(binary, start);
        }
        return null;
    }

    private static int SizeOfComponent(string componentType) => componentType switch
    {
        "BYTE" or "UNSIGNED_BYTE" => 1,
        "SHORT" or "UNSIGNED_SHORT" => 2,
        "INT" or "UNSIGNED_INT" or "FLOAT" => 4,
        "DOUBLE" => 8,
        _ => 0,
    };
}
