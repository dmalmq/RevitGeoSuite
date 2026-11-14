using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The C4 check (/mnt/project-files/revit-template-review/c4/c4-evidence.md, "Needs Revit"): GDB edits
/// that wait in the station's drift file show as a count on the GIS standard ribbon button and as a
/// warning on the export page, and both go away once the edit is applied. The scenario makes its own
/// station folder with a Cesium export (which gives the copy a model id) and its own one-shape drift file.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const string RibbonTab = "Revit Geo Suite";
    private static readonly TimeSpan RibbonWait = TimeSpan.FromSeconds(20);

    private string waitingDrift = string.Empty;
    private string waitingFloor = string.Empty;
    private bool waitingApplied;

    private async Task WaitingSteps()
    {
        await Step("C4.0", "The model has had Set up model once, so each level has a GIS_ plan", EnsureSetUp);
        await Step("C4.G", "The copy gets RevitGeoSuite's georeference record from its own coordinates, which the Cesium export needs", EnsureGeoreference);
        await Step("C4.P", "Cesium export into a station folder (model id), GIS export, a one-shape drift file for floor F", PrepareWaiting);
        await Step("C4.1", "Copying the drift file into the station's gis-drift folder: the button shows '· 1 GDB edit' and the export page warns", DriftArrives);
        await Step("C4.2", "Apply the shape edit: the count goes and the export page has no warning", ApplyWaiting);
    }

    private async Task PrepareWaiting(StepResult step)
    {
        string station = Path.Combine(options.OutputFolder, "station");
        if (await ExportCesium(step, station) == null)
        {
            return;
        }

        JsonNode? located = await Shell.RpcAsync("gisDrift.inspect", new { path = string.Empty, pick = false });
        string modelKey = located?["modelKey"]?.GetValue<string>() ?? string.Empty;
        string recorded = located?["stationFolder"]?.GetValue<string>() ?? string.Empty;
        step.Note($"Model key '{modelKey}', station folder '{recorded}'.");
        if (!step.Check(modelKey.Length > 0, "The model has a model id after the Cesium export.")
            || !step.Check(SamePath(recorded, station), $"The Cesium export recorded the run's station folder {station} ({recorded})."))
        {
            return;
        }

        if (!await ExportFloorUnits(step))
        {
            return;
        }

        HashSet<string> notEditable = FloorScan().Where(floor => floor.HasArc || floor.ExtraSketchLines || floor.ShapeEdited).Select(floor => floor.UniqueId).ToHashSet();
        List<FloorUnit> editable = exportedUnits.Where(unit => !notEditable.Contains(unit.UniqueId)).ToList();
        FloorUnit? unit = ReshapeFixture.Pick(editable, "retail") ?? ReshapeFixture.Pick(editable, "walkway");
        if (!step.Check(unit != null, "The export has a retail (or walkway) floor unit whose sketch can be redrawn."))
        {
            return;
        }

        string staged = Path.Combine(options.OutputFolder, "waiting.gis-drift.json");
        File.WriteAllText(staged, ReshapeFixture.DriftJson(unit!, ReshapeFixture.Reshaped(unit!, ReshapeFixture.Grow(unit!, GrowM)), modelKey));
        waitingDrift = Path.Combine(station, "gis-drift", DriftFileName(modelKey));
        waitingFloor = unit!.UniqueId;
        step.Note($"Staged {staged}: floor unit '{unit.Name}' ({unit.Category}, {unit.UniqueId}), east edge {GrowM} m east, model '{modelKey}'. C4.1 copies it to {waitingDrift}.");
        string before = RibbonButton(step)?["text"]?.GetValue<string>() ?? string.Empty;
        step.Check(!before.Contains("GDB edit", StringComparison.Ordinal), $"Before the file arrives, the button shows no GDB edits ('{OneLine(before)}').");
    }

    private async Task DriftArrives(StepResult step)
    {
        if (waitingDrift.Length == 0)
        {
            step.NotRun("C4.P made no drift file.");
            return;
        }

        int seq = host.LastEvent();
        Directory.CreateDirectory(Path.GetDirectoryName(waitingDrift)!);
        File.Copy(Path.Combine(options.OutputFolder, "waiting.gis-drift.json"), waitingDrift, overwrite: true);
        DateTime copied = DateTime.UtcNow;
        JsonNode? button = await WaitForRibbon(text => text.Contains("· 1 GDB edit", StringComparison.Ordinal));
        string text = button?["text"]?.GetValue<string>() ?? string.Empty;
        step.Check(text.Contains("· 1 GDB edit", StringComparison.Ordinal), $"Within {RibbonWait.TotalSeconds:0} s the button reads '… · 1 GDB edit' ('{OneLine(text)}', after {(DateTime.UtcNow - copied).TotalSeconds:0} s).");
        step.Note($"Tooltip: {OneLine(button?["toolTip"]?.GetValue<string>() ?? string.Empty)}");
        RevitEvent[] changes = host.EventsAfter(seq).Where(item => item.Kind != "dialog").ToArray();
        step.Check(changes.Length == 0, $"Counting makes no Revit transaction ({(changes.Length == 0 ? "none" : string.Join(" / ", changes.Select(item => item.ToString())))}).");

        await Shell.GoToAsync("/export/geopackage");
        ILocator warning = Shell.Text(new Regex(@"^1 GDB edit\(s\) from Kanizu wait for this model"));
        step.Check(await Shell.ShowsAsync(warning, 30000), "The export page warns '1 GDB edit(s) from Kanizu wait for this model'.");
        ILocator review = Shell.Button("Review GDB edits");
        bool offered = step.Check(await Shell.ShowsAsync(review), "The warning offers 'Review GDB edits'.");
        await Shot(step, "C4-1-export-warning");
        if (offered)
        {
            await review.ClickAsync();
            step.Check(await Shell.ShowsAsync(Shell.Text("Shape edits (1)"), 60000), "Review GDB edits opens the GDB edits tab with the station file's one shape edit.");
            await Shot(step, "C4-1-gdb-edits");
        }
    }

    private async Task ApplyWaiting(StepResult step)
    {
        if (waitingDrift.Length == 0 || !File.Exists(waitingDrift))
        {
            step.NotRun("C4.1 placed no drift file.");
            return;
        }

        if (await Shell.Text("Shape edits (1)").CountAsync() == 0)
        {
            await PickDrift(waitingDrift);
            if (!await ExpectShapeEdits(step, waitingDrift))
            {
                return;
            }
        }

        ElementRow floor = Element(waitingFloor);
        int seq = host.LastEvent();
        string[] log = await ApplyShapes(step, 1);
        step.Note($"Log: {string.Join(" / ", log)}");
        waitingApplied = host.EventsAfter(seq).Any(item => item.Kind == "commit" && item.Names.Any(each => each.StartsWith($"Reshape floor {floor.Id}", StringComparison.Ordinal)));
        if (!step.Check(waitingApplied, $"Apply commits 'Reshape floor {floor.Id} … to the GDB outline'."))
        {
            return;
        }

        JsonNode? button = await WaitForRibbon(text => !text.Contains("GDB edit", StringComparison.Ordinal));
        string text = button?["text"]?.GetValue<string>() ?? string.Empty;
        step.Check(!text.Contains("GDB edit", StringComparison.Ordinal), $"Within {RibbonWait.TotalSeconds:0} s the button drops the count ('{OneLine(text)}').");
        await Shell.GoToAsync("/export/geopackage");
        await Shell.Page.WaitForTimeoutAsync(3000);
        int warnings = await Shell.Text(new Regex(@"GDB edit\(s\) from Kanizu wait for this model")).CountAsync();
        step.Check(warnings == 0, $"The export page has no GDB edits warning ({warnings}).");
        await Shot(step, "C4-2-export-no-warning");
        await Shell.GoToAsync("/gis-standard");
    }

    /// <summary>The GIS standard button on RevitGeoSuite's tab (name, text, tooltip), or null with a note when there is none.</summary>
    private JsonNode? RibbonButton(StepResult step)
    {
        JsonArray items = host.Call("ribbonItems", new { tab = RibbonTab })["items"]?.AsArray() ?? new JsonArray();
        JsonNode? button = items.FirstOrDefault(item => (item?["text"]?.GetValue<string>() ?? string.Empty).StartsWith("GIS standard", StringComparison.Ordinal));
        if (button == null)
        {
            step.Note($"No GIS standard button on '{RibbonTab}': {string.Join(" | ", items.Select(item => OneLine(item?["text"]?.GetValue<string>() ?? string.Empty)))}.");
        }

        return button;
    }

    // RevitGeoSuite counts again on Idling every 5 s, so the button can take a few seconds to change.
    private async Task<JsonNode?> WaitForRibbon(Func<string, bool> done)
    {
        DateTime end = DateTime.UtcNow + RibbonWait;
        JsonNode? button;
        do
        {
            JsonArray items = host.Call("ribbonItems", new { tab = RibbonTab })["items"]?.AsArray() ?? new JsonArray();
            button = items.FirstOrDefault(item => (item?["text"]?.GetValue<string>() ?? string.Empty).StartsWith("GIS standard", StringComparison.Ordinal));
            if (button != null && done(button["text"]?.GetValue<string>() ?? string.Empty))
            {
                return button;
            }

            await Shell.Page.WaitForTimeoutAsync(1000);
        }
        while (DateTime.UtcNow < end);
        return button;
    }

    /// <summary>The drift file name Kanizu writes for a model key: ASCII letters, digits and '-' kept, anything else '_'.</summary>
    internal static string DriftFileName(string modelKey)
    {
        var safe = new StringBuilder();
        foreach (char character in modelKey)
        {
            safe.Append((character < 128 && char.IsLetterOrDigit(character)) || character == '-' ? character : '_');
        }

        return safe + ".gis-drift.json";
    }

    private static string OneLine(string text) => text.Replace("\r", string.Empty).Replace('\n', ' ');
}
