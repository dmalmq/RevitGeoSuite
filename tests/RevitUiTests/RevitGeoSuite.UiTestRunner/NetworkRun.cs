using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Playwright;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The C8 check (/mnt/project-files/revit-template-review/c8/ui-tests/c8-network.md): the GIS standard
/// page's Network tab reads Kanizu's walking network file for this model and draws it in Revit without
/// a transaction. The scenario makes its own station folder with a Cesium export into the run folder and
/// writes the C8 sample there, fitted to two adjacent levels of the model. Step ids C8.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private static readonly TimeSpan CesiumWait = TimeSpan.FromMinutes(60);

    private NetworkCase? network;

    private async Task NetworkSteps()
    {
        await Step("C8.0", "The model has had Set up model once, so each level has a GIS_ plan", EnsureSetUp);
        await Step("C8.G", "The copy gets RevitGeoSuite's georeference record from its own coordinates (Confirm existing setup), which the Cesium export needs", EnsureGeoreference);
        await Step("C8.P", "Export GIS and Cesium into a station folder, write the network file for two adjacent levels", PrepareNetwork);
        await Step("C8.1", "The Network tab counts what it can draw and names the items on unknown levels", OpenNetwork);
        await Step("C8.2", "Show in Revit with GIS_<A> active: links and connector squares, with no transaction", ShowNetwork);
        await Step("C8.3", "GIS_<B> shows B's link, the connector squares and the finding's red cross", step => NetworkView(step, network?.Upper, "C8-3-upper", "B's link, the connector squares, and a red cross at the transitWithoutLink place"));
        await Step("C8.4", "A 3D view draws the connectors rising from A to B", NetworkIn3D);
        await Step("C8.5", "GIS and Cesium exports with the network shown have the same feature counts", ExportWhileShown);
        await Step("C8.6", "A link dropped from the file disappears at the next view, without Read again", DropNetworkLink);
        await Step("C8.7", "Another epsg in the file: a warning naming both codes, nothing drawn", OtherEpsg);
        await Step("C8.8", "Version 2 in the file: the red version banner", NewerVersion);
        await Step("C8.9", "The audit has two network warnings; Select on the first selects nothing", NetworkAudit);
        await Step("C8.10", "Hide in Revit takes the lines away in every view, with no transaction", HideNetwork);
    }

    private async Task PrepareNetwork(StepResult step)
    {
        string gisBefore = await OpenExport(step, string.Empty, "export", keepDefaultViews: true);
        bool exported = await ExportNow(step);
        await Shell.GoToAsync("/gis-standard");
        if (!exported)
        {
            return;
        }

        List<FloorUnit> units = ReshapeFixture.FloorUnits(gisBefore);
        string station = Path.Combine(options.OutputFolder, "station");
        CesiumRun? cesium = await ExportCesium(step, station);
        if (cesium == null)
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

        PlanViewRow[] plans = GisPlans(Views());
        LevelRow[] levels = Levels()
            .Where(level => !string.IsNullOrEmpty(level.LevelId) && level.ElevationFt != null && plans.Any(plan => plan.Level == level.Name))
            .OrderBy(level => level.ElevationFt)
            .ToArray();
        var pairs = levels.Zip(levels.Skip(1))
            .Select(pair => (Lower: pair.First, Upper: pair.Second, Units: units.Where(unit => unit.LevelId == pair.First.LevelId).ToList()))
            .Where(pair => pair.Units.Count > 0)
            .OrderByDescending(pair => pair.Units.Count)
            .ToList();
        if (!step.Check(pairs.Count > 0, $"Two adjacent levels with a level_id and a GIS_ plan, the lower one with floor units ({levels.Length} level(s) with both)."))
        {
            return;
        }

        (LevelRow lower, LevelRow upper, List<FloorUnit> lowerUnits) = pairs[0];
        FloorUnit largest = lowerUnits.OrderByDescending(unit => ReshapeFixture.Area(unit.Rings[0])).First();
        (double X, double Y) anchor = (largest.Rings[0].Average(point => point.X), largest.Rings[0].Average(point => point.Y));
        JsonObject written = NetworkFixture.Rewrite(NetworkFixture.Read(NetworkFixture.SamplePath), modelKey, lower.LevelId!, upper.LevelId!, largest.Epsg, anchor);
        string path = Path.Combine(station, "gis-network", NetworkFixture.FileName(modelKey));
        NetworkFixture.Write(path, written);
        HashSet<string> modelLevels = Levels().Select(level => level.LevelId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        network = new NetworkCase(
            path,
            written,
            NetworkFixture.Expect(written, modelLevels),
            Plan(plans, lower.Name),
            Plan(plans, upper.Name),
            largest.Epsg,
            gisBefore,
            cesium);
        var extent = NetworkFixture.Extent(written);
        step.Note($"A = {lower.Name} ({lower.LevelId}, GIS plan {network.Lower}), B = {upper.Name} ({upper.LevelId}, GIS plan {network.Upper}), EPSG:{largest.Epsg}.");
        step.Note($"Anchor: the middle of '{largest.Name}' on A at ({Coordinate(anchor.X)}, {Coordinate(anchor.Y)}); the network spans ({Coordinate(extent.MinX)}, {Coordinate(extent.MinY)}) to ({Coordinate(extent.MaxX)}, {Coordinate(extent.MaxY)}).");
        step.Note($"Wrote {path}. Expected: '{network.Expected.CountsLine}' and '{network.Expected.UnknownLine}'");
    }

    private async Task OpenNetwork(StepResult step)
    {
        if (!NetworkReady(step))
        {
            return;
        }

        await Shell.GoToAsync("/gis-standard");
        await NetworkTab().ClickAsync();
        step.Check(await Shell.ShowsAsync(Shell.Text(network!.Expected.CountsLine), 60000), $"The tab reads '{network.Expected.CountsLine}' ({await CountsLine()}).");
        step.Check(await Shell.ShowsAsync(Shell.Text(network.Expected.UnknownLine), 5000), $"It notes '{network.Expected.UnknownLine}'.");
        step.Check(await Visible(Shell.Button("Show in Revit")).CountAsync() == 1, "It offers 'Show in Revit'.");
        await Shot(step, "C8-1-network-tab");
    }

    private async Task ShowNetwork(StepResult step)
    {
        if (!NetworkReady(step))
        {
            return;
        }

        host.Call("activateView", new { name = network!.Lower });
        int seq = host.LastEvent();
        await Visible(Shell.Button("Show in Revit")).ClickAsync();
        step.Check(await Shell.ShowsAsync(Visible(Shell.Button("Hide in Revit")), 30000), "The button turns into 'Hide in Revit'.");
        await Shell.Page.WaitForTimeoutAsync(2000);
        NoCommit(step, seq);
        RevitShot(step, "C8-2-lower");
        step.Note($"Eyes in {network.Lower}: green links, one with an arrowhead; a stairs square, an escalator square with an arrow and a moving walkway square.");
    }

    private async Task NetworkView(StepResult step, string? view, string shot, string eyes)
    {
        if (!NetworkReady(step) || view == null)
        {
            return;
        }

        int seq = host.LastEvent();
        host.Call("activateView", new { name = view });
        await Shell.Page.WaitForTimeoutAsync(2000);
        NoCommit(step, seq);
        RevitShot(step, shot);
        step.Note($"Eyes in {view}: {eyes}.");
    }

    private async Task NetworkIn3D(StepResult step)
    {
        if (!NetworkReady(step))
        {
            return;
        }

        string[] views = host.Call("views3d")["views"]?.AsArray().Select(name => name!.GetValue<string>()).ToArray() ?? Array.Empty<string>();
        string? view = views.FirstOrDefault(name => name.StartsWith(GisPrefix, StringComparison.OrdinalIgnoreCase));
        if (!step.Check(view != null || views.Length > 0, $"The model has a 3D view ({views.Length})."))
        {
            return;
        }

        int seq = host.LastEvent();
        host.Call("activateView", new { name = view ?? views[0] });
        await Shell.Page.WaitForTimeoutAsync(3000);
        NoCommit(step, seq);
        RevitShot(step, "C8-4-3d");
        step.Note(view != null
            ? $"Eyes in {view}: connector lines rise from A to B, every stroke about 100 mm above its level's floor."
            : $"No GIS_ 3D view, so {views[0]} (not active at Show, not named GIS_*) should draw nothing.");
    }

    private async Task ExportWhileShown(StepResult step)
    {
        if (!NetworkReady(step))
        {
            return;
        }

        string gisAfter = await OpenExport(step, string.Empty, "export-network-shown", keepDefaultViews: true);
        bool exported = await ExportNow(step);
        await Shell.GoToAsync("/gis-standard");
        if (exported)
        {
            Dictionary<string, int> before = GeoPackageTables.RowCounts(network!.GisBefore);
            Dictionary<string, int> after = GeoPackageTables.RowCounts(gisAfter);
            step.Check(SameCounts(before, after), $"GeoPackage rows match the export before Show ({Format(after)}; before {Format(before)}).");
        }

        CesiumRun? cesium = await ExportCesium(step, Path.Combine(options.OutputFolder, "station"));
        if (cesium != null)
        {
            Dictionary<string, int> before = GeoPackageTables.RowCounts(network!.CesiumBefore.PackageRoot);
            Dictionary<string, int> after = GeoPackageTables.RowCounts(cesium.PackageRoot);
            step.Check(SameCounts(before, after), $"The Cesium package's GIS rows match the one before Show ({Format(after)}; before {Format(before)}).");
            step.Note($"Tile files: {cesium.TileFiles} (before Show {network.CesiumBefore.TileFiles}).");
        }

        await NetworkTab().ClickAsync();
        step.Check(await Shell.ShowsAsync(Visible(Shell.Button("Hide in Revit")), 10000), "Back on the Network tab, the network is still shown ('Hide in Revit').");
    }

    private async Task DropNetworkLink(StepResult step)
    {
        if (!NetworkReady(step))
        {
            return;
        }

        // Link 3 is the sample's only link on lvl-2f, which is B here.
        NetworkFixture.Write(network!.Path, NetworkFixture.WithoutLink(network.Written, 3));
        await NetworkView(step, network.Lower, "C8-6-lower", "A's links as before");
        await NetworkView(step, network.Upper, "C8-6-upper-link-dropped", "B's link is gone, the connector squares and the red cross stay; nothing was pressed on the page");
        step.Note($"The page now reads: {await CountsLine()}");
    }

    private async Task OtherEpsg(StepResult step)
    {
        if (!NetworkReady(step))
        {
            return;
        }

        int other = network!.Epsg + 1;
        NetworkFixture.Write(network.Path, NetworkFixture.With(network.Written, "epsg", other));
        await ReadAgain();
        string[] lines = await PageLines();
        string? warning = lines.FirstOrDefault(line => line.Contains(network.Epsg.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) && line.Contains(other.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal));
        step.Check(warning != null, $"A banner names both EPSG:{network.Epsg} (the model) and EPSG:{other} (the file) ({warning ?? await Shell.AlertTextAsync()}).");
        await Shot(step, "C8-7-epsg");
        host.Call("activateView", new { name = network.Upper });
        await Shell.Page.WaitForTimeoutAsync(2000);
        RevitShot(step, "C8-7-upper-nothing-drawn");
        step.Note($"Eyes in {network.Upper}: nothing of the network is drawn.");
    }

    private async Task NewerVersion(StepResult step)
    {
        if (!NetworkReady(step))
        {
            return;
        }

        NetworkFixture.Write(network!.Path, NetworkFixture.With(network.Written, "version", 2));
        await ReadAgain();
        const string banner = "is version 2; this tool reads version 1. Update RevitGeoSuite.";
        step.Check(await Shell.ShowsAsync(Shell.Text(banner), 15000), $"The banner reads '… {banner}' ({await Shell.AlertTextAsync()}).");
        await Shot(step, "C8-8-version-2");
        NetworkFixture.Write(network.Path, network.Written);
        await ReadAgain();
        step.Check(await Shell.ShowsAsync(Shell.Text(network.Expected.CountsLine), 15000), $"With the file put back, Read again reads '{network.Expected.CountsLine}' ({await CountsLine()}).");
    }

    private async Task NetworkAudit(StepResult step)
    {
        if (!NetworkReady(step))
        {
            return;
        }

        await StandardTab().ClickAsync();
        await CheckAgain();
        FindingItem[] found = (await Inspect()).Findings.Where(finding => finding.Topic == "network").ToArray();
        step.Note($"network: {string.Join(" | ", found.Select(finding => $"{finding.Severity} '{finding.Message}' ({finding.ElementIds.Length} element(s))"))}");
        step.Check(found.Length == 2 && found.All(finding => finding.Severity == "warning"), $"The audit has two network warnings ({found.Length}: {string.Join(", ", found.Select(finding => finding.Severity))}).");
        if (found.Length > 0)
        {
            step.Check(found[0].ElementIds.Length == 0, $"The first one names no element, so Select selects nothing: stair2-uid is not in the model ({found[0].ElementIds.Length} element(s)).");
        }

        await Shot(step, "C8-9-audit");
    }

    private async Task HideNetwork(StepResult step)
    {
        if (!NetworkReady(step))
        {
            return;
        }

        await NetworkTab().ClickAsync();
        int seq = host.LastEvent();
        await Visible(Shell.Button("Hide in Revit")).ClickAsync();
        step.Check(await Shell.ShowsAsync(Visible(Shell.Button("Show in Revit")), 30000), "The button turns back into 'Show in Revit'.");
        await Shell.Page.WaitForTimeoutAsync(1000);
        NoCommit(step, seq);
        foreach ((string view, string shot) in new[] { (network!.Lower, "C8-10-lower-hidden"), (network.Upper, "C8-10-upper-hidden") })
        {
            host.Call("activateView", new { name = view });
            await Shell.Page.WaitForTimeoutAsync(2000);
            RevitShot(step, shot);
        }

        NoCommit(step, seq);
        step.Note($"Eyes in {network.Lower} and {network.Upper}: no network lines.");
    }

    /// <summary>
    /// Exports the model to Cesium as a station folder (a new version under revit/), through the page's
    /// bridge with the first floor plan profile. This records <paramref name="station"/> as the model's
    /// station folder, which is where the Network tab looks for Kanizu's file.
    /// </summary>
    private async Task<CesiumRun?> ExportCesium(StepResult step, string station)
    {
        JsonNode? state = await Shell.RpcAsync("cesium.export.getState");
        string[] profiles = state?["floorPlanProfiles"]?.AsArray().Select(name => name!.GetValue<string>()).ToArray() ?? Array.Empty<string>();
        if (!step.Check(profiles.Length > 0, "There is a floor plan export profile for the Cesium export (none means its first-run setup was never done on this PC)."))
        {
            return null;
        }

        Directory.CreateDirectory(station);
        int seq = host.LastEvent();
        JsonNode? result;
        try
        {
            result = await Shell.JobAsync("cesium.export.run", new
            {
                outputFolder = station,
                floorPlanProfileName = profiles[0],
                push = false,
                scope = "whole",
                lod = "coarse",
                preciseCrs = true,
                heightDatum = state?["lastHeightDatum"]?.GetValue<string>() ?? "unknown",
                keepVersions = true,
                selectedLinkUniqueIds = Array.Empty<string>(),
            }, CesiumWait);
        }
        catch (PlaywrightException ex)
        {
            step.Check(false, $"The Cesium export to {station} finishes: {ex.Message.Split('\n')[0]}");
            return null;
        }

        string root = result?["packageRoot"]?.GetValue<string>() ?? string.Empty;
        string[] warnings = result?["warnings"]?.AsArray().Select(item => item!.GetValue<string>()).ToArray() ?? Array.Empty<string>();
        string commits = string.Join(" / ", host.EventsAfter(seq).Where(item => item.Kind == "commit"));
        step.Note($"Cesium export (profile '{profiles[0]}', coarse) to {root}: {result?["summary"]}. Warnings: {(warnings.Length == 0 ? "none" : string.Join(" | ", warnings))}. Transactions: {(commits.Length == 0 ? "none" : commits)}.");
        if (!step.Check(Directory.Exists(root), $"The Cesium package exists ({root})."))
        {
            return null;
        }

        string tiles = result?["tilesetPath"]?.GetValue<string>() is { Length: > 0 } tileset ? Path.GetDirectoryName(tileset)! : root;
        int tileFiles = Directory.Exists(tiles) ? Directory.GetFiles(tiles, "*", SearchOption.AllDirectories).Count(file => file.EndsWith(".b3dm", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase)) : 0;
        return new CesiumRun(root, tileFiles);
    }

    private ILocator NetworkTab() => Shell.Page.GetByRole(AriaRole.Radio, new() { Name = "Network", Exact = true });

    private ILocator StandardTab() => Shell.Page.GetByRole(AriaRole.Radio, new() { Name = "Standard", Exact = true });

    private async Task ReadAgain()
    {
        await Visible(Shell.Button("Read again")).ClickAsync();
        await Shell.Page.WaitForTimeoutAsync(1500);
    }

    private async Task<string> CountsLine()
    {
        ILocator line = Shell.Text(new Regex(@"\d+ link\(s\), \d+ level connector\(s\) and \d+ finding\(s\) on a floor"));
        return await line.CountAsync() > 0 ? (await line.First.InnerTextAsync()).Trim() : "no counts line";
    }

    private async Task<string[]> PageLines() =>
        (await Shell.Page.InnerTextAsync("body")).Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();

    private bool NetworkReady(StepResult step)
    {
        if (network != null)
        {
            return true;
        }

        step.NotRun("C8.P wrote no network file.");
        return false;
    }

    // Buttons with the same name sit on other tabs of the page; act on the one on screen.
    private static ILocator Visible(ILocator locator) => locator.Locator("visible=true");

    private static string Plan(IEnumerable<PlanViewRow> plans, string level) => plans.First(plan => plan.Level == level).Name;

    private static bool SameCounts(Dictionary<string, int> before, Dictionary<string, int> after) =>
        before.Count == after.Count && before.All(pair => after.TryGetValue(pair.Key, out int count) && count == pair.Value);

    private static bool SamePath(string a, string b) =>
        a.Length > 0 && string.Equals(Path.GetFullPath(a).TrimEnd('\\', '/'), Path.GetFullPath(b).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase);

    private static string Coordinate(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);
}

internal sealed record NetworkCase(string Path, JsonObject Written, NetworkCounts Expected, string Lower, string Upper, int Epsg, string GisBefore, CesiumRun CesiumBefore);

internal sealed record CesiumRun(string PackageRoot, int TileFiles);
