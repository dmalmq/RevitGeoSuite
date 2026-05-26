using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace RevitGeoSuite.Core.Plateau.Catalog;

public sealed class PlateauHttpClient : IPlateauHttpClient, IDisposable
{
    private static readonly Lazy<HttpClient> SharedClient = new(CreateSharedClient, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly HttpClient httpClient;
    private readonly bool ownsClient;

    public PlateauHttpClient()
    {
        httpClient = SharedClient.Value;
        ownsClient = false;
    }

    public PlateauHttpClient(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        ownsClient = false;
    }

    public Task<string> GetStringAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url is null) throw new ArgumentNullException(nameof(url));
        return GetStringAsyncCore(url, cancellationToken);
    }

    private async Task<string> GetStringAsyncCore(Uri url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    }

    public Task<byte[]> GetBytesAsync(Uri url, CancellationToken cancellationToken)
    {
        if (url is null) throw new ArgumentNullException(nameof(url));
        return GetBytesAsyncCore(url, cancellationToken);
    }

    private async Task<byte[]> GetBytesAsyncCore(Uri url, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
    }

    public Task DownloadAsync(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (url is null) throw new ArgumentNullException(nameof(url));
        if (destination is null) throw new ArgumentNullException(nameof(destination));
        return DownloadAsyncCore(url, destination, progress, cancellationToken);
    }

    private async Task DownloadAsyncCore(Uri url, Stream destination, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        long? total = response.Content.Headers.ContentLength;
        using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        byte[] buffer = new byte[81920];
        long readTotal = 0;
        int read;
        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            readTotal += read;
            if (progress is not null && total.HasValue && total.Value > 0)
            {
                progress.Report((double)readTotal / total.Value);
            }
        }
        if (progress is not null) progress.Report(1.0);
    }

    public void Dispose()
    {
        if (ownsClient) httpClient.Dispose();
    }

    private static HttpClient CreateSharedClient()
    {
        ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11;
        HttpClientHandler handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        HttpClient client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RevitGeoSuite-PlateauOnline", "1.0"));
        return client;
    }
}
