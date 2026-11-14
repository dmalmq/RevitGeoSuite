using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The GIS standard page test (the manual "run 2" script), step for step, on a fresh copy of the
/// test model. Every step records pass or fail with what it saw; a failed check does not stop the
/// run unless later steps cannot make sense without it.
/// </summary>
internal sealed partial class GisStandardRun(RunOptions options, TestHostClient host, Report report)
{
    private const string Tab = "Revit Geo Suite";
    private const string Panel = "Geo Suite";
    private const string SetupTransaction = "Kanizu GIS: Set up model";
    private const string FixTransaction = "Kanizu GIS: Fix floor types";
    private const string ParametersTransaction = "Kanizu GIS: Parameters";
    private const string DriftTransaction = "Kanizu GIS: Review GDB edits";
    private const string FixtureTransaction = "UI test: fixture";
    private const string JapaneseName = "北改札内コンコース";
    private const string TestShortName = "UT";

    private static readonly TimeSpan EventWait = TimeSpan.FromSeconds(60);

    private readonly List<string> transactionsSeen = new();
    private ShellPage? shell;
    private string modelCopy = string.Empty;
    private InspectReply? firstInspect;
    private CsvSheet? written;
    private string editedUtf8 = string.Empty;
    private List<long> editedFloorIds = new();
    private Dictionary<long, string> namesBefore = new();
    private int previewChanges;
    private long? driftFloor;
    private const string DriftName = "UIテスト GDB名";

    private ShellPage Shell => shell ?? throw new InvalidOperationException("The page is not attached (step 1 failed).");

    public async Task RunAsync(DateTime revitStartedUtc)
    {
        try
        {
            await RunStepsAsync(revitStartedUtc);
        }
        catch (Exception ex)
        {
            // A stopped run is a failed run: the report says FAIL and the runner exits non-zero.
            report.Header.Add($"RUN STOPPED: {ex.Message}");
            var stopped = new StepResult("STOP", "The run finished every step it was asked to run");
            stopped.Check(false, $"The run stopped with {ex.GetType().Name}: {ex.Message}");
            report.Steps.Add(stopped);
            Console.Error.WriteLine($"Run stopped: {ex.Message}");
        }
    }

    private async Task RunStepsAsync(DateTime revitStartedUtc)
    {
        PrepareModel();
        try
        {
            await Step("1", "Open GIS standard from the ribbon", step => OpenPage(step, revitStartedUtc));
            if (shell == null)
            {
                return;
            }

            string language = await Shell.UseEnglishAsync();
            try
            {
                await Scenarios.Single(scenario => scenario.Name == options.Script).Steps(this);
            }
            finally
            {
                await Shell.RestoreLanguageAsync(language);
            }
        }
        finally
        {
            if (shell != null)
            {
                await shell.DisposeAsync();
            }
        }
    }

    /// <summary>
    /// Every script the runner knows, by its --scenario name. A new scenario is one entry here and a
    /// partial file with its steps; step 1 (open the page) and the English page come with every one.
    /// </summary>
    internal static readonly IReadOnlyList<Scenario> Scenarios = new Scenario[]
    {
        new("gis-standard", "the GIS standard page script (run 2), steps 2-16.", run => run.GisStandardSteps()),
        new("floor-types", "floor type standard 2, old names renamed by Fix floor types.", run => run.FloorTypeSteps()),
        new("b1", "Set up model adds the GIS export views (template, plans, crops, level_id).", run => run.ExportViewSteps()),
        new("b2a", "the export audit (ids, stairs, escalators, levels, views) and template repair.", run => run.ExportAuditSteps()),
        new("c3a-reshape", "Review GDB edits reshapes a floor to the GDB outline (RevitGeoSuite #57).", run => run.ReshapeSteps()),
        new("c3b-split", "Review GDB edits splits a floor by the GDB's pieces (C3b).", run => run.SplitSteps()),
        new("c3c-merge", "Review GDB edits merges floors the GDB merged (C3c).", run => run.MergeSteps()),
        new("c3d-hand-drawn", "Review GDB edits creates a floor for a unit drawn by hand in the GDB (C3d).", run => run.HandDrawnSteps()),
        new("a3-fields", "the export writes elevator levels_served, door fields and GDB-owned field rows (RevitGeoSuite #45).", run => run.A3Steps()),
        new("georef-read", "reads RevitGeoSuite's georeference record and Revit's coordinates from a copy of the model; changes nothing.", run => run.Step("G.R", "Read the georeference record and coordinates", run.ReadGeoreference)),
        new("c4-waiting", "GDB edits waiting in the station's drift file show on the GIS standard button and the export page, and go once applied (C4).", run => run.WaitingSteps()),
        new("c8-network", "the Network tab shows Kanizu's walking network in Revit without a transaction (C8).", run => run.NetworkSteps()),
    };

    private async Task GisStandardSteps()
    {
        await Step("2", "Set up model card shows what it would do", SetupPreview);
        await Step("3", "Apply Set up model", ApplySetup);
        await Step("4", "Undo Set up model, then Check again", UndoSetup);
        await Step("5", "Apply Set up model twice: the second run has nothing to do", SetupTwice);
        await Step("6", "Fix floor types with one row unticked", FixFloorTypes);
        await Step("7", "Undo Fix floor types, then Check again", UndoFix);
        await Step("8", "Select buttons select and show the elements", SelectButtons);
        await Step("10", "Parameters: Write CSV", WriteCsv);
        await Step("11", "Parameters: read an Excel-edited CSV", ReadEditedCsv);
        await Step("12", "Parameters: write, Undo, read again", WriteParameters);
        await Step("13", "GDB edits tab", DriftTab);
        await Step("14", "Pick a drift file for another model", OtherModelDrift);
        await Step("15", "Accept a real GDB edit, then Undo", RealDrift);
        await Step("16", "GeoPackage export asks before exporting with audit errors", ExportGate);
        await Step("9", "GIS standard with a family open", step => { FamilyOpen(step); return Task.CompletedTask; });
    }

