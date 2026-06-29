using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace RevitGeoSuite.Core.Plateau.Catalog;

public sealed class PlateauApiClient
{
    public const string DefaultCatalogUrl = "https://api.plateauview.mlit.go.jp/datacatalog/plateau-datasets";
    public const string DefaultReverseGeocoderUrl = "https://mreversegeocoder.gsi.go.jp/reverse-geocoder/LonLatToAddress";

    private static readonly TimeSpan CatalogCacheTtl = TimeSpan.FromDays(1);
    private static readonly TimeSpan ReverseGeocodeCacheTtl = TimeSpan.FromDays(30);

    private readonly IPlateauHttpClient httpClient;
    private readonly Uri catalogUrl;
    private readonly Uri reverseGeocoderUrl;
    private readonly ResponseCache cache;

    public PlateauApiClient(IPlateauHttpClient httpClient)
        : this(httpClient, new Uri(DefaultCatalogUrl), new Uri(DefaultReverseGeocoderUrl))
    {
    }

    public PlateauApiClient(IPlateauHttpClient httpClient, Uri catalogUrl, Uri reverseGeocoderUrl)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.catalogUrl = catalogUrl ?? throw new ArgumentNullException(nameof(catalogUrl));
        this.reverseGeocoderUrl = reverseGeocoderUrl ?? throw new ArgumentNullException(nameof(reverseGeocoderUrl));
        cache = new ResponseCache();
    }

    public async Task<PlateauCatalog> FetchCatalogAsync(CancellationToken cancellationToken = default)
    {
        const string cacheKey = "plateau-catalog-v1";

        string? cached = await cache.TryGetAsync(cacheKey, CatalogCacheTtl).ConfigureAwait(false);
        if (cached is not null)
        {
            PlateauCatalogResponse? cachedResponse = JsonConvert.DeserializeObject<PlateauCatalogResponse>(cached);
            if (cachedResponse is not null) return PlateauCatalog.Normalize(cachedResponse);
        }

        string body;
        try
        {
            body = await httpClient.GetStringAsync(catalogUrl, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            string? stale = cache.TryGetStale(cacheKey);
            if (stale is not null)
            {
                PlateauCatalogResponse? staleResponse = JsonConvert.DeserializeObject<PlateauCatalogResponse>(stale);
                if (staleResponse is not null) return PlateauCatalog.Normalize(staleResponse);
            }
            throw;
        }

        await cache.StoreAsync(cacheKey, body).ConfigureAwait(false);
        PlateauCatalogResponse? response = JsonConvert.DeserializeObject<PlateauCatalogResponse>(body)
            ?? throw new InvalidOperationException("PLATEAU catalog response was empty or invalid.");
        return PlateauCatalog.Normalize(response);
    }

    public async Task<string?> ReverseGeocodeMunicipalityCodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        string cacheKey = string.Format(CultureInfo.InvariantCulture, "reverse-geocode-{0:0.0000}-{1:0.0000}", latitude, longitude);

        string? cached = await cache.TryGetAsync(cacheKey, ReverseGeocodeCacheTtl).ConfigureAwait(false);
        if (cached is not null) return cached;

        UriBuilder builder = new UriBuilder(reverseGeocoderUrl)
        {
            Query = string.Format(
                CultureInfo.InvariantCulture,
                "lat={0:0.######}&lon={1:0.######}",
                latitude,
                longitude)
        };
        string body = await httpClient.GetStringAsync(builder.Uri, cancellationToken).ConfigureAwait(false);
        ReverseGeocodeResult? result = JsonConvert.DeserializeObject<ReverseGeocodeResult>(body);
        string? code = PlateauCatalog.NormalizeCode(result?.Results?.MuniCd);

        if (code is not null)
        {
            await cache.StoreAsync(cacheKey, code).ConfigureAwait(false);
        }

        return code;
    }

    private sealed class ReverseGeocodeResult
    {
        [JsonProperty("results")]
        public ReverseGeocodeResultBody? Results { get; set; }
    }

    private sealed class ReverseGeocodeResultBody
    {
        [JsonProperty("muniCd")]
        public string? MuniCd { get; set; }
    }
}
