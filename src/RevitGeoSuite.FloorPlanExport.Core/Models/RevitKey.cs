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
/// A feature from a linked model carries <c>model@LinkUniqueId</c> as its model part, where
/// <c>LinkUniqueId</c> is the UniqueId of the link instance in the host, so a model linked twice
/// keys each copy apart and a step line's <c>parent_key</c> names the stair of its own copy.
/// </remarks>
public static class RevitKey
{
    public const string AttributeName = "revit_key";

    /// <summary>On a detail line, the <see cref="AttributeName"/> of the feature the line was drawn from.</summary>
    public const string ParentAttributeName = "parent_key";

    public const char Separator = '|';

    /// <summary>Joins a linked model's key and the UniqueId of the link instance it is placed by.</summary>
    public const char LinkSeparator = '@';

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

    /// <summary>
    /// The model part for features of a linked model placed by the link instance
    /// <paramref name="linkInstanceUniqueId"/>, or <paramref name="modelKey"/> itself without one.
    /// </summary>
    public static string QualifyForLinkInstance(string modelKey, string? linkInstanceUniqueId)
    {
        if (modelKey is null)
        {
            throw new ArgumentNullException(nameof(modelKey));
        }

        return string.IsNullOrWhiteSpace(linkInstanceUniqueId)
            ? modelKey
            : string.Concat(modelKey, LinkSeparator.ToString(), linkInstanceUniqueId!.Trim());
    }

    /// <summary>True when <paramref name="modelKey"/> falls back to the document key.</summary>
    public static bool IsDocumentModelKey(string? modelKey)
    {
        return modelKey != null && modelKey.StartsWith(DocumentKeyPrefix, StringComparison.Ordinal);
    }

    /// <summary>
    /// Reads a key strictly as <c>model|UniqueId|part</c>: exactly three parts, a model and a
    /// UniqueId that are not blank, and a part that is a whole number written in digits only.
    /// Anything else is not a key, so nothing can be read from it.
    /// </summary>
    public static bool TryParse(string? key, out string modelPart, out string uniqueId, out int partIndex)
    {
        modelPart = string.Empty;
        uniqueId = string.Empty;
        partIndex = 0;
        if (string.IsNullOrEmpty(key))
        {
            return false;
        }

        string[] parts = key!.Split(Separator);
        if (parts.Length != 3 ||
            string.IsNullOrWhiteSpace(parts[0]) ||
            string.IsNullOrWhiteSpace(parts[1]) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out int part))
        {
            return false;
        }

        modelPart = parts[0];
        uniqueId = parts[1];
        partIndex = part;
        return true;
    }

    /// <summary>
    /// The model part of a key: everything before its UniqueId, so <c>model</c> or
    /// <c>model@LinkUniqueId</c>. Empty when <paramref name="key"/> does not read as a key
    /// (<see cref="TryParse"/>).
    /// </summary>
    public static string ModelPartOf(string? key)
    {
        return TryParse(key, out string modelPart, out _, out _) ? modelPart : string.Empty;
    }

    /// <summary>
    /// The name Kanizu files a model's drift and keep files under, from the model part of its
    /// keys: the model id as it is, or the document key without its <c>doc:</c> prefix.
    /// </summary>
    public static string FilingKeyOf(string? modelKey)
    {
        string key = (modelKey ?? string.Empty).Trim();
        return IsDocumentModelKey(key) ? key.Substring(DocumentKeyPrefix.Length).Trim() : key;
    }

    /// <summary>
    /// Whether the feature a key names belongs to the model <paramref name="hostModelKey"/> itself,
    /// not to a model linked into it or to another model. The model parts compare whole, so a
    /// linked model's <c>model@LinkUniqueId</c> never matches its host. False when either side
    /// names no model: a key that cannot be checked is never taken for the host's.
    /// </summary>
    public static bool BelongsToModel(string? key, string? hostModelKey)
    {
        string model = ModelPartOf(key).Trim();
        string host = (hostModelKey ?? string.Empty).Trim();
        return model.Length > 0 && host.Length > 0 && string.Equals(model, host, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The UniqueId of the link instance a model part names (<c>model@LinkUniqueId</c>), or empty
    /// for a model part without one.
    /// </summary>
    public static string LinkInstanceOf(string? modelPart)
    {
        if (string.IsNullOrEmpty(modelPart))
        {
            return string.Empty;
        }

        int link = modelPart!.LastIndexOf(LinkSeparator);
        return link < 0 ? string.Empty : modelPart.Substring(link + 1).Trim();
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
    /// A layer in which no two features of one floor carry the same key. Later steps of the export
    /// can split one element's footprint into more features than the extractor made, and two views
    /// of one floor can both show an element; each repeat after the first gets the next free part
    /// number, in feature order. Features on different floors (a different <c>level_id</c>) may
    /// share a key. Features without a key are kept as they are. Returns <paramref name="layer"/>
    /// itself when nothing repeats.
    /// </summary>
    public static ExportLayer EnsureUnique(ExportLayer layer)
    {
        if (layer is null)
        {
            throw new ArgumentNullException(nameof(layer));
        }

        List<string?> keys = layer.Features.Select(ReadKey).ToList();
        List<string> scopes = layer.Features.Select(ReadScope).ToList();
        HashSet<string> taken = new(StringComparer.Ordinal);
        int keyed = 0;
        for (int i = 0; i < keys.Count; i++)
        {
            if (keys[i] != null)
            {
                keyed++;
                taken.Add(Scoped(scopes[i], keys[i]!));
            }
        }

        if (taken.Count == keyed)
        {
            return layer;
        }

        ExportLayer unique = new(layer.Name, layer.GeometryType, layer.Attributes);
        HashSet<string> seen = new(StringComparer.Ordinal);
        for (int i = 0; i < layer.Features.Count; i++)
        {
            IExportFeature feature = layer.Features[i];
            string? key = keys[i];
            if (key == null || seen.Add(Scoped(scopes[i], key)))
            {
                unique.AddFeature(feature);
                continue;
            }

            string next = NextFreeKey(key, scopes[i], taken);
            taken.Add(Scoped(scopes[i], next));
            seen.Add(Scoped(scopes[i], next));
            unique.AddFeature(WithAttribute(feature, AttributeName, next));
        }

        return unique;
    }

    internal static string ReadScope(IExportFeature feature)
    {
        return feature.Attributes.TryGetValue("level_id", out object? value) && value != null
            ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
            : string.Empty;
    }

    internal static string Scoped(string scope, string key)
    {
        return string.Concat(scope, "\u001f", key);
    }

    internal static string? ReadKey(IExportFeature feature)
    {
        return feature.Attributes.TryGetValue(AttributeName, out object? value) &&
               value is string key &&
               key.Length > 0
            ? key
            : null;
    }

    internal static string NextFreeKey(string key, string scope, HashSet<string> taken)
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
        while (taken.Contains(Scoped(scope, candidate)));

        return candidate;
    }

    internal static IExportFeature WithAttribute(IExportFeature feature, string name, object? value)
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
