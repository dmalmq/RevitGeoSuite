using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.PlateauImport.Online;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>
/// Builds the bilingual searchable list of municipalities that have downloadable CityGML on
/// geospatial.jp. The downloadable set + per-year slugs come from the CKAN index (cached
/// <c>package_list</c>); the JP/EN/romaji search tokens are reused from the MLIT online catalog so a
/// search for "Shibuya", "渋谷", or "13113" all resolve the same row.
/// </summary>
public sealed class PlateauCkanCatalogHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public PlateauCkanCatalogHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.ckanCatalog";

    public Task<object?> HandleAsync(object? payload)
    {
        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "ckan", Percent = 10, Message = "Loading geospatial.jp dataset index…" });

            PlateauHttpClient httpClient = new PlateauHttpClient();
            GeospatialCkanClient ckan = new GeospatialCkanClient(httpClient);

            IReadOnlyList<string> slugs = await CkanIndexCache.GetOrFetchAsync(ckan, ct).ConfigureAwait(false);
            IReadOnlyList<CkanMunicipalityDatasets> municipalities = GeospatialCkanClient.GroupSlugs(slugs);

            progress.Report(new JobProgress { Phase = "names", Percent = 55, Message = "Loading municipality names…" });
            Dictionary<string, PlateauAreaOption> areaByCode = await TryBuildAreaLookupAsync(httpClient, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            progress.Report(new JobProgress { Phase = "indexing", Percent = 85, Message = "Indexing municipalities…" });

            PlateauCkanArea[] areas = municipalities
                .Select(municipality => BuildArea(municipality, areaByCode))
                .OrderBy(area => area.DisplayLabel, StringComparer.Ordinal)
                .ToArray();

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Catalog loaded" });

            return (object?)new PlateauCkanCatalogResponse
            {
                Areas = areas,
                AreaCount = areas.Length
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static async Task<Dictionary<string, PlateauAreaOption>> TryBuildAreaLookupAsync(
        PlateauHttpClient httpClient,
        CancellationToken cancellationToken)
    {
        Dictionary<string, PlateauAreaOption> lookup = new(StringComparer.Ordinal);
        try
        {
            PlateauCatalog catalog = await new PlateauApiClient(httpClient).FetchCatalogAsync(cancellationToken).ConfigureAwait(false);
            foreach (PlateauAreaOption area in catalog.AreaOptions)
            {
                lookup[area.Code] = area;
                foreach (string alias in area.Aliases)
                {
                    if (!lookup.ContainsKey(alias))
                    {
                        lookup[alias] = area;
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // MLIT catalog is best-effort enrichment; fall back to romaji-only labels when unavailable.
        }

        return lookup;
    }

    private static PlateauCkanArea BuildArea(
        CkanMunicipalityDatasets municipality,
        Dictionary<string, PlateauAreaOption> areaByCode)
    {
        string[] years = municipality.Datasets.Select(dataset => dataset.Year).ToArray();
        Dictionary<string, string> slugByYear = municipality.Datasets
            .ToDictionary(dataset => dataset.Year, dataset => dataset.Slug, StringComparer.Ordinal);

        double? latitude = null;
        double? longitude = null;
        if (MunicipalityCentroids.TryGet(municipality.Code, out double centroidLat, out double centroidLon))
        {
            latitude = centroidLat;
            longitude = centroidLon;
        }

        string displayLabel;
        string codeLabel;
        string searchTokens;

        if (areaByCode.TryGetValue(municipality.Code, out PlateauAreaOption? area) && area is not null)
        {
            AreaSearchOption option = PlateauOnlineAreaSearch.BuildOption(area);
            displayLabel = option.DisplayLabel;
            codeLabel = option.CodeLabel;
            searchTokens = option.SearchTokens;
        }
        else
        {
            string romajiDisplay = TitleizeRomaji(municipality.Romaji);
            displayLabel = $"{romajiDisplay} ({municipality.Code})";
            codeLabel = municipality.Code;
            searchTokens = string.Join("", new[]
            {
                municipality.Code,
                municipality.Romaji,
                municipality.Romaji.Replace("-", string.Empty)
            }).ToLowerInvariant();
        }

        return new PlateauCkanArea
        {
            Code = municipality.Code,
            DisplayLabel = displayLabel,
            CodeLabel = codeLabel,
            EnglishName = TitleizeRomaji(municipality.Romaji),
            SearchTokens = searchTokens,
            Latitude = latitude,
            Longitude = longitude,
            Years = years,
            LatestYear = years.Length > 0 ? years[0] : string.Empty,
            SlugByYear = slugByYear
        };
    }

    private static string TitleizeRomaji(string romaji)
    {
        if (string.IsNullOrEmpty(romaji))
        {
            return romaji;
        }

        return char.ToUpperInvariant(romaji[0]) + romaji.Substring(1);
    }
}

/// <summary>Lists the CityGML resources (feature/LOD ZIPs) of one CKAN dataset slug.</summary>
public sealed class PlateauCkanResourcesHandler : IRpcHandler
{
    public string Method => "plateau.ckanResources";

    public async Task<object?> HandleAsync(object? payload)
    {
        string? slug = (payload as JObject)?.Value<string>("slug");
        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new InvalidOperationException("A dataset slug is required.");
        }

        PlateauHttpClient httpClient = new PlateauHttpClient();
        GeospatialCkanClient ckan = new GeospatialCkanClient(httpClient);
        IReadOnlyList<CkanCityGmlResource> resources = await ckan
            .GetCityGmlResourcesAsync(slug!, CancellationToken.None)
            .ConfigureAwait(false);

        string? defaultId = ResolveDefaultResourceId(resources);

        PlateauCkanResource[] dtos = resources
            .Select(resource => new PlateauCkanResource
            {
                Id = resource.Id,
                FeatureKey = resource.FeatureKey,
                FeatureLabel = resource.FeatureLabel,
                Lod = resource.Lod,
                SizeBytes = resource.SizeBytes,
                Url = resource.Url,
                FileName = resource.FileName,
                DefaultSelected = resource.Id == defaultId
            })
            .ToArray();

        return new PlateauCkanResourcesResponse
        {
            Slug = slug!,
            Resources = dtos
        };
    }

    // Pre-check the highest-LOD building resource by default.
    private static string? ResolveDefaultResourceId(IReadOnlyList<CkanCityGmlResource> resources)
    {
        CkanCityGmlResource? best = null;
        double bestLod = double.NegativeInfinity;
        foreach (CkanCityGmlResource resource in resources)
        {
            if (!IsBuilding(resource.FeatureKey))
            {
                continue;
            }

            double lod = ParseLod(resource.Lod);
            if (best is null || lod > bestLod)
            {
                best = resource;
                bestLod = lod;
            }
        }

        return best?.Id;
    }

    private static bool IsBuilding(string featureKey)
    {
        return featureKey.Equals("building", StringComparison.OrdinalIgnoreCase)
            || featureKey.Equals("bldg", StringComparison.OrdinalIgnoreCase);
    }

    private static double ParseLod(string lod)
    {
        return double.TryParse(lod, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double value)
            ? value
            : -1;
    }
}

/// <summary>Streams and extracts the selected CityGML resource ZIPs, returning the package folder.</summary>
public sealed class PlateauCkanDownloadHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public PlateauCkanDownloadHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.ckanDownload";

    public Task<object?> HandleAsync(object? payload)
    {
        JObject? request = payload as JObject;
        string code = request?.Value<string>("code") ?? string.Empty;
        string year = request?.Value<string>("year") ?? string.Empty;
        string destinationFolder = request?.Value<string>("destinationFolder") ?? string.Empty;
        string areaName = request?.Value<string>("areaName") ?? string.Empty;
        bool force = request?.Value<bool>("force") ?? false;
        string[] resourceUrls = (request?.Value<JArray>("resourceUrls"))?
            .Select(token => token?.Value<string>())
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .ToArray() ?? Array.Empty<string>();

        if (string.IsNullOrWhiteSpace(code))
        {
            throw new InvalidOperationException("A municipality code is required.");
        }

        if (resourceUrls.Length == 0)
        {
            throw new InvalidOperationException("Select at least one CityGML resource to download.");
        }

        string jobId = jobs.Start(async (ct, progress) =>
        {
            PlateauHttpClient httpClient = new PlateauHttpClient();
            CityGmlPackageDownloader downloader = string.IsNullOrWhiteSpace(destinationFolder)
                ? new CityGmlPackageDownloader(httpClient)
                : new CityGmlPackageDownloader(httpClient, destinationFolder);

            IProgress<CityGmlPackageDownloadProgress> downloadProgress = new Progress<CityGmlPackageDownloadProgress>(p =>
            {
                int percent = p.Total > 0
                    ? (int)Math.Round((p.Completed + p.CurrentFileFraction) / p.Total * 100.0)
                    : 0;
                progress.Report(new JobProgress
                {
                    Phase = "downloading",
                    Current = p.Completed,
                    Total = p.Total,
                    Percent = Math.Max(0, Math.Min(99, percent)),
                    Message = $"Downloading {p.CurrentItem}…"
                });
            });

            CityGmlPackageDownloadResult result = await downloader
                .DownloadAsync(code, year, resourceUrls, downloadProgress, ct, areaName, force)
                .ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Download complete" });

            return (object?)new PlateauCkanDownloadResponse
            {
                FolderPath = result.FolderPath,
                FilesExtracted = result.FilesExtracted,
                Warnings = result.Warnings.ToArray()
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }
}

/// <summary>Reports which of the selected CityGML resources are already downloaded in the target folder.</summary>
public sealed class PlateauCkanDownloadCheckHandler : IRpcHandler
{
    public string Method => "plateau.ckanDownloadCheck";

    public Task<object?> HandleAsync(object? payload)
    {
        JObject? request = payload as JObject;
        string code = request?.Value<string>("code") ?? string.Empty;
        string year = request?.Value<string>("year") ?? string.Empty;
        string areaName = request?.Value<string>("areaName") ?? string.Empty;
        string destinationFolder = request?.Value<string>("destinationFolder") ?? string.Empty;
        string[] resourceUrls = (request?.Value<JArray>("resourceUrls"))?
            .Select(token => token?.Value<string>())
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url!)
            .ToArray() ?? Array.Empty<string>();

        string[] existing = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(code) && resourceUrls.Length > 0)
        {
            CityGmlPackageDownloader downloader = string.IsNullOrWhiteSpace(destinationFolder)
                ? new CityGmlPackageDownloader(new PlateauHttpClient())
                : new CityGmlPackageDownloader(new PlateauHttpClient(), destinationFolder);
            existing = downloader.GetAlreadyDownloaded(code, year, areaName, resourceUrls).ToArray();
        }

        return Task.FromResult<object?>(new PlateauCkanDownloadCheckResponse { ExistingUrls = existing });
    }
}

/// <summary>
/// Disk cache for the filtered PLATEAU dataset slug list from CKAN <c>package_list</c>. The index
/// changes rarely, so caching it (7-day TTL) keeps the download catalog fast to open.
/// </summary>
internal static class CkanIndexCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(7);

    public static async Task<IReadOnlyList<string>> GetOrFetchAsync(GeospatialCkanClient ckan, CancellationToken cancellationToken)
    {
        string path = CachePath();
        try
        {
            if (File.Exists(path))
            {
                CacheFile? cached = JsonConvert.DeserializeObject<CacheFile>(File.ReadAllText(path));
                if (cached?.Slugs is { Count: > 0 } && DateTime.UtcNow - cached.FetchedUtc < Ttl)
                {
                    return cached.Slugs;
                }
            }
        }
        catch
        {
            // Corrupt/unreadable cache — fall through and refetch.
        }

        IReadOnlyList<string> slugs = await ckan.ListPlateauDatasetSlugsAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(new CacheFile
            {
                FetchedUtc = DateTime.UtcNow,
                Slugs = slugs.ToList()
            }));
        }
        catch
        {
            // Caching is best-effort.
        }

        return slugs;
    }

    private static string CachePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitGeoSuite",
            "Ckan",
            "plateau-index.json");
    }

    private sealed class CacheFile
    {
        public DateTime FetchedUtc { get; set; }
        public List<string>? Slugs { get; set; }
    }
}
