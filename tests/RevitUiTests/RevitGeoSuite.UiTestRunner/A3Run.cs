using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The A3 check (/mnt/project-files/revit-template-review/a3-ui-scenario.md, RevitGeoSuite #45): the
/// GeoPackage export writes elevator levels_served, door fields and the GDB-owned rows of
/// gis-fields.json. Two exports of the "Copy 1" plan views, per level, in shared coordinates: "span"
/// with E's gis_levels_served empty, then "override" with two level names and "ZZ" in it and the test
/// door filled. Step ids A3.*; the pass rules A to F are steps A3.A to A3.F.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const string CopyViews = "Copy 1";
    private const int SharedEpsg = 6677;
    private static readonly string[] A3Parameters = { "gis_levels_served", "gis_door", "gis_access_con", "name" };

    private A3Case a3 = new();

    private async Task A3Steps()
    {
        await Step("A3.0", "Set up model (it binds the new parameters)", EnsureSetUp);
        await Step("A3.A", "Rule A: gis_levels_served an instance parameter; gis_door and gis_access_con type parameters; name includes Doors", A3Bindings);
        await Step("A3.P", "Export 'probe' with the span settings, to find the elements the export makes elevator units of", step => A3Export(step, "probe"));
        await Step("A3.1", "List the probe's elevator elements by shaft; E is the lowest object of the first shaft on two or more levels", A3Elevators);
        await Step("A3.2", "Export 'span': GeoPackage per level, the Copy 1 views, shared coordinates", step => A3Export(step, "span"));
        await Step("A3.3", "Pick the test door from the span export; fill it, and set E's gis_levels_served to two levels and ZZ", A3Fill);
        await Step("A3.4", "Export 'override' with the same settings", step => A3Export(step, "override"));
        await Step("A3.B", "Rule B (span): each shaft's rows share its levels lowest first; a lone object is empty with a warning", A3Span);
        await Step("A3.C", "Rule C (override): every row of E's shaft holds the two named levels, lowest first, other shafts keep their span; one warning names ZZ", A3Override);
        await Step("A3.D", "Rule D (override): the test door's row has automatic / test / A3 test door; only its type's doors have values", A3Door);
        await Step("A3.E", "Rule E: no warning about a missing door, access_con or name", A3NoMissing);
        await Step("A3.F", "Rule F: gis-fields.json has the 7 rows as specified", A3Fields);
    }

    private Task A3Bindings(StepResult step)
    {
        JsonArray rows = host.Call("bindings", new { names = A3Parameters })["bindings"]!.AsArray();
        foreach (JsonNode? row in rows)
        {
            step.Note($"{row!["name"]}: {row["binding"] ?? "not bound"} on {row["categories"]?.ToJsonString() ?? "-"}");
        }

        string?[] Categories(string name) => rows.First(row => (string?)row!["name"] == name)!["categories"]?.AsArray().Select(item => (string?)item).ToArray() ?? Array.Empty<string?>();
        string? Binding(string name) => (string?)rows.First(row => (string?)row!["name"] == name)!["binding"];
        step.Check(Binding("gis_levels_served") == "instance" && new[] { "OST_GenericModel", "OST_SpecialityEquipment", "OST_MechanicalEquipment" }.All(category => Categories("gis_levels_served").Contains(category)),
            "gis_levels_served is an instance parameter on Generic Models, Specialty Equipment and Mechanical Equipment.");
        step.Check(Binding("gis_door") == "type" && Categories("gis_door").Contains("OST_Doors"), "gis_door is a type parameter on Doors.");
        step.Check(Binding("gis_access_con") == "type" && Categories("gis_access_con").Contains("OST_Doors"), "gis_access_con is a type parameter on Doors.");
        step.Check(Binding("name") == "instance" && Categories("name").Contains("OST_Doors"), "name is an instance parameter whose categories include Doors.");
        return Task.CompletedTask;
    }

    private Task A3Elevators(StepResult step)
    {
        // The export finds elevators through gis_kind/gis_category and its built-in family catalog (LUMINE's
        // "j EV" is one), and any kind of element can be an elevator unit, so the list comes from the
        // export's own elevator rows: each row's revit_key names the element.
        if (!a3.Exports.TryGetValue("probe", out A3Output? probe))
        {
            step.NotRun("The probe export did not finish.");
            return Task.CompletedTask;
        }

        string[] uniqueIds = ElevatorUnits(probe.Folder).Select(A3Fixture.UniqueId).Where(id => id.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        JsonObject reply = host.Call("elevators", new { uniqueIds });
        a3.Elevators = reply["elevators"].Deserialize<ElevatorRow[]>(Web) ?? Array.Empty<ElevatorRow>();
        JsonArray missing = reply["missing"]?.AsArray() ?? new JsonArray();
        step.Note($"The probe export has elevator units for {uniqueIds.Length} element(s); {a3.Elevators.Length} found in the model{(missing.Count > 0 ? $", not found: {missing.ToJsonString()}" : string.Empty)}.");
        if (!step.Check(a3.Elevators.Length > 0, $"The export makes elevator units of elements of this model ({a3.Elevators.Length})."))
        {
            return Task.CompletedTask;
        }

        PlanBox[] boxes = a3.Elevators.Select(elevator => elevator.Box).ToArray();
        a3.Shafts = A3Fixture.Shafts(boxes).Select(group => group.Select(index => a3.Elevators[index]).ToArray()).ToArray();
        for (int shaft = 0; shaft < a3.Shafts.Length; shaft++)
        {
            foreach (ElevatorRow elevator in a3.Shafts[shaft].OrderBy(item => item.ZMinM))
            {
                step.Note($"Shaft {shaft + 1}: elevator {elevator.Id}, {elevator.Family} / {elevator.Type} (type {elevator.TypeId}, {elevator.Category}), level {elevator.Level}, "
                    + $"level ProjectElevation {Metres(elevator.LevelElevationFt * 0.3048)}, X {Metres(elevator.XMinM)} to {Metres(elevator.XMaxM)}, Y {Metres(elevator.YMinM)} to {Metres(elevator.YMaxM)}, Z {Metres(elevator.ZMinM)} to {Metres(elevator.ZMaxM)}, gis_levels_served '{elevator.LevelsServed}'.");
            }
        }

        foreach ((int first, int second) in A3Fixture.SameLevelOverlaps(boxes, a3.Elevators.Select(elevator => elevator.Level).ToArray()))
        {
            step.Note($"FLAG: elevators {a3.Elevators[first].Id} and {a3.Elevators[second].Id} are both on {a3.Elevators[first].Level} and their plan boxes overlap: two cars may have been joined into one shaft.");
        }

        a3.EShaft = a3.Shafts.FirstOrDefault(shaft => shaft.Select(elevator => elevator.LevelElementId).Distinct().Count() >= 2);
        if (!step.Check(a3.EShaft != null, $"A shaft has objects on two or more levels ({a3.Shafts.Length} shaft(s), {a3.Shafts.Count(shaft => shaft.Length == 1)} lone object(s))."))
        {
            return Task.CompletedTask;
        }

        a3.E = a3.EShaft!.OrderBy(elevator => elevator.ZMinM).First();
        HashSet<long> types = a3.EShaft!.Select(elevator => elevator.TypeId).ToHashSet();
        int[] shared = Enumerable.Range(0, a3.Shafts.Length).Where(index => a3.Shafts[index] != a3.EShaft && a3.Shafts[index].Any(elevator => elevator.TypeId == a3.E.TypeId)).Select(index => index + 1).ToArray();
        step.Note($"E = elevator {a3.E.Id} ({a3.E.Family} / {a3.E.Type}, level {a3.E.Level}), the lowest of shaft {Array.IndexOf(a3.Shafts, a3.EShaft) + 1} ({a3.EShaft!.Length} object(s)).");
        step.Note(shared.Length == 0 ? "No other shaft uses E's type." : $"Shaft(s) {string.Join(", ", shared)} also use E's type, so the override reaches them too.");
        var empty = a3.EShaft!.Where(elevator => !string.IsNullOrEmpty(elevator.LevelsServed))
            .Select(elevator => new { id = elevator.Id, name = "gis_levels_served", value = string.Empty }).ToArray();
        if (empty.Length > 0)
        {
            JsonObject fixture = host.Call("fixture", new { values = empty });
            step.Note($"Emptied gis_levels_served on {empty.Length} element(s) of E's shaft. Fixture: {fixture["log"]?.ToJsonString()}.");
        }

        return Task.CompletedTask;
    }

    private async Task A3Export(StepResult step, string name)
    {
        string output = Path.Combine(options.OutputFolder, "a3", name);
        await Shell.GoToAsync("/export/geopackage");
        ILocator filter = Shell.Page.GetByPlaceholder("Filter views...");
        await filter.WaitForAsync();
        await filter.FillAsync(CopyViews);
        await Shell.Button("Clear all").ClickAsync();
        ILocator boxes = Shell.Page.GetByRole(AriaRole.Checkbox, new() { NameRegex = new Regex(Regex.Escape(CopyViews)) });
        int views = await boxes.CountAsync();
        for (int index = 0; index < views; index++)
        {
            await boxes.Nth(index).CheckAsync();
        }

        step.Note($"Ticked {views} '{CopyViews}' view(s): {string.Join(", ", (await boxes.AllInnerTextsAsync()).Select(text => text.Trim()))}");
        step.Check(views > 0, $"There are '{CopyViews}' plan views to export (the LUMINE run had 11).");
        step.Check(await SetExportOption("Packaging", "perLevel"), "Packaging is 'Per level GeoPackage'.");
        step.Check(await SetExportOption("Coordinates", "shared"), "Coordinates are 'Shared coordinates'.");
        step.Check(await SetExportOption("Target EPSG", SharedEpsg.ToString(CultureInfo.InvariantCulture)), $"The EPSG is {SharedEpsg}.");
        await SetOutputFolder(step, output);
        await Shell.Button("Preview").ClickAsync();
        bool exported = await ExportNow(step);
        string[] warnings = exported ? (await Shell.Page.Locator("#export-warning-list li").AllInnerTextsAsync()).Select(text => text.Trim()).ToArray() : Array.Empty<string>();
        await Shot(step, $"A3-{name}-export");
        await Shell.GoToAsync("/gis-standard");
        if (!exported)
        {
            return;
        }

        a3.Exports[name] = new A3Output(output, warnings);
        string[] elevator = warnings.Where(A3Fixture.IsElevatorWarning).ToArray();
        step.Note($"{warnings.Length} warning(s); {elevator.Length} about elevators or levels_served:");
        foreach (string warning in elevator)
        {
            step.Note($"`{warning}`");
        }

        string[] missing = A3Fixture.MissingDoorFieldWarnings(warnings);
        step.Note($"{missing.Length} warning(s) name a missing door, access_con or name{(missing.Length > 0 ? ": " + string.Join(" | ", missing.Take(5)) : ".")}");
        step.Note($"Files: {string.Join(", ", Directory.GetFiles(output, "*", SearchOption.AllDirectories).Select(file => Path.GetRelativePath(output, file)))}");
    }

    private Task A3Fill(StepResult step)
    {
        if (!a3.Exports.TryGetValue("span", out A3Output? span))
        {
            step.NotRun("The span export did not finish.");
            return Task.CompletedTask;
        }

        DoorRow[] doors = host.Call("doors")["doors"].Deserialize<DoorRow[]>(Web) ?? Array.Empty<DoorRow>();
        HashSet<string> exported = A3Fixture.Rows(span.Folder, "opening").Select(A3Fixture.UniqueId).ToHashSet(StringComparer.Ordinal);
        a3.Door = doors.FirstOrDefault(door => exported.Contains(door.UniqueId));
        step.Note($"{doors.Length} door(s) in the model, {doors.Count(door => exported.Contains(door.UniqueId))} written as openings.");
        if (!step.Check(a3.Door != null, "A door is written as an opening in the span export."))
        {
            return Task.CompletedTask;
        }

        a3.Levels = ExportedLevels(span.Folder);
        step.Note($"Exported levels (level_id, elevation): {string.Join(", ", a3.Levels.Select(level => $"{level.Name} = {level.LevelId} ({Metres(level.ElevationFt * 0.3048)})"))}.");
        var values = new List<object>
        {
            new { id = a3.Door!.TypeId, name = "gis_door", value = A3Fixture.TestDoor },
            new { id = a3.Door.TypeId, name = "gis_access_con", value = A3Fixture.TestAccess },
            new { id = a3.Door.Id, name = "name", value = A3Fixture.TestDoorName },
        };
        JsonObject fixture = host.Call("fixture", new { values });
        step.Note($"Test door {a3.Door.Id} ({a3.Door.Family} / {a3.Door.Type}, type {a3.Door.TypeId}). Fixture: {fixture["log"]?.ToJsonString()}.");
        if (a3.E != null && step.Check(a3.Levels.Length >= 2, "The export has at least two levels to name."))
        {
            LevelRow[] named = a3.Levels.OrderBy(level => level.ElevationFt).Take(2).ToArray();
            string value = $"{named[0].Name}, {named[1].Name}, {A3Fixture.UnknownLevelName}";
            try
            {
                JsonObject levels = host.Call("fixture", new { values = new[] { new { id = a3.E.Id, name = "gis_levels_served", value } } });
                a3.Named = named;
                step.Note($"E {a3.E.Id} (instance): gis_levels_served = '{value}'. Fixture: {levels["log"]?.ToJsonString()}.");
            }
            catch (TestHostException ex) when (ex.Message.Contains("has no parameter", StringComparison.Ordinal))
            {
                step.Check(false, $"E {a3.E.Id} ({a3.E.Family} / {a3.E.Type}, category {a3.E.Category}) has no gis_levels_served to fill: Set up binds it to the categories A3.A lists, and {a3.E.Category} is not one of them. A3.C cannot run.");
            }
        }
        step.Note("The door is filled here, after the span export, because only an export says which doors it writes as openings; rules D and E read the override export.");
        return Task.CompletedTask;
    }

    private Task A3Span(StepResult step)
    {
        if (!a3.Exports.TryGetValue("span", out A3Output? span))
        {
            step.NotRun("The span export did not finish.");
            return Task.CompletedTask;
        }

        HashSet<string> exportedLevels = A3Fixture.Rows(span.Folder, "level").Select(row => A3Fixture.Value(row, "id")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        Dictionary<long, LevelRow> levels = Levels().ToDictionary(level => level.Id);
        List<Dictionary<string, string?>> rows = ElevatorUnits(span.Folder);
        var cases = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < a3.Shafts.Length; index++)
        {
            ElevatorRow[] shaft = a3.Shafts[index];
            HashSet<string> members = shaft.Select(elevator => elevator.UniqueId).ToHashSet(StringComparer.Ordinal);
            List<Dictionary<string, string?>> shaftRows = rows.Where(row => members.Contains(A3Fixture.UniqueId(row))).ToList();
            LevelRow[] stops = shaft.Select(elevator => elevator.LevelElementId is long id && levels.TryGetValue(id, out LevelRow? level) ? level : null)
                .OfType<LevelRow>().Where(level => level.LevelId != null && exportedLevels.Contains(level.LevelId))
                .DistinctBy(level => level.LevelId).OrderBy(level => level.ElevationFt).ToArray();
            string expected = stops.Length >= 2 ? string.Join(",", stops.Select(level => level.LevelId)) : string.Empty;
            string[] values = shaftRows.Select(row => A3Fixture.Value(row, "levels_served") ?? string.Empty).Distinct(StringComparer.Ordinal).ToArray();
            string label = $"Shaft {index + 1} ({string.Join(", ", shaft.Select(elevator => elevator.Id))}; {shaftRows.Count} row(s))";
            if (shaftRows.Count == 0)
            {
                step.Note($"{label}: no elevator unit rows in the span export.");
                continue;
            }

            step.Check(values.Length == 1, $"{label}: every row has the same levels_served ({string.Join(" | ", values.Select(value => $"'{value}'"))}).");
            if (stops.Length >= 2)
            {
                if (!step.Check(values.All(value => value.Replace(" ", string.Empty, StringComparison.Ordinal) == expected), $"{label}: levels_served is its exported levels lowest first, '{expected}' ({string.Join(", ", stops.Select(level => level.Name))})."))
                {
                    // The export's Z rule compares each object's box with its levels' ProjectElevation; show both sides.
                    foreach (ElevatorRow elevator in shaft.OrderBy(item => item.ZMinM))
                    {
                        LevelRow? host = elevator.LevelElementId is long id ? levels.GetValueOrDefault(id) : null;
                        step.Note($"{label}: elevator {elevator.Id} Z {Metres(elevator.ZMinM)} to {Metres(elevator.ZMaxM)}; host level {elevator.Level} ({host?.LevelId ?? "no level_id"}) at ProjectElevation {Metres(host?.ElevationFt * 0.3048)}.");
                    }
                }
                cases["shaft"] = cases.GetValueOrDefault("shaft") + shaftRows.Count;
            }
            else
            {
                step.Check(values.All(value => value.Length == 0), $"{label}: a shaft on fewer than two exported levels has empty levels_served.");
                foreach (ElevatorRow elevator in shaft)
                {
                    step.Check(span.Warnings.Any(warning => warning.StartsWith($"Elevator {elevator.Id} ", StringComparison.Ordinal) && warning.Contains("has no elevator object in the same plan spot on another exported level", StringComparison.Ordinal)),
                        $"Elevator {elevator.Id}: the export warns it has no elevator object in the same plan spot on another exported level.");
                }

                cases["lone"] = cases.GetValueOrDefault("lone") + shaftRows.Count;
            }
        }

        HashSet<string> known = a3.Elevators.Select(elevator => elevator.UniqueId).ToHashSet(StringComparer.Ordinal);
        int unmatched = rows.Count(row => !known.Contains(A3Fixture.UniqueId(row)));
        step.Note($"Elevator rows: {rows.Count}; in shafts on two or more levels {cases.GetValueOrDefault("shaft")}, lone {cases.GetValueOrDefault("lone")}, not matched to an elevator object {unmatched}; distinct levels_served values {rows.Select(row => A3Fixture.Value(row, "levels_served") ?? string.Empty).Distinct().Count()}.");
        step.Check(rows.Count > 0, "The span export has elevator unit rows.");
        return Task.CompletedTask;
    }

    private Task A3Override(StepResult step)
    {
        if (!a3.Exports.TryGetValue("override", out A3Output? export) || a3.E == null || a3.EShaft == null || a3.Named == null)
        {
            step.NotRun("The override export did not finish, or there was no E or no two levels to name.");
            return Task.CompletedTask;
        }

        string expected = $"{a3.Named[0].LevelId},{a3.Named[1].LevelId}";
        HashSet<string> members = a3.EShaft.Select(elevator => elevator.UniqueId).ToHashSet(StringComparer.Ordinal);
        List<Dictionary<string, string?>> rows = ElevatorUnits(export.Folder);
        List<Dictionary<string, string?>> mine = rows.Where(row => members.Contains(A3Fixture.UniqueId(row))).ToList();
        if (step.Check(mine.Count > 0, $"E's shaft has elevator unit rows ({mine.Count})."))
        {
            foreach (Dictionary<string, string?> row in mine)
            {
                string? value = A3Fixture.Value(row, "levels_served");
                step.Check(value?.Replace(" ", string.Empty, StringComparison.Ordinal) == expected, $"Unit {A3Fixture.Value(row, "id")} has levels_served '{expected}' ({a3.Named[0].Name}, {a3.Named[1].Name}); it has '{value}'.");
            }
        }

        // Every other shaft keeps the value the span export gave it: the override is on E's instance only.
        if (a3.Exports.TryGetValue("span", out A3Output? span))
        {
            Dictionary<string, string> before = ElevatorUnits(span.Folder)
                .GroupBy(row => $"{A3Fixture.Value(row, "revit_key")}|{A3Fixture.Value(row, "id")}")
                .ToDictionary(group => group.Key, group => A3Fixture.Value(group.First(), "levels_served") ?? string.Empty);
            string[] changed = rows.Where(row => !members.Contains(A3Fixture.UniqueId(row)))
                .Where(row => before.TryGetValue($"{A3Fixture.Value(row, "revit_key")}|{A3Fixture.Value(row, "id")}", out string? was) && was != (A3Fixture.Value(row, "levels_served") ?? string.Empty))
                .Select(row => $"{A3Fixture.Value(row, "id")} '{before[$"{A3Fixture.Value(row, "revit_key")}|{A3Fixture.Value(row, "id")}"]}' -> '{A3Fixture.Value(row, "levels_served")}'")
                .ToArray();
            step.Check(changed.Length == 0, $"Every other shaft's rows keep their span levels_served ({rows.Count - mine.Count} row(s); changed: {(changed.Length == 0 ? "none" : string.Join("; ", changed))}).");
        }

        string[] zz = export.Warnings.Where(warning => warning.Contains("match no level name or short_name", StringComparison.Ordinal) && Regex.IsMatch(warning, $@"\b{A3Fixture.UnknownLevelName}\b")).ToArray();
        step.Check(zz.Length == 1, $"Exactly one warning says {A3Fixture.UnknownLevelName} matches no level name or short_name ({zz.Length}: {string.Join(" | ", zz)}).");
        static bool Vertical(string warning) => warning.Contains("Vertical subtraction", StringComparison.Ordinal);
        string[] others = export.Warnings.Where(A3Fixture.IsElevatorWarning).Except(zz).Where(warning => !Vertical(warning)).ToArray();
        int vertical = export.Warnings.Count(Vertical);
        step.Check(others.Length == 0 && vertical == 2,
            $"Besides the ZZ warning, the elevator warnings are the 2 'Vertical subtraction' ones ({vertical} of those; others: {(others.Length == 0 ? "none" : string.Join(" | ", others))}).");
        return Task.CompletedTask;
    }

    private Task A3Door(StepResult step)
    {
        if (!a3.Exports.TryGetValue("override", out A3Output? export) || a3.Door == null)
        {
            step.NotRun("The override export did not finish, or no test door was picked.");
            return Task.CompletedTask;
        }

        DoorRow[] doors = host.Call("doors")["doors"].Deserialize<DoorRow[]>(Web) ?? Array.Empty<DoorRow>();
        Dictionary<string, DoorRow> byUniqueId = doors.ToDictionary(door => door.UniqueId, StringComparer.Ordinal);
        List<Dictionary<string, string?>> openings = A3Fixture.Rows(export.Folder, "opening");
        List<Dictionary<string, string?>> mine = openings.Where(row => A3Fixture.UniqueId(row) == a3.Door.UniqueId).ToList();
        if (step.Check(mine.Count > 0, $"The test door {a3.Door.Id} has opening rows ({mine.Count})."))
        {
            foreach (Dictionary<string, string?> row in mine)
            {
                step.Check(A3Fixture.Value(row, "door") == A3Fixture.TestDoor && A3Fixture.Value(row, "access_con") == A3Fixture.TestAccess && A3Fixture.Value(row, "name") == A3Fixture.TestDoorName,
                    $"Opening {A3Fixture.Value(row, "id")}: door '{A3Fixture.Value(row, "door")}', access_con '{A3Fixture.Value(row, "access_con")}', name '{A3Fixture.Value(row, "name")}'.");
            }
        }

        var filled = openings.Where(row => A3Fixture.Value(row, "door") != null || A3Fixture.Value(row, "access_con") != null).ToList();
        var sameType = filled.Where(row => byUniqueId.TryGetValue(A3Fixture.UniqueId(row), out DoorRow? door) && door.TypeId == a3.Door.TypeId).ToList();
        var others = filled.Except(sameType).ToList();
        step.Note($"{filled.Count} opening row(s) have door or access_con; {sameType.Count} of them are doors of the test door's type {a3.Door.Type} (expected: type parameters).");
        step.Check(others.Count == 0, $"No other opening has door or access_con ({string.Join(" | ", others.Take(5).Select(row => $"{A3Fixture.Value(row, "id")}: {A3Fixture.Value(row, "door")}/{A3Fixture.Value(row, "access_con")} ({A3Fixture.UniqueId(row)})"))}).");
        var named = openings.Where(row => A3Fixture.Value(row, "name") != null && A3Fixture.UniqueId(row) != a3.Door.UniqueId).ToList();
        step.Note($"Other openings with a name: {named.Count}{(named.Count > 0 ? ": " + string.Join(", ", named.Take(5).Select(row => A3Fixture.Value(row, "name"))) : string.Empty)}.");
        return Task.CompletedTask;
    }

    private Task A3NoMissing(StepResult step)
    {
        if (a3.Exports.Count == 0)
        {
            step.NotRun("No export finished.");
            return Task.CompletedTask;
        }

        foreach ((string name, A3Output export) in a3.Exports)
        {
            string[] missing = A3Fixture.MissingDoorFieldWarnings(export.Warnings);
            step.Check(missing.Length == 0, $"{name}: no warning about a missing door, access_con or name ({missing.Length}: {string.Join(" | ", missing.Take(5))}).");
        }

        return Task.CompletedTask;
    }

    private Task A3Fields(StepResult step)
    {
        if (a3.Exports.Count == 0)
        {
            step.NotRun("No export finished.");
            return Task.CompletedTask;
        }

        foreach ((string name, A3Output export) in a3.Exports)
        {
            (string? path, JsonArray fields) = A3Fixture.FieldList(export.Folder);
            if (!step.Check(path != null, $"{name}: gis-fields.json is in the export folder."))
            {
                continue;
            }

            foreach ((FieldExpectation expected, string? problem, string seen) in A3Fixture.CheckFields(fields))
            {
                step.Check(problem == null, $"{name}: {expected.Key} ({seen}){(problem == null ? string.Empty : ": " + problem)}.");
            }
        }

        return Task.CompletedTask;
    }

    // The model's levels whose level_id the export's level table holds, lowest first.
    private LevelRow[] ExportedLevels(string folder)
    {
        HashSet<string> ids = A3Fixture.Rows(folder, "level").Select(row => A3Fixture.Value(row, "id")).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return Levels().Where(level => level.LevelId != null && ids.Contains(level.LevelId) && level.ElevationFt != null).OrderBy(level => level.ElevationFt).ToArray();
    }

    private Dictionary<string, double> ElevationById() =>
        Levels().Where(level => !string.IsNullOrEmpty(level.LevelId) && level.ElevationFt != null)
            .GroupBy(level => level.LevelId!)
            .ToDictionary(group => group.Key, group => group.First().ElevationFt!.Value, StringComparer.Ordinal);

    private static List<Dictionary<string, string?>> ElevatorUnits(string folder) =>
        A3Fixture.Rows(folder, "unit").Where(row => A3Fixture.Value(row, "category") == "elevator").ToList();

    // Sets one option on the export page by its label (Packaging, Coordinates, Target EPSG), also when its section is folded.
    private async Task<bool> SetExportOption(string label, string value) =>
        await Shell.Page.EvaluateAsync<bool>(@"([label, value]) => {
            const span = [...document.querySelectorAll('label span')].find(item => item.textContent.trim() === label);
            const field = span?.closest('label')?.querySelector('select, input');
            if (!field) return false;
            const prototype = field instanceof HTMLSelectElement ? HTMLSelectElement.prototype : HTMLInputElement.prototype;
            Object.getOwnPropertyDescriptor(prototype, 'value').set.call(field, value);
            field.dispatchEvent(new Event('input', { bubbles: true }));
            field.dispatchEvent(new Event('change', { bubbles: true }));
            return field.value === value;
        }", new object[] { label, value });
}

internal sealed class A3Case
{
    public ElevatorRow[] Elevators { get; set; } = Array.Empty<ElevatorRow>();

    public ElevatorRow? E { get; set; }

    public ElevatorRow[][] Shafts { get; set; } = Array.Empty<ElevatorRow[]>();

    public ElevatorRow[]? EShaft { get; set; }

    public DoorRow? Door { get; set; }

    public LevelRow[] Levels { get; set; } = Array.Empty<LevelRow>();

    public LevelRow[]? Named { get; set; }

    public Dictionary<string, A3Output> Exports { get; } = new(StringComparer.Ordinal);
}

internal sealed record A3Output(string Folder, string[] Warnings);

internal sealed record ElevatorRow(
    long Id, string UniqueId, long TypeId, string? Family, string? Type, string? Category, string? Level, long? LevelElementId,
    double? XMinM, double? XMaxM, double? YMinM, double? YMaxM, double? ZMinM, double? ZMaxM, string? LevelsServed, double? LevelElevationFt = null)
{
    public PlanBox Box => new(XMinM ?? 0, YMinM ?? 0, XMaxM ?? 0, YMaxM ?? 0);
}

internal sealed record DoorRow(long Id, string UniqueId, long TypeId, string? Type, string? Family);
