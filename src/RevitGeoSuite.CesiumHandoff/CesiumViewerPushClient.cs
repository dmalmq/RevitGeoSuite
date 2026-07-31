using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace RevitGeoSuite.CesiumHandoff;

public enum CesiumViewerPushStatus
{
    Success,

    /// <summary>The viewer server could not be reached — treat as a soft fallback (folder already written).</summary>
    Unreachable,

    /// <summary>The viewer answered with a non-success status (auth failure, bad package, server bug).</summary>
    ServerError,
}

public sealed class CesiumViewerPushRequest
{
    public string ViewerUrl { get; set; } = string.Empty;

    public string PackageRoot { get; set; } = string.Empty;

    public string? Token { get; set; }
}

public sealed class CesiumViewerPushResult
{
    public CesiumViewerPushResult(CesiumViewerPushStatus status, string message, string? responseBody = null)
    {
        Status = status;
        Message = message;
        ResponseBody = responseBody;
    }

    public CesiumViewerPushStatus Status { get; }

    public string Message { get; }

    public string? ResponseBody { get; }
}

/// <summary>
/// Pushes a package folder to a Cesium viewer's <c>POST /api/import-package</c> endpoint as
/// multipart form data. File parts stream from disk (a content.glb can be hundreds of MB), and
/// each part's name is the file's package-relative path so the server can rebuild the folder.
/// </summary>
public sealed class CesiumViewerPushClient : IDisposable
{
    public const string ImportEndpointPath = "/api/import-package";

    private readonly HttpClient _httpClient;
    private readonly bool _ownsHttpClient;

    public CesiumViewerPushClient(HttpClient? httpClient = null, TimeSpan? timeout = null)
    {
        _ownsHttpClient = httpClient is null;
        _httpClient = httpClient ?? new HttpClient();
        if (_ownsHttpClient)
        {
            // Generous default: multi-hundred-MB GLBs over localhost still finish well within this.
            _httpClient.Timeout = timeout ?? TimeSpan.FromMinutes(10);
        }
    }

    public async Task<CesiumViewerPushResult> PushAsync(CesiumViewerPushRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        string manifestPath = Path.Combine(request.PackageRoot, "cesium-package.json");
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException(
                $"'{manifestPath}' not found. Write the package manifest before pushing.", manifestPath);
        }

        string endpoint = request.ViewerUrl.TrimEnd('/') + ImportEndpointPath;
        var openStreams = new List<Stream>();
        try
        {
            using var content = new MultipartFormDataContent();
            foreach (string relativePath in EnumeratePackageFiles(request.PackageRoot, manifestPath))
            {
                string filePath = Path.Combine(request.PackageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
                openStreams.Add(stream);
                var filePart = new StreamContent(stream);
                filePart.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                content.Add(filePart, relativePath, relativePath);
            }

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = content };
            if (!string.IsNullOrWhiteSpace(request.Token))
            {
                httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", request.Token);
            }

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(httpRequest, cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                return new CesiumViewerPushResult(
                    CesiumViewerPushStatus.Unreachable,
                    $"The Cesium viewer at '{request.ViewerUrl}' could not be reached: {RootMessage(exception)}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return new CesiumViewerPushResult(
                    CesiumViewerPushStatus.Unreachable,
                    $"The push to '{request.ViewerUrl}' timed out.");
            }

            using (response)
            {
                string body = response.Content is null
                    ? string.Empty
                    : await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new CesiumViewerPushResult(
                        CesiumViewerPushStatus.ServerError,
                        $"The Cesium viewer rejected the package ({(int)response.StatusCode} {response.ReasonPhrase}).",
                        body);
                }

                return new CesiumViewerPushResult(CesiumViewerPushStatus.Success, "Package pushed to the Cesium viewer.", body);
            }
        }
        finally
        {
            foreach (Stream stream in openStreams)
            {
                stream.Dispose();
            }
        }
    }

    private static IEnumerable<string> EnumeratePackageFiles(string packageRoot, string manifestPath)
    {
        yield return "cesium-package.json";
        CesiumPackageManifest manifest = CesiumPackageManifestSerializer.Deserialize(File.ReadAllText(manifestPath));
        foreach (string relativePath in CesiumPackagePayloadResolver.Resolve(packageRoot, manifest))
        {
            string fullPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException($"Manifest payload '{relativePath}' was not found.", fullPath);
            }

            yield return relativePath;
        }
    }

    private static string RootMessage(Exception exception)
    {
        Exception current = exception;
        while (current.InnerException != null)
        {
            current = current.InnerException;
        }

        return current.Message;
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _httpClient.Dispose();
        }
    }
}
