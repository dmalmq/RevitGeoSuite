using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.Core.Plateau.Catalog;

public sealed class PlateauCatalog
{
    private PlateauCatalog(IReadOnlyList<PlateauDatasetEntry> datasets, IReadOnlyList<PlateauAreaOption> areaOptions)
    {
        Datasets = datasets;
        AreaOptions = areaOptions;
    }

    public IReadOnlyList<PlateauDatasetEntry> Datasets { get; }

    public IReadOnlyList<PlateauAreaOption> AreaOptions { get; }

    public static PlateauCatalog Normalize(PlateauCatalogResponse response)
    {
        if (response is null) throw new ArgumentNullException(nameof(response));

        List<PlateauDatasetEntry> latest = response.LatestDatasets ?? new List<PlateauDatasetEntry>();
        foreach (PlateauDatasetEntry entry in latest)
        {
            entry.CatalogSource = PlateauCatalogSource.Latest;
        }

        List<PlateauDatasetEntry> regular = response.Datasets ?? new List<PlateauDatasetEntry>();
        foreach (PlateauDatasetEntry entry in regular)
        {
            entry.CatalogSource = PlateauCatalogSource.Dataset;
        }

        List<PlateauDatasetEntry> all = new List<PlateauDatasetEntry>(latest.Count + regular.Count);
        all.AddRange(latest);
        all.AddRange(regular);

        List<PlateauDatasetEntry> tiles = all.Where(IsPlateau3dTilesDataset).ToList();
        List<PlateauAreaOption> areaOptions = BuildAreaOptions(tiles);
        return new PlateauCatalog(tiles, areaOptions);
    }

    public static bool IsPlateau3dTilesDataset(PlateauDatasetEntry entry)
    {
        if (entry is null) return false;
        if (!string.Equals(entry.Format, "3D Tiles", StringComparison.Ordinal)) return false;
        if (string.IsNullOrEmpty(entry.TypeEn)) return false;
        string? url = entry.PreferredUrl;
        if (string.IsNullOrEmpty(url)) return false;
        if (!url!.Contains("tileset.json")) return false;
        if (entry.Interior == true) return false;
        return true;
    }

    private static List<PlateauAreaOption> BuildAreaOptions(IReadOnlyList<PlateauDatasetEntry> datasets)
    {
        Dictionary<string, AreaBuilder> areaMap = new Dictionary<string, AreaBuilder>(StringComparer.Ordinal);
        foreach (PlateauDatasetEntry dataset in datasets)
        {
            string? primary = NormalizeCode(dataset.WardCode) ?? NormalizeCode(dataset.CityCode);
            if (primary is null) continue;

            List<string> aliases = new List<string>(2);
            string? cityCode = NormalizeCode(dataset.CityCode);
            string? wardCode = NormalizeCode(dataset.WardCode);
            if (cityCode is not null) aliases.Add(cityCode);
            if (wardCode is not null && wardCode != cityCode) aliases.Add(wardCode);

            if (areaMap.TryGetValue(primary, out AreaBuilder? existing) && existing is not null)
            {
                foreach (string alias in aliases)
                {
                    if (!existing.Aliases.Contains(alias, StringComparer.Ordinal))
                    {
                        existing.Aliases.Add(alias);
                    }
                }
                continue;
            }

            areaMap[primary] = new AreaBuilder
            {
                Code = primary,
                Aliases = aliases,
                Label = FormatLabel(dataset),
                Pref = dataset.Pref ?? string.Empty,
                City = dataset.City ?? string.Empty,
                Ward = dataset.Ward ?? string.Empty
            };
        }

        return areaMap.Values
            .Select(b => new PlateauAreaOption(b.Code, b.Aliases, b.Label, b.Pref, b.City, b.Ward))
            .OrderBy(o => o.Label, StringComparer.Create(CultureInfo.GetCultureInfo("ja-JP"), ignoreCase: false))
            .ToList();
    }

    public static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string trimmed = code!.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string FormatLabel(PlateauDatasetEntry dataset)
    {
        string[] parts = new[] { dataset.Pref, dataset.City, dataset.Ward }
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Cast<string>()
            .ToArray();
        if (parts.Length > 0) return string.Join(" ", parts);
        return NormalizeCode(dataset.WardCode) ?? NormalizeCode(dataset.CityCode) ?? string.Empty;
    }

    private sealed class AreaBuilder
    {
        public string Code { get; set; } = string.Empty;
        public List<string> Aliases { get; set; } = new();
        public string Label { get; set; } = string.Empty;
        public string Pref { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string Ward { get; set; } = string.Empty;
    }
}
