using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>
/// Lossless preflighted attribute entry preserving explicit <c>null</c> vs empty string <c>""</c>,
/// exact 64-bit integer text (never rounded through IEEE-754 <see cref="double"/>), and leading-zero codes.
/// </summary>
public sealed class NormalizedShowcaseAttribute
{
    public NormalizedShowcaseAttribute(
        string fieldName,
        ShowcaseFieldType effectiveFieldType,
        bool preserveAsText,
        ShowcaseAttributeValue value,
        string? displayText)
    {
        FieldName = fieldName ?? throw new ArgumentNullException(nameof(fieldName));
        EffectiveFieldType = effectiveFieldType;
        PreserveAsText = preserveAsText;
        Value = value ?? throw new ArgumentNullException(nameof(value));
        DisplayText = displayText;
    }

    public string FieldName { get; }

    public ShowcaseFieldType EffectiveFieldType { get; }

    public bool PreserveAsText { get; }

    public ShowcaseAttributeValue Value { get; }

    public bool IsNull => Value.IsNull;

    public bool IsEmptyString => !Value.IsNull && Value.Kind == ShowcaseAttributeKind.Text && (Value.TextValue?.Length ?? 0) == 0;

    /// <summary>
    /// Exact invariant text value (<c>null</c> when <see cref="IsNull"/> is <c>true</c>, <c>""</c> when
    /// <see cref="IsEmptyString"/> is <c>true</c>, exact unrounded decimal string for 64-bit IDs, and exact
    /// leading-zero string for identifier codes like <c>"00123"</c>).
    /// </summary>
    public string? DisplayText { get; }
}

/// <summary>
/// Lossless attribute preflight normalizer for <c>kanizu.gis-showcase</c> v1 layers and features.
/// </summary>
public static class ShowcaseAttributeNormalizer
{
    private const string MaxSafeIntegerDigits = "9007199254740991";

    /// <summary>True when <paramref name="value"/> exceeds IEEE-754 double safe integer range (<c>2^53 - 1</c>).</summary>
    public static bool IsUnsafeJsonInteger(long value) =>
        value == long.MinValue || Math.Abs(value) > ShowcaseContract.MaxSafeJsonInteger;

    /// <summary>True when <paramref name="value"/> cannot be stored losslessly in Revit's 32-bit <c>StorageType.Integer</c>.</summary>
    public static bool ExceedsRevitInt32(long value) =>
        value < int.MinValue || value > int.MaxValue;

