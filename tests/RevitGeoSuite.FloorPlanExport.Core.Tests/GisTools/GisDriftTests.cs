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
            Entry("restrict", "rachi_nai"),
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

        Assert.Empty(problems);
        Assert.Equal(
            new[]
            {
                ("name", "instance", "北改札"),
                ("short_name", "instance", "北"),
                ("gis_restriction", "type", "rachi_nai"),
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
}
