using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class GisDriftTests
{
    private static JObject Entry(
        string field,
        string? gdb,
        string layer = "unit",
        string revit = "",
        string? @base = null,
        string parameter = "",
        string binding = "",
        bool conflict = false,
        string uniqueId = "u-1",
        string label = "改札") =>
        new()
        {
            ["revitKey"] = $"model|{uniqueId}|0",
            ["uniqueId"] = uniqueId,
            ["layer"] = layer,
            ["field"] = field,
            ["parameter"] = parameter,
            ["guid"] = "",
            ["binding"] = binding,
            ["label"] = label,
            ["revit"] = revit,
            ["gdb"] = gdb is null ? JValue.CreateNull() : new JValue(gdb),
            ["base"] = @base ?? revit,
            ["conflict"] = conflict,
        };

    private static string Text(IEnumerable<JObject> rows, string schema = GisDrift.Schema, int version = GisDrift.Version) =>
        new JObject
        {
            ["schema"] = schema,
            ["version"] = version,
            ["model"] = "abc",
            ["appliedVersion"] = "v/2",
            ["rows"] = new JArray(rows),
        }.ToString();

    /// <summary>Parameter values by (unique id, parameter, binding); a missing key is a missing parameter.</summary>
    private static Func<string, string, string, string?> Model(Dictionary<(string, string, string), string> values) =>
        (uniqueId, parameter, binding) => values.TryGetValue((uniqueId, parameter, binding), out string? value) ? value : null;

    [Fact]
    public void ReadsRowsAndTakesTheUniqueIdFromTheKeyWhenMissing()
    {
        JObject row = Entry("name", "北改札");
        row.Remove("uniqueId");

        GisDriftFile parsed = GisDrift.Parse(Text(new[] { row, Entry("alt_name", null) }));

        Assert.Equal("abc", parsed.Model);
        Assert.Equal("v/2", parsed.AppliedVersion);
        Assert.Equal("u-1", parsed.Rows[0].UniqueId);
        Assert.Equal("北改札", parsed.Rows[0].Gdb);
        Assert.Equal("", parsed.Rows[1].Gdb);
    }

    [Fact]
    public void KeepsDateLikeTextAsWritten()
    {
        GisDriftFile parsed = GisDrift.Parse(Text(new[] { Entry("name", "2026-11-08T12:00:00Z") }));

        Assert.Equal("2026-11-08T12:00:00Z", parsed.Rows[0].Gdb);
    }

    [Fact]
    public void RefusesAnotherSchemaOrVersion()
    {
        Assert.Throws<FormatException>(() => GisDrift.Parse(Text(Array.Empty<JObject>(), schema: "kanizu.link")));
        Assert.Throws<FormatException>(() => GisDrift.Parse(Text(Array.Empty<JObject>(), version: 2)));
        Assert.Throws<FormatException>(() => GisDrift.Parse("not json"));
    }

    [Fact]
    public void AcceptsVersionOneWrittenAsADecimal()
    {
        string text = Text(new[] { Entry("name", "北") }).Replace("\"version\": 1,", "\"version\": 1.0,");

        Assert.Contains("1.0", text);
        Assert.Equal("北", GisDrift.Parse(text).Rows[0].Gdb);
    }

    [Fact]
    public void AStationCategoryCodeIsWrittenAsItsRevitCategoryAndItsSideOfTheGates()
    {
        JObject inside = Entry("category", "walkway_sta", revit: "walkway");
        inside["gdbCategory"] = "walkway";
        inside["gdbRestriction"] = "rachi_nai";
        JObject store = Entry("category", "store", revit: "ticket office", uniqueId: "u-2");
        store["gdbCategory"] = "retail";
        JObject wrongSide = Entry("category", "walkway_sta", revit: "walkway", uniqueId: "u-3");
        wrongSide["gdbCategory"] = "walkway";
        wrongSide["gdbRestriction"] = "rachi_nai";
        var model = Model(new Dictionary<(string, string, string), string>
        {
            [("u-1", "category", "instance")] = "room",
            [("u-1", "gis_restriction", "type")] = "rachi_nai",
            [("u-2", "category", "instance")] = "ticketing",
            [("u-2", "gis_restriction", "type")] = "",
            [("u-3", "category", "instance")] = "walkway",
            [("u-3", "gis_restriction", "type")] = "rachi_gai",
        });

        var (changes, problems) = GisDrift.Plan(GisDrift.Parse(Text(new[] { inside, store, wrongSide })), model, Standard);

        Assert.Equal(
            new[] { ("u-1", "category", "walkway"), ("u-2", "category", "retail") },
            changes.Select(change => (change.UniqueId, change.Parameter, change.NewValue)));
        string problem = Assert.Single(problems);
        Assert.Contains("rachi_nai", problem);
        Assert.Contains("floor type", problem);
    }

    [Fact]
    public void ReadsAFileWrittenByKanizu()
    {
        // Written by Kanizu's DriftFile::save; see tests/Fixtures/Conformance/gis-drift/README.md.
        string path = Path.Combine(Path.GetDirectoryName(SourceFile())!, "..", "..", "Fixtures", "Conformance", "gis-drift", "8d7f3c1e-0000.gis-drift.json");
        GisDriftFile file = GisDrift.Read(path);
        Assert.Equal("8d7f3c1e-0000", file.Model);
        Assert.Equal("revit/lumine-8d7f3c1e/20261110T000000Z-aaaaaaaa", file.AppliedVersion);
        Assert.Equal(GisDrift.FileName(file.Model), Path.GetFileName(path));
        Assert.Equal(8, file.Rows.Count);
        Assert.Equal("", file.Rows[1].Revit);
        Assert.Equal("", file.Rows[5].Gdb);
        Assert.Equal(("walkway", "rachi_gai"), (file.Rows[4].GdbCategory, file.Rows[4].GdbRestriction));
        Assert.Equal(("", ""), (file.Rows[0].GdbCategory, file.Rows[0].GdbRestriction));
        var model = Model(new Dictionary<(string, string, string), string>
        {
            [("u-1", "name", "instance")] = "改札",
            [("u-1", "short_name", "instance")] = "",
            [("u-1", "kanizu_shop_hours", "type")] = "9-21",
            [("u-2", "category", "instance")] = "retail",
            [("u-2", "gis_restriction", "type")] = "rachi_gai",
            [("u-3", "name", "instance")] = "店舗",
            [("lvl", "ordinal", "instance")] = "1",
        });

        var (changes, problems) = GisDrift.Plan(file, model, Standard);

        Assert.Equal(
            new[]
            {
                "u-1 name 改札>北改札 -",
                "u-1 short_name >北 -",
                "u-1 kanizu_shop_hours 9-21>10-20 -",
                "u-2 category retail>walkway conflict",
                "u-3 name 店舗> -",
                "lvl ordinal 1>2 -",
            },
            changes.Select(change => $"{change.UniqueId} {change.Parameter} {change.OldValue}>{change.NewValue} {(change.Conflict ? "conflict" : "-")}"));
        // restrict (employeesonly to private) and level_id are listed for a change by hand.
        Assert.Equal(2, problems.Count);
        Assert.All(problems, problem => Assert.Contains("by hand", problem));
        Assert.Contains("restrict", problems[0]);
    }

    private static string SourceFile([System.Runtime.CompilerServices.CallerFilePath] string path = "") => path;

    [Fact]
    public void FileNameMatchesKanizus()
    {
        Assert.Equal("0f3a-B2.gis-drift.json", GisDrift.FileName("0f3a-B2"));
        Assert.Equal("a_b_c.gis-drift.json", GisDrift.FileName("a b|c"));
        Assert.Equal("____.gis-drift.json", GisDrift.FileName("新宿駅_"));
        Assert.Equal("0f3a", GisDrift.ModelKey(" 0F3A "));
    }

    [Fact]
    public void ReadsAFileWithAByteOrderMark()
    {
        string folder = Path.Combine(Path.GetTempPath(), "rgs-drift-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, GisDrift.FileName("abc"));
            byte[] body = new UTF8Encoding(false).GetBytes(Text(new[] { Entry("name", "北") }));
            File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(body).ToArray());

            Assert.Equal("北", GisDrift.Read(path).Rows[0].Gdb);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void CoreFieldsMapToTheirParametersAndStationFieldsToTheirOwn()
    {
        JObject[] rows =
        {
            Entry("name", "北改札", revit: "改札"),
            Entry("alt_name", "北"),
            Entry("restrict", "employeesonly"),
            Entry("shop_hours", "10-20", parameter: "kanizu_shop_hours", binding: "type"),
            Entry("ordinal", "2", layer: "level", revit: "1", uniqueId: "lvl"),
        };
        var model = Model(new Dictionary<(string, string, string), string>
        {
            [("u-1", "name", "instance")] = "改札",
            [("u-1", "short_name", "instance")] = "",
            [("u-1", "gis_restriction", "type")] = "",
            [("u-1", "kanizu_shop_hours", "type")] = "",
            [("lvl", "ordinal", "instance")] = "1",
        });

        var (changes, problems) = GisDrift.Plan(GisDrift.Parse(Text(rows)), model, Standard);

        // restrict holds IMDF restrictions in the GDB; gis_restriction is the side of the gates.
        Assert.Contains("by hand", Assert.Single(problems));
        Assert.Contains("restrict", problems[0]);
        Assert.Equal(
            new[]
            {
                ("name", "instance", "北改札"),
                ("short_name", "instance", "北"),
                ("kanizu_shop_hours", "type", "10-20"),
                ("ordinal", "instance", "2"),
            },
            changes.Select(change => (change.Parameter, change.Binding, change.NewValue)));
        Assert.Contains("every element of the type", changes[2].Label());
    }

    [Fact]
    public void RowsRevitAlreadyAgreesWithAreLeftOut()
    {
        JObject[] rows = { Entry("name", "北改札"), Entry("ordinal", "2", layer: "level", uniqueId: "lvl") };
        var model = Model(new Dictionary<(string, string, string), string>
        {
            [("u-1", "name", "instance")] = " 北改札",
            [("lvl", "ordinal", "instance")] = "2",
        });

        var (changes, problems) = GisDrift.Plan(GisDrift.Parse(Text(rows)), model, Standard);

        Assert.Empty(changes);
        Assert.Empty(problems);
    }

    [Fact]
    public void FieldsWithoutAParameterBadValuesAndMissingElementsAreProblems()
    {
        JObject[] rows =
        {
            Entry("level_id", "x"),
            Entry("category", "not-a-category"),
            Entry("ordinal", "two", layer: "level", uniqueId: "lvl"),
            Entry("name", "北", uniqueId: "gone"),
            Entry("direction", "上り", revit: "down"),
        };
        var model = Model(new Dictionary<(string, string, string), string>
        {
            [("u-1", "gis_direction", "instance")] = "down",
            [("lvl", "ordinal", "instance")] = "1",
        });

        var (changes, problems) = GisDrift.Plan(GisDrift.Parse(Text(rows)), model, Standard);

        Assert.Equal(new[] { ("gis_direction", "up") }, changes.Select(change => (change.Parameter, change.NewValue)));
        Assert.Equal(4, problems.Count);
        Assert.Contains("by hand", problems[0]);
        Assert.Contains("not-a-category", problems[1]);
        Assert.Contains("whole number", problems[2]);
        Assert.Contains("not in this model", problems[3]);
    }

    [Fact]
    public void ConflictsAreMarked()
    {
        JObject[] rows = { Entry("name", "北改札", revit: "南改札", @base: "改札", conflict: true) };
        var model = Model(new Dictionary<(string, string, string), string> { [("u-1", "name", "instance")] = "南改札" });

        var (changes, _) = GisDrift.Plan(GisDrift.Parse(Text(rows)), model, Standard);

        Assert.True(changes[0].Conflict);
        Assert.Contains("also changed in Revit", changes[0].Label());
    }

    [Fact]
    public void AcceptKeepsTickedChangesTheFileStillPlansAndListsTheRest()
    {
        GisDriftFile file = GisDrift.Parse(Text(new[] { Entry("name", "北改札"), Entry("alt_name", "北", uniqueId: "u-2") }));
        var model = Model(new Dictionary<(string, string, string), string>
        {
            [("u-1", "name", "instance")] = "改札",
            [("u-2", "short_name", "instance")] = "",
        });
        var (planned, _) = GisDrift.Plan(file, model, Standard);

        var (changes, skipped) = GisDrift.Accept(planned, new[]
        {
            ("u-1", "name", "instance", "北改札", "a"),
            ("u-1", "name", "instance", "北改札", "a"),
            ("u-2", "short_name", "instance", "南", "b"),
        });

        Assert.Equal(new[] { "u-1" }, changes.Select(change => change.UniqueId));
        Assert.Contains("Skipped b", Assert.Single(skipped));
    }
}
