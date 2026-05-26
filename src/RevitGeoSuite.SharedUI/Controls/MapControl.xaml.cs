using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    private readonly AsyncInitializationGate initializationGate;
    private readonly Queue<string> pendingMessages;
    private bool isInitialized;
    private bool isMapReady;

    public MapControl()
    {
        InitializeComponent();
        initializationGate = new AsyncInitializationGate();
        pendingMessages = new Queue<string>();
        Loaded += OnLoaded;
    }

    public event EventHandler<MapPointSelectedEventArgs>? MapPointSelected;

    public event EventHandler<MapOverlayFeatureClickedEventArgs>? OverlayFeatureClicked;

    public event EventHandler<MapOverlayFeaturesRectangleSelectedEventArgs>? OverlayFeaturesRectangleSelected;

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

    public async Task SetMarkerAsync(double latitude, double longitude, string? title = null)
    {
        await PostMessageAsync(new { type = "setMarker", latitude, longitude, title });
    }

    public async Task ClearMarkerAsync()
    {
        await PostMessageAsync(new { type = "clearMarker" });
    }

    public async Task ShowReferenceMarkersAsync(IEnumerable<MapReferenceMarker>? markers)
    {
        MapReferenceMarker[] normalizedMarkers = (markers ?? Array.Empty<MapReferenceMarker>())
            .Where(marker => marker is not null)
            .ToArray();

        await PostMessageAsync(new
        {
            type = "showReferenceMarkers",
            markers = normalizedMarkers.Select(marker => new
            {
                latitude = marker.Latitude,
                longitude = marker.Longitude,
                title = marker.Title,
                kind = marker.Kind
            }).ToArray()
        });
    }

    public async Task ClearReferenceMarkersAsync()
    {
        await PostMessageAsync(new { type = "clearReferenceMarkers" });
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

    public async Task ShowFeatureSelectionOverlayAsync(
        string geoJson,
        bool fitToBounds,
        double? focusLatitude = null,
        double? focusLongitude = null,
        string? statusText = null)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            return;
        }

        await PostMessageAsync(new
        {
            type = "showFeatureSelectionOverlay",
            geoJson,
            fitToBounds,
            focusLatitude,
            focusLongitude,
            statusText
        });
    }

    public async Task ClearFeatureSelectionOverlayAsync()
    {
        await PostMessageAsync(new { type = "clearFeatureSelectionOverlay" });
    }

    public async Task ShowModelFootprintOverlayAsync(
        string geoJson,
        bool fitToBounds,
        double? focusLatitude = null,
        double? focusLongitude = null)
    {
        if (string.IsNullOrWhiteSpace(geoJson))
        {
            return;
        }

        await PostMessageAsync(new
        {
            type = "showModelFootprintOverlay",
            geoJson,
            fitToBounds,
            focusLatitude,
            focusLongitude
        });
    }

    public async Task ClearModelFootprintOverlayAsync()
    {
        await PostMessageAsync(new { type = "clearModelFootprintOverlay" });
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
            await initializationGate.RunAsync(InitializeBrowserAsync);
        }
        catch
        {
            // InitializeBrowserAsync already reports the failure and keeps retries available.
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
            return;
        }

        if (string.Equals(message.Type, "overlayClick", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(message.FeatureId))
        {
            OverlayFeatureClicked?.Invoke(this, new MapOverlayFeatureClickedEventArgs(message.FeatureId));
            return;
        }

        if (string.Equals(message.Type, "overlayRectangleSelect", StringComparison.OrdinalIgnoreCase)
            && message.FeatureIds.Count > 0)
        {
            OverlayFeaturesRectangleSelected?.Invoke(this, new MapOverlayFeaturesRectangleSelectedEventArgs(message.FeatureIds));
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

    private async Task InitializeBrowserAsync()
    {
        string userDataFolder = MapHostEnvironment.GetUserDataFolder();
        string hostAssetFolder = MapHostEnvironment.GetHostAssetFolder();
        Trace.WriteLine($"[MapControl] Starting WebView2 initialization. controlId={GetHashCode()} userDataFolder='{userDataFolder}' hostAssetFolder='{hostAssetFolder}'");

        try
        {
            string hostHtml = ReadMapHostHtml();
            MapHostEnvironment.EnsureHostPage(hostHtml);

            CoreWebView2Environment environment = await MapHostEnvironment.CreateAsync();
            await MapBrowser.EnsureCoreWebView2Async(environment);
            ConfigureBrowserSettings(MapBrowser.CoreWebView2);
            MapBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                MapHostEnvironment.HostName,
                hostAssetFolder,
                CoreWebView2HostResourceAccessKind.Allow);
            MapBrowser.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
            MapBrowser.Source = MapHostEnvironment.GetHostPageUri();
            HideFallback();
            isInitialized = true;
            Trace.WriteLine($"[MapControl] WebView2 initialization succeeded. controlId={GetHashCode()} source='{MapBrowser.Source}'");
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"[MapControl] WebView2 initialization failed. controlId={GetHashCode()} error={ex}");
            ShowFallback($"Map host could not start. {ex.Message}");
            throw;
        }
    }

    private void HideFallback()
    {
        FallbackText.Text = string.Empty;
        FallbackOverlay.Visibility = Visibility.Collapsed;
    }
}
