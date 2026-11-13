using System;
using System.Diagnostics;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// Test-only add-in for the build PC. It lets the UI test runner drive Revit the way a person does:
/// open a per-run copy of a model, click ribbon buttons, press Undo, read the selection and parameters,
/// answer Windows file dialogs, and record every transaction and dialog Revit shows.
/// Commands arrive on a named pipe, so nothing listens on a network port.
/// </summary>
public sealed class TestHostApp : IExternalApplication
{
    /// <summary>Process-scoped WebView2 switch: remote debugging on a free port, bound to 127.0.0.1 by Chromium.</summary>
    internal const string WebViewArgumentsVariable = "WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS";
    internal const string RemoteDebuggingArgument = "--remote-debugging-port=0";

    private EventRecorder? recorder;
    private PipeServer? server;

    public Result OnStartup(UIControlledApplication application)
    {
        EnableWebViewDebugging();

        recorder = new EventRecorder();
        application.ControlledApplication.DocumentChanged += recorder.OnDocumentChanged;
        application.DialogBoxShowing += recorder.OnDialogBoxShowing;

        var api = new ApiQueue();
        var commands = new Commands(api, recorder, TestRoot.Resolve());
        server = new PipeServer(commands.Handle, () => recorder.Armed = false);
        server.Start();
        return Result.Succeeded;
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        server?.Stop();
        if (recorder != null)
        {
            application.ControlledApplication.DocumentChanged -= recorder.OnDocumentChanged;
            application.DialogBoxShowing -= recorder.OnDialogBoxShowing;
        }
        return Result.Succeeded;
    }

    // Set before any WebView2 environment exists, and only inside this Revit process, so other
    // WebView2 apps on the PC (Outlook, Teams) are untouched. Port 0 lets Chromium pick a free
    // port and write it to <user data folder>\EBWebView\DevToolsActivePort for the runner.
    private static void EnableWebViewDebugging()
    {
        string? existing = Environment.GetEnvironmentVariable(WebViewArgumentsVariable);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            Trace.WriteLine($"RevitGeoSuite.TestHost: {WebViewArgumentsVariable} is already '{existing}', left as is.");
            return;
        }

        Environment.SetEnvironmentVariable(WebViewArgumentsVariable, RemoteDebuggingArgument);
    }
}
