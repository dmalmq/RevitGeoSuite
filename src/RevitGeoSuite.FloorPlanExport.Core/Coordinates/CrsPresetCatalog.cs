using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Coordinates;

public sealed class CrsPresetEntry
{
    public CrsPresetEntry(int epsg, string displayName)
        : this(epsg, displayName, string.Empty)
    {
    }

    public CrsPresetEntry(int epsg, string displayName, string searchAliases)
    {
        Epsg = epsg;
        DisplayName = displayName;
        SearchAliases = searchAliases ?? string.Empty;
    }

    public int Epsg { get; }

    public string DisplayName { get; }

    public string SearchAliases { get; }
}

public sealed class CrsPresetGroup
{
    public CrsPresetGroup(string region, IReadOnlyList<CrsPresetEntry> entries)
    {
        Region = region;
        Entries = entries;
    }

    public string Region { get; }

    public IReadOnlyList<CrsPresetEntry> Entries { get; }
}

public static class CrsPresetCatalog
{
    private const string JapanSearchAliases = "JGD2024";
    private static IReadOnlyList<CrsPresetGroup>? _groups;

    public static IReadOnlyList<CrsPresetGroup> GetAllGroups()
    {
        return _groups ??= BuildGroups();
    }

    public static string? TryGetDisplayName(int epsg)
    {
        int normalizedEpsg = JapanPlaneRectangular.NormalizeEpsg(epsg);
        foreach (CrsPresetGroup group in GetAllGroups())
        {
            CrsPresetEntry? entry = group.Entries.FirstOrDefault(e => e.Epsg == normalizedEpsg);
            if (entry != null)
            {
                return entry.DisplayName;
            }
        }

        return null;
    }

    public static IReadOnlyList<CrsPresetEntry> Search(string? query)
    {
        CrsPresetEntry[] allEntries = GetAllGroups()
            .SelectMany(group => group.Entries)
            .ToArray();

        if (string.IsNullOrWhiteSpace(query))
        {
            return allEntries;
        }

        string[] tokens = query!
            .Trim()
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

        return allEntries
            .Where(entry => MatchesTokens(entry, tokens))
            .ToArray();
    }

    private static IReadOnlyList<CrsPresetGroup> BuildGroups()
    {
        return new[]
        {
            new CrsPresetGroup("Japan", BuildJapanPresets()),
        };
    }

    private static CrsPresetEntry[] BuildJapanPresets()
    {
        return JapanPlaneRectangular.AllZones
            .Select(zone => new CrsPresetEntry(zone.Epsg, zone.DisplayName, JapanSearchAliases))
            .ToArray();
    }

    private static bool MatchesTokens(CrsPresetEntry entry, string[] tokens)
    {
        string zoneTerms = JapanPlaneRectangular.TryGetZone(entry.Epsg, out JapanPlaneRectangularZoneDefinition? zone) && zone != null
            ? $"zone {zone.ZoneNumber} zone {zone.RomanZone} Japan Plane Rectangular CS {zone.RomanZone}"
            : string.Empty;

        string searchable = string.Join(
            " ",
            new[]
            {
                "Japan",
                entry.DisplayName,
                entry.SearchAliases,
                $"EPSG:{entry.Epsg}",
                entry.Epsg.ToString(),
                zoneTerms,
            });

        return tokens.All(token => searchable.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0);
    }
}
