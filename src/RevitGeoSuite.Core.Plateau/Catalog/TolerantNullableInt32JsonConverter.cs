using System;
using System.Globalization;
using Newtonsoft.Json;

namespace RevitGeoSuite.Core.Plateau.Catalog;

internal sealed class TolerantNullableInt32JsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType) => objectType == typeof(int?) || objectType == typeof(int);

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        switch (reader.TokenType)
        {
            case JsonToken.Null:
                return null;
            case JsonToken.Integer:
                return Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
            case JsonToken.Float:
                return Convert.ToInt32(reader.Value, CultureInfo.InvariantCulture);
            case JsonToken.String:
                string? text = reader.Value as string;
                return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
            default:
                return null;
        }
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is int i) writer.WriteValue(i);
        else writer.WriteNull();
    }
}
