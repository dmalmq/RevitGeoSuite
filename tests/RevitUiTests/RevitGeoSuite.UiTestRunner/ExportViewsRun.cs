using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The B1 check (/mnt/project-files/revit-template-review/b1/revit-check.md): Set up model adds the
/// GIS Export view template, a GIS_ plan per floor level, crops and level ids, and the default
/// export takes only the GIS_ plans. Step ids are B1.1 to B1.9.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const string TemplateName = "GIS Export";
    private const string GisPrefix = "GIS_";
    private static readonly string[] ViewCategories = { "OST_Floors", "OST_Walls" };
    private static readonly string[] ThinLines = { "<Thin Lines>", "<細線>" };
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private PlanViewRow[] viewsBefore = Array.Empty<PlanViewRow>();
    private LevelRow[] levelsBefore = Array.Empty<LevelRow>();
    private string[] setupLinesBefore = Array.Empty<string>();
    private bool setupApplied;

    private async Task ExportViewSteps()
    {
        await Step("B1.1", "Baseline: GIS_ plans, levels and the levels that hold floors", Baseline);
        await Step("B1.2", "Set up model lists the template, plans, crops and level ids", ViewSetupPreview);
        await Step("B1.3", "Apply Set up model", ApplyViewSetup);
        await Step("B1.4", "Nothing about views, crops or level ids is left to set up", ViewSetupDone);
        await Step("B1.5", "The template, the new plans and the level ids are right", ViewElements);
        await Step("B1.6", "Two GIS_ plans show what the export reads (eyes)", GisPlanShots);
        await Step("B1.7", "One Undo takes the whole Set up back; Redo", UndoRedoSetup);
        await Step("B1.8", "Export with the default view choice", DefaultExport);
        await Step("B1.9", "Compare with an export from before Set up", CompareBaseline);
    }

    private async Task Baseline(StepResult step)
    {
        viewsBefore = Views();
        levelsBefore = Levels();
        PlanViewRow[] gis = GisPlans(viewsBefore);
        step.Note($"{gis.Length} GIS_ plan(s): {string.Join(", ", gis.Select(view => $"{view.Name} ({view.Level}, crop {(view.CropOn ? "on" : "off")}, template {view.Template ?? "none"})"))}.");
        step.Note($"Template {TemplateName}: {(viewsBefore.Any(IsExportTemplate) ? "exists" : "none")}.");
        step.Note($"{levelsBefore.Length} level(s); with floors: {string.Join(", ", levelsBefore.Where(level => level.HasFloors).Select(level => level.Name))}; without level_id: {levelsBefore.Count(level => string.IsNullOrEmpty(level.LevelId))}.");
        setupLinesBefore = (await Inspect()).SetupLines;
        step.Note($"Set up lines: {string.Join(" / ", setupLinesBefore)}");
    }

    private async Task ViewSetupPreview(StepResult step)
    {
        string[] lines = (await Inspect()).SetupLines;
        bool hasTemplate = viewsBefore.Any(IsExportTemplate);
        bool createsTemplate = lines.Contains($"Create view template {TemplateName}");
        step.Check(createsTemplate == !hasTemplate, hasTemplate ? "No 'Create view template' line: the model has the template." : $"'Create view template {TemplateName}' is listed.");

        HashSet<string> gisLevels = GisPlans(viewsBefore).Select(view => view.Level ?? string.Empty).ToHashSet(StringComparer.Ordinal);
        foreach (LevelRow level in levelsBefore.Where(level => level.HasFloors && !gisLevels.Contains(level.Name)))
        {
            Regex create = new($"^Create plan {Regex.Escape(GisPrefix)}.* for level {Regex.Escape(level.Name)}$");
            step.Check(lines.Any(create.IsMatch), $"A 'Create plan GIS_… for level {level.Name}' line.");
        }

        foreach (PlanViewRow view in GisPlans(viewsBefore).Where(view => !view.CropOn))
        {
            step.Check(lines.Contains($"Crop plan {view.Name} to the floors"), $"'Crop plan {view.Name} to the floors' (its crop is off).");
        }

        step.Note($"Crop lines: {string.Join(" / ", lines.Where(line => line.StartsWith("Crop plan ", StringComparison.Ordinal)))}");
        int missingIds = levelsBefore.Count(level => string.IsNullOrEmpty(level.LevelId));
        if (missingIds > 0)
        {
            step.Check(lines.Any(line => line.StartsWith($"Give {missingIds} level(s) a level_id: ", StringComparison.Ordinal)), $"'Give {missingIds} level(s) a level_id: …'.");
        }

        Match extent = lines.Select(line => Regex.Match(line, @"^The crop holds every floor plus 5 m: about (\d+) m by (\d+) m$")).FirstOrDefault(match => match.Success) ?? Match.Empty;
        if (lines.Any(line => line.StartsWith("Create plan ", StringComparison.Ordinal) || line.StartsWith("Crop plan ", StringComparison.Ordinal)))
        {
            if (step.Check(extent.Success, "'The crop holds every floor plus 5 m: about W m by H m'."))
            {
                int width = int.Parse(extent.Groups[1].Value, CultureInfo.InvariantCulture);
                int height = int.Parse(extent.Groups[2].Value, CultureInfo.InvariantCulture);
                step.Note($"Crop W {width} m, H {height} m.");
                step.Check(width < 1000 && height < 1000, $"The crop is under 1 km each way (over that means a stray floor): {width} m by {height} m.");
            }
        }

        await Shot(step, "B1-2-setup-preview");
    }

    private async Task ApplyViewSetup(StepResult step)
    {
        string[] lines = (await Inspect()).SetupLines;
        if (lines.Length == 0)
        {
            step.NotRun("Nothing to set up on this model.");
            return;
        }

        string[] log = await ApplySetupAndReadLog(step);
        setupApplied = true;
        step.Note($"Log: {string.Join(" / ", log)}");
        if (lines.Contains($"Create view template {TemplateName}"))
        {
            step.Check(log.Contains($"Created view template {TemplateName}"), $"The log says 'Created view template {TemplateName}'.");
        }

        foreach (string line in lines)
        {
            Match create = Regex.Match(line, "^Create plan (.+) for level ");
            if (create.Success)
            {
                step.Check(log.Contains($"Created plan {create.Groups[1].Value}"), $"The log says 'Created plan {create.Groups[1].Value}'.");
            }

            if (line.StartsWith("Crop plan ", StringComparison.Ordinal))
            {
                step.Check(log.Contains("Cropped" + line["Crop".Length..]), $"The log says 'Cropped{line["Crop".Length..]}'.");
            }

            Match ids = Regex.Match(line, @"^Give (\d+) level\(s\) a level_id");
            if (ids.Success)
            {
                step.Check(log.Contains($"Gave {ids.Groups[1].Value} level(s) a level_id"), $"The log says 'Gave {ids.Groups[1].Value} level(s) a level_id'.");
            }
        }

        await Shot(step, "B1-3-setup-applied");
    }

    private async Task ViewSetupDone(StepResult step)
    {
        await CheckAgain();
        string[] left = (await Inspect()).SetupLines.Where(IsViewSetupLine).ToArray();
        step.Check(left.Length == 0, $"No view, crop or level_id line is left (left: {string.Join(" / ", left)}).");
    }

    private Task ViewElements(StepResult step)
    {
        PlanViewRow[] views = Views();
        PlanViewRow? template = views.FirstOrDefault(IsExportTemplate);
        if (!step.Check(template != null, $"The view template {TemplateName} exists."))
        {
            return Task.CompletedTask;
        }

        step.Check(template!.DetailLevel == "Coarse", $"Detail level Coarse ({template.DetailLevel}).");
        step.Check(template.Discipline == "Coordination", $"Discipline Coordination ({template.Discipline ?? "none"}).");
        step.Check(Near(template.CutPlaneMm, 1200), $"Cut plane 1200 mm ({Mm(template.CutPlaneMm)}).");
        step.Check(Near(template.BottomMm, 0) && template.BottomAtLevel == true, $"Bottom 0 at the level ({Mm(template.BottomMm)}, at level {template.BottomAtLevel}).");
        step.Check(Near(template.DepthMm, 0) && template.DepthAtLevel == true, $"View depth 0 at the level ({Mm(template.DepthMm)}, at level {template.DepthAtLevel}).");

        HashSet<long> before = viewsBefore.Select(view => view.Id).ToHashSet();
        foreach (PlanViewRow plan in GisPlans(views).Where(view => !before.Contains(view.Id)))
        {
            step.Check(plan.Template == TemplateName && plan.CropOn, $"New plan {plan.Name} uses {TemplateName} ({plan.Template ?? "none"}) with crop on ({plan.CropOn}).");
        }

        LevelRow[] levels = Levels();
        string[] missing = levels.Where(level => string.IsNullOrEmpty(level.LevelId)).Select(level => level.Name).ToArray();
        step.Check(missing.Length == 0, $"Every level has a level_id (missing: {string.Join(", ", missing)}).");
        HashSet<long> withFloors = levels.Where(level => level.HasFloors).Select(level => level.Id).ToHashSet();
        string[] uncovered = levels.Where(level => level.HasFloors && !GisPlans(views).Any(view => view.Level == level.Name)).Select(level => level.Name).ToArray();
        step.Check(uncovered.Length == 0, $"Every level with floors has a GIS_ plan (without one: {string.Join(", ", uncovered)}; {withFloors.Count} level(s) hold floors).");
        return Task.CompletedTask;
    }

    private Task GisPlanShots(StepResult step)
    {
        PlanViewRow[] plans = GisPlans(Views()).Take(2).ToArray();
        if (plans.Length == 0)
        {
            step.NotRun("The model has no GIS_ plan.");
            return Task.CompletedTask;
        }

        foreach (PlanViewRow plan in plans)
        {
            host.Call("activateView", new { name = plan.Name });
            RevitShot(step, "B1-6-" + Slug(plan.Name));
        }

        step.Note("Eyes: floors, stairs, doors, columns, escalators and elevators show; annotations, imports and non-GIS_ lines are hidden.");
        return Task.CompletedTask;
    }

    private async Task UndoRedoSetup(StepResult step)
    {
        if (!setupApplied)
        {
            step.NotRun("Set up model was not applied in B1.3.");
            return;
        }

        int seq = host.LastEvent();
        host.Call("undo");
        ExpectEvent(step, seq, "undo", SetupTransaction);
        await CheckAgain();
        string[] back = (await Inspect()).SetupLines;
        step.Check(back.SequenceEqual(setupLinesBefore), $"The step 2 lines are back ({back.Length} line(s), were {setupLinesBefore.Length}).");

        seq = host.LastEvent();
        host.Call("redo");
        ExpectEvent(step, seq, "redo", SetupTransaction);
        await CheckAgain();
        string[] left = (await Inspect()).SetupLines.Where(IsViewSetupLine).ToArray();
        step.Check(left.Length == 0, $"After Redo nothing about views is left to set up (left: {string.Join(" / ", left)}).");
    }

    private string? defaultExport;

    private async Task DefaultExport(StepResult step)
    {
        string output = await OpenExport(step, string.Empty, "export-default-views", keepDefaultViews: true);
        ViewChoice[] choice = await ExportViewChoice();
        string[] ticked = choice.Where(view => view.Checked).Select(view => view.Label).ToArray();
        step.Note($"Preselected: {string.Join(", ", ticked)}.");
        step.Check(ticked.Length > 0 && ticked.All(label => label.StartsWith(GisPrefix, StringComparison.OrdinalIgnoreCase)), "The dialog preselects only GIS_ plans.");
        string[] gisUnticked = choice.Where(view => !view.Checked && view.Label.StartsWith(GisPrefix, StringComparison.OrdinalIgnoreCase)).Select(view => view.Label).ToArray();
        step.Check(gisUnticked.Length == 0, $"Every GIS_ plan is preselected (not: {string.Join(", ", gisUnticked)}).");
        if (!await ExportNow(step))
        {
            return;
        }

        defaultExport = output;
        (int with, int without) = GeoPackageTables.VerticalUnitsToLevel(output);
        step.Note($"Stair and escalator units: {with} with to_level, {without} without.");
        step.Check(with + without > 0, "The export has stair or escalator units.");
        step.Check(without == 0, $"Every stair and escalator unit has a to_level ({without} without).");
        step.Note($"Rows: {Format(GeoPackageTables.RowCounts(output))}");
        await Shot(step, "B1-8-export-done");
        await Shell.GoToAsync("/gis-standard");
    }

    private Task CompareBaseline(StepResult step)
    {
        if (options.BaselineExport.Length == 0 || !Directory.Exists(options.BaselineExport))
        {
            step.NotRun("Pass --baseline-export <folder> with an export of the same views from before Set up (the run 2 build).");
            return Task.CompletedTask;
        }

        if (defaultExport == null)
        {
            step.NotRun("B1.8 made no export.");
            return Task.CompletedTask;
        }

        Dictionary<string, int> before = GeoPackageTables.RowCounts(options.BaselineExport);
        Dictionary<string, int> after = GeoPackageTables.RowCounts(defaultExport);
        string[] differ = before.Keys.Union(after.Keys)
            .Where(key => before.GetValueOrDefault(key) != after.GetValueOrDefault(key))
            .Select(key => $"{key}: {before.GetValueOrDefault(key)} -> {after.GetValueOrDefault(key)}")
            .ToArray();
        step.Note($"Baseline {options.BaselineExport}, {before.Count} table(s); this run {after.Count} table(s).");
        step.Check(differ.Length == 0, $"Same row count in every feature table (differ: {string.Join("; ", differ)}). A drop means the template hides a category the export reads.");
        return Task.CompletedTask;
    }

    /// <summary>Clicks Apply on the Set up card and returns the change list the banner shows.</summary>
    private async Task<string[]> ApplySetupAndReadLog(StepResult step)
    {
        InspectReply before = await Inspect();
        int seq = host.LastEvent();
        await Shell.Button($"Apply {before.SetupLines.Length} change(s)").ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Text("Set up model applied"), 120000), "Green banner 'Set up model applied'.");
        ExpectEvent(step, seq, "commit", SetupTransaction);
        ILocator changes = Shell.Page.Locator("details")
            .Filter(new() { Has = Shell.Page.Locator("summary", new() { HasTextRegex = new Regex(@"^\s*\d+ change\(s\)\s*$") }) })
            .Locator("li");
        return (await changes.AllTextContentsAsync()).Select(line => line.Trim()).ToArray();
    }

    /// <summary>Clicks Export on the loaded preview, exports anyway when the audit has errors, and waits for the end.</summary>
    private async Task<bool> ExportNow(StepResult step)
    {
        ILocator export = Shell.Button(new Regex(@"^Export (1 view|\d+ views)$"));
        if (!step.Check(await Shell.ShowsAsync(export, 600000), "The preview loads and offers to export."))
        {
            return false;
        }

        await export.ClickAsync();
        ILocator dialog = Shell.Page.GetByRole(AriaRole.Dialog);
        ILocator anyway = dialog.GetByRole(AriaRole.Button, new() { Name = "Export anyway", Exact = true });
        if (await Shell.ShowsAsync(anyway, 5000))
        {
            step.Note($"The audit has errors; exported anyway ({(await dialog.InnerTextAsync()).Split('\n').FirstOrDefault()?.Trim()}).");
            await dialog.GetByRole(AriaRole.Checkbox).CheckAsync();
            await anyway.ClickAsync();
        }

        return await FinishExport(step);
    }

    // The export page's view list: each checkbox's label (view name and level) and whether it is ticked.
    private async Task<ViewChoice[]> ExportViewChoice()
    {
        await Shell.Page.Locator("input[type=checkbox]").First.WaitForAsync(new() { State = WaitForSelectorState.Attached });
        JsonElement rows = await Shell.Page.EvaluateAsync<JsonElement>(@"() => {
            let card = document.querySelector(""input[placeholder='Filter views...']"");
            while (card && card.querySelectorAll('input[type=checkbox]').length === 0) card = card.parentElement;
            if (!card) return [];
            return [...card.querySelectorAll('input[type=checkbox]')].map(box => ({
                label: (box.closest('label')?.innerText ?? box.getAttribute('aria-label') ?? '').trim(),
                checked: box.checked
            }));
        }");
        return rows.Deserialize<ViewChoice[]>(Web) ?? Array.Empty<ViewChoice>();
    }

    private PlanViewRow[] Views() =>
        host.Call("views", new { categories = ViewCategories, lineStyles = ThinLines })["views"].Deserialize<PlanViewRow[]>(Web) ?? Array.Empty<PlanViewRow>();

    private LevelRow[] Levels() => host.Call("levels")["levels"].Deserialize<LevelRow[]>(Web) ?? Array.Empty<LevelRow>();

    private static PlanViewRow[] GisPlans(IEnumerable<PlanViewRow> views) =>
        views.Where(view => !view.IsTemplate && view.Name.StartsWith(GisPrefix, StringComparison.OrdinalIgnoreCase)).ToArray();

    private static bool IsExportTemplate(PlanViewRow view) => view.IsTemplate && view.Name == TemplateName;

    private static bool IsViewSetupLine(string line) =>
        new[] { "Create view template ", "Update view template ", "Create plan ", "Crop plan ", "Give ", "The crop holds " }
            .Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal));

    private static bool Near(double? value, double wanted) => value is double actual && Math.Abs(actual - wanted) < 1;

    private static string Mm(double? value) => value is double actual ? actual.ToString("0", CultureInfo.InvariantCulture) + " mm" : "none";

    private static string Slug(string name) => Regex.Replace(name, @"[^\w]+", "_");

    private static string Format(Dictionary<string, int> counts) => string.Join(", ", counts.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key} {pair.Value}"));
}

internal sealed record PlanViewRow(
    long Id,
    string Name,
    bool IsTemplate,
    string? Level,
    string? Template,
    bool CropOn,
    double? CropWidthM,
    double? CropHeightM,
    string DetailLevel,
    string? Discipline,
    double? CutPlaneMm,
    double? BottomMm,
    double? DepthMm,
    bool? BottomAtLevel,
    bool? DepthAtLevel,
    Dictionary<string, bool?>? Hidden,
    bool? LineStylesHidden)
{
    public bool? CategoryHidden(string category) => Hidden != null && Hidden.TryGetValue(category, out bool? hidden) ? hidden : null;
}

internal sealed record LevelRow(long Id, string Name, string? LevelId, bool HasFloors);

internal sealed record ViewChoice(string Label, bool Checked);
