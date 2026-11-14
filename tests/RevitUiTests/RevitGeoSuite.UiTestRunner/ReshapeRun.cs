using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The C3a check (/mnt/project-files/revit-template-review/c3/ui-tests/c3a-reshape.md): Review GDB
/// edits reshapes a floor to the GDB outline. The scenario makes its own drift file from its own
/// export unless --drift names one. Step ids are C3a.P (prepare) and C3a.1 to C3a.10.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const double GrowM = 1.0;
    private const double ShrinkBandM = 1.0;
    private const string SlopeArrow = "has a slope arrow or other sketch line besides its boundary";
    private const string CurvedEdges = "has curved edges, which the GDB's straight-segment outline would replace with short lines";

    private List<FloorUnit> exportedUnits = new();
    private ReshapeShape? reshape;
    private ElementRow? reshapeBefore;
    private bool reshapeApplied;
    private bool reshapeUndone = true;

    private async Task ReshapeSteps()
    {
        await Step("C3a.0", "The model has had Set up model once, so each level has a GIS_ plan", EnsureSetUp);
        await Step("C3a.P", "Export, make the reshape drift file, read floor F", PrepareReshape);
        await Step("C3a.1", "Pick the drift file: one shape edit card for floor F", PickReshape);
        await Step("C3a.2", "Show in Revit draws both outlines in the GIS_ plan, with no transaction", ShowShape);
        await Step("C3a.3", "Hide in Revit takes the outlines away, with no transaction", HideShape);
        await Step("C3a.4", "Apply 1 shape edit: one undo step, same UniqueId and type, new outline", ApplyReshape);
        await Step("C3a.5", "Reading the file again offers nothing", ReshapeDone);
        await Step("C3a.6", "One Undo puts floor F back", UndoReshape);
        await Step("C3a.7", "Redo, export again: F's outline is the GDB outline within 1 cm", RoundTrip);
        await Step("C3a.8", "A floor with a slope arrow or extra sketch line is a problem, not a card", SlopeArrowFloor);
        await Step("C3a.9", "Shrink past a hosted element: what Revit does to it", ShrinkPastHosted);
        await Step("C3a.10", "A floor with curved edges is a problem, not a card", CurvedFloor);
    }

    private async Task PrepareReshape(StepResult step)
    {
        if (!await ExportFloorUnits(step))
        {
            return;
        }

        string path = options.Drift;
        if (path.Length == 0)
        {
            HashSet<string> notEditable = FloorScan().Where(floor => floor.HasArc || floor.ExtraSketchLines || floor.ShapeEdited).Select(floor => floor.UniqueId).ToHashSet();
            List<FloorUnit> editable = exportedUnits.Where(unit => !notEditable.Contains(unit.UniqueId)).ToList();
            FloorUnit? unit = ReshapeFixture.Pick(editable, "retail") ?? ReshapeFixture.Pick(editable, "walkway");
            if (!step.Check(unit != null, "The export has a retail (or walkway) floor unit whose sketch can be redrawn."))
            {
                return;
            }

            path = WriteDrift("reshape.gis-drift.json", unit!, ReshapeFixture.Reshaped(unit!, ReshapeFixture.Grow(unit!, GrowM)));
            step.Note($"Wrote {path}: floor unit '{unit!.Name}' ({unit.Category}), east edge (vertices within {ReshapeFixture.EastEdgeToleranceM} m of the easternmost) {GrowM} m east, EPSG:{unit.Epsg}.");
        }
        else
        {
            step.Note($"Using the drift file from --drift: {path}.");
        }

        reshape = ReshapeShape.Read(path);
        reshapeBefore = Element(reshape.UniqueId);
        step.Note($"F = {reshape.UniqueId}, element {reshapeBefore.Id}, type {reshapeBefore.TypeName}, box {Box(reshapeBefore)}.");
        if (step.Check(reshapeBefore.Exists, $"Floor F ({reshape.UniqueId}) is in the model."))
        {
            DumpFloorGeometry(step, "afterPrepare");
        }
    }

    private readonly Dictionary<string, JsonObject> floorGeometry = new();

    private void DumpFloorGeometry(StepResult step, string moment) => DumpFloorGeometry(step, "F", reshape!.UniqueId, reshape.Path, moment);

    /// <summary>
    /// Adds a floor's sketch profile and top-face loops (model feet) under <paramref name="moment"/> to
    /// C3a-floor-&lt;letter&gt;-geometry.json in the run folder, with the drift file, so the port thread can
    /// rebuild offline the sketch Apply submits.
    /// </summary>
    private void DumpFloorGeometry(StepResult step, string letter, string uniqueId, string driftPath, string moment)
    {
        string path = Path.Combine(options.OutputFolder, $"C3a-floor-{letter}-geometry.json");
        JsonObject file = floorGeometry.TryGetValue(letter, out JsonObject? existing) ? existing : floorGeometry[letter] = new JsonObject();
        try
        {
            file[moment] = host.Call("floorGeometry", new { uniqueId });
            file["driftPath"] = driftPath;
            file["drift"] = JsonNode.Parse(File.ReadAllText(driftPath));
            File.WriteAllText(path, file.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            step.Note($"Floor {letter}'s geometry ({moment}) is in {path}.");
        }
        catch (TestHostException ex)
        {
            step.Note($"Floor {letter}'s geometry ({moment}) could not be read: {ex.Message}");
        }
    }

    private async Task PickReshape(StepResult step)
    {
        if (!Ready(step))
        {
            return;
        }

        await PickDrift(reshape!.Path);
        ILocator card = Shell.Page.GetByTestId("shape-edit");
        await ExpectShapeEdits(step, reshape.Path);
        step.Check(await Shell.ShowsAsync(card.GetByText($"Reshape floor {reshapeBefore!.Id}")), $"The card names floor F's element id {reshapeBefore.Id}.");
        // The drawing is in model feet, so on a rotated model an east grow need not widen it; it always adds area.
        JsonElement paths = await card.First.EvaluateAsync<JsonElement>(
            "card => [...card.querySelectorAll('svg path')].map(path => { const box = path.getBBox(); return { dashed: !!path.getAttribute('stroke-dasharray'), area: box.width * box.height }; })");
        double grey = paths.EnumerateArray().Where(path => !path.GetProperty("dashed").GetBoolean()).Select(path => path.GetProperty("area").GetDouble()).DefaultIfEmpty(0).Max();
        double orange = paths.EnumerateArray().Where(path => path.GetProperty("dashed").GetBoolean()).Select(path => path.GetProperty("area").GetDouble()).DefaultIfEmpty(0).Max();
        step.Check(orange > grey, $"The dashed GDB outline's box is larger than the grey one's (drawing areas {orange:0.#} and {grey:0.#}).");
        JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path = reshape.Path, pick = false });
        string[] problems = Problems(drift);
        step.Check(!problems.Any(problem => problem.Contains(reshape.UniqueId, StringComparison.Ordinal) || problem.Contains("shape edit", StringComparison.OrdinalIgnoreCase)),
            $"Problems list nothing about shapes ({string.Join(" | ", problems)}).");
        await Shot(step, "C3a-1-shape-card");
    }

    private async Task ShowShape(StepResult step)
    {
        if (!Ready(step))
        {
            return;
        }

        int seq = host.LastEvent();
        await Shell.Button("Show in Revit").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Button("Hide in Revit"), 30000), "The button turns into 'Hide in Revit'.");
        JsonObject state = host.Call("state");
        string view = state["viewName"]?.GetValue<string>() ?? string.Empty;
        string? level = FloorScan().FirstOrDefault(floor => floor.UniqueId == reshape!.UniqueId)?.Level;
        string? viewLevel = Views().FirstOrDefault(plan => !plan.IsTemplate && plan.Name == view)?.Level;
        step.Check(view.StartsWith("GIS_", StringComparison.OrdinalIgnoreCase) && viewLevel == level, $"The active view is the GIS_ plan of F's level {level} ({view}, level {viewLevel}).");
        step.Note($"Zoom corners: {state["zoom"]?.ToJsonString()}; floor F box {Box(reshapeBefore!)}.");
        NoCommit(step, seq);
        RevitShot(step, "C3a-2-outlines-shown");
        step.Note("Eyes: the grey and orange outlines show over floor F.");
    }

    private async Task HideShape(StepResult step)
    {
        if (!Ready(step))
        {
            return;
        }

        int seq = host.LastEvent();
        await Shell.Button("Hide in Revit").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Button("Show in Revit"), 30000), "The button turns back into 'Show in Revit'.");
        NoCommit(step, seq);
        RevitShot(step, "C3a-3-outlines-hidden");
        step.Note("Eyes: the outlines are gone.");
    }

    private async Task ApplyReshape(StepResult step)
    {
        if (!Ready(step))
        {
            return;
        }

        DumpFloorGeometry(step, "beforeApply");
        int seq = host.LastEvent();
        string[] log = await ApplyShapes(step, 1);
        step.Note($"Log: {string.Join(" / ", log)}");
        string name = ReshapeTransaction(reshapeBefore!.Id!.Value, reshape!.Label);
        ExpectEvent(step, seq, "commit", name);
        List<RevitEvent> commits = host.EventsAfter(seq).Where(item => item.Kind == "commit").ToList();
        step.Note($"Commit events: {string.Join(" / ", commits)}.");
        step.Check(commits.Count(item => item.Names.Any(each => each.StartsWith("Reshape floor ", StringComparison.Ordinal))) >= 1 && commits.All(item => item.Names.All(each => each == name)),
            $"Every commit is named '{name}' (one undo step is proven in C3a.6).");
        ElementRow after = Element(reshape.UniqueId);
        reshapeApplied = after.Exists && host.EventsAfter(seq).Any(item => item.Is("commit", name));
        step.Check(after.Exists && after.Id == reshapeBefore.Id, $"Floor F keeps its UniqueId and element id ({after.Id}).");
        step.Check(after.TypeId == reshapeBefore.TypeId, $"Floor F keeps its type ({after.TypeName}).");
        step.Check(!SameBox(after, reshapeBefore), $"Floor F's box changed: {Box(reshapeBefore)} to {Box(after)}.");
        step.Note($"Box change in model feet: min {Delta(reshapeBefore.Min, after.Min)}, max {Delta(reshapeBefore.Max, after.Max)}. The export grew {GrowM} m east in the project CRS; C3a.7 checks the outline to 1 cm.");
        await Shot(step, "C3a-4-applied");
    }

    private async Task ReshapeDone(StepResult step)
    {
        if (!reshapeApplied)
        {
            step.NotRun("C3a.4 did not apply.");
            return;
        }

        ILocator shapes = Shell.Page.GetByTestId("shape-edits");
        step.Check(await Gone(shapes), "After applying, the page reads the file again and 'Shape edits' is gone.");
        await Shell.Button("Check again").Last.ClickAsync();
        await Shell.Page.WaitForTimeoutAsync(2000);
        step.Check(await Gone(shapes), "A second Check again offers nothing.");
    }

    private Task UndoReshape(StepResult step)
    {
        if (!reshapeApplied)
        {
            step.NotRun("C3a.4 did not apply.");
            return Task.CompletedTask;
        }

        if (!UndoOrRedo(step, "undo", ReshapeTransaction(reshapeBefore!.Id!.Value, reshape!.Label)))
        {
            reshapeUndone = false;
            return Task.CompletedTask;
        }

        ElementRow after = Element(reshape.UniqueId);
        step.Check(SameBox(after, reshapeBefore), $"One Undo puts floor F's box back ({Box(after)}, was {Box(reshapeBefore)}).");
        return Task.CompletedTask;
    }

    private async Task RoundTrip(StepResult step)
    {
        if (!reshapeApplied)
        {
            step.NotRun("C3a.4 did not apply.");
            return;
        }

        if (reshapeUndone)
        {
            UndoOrRedo(step, "redo", ReshapeTransaction(reshapeBefore!.Id!.Value, reshape!.Label));
        }

        string output = await OpenExport(step, string.Empty, "export2", keepDefaultViews: true);
        bool exported = await ExportNow(step);
        await Shell.GoToAsync("/gis-standard");
        if (!exported)
        {
            return;
        }

        FloorUnit? unit = ReshapeFixture.Pick(ReshapeFixture.FloorUnits(output), string.Empty, reshape!.UniqueId);
        if (!step.Check(unit != null, $"The second export has floor F ({reshape.UniqueId}) as one polygon."))
        {
            return;
        }

        double distance = ReshapeFixture.Distance(reshape.Outline, unit!.Rings);
        step.Check(distance <= 0.01, $"F's exported outline is the GDB outline within 1 cm (largest gap {distance * 1000:0.0} mm).");
    }

    private async Task SlopeArrowFloor(StepResult step)
    {
        FloorScanRow? floor = FloorScan().FirstOrDefault(row => row.ExtraSketchLines && !row.HasArc && !row.ShapeEdited && Unit(row.UniqueId) != null);
        if (floor == null)
        {
            step.NotRun("No exported floor has a slope arrow or other extra sketch line.");
            return;
        }

        await ExpectProblem(step, floor, "slope.gis-drift.json", SlopeArrow);
    }

    private async Task ShrinkPastHosted(StepResult step)
    {
        if (exportedUnits.Count == 0)
        {
            step.NotRun("C3a.P made no export.");
            return;
        }

        FloorScanRow[] scan = FloorScan();
        FloorScanRow? floor = scan.FirstOrDefault(row => Redrawable(row) && row.Dependents.Any(NearEast));
        if (floor == null)
        {
            // LUMINE has one large floor per zone type per level, so its retail floors are in several parts
            // and none is a single-polygon unit; a walkway floor takes the opening then.
            FloorScanRow? target = scan.Where(Redrawable).FirstOrDefault(row => Unit(row.UniqueId)?.Category == "retail")
                ?? scan.Where(Redrawable).FirstOrDefault(row => Unit(row.UniqueId)?.Category == "walkway");
            if (target == null)
            {
                step.NotRun("No retail or walkway floor whose sketch can be redrawn is a single-polygon unit in the export (multi-part floors are refused), so no floor can take the fixture opening.");
                return;
            }

            step.Note($"The fixture opening goes on {Unit(target.UniqueId)!.Category} floor {target.Id}.");

            JsonObject fixture;
            try
            {
                fixture = host.Call("fixture", new { openingInEastBand = new[] { new { floor = target!.Id, bandM = 1.0, sizeM = 0.4 } } });
            }
            catch (TestHostException ex)
            {
                step.NotRun($"No floor has an opening or hosted family within 1 m of its east edge, and the opening fixture failed on floor {target!.Id}: {ex.Message}");
                return;
            }

            step.Note($"No floor has an opening or hosted family within 1 m of its east edge, so the fixture cut one: {fixture["log"]?.ToJsonString()}.");
            floor = FloorScan().First(row => row.Id == target.Id);
        }

        FloorScanRow h = floor;
        DependentRow[] dependents = h.Dependents;
        step.Note($"H = floor {h.Id} ({h.UniqueId}, level {h.Level}). Dependents: {string.Join("; ", dependents.Select(item => $"{item.Id} {item.Name} ({item.Category}) {Metres(item.WestOfEdgeM)} west of the east edge"))}.");
        FloorUnit unit = Unit(h.UniqueId)!;
        ShrunkOutline shrunk = ReshapeFixture.ShrinkEast(unit, ShrinkBandM);
        step.Note($"Shrink: the export polygon minus a {ShrinkBandM} m strip along its east side; the outline crosses the cut {shrunk.Crossings} time(s), kept {shrunk.Rings.Count - 1} hole(s), dropped {shrunk.DroppedHoles} hole(s) the strip reaches.");
        string path = WriteDrift("shrink.gis-drift.json", unit, ReshapeFixture.Reshaped(unit, shrunk.Rings));
        DumpFloorGeometry(step, "H", h.UniqueId, path, "beforeApply");
        await PickDrift(path);
        if (!await ExpectShapeEdits(step, path))
        {
            return;
        }

        int seq = host.LastEvent();
        string[] log = await ApplyShapes(step, 1);
        string name = ReshapeTransaction(h.Id, unit.Name);
        ExpectEvent(step, seq, "commit", name);
        step.Note($"Log: {string.Join(" / ", log)}");
        Dictionary<long, string> deleted = log
            .Select(line => Regex.Match(line, @"^Revit deleted (.+) (\d+), which hung on the floor outside its new outline\.$"))
            .Where(match => match.Success)
            .ToDictionary(match => long.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture), match => match.Groups[1].Value);
        string[] warned = log.Where(line => line.StartsWith("Revit warned: ", StringComparison.Ordinal)).ToArray();
        Dictionary<long, bool> exists = Elements(dependents.Select(item => item.Id)).ToDictionary(row => long.Parse(row.Key, CultureInfo.InvariantCulture), row => row.Exists);
        step.Check(deleted.Count > 0 || warned.Length > 0, $"The result lists what Revit deleted or warned about ({deleted.Count} deleted, {warned.Length} warning(s)).");
        step.Check(deleted.Keys.All(id => exists.TryGetValue(id, out bool alive) ? !alive : Element(id).Exists == false), $"Every listed id is gone ({string.Join(", ", deleted.Keys)}).");
        string[] wronglyListed = dependents.Where(item => exists[item.Id] && deleted.ContainsKey(item.Id)).Select(item => item.Id.ToString(CultureInfo.InvariantCulture)).ToArray();
        step.Check(wronglyListed.Length == 0, $"No dependent that still exists is listed as deleted ({string.Join(", ", wronglyListed)}).");
        foreach (DependentRow item in dependents)
        {
            string fate = !exists[item.Id] ? (deleted.ContainsKey(item.Id) ? "deleted, and listed" : "deleted, NOT listed") : "kept";
            step.Note($"Revit did: {item.Name} {item.Id} ({Metres(item.WestOfEdgeM)} west of the old east edge, a {ShrinkBandM} m band removed): {fate}.");
        }

        foreach (string warning in warned)
        {
            step.Note(warning);
        }

        await Shot(step, "C3a-9-shrink-applied");
        RevitShot(step, "C3a-9-shrink");
        if (!UndoOrRedo(step, "undo", name))
        {
            return;
        }

        string[] missing = Elements(dependents.Select(item => item.Id)).Where(row => !row.Exists).Select(row => row.Key).ToArray();
        step.Check(missing.Length == 0, $"Undo brings every dependent back (missing: {string.Join(", ", missing)}).");
    }

    private async Task CurvedFloor(StepResult step)
    {
        FloorScanRow? floor = FloorScan().FirstOrDefault(row => row.HasArc && !row.ShapeEdited && Unit(row.UniqueId) != null);
        if (floor == null)
        {
            step.NotRun("No exported floor has an arc in its sketch.");
            return;
        }

        ElementRow before = Element(floor.UniqueId);
        await ExpectProblem(step, floor, "curved.gis-drift.json", CurvedEdges);
        step.Check(SameBox(Element(floor.UniqueId), before), "Nothing changes: the floor's box is the same.");
    }

    private async Task ExpectProblem(StepResult step, FloorScanRow floor, string fileName, string reason)
    {
        FloorUnit unit = Unit(floor.UniqueId)!;
        string path = WriteDrift(fileName, unit, ReshapeFixture.Reshaped(unit, ReshapeFixture.Grow(unit, GrowM)));
        step.Note($"Floor {floor.Id} ({floor.UniqueId}): {path}.");
        await PickDrift(path);
        JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path, pick = false });
        string[] problems = Problems(drift);
        step.Check(problems.Any(problem => problem.Contains($"floor {floor.Id} {reason}", StringComparison.Ordinal)), $"Problems say floor {floor.Id} '{reason}' ({string.Join(" | ", problems)}).");
        step.Check((drift?["shapes"]?.AsArray().Count ?? 0) == 0 && await Shell.Page.GetByTestId("shape-edit").CountAsync() == 0, "There is no shape edit card.");
        await Shot(step, Path.GetFileNameWithoutExtension(fileName).Replace(".gis-drift", string.Empty, StringComparison.Ordinal) + "-problem");
    }

    /// <summary>Exports with the default views to &lt;run&gt;\export and reads its floor units into <see cref="exportedUnits"/>.</summary>
    private async Task<bool> ExportFloorUnits(StepResult step)
    {
        string output = await OpenExport(step, string.Empty, "export", keepDefaultViews: true);
        bool exported = await ExportNow(step);
        await Shell.GoToAsync("/gis-standard");
        if (exported)
        {
            exportedUnits = ReshapeFixture.FloorUnits(output);
            step.Note($"{exportedUnits.Count} single-polygon floor unit(s) in the export: {Format(exportedUnits.GroupBy(unit => unit.Category).ToDictionary(group => group.Key, group => group.Count()))}.");
        }

        return exported;
    }

    private async Task<string[]> ApplyShapes(StepResult step, int count)
    {
        int seq = host.LastEvent();
        string label = $"Apply {count} shape edit(s)";
        if (await Shell.Button(label).CountAsync() == 0)
        {
            // A model with no id yet (no station export) cannot be matched to the drift file, so the
            // page starts every shape edit unticked: tick them, as a person who knows the file would.
            ILocator unticked = Shell.Page.GetByTestId("shape-edits").GetByRole(AriaRole.Checkbox, new() { Checked = false });
            int ticked = 0;
            while (await unticked.CountAsync() > 0 && ticked < count)
            {
                await unticked.First.CheckAsync();
                ticked++;
            }

            step.Note($"The shape edits started unticked (the page: the model has no id yet, so the drift file cannot be matched to it); ticked {ticked}.");
        }

        await Shell.Button(label).ClickAsync();
        ILocator confirm = Shell.Page.GetByTestId("drift-confirm");
        if (await Shell.ShowsAsync(confirm, 3000))
        {
            step.Note($"Confirm dialog: {(await Shell.Page.GetByRole(AriaRole.Dialog).InnerTextAsync()).Replace('\n', ' ')}");
            await Shell.Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { Name = label, Exact = true }).ClickAsync();
        }

        // C3a's build says "floor(s) reshaped"; from C3b on, every shape edit says "shape edit(s) applied".
        Regex banner = new($@"^{count} (floor\(s\) reshaped|shape edit\(s\) applied)");
        bool applied = await Shell.ShowsAsync(Shell.Text(banner), 120000);
        if (!applied)
        {
            // A Revit error or warning dialog (such as "Error - cannot be ignored … Quit sketching")
            // holds the add-in until someone answers it. Record its text and cancel it, as a person
            // would, so the page can finish and show its own result and log.
            JsonObject closed = host.Call("closeDialogs", new { timeoutMs = 20000 });
            if (closed["closed"]?.AsArray() is { Count: > 0 } dialogs)
            {
                step.Check(false, $"Revit showed {dialogs.Count} dialog(s) during Apply; cancelled: {dialogs.ToJsonString()}");
                applied = await Shell.ShowsAsync(Shell.Text(banner), 60000);
            }
        }

        await RecordApplyResult(step, seq);
        step.Check(applied, $"The banner reads '{count} floor(s) reshaped' or '{count} shape edit(s) applied'.");
        return await ReadAppliedLog();
    }

    /// <summary>
    /// Word for word, so a refusal or a skipped edit reaches the report: the panel scrolled to the top,
    /// its banners and every line of their lists, Revit's dialogs, and every Revit event since the
    /// click as the test host recorded it (kind, names, added/modified/deleted).
    /// </summary>
    private async Task RecordApplyResult(StepResult step, int seq)
    {
        await Shell.Page.EvaluateAsync("() => document.querySelectorAll('*').forEach(element => { if (element.scrollTop > 0) element.scrollTop = 0; })");
        await Shell.Page.WaitForTimeoutAsync(500);
        step.Note($"Page messages: {await Shell.AlertTextAsync()}");
        foreach (string line in (await Shell.Page.Locator("[role=status] li, [role=alert] li, details li").AllInnerTextsAsync()).Select(text => text.Trim()).Where(text => text.Length > 0))
        {
            step.Note($"Panel line: {line}");
        }

        string dialogs = string.Join(" | ", host.EventsAfter(seq).Where(item => item.Kind == "dialog").Select(item => $"{item.DialogId}: {item.Message}"));
        step.Note($"Revit dialogs: {(dialogs.Length > 0 ? dialogs : "none")}.");
        step.Note($"Revit events since Apply: {string.Join(" ", host.RawEventsAfter(seq))}");
        await Shot(step, $"{step.Id}-apply-result-top");
    }

    /// <summary>Checks that the page shows one shape edit card; when it does not, the problems list goes into the report.</summary>
    private async Task<bool> ExpectShapeEdits(StepResult step, string path)
    {
        if (step.Check(await Shell.ShowsAsync(Shell.Text("Shape edits (1)"), 60000), $"The page shows 'Shape edits (1)' for {Path.GetFileName(path)}."))
        {
            return true;
        }

        JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path, pick = false });
        string[] problems = Problems(drift);
        step.Note($"No card. Problems ({problems.Length}): {(problems.Length == 0 ? "none" : string.Join(" | ", problems))}.");
        return false;
    }

    private async Task PickDrift(string path)
    {
        await Shell.Page.GetByRole(AriaRole.Radio, new() { Name = "GDB edits", Exact = true }).ClickAsync();
        await Shell.Button("Pick a drift file…").ClickAsync();
        host.Call("fileDialog", new { path, timeoutMs = 30000 });
        await Shell.Page.WaitForTimeoutAsync(1000);
    }

    private string WriteDrift(string fileName, FloorUnit unit, IEnumerable<JsonObject> shapes)
    {
        string path = Path.Combine(options.OutputFolder, fileName);
        File.WriteAllText(path, ReshapeFixture.DriftJson(unit, shapes));
        return path;
    }

    private bool Ready(StepResult step)
    {
        if (reshape != null && reshapeBefore is { Exists: true })
        {
            return true;
        }

        step.NotRun("C3a.P found no floor F.");
        return false;
    }

    private void NoCommit(StepResult step, int after)
    {
        List<RevitEvent> commits = host.EventsAfter(after).Where(item => item.Kind == "commit").ToList();
        step.Check(commits.Count == 0, $"No transaction committed ({string.Join(" / ", commits)}).");
    }

    private async Task<bool> Gone(ILocator locator)
    {
        DateTime end = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (await locator.CountAsync() > 0 && DateTime.UtcNow < end)
        {
            await Shell.Page.WaitForTimeoutAsync(300);
        }

        return await locator.CountAsync() == 0;
    }

    private FloorUnit? Unit(string uniqueId) => ReshapeFixture.Pick(exportedUnits, string.Empty, uniqueId);

    private bool Redrawable(FloorScanRow row) => !row.HasArc && !row.ExtraSketchLines && !row.ShapeEdited && Unit(row.UniqueId) != null;

    private static bool NearEast(DependentRow item) => item.WestOfEdgeM is >= 0 and <= 1;

    private static string ReshapeTransaction(long id, string label) =>
        $"Reshape floor {id}{(label.Length > 0 ? $" ({label})" : string.Empty)} to the GDB outline";

    private static string[] Problems(JsonNode? drift) =>
        drift?["problems"]?.AsArray().Select(problem => problem?.GetValue<string>() ?? string.Empty).ToArray() ?? Array.Empty<string>();

    private ElementRow Element(string uniqueId) =>
        host.Call("elements", new { uniqueIds = new[] { uniqueId } })["elements"].Deserialize<ElementRow[]>(Web)!.Single();

    private ElementRow Element(long id) => Elements(new[] { id }).Single();

    private ElementRow[] Elements(IEnumerable<long> ids) =>
        host.Call("elements", new { ids = ids.ToArray() })["elements"].Deserialize<ElementRow[]>(Web) ?? Array.Empty<ElementRow>();

    private FloorScanRow[] FloorScan() => host.Call("floorScan")["floors"].Deserialize<FloorScanRow[]>(Web) ?? Array.Empty<FloorScanRow>();

    private static bool SameBox(ElementRow a, ElementRow b) =>
        a.Min != null && b.Min != null && a.Max != null && b.Max != null &&
        a.Min.Zip(b.Min).Concat(a.Max.Zip(b.Max)).All(pair => Math.Abs(pair.First - pair.Second) < 1e-4);

    private static string Box(ElementRow row) =>
        row.Min == null || row.Max == null ? "none" : $"({string.Join(", ", row.Min.Select(Feet))}) to ({string.Join(", ", row.Max.Select(Feet))}) ft";

    private static string Delta(double[]? before, double[]? after) =>
        before == null || after == null ? "none" : "(" + string.Join(", ", before.Zip(after).Select(pair => Feet(pair.Second - pair.First))) + ")";

    private static string Feet(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);

    private static string Metres(double? value) => value is double metres ? metres.ToString("0.00", CultureInfo.InvariantCulture) + " m" : "? m";
}