    /// <summary>
    /// True when <paramref name="text"/> is a multi-digit ASCII numeric identifier starting with <c>'0'</c>
    /// (e.g. <c>"00123"</c>, <c>"0042"</c>, <c>"00"</c>) whose leading zeros would be lost by numeric coercion.
    /// </summary>
    public static bool IsLeadingZeroCode(string? text)
    {
        if (string.IsNullOrEmpty(text) || text!.Length < 2 || text[0] != '0')
        {
            return false;
        }

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when <paramref name="text"/> is a decimal integer literal whose magnitude exceeds <c>2^53 - 1</c>.</summary>
    public static bool IsUnsafeIntegerText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string trimmed = text!.Trim();
        int start = (trimmed[0] == '-' || trimmed[0] == '+') ? 1 : 0;
        if (start >= trimmed.Length)
        {
            return false;
        }

        for (int i = start; i < trimmed.Length; i++)
        {
            char c = trimmed[i];
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        string significant = trimmed.Substring(start).TrimStart('0');
        if (significant.Length != MaxSafeIntegerDigits.Length)
        {
            return significant.Length > MaxSafeIntegerDigits.Length;
        }

        return string.CompareOrdinal(significant, MaxSafeIntegerDigits) > 0;
    }

    /// <summary>
    /// Determines whether a column must be preserved as exact text in Revit/Forma so 64-bit integer IDs,
    /// values exceeding 32-bit Revit integers, and leading-zero codes are never truncated or rounded.
    /// </summary>
    public static bool RequiresTextPreservation(ShowcaseField? field, IEnumerable<ShowcaseAttributeValue>? columnValues)
    {
        if (field != null && field.PreserveAsText)
        {
            return true;
        }

        if (columnValues is null)
        {
            return false;
        }

        foreach (ShowcaseAttributeValue value in columnValues)
        {
            if (value is null || value.IsNull)
            {
                continue;
            }

            switch (value.Kind)
            {
                case ShowcaseAttributeKind.Integer:
                    if (IsUnsafeJsonInteger(value.Int64Value!.Value) || ExceedsRevitInt32(value.Int64Value.Value))
                    {
                        return true;
                    }

                    break;

                case ShowcaseAttributeKind.Text:
                    if (IsLeadingZeroCode(value.TextValue) || IsUnsafeIntegerText(value.TextValue))
                    {
                        return true;
                    }

                    break;
            }
        }

        return false;
    }

    /// <summary>
    /// Normalizes a single feature attribute losslessly:
    /// - <c>null</c> stays <see cref="ShowcaseAttributeValue.Null"/> (<see cref="NormalizedShowcaseAttribute.IsNull"/> = <c>true</c>)
    /// - <c>""</c> stays empty text (<see cref="NormalizedShowcaseAttribute.IsEmptyString"/> = <c>true</c>)
    /// - 64-bit integers outside safe/Revit-int32 bounds become exact decimal text with <c>PreserveAsText = true</c>
    /// - Leading-zero strings stay exact text with <c>PreserveAsText = true</c>
    /// </summary>
    public static NormalizedShowcaseAttribute NormalizeValue(
        string fieldName,
        ShowcaseAttributeValue? rawValue,
        ShowcaseField? field = null,
        bool columnPreserveAsText = false)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
        {
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        }

        ShowcaseAttributeValue value = rawValue ?? ShowcaseAttributeValue.Null;
        bool preserveAsText = columnPreserveAsText || (field?.PreserveAsText ?? false);

        if (value.IsNull)
        {
            ShowcaseFieldType nullFieldType = preserveAsText
                ? ShowcaseFieldType.Text
                : field?.FieldType ?? ShowcaseFieldType.Text;
            return new NormalizedShowcaseAttribute(fieldName, nullFieldType, preserveAsText, ShowcaseAttributeValue.Null, displayText: null);
        }

        switch (value.Kind)
        {
            case ShowcaseAttributeKind.Integer:
            {
                long intVal = value.Int64Value!.Value;
                bool forceText = preserveAsText || IsUnsafeJsonInteger(intVal) || ExceedsRevitInt32(intVal);
                string exactText = intVal.ToString(CultureInfo.InvariantCulture);
                if (forceText)
                {
                    return new NormalizedShowcaseAttribute(
                        fieldName,
                        ShowcaseFieldType.Text,
                        preserveAsText: true,
                        ShowcaseAttributeValue.FromText(exactText),
                        exactText);
                }

                return new NormalizedShowcaseAttribute(
                    fieldName,
                    ShowcaseFieldType.Integer,
                    preserveAsText: false,
                    value,
                    exactText);
            }

            case ShowcaseAttributeKind.Text:
            {
                string text = value.TextValue ?? string.Empty;
                bool forceText = preserveAsText || IsLeadingZeroCode(text) || IsUnsafeIntegerText(text);
                ShowcaseFieldType fieldType = field?.FieldType == ShowcaseFieldType.Date
                    ? ShowcaseFieldType.Date
                    : field?.FieldType == ShowcaseFieldType.Json
                        ? ShowcaseFieldType.Json
                        : ShowcaseFieldType.Text;
                return new NormalizedShowcaseAttribute(fieldName, fieldType, forceText, value, text);
            }

            case ShowcaseAttributeKind.Real:
            {
                double real = value.RealValue!.Value;
                if (double.IsNaN(real) || double.IsInfinity(real))
                {
                    throw new ShowcaseSnapshotException($"Attribute '{fieldName}' contains a non-finite real value.");
                }

                double normalized = real == 0d ? 0d : real;
                string exactText = normalized.ToString("G", CultureInfo.InvariantCulture);
                if (preserveAsText)
                {
                    return new NormalizedShowcaseAttribute(
                        fieldName,
                        ShowcaseFieldType.Text,
                        preserveAsText: true,
                        ShowcaseAttributeValue.FromText(exactText),
                        exactText);
                }

                return new NormalizedShowcaseAttribute(
                    fieldName,
                    ShowcaseFieldType.Real,
                    preserveAsText: false,
                    ShowcaseAttributeValue.FromReal(normalized),
                    exactText);
            }

            case ShowcaseAttributeKind.Boolean:
            {
                bool flag = value.BoolValue!.Value;
                string exactText = flag ? "true" : "false";
                if (preserveAsText)
                {
                    return new NormalizedShowcaseAttribute(
                        fieldName,
                        ShowcaseFieldType.Text,
                        preserveAsText: true,
                        ShowcaseAttributeValue.FromText(exactText),
                        exactText);
                }

                return new NormalizedShowcaseAttribute(
                    fieldName,
                    ShowcaseFieldType.Boolean,
                    preserveAsText: false,
                    value,
                    exactText);
            }

            case ShowcaseAttributeKind.Json:
            {
                string raw = value.RawJsonText ?? string.Empty;
                return new NormalizedShowcaseAttribute(
                    fieldName,
                    ShowcaseFieldType.Json,
                    preserveAsText: true,
                    value,
                    raw);
            }

            default:
                return new NormalizedShowcaseAttribute(fieldName, ShowcaseFieldType.Text, preserveAsText, ShowcaseAttributeValue.Null, null);
        }
    }

