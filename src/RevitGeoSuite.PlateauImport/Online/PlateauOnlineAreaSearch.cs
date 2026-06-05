using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Plateau.Catalog;

namespace RevitGeoSuite.PlateauImport.Online;

public static class PlateauOnlineAreaSearch
{
    private static readonly string[] RomajiMunicipalSuffixes =
    {
        "machi", "chou", "cho", "mura", "shi", "ku", "son", "gun",
    };

    public static IEnumerable<AreaSearchOption> Filter(IReadOnlyList<AreaSearchOption> all, string? query)
    {
        if (all is null) return Array.Empty<AreaSearchOption>();

        string trimmed = (query ?? string.Empty).Trim();
        if (trimmed.Length == 0) return Array.Empty<AreaSearchOption>();

        string[] tokens = trimmed
            .Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.ToLowerInvariant())
            .ToArray();
        if (tokens.Length == 0) return Array.Empty<AreaSearchOption>();

        return all
            .Where(option =>
            {
                string haystack = option.SearchTokens;
                foreach (string token in tokens)
                {
                    if (haystack.IndexOf(token, StringComparison.Ordinal) < 0) return false;
                }

                return true;
            })
            .OrderBy(o => o.DisplayLabel, StringComparer.Ordinal);
    }

    public static AreaSearchOption BuildOption(PlateauAreaOption area)
    {
        string japanesePref = area.Pref ?? string.Empty;
        string? englishPref = JapanPrefectureNames.GetEnglishName(japanesePref);
        string japaneseLocal = !string.IsNullOrWhiteSpace(area.Ward)
            ? area.Ward!
            : !string.IsNullOrWhiteSpace(area.City) ? area.City! : (area.Label ?? area.Code);

        string? romajiLiteral = null;
        if (MunicipalityRomajiNames.TryGet(area.Code, out string romajiHit)) romajiLiteral = romajiHit;

        string? romajiSimplified = romajiLiteral is null ? null : SimplifyRomaji(romajiLiteral);
        string? romajiPretty = romajiSimplified is null ? null : FormatRomajiForDisplay(romajiSimplified);

        string prefDisplay = englishPref is null ? japanesePref : $"{japanesePref} ({englishPref})";
        string localDisplay = romajiPretty is null ? japaneseLocal : $"{japaneseLocal} ({romajiPretty})";

        string displayLabel = string.IsNullOrEmpty(prefDisplay)
            ? localDisplay
            : !string.IsNullOrEmpty(localDisplay)
                ? $"{prefDisplay} -> {localDisplay}"
                : prefDisplay;

        string searchTokens = string.Join("\u0001",
            new[]
            {
                area.Pref ?? string.Empty,
                englishPref ?? string.Empty,
                area.City ?? string.Empty,
                area.Ward ?? string.Empty,
                area.Label ?? string.Empty,
                area.Code ?? string.Empty,
                romajiLiteral ?? string.Empty,
                romajiSimplified ?? string.Empty,
            }).ToLowerInvariant();

        return new AreaSearchOption(
            prefectureJapaneseName: area.Pref ?? string.Empty,
            area: area,
            displayLabel: displayLabel,
            codeLabel: area.Code ?? string.Empty,
            searchTokens: searchTokens);
    }

    public static int PickZoomForBounds(PlateauAreaBounds bounds)
    {
        double widthDeg = Math.Abs(bounds.EastDeg - bounds.WestDeg);
        double heightDeg = Math.Abs(bounds.NorthDeg - bounds.SouthDeg);
        double maxDeg = Math.Max(widthDeg, heightDeg);

        if (maxDeg > 1.0) return 9;
        if (maxDeg > 0.5) return 10;
        if (maxDeg > 0.25) return 11;
        if (maxDeg > 0.1) return 12;
        if (maxDeg > 0.05) return 13;

        return 14;
    }

    internal static string SimplifyRomaji(string romaji)
    {
        if (string.IsNullOrEmpty(romaji)) return romaji;

        return romaji
            .Replace("oo", "o")
            .Replace("ou", "o")
            .Replace("uu", "u");
    }

    internal static string FormatRomajiForDisplay(string romaji)
    {
        if (string.IsNullOrEmpty(romaji)) return romaji;

        foreach (string suffix in RomajiMunicipalSuffixes)
        {
            if (romaji.Length > suffix.Length && romaji.EndsWith(suffix, StringComparison.Ordinal))
            {
                string root = romaji.Substring(0, romaji.Length - suffix.Length);
                return CapitalizeFirst(root) + "-" + suffix;
            }
        }

        return CapitalizeFirst(romaji);
    }

    private static string CapitalizeFirst(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return char.ToUpperInvariant(s[0]) + s.Substring(1);
    }
}
