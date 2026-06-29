using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace RevitGeoSuite.Core.Plateau.Catalog;

/// <summary>
/// An <see cref="IPlateauHttpClient"/> that attaches a Bearer authorization header to every request.
/// Used for fetching Cesium Ion-hosted 3D Tiles where the short-lived token returned by the Ion
/// endpoint API must accompany all tile/tileset requests.
/// </summary>
public sealed class AuthenticatedPlateauHttpClient : IPlateauHttpClient, IDisposable
{
    private static readonly Lazy<HttpClient> SharedClient = new(CreateClient, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly HttpClient httpClient;
    private readonly string bearerToken;

    public AuthenticatedPlateauHttpClient(string bearerToken)
        : this(bearerToken, SharedClient.Value)
    {
    }

    public AuthenticatedPlateauHttpClient(string bearerToken, HttpClient httpClient)
    {
        this.bearerToken = bearerToken ?? throw new ArgumentNullException(nameof(bearerToken));
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
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
        ApplyBearer(request);
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
        ApplyBearer(request);
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
        ApplyBearer(request);
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
        progress?.Report(1.0);
    }

    public Task DownloadResumableAsync(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (url is null) throw new ArgumentNullException(nameof(url));
        if (string.IsNullOrWhiteSpace(destinationPath)) throw new ArgumentException("Destination path is required.", nameof(destinationPath));
        return DownloadResumableAsyncCore(url, destinationPath, progress, cancellationToken);
    }

    private async Task DownloadResumableAsyncCore(Uri url, string destinationPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        string partialPath = destinationPath + ".partial";
        long existingBytes = 0;
        if (File.Exists(partialPath))
        {
            existingBytes = new FileInfo(partialPath).Length;
        }

        using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
        ApplyBearer(request);
        if (existingBytes > 0)
        {
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingBytes, null);
        }

        using HttpResponseMessage response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        bool isPartialResponse = response.StatusCode == System.Net.HttpStatusCode.PartialContent;
        if (!isPartialResponse)
        {
            response.EnsureSuccessStatusCode();
            existingBytes = 0;
        }

        long? contentLength = response.Content.Headers.ContentLength;
        long totalBytes = contentLength.HasValue ? contentLength.Value + existingBytes : -1;

        FileMode fileMode = isPartialResponse ? FileMode.Append : FileMode.Create;
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);

        using (FileStream destination = new FileStream(partialPath, fileMode, FileAccess.Write, FileShare.None))
        {
            using Stream source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            byte[] buffer = new byte[81920];
            long readTotal = existingBytes;
            int read;
            while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                readTotal += read;
                if (progress is not null && totalBytes > 0)
                {
                    progress.Report((double)readTotal / totalBytes);
                }
            }
        }

        progress?.Report(1.0);
        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }
        File.Move(partialPath, destinationPath);
    }

    public void Dispose()
    {
        // The shared HttpClient is not owned by this instance.
    }

    private void ApplyBearer(HttpRequestMessage request)
    {
        if (!string.IsNullOrEmpty(bearerToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);
        }
    }

    private static HttpClient CreateClient()
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
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("RevitGeoSuite", "1.0"));
        return client;
    }
}
