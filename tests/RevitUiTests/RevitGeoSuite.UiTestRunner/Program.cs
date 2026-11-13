using System.Globalization;
using System.Text.Json.Nodes;
using RevitGeoSuite.UiTestRunner;

RunOptions options;
try
{
    options = RunOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(RunOptions.Usage);
    return 2;
}

DateTime runStartedUtc = DateTime.UtcNow;
using TestHostClient host = TestHostClient.Connect(TimeSpan.FromMinutes(5));
JsonObject ping = host.Call("ping");
options.RequireInside(ping["root"]!.GetValue<string>());
DateTime revitStartedUtc = DateTime.Parse(ping["started"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
Directory.CreateDirectory(options.OutputFolder);

var report = new Report(options.OutputFolder);
report.Header.Add($"Started {runStartedUtc:yyyy-MM-dd HH:mm:ss} UTC on {Environment.MachineName}.");
report.Header.Add($"Revit {ping["revit"]}, process {ping["pid"]}, WebView2 arguments '{ping["webViewArguments"]}'.");
report.Header.Add($"Test host: {ping["testHost"]}.");

host.Call("arm", new { on = true });
var run = new GisStandardRun(options, host, report);
try
{
    await run.RunAsync(revitStartedUtc);
}
finally
{
    host.Call("arm", new { on = false });
}

CheckJournals(options, report, runStartedUtc, run.TransactionsSeen);
string path = report.Write();
Console.WriteLine($"Report: {path}");
return report.Steps.Any(step => step.Outcome == Outcome.Fail) ? 1 : 0;

static void CheckJournals(RunOptions options, Report report, DateTime sinceUtc, IReadOnlyList<string> transactions)
{
    var step = new StepResult("J", "Revit journal: the transactions are there, with no handler errors");
    report.Steps.Add(step);
    string folder = JournalScan.DefaultFolder(options.RevitYear);
    IReadOnlyList<string> journals = JournalScan.JournalsSince(folder, sinceUtc.AddMinutes(-1));
    if (journals.Count == 0)
    {
        step.Check(false, $"No journal written since the run started in {folder}.");
        return;
    }

    // Revit keeps the journal open; read it shared.
    IEnumerable<string> lines = journals.SelectMany(file =>
    {
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n');
    }).ToList();
    JournalResult result = JournalScan.Scan(lines, transactions.Distinct());
    step.Note($"Journals: {string.Join(", ", journals.Select(Path.GetFileName))}.");
    step.Check(result.MissingNames.Count == 0, $"Every transaction name is in the journal (missing: {string.Join(", ", result.MissingNames)}).");
    step.Check(result.BadLines.Count == 0, $"No handler error, unhandled exception or failed commit (found: {string.Join(" / ", result.BadLines.Take(5))}).");
    foreach (string line in result.KanizuLines.Take(20))
    {
        step.Note($"`{line}`");
    }
}
