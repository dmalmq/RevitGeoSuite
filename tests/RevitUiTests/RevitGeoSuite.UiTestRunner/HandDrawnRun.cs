using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The C3d check (no check list file yet; from the port thread's description): Review GDB edits creates
/// a floor for a unit drawn by hand in the GDB. The scenario exports, deletes a floor F through the test
/// host and offers F's unit back as drawn by hand, so the new floor can be compared with F. Step ids C3d.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const string NoSuchLevel = "uitest-no-such-level";

    private HandDrawnCase? drawn;
    private long? drawnFloor;
    private bool drawnCard;
    private bool drawnUndone;

    private async Task HandDrawnSteps()
    {
        await Step("C3d.0", "The model has had Set up model once, so each level has a GIS_ plan", EnsureSetUp);
        await Step("C3d.G", "The copy gets RevitGeoSuite's georeference record from its own coordinates, which the Cesium export needs", EnsureGeoreference);
        await Step("C3d.S", "A Cesium export into a station folder gives the copy a model id, which a drift file must name", EnsureStation);
        await Step("C3d.P", "Export, delete floor F, make the hand-drawn drift files from F's unit", PrepareHandDrawn);
        await Step("C3d.1", "Pick the typed file: one 'New floor drawn in the GDB' card with its type chosen", PickHandDrawn);
        await Step("C3d.2", "Show in Revit draws the outline in the GIS_ plan, with no transaction", step => ShowShapes(step, drawnCard ? drawn?.Floor.Level : null));
        await Step("C3d.3", "Apply 1 shape edit: one new floor", ApplyHandDrawn);
        await Step("C3d.4", "The new floor: F's level, the unit's area, the type the log names", HandDrawnResult);
        await Step("C3d.5", "Check again offers nothing", step => NothingLeft(step, drawnFloor != null));
        await Step("C3d.6", "One Undo removes the new floor", UndoHandDrawn);
        await Step("C3d.7", "Redo, export again: the new floor's unit is the drawn outline within 1 cm", HandDrawnRoundTrip);
        await Step("C3d.8", "Without a category the card asks for a type, and Apply waits for it", UntypedHandDrawn);
        await Step("C3d.9", "A unit on a level_id no level has is a problem, not a card", HandDrawnNoLevel);
        await Step("C3d.10", "An outline that crosses itself is listed as a problem, not drawn", HandDrawnCrossing);
    }

    private async Task PrepareHandDrawn(StepResult step)
    {
        if (!await ExportFloorUnits(step))
        {
            return;
        }

        // F must be one part in the export (its unit is then the whole floor) and host nothing, so deleting it
        // takes nothing else with it. Retail and walkway first, any category after.
        FloorScanRow[] scan = FloorScan();
        string[] preferred = { "retail", "walkway" };
        var candidates = exportedUnits
            .Where(unit => exportedParts.GetValueOrDefault(unit.UniqueId) == 1 && unit.Rings.Count == 1 && ReshapeFixture.IsSimple(unit.Rings[0]) && unit.Category.Length > 0
                && scan.FirstOrDefault(row => row.UniqueId == unit.UniqueId) is { Dependents.Length: 0 })
            .OrderBy(unit => Array.IndexOf(preferred, unit.Category) is int index and >= 0 ? index : preferred.Length)
            .ToList();
        if (!step.Check(candidates.Count > 0, "The export has a floor that is one part without holes, has a category and hosts nothing."))
        {
            return;
        }

        FloorUnit unit = candidates[0];
        FloorScanRow floor = scan.First(row => row.UniqueId == unit.UniqueId);
        ElementRow before = Element(unit.UniqueId);
        JsonObject fixture = host.Call("fixture", new { delete = new[] { before.Id!.Value } });
        step.Note($"F = {before.Id} '{unit.Name}' ({unit.Category}/{unit.Restriction}), type {before.TypeName}, level {floor.Level}, area {before.AreaM2:0.0} m². Fixture: {fixture["log"]?.ToJsonString()}.");
        if (!step.Check(!Element(unit.UniqueId).Exists, $"Floor F ({before.Id}) is deleted, so its unit counts as not in the model."))
        {
            return;
        }

        drawn = new HandDrawnCase(
            WriteDrift("hand-drawn.gis-drift.json", unit, ReshapeFixture.HandDrawn(unit)),
            WriteDrift("hand-drawn-untyped.gis-drift.json", unit, ReshapeFixture.HandDrawn(unit, withType: false)),
            unit,
            floor,
            before,
            scan.Count(row => row.Level == floor.Level) - 1);
        step.Note($"Wrote {drawn.Path} and {drawn.UntypedPath}: '{drawn.Name}' on level_id {unit.LevelId}.");
    }

    private async Task PickHandDrawn(StepResult step)
    {
        if (drawn == null)
        {
            step.NotRun("C3d.P made no hand-drawn file.");
            return;
        }

        await PickDrift(drawn.Path);
        ILocator card = Shell.Page.GetByTestId("shape-edit");
        drawnCard = step.Check(await Shell.ShowsAsync(card.GetByText("New floor drawn in the GDB"), 60000), "One card reads 'New floor drawn in the GDB'.");
        if (!drawnCard)
        {
            await NoteProblems(step, drawn.Path);
            await Shot(step, "C3d-1-hand-drawn-card");
            return;
        }

        step.Check(await card.CountAsync() == 1, $"There is one card ({await card.CountAsync()}).");
        string type = await ChosenType(card.First);
        step.Check(type.Length > 0 && !type.StartsWith("Pick a floor type", StringComparison.Ordinal), $"Its floor type is chosen from the file's category and restriction ({type}).");
        step.Check(!await Shell.Button("Apply 1 shape edit(s)").IsDisabledAsync(), "'Apply 1 shape edit(s)' is enabled.");
        drawn = drawn with { ChosenType = type };
        await Shot(step, "C3d-1-hand-drawn-card");
    }

    private async Task ApplyHandDrawn(StepResult step)
    {
        if (drawn == null || !drawnCard)
        {
            step.NotRun(drawn == null ? "C3d.P made no hand-drawn file." : "C3d.1 showed no card.");
            return;
        }

        int seq = host.LastEvent();
        string[] log = await ApplyShapesOrRecord(step);
        step.Note($"Log: {string.Join(" / ", log)}");
        Match created = log.Select(line => Regex.Match(line, @"^New floor (\d+) \((.+)\)\. The next Kanizu apply pairs the hand-drawn unit with it\.$")).FirstOrDefault(match => match.Success) ?? Match.Empty;
        if (step.Check(created.Success, "The list has 'New floor <id> (<type>). The next Kanizu apply pairs the hand-drawn unit with it.'."))
        {
            drawnFloor = long.Parse(created.Groups[1].Value, CultureInfo.InvariantCulture);
            drawn = drawn with { LoggedType = created.Groups[2].Value };
        }

        Commits(step, seq, HandDrawnTransaction(drawn));
        await Shot(step, "C3d-3-hand-drawn-applied");
    }

    private Task HandDrawnResult(StepResult step)
    {
        if (drawn == null || drawnFloor is not long id)
        {
            step.NotRun("C3d.3 made no floor.");
            return Task.CompletedTask;
        }

        ElementRow added = Element(id);
        step.Check(added.LevelId == drawn.Before.LevelId, $"The new floor is on F's level ({added.LevelId}, F was on {drawn.Before.LevelId}).");
        double area = ReshapeFixture.Area(drawn.Unit.Rings[0]) - drawn.Unit.Rings.Skip(1).Sum(ReshapeFixture.Area);
        step.Check(SameArea(added.AreaM2, area), $"It has the drawn unit's area ({added.AreaM2:0.0} m², unit {area:0.0} m²).");
        step.Check(added.TypeName == drawn.LoggedType, $"Its type is the one the log names ({added.TypeName}, log {drawn.LoggedType}).");
        step.Check(added.TypeName == drawn.ChosenType, $"It is the type the card had chosen ({drawn.ChosenType}).");
        int floors = FloorScan().Count(row => row.Level == drawn.Floor.Level);
        step.Check(floors == drawn.FloorsOnLevel + 1, $"The level has one floor more ({floors}, was {drawn.FloorsOnLevel} after deleting F).");
        step.Note($"New floor values: {Describe(Values(id, GisFields))}. F had type {drawn.Before.TypeName}.");
        return Task.CompletedTask;
    }

    private Task UndoHandDrawn(StepResult step)
    {
        if (drawn == null || drawnFloor is not long id)
        {
            step.NotRun("C3d.3 made no floor.");
            return Task.CompletedTask;
        }

        if (UndoOrRedo(step, "undo", HandDrawnTransaction(drawn)))
        {
            drawnUndone = true;
            step.Check(!Element(id).Exists, $"One Undo removes the new floor {id}.");
        }

        return Task.CompletedTask;
    }

    private async Task HandDrawnRoundTrip(StepResult step)
    {
        if (drawn == null || drawnFloor is not long id)
        {
            step.NotRun("C3d.3 made no floor.");
            return;
        }

        string name = HandDrawnTransaction(drawn);
        if (drawnUndone && !UndoOrRedo(step, "redo", name))
        {
            // The floor is undone after C3d.6; apply the typed file again, as a person would after a refused Redo.
            int seq = host.LastEvent();
            await PickDrift(drawn.Path);
            if (!await ExpectShapeEdits(step, drawn.Path))
            {
                return;
            }

            string[] again = await ApplyShapes(step, 1);
            step.Note($"Applied the drift file again instead of Redo. Log: {string.Join(" / ", again)}");
            Match created = again.Select(line => Regex.Match(line, @"^New floor (\d+) ")).FirstOrDefault(match => match.Success) ?? Match.Empty;
            if (!step.Check(host.EventsAfter(seq).Any(item => item.Is("commit", name)) && created.Success, $"Applying again commits '{name}' and lists a new floor."))
            {
                return;
            }

            id = long.Parse(created.Groups[1].Value, CultureInfo.InvariantCulture);
            drawnFloor = id;
        }

        string? uniqueId = Element(id).UniqueId;
        string output = await OpenExport(step, string.Empty, "export2", keepDefaultViews: true);
        bool exported = await ExportNow(step);
        await Shell.GoToAsync("/gis-standard");
        if (!exported || !step.Check(uniqueId != null, $"The new floor {id} is back after Redo."))
        {
            return;
        }

        FloorUnit? unit = ReshapeFixture.Pick(ReshapeFixture.FloorUnits(output), string.Empty, uniqueId!);
        double gap = unit == null ? double.NaN : ReshapeFixture.Distance(drawn.Unit.Rings, unit.Rings);
        step.Check(gap <= 0.01, $"The unit keyed by the new floor ({uniqueId}) is the drawn outline within 1 cm (largest gap {gap * 1000:0.0} mm).");
    }

    private async Task UntypedHandDrawn(StepResult step)
    {
        if (drawn == null)
        {
            step.NotRun("C3d.P made no hand-drawn file.");
            return;
        }

        // The new floor from C3d.3/C3d.7 goes first, so the unit counts as not in the model again. The test
        // host deletes it rather than Undo, which Revit can refuse on the build PC.
        RemoveDrawnFloor(step, drawnFloor);

        await PickDrift(drawn.UntypedPath);
        ILocator card = Shell.Page.GetByTestId("shape-edit");
        if (!step.Check(await Shell.ShowsAsync(card.GetByText("New floor drawn in the GDB"), 60000), "The untyped file shows the card."))
        {
            return;
        }

        ILocator apply = Shell.Button("Apply 1 shape edit(s)");
        string type = await ChosenType(card.First);
        step.Check(type.Length == 0 || type.StartsWith("Pick a floor type", StringComparison.Ordinal), $"No floor type is chosen ({type}).");
        step.Check(await apply.IsDisabledAsync(), "'Apply 1 shape edit(s)' stays disabled until the card has a type.");
        await Shot(step, "C3d-8-untyped-card");
        ILocator select = TypeSelect(card.First);
        await select.SelectOptionAsync(new SelectOptionValue { Index = 1 });
        string picked = await ChosenType(card.First);
        step.Check(!await apply.IsDisabledAsync(), $"After picking '{picked}', Apply is enabled.");
        int seq = host.LastEvent();
        string[] log = await ApplyShapesOrRecord(step);
        Match created = log.Select(line => Regex.Match(line, @"^New floor (\d+) \((.+)\)\.")).FirstOrDefault(match => match.Success) ?? Match.Empty;
        step.Check(created.Success && created.Groups[2].Value == picked, $"The new floor has the picked type ({(created.Success ? created.Groups[2].Value : "no new floor")}).");
        step.Check(host.EventsAfter(seq).Any(item => item.Is("commit", HandDrawnTransaction(drawn))), $"Apply commits '{HandDrawnTransaction(drawn)}'.");
        RemoveDrawnFloor(step, created.Success ? long.Parse(created.Groups[1].Value, CultureInfo.InvariantCulture) : null);
    }

    private void RemoveDrawnFloor(StepResult step, long? id)
    {
        if (id is long floor && Element(floor).Exists)
        {
            JsonObject fixture = host.Call("fixture", new { delete = new[] { floor } });
            step.Note($"Removed new floor {floor} through the test host: {fixture["log"]?.ToJsonString()}.");
        }
    }

    private async Task HandDrawnNoLevel(StepResult step)
    {
        if (drawn == null)
        {
            step.NotRun("C3d.P made no hand-drawn file.");
            return;
        }

        string path = WriteDrift("hand-drawn-no-level.gis-drift.json", drawn.Unit, ReshapeFixture.HandDrawn(drawn.Unit, levelId: NoSuchLevel));
        int floors = FloorScan().Length;
        await PickDrift(path);
        JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path, pick = false });
        string[] problems = Problems(drift);
        string expected = $"no level of this model has level_id {NoSuchLevel}, so there is no level to draw it on. Draw it in Revit by hand.";
        step.Check(problems.Any(problem => problem.Contains(expected, StringComparison.Ordinal)), $"Problems list \"{expected}\" ({string.Join(" | ", problems)}).");
        step.Check((drift?["shapes"]?.AsArray().Count ?? 0) == 0 && await Shell.Page.GetByTestId("shape-edit").CountAsync() == 0, "There is no card.");
        step.Check(FloorScan().Length == floors, "Nothing changes.");
        await Shot(step, "C3d-9-no-level");
    }

    private async Task HandDrawnCrossing(StepResult step)
    {
        if (drawn == null)
        {
            step.NotRun("C3d.P made no hand-drawn file.");
            return;
        }

        JsonObject[] crossing = ReshapeFixture.HandDrawnCrossing(drawn.Unit);
        var ring = crossing[0]["outline"]!["coordinates"]![0]!.AsArray().Select(point => (point![0]!.GetValue<double>(), point[1]!.GetValue<double>())).ToList();
        if (ReshapeFixture.IsSimple(ring))
        {
            step.NotRun("Swapping two corners of F's outline did not make it cross itself.");
            return;
        }

        string path = WriteDrift("hand-drawn-crossing.gis-drift.json", drawn.Unit, crossing);
        int floors = FloorScan().Length;
        int seq = host.LastEvent();
        await PickDrift(path);
        await Shell.Page.WaitForTimeoutAsync(3000);
        JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path, pick = false });
        string[] problems = Problems(drift);
        step.Note($"Problems: {string.Join(" | ", problems)}");
        step.Check(problems.Any(problem => problem.Contains("crosses itself", StringComparison.Ordinal)), "A problem line says the outline crosses itself.");
        step.Check((drift?["shapes"]?.AsArray().Count ?? 0) == 0 && await Shell.Page.GetByTestId("shape-edit").CountAsync() == 0, "There is no card.");
        step.Check(FloorScan().Length == floors && !host.EventsAfter(seq).Any(item => item.Kind != "dialog"), "Nothing changes in Revit.");
        await Shot(step, "C3d-10-crossing");
    }

    private static ILocator TypeSelect(ILocator card) => card.GetByLabel("Floor type");

    // The card's floor type: the selected option's text, or the empty placeholder.
    private static async Task<string> ChosenType(ILocator card)
    {
        ILocator select = TypeSelect(card);
        return await select.CountAsync() == 0
            ? string.Empty
            : (await select.EvaluateAsync<string>("select => select.value ? (select.options[select.selectedIndex]?.text ?? '') : ''")).Trim();
    }

    private static string HandDrawnTransaction(HandDrawnCase drawn) => $"Create a floor for the hand-drawn unit {drawn.Name} from the GDB";
}

internal sealed record HandDrawnCase(string Path, string UntypedPath, FloorUnit Unit, FloorScanRow Floor, ElementRow Before, int FloorsOnLevel)
{
    public string Name => ReshapeFixture.DrawnName(Unit);

    public string ChosenType { get; init; } = string.Empty;

    public string LoggedType { get; init; } = string.Empty;
}
