using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The B2a check (/mnt/project-files/revit-template-review/b2/revit-check-b2a.md): the audit's new
/// topics, Set up model repairing the GIS Export template, and the duplicate id error. The model
/// must have had Set up model run once; B2.0 runs it when it has not. Step ids are B2.0 to B2.9.
/// </summary>
internal sealed partial class GisStandardRun
{
    private static readonly string[] AuditTopics = { "ids", "stairs", "escalators", "levels", "views" };
    private const string UpdateTemplate = "Update view template " + TemplateName + ": ";

    private string templateChanges = string.Empty;
    private bool templateRepaired;

    private async Task ExportAuditSteps()
    {
        await Step("B2.0", "The model has had Set up model once", EnsureSetUp);
        await Step("B2.1", "Audit findings per new topic", AuditTopicCounts);
        await Step("B2.2", "Select buttons of the new topics", AuditSelect);
        await Step("B2.3", "A changed GIS Export template shows in Set up and the audit", BreakTemplate);
        await Step("B2.4", "Set up model repairs the template", RepairTemplate);
        await Step("B2.5", "Nothing left to repair", TemplateRepaired);
        await Step("B2.6", "One Undo puts the change back; Redo", UndoRepair);
        await Step("B2.7", "A GIS_ plan without the template is a views finding only", PlanWithoutTemplate);
        await Step("B2.8", "A floor pasted in place gives a duplicate id error", DuplicateId);
        await Step("B2.9", "Stair findings (note the messages)", StairFindings);
    }

    private async Task EnsureSetUp(StepResult step)
    {
        if (!Views().Any(IsExportTemplate))
        {
            step.Note($"The model has no {TemplateName} template, so Set up model runs first (the check list expects B1's Set up to have run once).");
        }

        InspectReply model = await Inspect();
        if (model.SetupLines.Length == 0)
        {
            step.Note("Nothing to set up: the model was set up before.");
            return;
        }

        string[] log = await ApplySetupAndReadLog(step);
        step.Note($"Set up model: {string.Join(" / ", log)}");
        await CheckAgain();
        step.Check(Views().Any(IsExportTemplate), $"The {TemplateName} template exists.");
    }

    private async Task AuditTopicCounts(StepResult step)
    {
        InspectReply model = await Inspect();
        foreach (string topic in AuditTopics)
        {
            FindingItem[] found = model.Findings.Where(finding => finding.Topic == topic).ToArray();
            step.Note($"{topic}: {found.Length} finding(s){(found.Length > 0 ? ": " : string.Empty)}{string.Join(" | ", found.Select(finding => $"{finding.Severity} '{finding.Message}' ({finding.ElementIds.Length} element(s))"))}");
        }

        FindingItem[] views = model.Findings.Where(finding => finding.Topic == "views").ToArray();
        step.Check(views.Length == 0, $"No views finding on a model Set up just made (has {views.Length}: {string.Join(" | ", views.Select(finding => finding.Message))}).");
        await Shot(step, "B2-1-audit");
    }

    private async Task AuditSelect(StepResult step)
    {
        InspectReply model = await Inspect();
        FindingItem[] selectable = model.Findings.Where(finding => finding.ElementIds.Length > 0).ToArray();
        bool shot = false;
        foreach (string topic in AuditTopics)
        {
            int index = Array.FindIndex(selectable, finding => finding.Topic == topic);
            if (index < 0)
            {
                step.Note($"{topic}: no finding with elements to select.");
                continue;
            }

            FindingItem finding = selectable[index];
            await Shell.Button(new Regex(@"^Select \d+$")).Nth(index).ClickAsync();
            await ExpectSelection(step, finding.ElementIds, $"the {topic} finding '{finding.Message}'");
            if (!shot)
            {
                RevitShot(step, $"B2-2-{topic}-selection");
                shot = true;
            }
        }
    }

