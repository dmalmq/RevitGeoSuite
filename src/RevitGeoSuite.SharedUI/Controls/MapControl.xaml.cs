using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;

namespace RevitGeoSuite.SharedUI.Controls;

public partial class MapControl : UserControl
{
    private readonly Queue<string> pendingMessages;
    private bool isInitialized;
    private bool isMapReady;

    public MapControl()
    {
        InitializeComponent();
        pendingMessages = new Queue<string>();
        Loaded += OnLoaded;
    }

    public event EventHandler<MapPointSelectedEventArgs>? MapPointSelected;

    public async Task SearchAsync(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return;
        }

        string trimmedQuery = query.Trim();
        if (MapSearchQueryParser.TryParseCoordinatePair(trimmedQuery, out double latitude, out double longitude))
        {
            await SetViewAsync(latitude, longitude, 17);
            await SetMarkerAsync(latitude, longitude);
            return;
        }

        await PostMessageAsync(new { type = "searchLocation", query = trimmedQuery });
    }

    public async Task SetViewAsync(double latitude, double longitude, int zoom)
    {
        await PostMessageAsync(new { type = "setView", latitude, longitude, zoom });
    }

    public async Task SetMarkerAsync(double latitude, double longitude)
    {
        await PostMessageAsync(new { type = "setMarker", latitude, longitude });
    }

    public async Task ClearMarkerAsync()
    {
        await PostMessageAsync(new { type = "clearMarker" });
    }

    public async Task SetPointSelectionEnabledAsync(bool enabled)
    {
        await PostMessageAsync(new { type = "setPointSelectionEnabled", enabled });
    }

    public async Task ShowMeshGridAsync(string geoJson)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            return;
        }

        await PostMessageAsync(new { type = "showMeshGrid", geoJson });
    }

    public async Task ClearMeshGridAsync()
    {
        await PostMessageAsync(new { type = "clearMeshGrid" });
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (!isInitialized)
        {
            await EnsureInitializedAsync();
        }
    }

    private async Task EnsureInitializedAsync()
    {
        if (isInitialized)
        {
            return;
        }

        try
        {
            string hostHtml = ReadMapHostHtml();
            MapHostEnvironment.EnsureHostPage(hostHtml);

            CoreWebView2Environment environment = await MapHostEnvironment.CreateAsync();
            await MapBrowser.EnsureCoreWebView2Async(environment);
            ConfigureBrowserSettings(MapBrowser.CoreWebView2);
            MapBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                MapHostEnvironment.HostName,
                MapHostEnvironment.GetHostAssetFolder(),
                CoreWebView2HostResourceAccessKind.Allow);
            MapBrowser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            MapBrowser.Source = MapHostEnvironment.GetHostPageUri();
            isInitialized = true;
        }
        catch (Exception ex)
        {
            ShowFallback($"Map host could not start. {ex.Message}");
        }
    }

    private async Task PostMessageAsync(object payload)
    {
        await EnsureInitializedAsync();

        if (!isInitialized || MapBrowser.CoreWebView2 is null)
        {
            return;
        }

        string json = JsonConvert.SerializeObject(payload);
        if (!isMapReady)
        {
            pendingMessages.Enqueue(json);
            return;
        }

        MapBrowser.CoreWebView2.PostWebMessageAsJson(json);
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        MapBridgeMessage message = MapBridgeMessageParser.Parse(e.WebMessageAsJson);

        if (string.Equals(message.Type, "ready", StringComparison.OrdinalIgnoreCase))
        {
            isMapReady = true;
            while (pendingMessages.Count > 0)
            {
                MapBrowser.CoreWebView2?.PostWebMessageAsJson(pendingMessages.Dequeue());
            }

            return;
        }

        if (string.Equals(message.Type, "mapClick", StringComparison.OrdinalIgnoreCase)
            && message.Latitude.HasValue
            && message.Longitude.HasValue)
        {
            MapPointSelected?.Invoke(this, new MapPointSelectedEventArgs(message.Latitude.Value, message.Longitude.Value));
        }
    }

    private static void ConfigureBrowserSettings(CoreWebView2 coreWebView2)
    {
        coreWebView2.Settings.AreDefaultContextMenusEnabled = false;
        coreWebView2.Settings.IsStatusBarEnabled = false;

        string existingUserAgent = coreWebView2.Settings.UserAgent ?? string.Empty;
        if (existingUserAgent.IndexOf("RevitGeoSuite/", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return;
        }

        coreWebView2.Settings.UserAgent = string.IsNullOrWhiteSpace(existingUserAgent)
            ? "RevitGeoSuite/0.6"
            : $"{existingUserAgent} RevitGeoSuite/0.6";
    }

    private static string ReadMapHostHtml()
    {
        Assembly assembly = typeof(MapControl).Assembly;
        string resourceName = assembly
            .GetManifestResourceNames()
            .Single(name => name.EndsWith("MapHost.html", StringComparison.Ordinal));

        using Stream stream = assembly.GetManifestResourceStream(resourceName)!;
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private void ShowFallback(string message)
    {
        FallbackText.Text = message;
        FallbackOverlay.Visibility = Visibility.Visible;
    }
}
