using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.Core.Plateau.Catalog;

/// <summary>Generic CKAN action-API envelope: <c>{ "success": true, "result": ... }</c>.</summary>
public sealed class CkanEnvelope<T>
{
    [JsonProperty("success")]
    public bool Success { get; set; }

    [JsonProperty("result")]
    public T? Result { get; set; }
}

/// <summary>Subset of a CKAN <c>package_show</c> result we care about.</summary>
public sealed class CkanPackage
{
    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonProperty("resources")]
    public List<CkanResource>? Resources { get; set; }
}

/// <summary>One CKAN resource (download). <c>size</c> may be a number, a numeric string, or null.</summary>
public sealed class CkanResource
{
    [JsonProperty("id")]
    public string? Id { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }

    [JsonProperty("format")]
    public string? Format { get; set; }

    [JsonProperty("url")]
    public string? Url { get; set; }

    [JsonProperty("size")]
    public JToken? Size { get; set; }
}

/// <summary>All PLATEAU CKAN datasets for one municipality code, newest year first.</summary>
public sealed class CkanMunicipalityDatasets
{
    public CkanMunicipalityDatasets(string code, string romaji, IReadOnlyList<CkanYearDataset> datasets)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Romaji = romaji ?? string.Empty;
        Datasets = datasets ?? Array.Empty<CkanYearDataset>();
    }

    public string Code { get; }

    /// <summary>Romaji slug fragment as published (hyphenated, e.g. <c>shibuya-ku</c>).</summary>
    public string Romaji { get; }

    /// <summary>One entry per fiscal year, ordered newest-first.</summary>
    public IReadOnlyList<CkanYearDataset> Datasets { get; }
}

/// <summary>A single municipality dataset vintage (year + CKAN slug).</summary>
public sealed class CkanYearDataset
{
    public CkanYearDataset(string year, string slug)
    {
        Year = year ?? string.Empty;
        Slug = slug ?? string.Empty;
    }

    public string Year { get; }

    public string Slug { get; }
}

/// <summary>One downloadable CityGML resource (a feature/LOD ZIP) inside a dataset.</summary>
public sealed class CkanCityGmlResource
{
    public CkanCityGmlResource(
        string id,
        string featureKey,
        string featureLabel,
        string lod,
        long sizeBytes,
        string url,
        string fileName)
    {
        Id = id ?? string.Empty;
        FeatureKey = featureKey ?? string.Empty;
        FeatureLabel = featureLabel ?? string.Empty;
        Lod = lod ?? string.Empty;
        SizeBytes = sizeBytes;
        Url = url ?? string.Empty;
        FileName = fileName ?? string.Empty;
    }

    public string Id { get; }

    /// <summary>Normalized feature token (e.g. <c>building</c>, <c>tran</c>, <c>dem</c>).</summary>
    public string FeatureKey { get; }

    /// <summary>Human label for the feature (e.g. "Buildings", "Roads").</summary>
    public string FeatureLabel { get; }

    /// <summary>LOD digits parsed from the resource name (e.g. "2", "2.2"); empty when unknown.</summary>
    public string Lod { get; }

    public long SizeBytes { get; }

    public string Url { get; }

    public string FileName { get; }
}

/// <summary>Parses PLATEAU CKAN resource names into feature/LOD/size and maps feature keys to labels.</summary>
public static class CkanResourceNaming
{
    // Resource names follow {code}_{romaji}_{year}_citygml_{feature}_{lodX}_{op}, e.g.
    // "13113_shibuya-ku_2023_citygml_building_lod2_op".
    private static readonly Dictionary<string, string> FeatureLabels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["building"] = "Buildings",
        ["bldg"] = "Buildings",
        ["transportation"] = "Roads",
        ["tran"] = "Roads",
        ["road"] = "Roads",
        ["landuse"] = "Land use",
        ["luse"] = "Land use",
        ["urbanplanning"] = "Urban planning",
        ["urf"] = "Urban planning",
        ["relief"] = "Terrain",
        ["dem"] = "Terrain",
        ["vegetation"] = "Vegetation",
        ["veg"] = "Vegetation",
        ["bridge"] = "Bridges",
        ["brdg"] = "Bridges",
        ["cityfurniture"] = "City furniture",
        ["frn"] = "City furniture",
        ["disaster"] = "Disaster risk",
        ["fld"] = "Flood risk",
        ["lsld"] = "Landslide risk",
        ["tnm"] = "Tsunami risk",
        ["htd"] = "Storm-surge risk",
        ["ifld"] = "Inland-flood risk",
        ["waterway"] = "Waterways",
        ["wtr"] = "Waterways",
        ["railway"] = "Railways",
        ["rwy"] = "Railways",
        ["railroad"] = "Railways",
        ["track"] = "Tracks",
        ["squr"] = "Squares",
        ["tunnel"] = "Tunnels",
        ["tun"] = "Tunnels",
        ["underground"] = "Underground",
        ["unf"] = "Underground facilities",
        ["ubld"] = "Underground buildings",
        ["gen"] = "Generic city objects",
    };

    public static (string FeatureKey, string Lod) ParseFeatureAndLod(string? resourceName)
    {
        if (string.IsNullOrWhiteSpace(resourceName))
        {
            return (string.Empty, string.Empty);
        }

        string name = resourceName!.ToLowerInvariant();
        // Strip a trailing extension if a filename slipped through.
        int dot = name.LastIndexOf('.');
        if (dot > 0)
        {
            name = name.Substring(0, dot);
        }

        string[] parts = name.Split(new[] { '_' }, StringSplitOptions.RemoveEmptyEntries);

        string lod = string.Empty;
        foreach (string part in parts)
        {
            if (part.Length > 3 && part.StartsWith("lod", StringComparison.Ordinal))
            {
                string candidate = part.Substring(3);
                if (candidate.Length > 0 && (char.IsDigit(candidate[0])))
                {
                    lod = candidate;
                    break;
                }
            }
        }

        string feature = string.Empty;
        int citygmlIndex = Array.IndexOf(parts, "citygml");
        if (citygmlIndex >= 0 && citygmlIndex + 1 < parts.Length)
        {
            feature = parts[citygmlIndex + 1];
        }
        else
        {
            // No "citygml" marker — fall back to the first token that matches a known feature key.
            feature = parts.FirstOrDefault(p => FeatureLabels.ContainsKey(p)) ?? string.Empty;
        }

        return (feature, lod);
    }

    public static string FeatureLabel(string featureKey)
    {
        if (string.IsNullOrEmpty(featureKey))
        {
            return "CityGML";
        }

        if (FeatureLabels.TryGetValue(featureKey, out string? label))
        {
            return label;
        }

        return char.ToUpperInvariant(featureKey[0]) + featureKey.Substring(1);
    }

    public static long ParseSizeBytes(JToken? size)
    {
        if (size is null || size.Type == JTokenType.Null)
        {
            return 0;
        }

        switch (size.Type)
        {
            case JTokenType.Integer:
                return size.Value<long>();
            case JTokenType.Float:
                return (long)size.Value<double>();
            default:
                string raw = size.Value<string>() ?? string.Empty;
                return long.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0;
        }
    }
}
