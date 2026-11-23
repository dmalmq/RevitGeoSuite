using System;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

/// <summary>
/// The round trip's state (build list H1) read from a copy of the link contract's sample station,
/// the same answers Kanizu's <c>round_trip</c> test gives for that folder.
/// </summary>
public sealed class RoundTripTests
{
    private const string Model = "8d7f3c1e-0000-4000-8000-000000000001";
    private const string Folder = "tokyo-station-8d7f3c1e";
    private const string First = "revit/tokyo-station-8d7f3c1e/20261108T120000Z-130617a7";
    private const string Second = "revit/tokyo-station-8d7f3c1e/20261109T090000Z-11111111";

    private static void CopyDir(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (string entry in Directory.GetFileSystemEntries(from))
        {
            string target = Path.Combine(to, Path.GetFileName(entry));
            if (Directory.Exists(entry))
            {
                CopyDir(entry, target);
            }
            else
            {
                File.Copy(entry, target);
            }
        }
    }

    /// <summary>A copy of the sample station: one model with one export, never applied.</summary>
    private static string Station(string name)
    {
        string dir = Path.Combine(Path.GetTempPath(), "rgs-round-trip", name);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }

        CopyDir(SampleStation(), dir);
        return dir;
    }

    private static string SampleStation()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current != null && !File.Exists(Path.Combine(current.FullName, "RevitGeoSuite.sln")))
        {
            current = current.Parent;
        }

        Assert.NotNull(current);
        return Path.Combine(current!.FullName, "tests", "Fixtures", "Conformance", "station");
    }

    /// <summary>A second export of the model, a day after the first.</summary>
    private static void ExportAgain(string station)
    {
        CopyDir(Path.Combine(station, First.Replace('/', Path.DirectorySeparatorChar)), Path.Combine(station, Second.Replace('/', Path.DirectorySeparatorChar)));
        string manifest = Path.Combine(station, Second.Replace('/', Path.DirectorySeparatorChar), "cesium-package.json");
        string text = File.ReadAllText(manifest);
        string updated = text.Replace("2026-11-08T12:00:00Z", "2026-11-09T09:00:00Z");
        Assert.NotEqual(text, updated);
        File.WriteAllText(manifest, updated);
    }

    /// <summary>The drift file a GIS apply of <paramref name="applied"/> writes, with <paramref name="rows"/> GDB edits waiting.</summary>
    private static void Applied(string station, string applied, int rows, int version = GisDrift.Version)
    {
        JArray entries = new();
        for (int i = 0; i < rows; i++)
        {
            entries.Add(new JObject
            {
                ["revitKey"] = "model|u-" + i + "|0",
                ["uniqueId"] = "u-" + i,
                ["layer"] = "unit",
                ["field"] = "name",
                ["gdb"] = "edited",
            });
        }

        string text = new JObject
        {
            ["schema"] = GisDrift.Schema,
            ["version"] = version,
            ["model"] = Model,
            ["appliedVersion"] = applied,
            ["rows"] = entries,
        }.ToString();
        string dir = Path.Combine(station, GisDrift.Folder);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, GisDrift.FileName(Model)), text, new UTF8Encoding(false));
    }

    private static RoundTripState[] States(string station)
    {
        var states = new System.Collections.Generic.List<RoundTripState>();
        foreach (RoundTripModel model in RoundTrip.Models(station))
        {
            Assert.Equal((Model, Folder), (model.Model, model.Folder));
            states.Add(model.State);
        }

        return states.ToArray();
    }

    [Fact]
    public void EachModelReadsItsStateFromItsNewestExportAndItsDriftFile()
    {
        string dir = Station("states");
        Assert.Equal(new[] { RoundTripState.RevitAhead }, States(dir));

        Applied(dir, First, 0);
        Assert.Equal(new[] { RoundTripState.InSync }, States(dir));

        Applied(dir, First, 2);
        Assert.Equal(new[] { RoundTripState.GdbAhead }, States(dir));

        ExportAgain(dir);
        Assert.Equal(new[] { RoundTripState.BothChanged }, States(dir));

        Applied(dir, First, 0);
        Assert.Equal(new[] { RoundTripState.RevitAhead }, States(dir));

        Applied(dir, Second, 0);
        Assert.Equal(new[] { RoundTripState.InSync }, States(dir));
    }

    [Fact]
    public void ADriftFileThatCannotBeTrustedBlocksItsModel()
    {
        string dir = Station("blocked");
        Applied(dir, "revit/tokyo-station-8d7f3c1e/20261101T000000Z-gone0000", 0);
        Assert.Equal(new[] { RoundTripState.Blocked }, States(dir));

        string path = Path.Combine(dir, GisDrift.Folder, GisDrift.FileName(Model));
        File.WriteAllText(path, "{ not json");
        Assert.Equal(new[] { RoundTripState.Blocked }, States(dir));

        Applied(dir, First, 0);
        string text = File.ReadAllText(path);
        string newer = text.Replace("\"version\": " + GisDrift.Version, "\"version\": " + (GisDrift.Version + 1));
        Assert.NotEqual(text, newer);
        File.WriteAllText(path, newer);
        Assert.Equal(new[] { RoundTripState.Blocked }, States(dir));
    }

    [Fact]
    public void AStationTakesItsModelsWorstState()
    {
        RoundTripModel ModelOf(RoundTripState state) => new(string.Empty, string.Empty, state);
        RoundTripState? StationOf(params RoundTripState[] states)
        {
            var models = new RoundTripModel[states.Length];
            for (int i = 0; i < states.Length; i++)
            {
                models[i] = ModelOf(states[i]);
            }

            return RoundTrip.Station(models);
        }

        Assert.Null(StationOf());
        Assert.Equal(RoundTripState.InSync, StationOf(RoundTripState.InSync, RoundTripState.InSync));
        Assert.Equal(RoundTripState.RevitAhead, StationOf(RoundTripState.InSync, RoundTripState.RevitAhead));
        Assert.Equal(RoundTripState.GdbAhead, StationOf(RoundTripState.GdbAhead, RoundTripState.InSync));
        Assert.Equal(RoundTripState.BothChanged, StationOf(RoundTripState.RevitAhead, RoundTripState.GdbAhead));
        Assert.Equal(RoundTripState.BothChanged, StationOf(RoundTripState.BothChanged, RoundTripState.InSync));
        Assert.Equal(RoundTripState.Blocked, StationOf(RoundTripState.BothChanged, RoundTripState.Blocked));
    }

    [Fact]
    public void AMissingRevitFolderHasNoStateAndOneThatCannotBeListedIsBlocked()
    {
        string missing = Path.Combine(Path.GetTempPath(), "rgs-round-trip", "missing");
        Directory.CreateDirectory(missing);
        Assert.Empty(RoundTrip.Models(missing));
        Assert.Null(RoundTrip.Station(RoundTrip.Models(missing)));

        string blocked = Path.Combine(Path.GetTempPath(), "rgs-round-trip", "blocked-list");
        if (Directory.Exists(blocked))
        {
            Directory.Delete(blocked, recursive: true);
        }

        Directory.CreateDirectory(blocked);
        File.WriteAllText(Path.Combine(blocked, "revit"), "not a folder");
        RoundTripModel model = Assert.Single(RoundTrip.Models(blocked));
        Assert.Equal((string.Empty, string.Empty, RoundTripState.Blocked), (model.Model, model.Folder, model.State));
        Assert.Equal(RoundTripState.Blocked, RoundTrip.Station(RoundTrip.Models(blocked)));
    }
}
