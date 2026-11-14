using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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

    /// <summary>
    /// The test host does nothing unless Revit was started with this variable set to 1, which
    /// Run-UiTests.ps1 -StartRevit does for that Revit process only. A Revit started any other way
    /// gets no pipe, no event hooks and no DevTools port.
    /// </summary>
    internal const string EnableVariable = "REVITGEOSUITE_UITEST";

    /// <summary>
    /// A RevitGeoSuite build folder (with RevitGeoSuite.Shell.dll) that the test host starts inside
    /// this Revit, the way Revit starts an add-in from its manifest. Revit only asks "Always Load" for
    /// the DLL a manifest names, so an unsigned PR build started this way never prompts: the one
    /// trusted DLL stays the loader. Run-UiTests.ps1 -RgsBuild sets it for that Revit process only.
    /// </summary>
    internal const string RgsVariable = "REVITGEOSUITE_UITEST_RGS";

    /// <summary>What happened to the RevitGeoSuite build named by <see cref="RgsVariable"/>, for the report.</summary>
    internal static string RgsStatus { get; private set; } = "none requested";

    private EventRecorder? recorder;
    private PipeServer? server;
    private IExternalApplication? rgs;

    public Result OnStartup(UIControlledApplication application)
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
        {
            Trace.WriteLine($"RevitGeoSuite.TestHost: inert, because {EnableVariable} is not 1 for this Revit.");
            return Result.Succeeded;
        }

        EnableWebViewDebugging();

        recorder = new EventRecorder();
        application.ControlledApplication.DocumentChanged += recorder.OnDocumentChanged;
        application.DialogBoxShowing += recorder.OnDialogBoxShowing;

        var api = new ApiQueue();
        var commands = new Commands(api, recorder, TestRoot.Resolve());
        server = new PipeServer(commands.Handle, () => recorder.Armed = false);
        server.Start();
        StartRgs(application);
        return Result.Succeeded;
    }

    private void StartRgs(UIControlledApplication application)
    {
        string folder = Environment.GetEnvironmentVariable(RgsVariable) ?? string.Empty;
        if (folder.Length == 0)
        {
            return;
        }

        string path = Path.Combine(folder, "RevitGeoSuite.Shell.dll");
        try
        {
            // LoadFrom: its dependencies resolve from the same folder, and the ribbon commands Revit
            // later loads from this path get this same assembly, so they share its state.
            Assembly shell = Assembly.LoadFrom(path);
            rgs = (IExternalApplication)Activator.CreateInstance(shell.GetType("RevitGeoSuite.Shell.App", throwOnError: true));
            Result result = rgs.OnStartup(application);
            RgsStatus = $"{path} ({shell.GetName().Version}, built {File.GetLastWriteTimeUtc(path):yyyy-MM-dd HH:mm} UTC): OnStartup {result}";
        }
        catch (Exception ex)
        {
            RgsStatus = $"{path} failed to start: {ex.GetType().Name}: {ex.Message}";
            Trace.WriteLine($"RevitGeoSuite.TestHost: {RgsStatus}\n{ex}");
        }
    }

    public Result OnShutdown(UIControlledApplication application)
    {
        try
        {
            rgs?.OnShutdown(application);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($"RevitGeoSuite.TestHost: RevitGeoSuite shutdown failed: {ex}");
        }

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
