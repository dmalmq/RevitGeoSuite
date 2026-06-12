using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace RevitGeoSuite.Core.Plateau.Terrain;

/// <summary>
/// Default <see cref="ICesiumTerrainTransport"/> over a shared <see cref="HttpClient"/> with gzip
/// decompression enabled (Cesium serves quantized-mesh tiles gzip-encoded). Sets the Bearer token Ion
/// hands back from the asset endpoint, and the quantized-mesh Accept header for tile requests.
/// </summary>
public sealed class CesiumTerrainHttpTransport : ICesiumTerrainTransport
{
    private static readonly Lazy<HttpClient> SharedClient = new(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly HttpClient httpClient;

    public CesiumTerrainHttpTransport()
        : this(SharedClient.Value)
    {
    }

    public CesiumTerrainHttpTransport(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<string> GetJsonAsync(Uri url, string? bearerToken, CancellationToken cancellationToken)
    {
        if (url is null) throw new ArgumentNullException(nameof(url));

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        ApplyBearer(request, bearerToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public async Task<byte[]> GetTerrainTileAsync(Uri url, string? bearerToken, CancellationToken cancellationToken)
    {
        if (url is null) throw new ArgumentNullException(nameof(url));

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.quantized-mesh"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream") { Quality = 0.9 });
        ApplyBearer(request, bearerToken);
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    private static void ApplyBearer(HttpRequestMessage request, string? bearerToken)
    {
        if (!string.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }

    private static HttpClient CreateClient()
    {
        HttpClientHandler handler = new HttpClientHandler();
        if (handler.SupportsAutomaticDecompression)
        {
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        }

        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(60)
        };
    }
}
