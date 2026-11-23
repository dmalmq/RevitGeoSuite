using System;
using Autodesk.Revit.DB;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell;

public sealed class WebShellWindowManager
{
    private static readonly Lazy<WebShellWindowManager> lazy = new(() => new WebShellWindowManager());

    public static WebShellWindowManager Instance => lazy.Value;

    private WebShellWindow? currentWindow;
    private WebShellOptions? currentOptions;
    private string? lastNotifiedDocumentId;

    private WebShellWindowManager() { }

    public WebShellWindow? CurrentWindow => currentWindow;

    public WebShellWindow GetOrCreateWindow(WebShellOptions options)
    {
        if (currentWindow != null && !currentWindow.IsClosed)
        {
            return currentWindow;
        }

        currentOptions = options;
        lastNotifiedDocumentId = null;
        currentWindow = new WebShellWindow(options);
        currentWindow.Closed += OnWindowClosed;
        return currentWindow;
    }

    /// <summary>
    /// Notifies the open modeless web shell when the active Revit document changes so the UI
    /// refreshes its title bar, status bar, readiness, and document binding.
    /// </summary>
    public void NotifyActiveDocument(Document? document)
    {
        WebShellWindow? window = currentWindow;
        if (window == null || window.IsClosed)
        {
            return;
        }

        string documentId = document != null && document.IsValidObject
            ? DocumentIdentity.Create(document)
            : string.Empty;
        if (string.Equals(lastNotifiedDocumentId, documentId, StringComparison.Ordinal))
        {
            return;
        }

        lastNotifiedDocumentId = documentId;
        string documentTitle = document != null && document.IsValidObject
            ? document.Title ?? string.Empty
            : string.Empty;

        window.Bridge.SendEvent("document.changed", new DocumentChangedEvent
        {
            DocumentId = documentId,
            DocumentTitle = documentTitle
        });
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        if (currentWindow != null)
        {
            currentWindow.Closed -= OnWindowClosed;
        }
        currentWindow = null;
        lastNotifiedDocumentId = null;
    }
}
