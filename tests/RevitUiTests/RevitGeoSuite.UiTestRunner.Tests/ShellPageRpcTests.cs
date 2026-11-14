using System.Net;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using Microsoft.Playwright;
using RevitGeoSuite.UiTestRunner;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

/// <summary>
/// Sends bridge requests through <see cref="ShellPage"/> to a stand-in page whose chrome.webview.postMessage
/// copies the message through JSON as WebView2 does and echoes the payload back. It runs where a Chromium is
/// found (REVITGEOSUITE_TEST_CHROMIUM, or /opt/pw-browsers/chromium in the cloud) and returns early elsewhere.
/// </summary>
public sealed class ShellPageRpcTests
{
    private const string StandIn = @"<!doctype html><title>stand-in</title><script>
        const listeners = [];
        window.chrome = window.chrome || {};
        window.chrome.webview = {
            addEventListener: (type, fn) => listeners.push(fn),
            removeEventListener: (type, fn) => { const i = listeners.indexOf(fn); if (i >= 0) listeners.splice(i, 1); },
            postMessage: (message) => {
                const copy = JSON.parse(JSON.stringify(message));
                setTimeout(() => listeners.slice().forEach(fn => fn({ data: { kind: 'res', id: copy.id, payload: { method: copy.method, echo: copy.payload } } })), 0);
            },
        };
    </script>";

    [Fact]
    public async Task A_JsonObject_payload_reaches_the_bridge_as_plain_data()
    {
        string? chromium = Environment.GetEnvironmentVariable("REVITGEOSUITE_TEST_CHROMIUM");
        if (string.IsNullOrEmpty(chromium))
        {
            chromium = File.Exists("/opt/pw-browsers/chromium") ? "/opt/pw-browsers/chromium" : null;
        }

        if (chromium == null)
        {
            return;
        }

        int port = FreePort();
        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new()
        {
            ExecutablePath = chromium,
            Args = new[] { $"--remote-debugging-port={port}", "--remote-debugging-address=127.0.0.1", "--no-sandbox" },
        });
        IPage page = await browser.NewPageAsync();
        await page.RouteAsync($"http://{ShellPage.Host}/**", route => route.FulfillAsync(new() { ContentType = "text/html", Body = StandIn }));
        await page.GotoAsync($"http://{ShellPage.Host}/export/geopackage");

        await using ShellPage shell = await ShellPage.AttachAsync(port, TimeSpan.FromSeconds(10));
        var settings = new JsonObject { ["selectedViewIds"] = new JsonArray(1L, 2L), ["targetEpsg"] = 6677, ["outputDirectory"] = @"C:\run\profile-export" };
        JsonNode? reply = await shell.RpcAsync("floorplan.saveProfile", new { scope = "project", name = "UI test", settings });

        Assert.Equal("floorplan.saveProfile", reply?["method"]?.GetValue<string>());
        Assert.Equal("project", reply?["echo"]?["scope"]?.GetValue<string>());
        Assert.Equal(6677, reply?["echo"]?["settings"]?["targetEpsg"]?.GetValue<int>());
        Assert.Equal(2, reply?["echo"]?["settings"]?["selectedViewIds"]?.AsArray().Count);
        Assert.Equal(@"C:\run\profile-export", reply?["echo"]?["settings"]?["outputDirectory"]?.GetValue<string>());

        JsonNode? empty = await shell.RpcAsync("cesium.export.getState");
        Assert.Equal(0, empty?["echo"]?.AsObject().Count);
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
