using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

/// <summary>
/// The keep file as Kanizu's reader (kanizu-core/src/revit/keep.rs) takes it: schema
/// <c>kanizu.gis-keep</c>, version 1, the model, and entries of <c>revitKey</c>, <c>layer</c>,
/// <c>field</c>, <c>revit</c>, <c>gdb</c> and <c>keptUtc</c>.
/// </summary>
public sealed class GisKeepTests : IDisposable
{
    private const string Model = "8d7f3c1e-0000-4000-8000-000000000001";

    private readonly string _station;

    public GisKeepTests()
    {
        _station = Path.Combine(Path.GetTempPath(), "rgs-gis-keep", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_station);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_station, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static GisKeepEntry Entry(
        string field = "category",
        string revit = "retail",
        string gdb = "walkway",
        string keptUtc = "2026-11-21T09:30:00Z",
        string uniqueId = "u-2") =>
        new($"{Model}|{uniqueId}|0", "unit", field, revit, gdb, keptUtc);

    private static GisDriftRow Row(string revit, string gdb, string field = "name", string uniqueId = "u-2", string? revitKey = null) =>
        new(revitKey ?? $"{Model}|{uniqueId}|0", uniqueId, "unit", field, string.Empty, string.Empty, string.Empty, "改札", revit, gdb, revit, conflict: true);

    [Fact]
    public void WritesTheFileKanizuReads()
    {
        string text = GisKeep.Merge(null, Model, new[] { Entry() });

        string expected = string.Join(
            "\n",
            "{",
            "  \"schema\": \"kanizu.gis-keep\",",
            "  \"version\": 1,",
            "  \"model\": \"8d7f3c1e-0000-4000-8000-000000000001\",",
            "  \"entries\": [",
            "    {",
            "      \"revitKey\": \"8d7f3c1e-0000-4000-8000-000000000001|u-2|0\",",
            "      \"layer\": \"unit\",",
            "      \"field\": \"category\",",
            "      \"revit\": \"retail\",",
            "      \"gdb\": \"walkway\",",
            "      \"keptUtc\": \"2026-11-21T09:30:00Z\"",
            "    }",
            "  ]",
            "}",
            string.Empty);
        Assert.Equal(expected, text);
    }

    [Fact]
    public void TheSchemaVersionAndFileNameAreKanizus()
    {
        Assert.Equal("kanizu.gis-keep", GisKeep.Schema);
        Assert.Equal(1, GisKeep.Version);
        Assert.Equal("gis-drift", GisKeep.Folder);
        Assert.Equal(Model + ".gis-keep.json", GisKeep.FileName(Model));
        // Kanizu keeps ASCII letters, digits and '-' of the model and turns the rest into '_'.
        Assert.Equal("doc_C__A_B_rvt.gis-keep.json", GisKeep.FileName(@"doc:C:\A B.rvt"));
        Assert.Equal(
            Path.Combine(_station, "gis-drift", Model + ".gis-keep.json"),
            GisKeep.PathFor(_station, Model));
        Assert.Equal(
            Path.GetDirectoryName(Path.Combine(_station, GisDrift.Folder, GisDrift.FileName(Model))),
            Path.GetDirectoryName(GisKeep.PathFor(_station, Model)));
    }

    [Fact]
    public void WritesTheTimeToTheSecondInUtc()
    {
        DateTime local = new DateTime(2026, 11, 21, 9, 30, 0, 250, DateTimeKind.Utc).ToLocalTime();

        Assert.Equal("2026-11-21T09:30:00Z", GisKeep.Timestamp(local));
    }

    [Fact]
    public void AnAddedAnswerReadsBackFromTheStationFolder()
    {
        string path = GisKeep.PathFor(_station, Model);
        GisKeepEntry first = Entry();
        GisKeepEntry second = Entry(field: "name", revit: "北改札", gdb: string.Empty, uniqueId: "u-3");

        GisKeep.Add(path, Model, new[] { first });
        GisKeep.Add(path, Model, new[] { second });

        Assert.Equal(new[] { first, second }, GisKeep.Read(path, Model));
        byte[] bytes = File.ReadAllBytes(path);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal(new[] { Path.GetFileName(path) }, Directory.GetFiles(Path.GetDirectoryName(path)!).Select(Path.GetFileName));
    }

    [Fact]
    public void NoFileHoldsNoAnswers()
    {
        Assert.Empty(GisKeep.Read(GisKeep.PathFor(_station, Model), Model));
    }

    [Fact]
    public void AnAnswerGivenAgainReplacesItsRevitValueAndTime()
    {
        string text = GisKeep.Merge(null, Model, new[] { Entry(), Entry(gdb: "office") });

        string again = GisKeep.Merge(text, Model, new[] { Entry(revit: "restroom", keptUtc: "2026-11-22T10:00:00Z") });

        IReadOnlyList<GisKeepEntry> entries = GisKeep.Parse(again, Model);
        Assert.Equal(2, entries.Count);
        Assert.Equal(Entry(revit: "restroom", keptUtc: "2026-11-22T10:00:00Z"), entries[0]);
        Assert.Equal(Entry(gdb: "office"), entries[1]);
    }

    [Fact]
    public void KeepsWhatThisBuildDoesNotKnowAsItFoundIt()
    {
        string existing = new JObject
        {
            ["schema"] = GisKeep.Schema,
            ["version"] = GisKeep.Version,
            ["model"] = Model,
            ["writtenBy"] = "kanizu 0.9",
            ["entries"] = new JArray
            {
                new JObject
                {
                    ["revitKey"] = $"{Model}|u-2|0",
                    ["layer"] = "unit",
                    ["field"] = "category",
                    ["revit"] = "retail",
                    ["gdb"] = "walkway",
                    ["keptUtc"] = "2026-11-20T08:00:00Z",
                    ["note"] = "asked the station",
                },
                new JObject { ["revitKey"] = $"{Model}|u-7|0", ["field"] = "name", ["revit"] = new JArray("a", "b"), ["gdb"] = "x" },
                new JValue("not an entry"),
            },
        }.ToString();

        string merged = GisKeep.Merge(existing, Model, new[] { Entry(revit: "restroom", keptUtc: "2026-11-22T10:00:00Z"), Entry(field: "name", revit: "A", gdb: "B") });

        JObject root = JObject.Parse(merged);
        Assert.Equal("kanizu 0.9", (string?)root["writtenBy"]);
        JArray entries = (JArray)root["entries"]!;
        Assert.Equal(4, entries.Count);
        Assert.Equal("restroom", (string?)entries[0]["revit"]);
        Assert.Equal("asked the station", (string?)entries[0]["note"]);
        Assert.Contains("\"keptUtc\": \"2026-11-22T10:00:00Z\"", merged);
        Assert.Equal(JTokenType.Array, entries[1]["revit"]!.Type);
        Assert.Equal("not an entry", (string?)entries[2]);
        Assert.Equal("B", (string?)entries[3]["gdb"]);
        // The entry whose value is a list is no answer: Kanizu leaves it out, and so does this reader.
        Assert.Equal(2, GisKeep.Parse(merged, Model).Count);
    }

    [Fact]
    public void ATimeIsWrittenBackAsItWasRead()
    {
        string existing = GisKeep.Merge(null, Model, new[] { Entry(keptUtc: "2026-11-20T08:00:00.5Z") });

        string merged = GisKeep.Merge(existing, Model, new[] { Entry(field: "name", revit: "2026-11-08T12:00:00Z", gdb: "B") });

        Assert.Contains("\"keptUtc\": \"2026-11-20T08:00:00.5Z\"", merged);
        Assert.Contains("\"revit\": \"2026-11-08T12:00:00Z\"", merged);
    }

    [Fact]
    public void ReadsValuesAsKanizuDoes()
    {
        string text =
            "\uFEFF{\"schema\":\"kanizu.gis-keep\",\"version\":1,\"model\":\"" + Model + "\",\"entries\":[" +
            "{\"revitKey\":\"k|a|0\",\"layer\":\"level\",\"field\":\"ordinal\",\"revit\":2,\"gdb\":null,\"keptUtc\":\"t\"}," +
            "{\"revitKey\":\"k|b|0\",\"field\":\"flag\",\"revit\":true,\"gdb\":false}," +
            "{\"revitKey\":\"\",\"field\":\"name\",\"revit\":\"a\",\"gdb\":\"b\"}," +
            "{\"revitKey\":\"k|c|0\",\"field\":\"\",\"revit\":\"a\",\"gdb\":\"b\"}," +
            "{\"revitKey\":\"k|d|0\",\"field\":\"name\",\"revit\":{\"a\":1},\"gdb\":\"b\"}]}";

        IReadOnlyList<GisKeepEntry> entries = GisKeep.Parse(text, Model);

        Assert.Equal(2, entries.Count);
        Assert.Equal(new GisKeepEntry("k|a|0", "level", "ordinal", "2", string.Empty, "t"), entries[0]);
        Assert.Equal(new GisKeepEntry("k|b|0", string.Empty, "flag", "true", "false", string.Empty), entries[1]);
    }

    [Theory]
    [InlineData("{\"schema\":\"kanizu.gis-drift\",\"version\":1,\"model\":\"m\",\"entries\":[]}")]
    [InlineData("{\"schema\":\"kanizu.gis-keep\",\"version\":2,\"model\":\"m\",\"entries\":[]}")]
    [InlineData("{\"schema\":\"kanizu.gis-keep\",\"version\":1.0,\"model\":\"m\",\"entries\":[]}")]
    [InlineData("{\"schema\":\"kanizu.gis-keep\",\"version\":\"1\",\"model\":\"m\",\"entries\":[]}")]
    [InlineData("{\"schema\":\"kanizu.gis-keep\",\"version\":1,\"model\":\"another\",\"entries\":[]}")]
    [InlineData("{\"schema\":\"kanizu.gis-keep\",\"version\":1,\"entries\":[]}")]
    [InlineData("{\"schema\":\"kanizu.gis-keep\",\"version\":1,\"model\":\"m\",\"entries\":{}}")]
    [InlineData("[]")]
    [InlineData("not json")]
    [InlineData("")]
    public void RefusesAFileItCannotAnswerIn(string text)
    {
        Assert.Throws<FormatException>(() => GisKeep.Parse(text, "m"));
        Assert.Throws<FormatException>(() => GisKeep.Merge(text, "m", new[] { Entry() }));
    }

    [Fact]
    public void AFileOfAnotherModelIsLeftAlone()
    {
        string path = GisKeep.PathFor(_station, Model);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string other = GisKeep.Merge(null, "another-model", new[] { Entry() });
        File.WriteAllText(path, other, new UTF8Encoding(false));

        Assert.Throws<FormatException>(() => GisKeep.Add(path, Model, new[] { Entry(field: "name") }));

        Assert.Equal(other, File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void AddingNothingWritesNoFile()
    {
        string path = GisKeep.PathFor(_station, Model);

        GisKeep.Add(path, Model, Array.Empty<GisKeepEntry>());

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void ADriftRowIsAnsweredWithTheValuesTheDriftFileLists()
    {
        (IReadOnlyList<GisKeepEntry> entries, IReadOnlyList<string> skipped) = GisKeep.EntriesFor(
            new[] { Row("改札", "北改札") },
            "2026-11-21T09:30:00Z");

        Assert.Empty(skipped);
        Assert.Equal(new GisKeepEntry($"{Model}|u-2|0", "unit", "name", "改札", "北改札", "2026-11-21T09:30:00Z"), Assert.Single(entries));
    }

    [Fact]
    public void RowsKanizuWouldNeverTakeAreSkippedWithAReason()
    {
        (IReadOnlyList<GisKeepEntry> entries, IReadOnlyList<string> skipped) = GisKeep.EntriesFor(
            new[]
            {
                Row(string.Empty, "北改札"),
                Row("改札", "改札", uniqueId: "u-3"),
                Row("改札", "北改札", revitKey: string.Empty),
                Row("改札", string.Empty, uniqueId: "u-5"),
                Row("改札", string.Empty, uniqueId: "u-5"),
            },
            "t");

        GisKeepEntry kept = Assert.Single(entries);
        Assert.Equal($"{Model}|u-5|0", kept.RevitKey);
        Assert.Equal(string.Empty, kept.Gdb);
        Assert.Equal(3, skipped.Count);
        Assert.Contains("empty", skipped[0]);
        Assert.Contains("same value", skipped[1]);
        Assert.Contains("no element key", skipped[2]);
    }
}
