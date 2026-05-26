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

    private readonly IPlateauHttpClient httpClient;
    private readonly Uri catalogUrl;
    private readonly Uri reverseGeocoderUrl;

    public PlateauApiClient(IPlateauHttpClient httpClient)
        : this(httpClient, new Uri(DefaultCatalogUrl), new Uri(DefaultReverseGeocoderUrl))
    {
    }

    public PlateauApiClient(IPlateauHttpClient httpClient, Uri catalogUrl, Uri reverseGeocoderUrl)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.catalogUrl = catalogUrl ?? throw new ArgumentNullException(nameof(catalogUrl));
        this.reverseGeocoderUrl = reverseGeocoderUrl ?? throw new ArgumentNullException(nameof(reverseGeocoderUrl));
    }

    public async Task<PlateauCatalog> FetchCatalogAsync(CancellationToken cancellationToken = default)
    {
        string body = await httpClient.GetStringAsync(catalogUrl, cancellationToken).ConfigureAwait(false);
        PlateauCatalogResponse? response = JsonConvert.DeserializeObject<PlateauCatalogResponse>(body)
            ?? throw new InvalidOperationException("PLATEAU catalog response was empty or invalid.");
        return PlateauCatalog.Normalize(response);
    }

    public async Task<string?> ReverseGeocodeMunicipalityCodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
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
        return PlateauCatalog.NormalizeCode(result?.Results?.MuniCd);
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