    public IReadOnlyList<string> TransactionsSeen => transactionsSeen;

    private void PrepareModel()
    {
        string folder = Path.Combine(options.OutputFolder, "model");
        Directory.CreateDirectory(folder);
        modelCopy = Path.Combine(folder, Path.GetFileNameWithoutExtension(options.Model) + "-uitest.rvt");
        File.Copy(options.Model, modelCopy, overwrite: true);
        int beforeOpen = host.LastEvent();
        JsonObject opened = host.Call("open", new { path = modelCopy });
        report.Header.Add($"Revit links not on this PC, before opening the copy: {(opened["unloadedReferences"]?.AsArray().Count > 0 ? opened["unloadedReferences"]!.ToJsonString() : "none")}.");
        string dialogs = string.Join(" | ", host.EventsAfter(beforeOpen).Where(item => item.Kind == "dialog").Select(item => $"{item.DialogId}: {item.Message}"));
        report.Header.Add($"Dialogs while opening: {(dialogs.Length > 0 ? dialogs : "none")}.");
        string copyPath = modelCopy;
        modelCopy = opened["path"]?.GetValue<string>() ?? string.Empty;
        if (!string.Equals(modelCopy, copyPath, StringComparison.OrdinalIgnoreCase))
        {
            report.Header.Add($"The model is workshared: it was detached and saved once as its own central at {modelCopy}.");
        }

        RequireTestCopy(opened);
        JsonObject closed = host.Call("closeOthers");
        report.Header.Add($"Model: copy of {options.Model} at {modelCopy} ({opened["document"]}). The pristine file is never opened or saved.");
        report.Header.Add($"Closed earlier test documents: {closed["closed"]?.ToJsonString()}; left open: {closed["kept"]?.ToJsonString()}.");
    }

