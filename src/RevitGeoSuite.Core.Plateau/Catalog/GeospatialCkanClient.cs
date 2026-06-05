using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RevitGeoSuite.Core.Plateau.Catalog;

/// <summary>
/// Minimal client for the geospatial.jp CKAN action API used to discover and list PLATEAU CityGML
/// datasets. Discovery by code via <c>package_search</c> is unreliable on this instance, so the index
/// is built from <c>package_list</c> (all dataset slugs) filtered to the PLATEAU naming pattern, and
/// resources are read via <c>package_show</c> on the exact slug.
/// </summary>
public sealed class GeospatialCkanClient
{
    public const string DefaultApiBase = "https://www.geospatial.jp/ckan/api/3/action/";

    // plateau-{5-digit code}-{romaji}-{4-digit year}, e.g. plateau-13113-shibuya-ku-2023
    private static readonly Regex SlugPattern = new(
        @"^plateau-(\d{5})-(.+)-(\d{4})$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly IPlateauHttpClient http;
    private readonly Uri apiBase;

    public GeospatialCkanClient(IPlateauHttpClient http)
        : this(http, new Uri(DefaultApiBase))
    {
    }

    public GeospatialCkanClient(IPlateauHttpClient http, Uri apiBase)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.apiBase = apiBase ?? throw new ArgumentNullException(nameof(apiBase));
    }

    /// <summary>Returns every dataset slug on the instance that matches the PLATEAU naming pattern.</summary>
    public async Task<IReadOnlyList<string>> ListPlateauDatasetSlugsAsync(CancellationToken cancellationToken)
    {
        Uri url = new Uri(apiBase, "package_list");
        string body = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        CkanEnvelope<List<string>>? envelope = JsonConvert.DeserializeObject<CkanEnvelope<List<string>>>(body);
        IEnumerable<string> slugs = envelope?.Result ?? Enumerable.Empty<string>();
        return slugs
            .Where(slug => !string.IsNullOrWhiteSpace(slug) && SlugPattern.IsMatch(slug.Trim()))
            .Select(slug => slug.Trim())
            .ToList();
    }

    /// <summary>Convenience: list slugs and group them into per-municipality datasets.</summary>
    public async Task<IReadOnlyList<CkanMunicipalityDatasets>> ListPlateauMunicipalitiesAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> slugs = await ListPlateauDatasetSlugsAsync(cancellationToken).ConfigureAwait(false);
        return GroupSlugs(slugs);
    }

    /// <summary>Groups PLATEAU dataset slugs by municipality code, newest year first.</summary>
    public static IReadOnlyList<CkanMunicipalityDatasets> GroupSlugs(IEnumerable<string> slugs)
    {
        Dictionary<string, MunicipalityBuilder> byCode = new(StringComparer.Ordinal);
        foreach (string slug in slugs ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            Match match = SlugPattern.Match(slug.Trim());
            if (!match.Success)
            {
                continue;
            }

            string code = match.Groups[1].Value;
            string romaji = match.Groups[2].Value;
            string year = match.Groups[3].Value;

            if (!byCode.TryGetValue(code, out MunicipalityBuilder? builder))
            {
                builder = new MunicipalityBuilder { Code = code, Romaji = romaji };
                byCode[code] = builder;
            }

            // Keep the newest romaji form seen (later years tend to use canonical spelling).
            if (string.CompareOrdinal(year, builder.LatestYear) > 0)
            {
                builder.LatestYear = year;
                builder.Romaji = romaji;
            }

            builder.SlugByYear[year] = slug.Trim();
        }

        return byCode.Values
            .Select(builder => new CkanMunicipalityDatasets(
                builder.Code,
                builder.Romaji,
                builder.SlugByYear
                    .OrderByDescending(pair => pair.Key, StringComparer.Ordinal)
                    .Select(pair => new CkanYearDataset(pair.Key, pair.Value))
                    .ToList()))
            .OrderBy(municipality => municipality.Code, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Reads a dataset's CityGML (ZIP) resources via <c>package_show</c>.</summary>
    public async Task<IReadOnlyList<CkanCityGmlResource>> GetCityGmlResourcesAsync(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Dataset slug is required.", nameof(slug));
        }

        Uri url = new Uri(apiBase, "package_show?id=" + Uri.EscapeDataString(slug.Trim()));
        string body = await http.GetStringAsync(url, cancellationToken).ConfigureAwait(false);
        CkanEnvelope<CkanPackage>? envelope = JsonConvert.DeserializeObject<CkanEnvelope<CkanPackage>>(body);
        CkanPackage? package = envelope?.Result;
        if (package?.Resources is null)
        {
            return Array.Empty<CkanCityGmlResource>();
        }

        return ParseCityGmlResources(package.Resources);
    }

    /// <summary>Filters a resource list down to CityGML ZIP downloads, parsing feature/LOD/size.</summary>
    public static IReadOnlyList<CkanCityGmlResource> ParseCityGmlResources(IEnumerable<CkanResource> resources)
    {
        List<CkanCityGmlResource> result = new();
        foreach (CkanResource resource in resources ?? Enumerable.Empty<CkanResource>())
        {
            if (resource is null || string.IsNullOrWhiteSpace(resource.Url))
            {
                continue;
            }

            string url = resource.Url!.Trim();
            bool isCityGml = string.Equals(resource.Format, "CityGML", StringComparison.OrdinalIgnoreCase)
                || (url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    && (resource.Name?.IndexOf("citygml", StringComparison.OrdinalIgnoreCase) >= 0));
            if (!isCityGml)
            {
                continue;
            }

            string fileName = FileNameFromUrl(url);
            (string featureKey, string lod) = CkanResourceNaming.ParseFeatureAndLod(
                string.IsNullOrWhiteSpace(resource.Name) ? fileName : resource.Name);

            result.Add(new CkanCityGmlResource(
                id: string.IsNullOrWhiteSpace(resource.Id) ? url : resource.Id!,
                featureKey: featureKey,
                featureLabel: CkanResourceNaming.FeatureLabel(featureKey),
                lod: lod,
                sizeBytes: CkanResourceNaming.ParseSizeBytes(resource.Size),
                url: url,
                fileName: fileName));
        }

        return result
            .OrderBy(item => item.FeatureLabel, StringComparer.Ordinal)
            .ThenBy(item => item.Lod, StringComparer.Ordinal)
            .ToList();
    }

    private static string FileNameFromUrl(string url)
    {
        try
        {
            Uri uri = new Uri(url);
            string name = uri.Segments.Length > 0 ? uri.Segments[uri.Segments.Length - 1] : url;
            return Uri.UnescapeDataString(name.TrimEnd('/'));
        }
        catch (UriFormatException)
        {
            int slash = url.LastIndexOf('/');
            return slash >= 0 && slash < url.Length - 1 ? url.Substring(slash + 1) : url;
        }
    }

    private sealed class MunicipalityBuilder
    {
        public string Code { get; set; } = string.Empty;
        public string Romaji { get; set; } = string.Empty;
        public string LatestYear { get; set; } = string.Empty;
        public Dictionary<string, string> SlugByYear { get; } = new(StringComparer.Ordinal);
    }
}
