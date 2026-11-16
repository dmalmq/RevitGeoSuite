using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// G4: the Revit warning at the moment of the mistake. The test host's fixture places elements in its
/// own transaction and logs each Revit warning it takes off ("removed Revit warning: …"), so the
/// guard's warning shows in the fixture's log. The checks match the English texts: like every
/// scenario, this one runs RevitGeoSuite in English. Step ids G4.*.
/// </summary>
internal sealed partial class GisStandardRun
{
    private const string FloorGuardText = "This floor's type has no unit category of the Kanizu GIS standard";

    private async Task GuardSteps()
    {
        await Step("G4.0", "The model has had Set up model once", EnsureSetUp);
        ElementRow? floor = null;
        await Step("G4.1", "A floor of a standard type copied on a level with a GIS_ plan raises no guard warning", async step =>
        {
            HashSet<long> gisLevels = GisPlanLevelIds();
            // Floors the audit names under "floor types" have a type outside the standard; start from another.
            HashSet<long> flagged = (await Inspect()).Findings.Where(finding => finding.Topic == "floor types").SelectMany(finding => finding.ElementIds).ToHashSet();
            floor = Elements(FloorScan().Select(row => row.Id).Where(id => !flagged.Contains(id))).FirstOrDefault(row => row is { Exists: true, Id: not null, TypeId: not null, LevelId: not null }
                && gisLevels.Contains(row.LevelId!.Value));
            if (!step.Check(floor != null, $"A floor of a type with a unit category stands on a level with a GIS_ plan ({gisLevels.Count} such level(s))."))
            {
                return;
            }

            string log = Fixture(new { copyInPlace = new[] { floor!.Id!.Value } }, out long ms);
            step.Note($"Copy of floor {floor.Id} ({floor.TypeName}) in {ms} ms: {log}");
            step.Check(!log.Contains(FloorGuardText, StringComparison.Ordinal), "No guard warning for a standard floor type.");
        });

        await Step("G4.2", "The same floor's type without gis_category: editing the type warns nothing, a new floor of it warns", step =>
        {
            if (floor == null)
            {
                step.NotRun("G4.1 found no floor.");
                return Task.CompletedTask;
            }

            string cleared = Fixture(new { values = new[] { new { id = floor.TypeId!.Value, name = "gis_category", value = "" } } }, out _);
            step.Check(!cleared.Contains(FloorGuardText, StringComparison.Ordinal), $"Clearing gis_category on the type is not a placement, so no guard warning: {cleared}");
            string log = Fixture(new { copyInPlace = new[] { floor.Id!.Value } }, out long ms);
            step.Note($"Copy of floor {floor.Id} in {ms} ms: {log}");
            step.Check(log.Contains("removed Revit warning: " + FloorGuardText, StringComparison.Ordinal), "The new floor raises the guard's warning, a warning that the commit takes, not an error.");
            return Task.CompletedTask;
        });

        await Step("G4.3", "Another floor re-typed to that type warns", step =>
        {
            if (floor == null)
            {
                step.NotRun("G4.1 found no floor.");
                return Task.CompletedTask;
            }

            HashSet<long> gisLevels = GisPlanLevelIds();
            ElementRow? other = Elements(FloorScan().Select(row => row.Id)).FirstOrDefault(row => row is { Exists: true, Id: not null, TypeId: not null, LevelId: not null }
                && row.TypeId != floor.TypeId && gisLevels.Contains(row.LevelId!.Value));
            if (!step.Check(other != null, "Another floor of another type stands on a level with a GIS_ plan."))
            {
                return Task.CompletedTask;
            }

            string log = Fixture(new { retype = new[] { new { id = other!.Id!.Value, typeId = floor.TypeId!.Value } } }, out long ms);
            step.Note($"Floor {other.Id} ({other.TypeName}) re-typed to {floor.TypeName} in {ms} ms: {log}");
            step.Check(log.Contains("removed Revit warning: " + FloorGuardText, StringComparison.Ordinal), "The re-typed floor raises the guard's warning.");
            return Task.CompletedTask;
        });

        await Step("G4.4", "A new family that looks like an escalator off the standard list warns and names the standard escalator families", step =>
        {
            if (GuardSpot(floor) is not (string level, double x, double y))
            {
                step.NotRun("G4.1 found no floor to stand the family on.");
                return Task.CompletedTask;
            }

            JsonObject escalator = host.Call("placeFamily", new { path = Path.Combine(options.OutputFolder, "UITest Escalator G4.rfa"), category = "OST_GenericModel", level, x, y });
            string log = escalator["log"]?.ToJsonString() ?? string.Empty;
            step.Note($"Escalator look-alike: {log}.");
            step.Check(log.Contains("This family is not a standard escalator family (j エスカレータ-lightweight", StringComparison.Ordinal), "The escalator look-alike warns and names the standard escalator families.");
            return Task.CompletedTask;
        });

        await Step("G4.5", "A new floor made as a family warns", step =>
        {
            if (GuardSpot(floor) is not (string level, double x, double y))
            {
                step.NotRun("G4.1 found no floor to stand the family on.");
                return Task.CompletedTask;
            }

            if (PlaceFamilyOrRefused(step, "UITest Floor G4.rfa", "OST_Floors", level, x, y) is not JsonObject floorFamily)
            {
                return Task.CompletedTask;
            }

            string log = floorFamily["log"]?.ToJsonString() ?? string.Empty;
            step.Note($"Floor family: {log}.");
            step.Check(log.Contains("This floor is made as a family, which the export never reads", StringComparison.Ordinal), "The floor family warns.");
            return Task.CompletedTask;
        });

        await Step("G4.6", "Placing many floors stays quick: ten floors copied on a level with a GIS_ plan", step =>
        {
            if (floor == null)
            {
                step.NotRun("G4.1 found no floor.");
                return Task.CompletedTask;
            }

            long[] ids = Enumerable.Repeat(floor.Id!.Value, 10).ToArray();
            string log = Fixture(new { copyInPlace = ids }, out long ms);
            int warned = log.Split("removed Revit warning: " + FloorGuardText).Length - 1;
            step.Note($"Ten copies in {ms} ms with the guard on; the guard warned {warned} time(s) (one warning lists every new floor). The guard runs only when a transaction commits, never while a sketch is open.");
            step.Check(warned == 1, "One guard warning for the ten floors of one commit.");
            return Task.CompletedTask;
        });

        await Step("G4.7", "Probe: a guard warning nobody deletes, after the commit, in a saved and reopened copy, and after the type is fixed", step =>
        {
            if (floor == null)
            {
                step.NotRun("G4.1 found no floor.");
                return Task.CompletedTask;
            }

            // Runs last: it saves this run's detached copy under the run folder, and the model then is that file.
            JsonObject probe = host.Call("guardProbe", new { floorId = floor.Id!.Value, typeId = floor.TypeId!.Value, gisCategory = "walkway", path = Path.Combine(options.OutputFolder, "g4-guard-probe.rvt") });
            string[] Lines(string key) => probe[key]?.AsArray().Select(line => line?.GetValue<string>() ?? string.Empty).ToArray() ?? Array.Empty<string>();
            step.Note($"Probe: {probe["log"]?.ToJsonString()}");
            foreach (string key in new[] { "afterCommit", "afterReopen", "afterFix" })
            {
                string[] lines = Lines(key);
                step.Note($"{key}: {lines.Length} stored guard warning(s){(lines.Length > 0 ? ": " + string.Join(" | ", lines) : string.Empty)}");
            }

            step.Check(Lines("kept").Any(line => line.StartsWith(FloorGuardText, StringComparison.Ordinal)), $"The copy raised the guard's floor type warning and nothing deleted it ({Lines("kept").Length} kept). The notes say whether Revit stores it, keeps it on reopening, and drops it once the type is fixed.");
            return Task.CompletedTask;
        });
    }

    /// <summary>The level and the middle of G4.1's floor, where G4.4 and G4.5 place their families.</summary>
    private (string Level, double X, double Y)? GuardSpot(ElementRow? floor) =>
        floor?.Min == null || floor.Max == null
            ? null
            : (Levels().First(row => row.Id == floor.LevelId).Name, (floor.Min[0] + floor.Max[0]) / 2, (floor.Min[1] + floor.Max[1]) / 2);

    private string Fixture(object request, out long milliseconds)
    {
        var clock = Stopwatch.StartNew();
        JsonObject reply = host.Call("fixture", request);
        milliseconds = clock.ElapsedMilliseconds;
        return reply["log"]?.ToJsonString() ?? string.Empty;
    }
}
