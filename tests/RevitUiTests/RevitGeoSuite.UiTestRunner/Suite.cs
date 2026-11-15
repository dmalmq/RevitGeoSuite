using System.Globalization;
using System.Text;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>summary.md of a run of several scenarios in one Revit: one line per scenario, with its minutes.</summary>
internal static class Suite
{
    public static string Write(string folder, IReadOnlyList<string> header, StepResult connect, IReadOnlyList<(string Name, Report Report, TimeSpan Took)> reports)
    {
        int failed = reports.Count(item => Failed(item.Report));
        bool runFailed = failed > 0 || connect.Outcome == Outcome.Fail;
        var text = new StringBuilder();
        text.AppendLine("# RevitGeoSuite UI test suite").AppendLine();
        foreach (string line in header)
        {
            text.AppendLine($"- {line}");
        }

        foreach (string failure in connect.Failures)
        {
            text.AppendLine($"- FAILED: {failure}");
        }

        double minutes = reports.Sum(item => item.Took.TotalMinutes);
        text.AppendLine($"- {reports.Count} scenario(s) in one Revit, {Minutes(minutes)} min in all.");
        text.AppendLine().AppendLine($"**{(runFailed ? "FAIL" : "PASS")}**: {reports.Count - failed} scenario(s) passed, {failed} failed.").AppendLine();
        text.AppendLine("| Scenario | Result | Steps passed | Steps failed | Not run | Minutes | Report |").AppendLine("|---|---|---|---|---|---|---|");
        foreach ((string name, Report report, TimeSpan took) in reports)
        {
            string relative = Path.GetRelativePath(folder, Path.Combine(report.Folder, "report.md")).Replace('\\', '/');
            text.AppendLine($"| {name} | {(Failed(report) ? "**FAIL**" : "pass")} | {Count(report, Outcome.Pass)} | {Count(report, Outcome.Fail)} | {Count(report, Outcome.NotRun)} | {Minutes(took.TotalMinutes)} | [{relative}]({relative}) |");
        }

        string path = Path.Combine(folder, "summary.md");
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static bool Failed(Report report) => report.Steps.Any(step => step.Outcome == Outcome.Fail);

    private static int Count(Report report, Outcome outcome) => report.Steps.Count(step => step.Outcome == outcome);

    private static string Minutes(double minutes) => minutes.ToString("0.0", CultureInfo.InvariantCulture);
}