/// <summary>The one shape a C3a drift file reshapes: which floor, its label and the GDB outline (export CRS, metres).</summary>
internal sealed record ReshapeShape(string Path, string UniqueId, string Label, IReadOnlyList<IReadOnlyList<(double X, double Y)>> Outline)
{
    public static ReshapeShape Read(string path)
    {
        JsonNode shape = JsonNode.Parse(File.ReadAllText(path))?["shapes"]?.AsArray().FirstOrDefault()
            ?? throw new InvalidDataException($"{path} has no shape edit.");
        var rings = shape["outline"]!["coordinates"]!.AsArray()
            .Select(ring => (IReadOnlyList<(double X, double Y)>)ring!.AsArray().Select(point => (point![0]!.GetValue<double>(), point[1]!.GetValue<double>())).ToList())
            .ToList();
        return new ReshapeShape(path, shape["uniqueId"]!.GetValue<string>(), shape["label"]?.GetValue<string>() ?? string.Empty, rings);
    }
}

internal sealed record ElementRow(string Key, bool Exists, long? Id, string? UniqueId, string? Category, long? TypeId, string? TypeName, long? LevelId, double? HeightOffsetFt, double? AreaM2, double[]? Min, double[]? Max);

internal sealed record FloorScanRow(long Id, string UniqueId, string? Level, bool HasArc, bool ExtraSketchLines, bool ShapeEdited, DependentRow[] Dependents);

internal sealed record DependentRow(long Id, string? Category, string? Name, double? WestOfEdgeM);
