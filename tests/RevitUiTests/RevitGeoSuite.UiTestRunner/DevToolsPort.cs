namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// Finds the DevTools port of RevitGeoSuite's WebView2. The test host starts it with
/// --remote-debugging-port=0, so Chromium picks a free port (bound to 127.0.0.1) and writes it as
/// the first line of DevToolsActivePort in the WebView2 user data folder.
/// </summary>
internal static class DevToolsPort
{
    public static string DefaultFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevitGeoSuite", "WebView2", "EBWebView", "DevToolsActivePort");

    /// <summary>Reads the port, ignoring a file older than this Revit's start (left over from an earlier session).</summary>
    public static int Read(string file, DateTime revitStartedUtc)
    {
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"{file} does not exist. Open a RevitGeoSuite page once in this Revit, and check that the test host is installed.");
        }

        if (File.GetLastWriteTimeUtc(file) < revitStartedUtc)
        {
            throw new InvalidOperationException($"{file} is from an earlier Revit session. Open a RevitGeoSuite page in this Revit first.");
        }

        return Parse(File.ReadAllText(file));
    }

    public static int Parse(string content)
    {
        string first = content.Split('\n', 2)[0].Trim();
        return int.TryParse(first, out int port) && port is > 0 and < 65536
            ? port
            : throw new FormatException($"DevToolsActivePort does not start with a port: '{first}'.");
    }
}
