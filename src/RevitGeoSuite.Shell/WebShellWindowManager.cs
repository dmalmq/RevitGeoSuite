using System;
using RevitGeoSuite.SharedUI.Shell;

namespace RevitGeoSuite.Shell;

public sealed class WebShellWindowManager
{
    private static readonly Lazy<WebShellWindowManager> lazy = new(() => new WebShellWindowManager());

    public static WebShellWindowManager Instance => lazy.Value;

    private WebShellWindow? currentWindow;
    private WebShellOptions? currentOptions;

    private WebShellWindowManager() { }

    public WebShellWindow? CurrentWindow => currentWindow;

    public WebShellWindow GetOrCreateWindow(WebShellOptions options)
    {
        if (currentWindow != null && !currentWindow.IsClosed)
        {
            return currentWindow;
        }

        currentOptions = options;
        currentWindow = new WebShellWindow(options);
        currentWindow.Closed += OnWindowClosed;
        return currentWindow;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (currentWindow != null)
        {
            currentWindow.Closed -= OnWindowClosed;
        }
        currentWindow = null;
    }
}
