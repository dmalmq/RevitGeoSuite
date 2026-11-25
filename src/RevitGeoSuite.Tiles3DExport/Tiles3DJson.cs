using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace RevitGeoSuite.Tiles3DExport;

/// <summary>
/// Serializes 3D Tiles JSON so the bytes match on every runtime. Newtonsoft formats doubles with
/// "R", which .NET Framework renders as G15 (G17 when G15 does not round-trip) and .NET Core 3.0+
/// renders as the shortest round-trip string. Kanizu's conformance fixtures were written on .NET
/// Framework, so both runtimes use the .NET Framework algorithm.
/// </summary>
internal static class Tiles3DJson
{
    private static readonly char[] DecimalOrExponent = { '.', 'E', 'e' };

    public static string Serialize(object value, Formatting formatting, bool camelCase = false)
    {
        JsonSerializerSettings settings = new()
        {
            Culture = CultureInfo.InvariantCulture,
            Converters = { NetFrameworkDoubleConverter.Instance },
        };
        if (camelCase)
        {
            settings.ContractResolver = new CamelCasePropertyNamesContractResolver();
        }

        return JsonConvert.SerializeObject(value, formatting, settings);
    }

    /// <summary>The JSON token for <paramref name="value"/>, as .NET Framework Newtonsoft writes it.</summary>
    public static string FormatDouble(double value)
    {
        if (value == 0d)
        {
            // .NET Framework drops the sign of negative zero; .NET Core keeps it.
            return "0.0";
        }

        string text = value.ToString("G15", CultureInfo.InvariantCulture);
        // Use TryParse rather than Parse: G15 of double.MaxValue/MinValue rounds to 1.79769313486232E+308,
        // which overflows double.Parse on .NET Framework 4.8.
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double roundTrip) || roundTrip != value)
        {
            text = value.ToString("G17", CultureInfo.InvariantCulture);
        }

        return text.IndexOfAny(DecimalOrExponent) < 0 ? text + ".0" : text;
    }

    private sealed class NetFrameworkDoubleConverter : JsonConverter<double>
    {
        public static readonly NetFrameworkDoubleConverter Instance = new();

        public override bool CanRead => false;

        public override void WriteJson(JsonWriter writer, double value, JsonSerializer serializer)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                writer.WriteValue(value);
                return;
            }

            writer.WriteRawValue(FormatDouble(value));
        }

        public override double ReadJson(JsonReader reader, Type objectType, double existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            throw new NotSupportedException();
        }
    }
}
