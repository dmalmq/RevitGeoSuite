using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// B9: GIS writes on a grouped floor. A fixture groups one floor and places a second instance of the
/// group type; the export, Set up model and a GDB edit on one copy must then finish with no Revit dialog
/// and no group violation in the journal, and the two copies must keep their own <c>id</c>. Step ids B9.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private static readonly double[] GroupOffsetsM = { 5, 10, 20, 40, 80 };
    private static readonly string[] AtomMarkers = { "Disallowed Atom Touch", "AtomViolation" };

    private JournalMark? groupsStarted;
    private long[] groupFloors = Array.Empty<long>();
    private string[] groupUniqueIds = Array.Empty<string>();
    private string[] groupIds = Array.Empty<string>();
    private string groupIdBefore = string.Empty;

    private async Task GroupSteps()
    {
        groupsStarted = JournalScan.Mark(JournalScan.DefaultFolder(options.RevitYear));
        await Step("B9.0", "The model has had Set up model once, so each level has a GIS_ plan", EnsureSetUp);
        await Step("B9.G", "The copy gets RevitGeoSuite's georeference record from its own coordinates, which the Cesium export needs", EnsureGeoreference);
        await Step("B9.S", "A Cesium export into a station folder gives the copy a model id, which a drift file must name", EnsureStation);
        await Step("B9.P", "Export, pick a floor that is one unit and hosts nothing", PrepareGroups);
        await Step("B9.1", "Fixture: group the floor and place a second instance of its group type", step => WithoutDialogs(step, GroupFloor));
        await Step("B9.2", "Export GIS: two unit rows with two different ids, and id varies by group instance", step => WithoutDialogs(step, () => ExportGroups(step, "b9-export-1", keepFirst: false, shared: groupIdBefore)));
        await Step("B9.3", "Set up model finishes", step => WithoutDialogs(step, () => SetUpGroups(step)));
        await Step("B9.4", "A GDB edit of the name on copy 2 changes copy 2 only", step => WithoutDialogs(step, () => EditOneCopy(step)));
        await Step("B9.5", "Clear id on copy 2 and export: copy 2 gets a new id, copy 1 keeps its id", step => WithoutDialogs(step, () => ReassignOneId(step)));
        await Step("B9.6", "Give copy 2 copy 1's id and export: copy 1 keeps it, copy 2 gets a new one (the grouped write path)", step => WithoutDialogs(step, () => ShareThenExport(step)));
    }

    private async Task PrepareGroups(StepResult step)
    {
        if (!await ExportFloorUnits(step))
        {
            return;
        }

        FloorScanRow[] scan = FloorScan();
        string[] preferred = { "retail", "walkway" };
        FloorUnit? unit = exportedUnits
            .Where(unit => exportedParts.GetValueOrDefault(unit.UniqueId) == 1 && unit.Category.Length > 0
                && scan.FirstOrDefault(row => row.UniqueId == unit.UniqueId) is { Dependents.Length: 0 })
            .OrderBy(unit => Array.IndexOf(preferred, unit.Category) is int index and >= 0 ? index : preferred.Length)
            .FirstOrDefault();
        if (!step.Check(unit != null, "The export has a floor that is one unit with a category and hosts nothing."))
        {
            return;
        }

        ElementRow floor = Element(unit!.UniqueId);
        groupFloors = new[] { floor.Id!.Value };
        groupUniqueIds = new[] { unit.UniqueId };
        step.Note($"F = {floor.Id} '{unit.Name}' ({unit.Category}), id '{ParamText(floor.Id!.Value, "id")}', type {floor.TypeName}.");
        step.Note($"id definition before grouping: {host.Call("parameterDefinitions", new { names = new[] { "id" } })["definitions"]?.ToJsonString()}.");
    }

    private Task GroupFloor(StepResult step)
    {
        if (groupFloors.Length != 1)
        {
            step.NotRun("B9.P picked no floor.");
            return Task.CompletedTask;
        }

        // The copy goes along the model's X axis by the first offset whose box clears every other floor on
        // F's level, so Revit has no overlap or "identical instances" warning to raise; the fixture logs
        // any warning it removed.
        ElementRow f = Element(groupFloors[0]);
        ElementRow[] others = Elements(FloorScan().Select(row => row.Id).Where(id => id != f.Id)).Where(row => row.Exists && row.LevelId == f.LevelId && row.Min != null && row.Max != null).ToArray();
        double offsetM = GroupOffsetsM.FirstOrDefault(offset => !others.Any(other => Overlaps(f, other, offset / 0.3048)), double.NaN);
        if (double.IsNaN(offsetM))
        {
            offsetM = GroupOffsetsM[^1];
            step.Note($"Every offset up to {offsetM} m overlaps another floor on F's level; the copy goes {offsetM} m along X anyway.");
        }
        else
        {
            step.Note($"The copy goes {offsetM} m along the model's X axis, where its box overlaps none of the {others.Length} other floor(s) on F's level.");
        }

        JsonObject fixture = host.Call("fixture", new { groupCopy = new { floor = groupFloors[0], offsetM } });
        step.Note($"Fixture: {fixture["log"]?.ToJsonString()}.");
        long[] floors = fixture["group"]?["floors"].Deserialize<long[]>() ?? Array.Empty<long>();
        if (!step.Check(floors.Length == 2 && floors.All(id => Element(id).Exists), $"Two floors, one per group instance ({string.Join(", ", floors)})."))
        {
            return Task.CompletedTask;
        }

        groupFloors = floors;
        groupUniqueIds = floors.Select(id => Element(id).UniqueId!).ToArray();
        groupIdBefore = ParamText(floors[0], "id");
        step.Note($"Copy 1 = {floors[0]} ({groupUniqueIds[0]}), copy 2 = {floors[1]} ({groupUniqueIds[1]}); id '{ParamText(floors[0], "id")}' and '{ParamText(floors[1], "id")}' before the export.");
        return Task.CompletedTask;
    }

    private async Task ExportGroups(StepResult step, string folder, bool keepFirst, string shared)
    {
        if (groupFloors.Length != 2)
        {
            step.NotRun("B9.1 made no second copy.");
            return;
        }

        string output = await OpenExport(step, string.Empty, folder, keepDefaultViews: true);
        bool exported = await ExportNow(step);
        string[] warnings = exported ? await PageLines() : Array.Empty<string>();
        await Shell.GoToAsync("/gis-standard");
        if (!exported)
        {
            return;
        }

        List<(string UniqueId, string Id)> rows = ReshapeFixture.UnitIds(output);
        string[][] perCopy = groupUniqueIds.Select(uid => rows.Where(row => row.UniqueId == uid).Select(row => row.Id).ToArray()).ToArray();
        step.Note($"Unit rows: copy 1 {perCopy[0].Length} with id {string.Join(", ", perCopy[0].Select(id => $"'{id}'"))}; copy 2 {perCopy[1].Length} with id {string.Join(", ", perCopy[1].Select(id => $"'{id}'"))}.");
        step.Check(perCopy.All(ids => ids.Length == 1), "Each copy has one unit row.");
        string[] revit = groupFloors.Select(id => ParamText(id, "id")).ToArray();
        step.Check(revit.All(id => id.Length > 0) && revit[0] != revit[1], $"The copies hold two different non-empty ids in Revit ('{revit[0]}', '{revit[1]}').");
        if (perCopy.All(ids => ids.Length == 1))
        {
            step.Check(perCopy[0][0] == revit[0] && perCopy[1][0] == revit[1], "The unit rows carry the ids Revit holds.");
        }

        IdDefinitionsVary(step);
        if (keepFirst)
        {
            step.Check(revit[0] == groupIds[0], $"Copy 1 keeps its id '{groupIds[0]}' (now '{revit[0]}').");
            step.Check(revit[1].Length > 0 && revit[1] != groupIds[0], $"Copy 2 gets a new id ('{revit[1]}').");
        }
        else if (shared.Length > 0)
        {
            // Among elements that share an id, the export leaves it on the lowest element id: copy 1, the original.
            step.Check(revit[0] == shared, $"Copy 1 ({groupFloors[0]}, the lower element id) keeps '{shared}' (now '{revit[0]}').");
            step.Check(Guid.TryParse(revit[1], out _) && revit[1] != shared, $"Copy 2 gets a new GUID ('{revit[1]}').");
            string expected = $"Element {groupFloors[0]} keeps id {shared} as the oldest; element copied from it got a new id: {groupFloors[1]}.";
            string? kept = warnings.FirstOrDefault(line => line.Contains(expected, StringComparison.Ordinal));
            step.Check(kept != null, $"The export warnings have '{expected}'.");
            if (kept != null)
            {
                step.Note($"Export warning: {kept}");
            }

            // The id definition varies on this copy, so no copy may stay on the shared id and the original keeps it.
            string[] wrong = warnings.Where(line => line.Contains(" still share", StringComparison.Ordinal) || line.Contains("no longer holds it", StringComparison.Ordinal)).ToArray();
            step.Check(wrong.Length == 0, $"No 'still share(s) id' or 'no longer holds it' warning ({string.Join(" | ", wrong)}).");
        }

        groupIds = revit;
    }

    private async Task SetUpGroups(StepResult step)
    {
        InspectReply model = await Inspect();
        if (model.SetupLines.Length == 0)
        {
            step.Note("Set up model has nothing to do (no Apply button); the export's own parameter step ran in B9.2.");
            return;
        }

        string[] log = await ApplySetupAndReadLog(step);
        step.Note($"Set up model: {string.Join(" / ", log)}");
    }

    private async Task EditOneCopy(StepResult step)
    {
        if (groupFloors.Length != 2 || groupIds.Length != 2)
        {
            step.NotRun("B9.2 did not export the two copies.");
            return;
        }

        Dictionary<long, string?> before = ReadNames(groupFloors);
        string current = before[groupFloors[1]] ?? string.Empty;
        string renamed = current.Length > 0 ? $"{current} (B9)" : "B9 copy 2"; // the product trims names, so no leading space
        string path = Path.Combine(options.OutputFolder, "b9-name.gis-drift.json");
        File.WriteAllText(path, DriftFixture.Write(stationModelKey, groupUniqueIds[1], current, renamed));
        step.Note($"Wrote {path}: copy 2 ({groupFloors[1]}) name '{current}' to '{renamed}' (model '{stationModelKey}').");
        await PickDrift(path);
        ILocator boxes = Shell.Page.GetByRole(AriaRole.Table).GetByRole(AriaRole.Checkbox);
        if (!step.Check(await Shell.ShowsAsync(boxes, 60000) && await boxes.CountAsync() == 1, $"The drift file shows one row ({await boxes.CountAsync()})."))
        {
            return;
        }

        await boxes.First.SetCheckedAsync(true);
        int seq = host.LastEvent();
        await Shell.Button(new Regex(@"^Accept 1 (value )?change\(s\)")).Last.ClickAsync();
        if (await Shell.ShowsAsync(Shell.Page.GetByTestId("drift-confirm"), 3000))
        {
            await Shell.Page.GetByRole(AriaRole.Dialog).GetByRole(AriaRole.Button, new() { NameRegex = new Regex(@"^Accept 1 (value )?change\(s\)") }).ClickAsync();
        }

        step.Check(await Shell.ShowsAsync(Shell.Text("1 GDB value(s) accepted"), 120000), "Banner '1 GDB value(s) accepted'.");
        ExpectEvent(step, seq, "commit", DriftTransaction);
        Dictionary<long, string?> after = ReadNames(groupFloors);
        step.Check(after[groupFloors[1]] == renamed, $"Copy 2's name is '{renamed}' (is '{after[groupFloors[1]]}').");
        step.Check(after[groupFloors[0]] == before[groupFloors[0]], $"Copy 1's name stays '{before[groupFloors[0]]}' (is '{after[groupFloors[0]]}').");
        await Shot(step, "B9-4-one-copy");
    }

    private async Task ReassignOneId(StepResult step)
    {
        if (groupFloors.Length != 2 || groupIds.Length != 2)
        {
            step.NotRun("B9.2 did not export the two copies.");
            return;
        }

        JsonObject fixture = host.Call("fixture", new { values = new[] { new { id = groupFloors[1], name = "id", value = string.Empty } } });
        step.Note($"Fixture: {fixture["log"]?.ToJsonString()}; copy 1 id '{ParamText(groupFloors[0], "id")}', copy 2 id '{ParamText(groupFloors[1], "id")}'.");
        step.Check(ParamText(groupFloors[0], "id") == groupIds[0], "Clearing copy 2's id leaves copy 1's.");
        await ExportGroups(step, "b9-export-2", keepFirst: true, shared: string.Empty);
    }

    private async Task ShareThenExport(StepResult step)
    {
        if (groupFloors.Length != 2 || groupIds.Length != 2)
        {
            step.NotRun("B9.5 did not export the two copies.");
            return;
        }

        string first = ParamText(groupFloors[0], "id");
        JsonObject fixture = host.Call("fixture", new { values = new[] { new { id = groupFloors[1], name = "id", value = first } } });
        step.Note($"Fixture: {fixture["log"]?.ToJsonString()}; copy 1 id '{ParamText(groupFloors[0], "id")}', copy 2 id '{ParamText(groupFloors[1], "id")}'.");
        if (!step.Check(ParamText(groupFloors[1], "id") == first && ParamText(groupFloors[0], "id") == first, $"Both copies now hold '{first}', and copy 1 still does."))
        {
            return;
        }

        await ExportGroups(step, "b9-export-3", keepFirst: false, shared: first);
    }

    /// <summary>Runs a step that changes the model, then checks Revit showed no dialog and logged no group violation.</summary>
    private async Task WithoutDialogs(StepResult step, Func<StepResult, Task> body) => await WithoutDialogs(step, () => body(step));

    private async Task WithoutDialogs(StepResult step, Func<Task> body)
    {
        int seq = host.LastEvent();
        int atoms = AtomLines().Count;
        await body();
        RevitEvent[] dialogs = host.EventsAfter(seq).Where(item => item.Kind == "dialog").ToArray();
        step.Check(dialogs.Length == 0, $"Revit raised no dialog ({string.Join(" / ", dialogs.Select(item => item.ToString()))}).");
        JsonArray open = host.Call("windows")["windows"]?.AsArray() ?? new JsonArray();
        step.Check(open.Count == 0, $"No Revit dialog is on screen ({open.ToJsonString()}).");
        List<string> found = AtomLines().Skip(atoms).ToList();
        step.Check(found.Count == 0, $"The journal has no group violation ({string.Join(" | ", found.Take(5))}).");
    }

    // Every instance-bound definition named id must vary by group instance (a model can bind an older one beside
    // the current one); a type-bound one cannot vary, and the product switches only instance-bound ones.
    private void IdDefinitionsVary(StepResult step)
    {
        JsonArray definitions = host.Call("parameterDefinitions", new { names = new[] { "id" } })["definitions"]?.AsArray() ?? new JsonArray();
        JsonNode?[] instance = definitions.Where(row => row?["binding"]?.GetValue<string>() == "instance").ToArray();
        step.Check(instance.Length > 0 && instance.All(row => row?["variesAcrossGroups"]?.GetValue<bool>() == true), $"Every instance-bound 'id' definition varies by group instance ({definitions.ToJsonString()}).");
    }

    private static bool Overlaps(ElementRow moved, ElementRow other, double dxFt) =>
        moved.Min![0] + dxFt < other.Max![0] && moved.Max![0] + dxFt > other.Min![0] && moved.Min[1] < other.Max[1] && moved.Max![1] > other.Min![1];

    private List<string> AtomLines() =>
        (groupsStarted ?? throw new InvalidOperationException("The B9 journal mark is taken when the group steps start.")).LinesAfter().Lines
            .Where(line => AtomMarkers.Any(marker => line.Contains(marker, StringComparison.Ordinal)))
            .ToList();

    private string ParamText(long id, string name) =>
        host.Call("params", new { ids = new[] { id }, names = new[] { name } })["elements"]?[id.ToString(System.Globalization.CultureInfo.InvariantCulture)]?[name]?.GetValue<string>() ?? string.Empty;
}
