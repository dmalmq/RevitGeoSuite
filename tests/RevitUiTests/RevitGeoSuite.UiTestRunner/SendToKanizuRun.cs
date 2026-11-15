using System.Diagnostics;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// F1, Send to Kanizu: the GIS package with level and room indexes and no 3D Tiles goes to the
/// station folder as a new version, places its levels where a Cesium export does, says what changed
/// since the last send, and stops on audit errors until Send anyway. Step ids F1.*. Everything is
/// written under the run folder's station/ and kept in locals, so the scenario can share a Revit.
/// </summary>
internal sealed partial class GisStandardRun
{
    private async Task SendToKanizuSteps()
    {
        string station = Path.Combine(options.OutputFolder, "station");
        string? cesiumVersion = null;
        string? firstSend = null;
        await Step("F1.1", "Set up model and export to Cesium into a station folder in the run folder: the version a send compares with", async step =>
        {
            if ((await Inspect()).SetupLines.Length > 0)
            {
                await ApplySetup(step);
            }

            Stopwatch clock = Stopwatch.StartNew();
            CesiumRun? cesium = await ExportCesium(step, station);
            step.Note($"Cesium export took {clock.Elapsed.TotalSeconds:F1} s.");
            cesiumVersion = cesium?.PackageRoot;
            await Shell.GoToAsync("/gis-standard");
        });

        await Step("F1.2", "station.send.run writes GIS, levels.json and rooms.json, no tileset, and finds nothing changed since the Cesium export", async step =>
        {
            if (cesiumVersion == null)
            {
                step.NotRun("No Cesium version to compare with.");
                return;
            }

            int seq = host.LastEvent();
            JsonNode? state = await Shell.RpcAsync("station.send.getState", new { stationFolder = station });
            step.Note($"State: {state?.ToJsonString()}");
            step.Check(state?["folderKind"]?.GetValue<string>() == "station", "The Cesium export's folder reads as a station folder.");
            JsonNode? result = await Shell.JobAsync(
                "station.send.run",
                new { stationFolder = station, documentKey = state?["documentKey"]?.GetValue<string>() ?? string.Empty, confirmNewFolder = false },
                CesiumWait);
            firstSend = result?["versionFolder"]?.GetValue<string>();
            step.Note($"Send: {result?.ToJsonString()}");
            step.Note($"Transactions: {string.Join(" / ", host.EventsAfter(seq).Where(item => item.Kind == "commit"))}.");
            if (!step.Check(firstSend != null && Directory.Exists(firstSend), $"The new version exists ({firstSend})."))
            {
                return;
            }

            step.Check(result?["changesCounted"]?.GetValue<bool>() == true, "The changes were counted.");
            step.Check(result?["hasPrevious"]?.GetValue<bool>() == true, "It compares with the Cesium export's version.");
            step.Check(result?["elementsChanged"]?.GetValue<int>() == 0, $"Nothing changed since the Cesium export ({result?["elementsChanged"]} element(s); layers {result?["layers"]?.ToJsonString()}).");
            CheckSendPackage(step, firstSend!, cesiumVersion!);
        });

        await Step("F1.3", "The ribbon's page (#/send/now) sends at once and says nothing changed", async step =>
        {
            if (firstSend == null)
            {
                step.NotRun("No first send.");
                return;
            }

            int before = Versions(station).Length;
            bool blocked = await OpenSendNow(step);
            // After Set up model in F1.1 the copy has no audit errors, so the ribbon's page must send at once.
            if (!step.Check(!blocked, "The page sent without stopping: the copy has no audit errors after Set up model."))
            {
                await Shell.Button("Send anyway").ClickAsync();
            }

            if (!step.Check(await Shell.ShowsAsync(Shell.Text("Sent to Kanizu"), 300000), "The page shows 'Sent to Kanizu'."))
            {
                return;
            }

            step.Check(await Shell.ShowsAsync(Shell.Text("Nothing changed since the last send."), 5000), "It says nothing changed since the last send.");
            step.Check(Versions(station).Length == before + 1, $"One new version ({before} before, {Versions(station).Length} after).");
            step.Check(await Shell.Page.EvaluateAsync<string>("() => location.hash") == "#/send", "The address is back at #/send, so a reload does not send again.");
            await Shot(step, "F1-3-sent-nothing-changed");
        });

        await Step("F1.4", $"Change {PlatformType}'s category: the page stops on the audit error and sends nothing; Send anyway reports edited elements", async step =>
        {
            long typeId = FloorTypeId(PlatformType);
            if (!step.Check(typeId != 0, $"The model has a floor type {PlatformType}."))
            {
                return;
            }

            JsonObject fixture = host.Call("fixture", new { values = new[] { new { id = typeId, name = "gis_category", value = "walkway" } } });
            step.Note($"Fixture: {fixture["log"]?.ToJsonString()}.");
            int before = Versions(station).Length;
            bool blocked = await OpenSendNow(step);
            FindingItem? finding = (await Inspect()).Findings.FirstOrDefault(item => item.FixTargetId == typeId);
            step.Note($"Finding: {finding?.Severity} {finding?.Message}");
            if (!step.Check(finding?.Severity == "error", "The changed category is an audit error on the platform floor type."))
            {
                return;
            }

            step.Check(blocked, "The page lists the audit errors instead of sending.");
            step.Check(Versions(station).Length == before, "No version was written.");
            await Shot(step, "F1-4-blocked");
            if (!blocked)
            {
                return;
            }

            await Shell.Button("Send anyway").ClickAsync();

            if (!step.Check(await Shell.ShowsAsync(Shell.Text("Sent to Kanizu"), 300000), "The page shows 'Sent to Kanizu'."))
            {
                return;
            }

            ILocator line = Shell.Text(new Regex(@"(\d+) element\(s\) changed since the last send: (\d+) added, (\d+) edited, (\d+) removed\."));
            if (step.Check(await Shell.ShowsAsync(line, 5000), "It counts the changed elements."))
            {
                string text = await line.First.InnerTextAsync();
                Match match = Regex.Match(text, @"(\d+) element\(s\) changed since the last send: (\d+) added, (\d+) edited, (\d+) removed\.");
                int edited = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
                step.Note(text);
                step.Check(edited > 0 && match.Groups[2].Value == "0" && match.Groups[4].Value == "0", "Only edits: the platform floors changed category, none was added or removed.");
            }

            step.Check(await Shell.ShowsAsync(Shell.Page.GetByRole(AriaRole.Cell, new() { Name = "Units", Exact = true }), 5000), "The Units row lists the edits.");
            int rows = await Shell.Page.Locator("table tbody tr").CountAsync();
            step.Check(rows == 1, $"Only the Units layer changed ({rows} layer row(s)): the category change touches the platform units and nothing else.");
            await Shot(step, "F1-4-sent-edits");
        });
    }