    private async Task BreakTemplate(StepResult step)
    {
        JsonObject fixture = host.Call("fixture", new
        {
            views = new[] { new { view = TemplateName, hide = new[] { "OST_Floors" }, show = new[] { "OST_Walls" }, showLineStyles = ThinLines } },
        });
        step.Note($"Fixture: {fixture["log"]?.ToJsonString()}");
        await CheckAgain();
        InspectReply model = await Inspect();
        string? line = model.SetupLines.FirstOrDefault(item => item.StartsWith(UpdateTemplate, StringComparison.Ordinal));
        if (!step.Check(line != null, $"Set up lists '{UpdateTemplate}…' (lines: {string.Join(" / ", model.SetupLines)})."))
        {
            return;
        }

        templateChanges = line![UpdateTemplate.Length..];
        step.Note($"Changes: {templateChanges}");
        step.Check(Regex.IsMatch(templateChanges, @"(^|; )show [^;]*(Floors|床)"), "It shows Floors again.");
        step.Check(Regex.IsMatch(templateChanges, @"(^|; )hide [^;]*(Thin Lines|細線)"), "It hides the Thin Lines line style again.");
        step.Check(!Regex.IsMatch(templateChanges, "Walls|壁"), "It says nothing about Walls.");
        FindingItem? finding = model.Findings.FirstOrDefault(item => item.Topic == "views" && item.Message.Contains(TemplateName, StringComparison.Ordinal));
        step.Check(finding != null && finding.Severity == "warning", $"A views warning about {TemplateName} ({finding?.Severity ?? "none"}: {finding?.Message}).");
        step.Check(finding != null && finding.Message.Contains(templateChanges, StringComparison.Ordinal), "The warning names the same changes.");
        await Shot(step, "B2-3-template-changed");
    }

    private async Task RepairTemplate(StepResult step)
    {
        if (templateChanges.Length == 0)
        {
            step.NotRun("B2.3 found no template line to apply.");
            return;
        }

        string[] log = await ApplySetupAndReadLog(step);
        step.Note($"Log: {string.Join(" / ", log)}");
        step.Check(log.Contains($"Updated view template {TemplateName}: {templateChanges}"), $"The log says 'Updated view template {TemplateName}: {templateChanges}'.");
        templateRepaired = true;
        PlanViewRow? template = Views().FirstOrDefault(IsExportTemplate);
        step.Check(template?.CategoryHidden("OST_Floors") == false, $"Floors are shown in the template (hidden: {template?.CategoryHidden("OST_Floors")}).");
        step.Check(template?.LineStylesHidden == true, $"Thin Lines are hidden in the template (hidden: {template?.LineStylesHidden}).");
        step.Check(template?.CategoryHidden("OST_Walls") == false, $"Walls stay shown (hidden: {template?.CategoryHidden("OST_Walls")}).");
    }

    private async Task TemplateRepaired(StepResult step)
    {
        await CheckAgain();
        InspectReply model = await Inspect();
        step.Check(!model.SetupLines.Any(line => line.StartsWith("Update view template", StringComparison.Ordinal)), "No 'Update view template' line.");
        step.Check(!model.Findings.Any(finding => finding.Topic == "views"), $"No views finding ({string.Join(" | ", model.Findings.Where(finding => finding.Topic == "views").Select(finding => finding.Message))}).");
        if (model.SetupLines.Length == 0)
        {
            step.Check(await Shell.ShowsAsync(Shell.Text("Nothing to set up.")), "Set up model has nothing to do.");
        }
        else
        {
            step.Check(false, $"Set up model still lists: {string.Join(" / ", model.SetupLines)}.");
        }
    }

    private async Task UndoRepair(StepResult step)
    {
        if (!templateRepaired)
        {
            step.NotRun("B2.4 did not apply.");
            return;
        }

        int seq = host.LastEvent();
        host.PostCommand("undo");
        ExpectEvent(step, seq, "undo", SetupTransaction);
        PlanViewRow? template = Views().FirstOrDefault(IsExportTemplate);
        step.Check(template?.CategoryHidden("OST_Floors") == true, $"Floors are hidden again, as B2.3 left them (hidden: {template?.CategoryHidden("OST_Floors")}).");
        step.Check(template?.LineStylesHidden == false, $"Thin Lines are shown again (hidden: {template?.LineStylesHidden}).");

        seq = host.LastEvent();
        host.PostCommand("redo");
        ExpectEvent(step, seq, "redo", SetupTransaction);
        template = Views().FirstOrDefault(IsExportTemplate);
        step.Check(template?.CategoryHidden("OST_Floors") == false, $"After Redo Floors are shown (hidden: {template?.CategoryHidden("OST_Floors")}).");
        await CheckAgain();
    }

