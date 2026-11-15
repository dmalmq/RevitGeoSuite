using System.Text.Json.Nodes;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The Georeference page on a model that already has coordinates (R3): one screen with what was found,
/// the CRS and Save, which records RevitGeoSuite's metadata and moves nothing in Revit; "Set up new
/// coordinates" starts the six-step flow. Step ids R3.*. The copy's coordinates are only read; the
/// six-step flow is followed to its second step, which reads the CRS origin and writes nothing.
/// </summary>
internal sealed partial class GisStandardRun
{
    /// <summary>
    /// The active project location's position at the model origin and both axes, read by the test
    /// host through a floor (floorGeometry), independent of RevitGeoSuite. Null when the copy has no floor.
    /// </summary>
    private JsonNode? HostPosition()
    {
        FloorScanRow? floor = FloorScan().FirstOrDefault();
        return floor == null ? null : host.Call("floorGeometry", new { uniqueId = floor.UniqueId })["projectPosition"]?.DeepClone();
    }

    private async Task GeoreferenceScreenSteps()
    {
        JsonNode? before = null;
        JsonNode? hostBefore = null;
        string shown = string.Empty;
        await Step("R3.1", "An existing setup opens on one screen: no step count, the CRS select, an amber caveat", async step =>
        {
            before = await Shell.RpcAsync("georeference.getCurrentState");
            step.Note($"Before: {(before == null ? "nothing" : Describe(before))}");
            await Shell.GoToAsync("/georeference");
            ILocator select = Shell.Page.Locator("#confirm-crs-select");
            if (!step.Check(await Shell.ShowsAsync(select), "The page shows the CRS select of the existing-setup screen."))
            {
                await Shot(step, "R3-1-no-existing-screen");
                return;
            }

            ILocator panel = Shell.Page.Locator("[data-georef-panel]");
            step.Check(await panel.Locator("h1", new() { HasText = "Existing setup" }).CountAsync() == 1, "Its title is Existing setup.");
            step.Check(await panel.GetByText("Step 1 of", new() { Exact = false }).CountAsync() == 0, "No \"Step 1 of 1\" over the one screen.");
            step.Check(await Shell.ShowsAsync(panel.GetByText("Positions are estimated from the shared coordinates.")), "The estimate caveat shows.");

            int? stored = (int?)before?["storedCrsEpsgCode"];
            shown = await select.InputValueAsync();
            if (stored != null)
            {
                step.Check(shown == $"EPSG:{stored}", $"The select shows the stored CRS (EPSG:{stored}; shown {shown}).");
            }
            else
            {
                step.Check(shown.Length == 0, $"With no stored CRS the select asks for one ({shown}).");
                step.Check(await Shell.Button("Save").IsDisabledAsync(), "Save stays off until a CRS is chosen.");
            }

            // A CRS other than the stored one, so "the saved CRS is the one shown" means something,
            // and the pick must stay put (the page once put the stored CRS back over every pick).
            string[] values = await select.Locator("option").EvaluateAllAsync<string[]>("options => options.map(o => o.value)");
            string? other = values.FirstOrDefault(value => value.Length > 0 && value != $"EPSG:{stored}" && value != $"EPSG:{LumineEpsg}")
                ?? values.FirstOrDefault(value => value.Length > 0 && value != $"EPSG:{stored}");
            if (!step.Check(other != null, $"The list offers a CRS other than the stored one ({values.Length} options)."))
            {
                return;
            }

            await select.SelectOptionAsync(other!);
            await Shell.Page.WaitForTimeoutAsync(1500);
            shown = await select.InputValueAsync();
            step.Check(shown == other, $"The pick stays ({other}; shown {shown}).");
            hostBefore = HostPosition();
            step.Note($"Revit's project position by the test host: {hostBefore?.ToJsonString()}");

            string label = (await select.Locator("option:checked").InnerTextAsync()).Trim();
            step.Check(label.StartsWith(shown + " · ", StringComparison.Ordinal), $"The chosen CRS reads with its code first ({label}).");
            await Shot(step, "R3-1-existing-screen");
        });

        await Step("R3.2", "Save records the CRS shown and moves nothing in Revit", async step =>
        {
            if (before == null || hostBefore == null || shown.Length == 0)
            {
                step.NotRun("R3.1 did not reach the existing-setup screen.");
                return;
            }

            int seq = host.LastEvent();
            await Shell.Button("Save").ClickAsync();
            step.Check(await Shell.ShowsAsync(Shell.Text("Metadata saved"), 60000), "The page says the metadata was saved.");
            step.Check(await Shell.ShowsAsync(Shell.Button("Saved")) && await Shell.Button("Saved").IsDisabledAsync(), "Save settles as Saved.");
            string[] commits = host.EventsAfter(seq).Where(item => item.Kind == "commit").SelectMany(item => item.Names).ToArray();
            string[] allowed = { "Apply Georeference", "Save Primary Mesh Code" };
            step.Check(commits.Contains("Apply Georeference") && commits.All(allowed.Contains),
                $"Save committed Apply Georeference and nothing but {string.Join(" / ", allowed)} ({string.Join(" / ", commits)}).");
            JsonNode after = await Shell.RpcAsync("georeference.getCurrentState") ?? throw new InvalidOperationException("georeference.getCurrentState returned nothing.");
            step.Note($"After: {Describe(after)}");
            step.Check(shown == $"EPSG:{(int?)after["storedCrsEpsgCode"]}", $"The stored CRS is the one shown ({shown}; stored EPSG:{(int?)after["storedCrsEpsgCode"]}).");
            JsonNode? hostAfter = HostPosition();
            step.Check(hostAfter != null && JsonNode.DeepEquals(hostBefore, hostAfter),
                $"Revit's project position, read by the test host at the origin and both axes, is unchanged ({hostAfter?.ToJsonString()}).");
            step.Check(before["projectPosition"] != null && JsonNode.DeepEquals(before["projectPosition"], after["projectPosition"]),
                "The page's own read of the project position is there and unchanged.");
            step.Check(before["surveyPoint"] != null && before["projectBasePoint"] != null
                && JsonNode.DeepEquals(before["projectBasePoint"], after["projectBasePoint"]) && JsonNode.DeepEquals(before["surveyPoint"], after["surveyPoint"]),
                "The Survey Point and Project Base Point are there and unchanged.");
            await Shot(step, "R3-2-saved");
        });

        await Step("R3.3", "\"Set up new coordinates\" starts the six steps without the saved result, and its CRS step goes on to the area", async step =>
        {
            await Shell.Button("Set up new coordinates").ClickAsync();
            ILocator panel = Shell.Page.Locator("[data-georef-panel]");
            step.Check(await Shell.ShowsAsync(panel.GetByText("Step 1 of 6", new() { Exact = false })), "The step count reads Step 1 of 6.");
            step.Check(await Shell.ShowsAsync(Shell.Page.Locator("#crs-select")), "The CRS step shows its select.");
            step.Check(await Shell.Text("Metadata saved").CountAsync() == 0, "Save's result from the one screen is gone.");
            int seq = host.LastEvent();
            await Shell.Button("Confirm CRS").ClickAsync();
            step.Check(await Shell.ShowsAsync(panel.GetByText("Step 2 of 6", new() { Exact = false })), "Confirm CRS moves on to step 2 (the area).");
            step.Check(!host.EventsAfter(seq).Any(item => item.Kind == "commit"), "Nothing was written to the model on the way.");
            await Shot(step, "R3-3-new-flow");
        });

        await Step("R3.4", "Save is off in a read-only model", step =>
        {
            step.NotRun("The runner cannot open the copy read-only; the web test 'keeps Save off while the CRS list loads and in a read-only model' covers the button.");
            return Task.CompletedTask;
        });
    }
}
