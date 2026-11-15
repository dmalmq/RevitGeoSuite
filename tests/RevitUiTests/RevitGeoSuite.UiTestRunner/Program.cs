using System.Globalization;
using System.Text.Json.Nodes;
using RevitGeoSuite.UiTestRunner;

RunOptions options;
ExportStore? exportStore;
try
{
    options = RunOptions.Parse(args);
    // Taken before any scenario runs: the files as Run-UiTests.ps1 left them for the run.
    exportStore = options.ExportSettings.Length > 0 ? new ExportStore(options.ExportSettings) : null;
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    Console.Error.WriteLine(RunOptions.Usage);
    return 2;
}

Directory.CreateDirectory(options.OutputFolder);
var reports = new List<(string Name, Report Report, TimeSpan Took)>();
TestHostClient? host = null;
string? stopped = null;
DateTime revitStartedUtc = DateTime.MinValue;
var connect = new StepResult("START", "The test host answers");
var ping = new List<string>();
try
{
    host = TestHostClient.Connect(TimeSpan.FromMinutes(5));
    JsonObject reply = host.Call("ping");
    options.RequireInside(reply["root"]!.GetValue<string>());
    revitStartedUtc = DateTime.Parse(reply["started"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal);
    ping.Add($"Revit {reply["revit"]}, process {reply["pid"]}, started {revitStartedUtc:yyyy-MM-dd HH:mm:ss} UTC, WebView2 arguments '{reply["webViewArguments"]}'.");
    ping.Add($"Test host: {reply["testHost"]}.");
    ping.Add($"RevitGeoSuite: {reply["rgs"] ?? "(test host without RevitGeoSuite start)"}; loaded from {reply["rgsLoaded"]?.ToJsonString() ?? "?"}.");
}
catch (Exception ex)
{
    stopped = $"{ex.GetType().Name}: {ex.Message}";
    connect.Check(false, $"The run stopped with {stopped}");
}

foreach (string name in options.ScenarioNames)
{
    RunOptions scenario = options.ForScenario(name);
    Directory.CreateDirectory(scenario.OutputFolder);
    var report = new Report(scenario.OutputFolder);
    DateTime startedUtc = DateTime.UtcNow;
    // The first scenario also answers for Revit's start-up, so it reads the whole journal; each later one only its own lines.
    string journals = JournalScan.DefaultFolder(options.RevitYear);
    JournalMark journal = reports.Count == 0 ? new JournalMark(journals, startedUtc, new Dictionary<string, int>()) : JournalScan.Mark(journals);
    report.Header.Add($"Scenario {name}. Started {startedUtc:yyyy-MM-dd HH:mm:ss} UTC on {Environment.MachineName}.");
    report.Header.AddRange(ping);
    GisStandardRun? run = null;
    if (stopped != null)
    {
        // Revit gone, the pipe never up, or a call that timed out: every scenario still ends in a FAIL report.
        report.Header.Add($"RUN STOPPED: {stopped}");
        var notRun = new StepResult("STOP", "The run finished every step it was asked to run");
        notRun.Check(false, $"The run stopped before this scenario with {stopped}");
        report.Steps.Add(notRun);
    }
    else
    {
        try
        {
            if (reports.Count > 0 && exportStore != null)
            {
                report.Header.Add(exportStore.Reset());
            }

            host!.Call("arm", new { on = true });
            run = new GisStandardRun(scenario, host, report);
            try
            {
                await run.RunAsync(revitStartedUtc);
            }
            finally
            {
                host.Call("arm", new { on = false });
            }
        }
        catch (Exception ex)
        {
            stopped = $"{ex.GetType().Name}: {ex.Message}";
            report.Header.Add($"RUN STOPPED: {stopped}");
            var failed = new StepResult("STOP", "The run finished every step it was asked to run");
            failed.Check(false, $"The run stopped with {stopped}");
            report.Steps.Add(failed);
            Console.Error.WriteLine($"Run stopped: {ex.Message}");
        }

        CheckJournals(report, journal, run?.TransactionsSeen ?? Array.Empty<string>());
    }

    TimeSpan took = DateTime.UtcNow - startedUtc;
    report.Header.Add($"Took {took.TotalMinutes:0.0} min.");
    Console.WriteLine($"Report: {report.Write()}");
    reports.Add((name, report, took));
}

host?.Dispose();
if (options.ScenarioNames.Count > 1)
{
    Console.WriteLine($"Summary: {Suite.Write(options.OutputFolder, ping, connect, reports)}");
}

return reports.Any(item => item.Report.Steps.Any(step => step.Outcome == Outcome.Fail)) || connect.Outcome == Outcome.Fail ? 1 : 0;

static void CheckJournals(Report report, JournalMark mark, IReadOnlyList<string> transactions)
{
    var step = new StepResult("J", "Revit journal: the transactions are there, with no handler errors");
    report.Steps.Add(step);
    (IReadOnlyList<string> journals, List<string> lines) = mark.LinesAfter();
    if (journals.Count == 0)
    {
        step.Check(false, $"No journal written since the scenario started in {mark.Folder}.");
        return;
    }

    JournalResult result = JournalScan.Scan(lines, transactions.Distinct());
    step.Note($"Journals: {string.Join(", ", journals.Select(Path.GetFileName))}; {lines.Count} line(s) written during this scenario.");
    step.Check(result.MissingNames.Count == 0, $"Every transaction name is in the journal (missing: {string.Join(", ", result.MissingNames)}).");
    step.Check(result.BadLines.Count == 0, $"No handler error, unhandled exception or failed commit (found: {string.Join(" / ", result.BadLines.Take(5))}).");
    foreach (string line in result.KanizuLines.Take(20))
    {
        step.Note($"`{line}`");
    }
}
