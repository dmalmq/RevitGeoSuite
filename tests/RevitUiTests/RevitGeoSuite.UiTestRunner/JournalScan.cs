namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// Reads the Revit journals written during a run, as the manual script asked: the transaction names
/// must be there, and no handler error, unhandled exception or failed commit.
/// </summary>
internal static class JournalScan
{
    public static readonly string[] BadMarkers = { "HANDLER_ERROR", "Unhandled exception", "unhandled exception", "Revit did not commit" };

    public static string DefaultFolder(string revitYear) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Autodesk", "Revit", $"Autodesk Revit {revitYear}", "Journals");

    public static IReadOnlyList<string> JournalsSince(string folder, DateTime sinceUtc) =>
        Directory.Exists(folder)
            ? Directory.GetFiles(folder, "journal.*.txt").Where(file => File.GetLastWriteTimeUtc(file) >= sinceUtc).OrderBy(file => file).ToList()
            : Array.Empty<string>();

    public static JournalResult Scan(IEnumerable<string> lines, IEnumerable<string> requiredNames)
    {
        List<string> all = lines.ToList();
        List<string> missing = requiredNames.Where(name => !all.Any(line => line.Contains(name, StringComparison.Ordinal))).ToList();
        List<string> bad = all.Where(line => BadMarkers.Any(marker => line.Contains(marker, StringComparison.Ordinal))).Select(line => line.Trim()).ToList();
        List<string> named = all.Where(line => line.Contains("Kanizu GIS:", StringComparison.Ordinal)).Select(line => line.Trim()).ToList();
        return new JournalResult(missing, bad, named);
    }
}

internal sealed record JournalResult(IReadOnlyList<string> MissingNames, IReadOnlyList<string> BadLines, IReadOnlyList<string> KanizuLines)
{
    public bool Passed => MissingNames.Count == 0 && BadLines.Count == 0;
}
