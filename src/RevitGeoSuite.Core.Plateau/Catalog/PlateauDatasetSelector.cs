using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace RevitGeoSuite.Core.Plateau.Catalog;

public enum PlateauTexturePreference
{
    PreferTextured = 0,
    PreferUntextured = 1
}

public sealed class PlateauDatasetSelector
{
    public PlateauTexturePreference TexturePreference { get; set; } = PlateauTexturePreference.PreferTextured;

    public PlateauDatasetEntry? SelectBest(IEnumerable<PlateauDatasetEntry> candidates)
    {
        if (candidates is null) throw new ArgumentNullException(nameof(candidates));
        return candidates.OrderByDescending(c => c, PlateauDatasetPreferenceComparer.For(TexturePreference)).FirstOrDefault();
    }

    public IReadOnlyList<PlateauDatasetEntry> SelectByTypes(
        PlateauCatalog catalog,
        PlateauAreaOption area,
        IEnumerable<string> typeEnCodes)
    {
        if (catalog is null) throw new ArgumentNullException(nameof(catalog));
        if (area is null) throw new ArgumentNullException(nameof(area));

        HashSet<string> wanted = new HashSet<string>(typeEnCodes ?? Array.Empty<string>(), StringComparer.Ordinal);
        Dictionary<string, List<PlateauDatasetEntry>> byType = new Dictionary<string, List<PlateauDatasetEntry>>(StringComparer.Ordinal);

        foreach (PlateauDatasetEntry dataset in catalog.Datasets)
        {
            string? typeEn = dataset.TypeEn;
            if (typeEn is null) continue;
            if (!wanted.Contains(typeEn)) continue;
            if (!AreaMatchesDataset(area, dataset)) continue;
            if (!byType.TryGetValue(typeEn, out List<PlateauDatasetEntry>? list))
            {
                list = new List<PlateauDatasetEntry>();
                byType[typeEn] = list;
            }
            list.Add(dataset);
        }

        List<PlateauDatasetEntry> picks = new List<PlateauDatasetEntry>();
        foreach (string code in wanted)
        {
            if (!byType.TryGetValue(code, out List<PlateauDatasetEntry>? rows)) continue;
            PlateauDatasetEntry? best = SelectBest(rows);
            if (best is not null) picks.Add(best);
        }
        return picks;
    }

    public static bool AreaMatchesDataset(PlateauAreaOption area, PlateauDatasetEntry dataset)
    {
        if (area.MatchesCode(PlateauCatalog.NormalizeCode(dataset.CityCode))) return true;
        if (area.MatchesCode(PlateauCatalog.NormalizeCode(dataset.WardCode))) return true;
        return false;
    }
}

internal sealed class PlateauDatasetPreferenceComparer : IComparer<PlateauDatasetEntry>
{
    private readonly PlateauTexturePreference texturePreference;

    private PlateauDatasetPreferenceComparer(PlateauTexturePreference texturePreference)
    {
        this.texturePreference = texturePreference;
    }

    public static PlateauDatasetPreferenceComparer For(PlateauTexturePreference texturePreference) => new(texturePreference);

    public int Compare(PlateauDatasetEntry? x, PlateauDatasetEntry? y)
    {
        if (x is null && y is null) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int tex = TextureRank(x.Texture).CompareTo(TextureRank(y.Texture));
        if (tex != 0) return tex;

        int lod = LodRank(x.Lod).CompareTo(LodRank(y.Lod));
        if (lod != 0) return lod;

        int cat = CatalogRank(x.CatalogSource).CompareTo(CatalogRank(y.CatalogSource));
        if (cat != 0) return cat;

        int year = YearRank(x).CompareTo(YearRank(y));
        return year;
    }

    private int TextureRank(bool? texture)
    {
        // Higher rank = better. Two preference modes flip the ordering of true vs false.
        if (texturePreference == PlateauTexturePreference.PreferTextured)
        {
            return texture switch
            {
                true => 2,
                false => 1,
                _ => 0
            };
        }
        return texture switch
        {
            false => 2,
            true => 1,
            _ => 0
        };
    }

    private static int LodRank(string? lod)
    {
        if (string.IsNullOrWhiteSpace(lod)) return -1;
        return int.TryParse(lod, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : -1;
    }

    private static int CatalogRank(PlateauCatalogSource source) => source switch
    {
        PlateauCatalogSource.Latest => 1,
        _ => 0
    };

    private static int YearRank(PlateauDatasetEntry entry)
    {
        int direct = entry.Year ?? -1;
        int registration = entry.RegistrationYear ?? -1;
        return Math.Max(direct, registration);
    }
}
