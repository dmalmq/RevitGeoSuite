using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The C3b and C3c checks (/mnt/project-files/revit-template-review/c3/ui-tests/c3b-split.md and
/// c3c-merge.md): Review GDB edits splits a floor by the GDB's pieces and merges floors the GDB merged.
/// Both make their drift files from their own export, like c3a-reshape. Step ids C3b.* and C3c.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const double SameAreaShare = 0.01;

    private SplitCase? split;
    private long? newFloor;
    private bool splitUndone;
    private (FloorUnit Unit, List<(double X, double Y)> West, List<(double X, double Y)> East)? splitSpare;
    private MergeCase? merge;
    private bool splitCard;
    private bool mergeCard;

    private async Task SplitSteps()
    {
        await Step("C3b.0", "The model has had Set up model once, so each level has a GIS_ plan", EnsureSetUp);
        await Step("C3b.G", "The copy gets RevitGeoSuite's georeference record from its own coordinates, which the Cesium export needs", EnsureGeoreference);
        await Step("C3b.S", "A Cesium export into a station folder gives the copy a model id, which a drift file must name", EnsureStation);
        await Step("C3b.P", "Export, make the split drift file, read floor F", PrepareSplit);
        await Step("C3b.1", "Pick the drift file: one split card with two dashed pieces", PickSplit);
        await Step("C3b.2", "Show in Revit draws both pieces in the GIS_ plan, with no transaction", step => ShowShapes(step, splitCard ? split?.Floor.Level : null));
        await Step("C3b.3", "Apply 1 shape edit: SketchEditScope inside a TransactionGroup", ApplySplit);
        await Step("C3b.4", "The new floor: level, offset, values, type, and F halved", SplitResult);
        await Step("C3b.5", "Check again offers nothing", step => NothingLeft(step, newFloor != null));
        await Step("C3b.6", "One Undo takes the whole split back", UndoSplit);
        await Step("C3b.7", "Redo, export again: both floors are the GDB pieces within 1 cm", SplitRoundTrip);
        await Step("C3b.8", "A split that leaves the kept floor's opening outside its piece is a problem, not a card", SplitOpening);
    }

    private async Task MergeSteps()
    {
        await Step("C3c.0", "The model has had Set up model once, so each level has a GIS_ plan", EnsureSetUp);
        await Step("C3c.G", "The copy gets RevitGeoSuite's georeference record from its own coordinates, which the Cesium export needs", EnsureGeoreference);
        await Step("C3c.S", "A Cesium export into a station folder gives the copy a model id, which a drift file must name", EnsureStation);
        await Step("C3c.P", "Export, pick floors K and G on one level, make the merge drift file", PrepareMerge);
        await Step("C3c.1", "Pick the drift file: one merge card", PickMerge);
        await Step("C3c.2", "Show in Revit draws both floors, with no transaction", step => ShowShapes(step, mergeCard ? merge?.Kept.Level : null));
        await Step("C3c.3", "Apply 1 shape edit: G deleted inside a TransactionGroup", ApplyMerge);
        await Step("C3c.4", "G is gone, K keeps its UniqueId and outline, one floor fewer", MergeResult);
        await Step("C3c.5", "Check again offers nothing", step => NothingLeft(step, merge?.Applied == true));
        await Step("C3c.6", "One Undo brings G back", UndoMerge);
        await Step("C3c.7", "A room taken in is a problem, not a card", MergeRoom);
        await Step("C3c.8", "Merging a floor that hosts an opening deletes it, and nothing blocks", MergeHosted);
        await Step("C3c.9", "Taking in a floor that is in several parts in the export is a problem, not a card", MergeMultiPart);
    }

    private async Task PrepareSplit(StepResult step)
    {
        if (!await ExportFloorUnits(step))
        {
            return;
        }

        FloorScanRow[] scan = FloorScan();
        var candidates = ShapeEditPicks.SplitCandidates(exportedUnits, exportedParts, uniqueId => scan.FirstOrDefault(row => row.UniqueId == uniqueId) is FloorScanRow row && Redrawable(row));
        step.Note($"{exportedParts.Count(pair => pair.Value == 1)} floor(s) export as one part; {candidates.Count} of them split along a line crossed twice: {Format(candidates.GroupBy(pair => pair.Unit.Category).ToDictionary(group => group.Key, group => group.Count()))}.");
        if (!step.Check(candidates.Count > 0, "The export has a floor that exports as one part without holes, that a straight line cuts in two pieces over 1 m², and that can be redrawn."))
        {
            return;
        }

        (FloorUnit unit, SplitCut pieces) = candidates[0];
        splitSpare = candidates.Skip(1).Where(pair => pair.Cut.NewPieceOnEastSide(1.0)).Select(pair => ((FloorUnit Unit, List<(double X, double Y)> West, List<(double X, double Y)> East)?)(pair.Unit, pair.Cut.West, pair.Cut.East)).FirstOrDefault();
        string path = WriteDrift("split.gis-drift.json", unit, ReshapeFixture.SplitShapes(unit, pieces.West, pieces.East));
        FloorScanRow floor = scan.First(row => row.UniqueId == unit.UniqueId);
        ElementRow before = Element(unit.UniqueId);
        Dictionary<string, string?> values = Values(before.Id!.Value, GisFields);
        split = new SplitCase(path, unit, floor, before, values, pieces.West, pieces.East, scan.Count(row => row.Level == floor.Level));
        step.Note($"Wrote {path}: '{unit.Name}' ({unit.Category}) cut along {pieces.Axis} = {pieces.At:0.00}, kept piece {ReshapeFixture.Area(pieces.West):0.0} m², new piece {ReshapeFixture.Area(pieces.East):0.0} m².");
        step.Note($"F = {unit.UniqueId}, element {before.Id}, type {before.TypeName} ({before.TypeId}), level {floor.Level} ({before.LevelId}), offset {Feet(before.HeightOffsetFt ?? 0)} ft, area {before.AreaM2:0.0} m², {split.FloorsOnLevel} floor(s) on the level. Values: {Describe(values)}.");
    }

    private async Task PickSplit(StepResult step)
    {
        if (split == null)
        {
            step.NotRun("C3b.P made no split file.");
            return;
        }

        await PickDrift(split.Path);
        ILocator card = Shell.Page.GetByTestId("shape-edit");
        splitCard = step.Check(await Shell.ShowsAsync(card.GetByText($"Split floor {split.Before.Id} into 2 floors"), 60000), $"One card reads 'Split floor {split.Before.Id} into 2 floors'.");
        if (!splitCard)
        {
            await NoteProblems(step, split.Path);
        }

        step.Check(await card.CountAsync() == 1, $"There is one card ({await card.CountAsync()}).");
        int dashed = await card.First.Locator("svg path[stroke-dasharray]").CountAsync();
        step.Check(dashed == 2, $"Its drawing has two dashed pieces ({dashed}).");
        await Shot(step, "C3b-1-split-card");
    }

    private async Task ShowShapes(StepResult step, string? level)
    {
        if (level == null)
        {
            step.NotRun("No shape edit card was shown.");
            return;
        }

        int seq = host.LastEvent();
        await Shell.Button("Show in Revit").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Button("Hide in Revit"), 30000), "The button turns into 'Hide in Revit'.");
        string view = host.Call("state")["viewName"]?.GetValue<string>() ?? string.Empty;
        string? viewLevel = Views().FirstOrDefault(plan => !plan.IsTemplate && plan.Name == view)?.Level;
        step.Check(view.StartsWith("GIS_", StringComparison.OrdinalIgnoreCase) && viewLevel == level, $"The active view is the GIS_ plan of level {level} ({view}, level {viewLevel}).");
        NoCommit(step, seq);
        RevitShot(step, $"{step.Id.Replace('.', '-')}-outlines");
        step.Note("Eyes: the screenshot shows every outline of the card (DirectContext3D at the floor top + 0.15 ft).");
        await Shell.Button("Hide in Revit").ClickAsync();
    }

    private async Task ApplySplit(StepResult step)
    {
        if (split == null || !splitCard)
        {
            step.NotRun(split == null ? "C3b.P made no split file." : "C3b.1 showed no split card.");
            return;
        }

        int seq = host.LastEvent();
        string[] log = await ApplyShapesOrRecord(step);
        step.Note($"Log: {string.Join(" / ", log)}");
        Match created = log.Select(line => Regex.Match(line, @"^New floor (\d+)")).FirstOrDefault(match => match.Success) ?? Match.Empty;
        if (step.Check(created.Success, "The list has a 'New floor <id> …' line under the edit."))
        {
            newFloor = long.Parse(created.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        split = split with { Log = log };
        Commits(step, seq, SplitTransaction(split));
        await Shot(step, "C3b-3-split-applied");
    }

    private Task SplitResult(StepResult step)
    {
        if (split == null || newFloor is not long id)
        {
            step.NotRun("C3b.3 made no new floor.");
            return Task.CompletedTask;
        }

        ElementRow kept = Element(split.Unit.UniqueId);
        ElementRow added = Element(id);
        step.Check(kept.Exists && kept.Id == split.Before.Id, $"Floor F keeps its UniqueId and element id ({kept.Id}).");
        step.Check(SameArea(kept.AreaM2, ReshapeFixture.Area(split.West)), $"F now has the west piece's area ({kept.AreaM2:0.0} m², piece {ReshapeFixture.Area(split.West):0.0} m², was {split.Before.AreaM2:0.0} m²).");
        step.Check(SameArea(added.AreaM2, ReshapeFixture.Area(split.East)), $"The new floor has the east piece's area ({added.AreaM2:0.0} m², piece {ReshapeFixture.Area(split.East):0.0} m²).");
        int floors = FloorScan().Count(row => row.Level == split.Floor.Level);
        step.Check(floors == split.FloorsOnLevel + 1, $"The level has one floor more ({floors}, was {split.FloorsOnLevel}).");
        step.Check(added.LevelId == split.Before.LevelId, $"The new floor is on F's level ({added.LevelId}).");
        step.Check(Math.Abs((added.HeightOffsetFt ?? 0) - (split.Before.HeightOffsetFt ?? 0)) < 1e-6, $"It has F's height offset ({Feet(added.HeightOffsetFt ?? 0)} ft).");
        Dictionary<string, string?> values = Values(id, GisFields);
        step.Note($"New floor values: {Describe(values)}; type {added.TypeName} ({added.TypeId}).");
        step.Check(values["name"] == split.Values["name"] + " east", $"Its name is \"{split.Values["name"]} east\" ({values["name"]}).");
        step.Check(values["short_name"] == "E", $"Its short_name is E ({values["short_name"]}).");
        step.Check(values["category"] == split.Values["category"], $"Its category is F's, {split.Values["category"]} ({values["category"]}).");
        step.Check(string.IsNullOrEmpty(values["id"]), $"Its id is empty until the next export, never F's ({values["id"]}).");
        step.Check(string.IsNullOrEmpty(values["gis_key"]), $"Its gis_key is empty until the next export, never F's ({values["gis_key"]}).");
        bool noStandardType = split.Log.Any(line => line.StartsWith("No floor type ", StringComparison.Ordinal));
        if (noStandardType)
        {
            step.Check(added.TypeId == split.Before.TypeId, $"The result names a missing standard type, and the new floor keeps F's type ({added.TypeName}).");
        }
        else
        {
            step.Note($"The new floor took type {added.TypeName}, the standard type for {values["category"]} and F's restriction; F's type is {split.Before.TypeName}.");
        }

        return Task.CompletedTask;
    }

    private async Task NothingLeft(StepResult step, bool applied)
    {
        if (!applied)
        {
            step.NotRun("Nothing was applied.");
            return;
        }

        await Shell.Button("Check again").Last.ClickAsync();
        await Shell.Page.WaitForTimeoutAsync(2000);
        step.Check(await Gone(Shell.Page.GetByTestId("shape-edits")), "No shape edits are left.");
    }

    private Task UndoSplit(StepResult step)
    {
        if (split == null || newFloor is not long id)
        {
            step.NotRun("C3b.3 made no new floor.");
            return Task.CompletedTask;
        }

        if (!UndoOrRedo(step, "undo", SplitTransaction(split)))
        {
            return Task.CompletedTask;
        }

        splitUndone = true;
        step.Check(!Element(id).Exists, $"One Undo removes the new floor {id}.");
        ElementRow kept = Element(split.Unit.UniqueId);
        step.Check(SameBox(kept, split.Before) && SameArea(kept.AreaM2, split.Before.AreaM2 ?? 0), $"The same Undo gives F its old outline (area {kept.AreaM2:0.0} m², was {split.Before.AreaM2:0.0} m²).");
        return Task.CompletedTask;
    }

    private async Task SplitRoundTrip(StepResult step)
    {
        if (split == null || newFloor is not long id)
        {
            step.NotRun("C3b.3 made no new floor.");
            return;
        }

        string name = SplitTransaction(split);
        if (splitUndone && !UndoOrRedo(step, "redo", name))
        {
            // The split is undone after C3b.6, so exporting now would compare the old floor. Apply the
            // same file again, as a person would after a refused Redo; the new floor then has a new id.
            int seq = host.LastEvent();
            await PickDrift(split.Path);
            if (!await ExpectShapeEdits(step, split.Path))
            {
                return;
            }

            string[] again = await ApplyShapes(step, 1);
            step.Note($"Applied the drift file again instead of Redo. Log: {string.Join(" / ", again)}");
            Match created = again.Select(line => Regex.Match(line, @"^New floor (\d+)")).FirstOrDefault(match => match.Success) ?? Match.Empty;
            if (!step.Check(host.EventsAfter(seq).Any(item => item.Is("commit", name)) && created.Success, $"Applying again commits '{name}' and lists a new floor."))
            {
                return;
            }

            id = long.Parse(created.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        string? newUniqueId = Element(id).UniqueId;
        string output = await OpenExport(step, string.Empty, "export2", keepDefaultViews: true);
        bool exported = await ExportNow(step);
        await Shell.GoToAsync("/gis-standard");
        if (!exported || !step.Check(newUniqueId != null, $"The new floor {id} is back after Redo."))
        {
            return;
        }

        List<FloorUnit> units = ReshapeFixture.FloorUnits(output);
        FloorUnit? kept = ReshapeFixture.Pick(units, string.Empty, split.Unit.UniqueId);
        FloorUnit? added = ReshapeFixture.Pick(units, string.Empty, newUniqueId!);
        double keptGap = kept == null ? double.NaN : ReshapeFixture.Distance(new[] { split.West }, kept.Rings);
        double addedGap = added == null ? double.NaN : ReshapeFixture.Distance(new[] { split.East }, added.Rings);
        step.Check(keptGap <= 0.01, $"F's exported outline is the west piece within 1 cm (largest gap {keptGap * 1000:0.0} mm).");
        step.Check(addedGap <= 0.01, $"A unit keyed by the new floor ({newUniqueId}) is the east piece within 1 cm (largest gap {addedGap * 1000:0.0} mm).");
    }

    // The kept floor gets the west piece, so an opening the fixture cuts within 1 m of its east edge falls
    // outside it. Like C3a.9's H case, the plan should name the opening as a problem and offer no card.
    private async Task SplitOpening(StepResult step)
    {
        if (splitSpare is not { } spare)
        {
            step.NotRun("C3b.P found no second floor whose cut leaves a new piece east of the kept one, wider than the fixture's 1 m band.");
            return;
        }

        FloorScanRow floor = FloorScan().First(row => row.UniqueId == spare.Unit.UniqueId);
        JsonObject fixture;
        try
        {
            fixture = host.Call("fixture", new { openingInEastBand = new[] { new { floor = floor.Id, bandM = 1.0, sizeM = 0.4 } } });
        }
        catch (TestHostException ex)
        {
            step.NotRun($"The opening fixture failed on floor {floor.Id}: {ex.Message}");
            return;
        }

        step.Note($"Floor {floor.Id} '{spare.Unit.Name}' ({spare.Unit.Category}). Fixture: {fixture["log"]?.ToJsonString()}.");
        DependentRow[] dependents = FloorScan().First(row => row.Id == floor.Id).Dependents;
        long[] openings = dependents.Where(IsOpening).Select(item => item.Id).ToArray();
        if (openings.Length == 0)
        {
            openings = dependents.Select(item => item.Id).ToArray();
        }

        string path = WriteDrift("split-opening.gis-drift.json", spare.Unit, ReshapeFixture.SplitShapes(spare.Unit, spare.West, spare.East));
        int pickSeq = host.LastEvent();
        await PickDrift(path);
        await Shell.Page.WaitForTimeoutAsync(3000);
        JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path, pick = false });
        string[] problems = Problems(drift);
        step.Note($"Problems: {string.Join(" | ", problems)}");
        string? line = problems.FirstOrDefault(problem => problem.Contains($"floor {floor.Id} would leave opening(s) ", StringComparison.Ordinal));
        step.Check(openings.Length > 0 && line != null && openings.All(id => line.Contains(id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)),
            $"A problem line says floor {floor.Id} would leave opening(s) {string.Join(", ", openings)} outside its new outline ({line ?? "none"}).");
        step.Check(line?.Contains("(\"Can't make cut-out\")", StringComparison.Ordinal) == true && line.Contains("Delete or move the opening(s) in Revit first", StringComparison.Ordinal), "It names Revit's refusal, \"Can't make cut-out\", and says to delete or move the opening(s) first.");
        step.Check((drift?["shapes"]?.AsArray().Count ?? 0) == 0 && await Shell.Page.GetByTestId("shape-edit").CountAsync() == 0, "There is no shape edit card.");
        RevitEvent[] changes = host.EventsAfter(pickSeq).Where(item => item.Kind != "dialog").ToArray();
        step.Check(changes.Length == 0, $"No Revit transaction happens ({(changes.Length == 0 ? "none" : string.Join(" / ", changes.Select(item => item.ToString())))}).");
        string[] gone = Elements(dependents.Select(item => item.Id)).Where(row => !row.Exists).Select(row => row.Key).ToArray();
        step.Check(gone.Length == 0, $"Every dependent is still there (missing: {string.Join(", ", gone)}).");
        await Shot(step, "C3b-8-opening-problem");
    }

    private async Task PrepareMerge(StepResult step)
    {
        if (!await ExportFloorUnits(step))
        {
            return;
        }

        FloorScanRow[] scan = FloorScan();
        FloorScanRow? Row(string uniqueId) => scan.FirstOrDefault(row => row.UniqueId == uniqueId);
        var pairs = ShapeEditPicks.MergePairs(
            exportedUnits,
            exportedParts,
            uniqueId => Row(uniqueId) is FloorScanRow row && Redrawable(row),
            uniqueId => Row(uniqueId) is FloorScanRow row && Redrawable(row) && row.Dependents.Length == 0);
        if (!step.Check(pairs.Count > 0, "Two floors on one level that each export as one part, the second hosting nothing."))
        {
            return;
        }

        (FloorUnit keptUnit, FloorUnit takenUnit, double distance) = pairs[0];
        FloorScanRow keptRow = Row(keptUnit.UniqueId)!, takenRow = Row(takenUnit.UniqueId)!;
        string path = WriteDrift("merge.gis-drift.json", keptUnit, ReshapeFixture.Merged(keptUnit, takenUnit.UniqueId));
        merge = new MergeCase(path, keptUnit, keptRow, takenUnit, takenRow, Element(keptUnit.UniqueId), scan.Count(row => row.Level == keptRow.Level));
        step.Note($"K = {keptRow.Id} '{keptUnit.Name}' ({keptUnit.Category}, {exportedParts.GetValueOrDefault(keptUnit.UniqueId)} part), G = {takenRow.Id} '{takenUnit.Name}' ({takenUnit.Category}, {exportedParts.GetValueOrDefault(takenUnit.UniqueId)} part) on level {keptRow.Level}, {(distance <= 0.01 ? "touching" : $"{distance:0.00} m apart")}; {merge.FloorsOnLevel} floor(s) on the level. Wrote {path}.");
    }

    private async Task PickMerge(StepResult step)
    {
        if (merge == null)
        {
            step.NotRun("C3c.P made no merge file.");
            return;
        }

        await PickDrift(merge.Path);
        ILocator card = Shell.Page.GetByTestId("shape-edit");
        mergeCard = step.Check(await Shell.ShowsAsync(card.GetByText($"Merge 2 floors into floor {merge.Kept.Id}"), 60000), $"One card reads 'Merge 2 floors into floor {merge.Kept.Id}'.");
        if (!mergeCard)
        {
            await NoteProblems(step, merge.Path);
        }

        step.Check(await card.CountAsync() == 1, $"There is one card ({await card.CountAsync()}).");
        int grey = await card.First.Locator("svg path:not([stroke-dasharray])").CountAsync();
        step.Note($"The drawing has {grey} grey path(s); both floors should show in grey.");
        await Shot(step, "C3c-1-merge-card");
    }

    private async Task ApplyMerge(StepResult step)
    {
        if (merge == null || !mergeCard)
        {
            step.NotRun(merge == null ? "C3c.P made no merge file." : "C3c.1 showed no merge card.");
            return;
        }

        int seq = host.LastEvent();
        string[] log = await ApplyShapesOrRecord(step);
        step.Note($"Log: {string.Join(" / ", log)}");
        step.Check(log.Contains($"Deleted floor {merge.Taken.Id}."), $"The list has 'Deleted floor {merge.Taken.Id}.' under the edit.");
        merge = merge with { Applied = !Element(merge.Taken.UniqueId).Exists };
        Commits(step, seq, MergeTransaction(merge));
        await Shot(step, "C3c-3-merge-applied");
    }

    private Task MergeResult(StepResult step)
    {
        if (merge is not { Applied: true })
        {
            step.NotRun("C3c.3 did not apply.");
            return Task.CompletedTask;
        }

        ElementRow kept = Element(merge.KeptUnit.UniqueId);
        step.Check(!Element(merge.Taken.UniqueId).Exists, $"G ({merge.Taken.Id}) is gone.");
        step.Check(kept.Exists && kept.Id == merge.Kept.Id && SameBox(kept, merge.KeptBefore), $"K keeps its UniqueId, element id and outline ({Box(kept)}).");
        int floors = FloorScan().Count(row => row.Level == merge.Kept.Level);
        step.Check(floors == merge.FloorsOnLevel - 1, $"The level has one floor fewer ({floors}, was {merge.FloorsOnLevel}).");
        return Task.CompletedTask;
    }

    private Task UndoMerge(StepResult step)
    {
        if (merge is not { Applied: true })
        {
            step.NotRun("C3c.3 did not apply.");
            return Task.CompletedTask;
        }

        if (!UndoOrRedo(step, "undo", MergeTransaction(merge)))
        {
            return Task.CompletedTask;
        }

        merge = merge with { Applied = false };
        ElementRow back = Element(merge.Taken.UniqueId);
        step.Check(back.Exists && back.Id == merge.Taken.Id, $"G is back with its UniqueId ({back.Id}).");
        return Task.CompletedTask;
    }

    private async Task MergeRoom(StepResult step)
    {
        if (merge == null)
        {
            step.NotRun("C3c.P made no merge file.");
            return;
        }

        JsonObject room = host.Call("firstOf", new { category = "OST_Rooms" });
        if (room["uniqueId"] is null)
        {
            step.NotRun("The model has no room.");
            return;
        }

        string roomUniqueId = room["uniqueId"]!.GetValue<string>();
        long roomId = room["id"]!.GetValue<long>();
        int floors = FloorScan().Length;
        string path = WriteDrift("merge-room.gis-drift.json", merge.KeptUnit, ReshapeFixture.Merged(merge.KeptUnit, roomUniqueId));
        await PickDrift(path);
        JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path, pick = false });
        string[] problems = Problems(drift);
        string expected = $"element {roomId} is not a floor, so the merge does not delete it. Merge it in Revit by hand.";
        step.Check(problems.Any(problem => problem.Contains(expected, StringComparison.Ordinal)), $"Problems list \"{expected}\" ({string.Join(" | ", problems)}).");
        step.Check((drift?["shapes"]?.AsArray().Count ?? 0) == 0 && await Shell.Page.GetByTestId("shape-edit").CountAsync() == 0, "There is no card.");
        step.Check(FloorScan().Length == floors && Element(roomUniqueId).Exists, "Nothing changes: the floors and the room are all there.");
        await Shot(step, "C3c-7-room-problem");
    }

    // The GDB merged one of G's units; deleting the whole floor would drop its other parts, so the plan refuses.
    private async Task MergeMultiPart(StepResult step)
    {
        if (merge == null)
        {
            step.NotRun("C3c.P made no merge file.");
            return;
        }

        FloorScanRow[] scan = FloorScan();
        FloorScanRow? taken = scan.FirstOrDefault(row => row.Level == merge.Kept.Level && exportedParts.GetValueOrDefault(row.UniqueId) > 1);
        if (taken == null)
        {
            step.NotRun($"No floor on level {merge.Kept.Level} is in several parts in the export.");
            return;
        }

        int parts = exportedParts[taken.UniqueId];
        int floors = scan.Length;
        string path = WriteDrift("merge-multipart.gis-drift.json", merge.KeptUnit, ReshapeFixture.Merged(merge.KeptUnit, taken.UniqueId));
        int pickSeq = host.LastEvent();
        await PickDrift(path);
        await Shell.Page.WaitForTimeoutAsync(3000);
        JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path, pick = false });
        string[] problems = Problems(drift);
        string? line = problems.FirstOrDefault(problem => problem.Contains($"floor {taken.Id} is in ", StringComparison.Ordinal) && problem.Contains(" separate parts", StringComparison.Ordinal));
        step.Check(line != null, $"A problem line says floor {taken.Id} is in separate parts; the export has {parts} ({string.Join(" | ", problems)}).");
        step.Check((drift?["shapes"]?.AsArray().Count ?? 0) == 0 && await Shell.Page.GetByTestId("shape-edit").CountAsync() == 0, "There is no card.");
        RevitEvent[] changes = host.EventsAfter(pickSeq).Where(item => item.Kind != "dialog").ToArray();
        step.Check(changes.Length == 0 && FloorScan().Length == floors, $"Nothing changes in Revit ({(changes.Length == 0 ? "no transaction" : string.Join(" / ", changes.Select(item => item.ToString())))}).");
        await Shot(step, "C3c-9-multipart-problem");
    }

    // The evidence table's last assumption: deleting a merged floor deletes what it hosts, and Revit blocks nothing.
    private async Task MergeHosted(StepResult step)
    {
        if (merge == null)
        {
            step.NotRun("C3c.P made no merge file.");
            return;
        }

        if (merge.Applied)
        {
            step.NotRun($"G ({merge.Taken.Id}) is still merged away: Revit refused the Undo in C3c.6.");
            return;
        }

        if (!mergeCard)
        {
            step.NotRun("C3c.1 showed no merge card.");
            return;
        }

        JsonObject fixture;
        try
        {
            fixture = host.Call("fixture", new { openingInEastBand = new[] { new { floor = merge.Taken.Id, bandM = 1.0, sizeM = 0.4 } } });
        }
        catch (TestHostException ex)
        {
            step.NotRun($"The opening fixture failed on G ({merge.Taken.Id}): {ex.Message}");
            return;
        }

        step.Note($"Fixture: {fixture["log"]?.ToJsonString()}.");
        long opening = FloorScan().First(row => row.Id == merge.Taken.Id).Dependents.Select(item => item.Id).FirstOrDefault();
        await PickDrift(merge.Path);
        if (!step.Check(await Shell.ShowsAsync(Shell.Page.GetByTestId("shape-edit"), 60000), "The merge card is back."))
        {
            return;
        }

        int seq = host.LastEvent();
        string[] log = await ApplyShapesOrRecord(step);
        step.Note($"Log: {string.Join(" / ", log)}");
        Commits(step, seq, MergeTransaction(merge));
        step.Check(!Element(merge.Taken.UniqueId).Exists, $"G ({merge.Taken.Id}) is deleted although it hosts opening {opening}.");
        step.Check(opening != 0 && !Element(opening).Exists, $"Opening {opening} is gone with it.");
        step.Check(log.Any(line => line.StartsWith($"Deleted floor {merge.Taken.Id}, and with it opening {opening}", StringComparison.Ordinal)), $"The result reads 'Deleted floor {merge.Taken.Id}, and with it opening {opening}.'");
        if (UndoOrRedo(step, "undo", MergeTransaction(merge)))
        {
            step.Check(Element(merge.Taken.UniqueId).Exists && Element(opening).Exists, "One Undo brings G and the opening back.");
        }
    }

    /// <summary>
    /// Applies one shape edit. When Revit refuses (the open risk: a SketchEditScope inside a
    /// TransactionGroup), the step fails with the page's error text, so the report quotes Revit.
    /// </summary>
    private async Task<string[]> ApplyShapesOrRecord(StepResult step)
    {
        string[] log = await ApplyShapes(step, 1);
        if (log.Length == 0)
        {
            step.Check(false, $"Revit's answer: {await Shell.AlertTextAsync()}");
        }

        return log;
    }

    // Each transaction Revit committed during the apply. One Undo, checked after, is what proves one undo step.
    private void Commits(StepResult step, int after, string name)
    {
        List<RevitEvent> commits = host.EventsAfter(after).Where(item => item.Kind == "commit").ToList();
        step.Note($"Commit events: {string.Join(" / ", commits)}.");
        step.Check(commits.Any(item => item.Names.Contains(name, StringComparer.Ordinal)), $"A commit is named '{name}'.");
    }

    // The plan's reasons when a card is missing, also shown under "row(s) will not be written".
    private async Task NoteProblems(StepResult step, string path)
    {
        JsonNode? drift = await Shell.RpcAsync("gisDrift.inspect", new { path, pick = false });
        step.Note($"Problems: {string.Join(" | ", Problems(drift))}");
        ILocator rows = Shell.Page.GetByText(new Regex(@"row\(s\) will not be written"));
        if (await rows.CountAsync() > 0)
        {
            await rows.First.ClickAsync();
            await Shot(step, "problems-expanded");
        }
    }

    private static readonly string[] GisFields = { "name", "short_name", "category", "id", "gis_key" };

    private Dictionary<string, string?> Values(long id, string[] names)
    {
        JsonNode? element = host.Call("params", new { ids = new[] { id }, names })["elements"]?[id.ToString(CultureInfo.InvariantCulture)];
        return names.ToDictionary(name => name, name => element?[name]?.GetValue<string>());
    }

    private static bool SameArea(double? actual, double expected) =>
        actual is double area && Math.Abs(area - expected) <= SameAreaShare * Math.Max(expected, 1);

    private static string Describe(Dictionary<string, string?> values) => string.Join(", ", values.Select(pair => $"{pair.Key} '{pair.Value}'"));

    private static string SplitTransaction(SplitCase split) =>
        $"Split floor {split.Before.Id}{(split.Unit.Name.Length > 0 ? $" ({split.Unit.Name})" : string.Empty)} into 2 floors by the GDB";

    private static string MergeTransaction(MergeCase merge) =>
        $"Merge floor(s) {merge.Taken.Id} into floor {merge.Kept.Id}{(merge.KeptUnit.Name.Length > 0 ? $" ({merge.KeptUnit.Name})" : string.Empty)} by the GDB";
}

internal sealed record SplitCase(
    string Path,
    FloorUnit Unit,
    FloorScanRow Floor,
    ElementRow Before,
    Dictionary<string, string?> Values,
    List<(double X, double Y)> West,
    List<(double X, double Y)> East,
    int FloorsOnLevel)
{
    public string[] Log { get; init; } = Array.Empty<string>();
}

internal sealed record MergeCase(string Path, FloorUnit KeptUnit, FloorScanRow Kept, FloorUnit TakenUnit, FloorScanRow Taken, ElementRow KeptBefore, int FloorsOnLevel)
{
    public bool Applied { get; init; }
}