    /// <summary>Opens #/send/now as the ribbon does; true when the page stopped on audit errors.</summary>
    private async Task<bool> OpenSendNow(StepResult step)
    {
        await Shell.GoToAsync("/send/now");
        ILocator blocked = Shell.Text(new Regex(@"The audit found \d+ error\(s\)\. Nothing was sent\."));
        ILocator sent = Shell.Text("Sent to Kanizu");
        await Shell.Page.WaitForTimeoutAsync(500);
        for (int waited = 0; waited < 300000; waited += 500)
        {
            if (await blocked.CountAsync() > 0)
            {
                await Shot(step, "F1-blocked");
                return true;
            }

            if (await sent.CountAsync() > 0)
            {
                return false;
            }

            await Shell.Page.WaitForTimeoutAsync(500);
        }

        return false;
    }

    private static string[] Versions(string station)
    {
        string revit = Path.Combine(station, "revit");
        return Directory.Exists(revit)
            ? Directory.GetDirectories(revit).SelectMany(Directory.GetDirectories).Where(path => !Path.GetFileName(path).StartsWith('.')).ToArray()
            : Array.Empty<string>();
    }

    /// <summary>
    /// The send's package against the Cesium export's: no tileset, the index files there, every GIS
    /// level in levels.json under the same key and UniqueId, at the same height above TP within 1 cm.
    /// </summary>
    private static void CheckSendPackage(StepResult step, string send, string cesium)
    {
        step.Check(!File.Exists(Path.Combine(send, "tiles", "tileset.json")), "No tileset.json.");
        step.Check(Directory.GetFiles(send, "*.glb", SearchOption.AllDirectories).Length == 0, "No GLB.");
        step.Check(Directory.GetFiles(Path.Combine(send, "gis"), "*.gpkg").Length > 0, "A GeoPackage in gis/.");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(Path.Combine(send, "cesium-package.json")))!;
        JsonNode cesiumManifest = JsonNode.Parse(File.ReadAllText(Path.Combine(cesium, "cesium-package.json")))!;
        step.Check(manifest["tiles"]?["tileset"] == null && manifest["tiles"]?["levels"]?.GetValue<string>() == "tiles/levels.json" && manifest["tiles"]?["rooms"]?.GetValue<string>() == "tiles/rooms.json",
            $"The manifest lists levels and rooms and no tileset ({manifest["tiles"]?.ToJsonString()}).");
        step.Check(manifest["export"] == null, "No export section: no tiles were exported.");
        step.Check(manifest["source"]?["modelId"]?.ToString() == cesiumManifest["source"]?["modelId"]?.ToString(), "The same model id as the Cesium export, so Kanizu reads it as a new version of the same model.");

