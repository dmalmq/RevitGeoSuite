using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// G3: what the export decides by guess is an error on a level a GIS_ plan exports. G3.1 reads the
/// copy's own numbers and compares the ramp and floor family findings with the test host's own count;
/// G3.2 to G3.5 make one case each with a fixture and expect the finding; G3.6 exports and expects
/// the gate. Step ids G3.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    // The sentence ExportAudit.RaiseGuessesOnGisPlans adds to a warning it raises to an error.
    private const string RaisedText = "on levels with a GIS_ plan, so the export asks to fix or accept this first.";
    private const string UnreadText = "which the export never reads";

    // The test host's FamilyFixtures.CategoryRefused.
    private const string CategoryRefused = "Revit refuses a loadable family of category";

    private ElementRow? guessFloor;
    private string? guessFloorError;

    private async Task GuessSteps()
    {
        await Step("G3.0", "The model has had Set up model once (gis_door is bound)", EnsureSetUp);
        await Step("G3.1", "The copy's own numbers; every ramp and floor family on a level with a GIS_ plan is an error", GuessCounts);
        await Step("G3.2", "A floor type on a GIS_ plan without gis_category is an error", FloorTypeGuess);
        await Step("G3.3", "A door type on a GIS_ plan: gis_door set is no finding, gis_door cleared is a doors warning", DoorGuess);
        await Step("G3.4", "A family that looks like an escalator off the standard list, on a GIS_ plan, is an error", EscalatorGuess);
        await Step("G3.5", "A floor made as a family, on a GIS_ plan, is an error", FloorFamilyGuess);
        await Step("G3.6", "The export asks first, lists G3.2's error, and Export anyway exports", GuessExport);
    }

    private async Task GuessCounts(StepResult step)
    {
        InspectReply model = await Inspect();
        NoteCounts(step, model);
        FindingItem[] raised = model.Findings.Where(finding => finding.Message.Contains(RaisedText, StringComparison.Ordinal)).ToArray();
        step.Note($"{raised.Length} guess(es) raised to errors: {string.Join(", ", raised.GroupBy(finding => finding.Topic).Select(group => $"{group.Key} {group.Count()}"))}.");

        HashSet<long> gisLevels = GisPlanLevelIds();
        FindingItem[] unread = model.Findings.Where(finding => finding.Message.Contains(UnreadText, StringComparison.Ordinal)).ToArray();
        foreach (FindingItem finding in unread)
        {
            step.Note($"{finding.Severity} {finding.Topic} ({finding.ElementIds.Length} element(s)): {finding.Message}");
        }

        HashSet<long> reported = unread.SelectMany(finding => finding.ElementIds).ToHashSet();
        InstanceRow[] ramps = Instances("OST_Ramps");
        InstanceRow[] floorFamilies = Instances("OST_Floors").Where(row => row.Class == "FamilyInstance").ToArray();
        foreach ((string what, InstanceRow[] rows) in new[] { ("Revit ramps", ramps), ("floor families", floorFamilies) })
        {
            InstanceRow[] shown = rows.Where(row => !row.Demolished && row.DesignOption == null).ToArray();
            InstanceRow[] onPlans = shown.Where(row => gisLevels.Contains(row.LevelId)).ToArray();
            step.Note($"The test host counts {rows.Length} {what}: {shown.Length} not demolished and in no design option, {onPlans.Length} with their own level on a GIS_ plan, {rows.Count(row => row.LevelId < 0)} without their own level, {rows.Count(row => row.InPlace == true)} in-place.");
            if (onPlans.Length == 0)
            {
                step.Note($"No {what} stand on a level with a GIS_ plan, so this rule is not exercised on this model.");
                continue;
            }

            step.Check(onPlans.All(row => reported.Contains(row.Id)), $"Every one of the {onPlans.Length} {what} whose own level has a GIS_ plan is in an error.");
        }

        HashSet<long> known = ramps.Concat(floorFamilies).Select(row => row.Id).ToHashSet();
        step.Check(reported.All(known.Contains), $"Every element of those findings ({reported.Count}) is a ramp or a floor family the test host lists.");
        step.Check(unread.All(finding => finding.Severity == "error"), $"Those findings are errors ({unread.Length}).");
        await Shot(step, "G3-1-audit");
    }

    /// <summary>The ids of the levels a GIS_ plan exports, by the plans' level names.</summary>
    private HashSet<long> GisPlanLevelIds()
    {
        HashSet<string> names = new(GisPlans(Views()).Select(view => view.Level ?? string.Empty), StringComparer.Ordinal);
        return Levels().Where(level => names.Contains(level.Name)).Select(level => level.Id).ToHashSet();
    }

    private InstanceRow[] Instances(string category) =>
        host.Call("instances", new { category })["elements"].Deserialize<InstanceRow[]>(Web) ?? Array.Empty<InstanceRow>();

    private async Task FloorTypeGuess(StepResult step)
    {
        HashSet<long> gisLevels = GisPlanLevelIds();
        HashSet<long> flagged = (await Inspect()).Findings.Where(finding => finding.Topic == "floor types").SelectMany(finding => finding.ElementIds).ToHashSet();
        guessFloor = Elements(FloorScan().Select(row => row.Id).Where(id => !flagged.Contains(id)))
            .FirstOrDefault(row => row is { Exists: true, Id: not null, TypeId: not null, LevelId: not null } && gisLevels.Contains(row.LevelId!.Value));
        if (!step.Check(guessFloor != null, $"A floor with no floor type finding stands on a level with a GIS_ plan ({gisLevels.Count} such level(s))."))
        {
            return;
        }

        JsonObject fixture = host.Call("fixture", new { values = new[] { new { id = guessFloor!.TypeId!.Value, name = "gis_category", value = "" } } });
        step.Note($"Fixture: floor type {guessFloor.TypeName} ({guessFloor.TypeId}) of floor {guessFloor.Id}: {fixture["log"]?.ToJsonString()}");
        await CheckAgain();
        FindingItem? finding = (await Inspect()).Findings.FirstOrDefault(item => item.Topic == "floor types" && item.ElementIds.Contains(guessFloor.Id!.Value));
        if (step.Check(finding is { Severity: "error" } && finding.Message.Contains(RaisedText, StringComparison.Ordinal), $"Floor type {guessFloor.TypeName} is an error that says why: {finding?.Severity} {finding?.Message}"))
        {
            guessFloorError = finding!.Message;
        }

        await Shot(step, "G3-2-floor-type");
    }

    private async Task DoorGuess(StepResult step)
    {
        HashSet<long> gisLevels = GisPlanLevelIds();
        DoorRow[] doors = host.Call("doors")["doors"].Deserialize<DoorRow[]>(Web) ?? Array.Empty<DoorRow>();
        ElementRow? door = Elements(doors.Select(row => row.Id)).FirstOrDefault(row => row is { Exists: true, Id: not null, TypeId: not null, LevelId: not null } && gisLevels.Contains(row.LevelId!.Value));
        if (!step.Check(door != null, $"A door stands on a level with a GIS_ plan ({doors.Length} door(s) in the model)."))
        {
            return;
        }

        async Task<FindingItem?> DoorFinding(string value)
        {
            JsonObject fixture = host.Call("fixture", new { values = new[] { new { id = door!.TypeId!.Value, name = "gis_door", value } } });
            step.Note($"Fixture: gis_door '{value}' on door type {door.TypeName} ({door.TypeId}): {fixture["log"]?.ToJsonString()}");
            await CheckAgain();
            return (await Inspect()).Findings.FirstOrDefault(item => item.Topic == "doors" && item.ElementIds.Contains(door.Id!.Value));
        }

        FindingItem? set = await DoorFinding("uitest");
        step.Check(set == null, $"With gis_door set, door {door!.Id} is in no doors finding ({set?.Message}).");
        FindingItem? cleared = await DoorFinding("");
        step.Check(cleared is { Severity: "warning" }, $"With gis_door cleared, door type {door.TypeName} is a doors warning: {cleared?.Severity} {cleared?.Message}");
        await Shot(step, "G3-3-door");
    }

    private async Task EscalatorGuess(StepResult step)
    {
        if (FamilySpot() is not (string level, double x, double y))
        {
            step.NotRun("G3.2 found no floor to stand the family on.");
            return;
        }

        JsonObject escalator = host.Call("placeFamily", new { path = Path.Combine(options.OutputFolder, "UITest Escalator G3.rfa"), category = "OST_GenericModel", level, x, y });
        step.Note($"Placed on {level}: {escalator.ToJsonString()}");
        await CheckAgain();
        long escalatorId = escalator["id"]!.GetValue<long>();
        FindingItem? family = (await Inspect()).Findings.FirstOrDefault(item => item.Topic == "families" && item.ElementIds.Contains(escalatorId));
        step.Check(family is { Severity: "error" } && family.Message.Contains("looks like an escalator", StringComparison.Ordinal) && family.Message.Contains(RaisedText, StringComparison.Ordinal),
            $"The family off the list is an error that says why: {family?.Severity} {family?.Message}");
        await Shot(step, "G3-4-escalator");
    }

    private async Task FloorFamilyGuess(StepResult step)
    {
        step.Note("Not made here: a Revit ramp and an in-place floor. The Revit API cannot create either. For a fixture model, someone draws both on a copy of LUMINE on a level with a GIS_ plan: Architecture > Ramp (any size), and Architecture > Component > Model In-Place, category Floors, one extrusion of about 2 x 2 m. G3.1 then lists them.");
        if (FamilySpot() is not (string level, double x, double y))
        {
            step.NotRun("G3.2 found no floor to stand the family on.");
            return;
        }

        if (PlaceFamilyOrRefused(step, "UITest Floor G3.rfa", "OST_Floors", level, x, y) is not JsonObject floor)
        {
            return;
        }

        await CheckAgain();
        long floorId = floor["id"]!.GetValue<long>();
        FindingItem? unread = (await Inspect()).Findings.FirstOrDefault(item => item.ElementIds.Contains(floorId) && item.Message.Contains(UnreadText, StringComparison.Ordinal));
        step.Check(unread is { Severity: "error" } && unread.Message.Contains("made as a family", StringComparison.Ordinal), $"The floor family is an error: {unread?.Severity} {unread?.Message}");
        await Shot(step, "G3-5-floor-family");
    }

    /// <summary>The level and the middle of G3.2's floor, where G3.4 and G3.5 place their families.</summary>
    private (string Level, double X, double Y)? FamilySpot() =>
        guessFloor?.Min == null || guessFloor.Max == null
            ? null
            : (Levels().First(row => row.Id == guessFloor.LevelId).Name, (guessFloor.Min[0] + guessFloor.Max[0]) / 2, (guessFloor.Min[1] + guessFloor.Max[1]) / 2);

    /// <summary>
    /// Places a box family with the test host. When Revit refuses the category for a loadable family,
    /// the step is not run, with Revit's message; any other failure fails the step.
    /// </summary>
    private JsonObject? PlaceFamilyOrRefused(StepResult step, string file, string category, string level, double x, double y)
    {
        try
        {
            JsonObject placed = host.Call("placeFamily", new { path = Path.Combine(options.OutputFolder, file), category, level, x, y });
            step.Note($"Placed on {level}: {placed.ToJsonString()}");
            return placed;
        }
        catch (Exception ex) when (ex.Message.Contains(CategoryRefused, StringComparison.Ordinal))
        {
            step.NotRun($"Not exercised: {ex.Message.Split('\n')[0]}");
            return null;
        }
        catch (Exception ex)
        {
            step.Check(false, $"placeFamily for {file} failed: {ex.Message.Split('\n')[0]}");
            return null;
        }
    }

    private async Task GuessExport(StepResult step)
    {
        if (guessFloor == null)
        {
            step.NotRun("G3.2 found no floor, so there is no plan to export.");
            return;
        }

        string level = Levels().First(row => row.Id == guessFloor.LevelId).Name;
        string? plan = GisPlans(Views()).FirstOrDefault(view => view.Level == level)?.Name;
        if (!step.Check(plan != null, $"Level {level} has a GIS_ plan."))
        {
            return;
        }

        await OpenExport(step, plan!, "g3-export");
        ILocator export = Shell.Button(new System.Text.RegularExpressions.Regex(@"^Export (1 view|\d+ views)$"));
        if (!step.Check(await Shell.ShowsAsync(export, 600000), "The preview loads and offers to export."))
        {
            return;
        }

        await export.ClickAsync();
        ILocator dialog = Shell.Page.GetByRole(AriaRole.Dialog);
        ILocator anyway = dialog.GetByRole(AriaRole.Button, new() { Name = "Export anyway", Exact = true });
        if (!step.Check(await Shell.ShowsAsync(anyway, 30000), "The export asks first: the audit has errors."))
        {
            await Shot(step, "G3-6-no-gate");
            return;
        }

        step.Note($"The gate says: {(await dialog.InnerTextAsync()).Replace('\n', ' ')}");
        string[] listed = (await dialog.GetByTestId("export-audit-errors").Locator("li").AllInnerTextsAsync()).Select(line => line.Trim()).ToArray();
        step.Check(guessFloorError != null && listed.Contains(guessFloorError), $"The gate lists G3.2's floor type error ({listed.Length} error(s) listed).");
        await Shot(step, "G3-6-gate");
        await dialog.GetByRole(AriaRole.Checkbox).CheckAsync();
        await anyway.ClickAsync();
        step.Check(await FinishExport(step), "Export anyway exports.");
    }
}

internal sealed record InstanceRow(long Id, string Class, long LevelId, bool? InPlace, bool Demolished, string? DesignOption, long? SymbolId = null, string? Family = null, string? Type = null);
