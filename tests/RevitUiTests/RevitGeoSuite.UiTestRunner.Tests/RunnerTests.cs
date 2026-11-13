using System.Text.Json.Nodes;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

public class DevToolsPortTests
{
    [Theory]
    [InlineData("53412\n/devtools/browser/0b6c\n", 53412)]
    [InlineData("9222\r\n", 9222)]
    public void Reads_the_port_from_the_first_line(string content, int port) => Assert.Equal(port, DevToolsPort.Parse(content));

    [Theory]
    [InlineData("")]
    [InlineData("0\n")]
    [InlineData("/devtools/browser/x\n")]
    public void Rejects_a_file_without_a_port(string content) => Assert.Throws<FormatException>(() => DevToolsPort.Parse(content));

    [Fact]
    public void Refuses_a_file_left_from_an_earlier_Revit()
    {
        string file = Path.GetTempFileName();
        File.WriteAllText(file, "1234\n");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddHours(-1));

        Assert.Throws<InvalidOperationException>(() => DevToolsPort.Read(file, DateTime.UtcNow.AddMinutes(-5)));
        Assert.Equal(1234, DevToolsPort.Read(file, DateTime.UtcNow.AddHours(-2)));
    }
}

public class JournalScanTests
{
    [Fact]
    public void Passes_when_every_name_is_there_and_nothing_failed()
    {
        string[] lines =
        {
            "Jrn.Data \"Transaction Successful\" , \"Kanizu GIS: Set up model\"",
            "Jrn.Command \"Ribbon\" , \"Undo , ID_EDIT_UNDO\"",
            "' 0:< Kanizu GIS: Fix floor types",
        };

        JournalResult result = JournalScan.Scan(lines, new[] { "Kanizu GIS: Set up model", "Kanizu GIS: Fix floor types" });

        Assert.True(result.Passed);
        Assert.Equal(2, result.KanizuLines.Count);
    }

    [Fact]
    public void Reports_missing_names_and_error_lines()
    {
        string[] lines = { "' HANDLER_ERROR gisStandard.setup", "Revit did not commit the transaction" };

        JournalResult result = JournalScan.Scan(lines, new[] { "Kanizu GIS: Parameters" });

        Assert.False(result.Passed);
        Assert.Equal(new[] { "Kanizu GIS: Parameters" }, result.MissingNames);
        Assert.Equal(2, result.BadLines.Count);
    }
}

public class RunOptionsTests
{
    [Fact]
    public void Parses_every_option()
    {
        RunOptions options = RunOptions.Parse(new[]
        {
            "--model", "/tmp/revit-test/pristine.rvt", "--out", "/tmp/revit-test/auto/1", "--steps", "1, 2,10",
            "--export-view", "1F", "--excel", "--real-drift", "--revit-year", "2027",
        });

        Assert.Equal("/tmp/revit-test/pristine.rvt", options.Model);
        Assert.True(options.Runs("10"));
        Assert.False(options.Runs("3"));
        Assert.Equal("1F", options.ExportView);
        Assert.True(options.Excel);
        Assert.True(options.RealDrift);
        Assert.Equal("2027", options.RevitYear);
    }

    [Fact]
    public void Picks_the_script()
    {
        Assert.Equal(RunOptions.GisStandardScript, RunOptions.Parse(new[] { "--model", "/tmp/revit-test/a.rvt" }).Script);
        Assert.Equal(RunOptions.FloorTypesScript, RunOptions.Parse(new[] { "--model", "/tmp/revit-test/a.rvt", "--script", "floor-types" }).Script);
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(new[] { "--model", "/tmp/revit-test/a.rvt", "--script", "everything" }));
    }

    [Fact]
    public void Needs_a_model() => Assert.Throws<ArgumentException>(() => RunOptions.Parse(new[] { "--excel" }));

    [Fact]
    public void Refuses_a_model_outside_the_test_folder()
    {
        RunOptions inside = RunOptions.Parse(new[] { "--model", "/tmp/revit-test/a.rvt", "--out", "/tmp/revit-test/auto/1" });
        RunOptions original = inside with { Model = "/tmp/models/LUMINE.rvt" };
        RunOptions sibling = inside with { Model = "/tmp/revit-test-other/a.rvt" };

        inside.RequireInside("/tmp/revit-test");
        Assert.Throws<ArgumentException>(() => original.RequireInside("/tmp/revit-test"));
        Assert.Throws<ArgumentException>(() => sibling.RequireInside("/tmp/revit-test"));
    }
}

public class RenameOptionTests
{
    [Fact]
    public void Defaults_to_the_run_2_renames()
    {
        RunOptions options = RunOptions.Parse(new[] { "--model", "/tmp/revit-test/a.rvt" });

        Assert.Equal(RunOptions.DefaultRenames, options.Renames);
    }

    [Fact]
    public void Replaces_the_defaults_with_the_given_renames()
    {
        RunOptions options = RunOptions.Parse(new[] { "--model", "/tmp/revit-test/a.rvt", "--rename-type", "Toilet => Toilet ラチ内" });

        Assert.Equal(new[] { ("Toilet", "Toilet ラチ内") }, options.Renames);
    }

    [Fact]
    public void Rejects_a_rename_without_an_arrow() =>
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(new[] { "--model", "/tmp/revit-test/a.rvt", "--rename-type", "Toilet" }));
}

public class DriftFixtureTests
{
    [Fact]
    public void Writes_one_name_row_in_the_kanizu_drift_schema()
    {
        JsonObject file = JsonNode.Parse(DriftFixture.Write("lumine-id", "abc-123", "北改札", "UIテスト"))!.AsObject();

        Assert.Equal("kanizu.gis-drift", file["schema"]!.GetValue<string>());
        Assert.Equal(1, file["version"]!.GetValue<int>());
        Assert.Equal("lumine-id", file["model"]!.GetValue<string>());
        JsonObject row = Assert.Single(file["rows"]!.AsArray())!.AsObject();
        Assert.Equal("lumine-id|abc-123|0", row["revitKey"]!.GetValue<string>());
        Assert.Equal(("unit", "name"), (row["layer"]!.GetValue<string>(), row["field"]!.GetValue<string>()));
        Assert.Equal("北改札", row["revit"]!.GetValue<string>());
        Assert.Equal("UIテスト", row["gdb"]!.GetValue<string>());
        Assert.False(row["conflict"]!.GetValue<bool>());
    }

    [Fact]
    public void Has_the_same_fields_as_the_conformance_fixture()
    {
        string fixture = Path.Combine(AppContext.BaseDirectory, "fixtures", "other-model.gis-drift.json");
        JsonObject expected = JsonNode.Parse(File.ReadAllText(fixture))!.AsObject();
        JsonObject written = JsonNode.Parse(DriftFixture.Write("m", "u", "a", "b"))!.AsObject();

        Assert.Equal(expected.Select(pair => pair.Key).OrderBy(key => key), written.Select(pair => pair.Key).OrderBy(key => key));
        Assert.Equal(
            expected["rows"]!.AsArray().SelectMany(row => row!.AsObject().Select(pair => pair.Key)).Distinct().OrderBy(key => key),
            written["rows"]![0]!.AsObject().Select(pair => pair.Key).OrderBy(key => key));
    }
}