        double sendBase = manifest["anchor"]?["orthometricHeightMeters"]?.GetValue<double>() ?? double.NaN;
        double cesiumBase = cesiumManifest["anchor"]?["orthometricHeightMeters"]?.GetValue<double>() ?? double.NaN;
        JsonArray sendLevels = JsonNode.Parse(File.ReadAllText(Path.Combine(send, "tiles", "levels.json")))!["levels"]!.AsArray();
        JsonArray cesiumLevels = JsonNode.Parse(File.ReadAllText(Path.Combine(cesium, "tiles", "levels.json")))!["levels"]!.AsArray();
        JsonArray levelMap = manifest["levelMap"]?.AsArray() ?? new JsonArray();
        step.Check(levelMap.Count > 0 && levelMap.Count == sendLevels.Count, $"levels.json has one level per levelMap entry ({sendLevels.Count} levels, {levelMap.Count} entries).");
        int matched = 0;
        foreach (JsonNode? entry in levelMap)
        {
            string key = entry!["tilesLevelKey"]!.GetValue<string>();
            JsonNode? mine = sendLevels.FirstOrDefault(level => level!["levelKey"]!.GetValue<string>() == key);
            JsonNode? theirs = cesiumLevels.FirstOrDefault(level => level!["levelKey"]!.GetValue<string>() == key);
            if (!step.Check(mine != null, $"Level {key} is in levels.json."))
            {
                continue;
            }

            double height = sendBase + mine!["levelElevationMeters"]!.GetValue<double>();
            if (theirs == null)
            {
                step.Note($"{key}: {height:F3} m; the Cesium export has no element on it.");
                continue;
            }

            double cesiumHeight = cesiumBase + theirs["levelElevationMeters"]!.GetValue<double>();
            bool sameIds = theirs["levelUniqueIds"]!.AsArray().Select(id => id!.GetValue<string>()).Contains(mine["levelUniqueIds"]![0]!.GetValue<string>());
            step.Check(sameIds && Math.Abs(height - cesiumHeight) < 0.01,
                $"{key}: {height:F3} m above TP, Cesium {cesiumHeight:F3} m; {mine["elementCount"]} GIS feature(s); same UniqueId {sameIds}.");
            matched++;
        }

        step.Check(matched > 0, $"{matched} level(s) compared with the Cesium export.");
    }
}