    /// <summary>
    /// Computes per-column <c>preserveAsText</c> flags across every feature of <paramref name="layer"/>.
    /// </summary>
    public static IReadOnlyDictionary<string, bool> ComputeColumnPreserveAsTextMap(ShowcaseLayer layer)
    {
        if (layer is null)
        {
            throw new ArgumentNullException(nameof(layer));
        }

        Dictionary<string, ShowcaseField> fieldsByName = new(StringComparer.Ordinal);
        foreach (ShowcaseField field in layer.Fields)
        {
            if (!fieldsByName.ContainsKey(field.Name))
            {
                fieldsByName[field.Name] = field;
            }
        }

        HashSet<string> allColumns = new(fieldsByName.Keys, StringComparer.Ordinal);
        foreach (ShowcaseFeature feature in layer.Features)
        {
            foreach (string key in feature.Attributes.Keys)
            {
                allColumns.Add(key);
            }
        }

        Dictionary<string, bool> result = new(StringComparer.Ordinal);
        foreach (string col in allColumns)
        {
            fieldsByName.TryGetValue(col, out ShowcaseField? field);
            IEnumerable<ShowcaseAttributeValue> values = layer.Features
                .Select(feature => feature.Attributes.TryGetValue(col, out ShowcaseAttributeValue? v) ? v : ShowcaseAttributeValue.Null);
            result[col] = RequiresTextPreservation(field, values);
        }

        return result;
    }

    /// <summary>
    /// Normalizes every attribute of <paramref name="feature"/> in deterministic order: declared layer fields first,
    /// followed by any undeclared extra attributes sorted by ordinal name.
    /// </summary>
    public static IReadOnlyList<NormalizedShowcaseAttribute> NormalizeFeatureAttributes(
        ShowcaseLayer layer,
        ShowcaseFeature feature,
        IReadOnlyDictionary<string, bool>? columnPreserveMap = null)
    {
        if (layer is null)
        {
            throw new ArgumentNullException(nameof(layer));
        }

        if (feature is null)
        {
            throw new ArgumentNullException(nameof(feature));
        }

        IReadOnlyDictionary<string, bool> preserveMap = columnPreserveMap ?? ComputeColumnPreserveAsTextMap(layer);
        List<NormalizedShowcaseAttribute> normalized = new(layer.Fields.Count + feature.Attributes.Count);
        HashSet<string> emitted = new(StringComparer.Ordinal);

        foreach (ShowcaseField field in layer.Fields)
        {
            if (!emitted.Add(field.Name))
            {
                continue;
            }

            feature.Attributes.TryGetValue(field.Name, out ShowcaseAttributeValue? raw);
            bool colPreserve = preserveMap.TryGetValue(field.Name, out bool flag) && flag;
            normalized.Add(NormalizeValue(field.Name, raw ?? ShowcaseAttributeValue.Null, field, colPreserve));
        }

        foreach (string extraKey in feature.Attributes.Keys
                     .Where(key => !emitted.Contains(key))
                     .OrderBy(key => key, StringComparer.Ordinal))
        {
            emitted.Add(extraKey);
            ShowcaseAttributeValue raw = feature.Attributes[extraKey];
            bool colPreserve = preserveMap.TryGetValue(extraKey, out bool flag) && flag;
            normalized.Add(NormalizeValue(extraKey, raw, field: null, colPreserve));
        }

        return normalized;
    }

    /// <summary>
    /// Serializes a set of normalized attributes into compact, deterministic, lossless JSON for
    /// <c>ks_attributes_json</c>, preserving explicit <c>null</c> vs <c>""</c>, exact 64-bit integer strings,
    /// leading-zero codes, and sorted keys.
    /// </summary>
    public static string SerializeLosslessAttributesJson(IEnumerable<NormalizedShowcaseAttribute> attributes)
    {
        if (attributes is null)
        {
            throw new ArgumentNullException(nameof(attributes));
        }

        List<NormalizedShowcaseAttribute> ordered = attributes
            .OrderBy(item => item.FieldName, StringComparer.Ordinal)
            .ToList();

        StringBuilder sb = new();
        using (StringWriter stringWriter = new(sb, CultureInfo.InvariantCulture))
        using (JsonTextWriter writer = new(stringWriter))
        {
            writer.Formatting = Formatting.None;
            writer.WriteStartObject();
            foreach (NormalizedShowcaseAttribute attribute in ordered)
            {
                writer.WritePropertyName(attribute.FieldName);
                WriteAttributeValue(writer, attribute);
            }

            writer.WriteEndObject();
        }

        return sb.ToString();
    }

    private static void WriteAttributeValue(JsonTextWriter writer, NormalizedShowcaseAttribute attribute)
    {
        if (attribute.IsNull)
        {
            writer.WriteNull();
            return;
        }

        if (attribute.PreserveAsText && attribute.Value.Kind != ShowcaseAttributeKind.Json)
        {
            writer.WriteValue(attribute.DisplayText ?? string.Empty);
            return;
        }

        switch (attribute.Value.Kind)
        {
            case ShowcaseAttributeKind.Text:
                writer.WriteValue(attribute.Value.TextValue ?? string.Empty);
                break;
            case ShowcaseAttributeKind.Integer:
                writer.WriteValue(attribute.Value.Int64Value!.Value);
                break;
            case ShowcaseAttributeKind.Real:
                writer.WriteValue(attribute.Value.RealValue!.Value);
                break;
            case ShowcaseAttributeKind.Boolean:
                writer.WriteValue(attribute.Value.BoolValue!.Value);
                break;
            case ShowcaseAttributeKind.Json:
                writer.WriteRawValue(attribute.Value.RawJsonText ?? "null");
                break;
            default:
                writer.WriteNull();
                break;
        }
    }
}
