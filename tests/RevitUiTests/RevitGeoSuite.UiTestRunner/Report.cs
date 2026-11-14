using System.Text;

namespace RevitGeoSuite.UiTestRunner;

internal enum Outcome { Pass, Fail, NotRun, KnownFail }

/// <summary>One step of the script: what was done, what was checked, and the pictures to look at.</summary>
internal sealed class StepResult(string id, string title)
{
    public string Id { get; } = id;
    public string Title { get; } = title;
    public Outcome Outcome { get; set; } = Outcome.Pass;
    public List<string> Notes { get; } = new();
    public List<string> Failures { get; } = new();
    public List<string> Screenshots { get; } = new();

    public void Note(string text) => Notes.Add(text);

    /// <summary>Records a failed expectation and carries on, so one run reports every problem it can see.</summary>
    public bool Check(bool condition, string expectation)
    {
        if (!condition)
        {
            Failures.Add(expectation);
            Outcome = Outcome.Fail;
        }

        return condition;
    }

    /// <summary>
    /// A check that RevitGeoSuite is known to fail until <paramref name="fixedBy"/> lands. Its failure is
    /// reported as known and does not fail the run; any other failed check in the step still does.
    /// </summary>
    public bool CheckKnown(bool condition, string expectation, string fixedBy)
    {
        if (!condition)
        {
            Failures.Add($"KNOWN PRODUCT FAILURE until {fixedBy}: {expectation}");
            if (Outcome == Outcome.Pass)
            {
                Outcome = Outcome.KnownFail;
            }
        }

        return condition;
    }

    public void NotRun(string reason)
    {
        Outcome = Outcome.NotRun;
        Notes.Add(reason);
    }
}

/// <summary>The run's report.md: pass, fail or not run for each step, with notes and screenshots.</summary>
internal sealed class Report(string folder)
{
    public string Folder { get; } = folder;
    public List<StepResult> Steps { get; } = new();
    public List<string> Header { get; } = new();
    public List<string> Footer { get; } = new();

    public string Write()
    {
        var text = new StringBuilder();
        text.AppendLine("# RevitGeoSuite UI test run").AppendLine();
        foreach (string line in Header)
        {
            text.AppendLine($"- {line}");
        }

        int failed = Steps.Count(step => step.Outcome == Outcome.Fail);
        int passed = Steps.Count(step => step.Outcome == Outcome.Pass);
        int notRun = Steps.Count(step => step.Outcome == Outcome.NotRun);
        int known = Steps.Count(step => step.Outcome == Outcome.KnownFail);
        string knownText = known == 0 ? string.Empty : $", {known} known product failure(s)";
        text.AppendLine().AppendLine($"**{(failed == 0 ? "PASS" : "FAIL")}**: {passed} passed, {failed} failed{knownText}, {notRun} not run.").AppendLine();
        text.AppendLine("| Step | What | Result |").AppendLine("|---|---|---|");
        foreach (StepResult step in Steps)
        {
            text.AppendLine($"| {step.Id} | {Cell(step.Title)} | {Label(step.Outcome)} |");
        }

        foreach (StepResult step in Steps)
        {
            text.AppendLine().AppendLine($"## {step.Id}. {step.Title}: {Label(step.Outcome)}").AppendLine();
            foreach (string failure in step.Failures)
            {
                text.AppendLine($"- FAILED: {failure}");
            }

            foreach (string note in step.Notes)
            {
                text.AppendLine($"- {note}");
            }

            foreach (string shot in step.Screenshots)
            {
                text.AppendLine($"- Screenshot: [{shot}]({shot})");
            }
        }

        if (Footer.Count > 0)
        {
            text.AppendLine();
            foreach (string line in Footer)
            {
                text.AppendLine(line);
            }
        }

        string path = Path.Combine(Folder, "report.md");
        File.WriteAllText(path, text.ToString(), new UTF8Encoding(false));
        return path;
    }

    private static string Label(Outcome outcome) => outcome switch
    {
        Outcome.Pass => "pass",
        Outcome.Fail => "**FAIL**",
        Outcome.KnownFail => "known product failure",
        _ => "not run",
    };

    private static string Cell(string text) => text.Replace("|", "\\|");
}
