using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.CesiumHandoff;
using Xunit;

namespace RevitGeoSuite.CesiumHandoff.Tests;

public sealed class CesiumViewerPushClientTests : IDisposable
{
    private readonly string _packageRoot;

    public CesiumViewerPushClientTests()
    {
        _packageRoot = Path.Combine(Path.GetTempPath(), "cesium-push-tests", Guid.NewGuid().ToString("N"));
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout layout = builder.CreateLayout(_packageRoot);
        File.WriteAllText(Path.Combine(layout.TilesDirectory, "tileset.json"), "{\"asset\":{}}");
        File.WriteAllText(Path.Combine(layout.TilesDirectory, "content.glb"), "GLBBYTES");
        File.WriteAllText(Path.Combine(layout.GisDirectory, "tower.gpkg"), "GPKG");
        builder.WriteManifest(layout, new CesiumPackageBuildInputs
        {
            BuildingId = "tower",
            BuildingName = "Tower",
            GeneratorVersion = "1.0.0",
        });
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_packageRoot))
            {
                Directory.Delete(_packageRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private sealed class StubServer : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly Task _loop;

        public StubServer(int statusCode, string responseBody)
        {
            Port = FindFreePort();
            _listener.Prefixes.Add($"http://localhost:{Port}/");
            _listener.Start();
            _loop = Task.Run(async () =>
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext context;
                    try
                    {
                        context = await _listener.GetContextAsync().ConfigureAwait(false);
                    }
                    catch (HttpListenerException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    RequestPath = context.Request.Url?.AbsolutePath;
                    AuthorizationHeader = context.Request.Headers["Authorization"];
                    ContentType = context.Request.ContentType;
                    using (var memory = new MemoryStream())
                    {
                        await context.Request.InputStream.CopyToAsync(memory).ConfigureAwait(false);
                        RequestBody = Encoding.UTF8.GetString(memory.ToArray());
                    }

                    byte[] body = Encoding.UTF8.GetBytes(responseBody);
                    context.Response.StatusCode = statusCode;
                    context.Response.ContentType = "application/json";
                    context.Response.OutputStream.Write(body, 0, body.Length);
                    context.Response.Close();
                }
            });
        }

        public int Port { get; }

        public string? RequestPath { get; private set; }

        public string? AuthorizationHeader { get; private set; }

        public string? ContentType { get; private set; }

        public string? RequestBody { get; private set; }

        private static int FindFreePort()
        {
            var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public void Dispose()
        {
            try
            {
                _listener.Stop();
                _listener.Close();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    [Fact]
    public async Task PushAsync_SendsMultipartWithManifestAndFiles()
    {
        using var server = new StubServer(200, "{\"ok\":true,\"packageId\":\"p1\"}");
        var client = new CesiumViewerPushClient();

        CesiumViewerPushResult result = await client.PushAsync(
            new CesiumViewerPushRequest
            {
                ViewerUrl = $"http://localhost:{server.Port}",
                PackageRoot = _packageRoot,
            },
            CancellationToken.None);

        Assert.Equal(CesiumViewerPushStatus.Success, result.Status);
        Assert.Equal("/api/import-package", server.RequestPath);
        Assert.StartsWith("multipart/form-data", server.ContentType);
        Assert.Contains("cesium-package.json", server.RequestBody);
        Assert.Contains("tiles/tileset.json", server.RequestBody);
        Assert.Contains("tiles/content.glb", server.RequestBody);
        Assert.Contains("gis/tower.gpkg", server.RequestBody);
    }

    [Fact]
    public async Task PushAsync_SendsBearerTokenWhenConfigured()
    {
        using var server = new StubServer(200, "{\"ok\":true}");
        var client = new CesiumViewerPushClient();

        await client.PushAsync(
            new CesiumViewerPushRequest
            {
                ViewerUrl = $"http://localhost:{server.Port}",
                PackageRoot = _packageRoot,
                Token = "secret-token",
            },
            CancellationToken.None);

        Assert.Equal("Bearer secret-token", server.AuthorizationHeader);
    }

    [Fact]
    public async Task PushAsync_ServerErrorReportsServerError()
    {
        using var server = new StubServer(401, "{\"error\":\"unauthorized\"}");
        var client = new CesiumViewerPushClient();

        CesiumViewerPushResult result = await client.PushAsync(
            new CesiumViewerPushRequest
            {
                ViewerUrl = $"http://localhost:{server.Port}",
                PackageRoot = _packageRoot,
            },
            CancellationToken.None);

        Assert.Equal(CesiumViewerPushStatus.ServerError, result.Status);
        Assert.Contains("401", result.Message);
    }

    [Fact]
    public async Task PushAsync_UnreachableViewerReportsUnreachable()
    {
        var client = new CesiumViewerPushClient();

        CesiumViewerPushResult result = await client.PushAsync(
            new CesiumViewerPushRequest
            {
                // A port from the TEST-NET range that nothing listens on locally.
                ViewerUrl = "http://localhost:1",
                PackageRoot = _packageRoot,
            },
            CancellationToken.None);

        Assert.Equal(CesiumViewerPushStatus.Unreachable, result.Status);
    }

    [Fact]
    public async Task PushAsync_MissingManifestThrows()
    {
        var client = new CesiumViewerPushClient();
        string emptyRoot = Path.Combine(Path.GetTempPath(), "cesium-push-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);
        try
        {
            await Assert.ThrowsAsync<FileNotFoundException>(() => client.PushAsync(
                new CesiumViewerPushRequest { ViewerUrl = "http://localhost:1", PackageRoot = emptyRoot },
                CancellationToken.None));
        }
        finally
        {
            Directory.Delete(emptyRoot, recursive: true);
        }
    }
}
