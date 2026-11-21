using System.Text.Json.Nodes;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// G7: a standard floor type whose gis_category was changed is an audit error, and Reset to standard
/// puts the standard's values back in one undoable transaction. Step ids G7.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const string PlatformType = "j ホーム_床";
    private const string ResetTransaction = "Kanizu GIS: Reset floor type";

    private async Task StandardTypeResetSteps()
    {
        await Step("G7.1", "Set up model, so the standard floor types exist", ApplySetup);
        long typeId = 0;
        await Step("G7.2", $"Change the gis_category of {PlatformType}: the audit reports an error with Reset to standard", async step =>
        {
            typeId = FloorTypeId(PlatformType);
            if (!step.Check(typeId != 0, $"The model has a floor type {PlatformType}."))
            {
                return;
            }

            JsonObject fixture = host.Call("fixture", new { values = new[] { new { id = typeId, name = "gis_category", value = "walkway" } } });
            step.Note($"Fixture: {fixture["log"]?.ToJsonString()}.");
            await CheckAgain();
            FindingItem? finding = (await Inspect()).Findings.FirstOrDefault(item => item.FixTargetId == typeId);
            step.Check(finding is { Severity: "error" or "warning", Topic: "floor types", Fix: "resetFloorType" }, $"The audit reports the type with a reset ({finding?.Severity} {finding?.Fix}: {finding?.Message}).");
            step.Check(await Shell.ShowsAsync(Shell.Button("Reset to standard")), "The finding has a Reset to standard button.");
            await Shot(step, "G7-2-finding");
        });
        await Step("G7.3", "Reset to standard writes the standard's category back in one transaction", async step =>
        {
            if (typeId == 0)
            {
                step.NotRun("No standard platform type.");
                return;
            }

            int seq = host.LastEvent();
            await Shell.Button("Reset to standard").First.ClickAsync();
            step.Check(await Shell.ShowsAsync(Shell.Text("Floor type reset to the standard"), 60000), "Banner 'Floor type reset to the standard'.");
            ExpectEvent(step, seq, "commit", ResetTransaction);
            step.Check(TypeParameter(typeId, "gis_category") == "platform", $"gis_category is platform again (reads '{TypeParameter(typeId, "gis_category")}').");
            step.Check((await Inspect()).Findings.All(item => item.FixTargetId != typeId), "The audit no longer reports the type.");
            await Shot(step, "G7-3-reset");
        });
        await Step("G7.4", "Undo the reset: the changed category and the finding come back", async step =>
        {
            if (typeId == 0)
            {
                step.NotRun("No standard platform type.");
                return;
            }

            int seq = host.LastEvent();
            host.PostCommand("undo");
            ExpectEvent(step, seq, "undo", ResetTransaction);
            await CheckAgain();
            step.Check(TypeParameter(typeId, "gis_category") == "walkway", $"gis_category is walkway again (reads '{TypeParameter(typeId, "gis_category")}').");
            step.Check((await Inspect()).Findings.Any(item => item.FixTargetId == typeId), "The finding is back.");
        });
    }

    private long FloorTypeId(string name) =>
        host.Call("floorTypes")["floorTypes"]!.AsArray()
            .Where(node => node!["name"]!.GetValue<string>() == name)
            .Select(node => node!["id"]!.GetValue<long>())
            .FirstOrDefault();
}
