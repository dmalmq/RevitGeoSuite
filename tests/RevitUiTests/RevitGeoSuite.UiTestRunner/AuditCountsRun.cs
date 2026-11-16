using System.Globalization;
using System.Text;
using System.Text.Json;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The audit's real numbers on a copy of a model: the GIS standard page's findings before and after
/// Set up model, counted by severity and topic, with the family and floor type findings listed in
/// full. Writes audit-counts.md and audit-findings.json into the run folder. Step ids AC.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private static readonly string[] ListedTopics = { "families", "floor types", "doors", "stairs" };

    private async Task AuditCountsSteps()
    {
        InspectReply? before = null, after = null;
        await Step("AC.1", "Audit before Set up model: counts by severity and topic", async step =>
        {
            before = await Inspect();
            NoteCounts(step, before);
            await Shot(step, "AC1-audit-before");
        });
        await Step("AC.2", "Apply Set up model", ApplySetup);
        await Step("AC.3", "Audit after Set up model: counts by severity and topic", async step =>
        {
            await CheckAgain();
            after = await Inspect();
            step.Check(after.SetupLines.Length == 0, $"Set up model has nothing left to do ({after.SetupLines.Length} line(s)).");
            NoteCounts(step, after);
            await Shot(step, "AC3-audit-after");
        });

        WriteAuditCounts(before, after);
    }

    private static void NoteCounts(StepResult step, InspectReply model)
    {
        step.Note($"{model.Count("error")} errors, {model.Count("warning")} warnings, {model.Count("info")} notes; setup lines {model.SetupLines.Length}.");
        foreach (var group in model.Findings.GroupBy(finding => (finding.Topic, finding.Severity)).OrderBy(group => group.Key.Topic, StringComparer.Ordinal))
        {
            step.Note($"{group.Key.Topic} / {group.Key.Severity}: {group.Count()} finding(s), {group.Sum(finding => finding.ElementIds.Length)} element(s).");
        }

        foreach (FindingItem finding in model.Findings.Where(finding => ListedTopics.Contains(finding.Topic)))
        {
            step.Note($"{finding.Severity} {finding.Topic} ({finding.ElementIds.Length} element(s)): {finding.Message}");
        }

        foreach (string line in model.SetupLines.Where(line => line.Contains("gis_kind", StringComparison.Ordinal) || line.StartsWith("Update floor type", StringComparison.Ordinal)))
        {
            step.Note($"Setup line: {line}");
        }
    }

    private void WriteAuditCounts(InspectReply? before, InspectReply? after)
    {
        File.WriteAllText(
            Path.Combine(options.OutputFolder, "audit-findings.json"),
            JsonSerializer.Serialize(new { model = options.Model, before, after }, new JsonSerializerOptions(Web) { WriteIndented = true }));

        var text = new StringBuilder($"# Audit counts: {Path.GetFileName(options.Model)}\n\nElement ids are in audit-findings.json.\n");
        foreach ((string title, InspectReply? model) in new[] { ("Before Set up model", before), ("After Set up model", after) })
        {
            text.Append(CultureInfo.InvariantCulture, $"\n## {title}\n\n");
            if (model == null)
            {
                text.Append("Not read.\n");
                continue;
            }

            text.Append("| Topic | Severity | Findings | Elements |\n| --- | --- | --- | --- |\n");
            foreach (var group in model.Findings.GroupBy(finding => (finding.Topic, finding.Severity)).OrderBy(group => group.Key.Topic, StringComparer.Ordinal))
            {
                text.Append(CultureInfo.InvariantCulture, $"| {group.Key.Topic} | {group.Key.Severity} | {group.Count()} | {group.Sum(finding => finding.ElementIds.Length)} |\n");
            }

            text.Append("\nFamily, floor type, door and stair findings:\n\n");
            foreach (FindingItem finding in model.Findings.Where(finding => ListedTopics.Contains(finding.Topic)))
            {
                text.Append(CultureInfo.InvariantCulture, $"- {finding.Severity}, {finding.ElementIds.Length} element(s): {finding.Message}\n");
            }
        }

        File.WriteAllText(Path.Combine(options.OutputFolder, "audit-counts.md"), text.ToString());
    }
}
