using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// F6b: a finding's Fix, applied from its row on the Readiness tab, and one Undo for each. The
/// fixture makes each finding when the copy has none (a cleared short_name or gis_kind, a shared
/// level_id, a GIS_ plan without its template) and puts its own change back at the end of the step,
/// in a finally. Only F6b.4 depends on the copy: no fixture can make stairs end on a new level.
/// Step ids F6b.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const string FindingFixTransaction = "Kanizu GIS: Fix";

    private const string ChangedRefusal = "Nothing was changed: the model changed since this fix was shown.";

    private async Task FixSteps()
    {
        await Step("F6b.0", "The model has had Set up model once", EnsureSetUp);
        await Step("F6b.1", "A level without short_name: Fix shows the level name, takes an edited one, and one Undo takes it back", LevelShortNameFix);
        await Step("F6b.2", "A fix shown before the model changed is refused and writes nothing; so are stale writes and a non-whole ordinal sent straight to Revit", StaleFix);
        await Step("F6b.3", "A standard family type without gis_kind: Fix writes the kind it is listed as, and one Undo takes it back", FamilyKindFix);
        await Step("F6b.4", "Two levels sharing level_id: Fix names the level that keeps it, clears it on the other, and one Undo takes it back", SharedLevelIdFix);
        await Step("F6b.5", "A finding Set up model fixes: its Fix opens the Set up row instead of applying anything", SetUpFix);
        await Step("F6b.6", "A level stairs end on without floors or a GIS_ plan: Fix shows the plan Revit will add, adds it, and one Undo takes it away", AddPlanFix);
    }

    private async Task LevelShortNameFix(StepResult step)
    {
        if (ClearShortName(step) is not (LevelRow level, string old))
        {
            return;
        }

        try
        {
            FixItem? fix = await FindFix(step, $"level:{level.Id}");
            FixWriteItem? write = fix?.Writes.FirstOrDefault(entry => entry.Parameter == "short_name");
            if (!step.Check(write is { Editable: true } && write.NewValue == level.Name, $"The fix suggests the level name {level.Name} as short_name, editable (got '{write?.NewValue}')."))
            {
                return;
            }

            await ApplyFix(step, fix!, new Dictionary<string, string> { [$"short_name of {level.Name}"] = "UIT" });
            step.Check(TypeParameter(level.Id, "short_name") == "UIT", $"short_name is the edited UIT (is '{TypeParameter(level.Id, "short_name")}').");
            step.Check(!(await Inspect()).Findings.Any(finding => finding.Fix?.Id == fix!.Id && finding.Fix.Writes.Any(entry => entry.Parameter == "short_name")), "The level no longer lacks short_name.");
            await UndoFindingFix(step);
            step.Check(TypeParameter(level.Id, "short_name") is null or "", $"After one Undo, short_name is empty again (is '{TypeParameter(level.Id, "short_name")}').");
        }
        finally
        {
            PutBack(step, level.Id, "short_name", old, level.Name);
        }
    }

    private async Task StaleFix(StepResult step)
    {
        if (ClearShortName(step) is not (LevelRow level, string old))
        {
            return;
        }

        try
        {
            FixItem? fix = await FindFix(step, $"level:{level.Id}");
            if (fix == null)
            {
                return;
            }

            // The page shows the fix; then the model changes behind it.
            ILocator preview = await OpenFix(step, fix);
            step.Note($"Fixture behind the page: {host.Call("fixture", new { values = new[] { new { id = level.Id, name = "short_name", value = "UIT-OTHER" } } })["log"]?.ToJsonString()}");
            int seq = host.LastEvent();
            await preview.GetByRole(AriaRole.Button, new() { Name = "Apply fix", Exact = true }).ClickAsync();
            step.Check(await Shell.ShowsAsync(Shell.Text("Nothing was changed"), 120000), "Banner 'Nothing was changed'.");
            step.Check(await Shell.ShowsAsync(Shell.Page.GetByText("Nothing was changed: the model", new() { Exact = false }).First), "The banner says the model changed or the finding has gone.");
            step.Check(host.LastEvent() == seq, "Nothing was committed.");
            step.Check(TypeParameter(level.Id, "short_name") == "UIT-OTHER", $"short_name is still the value typed behind the page (is '{TypeParameter(level.Id, "short_name")}').");
            await Shot(step, "F6b-stale");

            // Straight to Revit: writes whose old value is not the model's, and a non-whole ordinal.
            host.Call("fixture", new { values = new[] { new { id = level.Id, name = "short_name", value = string.Empty } } });
            fix = await FindFix(step, $"level:{level.Id}");
            if (fix == null)
            {
                return;
            }

            object Request(Func<FixWriteItem, FixWriteItem> change) => new { fixId = fix.Id, writes = fix.Writes.Select(change).ToArray(), preview = fix.Preview ?? Array.Empty<string>() };
            seq = host.LastEvent();
            ApplyReply stale = await Shell.RpcAsync<ApplyReply>("gisStandard.applyFix", Request(write => write with { OldValue = write.OldValue + "x" }));
            step.Check(stale.Log.Length == 0 && stale.Skipped.Any(text => text.StartsWith(ChangedRefusal, StringComparison.Ordinal)), $"Writes with another old value are refused as changed (got {JsonSerializer.Serialize(stale)}).");
            ApplyReply hidden = await Shell.RpcAsync<ApplyReply>("gisStandard.applyFix", new { fixId = fix.Id, writes = fix.Writes.Take(fix.Writes.Length - 1).ToArray(), preview = fix.Preview ?? Array.Empty<string>() });
            step.Check(hidden.Log.Length == 0 && hidden.Skipped.Any(text => text.StartsWith(ChangedRefusal, StringComparison.Ordinal)), $"A write the user was not shown is refused (got {JsonSerializer.Serialize(hidden)}).");
            if (fix.Writes.Any(write => write.WholeNumber))
            {
                ApplyReply word = await Shell.RpcAsync<ApplyReply>("gisStandard.applyFix", Request(write => write.WholeNumber ? write with { NewValue = "B1" } : write));
                step.Check(word.Log.Length == 0 && word.Skipped.Any(text => text.Contains("takes a whole number, not 'B1'", StringComparison.Ordinal)), $"A non-whole ordinal is refused (got {JsonSerializer.Serialize(word)}).");
            }
            else
            {
                step.Note($"{level.Name} has an ordinal, so its fix has no ordinal to send as a word; GisFixTests covers that refusal.");
            }

            step.Check(host.LastEvent() == seq, "None of them committed anything.");
            step.Check(TypeParameter(level.Id, "short_name") is null or "", "short_name is still empty.");
        }
        finally
        {
            PutBack(step, level.Id, "short_name", old, level.Name);
        }
    }

    private async Task FamilyKindFix(StepResult step)
    {
        Dictionary<string, string> listed = ListedFamilies();
        InstanceRow? instance = FamilyInstances().FirstOrDefault(row => row.SymbolId != null && row.Family != null && listed.ContainsKey(row.Family));
        if (instance == null)
        {
            // The copy has no standard family: the fixture loads one by a listed name, with no gis_kind.
            string family = listed.Keys.First(name => name == "j EV" || listed[name] == "elevator");
            string? level = GisPlans(Views()).Select(view => view.Level).FirstOrDefault(name => name != null);
            step.Note($"Fixture: no listed family on the copy; placing {family}: {host.Call("placeFamily", new { path = Path.Combine(options.OutputFolder, family + ".rfa"), category = "OST_GenericModel", level, x = 0.0, y = 0.0 })["log"]?.ToJsonString()}");
            instance = FamilyInstances().FirstOrDefault(row => row.Family == family && row.SymbolId != null);
        }

        if (!step.Check(instance != null, "A type of a listed family is placed on the copy."))
        {
            return;
        }

        long typeId = instance!.SymbolId!.Value;
        string old = TypeParameter(typeId, "gis_kind") ?? string.Empty;
        step.Note($"Fixture: {host.Call("fixture", new { values = new[] { new { id = typeId, name = "gis_kind", value = string.Empty } } })["log"]?.ToJsonString()} ({instance.Family} : {instance.Type}, gis_kind was '{old}').");
        try
        {
            FixItem? fix = await FindFix(step, $"kind:{typeId}");
            FixWriteItem? write = fix?.Writes.SingleOrDefault();
            if (!step.Check(write != null && write.NewValue == listed[instance.Family!], $"The fix writes gis_kind {listed[instance.Family!]}, the kind {instance.Family} is listed as (got '{write?.NewValue}')."))
            {
                return;
            }

            await ApplyFix(step, fix!, new Dictionary<string, string>());
            step.Check(TypeParameter(typeId, "gis_kind") == write!.NewValue, $"gis_kind is {write.NewValue} (is '{TypeParameter(typeId, "gis_kind")}').");
            step.Check(!(await Inspect()).Findings.Any(finding => finding.Fix?.Id == fix!.Id), "The finding is gone.");
            await UndoFindingFix(step);
            step.Check((TypeParameter(typeId, "gis_kind") ?? string.Empty) == string.Empty, "After one Undo, gis_kind is empty again.");
        }
        finally
        {
            PutBack(step, typeId, "gis_kind", old, $"{instance.Family} : {instance.Type}");
        }
    }

    private async Task SharedLevelIdFix(StepResult step)
    {
        LevelRow[] levels = Levels().Where(row => GisPlanLevelIds().Contains(row.Id) && !string.IsNullOrEmpty(row.LevelId)).Take(2).ToArray();
        if (!step.Check(levels.Length == 2, "Two levels with a GIS_ plan have a level_id."))
        {
            return;
        }

        (LevelRow first, LevelRow second) = (levels[0], levels[1]);
        step.Note($"Fixture: {host.Call("fixture", new { values = new[] { new { id = second.Id, name = "level_id", value = first.LevelId } } })["log"]?.ToJsonString()}");
        try
        {
            FixItem? fix = await FindFix(step, $"levelid:{first.LevelId}");
            if (fix == null)
            {
                return;
            }

            long cleared = fix.Writes.Single().ElementId;
            string keeper = cleared == first.Id ? second.Name : first.Name;
            step.Check(fix.Preview?.SequenceEqual(new[] { $"{keeper} keeps level_id {first.LevelId}." }) == true, $"The fix says {keeper} keeps the id (got {JsonSerializer.Serialize(fix.Preview)}).");
            await ApplyFix(step, fix, new Dictionary<string, string>(), $"{keeper} keeps level_id {first.LevelId}.");
            step.Check(TypeParameter(cleared, "level_id") is null or "", $"level_id is cleared on {fix.Writes.Single().ElementName}.");
            step.Check(new[] { first.Id, second.Id }.Count(id => TypeParameter(id, "level_id") == first.LevelId) == 1, $"One of the two levels keeps {first.LevelId}.");
            await UndoFindingFix(step);
            step.Check(TypeParameter(cleared, "level_id") == first.LevelId, $"After one Undo, both levels share {first.LevelId} again.");
        }
        finally
        {
            PutBack(step, second.Id, "level_id", second.LevelId ?? string.Empty, second.Name);
        }
    }

    private async Task SetUpFix(StepResult step)
    {
        // A GIS_ plan without its template is a finding Set up model fixes.
        PlanViewRow? plan = GisPlans(Views()).FirstOrDefault(view => view.Template == TemplateName);
        if (!step.Check(plan != null, $"A GIS_ plan has the {TemplateName} template."))
        {
            return;
        }

        step.Note($"Fixture: {host.Call("fixture", new { views = new[] { new { view = plan!.Name, template = "none" } } })["log"]?.ToJsonString()}");
        try
        {
            await CheckAgain();
            FindingItem? finding = (await Inspect()).Findings.FirstOrDefault(item => item.Fix?.Kind == "setup");
            if (!step.Check(finding != null, "A finding offers Set up model as its fix."))
            {
                return;
            }

            await ShowFixButtons();
            int seq = host.LastEvent();
            await Shell.Page.Locator("[data-fix='setup']").First.ClickAsync();
            step.Check(await Shell.Page.GetByTestId("readiness-setup").Locator("button[aria-expanded='true']").CountAsync() > 0, "The Set up row is open.");
            step.Check(await Shell.Page.Locator("[data-testid='fix-setup']").CountAsync() == 0, "No fix preview opened.");
            step.Check(host.LastEvent() == seq, "Nothing was applied.");
        }
        finally
        {
            step.Note($"Put back: {host.Call("fixture", new { views = new[] { new { view = plan!.Name, template = TemplateName } } })["log"]?.ToJsonString()}");
        }
    }

    private async Task AddPlanFix(StepResult step)
    {
        await CheckAgain();
        FixItem? fix = (await Inspect()).Findings.Select(finding => finding.Fix).FirstOrDefault(candidate => candidate?.Kind == "addPlan");
        if (fix == null)
        {
            // Only the copy can give this finding: no fixture makes stairs end on a level without floors.
            step.NotRun("No stair or escalator on this copy ends on a level that has no floors and no GIS_ plan. The plan itself is made by the same code as Set up model's plans (gis-standard step 3).");
            return;
        }

        string[] lines = fix.Preview ?? Array.Empty<string>();
        step.Check(lines.Any(line => line.StartsWith("Create plan GIS_", StringComparison.Ordinal) && line.EndsWith($"for level {fix.LevelName}", StringComparison.Ordinal)), $"The fix names the plan it adds (got {JsonSerializer.Serialize(lines)}).");
        int before = GisPlans(Views()).Count();
        bool undone = false;
        try
        {
            await ApplyFix(step, fix, new Dictionary<string, string>(), lines);
            string? name = lines.Select(line => line.Split(' ')).Where(words => words.Length > 2 && words[0] == "Create" && words[1] == "plan").Select(words => words[2]).FirstOrDefault();
            step.Check(GisPlans(Views()).Any(view => view.Level == fix.LevelName && (name == null || view.Name.StartsWith(name, StringComparison.Ordinal))), $"Level {fix.LevelName} has the GIS_ plan the fix named.");
            await UndoFindingFix(step);
            undone = true;
            step.Check(GisPlans(Views()).Count() == before, $"After one Undo, the GIS_ plans are the {before} there were.");
        }
        finally
        {
            if (!undone && GisPlans(Views()).Count() != before)
            {
                host.PostCommand("undo");
                step.Note("Put back: undid the added plan.");
            }
        }
    }

    // A level with a GIS_ plan and a short_name, with its short_name cleared; null when there is none.
    private (LevelRow Level, string Old)? ClearShortName(StepResult step)
    {
        LevelRow? level = Levels().Where(row => GisPlanLevelIds().Contains(row.Id)).FirstOrDefault(row => !string.IsNullOrEmpty(TypeParameter(row.Id, "short_name")));
        if (!step.Check(level != null, "A level with a GIS_ plan has a short_name to clear."))
        {
            return null;
        }

        string old = TypeParameter(level!.Id, "short_name")!;
        step.Note($"Fixture: {host.Call("fixture", new { values = new[] { new { id = level.Id, name = "short_name", value = string.Empty } } })["log"]?.ToJsonString()}");
        return (level, old);
    }

    private void PutBack(StepResult step, long id, string parameter, string value, string what)
    {
        host.Call("fixture", new { values = new[] { new { id, name = parameter, value } } });
        step.Note($"Fixture put back: {parameter} '{value}' on {what}.");
    }

    private InstanceRow[] FamilyInstances() =>
        new[] { "OST_GenericModel", "OST_SpecialityEquipment", "OST_MechanicalEquipment" }.SelectMany(Instances).ToArray();

    // Family name to kind, from the standard's own families.json in this checkout.
    private static Dictionary<string, string> ListedFamilies()
    {
        string? folder = AppContext.BaseDirectory;
        while (folder != null && !File.Exists(Path.Combine(folder, "standards", "families.json")))
        {
            folder = Path.GetDirectoryName(folder);
        }

        if (folder == null)
        {
            throw new FileNotFoundException("standards/families.json is not above the runner.");
        }

        Dictionary<string, string> listed = new(StringComparer.Ordinal);
        foreach (JsonNode? kind in JsonNode.Parse(File.ReadAllText(Path.Combine(folder, "standards", "families.json")))!["kinds"]!.AsArray())
        {
            foreach (JsonNode? family in kind!["families"]!.AsArray())
            {
                listed[family!.GetValue<string>()] = kind["kind"]!.GetValue<string>();
            }
        }

        return listed;
    }

    // Opens every row and every long list, so each finding's Fix button is on the page.
    private async Task ShowFixButtons()
    {
        await CheckAgain();
        await OpenAllReadinessRows();
        foreach (ILocator more in await Shell.Page.GetByRole(AriaRole.Button, new() { NameRegex = new System.Text.RegularExpressions.Regex(@"^Show all \d+$") }).AllAsync())
        {
            await more.ClickAsync();
        }
    }

    private async Task<FixItem?> FindFix(StepResult step, string id)
    {
        await CheckAgain();
        FixItem? fix = (await Inspect()).Findings.Select(finding => finding.Fix).FirstOrDefault(candidate => candidate?.Id == id);
        step.Check(fix != null, $"The audit offers fix {id}.");
        return fix;
    }

    // Opens the fix's preview from its row's Fix button.
    private async Task<ILocator> OpenFix(StepResult step, FixItem fix)
    {
        await ShowFixButtons();
        await Shell.Page.Locator($"[data-fix='{fix.Id}']").First.ClickAsync();
        ILocator preview = Shell.Page.GetByTestId($"fix-{fix.Id}");
        step.Check(await Shell.ShowsAsync(preview), $"Fix {fix.Id} shows what it does first.");
        return preview;
    }

    // Opens the fix's preview, checks its lines, fills the edited values by their labels, applies it and waits for its commit.
    private async Task<ILocator> ApplyFix(StepResult step, FixItem fix, Dictionary<string, string> edits, params string[] previewLines)
    {
        ILocator preview = await OpenFix(step, fix);
        foreach (string line in previewLines)
        {
            step.Check(await preview.GetByText(line, new() { Exact = true }).CountAsync() == 1, $"The preview says '{line}'.");
        }

        foreach ((string label, string value) in edits)
        {
            await preview.GetByLabel(label, new() { Exact = true }).FillAsync(value);
        }

        int seq = host.LastEvent();
        await preview.GetByRole(AriaRole.Button, new() { Name = "Apply fix", Exact = true }).ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Text("Fix applied"), 120000), "Banner 'Fix applied'.");
        ExpectEvent(step, seq, "commit", FindingFixTransaction);
        await Shot(step, $"F6b-{fix.Id.Replace(':', '-')}");
        return preview;
    }

    private async Task UndoFindingFix(StepResult step)
    {
        int seq = host.LastEvent();
        host.PostCommand("undo");
        ExpectEvent(step, seq, "undo", FindingFixTransaction);
        await CheckAgain();
    }
}
