using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>
/// The <c>gis.rules.hash</c> of a package: sha256, in lower-case hex, of the canonical JSON of
/// <c>{"categoryRules": [...], "profile": {...}}</c>. The canonical form is written out in
/// docs/kanizu-link-contract.md; Kanizu computes the same bytes.
/// </summary>
public static class GisRulesHash
{
    public static string Compute(IEnumerable<MappingRule> categoryRules, GisRulesProfile profile)
    {
        return Sha256(CanonicalJson(categoryRules, profile));
    }

    public static string CanonicalJson(IEnumerable<MappingRule> categoryRules, GisRulesProfile profile)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        JArray rules = new(categoryRules
            .Select(GisRulesRule.From)
            .Where(rule => rule.Match.Length > 0)
            .Select(rule => JObject.FromObject(rule, GisRulesFile.Serializer)));
        JObject root = new()
        {
            ["categoryRules"] = SortRules(rules),
            ["profile"] = profile.ToToken(),
        };
        StringBuilder text = new();
        Write(root, text);
        return text.ToString();
    }

    private static JArray SortRules(JArray rules)
    {
        return new JArray(rules
            .OrderBy(rule => Nfc(rule.Value<string>("type")), StringComparer.Ordinal)
            .ThenBy(rule => Nfc(rule.Value<string>("match")), StringComparer.Ordinal));
    }

    private static string Sha256(string text)
    {
        using SHA256 sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(new UTF8Encoding(false).GetBytes(text)).Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static string Nfc(string? value)
    {
        return (value ?? string.Empty).Normalize(NormalizationForm.FormC);
    }

    private static void Write(JToken token, StringBuilder text)
    {
        switch (token)
        {
            case JObject obj:
                text.Append('{');
                bool first = true;
                foreach (JProperty property in obj.Properties()
                    .Where(property => property.Value.Type != JTokenType.Null)
                    .OrderBy(property => Nfc(property.Name), StringComparer.Ordinal))
                {
                    if (!first)
                    {
                        text.Append(',');
                    }

                    first = false;
                    WriteString(Nfc(property.Name), text);
                    text.Append(':');
                    Write(property.Value, text);
                }

                text.Append('}');
                break;
            case JArray array:
                text.Append('[');
                for (int i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                    {
                        text.Append(',');
                    }

                    Write(array[i], text);
                }

                text.Append(']');
                break;
            case JValue value:
                WriteValue(value, text);
                break;
            default:
                throw new InvalidOperationException($"A {token.Type} cannot be part of the rules hash.");
        }
    }

    private static void WriteValue(JValue value, StringBuilder text)
    {
        switch (value.Type)
        {
            case JTokenType.String:
                WriteString(Nfc((string?)value.Value), text);
                break;
            case JTokenType.Boolean:
                text.Append((bool)value.Value! ? "true" : "false");
                break;
            case JTokenType.Integer:
                text.Append(Convert.ToString(value.Value, CultureInfo.InvariantCulture));
                break;
            case JTokenType.Float:
                text.Append(FormatNumber(Convert.ToDouble(value.Value, CultureInfo.InvariantCulture)));
                break;
            default:
                throw new InvalidOperationException($"A {value.Type} cannot be part of the rules hash.");
        }
    }

    private static void WriteString(string value, StringBuilder text)
    {
        text.Append('"');
        foreach (char c in value)
        {
            if (c == '"')
            {
                text.Append("\\\"");
            }
            else if (c == '\\')
            {
                text.Append("\\\\");
            }
            else if (c < 0x20)
            {
                text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            }
            else
            {
                text.Append(c);
            }
        }

        text.Append('"');
    }

    /// <summary>
    /// The shortest decimal digits that read back as the same double, laid out positionally
    /// (never with an exponent); a whole number has no fraction and zero is <c>0</c>.
    /// </summary>
    internal static string FormatNumber(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new InvalidOperationException("NaN and infinity cannot be part of the rules hash.");
        }

        if (value == 0d)
        {
            return "0";
        }

        string scientific = string.Empty;
        for (int digits = 1; digits <= 17; digits++)
        {
            scientific = Math.Abs(value).ToString("E" + (digits - 1), CultureInfo.InvariantCulture);
            if (double.Parse(scientific, CultureInfo.InvariantCulture) == Math.Abs(value))
            {
                break;
            }
        }

        string[] parts = scientific.Split('E');
        string mantissa = parts[0].Replace(".", string.Empty).TrimEnd('0');
        if (mantissa.Length == 0)
        {
            mantissa = "0";
        }

        int exponent = int.Parse(parts[1], CultureInfo.InvariantCulture);
        string sign = value < 0d ? "-" : string.Empty;
        int pointIndex = exponent + 1;
        if (pointIndex <= 0)
        {
            return sign + "0." + new string('0', -pointIndex) + mantissa;
        }

        if (pointIndex >= mantissa.Length)
        {
            return sign + mantissa + new string('0', pointIndex - mantissa.Length);
        }

        return sign + mantissa.Substring(0, pointIndex) + "." + mantissa.Substring(pointIndex);
    }
}