    private async Task Step(string id, string title, Func<StepResult, Task> body)
    {
        var step = new StepResult(id, title);
        report.Steps.Add(step);
        if (!options.Runs(id))
        {
            step.NotRun("Skipped by --steps.");
            return;
        }

        Console.WriteLine($"Step {id}: {title}");
        try
        {
            RequireTestCopy(host.Call("state"));
            await body(step);
        }
        catch (NotTheTestCopyException ex)
        {
            step.Check(false, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            step.Check(false, $"Stopped with {ex.GetType().Name}: {ex.Message}");
            await TryShot(step, $"{id}-error");
        }

        await ClearLeftovers(step);
        Console.WriteLine($"  -> {step.Outcome}{(step.Failures.Count > 0 ? ": " + string.Join("; ", step.Failures) : string.Empty)}");
    }

    /// <summary>
    /// Leaves nothing of a step for the next one: closes any dialog still open (a modal dialog holds
    /// Revit's API thread, so every later call would time out) and takes the page back to the GIS
    /// standard page after a failure. A dialog left open fails the step that left it.
    /// </summary>
    private async Task ClearLeftovers(StepResult step)
    {
        JsonObject result = host.Call("closeDialogs", new { timeoutMs = 20000 });
        JsonArray closed = result["closed"]?.AsArray() ?? new JsonArray();
        if (closed.Count > 0)
        {
            step.Check(false, $"The step left {closed.Count} dialog(s) open; closed so the next step starts clean: {closed.ToJsonString()}");
        }

        if (result["stillOpen"]?.AsArray() is { Count: > 0 } open)
        {
            throw new InvalidOperationException($"Dialog(s) {open.ToJsonString()} would not close after step {step.Id}; later steps cannot reach Revit.");
        }

        if (step.Failures.Count > 0 && shell != null)
        {
            try
            {
                await Shell.GoToAsync("/gis-standard");
            }
            catch (Exception ex)
            {
                step.Note($"Could not take the page back to the GIS standard page: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Stops the whole run unless the active document is this run's copy, detached when workshared:
    /// every step writes to the active document, and it must never be anything else.
    /// </summary>
    private void RequireTestCopy(JsonObject document)
    {
        string path = document["path"]?.GetValue<string>() ?? string.Empty;
        string? problem = TestCopy.Problem(path, modelCopy, options.OutputFolder,
            document["isWorkshared"]?.GetValue<bool>() == true, document["centralPath"]?.GetValue<string>() ?? string.Empty);
        if (problem != null)
        {
            throw new NotTheTestCopyException(problem);
        }
    }

    private async Task OpenPage(StepResult step, DateTime revitStartedUtc)
    {
        // A port file from an earlier Revit names a dead port; take it away so only this Revit's counts.
        string portFile = options.DevToolsFile;
        if (File.Exists(portFile) && File.GetLastWriteTimeUtc(portFile) < revitStartedUtc)
        {
            File.Delete(portFile);
            step.Note($"Deleted {portFile} from an earlier Revit session.");
        }

        host.Call("ribbon", new { tab = Tab, panel = Panel, button = "GisStandard" });
        int port = await WaitForPort(step, revitStartedUtc);
        shell = await ShellPage.AttachAsync(port, TimeSpan.FromSeconds(60));
        step.Note($"DevTools port {port} (127.0.0.1), page {Shell.Page.Url}.");
        await Shell.UseEnglishAsync();
        await Shell.GoToAsync("/gis-standard");
        step.Check(await Shell.ShowsAsync(Shell.Text("Standard version")), "The summary strip shows the standard version cell.");
        foreach (string label in new[] { "Set up model", "Fix floor types", "Audit" })
        {
            step.Check(await Shell.ShowsAsync(Shell.Text(label)), $"The '{label}' card is on the page.");
        }

        firstInspect = await Inspect();
        step.Check(await Shell.Alerts.CountAsync() == 0, $"No error banner (saw: {await Shell.AlertTextAsync()}).");
        step.Note($"Errors {firstInspect.Count("error")}, warnings {firstInspect.Count("warning")}, notes {firstInspect.Count("info")}, version '{firstInspect.ModelVersion}' / {firstInspect.StandardVersion}.");
        await Shot(step, "01-gis-standard");
    }

    private async Task SetupPreview(StepResult step)
    {
        InspectReply model = await Inspect();
        if (model.SetupLines.Length == 0)
        {
            step.Check(await Shell.ShowsAsync(Shell.Text($"The model is on standard version {model.StandardVersion}. Nothing to set up.")), "Says there is nothing to set up.");
            step.Note("The model was already set up, so steps 3-5 have nothing to apply. Use a pristine copy that was never set up.");
            return;
        }

        step.Check(await Shell.ShowsAsync(Shell.Button($"Apply {model.SetupLines.Length} change(s)")), $"Shows 'Apply {model.SetupLines.Length} change(s)'.");
        step.Note($"{model.SetupLines.Length} setup line(s), first: {model.SetupLines[0]}");
    }

    private async Task ApplySetup(StepResult step)
    {
        InspectReply before = await Inspect();
        if (before.SetupLines.Length == 0)
        {
            step.NotRun("Nothing to set up on this model.");
            return;
        }

        int seq = host.LastEvent();
        await Shell.Button($"Apply {before.SetupLines.Length} change(s)").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Text("Set up model applied"), 120000), "Green banner 'Set up model applied'.");
        ExpectEvent(step, seq, "commit", SetupTransaction);
        step.Check(await Shell.ShowsAsync(Shell.Text("Nothing to set up.")), "The Set up card now says nothing to set up.");
        string version = $"{before.StandardVersion} / {before.StandardVersion}";
        step.Check(await Shell.ShowsAsync(Shell.Text(version)), $"The version cell shows '{version}'.");
        await Shot(step, "03-setup-applied");
    }

    private async Task UndoSetup(StepResult step)
    {
        if (firstInspect == null || firstInspect.SetupLines.Length == 0)
        {
            step.NotRun("Set up model was not applied.");
            return;
        }

        int seq = host.LastEvent();
        host.PostCommand("undo");
        ExpectEvent(step, seq, "undo", SetupTransaction);
        await CheckAgain();
        InspectReply after = await Inspect();
        step.Check(after.SetupLines.SequenceEqual(firstInspect.SetupLines), $"The same preview is back ({after.SetupLines.Length} line(s), first had {firstInspect.SetupLines.Length}).");
    }

    private async Task SetupTwice(StepResult step)
    {
        InspectReply before = await Inspect();
        if (before.SetupLines.Length == 0)
        {
            step.NotRun("Nothing to set up on this model.");
            return;
        }

        int seq = host.LastEvent();
        await Shell.Button($"Apply {before.SetupLines.Length} change(s)").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Text("Set up model applied"), 120000), "Set up model applied again.");
        ExpectEvent(step, seq, "commit", SetupTransaction);
        await CheckAgain();
        InspectReply after = await Inspect();
        step.Check(after.SetupLines.Length == 0, $"The second check reports nothing to do (has {after.SetupLines.Length} line(s): {string.Join("; ", after.SetupLines.Take(3))}).");
        step.Check(await Shell.ShowsAsync(Shell.Text("Nothing to set up.")), "The card says nothing to set up.");
    }

    private ProposalItem[] fixProposals = Array.Empty<ProposalItem>();

    private async Task FixFloorTypes(StepResult step)
    {
        InspectReply before = await Inspect();
        if (SingleCandidates(before).Count < 2 && options.Renames.Count > 0)
        {
            // Before floor type standard 2, LUMINE has no floor type the name rules match after Set up
            // model, so give two types names they do match: the renames Daniel made by hand in run 2.
            JsonObject fixture = host.Call("fixture", new { renames = options.Renames.Select(pair => new { from = pair.From, to = pair.To }).ToArray() });
            step.Note($"Fixture: {fixture["log"]?.ToJsonString()}");
            await CheckAgain();
            before = await Inspect();
        }

        fixProposals = before.Proposals;
        if (!string.IsNullOrEmpty(before.FixBlocker))
        {
            step.Check(false, $"Fix floor types is blocked: {before.FixBlocker}");
            return;
        }

        // A floor type with one candidate starts ticked; one with several starts with none ticked.
        List<ProposalItem> ticked = SingleCandidates(before);
        if (ticked.Count < 2)
        {
            step.NotRun($"Needs at least two pre-ticked proposals to untick one; the model has {ticked.Count}.");
            return;
        }

        ProposalItem kept = ticked[0];
        int fixedCount = ticked.Count - 1;
        await ProposalCheckbox(kept.FloorTypeName, kept.StandardTypeName).UncheckAsync();
        int seq = host.LastEvent();
        await Shell.Button($"Apply to {fixedCount} type(s)").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Text($"{fixedCount} floor type(s) fixed"), 120000), $"Banner '{fixedCount} floor type(s) fixed'.");
        ExpectEvent(step, seq, "commit", FixTransaction);
        InspectReply after = await Inspect();
        var left = after.Proposals.Select(proposal => proposal.FloorTypeId).ToHashSet();
        step.Check(left.Contains(kept.FloorTypeId), $"The unticked type '{kept.FloorTypeName}' stays in the table.");
        step.Check(!ticked.Skip(1).Any(proposal => left.Contains(proposal.FloorTypeId)), "The fixed types leave the table.");
        int warningsBefore = before.FloorTypeFindings();
        int warningsAfter = after.FloorTypeFindings();
        step.Check(warningsAfter == warningsBefore - fixedCount, $"Floor type findings drop by {fixedCount} (were {warningsBefore}, now {warningsAfter}).");
        await Shot(step, "06-floor-types-fixed");
    }

    private static List<ProposalItem> SingleCandidates(InspectReply model) =>
        model.Proposals.GroupBy(proposal => proposal.FloorTypeId).Where(group => group.Count() == 1).Select(group => group.First()).ToList();

    // Standard 2 labels a row's checkbox "<type> to <standard type>"; standard 1 labels it "<type>".
    private ILocator ProposalCheckbox(string floorTypeName, string? standardTypeName)
    {
        string pattern = standardTypeName is { Length: > 0 }
            ? $"^{Regex.Escape(floorTypeName)}( to {Regex.Escape(standardTypeName)})?$"
            : $"^{Regex.Escape(floorTypeName)}$";
        return Shell.Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new Regex(pattern) }).First;
    }

    private async Task UndoFix(StepResult step)
    {
        if (fixProposals.Length < 2)
        {
            step.NotRun("Fix floor types was not applied.");
            return;
        }

        int seq = host.LastEvent();
        host.PostCommand("undo");
        ExpectEvent(step, seq, "undo", FixTransaction);
        await CheckAgain();
        InspectReply after = await Inspect();
        step.Check(
            after.Proposals.Select(proposal => proposal.FloorTypeId).ToHashSet().SetEquals(fixProposals.Select(proposal => proposal.FloorTypeId)),
            $"The proposals are back ({after.Proposals.Length}, were {fixProposals.Length}).");
    }

    private async Task SelectButtons(StepResult step)
    {
        InspectReply model = await Inspect();
        FindingItem? finding = model.Findings.FirstOrDefault(item => item.ElementIds.Length > 0);
        if (finding == null)
        {
            step.Note("No audit row has elements to select.");
        }
        else
        {
            await Shell.Button(new Regex(@"^Select \d+$")).First.ClickAsync();
            await ExpectSelection(step, finding.ElementIds, $"audit row '{finding.Topic}: {finding.Message}'");
            RevitShot(step, "08-audit-selection");
        }

        if (model.Unmatched.Length == 0)
        {
            step.Note("No floor type matches no standard type, so that Select was not tried.");
        }
        else
        {
            UnmatchedItem unmatched = model.Unmatched[0];
            await Shell.Text(new Regex("match no standard type")).First.ClickAsync();
            await Shell.Button("Select").First.ClickAsync();
            await ExpectSelection(step, unmatched.FloorIds, $"floor type '{unmatched.Name}'");
            RevitShot(step, "08-unmatched-selection");
        }

        step.Check(await Shell.Alerts.CountAsync() == 0, $"The page shows no error (saw: {await Shell.AlertTextAsync()}).");
        await Shot(step, "08-page");
    }

    private async Task WriteCsv(StepResult step)
    {
        await Shell.Page.GetByRole(AriaRole.Radio, new() { Name = "Parameters", Exact = true }).ClickAsync();
        string folder = Path.Combine(options.OutputFolder, "csv");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "gis-parameters.csv");
        File.Delete(path);
        await Shell.Button("Write CSV…").ClickAsync();
        host.Call("fileDialog", new { path, timeoutMs = 30000 });
        ILocator line = Shell.Page.GetByTestId("parameters-written");
        step.Check(await Shell.ShowsAsync(line, 60000), "A line 'Wrote N rows to …'.");
        string text = await line.InnerTextAsync();
        step.Note(text.Trim());
        byte[] data = File.ReadAllBytes(path);
        step.Check(CsvSheet.StartsWithUtf8Bom(data), "The file is UTF-8 with a byte order mark, so Excel reads Japanese correctly.");
        written = CsvSheet.Read(data);
        Match rows = Regex.Match(text, @"Wrote (\d+) rows");
        step.Check(rows.Success && int.Parse(rows.Groups[1].Value) == written.Rows.Count, $"The row count on the page matches the file ({written.Rows.Count} rows).");
        step.Note($"Japanese text in the file: {(written.Rows.Any(row => row.Values.Any(HasJapanese)) ? "yes" : "no")}.");

        if (options.Excel)
        {
            string roundTrip = Path.Combine(folder, "excel-round-trip.csv");
            step.Note(ExcelRoundTrip.Save(path, roundTrip));
            await Shell.Button("Read edited CSV…").ClickAsync();
            host.Call("fileDialog", new { path = roundTrip, timeoutMs = 30000 });
            step.Check(
                await Shell.ShowsAsync(Shell.Text("The model already has every value in the file.")),
                "A CSV opened and saved by Excel without edits reads back with no changes, so Excel kept the Japanese text.");
        }
        else
        {
            step.Note("Excel round trip not run (pass --excel on a PC with Excel).");
        }
    }

    private async Task ReadEditedCsv(StepResult step)
    {
        if (written == null)
        {
            step.NotRun("Step 10 wrote no CSV.");
            return;
        }

        CsvSheet edited = written.Copy();
        List<Dictionary<string, string>> floors = edited.Rows.Where(row => row["kind"] == "floor" && row["name"] != JapaneseName).ToList();
        Dictionary<string, string>? level = edited.Rows.FirstOrDefault(row => row["kind"] == "level" && row["short_name"] != TestShortName);
        if (floors.Count < 3 || level == null)
        {
            step.Check(false, $"The CSV needs three floor rows and a level row to edit (has {floors.Count} floors).");
            return;
        }

        floors[0]["name"] = JapaneseName;
        floors[1]["name"] = JapaneseName;
        level["short_name"] = TestShortName;
        floors[2]["category"] = "shop";
        Dictionary<string, string> deleted = edited.Rows.Last(row => !ReferenceEquals(row, floors[0]) && !ReferenceEquals(row, floors[1]) && !ReferenceEquals(row, floors[2]) && !ReferenceEquals(row, level));
        edited.Rows.Remove(deleted);
        editedFloorIds = floors.Take(2).Select(row => long.Parse(row["element_id"])).ToList();
        namesBefore = floors.Take(2).ToDictionary(row => long.Parse(row["element_id"]), row => written.Rows.First(original => original["element_id"] == row["element_id"])["name"]);
        step.Note($"Edits: name on floors {string.Join(", ", editedFloorIds)}, short_name on level {level["element_id"]}, category 'shop' on floor {floors[2]["element_id"]}, deleted row {deleted["kind"]} {deleted["element_id"]}.");

        string folder = Path.Combine(options.OutputFolder, "csv");
        string cp932 = Path.Combine(folder, "edited-excel-cp932.csv");
        editedUtf8 = Path.Combine(folder, "edited-utf8-bom.csv");
        File.WriteAllBytes(cp932, edited.Write(CsvSheet.ExcelJapanese));
        File.WriteAllBytes(editedUtf8, edited.Write(CsvSheet.Utf8WithBom));

        await Shell.Button("Read edited CSV…").ClickAsync();
        host.Call("fileDialog", new { path = cp932, timeoutMs = 30000 });
        previewChanges = await ExpectPreview(step, 3);
        step.Check(await Shell.ShowsAsync(Shell.Text(JapaneseName)), "The Japanese name shows correctly in the table.");
        ILocator notWritten = Shell.Text(new Regex(@"1 row\(s\) will not be written"));
        if (step.Check(await Shell.ShowsAsync(notWritten), "'1 row(s) will not be written'."))
        {
            await notWritten.First.ClickAsync();
            step.Check(await Shell.ShowsAsync(Shell.Text("'shop' is not a unit category")), "It lists \"'shop' is not a unit category\".");
        }

        await Shot(step, "11-edited-csv");
    }

    private async Task WriteParameters(StepResult step)
    {
        if (previewChanges < 2)
        {
            step.NotRun("Step 11 showed no preview to write.");
            return;
        }

        int ticked = previewChanges - 1;
        await PreviewCheckboxes().First.UncheckAsync();
        int seq = host.LastEvent();
        await Shell.Button($"Write {ticked} change(s)").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Text($"{ticked} parameter value(s) written"), 120000), $"Banner '{ticked} parameter value(s) written'.");
        ExpectEvent(step, seq, "commit", ParametersTransaction);
        Dictionary<long, string?> names = ReadNames(editedFloorIds);
        step.Check(names.Values.Any(name => name == JapaneseName), $"A written floor's name holds the Japanese text ({Describe(names)}).");

        seq = host.LastEvent();
        host.PostCommand("undo");
        ExpectEvent(step, seq, "undo", ParametersTransaction);
        names = ReadNames(editedFloorIds);
        step.Check(editedFloorIds.All(id => (names[id] ?? string.Empty) == namesBefore[id]), $"After Undo the names are back ({Describe(names)}).");

        await Shell.Button("Read edited CSV…").ClickAsync();
        host.Call("fileDialog", new { path = editedUtf8, timeoutMs = 30000 });
        await ExpectPreview(step, previewChanges);
        step.Note("The second read used the same edits saved as UTF-8 with a byte order mark; the first used Excel's Shift-JIS.");
    }

    private async Task DriftTab(StepResult step)
    {
        await Shell.Page.GetByRole(AriaRole.Radio, new() { Name = "GDB edits", Exact = true }).ClickAsync();
        ILocator none = Shell.Page.GetByTestId("drift-none");
        ILocator rows = Shell.Page.GetByRole(AriaRole.Table);
        bool shown = await Shell.ShowsAsync(none.Or(rows), 60000);
        step.Check(shown, "The tab shows either GDB edit rows or why there are none.");
        if (await none.CountAsync() > 0)
        {
            step.Note((await none.InnerTextAsync()).Trim());
        }
        else if (shown)
        {
            step.Note($"{await rows.Locator("tbody tr").CountAsync()} GDB edit row(s).");
        }

        await Shot(step, "13-gdb-edits");
    }

    private async Task OtherModelDrift(StepResult step)
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "other-model.gis-drift.json");
        string copy = Path.Combine(options.OutputFolder, "csv", "other-model.gis-drift.json");
        File.Copy(fixture, copy, overwrite: true);
        await Shell.Button("Pick a drift file…").ClickAsync();
        host.Call("fileDialog", new { path = copy, timeoutMs = 30000 });
        // "For another model" needs the open model to have a model id, which it gets from its first
        // export. A copy never exported has none, and the page treats the file as picked by hand.
        ILocator other = Shell.Text("This drift file is for another model");
        ILocator picked = Shell.Text("This drift file was picked by hand");
        step.Check(await Shell.ShowsAsync(other.Or(picked)), "A warning names the drift file's model and the open model.");
        bool isOther = await other.CountAsync() > 0;
        step.Note(isOther
            ? "The warning says the file is for another model."
            : "The open copy has no model id (never exported), so the warning says 'picked by hand' instead of 'for another model'.");
        ILocator accept = Shell.Button(new Regex(isOther ? @"^Accept 0 (value )?change\(s\) from another model$" : @"^Accept \d+ (value )?change\(s\)$"));
        if (isOther && step.Check(await Shell.ShowsAsync(accept), "The button reads 'Accept 0 value change(s) from another model' (before C4: 'Accept 0 change(s) …')."))
        {
            step.Check(await accept.IsDisabledAsync(), "That button is disabled.");
        }

        ILocator boxes = Shell.Page.GetByRole(AriaRole.Table).GetByRole(AriaRole.Checkbox);
        int count = await boxes.CountAsync();
        int ticked = 0;
        for (int index = 0; index < count; index++)
        {
            ticked += await boxes.Nth(index).IsCheckedAsync() ? 1 : 0;
        }

        if (isOther)
        {
            step.Check(ticked == 0, $"Every row starts unticked ({ticked} of {count} ticked).");
        }
        else
        {
            step.Note($"{ticked} of {count} row(s) start ticked; only a file for another model starts all unticked.");
        }
        step.Note($"{count} row(s); 'will not be written' list shown: {await Shell.Text("will not be written").CountAsync() > 0}.");
        await Shot(step, "14-other-model-drift");
    }

    // With --real-drift the model's own Kanizu drift file is used. Otherwise the runner writes a
    // one-row drift file for a floor of this copy and picks it by hand, which reaches the same
    // accept, transaction and Undo.
    private async Task RealDrift(StepResult step)
    {
        if (options.RealDrift)
        {
            await Shell.Button("Check again").Last.ClickAsync();
        }
        else
        {
            if (written == null)
            {
                step.NotRun("Step 10 wrote no CSV to take a floor from.");
                return;
            }

            long floor = long.Parse(written.Rows.First(row => row["kind"] == "floor" && !editedFloorIds.Contains(long.Parse(row["element_id"])))["element_id"]);
            driftFloor = floor;
            string uniqueId = host.Call("uniqueIds", new { ids = new[] { floor } })["uniqueIds"]![floor.ToString()]!.GetValue<string>();
            string current = ReadNames(new[] { floor })[floor] ?? string.Empty;
            JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path = string.Empty, pick = false });
            string model = drift?["modelKey"]?.GetValue<string>() is { Length: > 0 } key ? key : "uitest";
            string path = Path.Combine(options.OutputFolder, "csv", "uitest.gis-drift.json");
            File.WriteAllText(path, DriftFixture.Write(model, uniqueId, current, DriftName));
            step.Note($"Generated {path}: floor {floor} name '{current}' to '{DriftName}' (model '{model}').");
            await Shell.Button("Pick a drift file…").ClickAsync();
            host.Call("fileDialog", new { path, timeoutMs = 30000 });
        }

        ILocator boxes = Shell.Page.GetByRole(AriaRole.Table).GetByRole(AriaRole.Checkbox);
        if (!step.Check(await Shell.ShowsAsync(boxes, 60000), "The drift file has rows."))
        {
            return;
        }

        int rows = await boxes.CountAsync();
        for (int index = 0; index < rows; index++)
        {
            await boxes.Nth(index).SetCheckedAsync(index == 0);
        }

        int seq = host.LastEvent();
        await Shell.Button(new Regex(@"^Accept 1 (value )?change\(s\)")).Last.ClickAsync();
        if (await Shell.ShowsAsync(Shell.Page.GetByTestId("drift-confirm"), 3000))
        {
            await Shell.Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^Accept 1 (value )?change\(s\)") }).ClickAsync();
        }

        step.Check(await Shell.ShowsAsync(Shell.Text("1 GDB value(s) accepted"), 120000), "Banner '1 GDB value(s) accepted'.");
        ExpectEvent(step, seq, "commit", DriftTransaction);
        if (driftFloor is long id)
        {
            step.Check(ReadNames(new[] { id })[id] == DriftName, $"Floor {id}'s name is now '{DriftName}'.");
        }

        seq = host.LastEvent();
        host.PostCommand("undo");
        ExpectEvent(step, seq, "undo", DriftTransaction);
        await Shell.Button("Check again").Last.ClickAsync();
        step.Check(await Shell.ShowsAsync(boxes) && await boxes.CountAsync() == rows, $"After Undo the row is proposed again ({await boxes.CountAsync()} row(s), were {rows}).");
        await Shot(step, "15-drift-after-undo");
    }

    private async Task ExportGate(StepResult step)
    {
        if (string.IsNullOrWhiteSpace(options.ExportView))
        {
            step.NotRun("Pass --export-view <plan view name> to run the export check.");
            return;
        }

        InspectReply model = await Inspect();
        int errors = model.Count("error");
        if (errors == 0)
        {
            // LUMINE audits clean after set up, so give one floor a category the standard does not
            // know: the audit reports it as an error and the export has to ask.
            if (written == null)
            {
                step.NotRun("The GIS audit has no errors and step 10 wrote no CSV to pick a floor from.");
                return;
            }

            long floor = long.Parse(written.Rows.First(row => row["kind"] == "floor")["element_id"]);
            JsonObject fixture = host.Call("fixture", new { values = new[] { new { id = floor, name = "category", value = "uitest-not-a-category" } } });
            step.Note($"Fixture: {fixture["log"]?.ToJsonString()} (category 'uitest-not-a-category').");
            errors = (await Inspect()).Count("error");
            if (!step.Check(errors > 0, "The fixture gives the audit an error."))
            {
                return;
            }
        }

        string output = await OpenExport(step, options.ExportView, "export");
        ILocator export = Shell.Button(new Regex(@"^Export (1 view|\d+ views)$"));
        if (!step.Check(await Shell.ShowsAsync(export, 600000), "The preview loads and offers to export."))
        {
            return;
        }

        ILocator dialog = Shell.Page.GetByRole(AriaRole.Dialog);
        ILocator accept = dialog.GetByRole(AriaRole.Checkbox);
        ILocator anyway = dialog.GetByRole(AriaRole.Button, new() { Name = "Export anyway", Exact = true });
        await export.ClickAsync();
        step.Check(await Shell.ShowsAsync(dialog.GetByText(new Regex($@"The GIS audit found {errors} error\(s\)"))), $"A dialog lists the {errors} audit error(s).");
        step.Check(!await accept.IsCheckedAsync(), "'Export anyway' starts unticked.");
        step.Check(await anyway.IsDisabledAsync(), "The 'Export anyway' button is disabled.");
        await Shot(step, "16-audit-dialog");
        await dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).First.ClickAsync();
        await export.ClickAsync();
        step.Check(await Shell.ShowsAsync(accept) && !await accept.IsCheckedAsync(), "After closing and exporting again, the box is unticked again.");
        await accept.CheckAsync();
        await anyway.ClickAsync();
        if (await FinishExport(step))
        {
            ILocator accepted = Shell.Text(new Regex($@"Exported with {errors} GIS audit error\(s\) accepted"));
            step.Check(await Shell.ShowsAsync(accepted, 10000), $"The export's warnings include 'Exported with {errors} GIS audit error(s) accepted'.");
        }

        step.Note($"Output folder: {output}");
        await Shot(step, "16-export-done");
        await Shell.GoToAsync("/gis-standard");
    }

    /// <summary>
    /// Opens GeoPackage Export, ticks one view (or all of them when <paramref name="viewName"/> is
    /// empty), picks a fresh output folder in the run folder and opens the preview. Returns the folder.
    /// </summary>
    private async Task<string> OpenExport(StepResult step, string viewName, string folderName, bool keepDefaultViews = false, Func<Task>? beforePreview = null)
    {
        await Shell.GoToAsync("/export/geopackage");
        if (keepDefaultViews)
        {
            await Shell.Page.GetByPlaceholder("Filter views...").WaitForAsync();
        }
        else if (viewName.Length > 0)
        {
            await Shell.Page.GetByPlaceholder("Filter views...").FillAsync(viewName);
            await Shell.Button("Clear all").ClickAsync();
            await Shell.Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new Regex("^" + Regex.Escape(viewName)) }).First.CheckAsync();
        }
        else
        {
            await Shell.Button("Select all").ClickAsync();
        }

        string output = Path.Combine(options.OutputFolder, folderName);
        await SetOutputFolder(step, output);
        if (beforePreview != null)
        {
            await beforePreview();
        }

        await Shell.Button("Preview").ClickAsync();
        step.Note($"Export of {(keepDefaultViews ? "the default view choice" : viewName.Length > 0 ? $"view '{viewName}'" : "all plan views")} to {output}.");
        return output;
    }

    /// <summary>
    /// Points the export page's output folder at <paramref name="folder"/>, a fresh folder in this run's
    /// folder, and reads the field back. The page starts on the folder it remembers from the last export
    /// (once Daniel's own data), so it types the path, falls back to Browse... and its folder picker, and
    /// throws before any Preview or Export click when the field shows anything else.
    /// </summary>
    private async Task SetOutputFolder(StepResult step, string folder)
    {
        if (!TestPaths.IsUnder(folder, options.OutputFolder))
        {
            throw new InvalidOperationException($"Refusing output folder {folder}: it is not inside this run's folder {options.OutputFolder}.");
        }

        Directory.CreateDirectory(folder);
        ILocator field = Shell.Page.Locator("#floorplan-output-folder");
        string remembered = await field.InputValueAsync();
        await field.FillAsync(folder);
        await field.PressAsync("Tab");
        string shown = await field.InputValueAsync();
        if (!TestPaths.Same(shown, folder))
        {
            step.Note($"Typing the output folder left the field at '{shown}'; setting it with Browse... instead.");
            await Shell.Button("Browse...").ClickAsync();
            host.Call("fileDialog", new { path = folder, timeoutMs = 30000 });
            shown = await field.InputValueAsync();
        }

        if (!TestPaths.Same(shown, folder))
        {
            throw new InvalidOperationException($"The output folder field shows '{shown}', not this run's {folder}. Nothing was exported.");
        }

        step.Note($"Output folder: the page offered '{remembered}'; the field now shows {shown}.");
    }

    /// <summary>Waits for the export to finish and opens its warning list. False when it failed or never finished.</summary>
    private async Task<bool> FinishExport(StepResult step)
    {
        ILocator done = Shell.Button("Open folder");
        ILocator failed = Shell.Text("Export failed");
        if (!step.Check(await Shell.ShowsAsync(done.Or(failed), 1800000), "The export finishes within 30 minutes."))
        {
            return false;
        }

        if (await failed.CountAsync() > 0)
        {
            step.Check(false, "The export failed; the screenshot shows its message.");
            return false;
        }

        ILocator show = Shell.Button("Show warnings");
        if (await show.CountAsync() > 0)
        {
            await show.ClickAsync();
        }

        return true;
    }

    private void FamilyOpen(StepResult step)
    {
        string family = Path.Combine(options.OutputFolder, "model", "uitest-family.rfa");
        host.Call("newFamily", new { path = family });
        int seq = host.LastEvent();
        host.Call("ribbon", new { tab = Tab, panel = Panel, button = "GisStandard" });
        try
        {
            RevitEvent shown = host.WaitForEvent(seq, item => item.Kind == "dialog", TimeSpan.FromSeconds(20), "a dialog");
            step.Check(shown.Message.Contains("Open a project model", StringComparison.Ordinal), $"The dialog says 'Open a project model …' (it said: {shown.Message}).");
        }
        catch (TimeoutException)
        {
            // Revit raises DialogBoxShowing for its own dialogs; read an add-in's TaskDialog off the screen instead.
            JsonObject pressed = host.Call("pressButton", new { button = "Close", timeoutMs = 10000 });
            string text = pressed["text"]?.GetValue<string>() ?? string.Empty;
            step.Check(text.Contains("Open a project model", StringComparison.Ordinal), $"The dialog says 'Open a project model …' (it said: {text}).");
        }
        finally
        {
            host.Call("activate", new { path = modelCopy });
            host.Call("closeOthers");
        }
    }

    /// <summary>
    /// Waits for this Revit's WebView2 to write its DevTools port, for up to 5 minutes. A posted ribbon
    /// click can be lost: on the build PC the port once came 1 s after a second click at 151 s. The button
    /// only shows the one shell window (GetOrCreateWindow), so clicking again is safe; the runner clicks
    /// again 30 s after the first click and every 60 s after that, and notes each click so a lost one shows.
    /// </summary>
    private async Task<int> WaitForPort(StepResult step, DateTime revitStartedUtc)
    {
        DateTime start = DateTime.UtcNow;
        TimeSpan nextClick = TimeSpan.FromSeconds(30);
        int clicks = 1;
        while (true)
        {
            try
            {
                int port = DevToolsPort.Read(options.DevToolsFile, revitStartedUtc);
                step.Note($"The page's DevTools port was ready {(DateTime.UtcNow - start).TotalSeconds:0} s after the first ribbon click, after {clicks} click(s).");
                return port;
            }
            catch (InvalidOperationException ex)
            {
                TimeSpan waited = DateTime.UtcNow - start;
                if (waited > TimeSpan.FromMinutes(5))
                {
                    throw new InvalidOperationException($"{ex.Message} Waited {waited.TotalSeconds:0} s. Revit dialogs: {host.Call("windows")["windows"]?.ToJsonString()}.");
                }

                if (waited > nextClick)
                {
                    nextClick += TimeSpan.FromSeconds(60);
                    clicks++;
                    step.Note($"No DevTools port {waited.TotalSeconds:0} s after the first ribbon click; clicking it again (click {clicks}). Revit dialogs: {host.Call("windows")["windows"]?.ToJsonString()}.");
                    host.Call("ribbon", new { tab = Tab, panel = Panel, button = "GisStandard" });
                }

                await Task.Delay(1000);
            }
        }
    }

    private async Task<InspectReply> Inspect() => await Shell.RpcAsync<InspectReply>("gisStandard.inspect");

    // Check again disables itself while the page reads the model; wait until it is enabled again.
    private async Task CheckAgain()
    {
        ILocator button = Shell.Button("Check again").First;
        await button.ClickAsync();
        await Shell.Page.WaitForTimeoutAsync(300);
        DateTime end = DateTime.UtcNow + TimeSpan.FromSeconds(120);
        while (await button.IsDisabledAsync() && DateTime.UtcNow < end)
        {
            await Shell.Page.WaitForTimeoutAsync(200);
        }
    }

    private ILocator PreviewCheckboxes() => Shell.Page.GetByRole(AriaRole.Table).GetByRole(AriaRole.Checkbox);

    private async Task<int> ExpectPreview(StepResult step, int expected)
    {
        ILocator title = Shell.Text(new Regex(@"\d+ change\(s\) in "));
        if (!step.Check(await Shell.ShowsAsync(title, 60000), "A preview of the changes appears."))
        {
            return 0;
        }

        int rows = await PreviewCheckboxes().CountAsync();
        step.Check(rows == expected, $"The preview has {expected} change(s) (shows {rows}).");
        return rows;
    }

    /// <summary>
    /// Posts Undo or Redo and waits for Revit to report it on <paramref name="transaction"/>. Revit
    /// refuses Undo in some runs (b1, b2a) for a reason outside the model; the test host's error then
    /// names Revit's state. A refusal is noted, not failed: the step checks that
    /// <paramref name="transaction"/> committed as one transaction, which is what one Undo takes back,
    /// and gets false so it can check the model as it is and put back what Undo would have.
    /// </summary>
    private bool UndoOrRedo(StepResult step, string command, string transaction)
    {
        int seq = host.LastEvent();
        try
        {
            host.PostCommand(command);
        }
        catch (TestHostException ex) when (ex.Message.Contains("cannot run", StringComparison.Ordinal))
        {
            step.Note($"Revit refused {command}, so this step checks the model by state instead. {ex.Message}");
            RevitEvent[] commits = host.EventsAfter(0).Where(item => item.Is("commit", transaction)).ToArray();
            step.Check(commits.Length > 0 && commits[^1].Names.Count == 1,
                $"'{transaction}' committed as one transaction, so one Undo takes it back ({(commits.Length == 0 ? "no commit seen" : commits[^1].ToString())}).");
            return false;
        }

        ExpectEvent(step, seq, command, transaction);
        return true;
    }

    private void ExpectEvent(StepResult step, int after, string kind, string name)
    {
        try
        {
            RevitEvent found = host.WaitForEvent(after, item => item.Is(kind, name), EventWait, $"{kind} of '{name}'");
            step.Note($"Revit: {found}");
            if (kind == "commit")
            {
                transactionsSeen.Add(name);
            }
        }
        catch (TimeoutException ex)
        {
            step.Check(false, ex.Message);
        }
    }

    private async Task ExpectSelection(StepResult step, long[] expected, string what)
    {
        long[] selection = Array.Empty<long>();
        JsonObject state = new();
        DateTime end = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < end)
        {
            state = host.Call("state");
            selection = state["selection"]?.AsArray().Select(id => id!.GetValue<long>()).ToArray() ?? Array.Empty<long>();
            if (selection.ToHashSet().SetEquals(expected))
            {
                break;
            }

            await Task.Delay(300);
        }

        step.Check(selection.ToHashSet().SetEquals(expected), $"Select on {what} selects its {expected.Length} element(s) in Revit (selected {selection.Length}).");
        step.Note($"Active view: {state["viewName"]}; zoom corners: {state["zoom"]?.ToJsonString() ?? "no open view"}.");
    }

    private Dictionary<long, string?> ReadNames(IEnumerable<long> ids)
    {
        JsonObject elements = host.Call("params", new { ids = ids.ToArray(), names = new[] { "name" } })["elements"]!.AsObject();
        return ids.ToDictionary(id => id, id => elements[id.ToString()]?["name"]?.GetValue<string>());
    }

    private async Task Shot(StepResult step, string name)
    {
        string relative = Path.Combine("screenshots", name + ".png");
        await Shell.ScreenshotAsync(Path.Combine(options.OutputFolder, relative));
        step.Screenshots.Add(relative.Replace('\\', '/'));
    }

    private void RevitShot(StepResult step, string name)
    {
        string relative = Path.Combine("screenshots", name + "-revit.png");
        try
        {
            host.Call("screenshot", new { path = Path.Combine(options.OutputFolder, relative) });
            step.Screenshots.Add(relative.Replace('\\', '/'));
        }
        catch (TestHostException ex)
        {
            step.Note($"No Revit screenshot: {ex.Message}");
        }
    }

    private async Task TryShot(StepResult step, string name)
    {
        if (shell == null)
        {
            return;
        }

        try
        {
            await Shot(step, name);
        }
        catch (PlaywrightException)
        {
        }
    }

    private static bool HasJapanese(string text) => text.Any(c => c is >= '぀' and <= 'ヿ' or >= '一' and <= '鿿');

    private static string Describe(Dictionary<long, string?> names) =>
        string.Join(", ", names.Select(pair => $"{pair.Key}: {pair.Value ?? "(none)"}"));
}

internal sealed record Scenario(string Name, string Summary, Func<GisStandardRun, Task> Steps);
