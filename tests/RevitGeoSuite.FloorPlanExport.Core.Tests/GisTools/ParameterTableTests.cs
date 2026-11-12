using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class ParameterTableTests
{
    private static KeyValuePair<string, string> Cell(string column, string value) => new(column, value);

    private static List<IReadOnlyDictionary<string, string>> ModelRows() => new()
    {
        ParameterTable.Row(101, "floor", "1FL", "j ホーム_床", "m|u1|0", new[] { Cell("name", ""), Cell("category", "") }),
        ParameterTable.Row(102, "stair", "1FL", values: new[] { Cell("name", "階段A"), Cell("gis_direction", "both") }),
        ParameterTable.Row(103, "family", familyType: "ESC : 1200", values: new[] { Cell("gis_direction", "") }),
        ParameterTable.Row(104, "level", "1FL", values: new[] { Cell("short_name", "1F"), Cell("ordinal", "0") }),
    };

    private static List<Dictionary<string, string>> Edited() => ParameterTable.ParseCsvText(ParameterTable.ToCsvText(ModelRows())).ToList();

    private static (IReadOnlyList<ParameterTableChange> Changes, IReadOnlyList<string> Problems) Diff(IEnumerable<Dictionary<string, string>> edited) =>
        ParameterTable.Diff(ModelRows(), edited, Standard);

    [Fact]
    public void RoundTripsThroughTextWithCommasQuotesAndJapanese()
    {
        IReadOnlyList<Dictionary<string, string>> parsed = ParameterTable.ParseCsvText(ParameterTable.ToCsvText(new[]
        {
            ParameterTable.Row(1, "floor", values: new[] { Cell("name", "改札, \"北\""), Cell("short_name", " 1F"), Cell("category", "walkway") }),
        }));

        Assert.Equal("改札, \"北\"", parsed[0]["name"]);
        Assert.Equal(" 1F", parsed[0]["short_name"]);
        Assert.Equal("1", parsed[0]["element_id"]);
    }

    [Fact]
    public void ReadsALineBreakInsideQuotesAndSkipsBlankLines()
    {
        IReadOnlyList<Dictionary<string, string>> parsed = ParameterTable.ParseCsvText("element_id,name\r\n1,\"a\nb\"\r\n\r\n2,c");

        Assert.Equal(new[] { "a\nb", "c" }, parsed.Select(entry => entry["name"]));
    }

    [Fact]
    public void RefusesAFileWithoutElementId()
    {
        Assert.Throws<FormatException>(() => ParameterTable.ParseCsvText("id,name\n1,a\n"));
    }

    [Fact]
    public void UnknownColumnIsRefused()
    {
        Assert.Throws<ArgumentException>(() => ParameterTable.Row(1, "floor", values: new[] { Cell("colour", "red") }));
    }

    [Fact]
    public void WritesUtf8WithBomAndReadsCp932FromExcel()
    {
        string folder = Path.Combine(Path.GetTempPath(), "rgs-parameters-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string path = Path.Combine(folder, "parameters.csv");
            ParameterTable.WriteCsv(path, new[] { ParameterTable.Row(1, "floor", values: new[] { Cell("name", "ホーム") }) });
            byte[] written = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, written.Take(3));
            Assert.Equal("ホーム", ParameterTable.ReadCsv(path)[0]["name"]);

            File.WriteAllBytes(path, Encoding.GetEncoding(932).GetBytes("element_id,name\r\n1,ホーム\r\n"));
            Assert.Equal("ホーム", ParameterTable.ReadCsv(path)[0]["name"]);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void UnchangedFileChangesNothing()
    {
        var (changes, problems) = Diff(Edited());

        Assert.Empty(changes);
        Assert.Empty(problems);
    }

    [Fact]
    public void ListsOnlyChangedCellsInOwnedColumns()
    {
        List<Dictionary<string, string>> edited = Edited();
        edited[0]["name"] = "1番線ホーム";
        edited[0]["category"] = "Platform";
        edited[1]["gis_direction"] = "上り";
        edited[3]["ordinal"] = "1";
        edited[3]["level"] = "renamed in Excel";

        var (changes, problems) = Diff(edited);

        Assert.Empty(problems);
        Assert.Equal(
            new[] { ("101", "name", "1番線ホーム"), ("101", "category", "Platform"), ("102", "gis_direction", "up"), ("104", "ordinal", "1") },
            changes.Select(change => (change.ElementId, change.Column, change.NewValue)));
    }

    [Fact]
    public void ReportsValuesAndColumnsItWillNotWrite()
    {
        List<Dictionary<string, string>> edited = Edited();
        edited[0]["category"] = "not-a-category";
        edited[0]["ordinal"] = "3";
        edited[1]["gis_direction"] = "sideways";
        edited[3]["ordinal"] = "one";
        edited.Add(new Dictionary<string, string>(edited[2]));
        edited.Add(new Dictionary<string, string>(edited[2]) { ["element_id"] = "999" });

        var (changes, problems) = Diff(edited);

        Assert.Empty(changes);
        Assert.Equal(6, problems.Count);
        Assert.Contains("not a unit category", problems[0]);
        Assert.Contains("ordinal cannot be set on a floor", problems[1]);
        Assert.Contains("sideways", problems[2]);
        Assert.Contains("not a whole number", problems[3]);
        Assert.Contains("appears twice", problems[4]);
        Assert.Contains("999 is not in the model", problems[5]);
    }

    [Fact]
    public void AMissingColumnOrDeletedRowChangesNothing()
    {
        List<Dictionary<string, string>> edited = Edited()
            .Take(2)
            .Select(entry => entry.Where(cell => cell.Key != "name").ToDictionary(cell => cell.Key, cell => cell.Value))
            .ToList();

        var (changes, problems) = Diff(edited);

        Assert.Empty(changes);
        Assert.Empty(problems);
    }

    [Fact]
    public void EmptyingANameIsAChange()
    {
        List<Dictionary<string, string>> edited = Edited();
        edited[1]["name"] = "";

        var (changes, _) = Diff(edited);

        Assert.Equal(new[] { ("name", "階段A", "") }, changes.Select(change => (change.Column, change.OldValue, change.NewValue)));
    }
}