    private async Task PlanWithoutTemplate(StepResult step)
    {
        PlanViewRow? plan = GisPlans(Views()).FirstOrDefault(view => view.Template == TemplateName);
        if (plan == null)
        {
            step.NotRun($"No GIS_ plan uses {TemplateName}.");
            return;
        }

        JsonObject fixture = host.Call("fixture", new { views = new[] { new { view = plan.Name, template = "none" } } });
        step.Note($"Fixture: {fixture["log"]?.ToJsonString()}");
        await CheckAgain();
        InspectReply model = await Inspect();
        FindingItem? finding = model.Findings.FirstOrDefault(item => item.Topic == "views" && item.Message.Contains(plan.Name, StringComparison.Ordinal));
        step.Check(finding != null, $"A views finding names {plan.Name} ({finding?.Message ?? "none"}).");
        step.Check(!model.SetupLines.Any(line => line.Contains(plan.Name, StringComparison.Ordinal)), $"Set up model does not mention {plan.Name} (lines: {string.Join(" / ", model.SetupLines)}).");
        await Shot(step, "B2-7-plan-without-template");

        int seq = host.LastEvent();
        host.PostCommand("undo");
        ExpectEvent(step, seq, "undo", FixtureTransaction);
        PlanViewRow? after = Views().FirstOrDefault(view => view.Id == plan.Id);
        step.Check(after?.Template == TemplateName, $"After Undo {plan.Name} uses {TemplateName} again ({after?.Template ?? "none"}).");
        await CheckAgain();
    }

    private async Task DuplicateId(StepResult step)
    {
        long? floor = host.Call("floorTypes")["floorTypes"]!.AsArray()
            .Select(node => node!["firstFloor"]?.GetValue<long>())
            .FirstOrDefault(id => id != null);
        if (floor is not long id)
        {
            step.NotRun("The model has no floor.");
            return;
        }

        // A copy keeps its original's id; a floor never exported has none, so give it one in the same fixture.
        string existing = host.Call("params", new { ids = new[] { id }, names = new[] { "id" } })["elements"]![id.ToString()]?["id"]?.GetValue<string>() ?? string.Empty;
        object fixtureRequest = existing.Length > 0
            ? (object)new { copyInPlace = new[] { id } }
            : new { values = new[] { new { id, name = "id", value = "uitest-duplicate-id" } }, copyInPlace = new[] { id } };
        JsonObject fixture = host.Call("fixture", fixtureRequest);
        step.Note($"Fixture: {fixture["log"]?.ToJsonString()} (floor {id}, id '{(existing.Length > 0 ? existing : "uitest-duplicate-id")}').");
        await CheckAgain();
        InspectReply model = await Inspect();
        FindingItem[] shared = model.Findings.Where(item => item.Topic == "ids" && item.Message.StartsWith("2 elements share id ", StringComparison.Ordinal) && item.ElementIds.Contains(id)).ToArray();
        if (step.Check(shared.Length == 1, $"One ids error '2 elements share id …' for floor {id} (found {shared.Length})."))
        {
            step.Check(shared[0].Severity == "error", $"It is an error ({shared[0].Severity}).");
            step.Check(shared[0].ElementIds.Length == 2, $"It lists both element ids ({string.Join(", ", shared[0].ElementIds)}).");
            step.Note(shared[0].Message);
        }

        int errors = model.Count("error");
        await OpenExport(step, options.ExportView, "export-duplicate-id", keepDefaultViews: options.ExportView.Length == 0);
        ILocator export = Shell.Button(new Regex(@"^Export (1 view|\d+ views)$"));
        if (step.Check(await Shell.ShowsAsync(export, 600000), "The preview loads and offers to export."))
        {
            await export.ClickAsync();
            ILocator dialog = Shell.Page.GetByRole(AriaRole.Dialog);
            step.Check(await Shell.ShowsAsync(dialog.GetByText(new Regex($@"The GIS audit found {errors} error\(s\)"))), $"The export asks about the {errors} audit error(s).");
            await Shot(step, "B2-8-audit-dialog");
            await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).First.ClickAsync();
        }

        await Shell.GoToAsync("/gis-standard");
        int undoSeq = host.LastEvent();
        host.PostCommand("undo");
        ExpectEvent(step, undoSeq, "undo", FixtureTransaction);
        await CheckAgain();
        step.Check(!(await Inspect()).Findings.Any(item => item.Topic == "ids" && item.ElementIds.Contains(id)), "After Undo the ids error is gone.");
    }

    private async Task StairFindings(StepResult step)
    {
        InspectReply model = await Inspect();
        FindingItem[] stairs = model.Findings.Where(finding => finding.Topic is "stairs" or "escalators").ToArray();
        if (stairs.Length == 0)
        {
            step.Note("No stair or escalator finding: LUMINE has no multistorey stair or stair family named 階段 the audit flags.");
        }

        foreach (FindingItem finding in stairs)
        {
            step.Note($"{finding.Topic} {finding.Severity}: {finding.Message} ({finding.ElementIds.Length} element(s)). Judge by eye whether it is right.");
        }
    }
}
