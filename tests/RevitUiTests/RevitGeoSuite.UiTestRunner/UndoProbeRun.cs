using System.Text.Json.Nodes;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// Why Revit refuses Undo on the build PC: after a plain fixture transaction and after a RevitGeoSuite
/// Apply, the run reads whether Revit offers Undo with the shell window in front, after a click in the
/// view, with the shell window minimised and hidden, and then presses Ctrl+Z as a person would. The
/// notes are the answer; a step fails only when the probe itself cannot run. Step ids UP.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private async Task UndoProbeSteps()
    {
        await Step("UP.0", "The model has had Set up model once, so each level has a GIS_ plan", EnsureSetUp);
        await Step("UP.1", "After a plain fixture transaction: where Revit offers Undo, and whether Ctrl+Z undoes it", ProbeFixture);
        await Step("UP.G", "The copy gets RevitGeoSuite's georeference record from its own coordinates, which the Cesium export needs", EnsureGeoreference);
        await Step("UP.S", "A Cesium export into a station folder gives the copy a model id, which a drift file must name", EnsureStation);
        await Step("UP.P", "Export, make the reshape drift file, read floor F", PrepareReshape);
        await Step("UP.2", "After a RevitGeoSuite Apply: where Revit offers Undo, and whether Ctrl+Z undoes it", ProbeApply);
    }

    private async Task ProbeFixture(StepResult step)
    {
        FloorScanRow? floor = FloorScan().FirstOrDefault();
        if (!step.Check(floor != null, "The model has a floor for the fixture."))
        {
            return;
        }

        long id = floor!.Id;
        string before = GisKey(id);
        int seq = host.LastEvent();
        JsonObject fixture = host.Call("fixture", new { values = new[] { new { id, name = "gis_key", value = "undo-probe" } } });
        step.Note($"Fixture: {fixture["log"]?.ToJsonString()}.");
        RevitEvent? commit = host.EventsAfter(seq).LastOrDefault(item => item.Kind == "commit");
        step.Check(commit != null, $"The fixture committed ({commit?.ToString() ?? "no commit seen"}).");
        try
        {
            await ProbeStates(step, commit?.Names.FirstOrDefault() ?? string.Empty);
        }
        finally
        {
            // Ctrl+Z does not take the fixture back on the build PC, so put gis_key back through the host.
            if (GisKey(id) != before)
            {
                host.Call("fixture", new { values = new[] { new { id, name = "gis_key", value = before } } });
                step.Note($"Put floor {id}'s gis_key back to '{before}'.");
            }
        }
    }

    private string GisKey(long id) =>
        host.Call("params", new { ids = new[] { id }, names = new[] { "gis_key" } })["elements"]![id.ToString(System.Globalization.CultureInfo.InvariantCulture)]?["gis_key"]?.GetValue<string>() ?? string.Empty;

    private async Task ProbeApply(StepResult step)
    {
        if (reshape == null)
        {
            step.NotRun("UP.P made no reshape file.");
            return;
        }

        await PickDrift(reshape.Path);
        if (!await ExpectShapeEdits(step, reshape.Path))
        {
            return;
        }

        int seq = host.LastEvent();
        string[] log = await ApplyShapes(step, 1);
        step.Note($"Log: {string.Join(" / ", log)}");
        RevitEvent? commit = host.EventsAfter(seq).LastOrDefault(item => item.Kind == "commit");
        if (!step.Check(commit != null, $"Apply committed ({commit?.ToString() ?? "no commit seen"})."))
        {
            return;
        }

        await ProbeStates(step, commit!.Names.FirstOrDefault() ?? string.Empty);
    }

    // Each state is noted as one line; Ctrl+Z goes last, with the shell window hidden, then the window comes back.
    // The window comes back even when a step throws, so the rest of the session keeps its shell window.
    private async Task ProbeStates(StepResult step, string transaction)
    {
        void Record(string state) => step.Note($"[{state}] {host.Call("undoProbe").ToJsonString()}");

        Record("as left, shell window in front");
        step.Note($"Click in the view: {host.Call("clickView")["did"]}.");
        Record("after a click in the active view");
        try
        {
            step.Note($"Shell window: {host.Call("shellWindow", new { action = "minimize" })["did"]}.");
            Record("shell window minimised");
            step.Note($"Shell window: {host.Call("shellWindow", new { action = "hide" })["did"]}.");
            Record("shell window hidden");
            step.Note($"Click in the view: {host.Call("clickView")["did"]}.");
            Record("shell window hidden, after a click in the view");

            int seq = host.LastEvent();
            JsonObject typed = host.Call("ctrlZ");
            step.Note($"Ctrl+Z: {typed["typing"]}.");
            await Shell.Page.WaitForTimeoutAsync(2000);
            RevitEvent[] after = host.EventsAfter(seq).Where(item => item.Kind != "dialog").ToArray();
            bool undone = after.Any(item => item.Kind == "undo");
            step.Note($"After Ctrl+Z: {(after.Length == 0 ? "no Revit event" : string.Join(" / ", after.Select(item => item.ToString())))}. {(typed["typed"]?.GetValue<bool>() != true ? "Nothing was typed." : undone ? $"Ctrl+Z undid '{transaction}'." : $"Ctrl+Z did not undo '{transaction}'.")}");
            Record("after Ctrl+Z");
        }
        finally
        {
            step.Note($"Shell window: {host.Call("shellWindow", new { action = "show" })["did"]}; {host.Call("shellWindow", new { action = "restore" })["did"]}.");
            Record("shell window back");
        }
    }
}
