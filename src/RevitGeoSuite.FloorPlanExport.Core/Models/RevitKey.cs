using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>
/// The key that ties an exported feature to the Revit element it came from, so that two exports of
/// the same model, and the GDB built from them, can be matched row by row.
/// </summary>
/// <remarks>
/// A key reads <c>model|UniqueId|part</c>. <c>model</c> is the model's stable id from
/// RevitGeoSuite (a lowercase GUID), or <c>doc:</c> plus the document key when the model has none
/// yet; the document key holds the file path, so it changes between PCs and on Save As.
/// <c>UniqueId</c> is the element's Revit UniqueId, which survives edits, worksharing and
/// Save As. <c>part</c> counts the features one element makes on a floor: 0 for the only or
/// first one, then 1, 2 and so on. A key is unique within one layer of one floor; the same stair
/// exported on two floors carries the same key on both, and <c>level_id</c> tells them apart.
/// </remarks>
public static class RevitKey
{
    public const string AttributeName = "revit_key";

    /// <summary>On a detail line, the <see cref="AttributeName"/> of the feature the line was drawn from.</summary>
    public const string ParentAttributeName = "parent_key";

    public const char Separator = '|';

    private const string DocumentKeyPrefix = "doc:";

    /// <summary>The model part of a key: the stable model id, or the document key without one.</summary>
    public static string CreateModelKey(string? modelId, string? documentKey)
    {
        if (!string.IsNullOrWhiteSpace(modelId))
        {
            return modelId!.Trim().ToLowerInvariant();
        }

        return DocumentKeyPrefix + (documentKey ?? string.Empty).Trim();
    }

    /// <summary>True when <paramref name="modelKey"/> falls back to the document key.</summary>
    public static bool IsDocumentModelKey(string? modelKey)
    {
        return modelKey != null && modelKey.StartsWith(DocumentKeyPrefix, StringComparison.Ordinal);
    }

    /// <summary>The key, or null when the element has no UniqueId (a synthetic feature).</summary>
    public static string? Create(string? modelKey, string? uniqueId, int partIndex = 0)
    {
        if (string.IsNullOrWhiteSpace(modelKey) || string.IsNullOrWhiteSpace(uniqueId))
        {
            return null;
        }

        if (partIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(partIndex), partIndex, "Part index cannot be negative.");
        }

        return string.Concat(
            modelKey!.Trim(),
            Separator.ToString(),
            uniqueId!.Trim(),
            Separator.ToString(),
            partIndex.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// A layer whose features all carry a different key. Later steps of the export can split one
    /// element's footprint into more features than the extractor made; each repeat after the
    /// first gets the next free part number, in feature order. Features without a key are kept as
    /// they are. Returns <paramref name="layer"/> itself when nothing repeats.
    /// </summary>
    public static ExportLayer EnsureUnique(ExportLayer layer)
    {
        if (layer is null)
        {
            throw new ArgumentNullException(nameof(layer));
        }

        List<string?> keys = layer.Features.Select(ReadKey).ToList();
        HashSet<string> taken = new(keys.Where(key => key != null)!, StringComparer.Ordinal);
        if (taken.Count == keys.Count(key => key != null))
        {
            return layer;
        }

        ExportLayer unique = new(layer.Name, layer.GeometryType, layer.Attributes);
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            IExportFeature feature = layer.Features[i];
            string? key = keys[i];
            if (key == null || seen.Add(key))
            {
                unique.AddFeature(feature);
                continue;
            }

            string next = NextFreeKey(key, taken);
            taken.Add(next);
            seen.Add(next);
            unique.AddFeature(WithAttribute(feature, AttributeName, next));
        }

        return unique;
    }

    private static string? ReadKey(IExportFeature feature)
    {
        return feature.Attributes.TryGetValue(AttributeName, out object? value) &&
               value is string key &&
               key.Length > 0
            ? key
            : null;
    }

    private static string NextFreeKey(string key, HashSet<string> taken)
    {
        int split = key.LastIndexOf(Separator);
        string stem = split >= 0 ? key.Substring(0, split) : key;
        int part = split >= 0 &&
                   int.TryParse(key.Substring(split + 1), NumberStyles.None, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : 0;

        string candidate;
        do
        {
            part++;
            candidate = string.Concat(stem, Separator.ToString(), part.ToString(CultureInfo.InvariantCulture));
        }
        while (taken.Contains(candidate));

        return candidate;
    }

    private static IExportFeature WithAttribute(IExportFeature feature, string name, object? value)
    {
        Dictionary<string, object?> attributes = new(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> entry in feature.Attributes)
        {
            attributes[entry.Key] = entry.Value;
        }

        attributes[name] = value;
        return feature switch
        {
            ExportPolygon polygon => new ExportPolygon(polygon.Polygons, attributes),
            ExportLineString line => new ExportLineString(line.LineString, attributes),
            _ => throw new NotSupportedException($"Feature type '{feature.GetType().Name}' is not supported."),
        };
    }
}
