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

    /// <summary>
    /// Where each journal ends now. Several scenarios share one Revit and so one journal: a scenario reads only
    /// the lines written after its mark (<see cref="JournalMark.LinesAfter"/>), never an earlier scenario's.
    /// </summary>
    public static JournalMark Mark(string folder)
    {
        DateTime nowUtc = DateTime.UtcNow;
        var ends = JournalsSince(folder, nowUtc.AddDays(-1)).ToDictionary(file => file, file => Read(file).Count(c => c == '\n'), StringComparer.OrdinalIgnoreCase);
        return new JournalMark(folder, nowUtc, ends);
    }

    /// <summary>A journal's text, read shared: Revit keeps the journal open.</summary>
    internal static string Read(string file)
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

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

/// <summary>The end of each journal at one moment (<see cref="JournalScan.Mark"/>).</summary>
internal sealed record JournalMark(string Folder, DateTime TakenUtc, IReadOnlyDictionary<string, int> Ends)
{
    /// <summary>The journals written since the mark, and their lines after it: complete lines only were counted, so none is lost.</summary>
    public (IReadOnlyList<string> Journals, List<string> Lines) LinesAfter()
    {
        IReadOnlyList<string> journals = JournalScan.JournalsSince(Folder, TakenUtc.AddMinutes(-1));
        List<string> lines = journals.SelectMany(file =>
            JournalScan.Read(file).Split('\n').Skip(Ends.TryGetValue(file, out int end) ? end : 0).Select(line => line.TrimEnd('\r'))).ToList();
        return (journals, lines);
    }
}

