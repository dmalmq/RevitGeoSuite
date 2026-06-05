using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using RevitGeoSuite.SharedUI.Shell.Handlers;

namespace RevitGeoSuite.SharedUI.Shell;

public partial class WebShellWindow : Window
{
    private const string WebView2DownloadUrl =
        "https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section";

    private readonly WebShellOptions options;
    private readonly WebRpcBridge rpcBridge;
    private readonly JobManager jobManager;
    private CoreWebView2? coreWebView;
    private bool isInitialized;
    private bool isClosed;

    public WebShellWindow(WebShellOptions options)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        InitializeComponent();

        var allHandlers = new List<IRpcHandler>(options.Handlers ?? Array.Empty<IRpcHandler>())
        {
            new WindowCloseHandler(() => this),
            new WindowNavigateHandler(() => this, route => NavigateTo(route)),
            new WindowMinimizeHandler(() => this),
            new WindowMaximizeHandler(() => this),
            new WindowResizeHandler(() => this),
            new LocalizationGetAllHandler(),
            new LocalizationSetLanguageHandler()
        };

        rpcBridge = new WebRpcBridge(allHandlers);
        jobManager = new JobManager(rpcBridge.SendEvent);
        rpcBridge.RegisterHandler(new JobCancelHandler(jobManager));
        
        options.RegisterHandlers?.Invoke(rpcBridge, jobManager);
        
        Loaded += OnLoaded;
    }

    public string? PendingModuleNavigationKey { get; private set; }

    public WebRpcBridge Bridge => rpcBridge;

    public JobManager Jobs => jobManager;

    public void NavigateTo(string route)
    {
        if (coreWebView != null && isInitialized)
        {
            var uri = WebShellEnvironment.GetIndexPageUri(route);
            coreWebView.Navigate(uri.ToString());
        }
    }

    public bool IsRouteActive => coreWebView != null && isInitialized;

    public bool IsClosed => isClosed;

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnLoaded;
        await InitializeWebViewAsync();
    }

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var environment = await WebShellEnvironment.CreateAsync();
            await ShellBrowser.EnsureCoreWebView2Async(environment);

            coreWebView = ShellBrowser.CoreWebView2;
            TryEnableNonClientRegionSupport(coreWebView);

            string assetFolder = WebShellEnvironment.EnsureWebAssets();
            coreWebView.SetVirtualHostNameToFolderMapping(
                WebShellEnvironment.HostName,
                assetFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            rpcBridge.Attach(coreWebView);

            var uri = WebShellEnvironment.GetIndexPageUri(options.InitialRoute);
            coreWebView.Navigate(uri.ToString());

            isInitialized = true;
        }
        catch (Exception ex)
        {
            // Check if this is a WebView2 runtime issue
            if (IsWebView2RuntimeError(ex))
            {
                ShowRuntimeMissingFallback(ex.Message);
            }
            else
            {
                ShowFallback(ex.Message);
            }
        }
    }

    // Lets the page mark its title bar with CSS `app-region: drag`, so the OS drags the window
    // natively (smooth). Without this, the header had to round-trip a mouse-move RPC per frame to
    // reposition the window, which lagged behind the cursor.
    private static void TryEnableNonClientRegionSupport(CoreWebView2 webView)
    {
        try
        {
            webView.Settings.IsNonClientRegionSupportEnabled = true;
        }
        catch (Exception)
        {
            // Older WebView2 runtimes don't support non-client regions; the header simply isn't
            // draggable there rather than crashing initialization.
        }
    }

    private static bool IsWebView2RuntimeError(Exception ex)
    {
        // Check for specific WebView2 runtime exceptions
        if (ex is WebView2RuntimeNotFoundException)
            return true;

        // Check exception type name
        if (ex.GetType().Name.Contains("WebView2"))
            return true;

        // Check exception message for runtime-related keywords
        string message = ex.Message.ToLowerInvariant();
        return message.Contains("webview2") && 
               (message.Contains("runtime") || message.Contains("not found") || message.Contains("not installed"));
    }

    private void ShowFallback(string message)
    {
        ShellBrowser.Visibility = Visibility.Collapsed;
        FallbackOverlay.Visibility = Visibility.Visible;
        FallbackTitle.Text = "WebView2 initialization failed";
        FallbackMessage.Text = message;
        DownloadRuntimeButton.Visibility = Visibility.Collapsed;
        DownloadHint.Visibility = Visibility.Collapsed;
    }

    private void ShowRuntimeMissingFallback(string detail)
    {
        ShellBrowser.Visibility = Visibility.Collapsed;
        FallbackOverlay.Visibility = Visibility.Visible;
        FallbackTitle.Text = "WebView2 runtime is not installed";
        FallbackMessage.Text =
            "RevitGeoSuite requires the Microsoft Edge WebView2 runtime to display its interface. " +
            "Please install the Evergreen Standalone Bootstrapper and restart Revit." +
            (string.IsNullOrWhiteSpace(detail) ? "" : "\n\n" + detail);
        DownloadRuntimeButton.Visibility = Visibility.Visible;
        DownloadHint.Visibility = Visibility.Visible;
    }

    private void DownloadRuntimeButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = WebView2DownloadUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            FallbackMessage.Text =
                "Could not open the download page. Please visit:\n" +
                WebView2DownloadUrl + "\n\n" + ex.Message;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        isClosed = true;
        rpcBridge.Detach();
        ShellBrowser.Dispose();
        base.OnClosed(e);
    }
}
