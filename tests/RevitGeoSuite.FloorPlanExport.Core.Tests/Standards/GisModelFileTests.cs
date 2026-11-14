using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class GisModelFileTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gis-model-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void PathFor_KeysTheFileByModelIdInsideTheStationFolder()
    {
        string path = GisModelFile.PathFor(_folder, "AB12CD34-0000-4000-8000-000000000001");

        Assert.Equal(
            Path.Combine(_folder, "gis-models", "ab12cd34-0000-4000-8000-000000000001.gis-model.json"),
            path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("a/b")]
    public void PathFor_RefusesAnIdThatIsNotAFileName(string modelId)
    {
        Assert.Throws<ArgumentException>(() => GisModelFile.PathFor(_folder, modelId));
    }

    [Fact]
    public void Defaults_GiveTheStandardStairSettings()
    {
        StairDrawingSettings settings = GisModelFile.CreateDefault().ToStairDrawingSettings();

        Assert.Equal(0.60d, settings.StepSpacingMeters);
        Assert.Equal(1.10d, settings.LineGapBeforeMiddleMeters);
        Assert.Equal(1.80d, settings.FloorOverlapMeters);
    }

    [Fact]
    public void Template_ReadsBackAsTheDefaults()
    {
        Assert.Equal(GisModelFile.CreateDefault().ToJson(), GisModelFile.CreateFromTemplate().ToJson());
    }

    [Fact]
    public void Parse_FillsMissingSectionsAndNumbersWithTheStandard()
    {
        GisModelFile file = GisModelFile.Parse(
            "{\"schema\":\"revitgeosuite.gis-model\",\"version\":1,\"drawingRules\":{\"stairs\":{\"stepSpacingMeters\":0.3,\"overlapMeters\":null}}}",
            "test");

        StairDrawingSettings settings = file.ToStairDrawingSettings();
        Assert.Equal(0.30d, settings.StepSpacingMeters);
        Assert.Equal(1.10d, settings.LineGapBeforeMiddleMeters);
        Assert.Equal(1.80d, settings.FloorOverlapMeters);
        Assert.Empty(file.Levels);
        Assert.Empty(file.Views);
        Assert.Equal(GisModelFile.StandardFields, file.Fields);
        Assert.NotNull(file.Identity);
        Assert.NotNull(file.Versions);
    }

    [Fact]
    public void Parse_KeepsAZeroOverlapForExactTiling()
    {
        GisModelFile file = GisModelFile.Parse(
            "{\"schema\":\"revitgeosuite.gis-model\",\"version\":1,\"drawingRules\":{\"stairs\":{\"overlapMeters\":0}}}",
            "test");

        Assert.Equal(0d, file.ToStairDrawingSettings().FloorOverlapMeters);
    }

    [Theory]
    [InlineData("{\"schema\":\"something-else\",\"version\":1}")]
    [InlineData("{\"schema\":\"revitgeosuite.gis-model\",\"version\":2}")]
    [InlineData("{\"schema\":\"revitgeosuite.gis-model\"}")]
    [InlineData("not json")]
    public void Parse_RefusesAFileItCannotRead(string json)
    {
        Assert.Throws<InvalidDataException>(() => GisModelFile.Parse(json, "test"));
    }

    [Fact]
    public void SaveAndLoad_RoundTripAndKeepUnknownFields()
    {
        string path = GisModelFile.PathFor(_folder, "11111111-2222-4333-8444-555555555555");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(
            path,
            "{\"schema\":\"revitgeosuite.gis-model\",\"version\":1,\"futureSection\":{\"a\":1}," +
            "\"views\":[{\"uniqueId\":\"v-1\",\"name\":\"GIS_1F\"}]}",
            new UTF8Encoding(false));

        GisModelFile file = GisModelFile.TryLoad(path)!;
        file.Identity.Station = "新宿";
        file.Levels.Add(new GisModelLevel { UniqueId = "l-1", Name = "1FL", LevelId = "L1", Ordinal = 0, ShortName = "1F" });
        file.Save(path);

        string written = File.ReadAllText(path, Encoding.UTF8);
        Assert.Contains("\"futureSection\"", written);
        Assert.Contains("新宿", written);
        GisModelFile reloaded = GisModelFile.TryLoad(path)!;
        Assert.Equal("GIS_1F", reloaded.Views.Single().Name);
        Assert.Equal(0, reloaded.Levels.Single().Ordinal);
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp-*"));
    }

    [Fact]
    public void TryLoad_ReturnsNullWhenThereIsNoFile()
    {
        Assert.Null(GisModelFile.TryLoad(Path.Combine(_folder, "missing.gis-model.json")));
    }

    [Fact]
    public void MatchViews_FindsByUniqueIdThenByNameInTheFileOrder()
    {
        GisModelFile file = GisModelFile.CreateDefault();
        file.Views.Add(new GisModelView { UniqueId = "gone", Name = "GIS_2F" });
        file.Views.Add(new GisModelView { UniqueId = "u-1", Name = "renamed" });
        file.Views.Add(new GisModelView { UniqueId = "missing", Name = "missing" });
        List<(string Id, string Name)> available = new()
        {
            ("u-1", "GIS_1F"),
            ("u-2", "GIS_2F"),
            ("u-3", "Working"),
        };

        IReadOnlyList<(string Id, string Name)> matched = file.MatchViews(available, view => view.Id, view => view.Name);

        Assert.Equal(new[] { "u-2", "u-1" }, matched.Select(view => view.Id));
    }

    [Fact]
    public void ChooseDefaultViews_PrefersTheFileThenGisViewsThenAll()
    {
        List<(string Id, string Name)> available = new()
        {
            ("u-1", "1F 作業用"),
            ("u-2", "GIS_1F"),
            ("u-3", "gis_2F"),
        };
        GisModelFile listing = GisModelFile.CreateDefault();
        listing.Views.Add(new GisModelView { UniqueId = "u-1", Name = "1F 作業用" });
        GisModelFile stale = GisModelFile.CreateDefault();
        stale.Views.Add(new GisModelView { UniqueId = "gone", Name = "gone" });

        Assert.Equal(new[] { "u-1" }, Ids(GisModelFile.ChooseDefaultViews(available, view => view.Id, view => view.Name, listing)));
        Assert.Equal(new[] { "u-2", "u-3" }, Ids(GisModelFile.ChooseDefaultViews(available, view => view.Id, view => view.Name, stale)));
        Assert.Equal(new[] { "u-2", "u-3" }, Ids(GisModelFile.ChooseDefaultViews(available, view => view.Id, view => view.Name, null)));
        Assert.Equal(
            new[] { "u-1" },
            Ids(GisModelFile.ChooseDefaultViews(available.Take(1), view => view.Id, view => view.Name, null)));
    }

    private static IEnumerable<string> Ids(IEnumerable<(string Id, string Name)> views) => views.Select(view => view.Id);

    [Fact]
    public void Profile_IsStationWhenTheFileNamesNone_AndIsKeptWhenItNamesOne()
    {
        GisModelFile none = GisModelFile.Parse("{\"schema\":\"revitgeosuite.gis-model\",\"version\":1}", "test");
        GisModelFile named = GisModelFile.Parse("{\"schema\":\"revitgeosuite.gis-model\",\"version\":1,\"profile\":\"Night\"}", "test");

        Assert.Equal("Station", none.ProfileName);
        Assert.DoesNotContain("rofile", none.ToJson());
        Assert.Equal("Night", named.ProfileName);
        Assert.Contains("\"profile\": \"Night\"", named.ToJson());
    }

    [Fact]
    public void Links_AreAbsentUntilAnExportRecordsThem_AndRoundTripByUniqueId()
    {
        GisModelFile none = GisModelFile.Parse("{\"schema\":\"revitgeosuite.gis-model\",\"version\":1}", "test");
        GisModelFile recorded = GisModelFile.CreateDefault();
        recorded.Links = new List<GisModelLink> { new() { UniqueId = "link-platform-1", Name = "Platform.rvt : 1" } };

        GisModelFile read = GisModelFile.Parse(recorded.ToJson(), "test");

        Assert.Null(none.Links);
        Assert.DoesNotContain("\"links\"", none.ToJson());
        Assert.Equal("link-platform-1", Assert.Single(read.Links!).UniqueId);
        Assert.Equal("Platform.rvt : 1", read.Links![0].Name);
        Assert.Contains("\"links\": []", GisModelFile.Parse("{\"schema\":\"revitgeosuite.gis-model\",\"version\":1,\"links\":[]}", "test").ToJson());
    }

    [Fact]
    public void MatchLinks_FindsLinksByUniqueIdAndNamesTheOnesThatAreGone()
    {
        GisModelFile file = GisModelFile.CreateDefault();
        file.Links = new List<GisModelLink>
        {
            new() { UniqueId = "link-b", Name = "B.rvt : 1" },
            new() { UniqueId = "link-gone", Name = "Gone.rvt : 1" },
        };
        (string Id, long ElementId)[] available = { ("LINK-B", 22), ("link-c", 33) };

        (IReadOnlyList<(string Id, long ElementId)> found, IReadOnlyList<GisModelLink> missing) = file.MatchLinks(available, link => link.Id);

        Assert.Equal(new long[] { 22 }, found.Select(link => link.ElementId));
        Assert.Equal("Gone.rvt : 1", Assert.Single(missing).Name);
    }
}
