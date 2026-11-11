using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>One area colour of Kanizu's area colour table.</summary>
public sealed class KanizuThemeArea
{
    public KanizuThemeArea(string key, string nameJa, string nameEn, string hex)
    {
        Key = key;
        NameJa = nameJa;
        NameEn = nameEn;
        Hex = hex;
    }

    public string Key { get; }

    public string NameJa { get; }

    public string NameEn { get; }

    /// <summary>Upper-case <c>#RRGGBB</c>.</summary>
    public string Hex { get; }
}

/// <summary>
/// The area colour table of Kanizu's StaNavi map design, read from <c>color_theme.json</c>. The file
/// is vendored unchanged from Kanizu (<c>crates/kanizu-core/src/recolor/color_theme.json</c>) into
/// <c>standards/</c>, so the Revit floor types use the colours of the delivered map.
/// </summary>
public sealed class KanizuColorTheme
{
    private readonly Dictionary<string, KanizuThemeArea> _areas;

    private KanizuColorTheme(IReadOnlyList<KanizuThemeArea> areas, string outlineHex, string otherHex)
    {
        Areas = areas;
        OutlineHex = outlineHex;
        OtherHex = otherHex;
        _areas = new Dictionary<string, KanizuThemeArea>(StringComparer.Ordinal);
        foreach (KanizuThemeArea area in areas)
        {
            _areas[area.Key] = area;
        }
    }

    public IReadOnlyList<KanizuThemeArea> Areas { get; }

    public string OutlineHex { get; }

    public string OtherHex { get; }

    public KanizuThemeArea GetArea(string key)
    {
        if (!_areas.TryGetValue(key, out KanizuThemeArea? area))
        {
            throw new KeyNotFoundException($"The colour theme has no area '{key}'.");
        }

        return area;
    }

    public static KanizuColorTheme Parse(string json)
    {
        if (json is null)
        {
            throw new ArgumentNullException(nameof(json));
        }

        JObject root = JObject.Parse(json);
        List<KanizuThemeArea> areas = new();
        foreach (JToken token in (JArray?)root["areas"] ?? new JArray())
        {
            string key = RequireString(token, "key");
            areas.Add(new KanizuThemeArea(
                key,
                (string?)token["name"]?["ja"] ?? key,
                (string?)token["name"]?["en"] ?? key,
                NormalizeHex(RequireString(token, "hex"))));
        }

        if (areas.Count == 0)
        {
            throw new FormatException("The colour theme lists no areas.");
        }

        return new KanizuColorTheme(
            areas,
            NormalizeHex(RequireString(RequireObject(root, "outline"), "hex")),
            NormalizeHex(RequireString(RequireObject(root, "other"), "hex")));
    }

    public static KanizuColorTheme LoadEmbedded()
    {
        return Parse(GisStandardFiles.ReadColorTheme());
    }

    internal static string NormalizeHex(string value)
    {
        string trimmed = value.Trim();
        if (!trimmed.StartsWith("#", StringComparison.Ordinal))
        {
            trimmed = "#" + trimmed;
        }

        if (trimmed.Length != 7)
        {
            throw new FormatException($"'{value}' is not a #RRGGBB colour.");
        }

        return trimmed.ToUpperInvariant();
    }

    private static JToken RequireObject(JToken token, string name)
    {
        return token[name] as JObject ?? throw new FormatException($"The colour theme is missing '{name}'.");
    }

    private static string RequireString(JToken token, string name)
    {
        string? value = (string?)token[name];
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"The colour theme is missing '{name}'.");
        }

        return value!;
    }
}
