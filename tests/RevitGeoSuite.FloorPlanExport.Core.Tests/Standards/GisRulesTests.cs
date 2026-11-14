using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Validation;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class GisRulesTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "gis-rules-tests-" + Guid.NewGuid().ToString("N"));

    public GisRulesTests()
    {
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string Station => Path.Combine(_folder, "station");

    private string RulesPath => GisRulesFile.PathFor(Station);

    private GisRulesStore NewStore(GisRulesStoreOptions? options = null)
    {
        Directory.CreateDirectory(Station);
        return new GisRulesStore(RulesPath, options ?? FastOptions());
    }

    private static GisRulesStoreOptions FastOptions()
    {
        return new GisRulesStoreOptions
        {
            Sleep = _ => { },
            Stamp = () => new GisRulesChanged { Utc = "2026-11-14T00:00:00Z", User = "tester", RevitGeoSuite = "1.0" },
        };
    }

    private static MappingRule Floor(string match, string value) =>
        new() { RuleType = MappingRuleType.FloorCategory, MatchValue = match, ResolvedValue = value };

    private static GisRulesProfile Profile(string name, int epsg = 6677) =>
        new() { Name = name, TargetEpsg = epsg };

    [Fact]
    public void Apply_RoundTripsRulesAndProfilesThroughTheFile()
    {
        GisRulesStore store = NewStore();

        store.Apply(new[]
        {
            GisRulesOperation.UpsertRule(Floor("Concourse", "walkway")),
            GisRulesOperation.UpsertRule(new MappingRule { RuleType = MappingRuleType.AcceptedOpeningFamily, MatchValue = "Gate" }),
            GisRulesOperation.UpsertProfile(Profile("Station", 6676)),
        });

        GisRulesFile read = GisRulesFile.Parse(File.ReadAllText(RulesPath), "test");
        ProjectMappingRules rules = read.ToMappingRules();
        Assert.Equal("walkway", rules.FloorCategoryOverrides["Concourse"]);
        Assert.Contains("Gate", rules.AcceptedOpeningFamilies);
        Assert.Equal(6676, read.FindProfile("station")!.TargetEpsg);
        Assert.Equal("tester", read.Changed.User);
        Assert.Contains("\"type\": \"floorCategory\"", File.ReadAllText(RulesPath));
    }

    [Fact]
    public void Apply_KeepsFieldsThisVersionDoesNotKnow()
    {
        Directory.CreateDirectory(Station);
        File.WriteAllText(RulesPath, @"{
  ""schema"": ""revitgeosuite.gis-rules"",
  ""version"": 1,
  ""futureTopLevel"": { ""a"": 1 },
  ""categoryRules"": [ { ""type"": ""floorCategory"", ""match"": ""A"", ""value"": ""walkway"", ""futureRuleField"": ""x"" } ],
  ""profiles"": [ { ""name"": ""Station"", ""futureProfileField"": 7 } ]
}");

        NewStore().Apply(new[] { GisRulesOperation.UpsertRule(Floor("B", "retail")) });

        string text = File.ReadAllText(RulesPath);
        Assert.Contains("futureTopLevel", text);
        Assert.Contains("futureRuleField", text);
        Assert.Contains("futureProfileField", text);
        Assert.Equal(2, GisRulesFile.Parse(text, "test").CategoryRules.Count);
    }

    [Fact]
    public void Apply_RefusesAFileOfANewerVersion()
    {
        Directory.CreateDirectory(Station);
        File.WriteAllText(RulesPath, "{\"schema\":\"revitgeosuite.gis-rules\",\"version\":2}");

        Assert.Throws<InvalidDataException>(() => NewStore().Apply(new[] { GisRulesOperation.UpsertRule(Floor("A", "walkway")) }));
        Assert.Contains("\"version\":2", File.ReadAllText(RulesPath));
    }

    [Fact]
    public void Apply_KeepsAnotherPcsRuleWhenItsWriteReplacedOursBeforeWeReadBack()
    {
        string otherPc = null!;
        GisRulesStoreOptions options = FastOptions();
        int writes = 0;
        options.AfterWrite = () =>
        {
            if (++writes == 1)
            {
                File.WriteAllText(RulesPath, otherPc);
            }
        };
        GisRulesStore store = NewStore(options);
        otherPc = new GisRulesFile { CategoryRules = { GisRulesRule.From(Floor("Other", "retail")) } }.ToJson();

        store.Apply(new[] { GisRulesOperation.UpsertRule(Floor("Mine", "walkway")) });

        IReadOnlyDictionary<string, string> floors = GisRulesFile.TryLoad(RulesPath)!.ToMappingRules().FloorCategoryOverrides;
        Assert.Equal("walkway", floors["Mine"]);
        Assert.Equal("retail", floors["Other"]);
    }

    [Fact]
    public void Apply_ChangesOnlyItsOwnKeyWhenTheFileChangedSinceTheCallerLoaded()
    {
        GisRulesStore pcA = NewStore();
        GisRulesStore pcB = new(RulesPath, FastOptions());
        pcA.Apply(new[] { GisRulesOperation.UpsertRule(Floor("Shared", "walkway")) });

        pcB.Apply(new[] { GisRulesOperation.UpsertRule(Floor("OnlyB", "retail")) });
        pcA.Apply(new[] { GisRulesOperation.UpsertRule(Floor("OnlyA", "ticketing")) });

        IReadOnlyDictionary<string, string> floors = GisRulesFile.TryLoad(RulesPath)!.ToMappingRules().FloorCategoryOverrides;
        Assert.Equal(3, floors.Count);
    }

    [Fact]
    public void Apply_LastWriteWinsOnTheSameKey()
    {
        GisRulesStore store = NewStore();
        store.Apply(new[] { GisRulesOperation.UpsertRule(Floor("A", "walkway")) });
        store.Apply(new[] { GisRulesOperation.UpsertRule(Floor("A", "retail")) });

        Assert.Equal("retail", GisRulesFile.TryLoad(RulesPath)!.ToMappingRules().FloorCategoryOverrides["A"]);
    }

    private string LockPath => Path.Combine(Station, GisRulesStore.LockFileName);

    [Fact]
    public void Apply_KeepsBothKeysWhenAnotherPcSavesBetweenOurReadAndOurWrite()
    {
        GisRulesStore pcA = NewStore();
        pcA.Apply(new[] { GisRulesOperation.UpsertRule(Floor("Shared", "walkway")) });
        ManualResetEventSlim pcBWaited = new();
        GisRulesStoreOptions pcBOptions = FastOptions();
        pcBOptions.WriteAttempts = 400;
        pcBOptions.BackoffMilliseconds = _ => 5;
        pcBOptions.Sleep = milliseconds =>
        {
            pcBWaited.Set();
            Thread.Sleep(milliseconds);
        };
        GisRulesStore pcB = new(RulesPath, pcBOptions);
        Thread? pcBSave = null;
        Exception? pcBError = null;
        GisRulesStoreOptions pcAOptions = FastOptions();
        pcAOptions.AfterRead = () =>
        {
            if (pcBSave != null)
            {
                return;
            }

            pcBSave = new Thread(() =>
            {
                try
                {
                    pcB.Apply(new[] { GisRulesOperation.UpsertRule(Floor("OnlyB", "retail")) });
                }
                catch (Exception ex)
                {
                    pcBError = ex;
                }
            });
            pcBSave.Start();
            Assert.True(SpinWait.SpinUntil(() => pcBWaited.IsSet || !pcBSave.IsAlive, TimeSpan.FromSeconds(10)));
        };

        new GisRulesStore(RulesPath, pcAOptions).Apply(new[] { GisRulesOperation.UpsertRule(Floor("OnlyA", "ticketing")) });
        Assert.True(pcBSave!.Join(TimeSpan.FromSeconds(10)));

        Assert.Null(pcBError);
        IReadOnlyDictionary<string, string> floors = GisRulesFile.TryLoad(RulesPath)!.ToMappingRules().FloorCategoryOverrides;
        Assert.Equal("ticketing", floors["OnlyA"]);
        Assert.Equal("retail", floors["OnlyB"]);
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public void Apply_WaitsForAnotherPcsLockThenFailsNamingItAndKeepsTheFile()
    {
        NewStore().Apply(new[] { GisRulesOperation.UpsertRule(Floor("Old", "walkway")) });
        string before = File.ReadAllText(RulesPath);
        File.WriteAllText(LockPath, "{\"machine\":\"PC-SHINJUKU-07\",\"user\":\"hanako\",\"utc\":\"2026-11-14T09:00:00Z\"}");
        List<int> waits = new();
        GisRulesStoreOptions options = FastOptions();
        options.Sleep = waits.Add;

        IOException error = Assert.Throws<IOException>(() =>
            new GisRulesStore(RulesPath, options).Apply(new[] { GisRulesOperation.UpsertRule(Floor("New", "retail")) }));

        Assert.Equal(new[] { 100, 200, 400, 800 }, waits);
        Assert.Contains("hanako", error.Message);
        Assert.Contains("PC-SHINJUKU-07", error.Message);
        Assert.Equal(before, File.ReadAllText(RulesPath));
        Assert.True(File.Exists(LockPath));
    }

    [Fact]
    public void Apply_TakesOverALockOlderThanThirtySeconds()
    {
        Directory.CreateDirectory(Station);
        File.WriteAllText(LockPath, "{\"machine\":\"PC-CRASHED\",\"user\":\"taro\",\"utc\":\"2026-11-14T09:00:00Z\"}");
        File.SetLastWriteTimeUtc(LockPath, DateTime.UtcNow.AddSeconds(-31));

        NewStore().Apply(new[] { GisRulesOperation.UpsertRule(Floor("A", "walkway")) });

        Assert.Equal("walkway", GisRulesFile.TryLoad(RulesPath)!.ToMappingRules().FloorCategoryOverrides["A"]);
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public void Apply_WritesNothingWhenTheFileAlreadyHoldsTheOperations()
    {
        int writes = 0;
        GisRulesStoreOptions options = FastOptions();
        options.AfterWrite = () => writes++;
        GisRulesStore store = NewStore(options);
        store.Apply(new[] { GisRulesOperation.UpsertRule(Floor("A", "walkway")) });

        store.Apply(new[] { GisRulesOperation.UpsertRule(Floor("A", "walkway")) });

        Assert.Equal(1, writes);
    }

    [Fact]
    public void Apply_RemovesDeletedRulesAndProfiles()
    {
        GisRulesStore store = NewStore();
        store.Apply(new[]
        {
            GisRulesOperation.UpsertRule(Floor("A", "walkway")),
            GisRulesOperation.UpsertProfile(Profile("Station")),
        });

        store.Apply(new[]
        {
            GisRulesOperation.DeleteRule(MappingRuleType.FloorCategory, "A"),
            GisRulesOperation.DeleteProfile("STATION"),
        });

        GisRulesFile read = GisRulesFile.TryLoad(RulesPath)!;
        Assert.Empty(read.CategoryRules);
        Assert.Empty(read.Profiles);
    }

    [Fact]
    public void Apply_KeepsTheOldFileReadableAndLeavesNoTempWhenTheShareKeepsRefusingTheSwap()
    {
        NewStore().Apply(new[] { GisRulesOperation.UpsertRule(Floor("Old", "walkway")) });
        string before = File.ReadAllText(RulesPath);
        List<int> waits = new();
        GisRulesStoreOptions options = FastOptions();
        options.Sleep = waits.Add;
        options.Swap = (_, _) => throw new IOException("The process cannot access the file because it is being used by another process.");

        Assert.Throws<IOException>(() => new GisRulesStore(RulesPath, options).Apply(new[] { GisRulesOperation.UpsertRule(Floor("New", "retail")) }));

        Assert.Equal(new[] { 100, 200, 400, 800 }, waits);
        Assert.Equal(before, File.ReadAllText(RulesPath));
        Assert.Empty(Directory.GetFiles(Station, ".gis-rules.*.tmp"));
        Assert.False(File.Exists(LockPath));
    }

    [Fact]
    public void Apply_WritesOnceTheShareLetsGo()
    {
        GisRulesStoreOptions options = FastOptions();
        int refusals = 2;
        Action<string, string> real = options.Swap;
        options.Swap = (temp, target) =>
        {
            if (refusals-- > 0)
            {
                throw new IOException("busy");
            }

            real(temp, target);
        };

        NewStore(options).Apply(new[] { GisRulesOperation.UpsertRule(Floor("A", "walkway")) });

        Assert.Equal("walkway", GisRulesFile.TryLoad(RulesPath)!.ToMappingRules().FloorCategoryOverrides["A"]);
        Assert.Empty(Directory.GetFiles(Station, ".gis-rules.*.tmp"));
    }

    [Fact]
    public void Apply_FailsAndKeepsTheOldFileWhenAnotherPcHoldsItOpen()
    {
        NewStore().Apply(new[] { GisRulesOperation.UpsertRule(Floor("Old", "walkway")) });
        string before = File.ReadAllText(RulesPath);
        FileStream holder = new(RulesPath, FileMode.Open, FileAccess.Read, FileShare.None);
        bool sharingIsEnforced;
        try
        {
            using (new FileStream(RulesPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
            }

            sharingIsEnforced = false;
        }
        catch (IOException)
        {
            sharingIsEnforced = true;
        }

        try
        {
            if (sharingIsEnforced)
            {
                Assert.Throws<IOException>(() => NewStore().Apply(new[] { GisRulesOperation.UpsertRule(Floor("New", "retail")) }));
            }
        }
        finally
        {
            holder.Dispose();
        }

        if (sharingIsEnforced)
        {
            Assert.Equal(before, File.ReadAllText(RulesPath));
            Assert.Empty(Directory.GetFiles(Station, ".gis-rules.*.tmp"));
        }
    }

    [Fact]
    public void Apply_RemovesTempFilesOlderThanAnHourAndKeepsFreshOnes()
    {
        Directory.CreateDirectory(Station);
        string stale = Path.Combine(Station, ".gis-rules.deadbeef.tmp");
        string fresh = Path.Combine(Station, ".gis-rules.cafebabe.tmp");
        File.WriteAllText(stale, "x");
        File.WriteAllText(fresh, "x");
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));

        NewStore().Apply(new[] { GisRulesOperation.UpsertRule(Floor("A", "walkway")) });

        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void Source_UsesThisPcsRulesWhenTheModelHasNoStationFolder()
    {
        MappingRuleStore perPc = new(Path.Combine(_folder, "pc"), Path.Combine(_folder, "legacy"));
        GisRulesSource source = GisRulesSource.Resolve(null, "project-a", perPc);

        source.Save(ProjectMappingRules.Empty, ProjectMappingRules.Create(new Dictionary<string, string> { ["A"] = "walkway" }, null, null, null));

        Assert.False(source.IsStation);
        Assert.Null(source.SavedStatus);
        Assert.Equal("walkway", perPc.Load("project-a").FloorCategoryOverrides["A"]);
    }

    [Fact]
    public void Source_SaysWhichStationASaveWentTo()
    {
        Directory.CreateDirectory(Path.Combine(Station, "..", "Shinagawa"));
        string station = Path.GetFullPath(Path.Combine(Station, "..", "Shinagawa"));
        GisRulesSource source = GisRulesSource.Resolve(station, "project-a", new MappingRuleStore(Path.Combine(_folder, "pc"), Path.Combine(_folder, "legacy")));

        source.Save(ProjectMappingRules.Empty, ProjectMappingRules.Create(new Dictionary<string, string> { ["A"] = "walkway" }, null, null, null));

        Assert.Equal("Saved for station Shinagawa", source.SavedStatus);
        Assert.Equal("walkway", source.Load().Value.FloorCategoryOverrides["A"]);
    }

    [Fact]
    public void Source_SavesOnlyWhatTheCallerChanged()
    {
        Directory.CreateDirectory(Station);
        GisRulesSource source = GisRulesSource.Resolve(Station, "project-a", new MappingRuleStore(Path.Combine(_folder, "pc"), Path.Combine(_folder, "legacy")), FastOptions());
        ProjectMappingRules loaded = ProjectMappingRules.Create(new Dictionary<string, string> { ["Mine"] = "walkway" }, null, null, null);
        source.Save(ProjectMappingRules.Empty, loaded);
        new GisRulesStore(RulesPath, FastOptions()).Apply(new[] { GisRulesOperation.UpsertRule(Floor("Theirs", "retail")) });

        source.Save(loaded, ProjectMappingRules.Create(new Dictionary<string, string> { ["Mine"] = "ticketing" }, null, null, null));

        IReadOnlyDictionary<string, string> floors = source.Load().Value.FloorCategoryOverrides;
        Assert.Equal("ticketing", floors["Mine"]);
        Assert.Equal("retail", floors["Theirs"]);
    }

    [Fact]
    public void ForExport_FallsBackToThisPcAndSaysSoWhenTheStationCannotBeReached()
    {
        MappingRuleStore perPc = new(Path.Combine(_folder, "pc"), Path.Combine(_folder, "legacy"));
        perPc.Save("project-a", ProjectMappingRules.Create(new Dictionary<string, string> { ["A"] = "walkway" }, null, null, null));
        GisRulesSource source = GisRulesSource.Resolve(Path.Combine(_folder, "gone"), "project-a", perPc);
        List<string> warnings = new();

        GisRulesSource used = source.ForExport(warnings);

        Assert.False(used.IsStation);
        Assert.Contains(warnings, warning => warning.Contains("cannot be reached") && warning.Contains("this PC's rules"));
        Assert.Equal("walkway", used.Load().Value.FloorCategoryOverrides["A"]);
    }

    private GisRulesSource StationSource(MappingRuleStore? perPc = null)
    {
        Directory.CreateDirectory(Station);
        return GisRulesSource.Resolve(Station, "project-a", perPc ?? new MappingRuleStore(Path.Combine(_folder, "pc"), Path.Combine(_folder, "legacy")), FastOptions());
    }

    [Fact]
    public void StationProfile_IsCreatedFromTheSettingsOfTheExportThatCreatesIt()
    {
        GisRulesSource source = StationSource();
        GisRulesProfile mine = Profile("Dialog", 6676);
        mine.FeatureTypes = new List<string> { "unit", "opening" };
        List<string> warnings = new();

        GisRulesProfile used = source.ResolveStationProfile(GisModelFile.DefaultProfileName, mine, warnings);

        Assert.Equal(6676, used.TargetEpsg);
        Assert.Equal(new[] { "unit", "opening" }, used.FeatureTypes);
        GisRulesProfile stored = GisRulesFile.TryLoad(RulesPath)!.FindProfile(GisModelFile.DefaultProfileName)!;
        Assert.Equal(6676, stored.TargetEpsg);
        Assert.Empty(warnings);
    }

    [Fact]
    public void AddProfile_KeepsTheProfileAnotherPcCreatedFirst()
    {
        GisRulesStore store = NewStore();
        store.Apply(new[] { GisRulesOperation.UpsertProfile(Profile(GisModelFile.DefaultProfileName, 6669)) });

        GisRulesFile read = store.Apply(new[] { GisRulesOperation.AddProfile(Profile(GisModelFile.DefaultProfileName, 6676)) });

        Assert.Equal(6669, read.FindProfile(GisModelFile.DefaultProfileName)!.TargetEpsg);
    }

    [Fact]
    public void StationProfile_IsUsedWithOneWarningListingEachSettingThatDiffersFromThisExport()
    {
        GisRulesSource source = StationSource();
        GisRulesProfile station = Profile(GisModelFile.DefaultProfileName, 6677);
        station.SimplifyStairUnits = true;
        source.ApplyToStation(new[] { GisRulesOperation.UpsertProfile(station) });
        GisRulesProfile mine = Profile("Dialog", 6676);
        mine.FeatureTypes = new List<string> { "unit" };
        List<string> warnings = new();

        GisRulesProfile used = source.ResolveStationProfile(GisModelFile.DefaultProfileName, mine, warnings);

        Assert.Equal(6677, used.TargetEpsg);
        string warning = Assert.Single(warnings);
        Assert.Contains("targetEpsg: 6676 -> 6677", warning);
        Assert.Contains("simplifyStairUnits: false -> true", warning);
        Assert.Contains("featureTypes: unit -> unit, detail, opening, level, fixture", warning);
        Assert.DoesNotContain("name:", warning);
        Assert.DoesNotContain("coordinateMode", warning);
    }

    [Fact]
    public void StationProfile_GivesNoWarningWhenThisExportMatchesIt()
    {
        GisRulesSource source = StationSource();
        source.ApplyToStation(new[] { GisRulesOperation.UpsertProfile(Profile(GisModelFile.DefaultProfileName)) });
        List<string> warnings = new();

        source.ResolveStationProfile("station", Profile("Dialog"), warnings);

        Assert.Empty(warnings);
    }

    [Fact]
    public void StationProfile_ThatTheStationLacksStopsTheExportListingTheProfilesItHas()
    {
        GisRulesSource source = StationSource();
        source.ApplyToStation(new[] { GisRulesOperation.UpsertProfile(Profile("Night")) });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() =>
            source.ResolveStationProfile("Day", Profile("Dialog"), new List<string>()));

        Assert.Contains("'Day'", error.Message);
        Assert.Contains("Night", error.Message);
        Assert.Null(source.LoadStationFile()!.FindProfile("Day"));
    }

    [Fact]
    public void Move_AddsThisPcsRulesOnceAndSetsItsFileAside()
    {
        string pcRules = Path.Combine(_folder, "pc");
        MappingRuleStore perPc = new(pcRules, Path.Combine(_folder, "legacy"));
        perPc.Save("project-a", ProjectMappingRules.Create(
            new Dictionary<string, string> { ["A"] = "walkway", ["Clash"] = "walkway" },
            null,
            new Dictionary<string, string> { ["Escalator"] = "escalator" },
            null));
        Directory.CreateDirectory(Station);
        new GisRulesStore(RulesPath, FastOptions()).Apply(new[] { GisRulesOperation.UpsertRule(Floor("Clash", "retail")) });
        GisRulesSource source = GisRulesSource.Resolve(Station, "project-a", perPc, FastOptions());
        List<string> warnings = new();

        bool moved = source.MoveLocalIntoStation(new[] { Profile("Night", 6676) }, warnings);

        Assert.True(moved);
        GisRulesFile station = GisRulesFile.TryLoad(RulesPath)!;
        ProjectMappingRules rules = station.ToMappingRules();
        Assert.Equal("walkway", rules.FloorCategoryOverrides["A"]);
        Assert.Equal("retail", rules.FloorCategoryOverrides["Clash"]);
        Assert.Equal("escalator", rules.FamilyCategoryOverrides["Escalator"]);
        Assert.Equal(6676, station.FindProfile("Night")!.TargetEpsg);
        Assert.Single(warnings);
        Assert.Contains("Clash", warnings[0]);
        Assert.Empty(Directory.GetFiles(pcRules, "*.json"));
        Assert.Single(Directory.GetFiles(pcRules, "*" + MappingRuleStore.MovedSuffix));
    }

    [Fact]
    public void Move_ChangesNothingTheSecondTimeAndDoesNotBringBackADeletedStationRule()
    {
        MappingRuleStore perPc = new(Path.Combine(_folder, "pc"), Path.Combine(_folder, "legacy"));
        ProjectMappingRules local = ProjectMappingRules.Create(new Dictionary<string, string> { ["A"] = "walkway" }, null, null, null);
        perPc.Save("project-a", local);
        Directory.CreateDirectory(Station);
        GisRulesSource source = GisRulesSource.Resolve(Station, "project-a", perPc, FastOptions());
        source.MoveLocalIntoStation(Array.Empty<GisRulesProfile>(), new List<string>());
        string afterFirst = File.ReadAllText(RulesPath);

        Assert.False(source.MoveLocalIntoStation(Array.Empty<GisRulesProfile>(), new List<string>()));
        Assert.Equal(afterFirst, File.ReadAllText(RulesPath));

        source.Save(source.Load().Value, ProjectMappingRules.Empty);
        Assert.False(source.MoveLocalIntoStation(Array.Empty<GisRulesProfile>(), new List<string>()));
        Assert.Empty(source.Load().Value.Rules);
    }

    [Fact]
    public void Move_AlsoSetsAsideThePreviousVersionsRuleFiles()
    {
        string legacy = Path.Combine(_folder, "legacy");
        MappingRuleStore perPc = new(Path.Combine(_folder, "pc"), legacy);
        string legacyFile = Path.Combine(legacy, "floor-category-overrides", Sha("project-a") + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacyFile)!);
        File.WriteAllText(legacyFile, "{\"Overrides\":{\"A\":\"walkway\"}}");
        Directory.CreateDirectory(Station);
        GisRulesSource source = GisRulesSource.Resolve(Station, "project-a", perPc, FastOptions());

        Assert.True(source.MoveLocalIntoStation(Array.Empty<GisRulesProfile>(), new List<string>()));
        Assert.False(source.MoveLocalIntoStation(Array.Empty<GisRulesProfile>(), new List<string>()));

        Assert.Equal("walkway", source.Load().Value.FloorCategoryOverrides["A"]);
        Assert.False(File.Exists(legacyFile));
    }

    [Fact]
    public void Hash_IsTheSameForTheSameContentInAnyOrderAndDiffersWhenARuleChanges()
    {
        GisRulesProfile profile = Profile("Station");
        string hash = GisRulesHash.Compute(new[] { Floor("A", "walkway"), Floor("B", "retail") }, profile);

        Assert.Equal(hash, GisRulesHash.Compute(new[] { Floor("B", "retail"), Floor("A", "walkway") }, profile.Clone()));
        Assert.NotEqual(hash, GisRulesHash.Compute(new[] { Floor("A", "walkway"), Floor("B", "ticketing") }, profile));
        Assert.NotEqual(hash, GisRulesHash.Compute(new[] { Floor("A", "walkway"), Floor("B", "retail") }, Profile("Station", 6676)));
        Assert.Matches("^[0-9a-f]{64}$", hash);
    }

    [Fact]
    public void Hash_IgnoresWhoChangedTheFileAndWhen()
    {
        GisRulesStore store = NewStore();
        store.Apply(new[] { GisRulesOperation.UpsertRule(Floor("A", "walkway")), GisRulesOperation.UpsertProfile(Profile("Station")) });
        GisRulesFile first = GisRulesFile.TryLoad(RulesPath)!;
        string hash = GisRulesHash.Compute(first.ToMappingRules().Rules, first.FindProfile("Station")!);

        store.Apply(new[] { GisRulesOperation.UpsertRule(Floor("B", "retail")) });
        store.Apply(new[] { GisRulesOperation.DeleteRule(MappingRuleType.FloorCategory, "B") });

        GisRulesFile second = GisRulesFile.TryLoad(RulesPath)!;
        Assert.Equal(hash, GisRulesHash.Compute(second.ToMappingRules().Rules, second.FindProfile("Station")!));
    }

    [Theory]
    [InlineData(1.2, "1.2")]
    [InlineData(0.0000125, "0.0000125")]
    [InlineData(1e21, "1000000000000000000000")]
    [InlineData(-3.0, "-3")]
    [InlineData(0.1, "0.1")]
    [InlineData(123456.789, "123456.789")]
    public void Hash_WritesNumbersAsShortestDigitsWithoutAnExponent(double value, string expected)
    {
        GisRulesProfile profile = Profile("Station");
        profile.SectionBoxAboveFloorMeters = value;

        string canonical = GisRulesHash.CanonicalJson(Array.Empty<MappingRule>(), profile);

        Assert.Contains("\"sectionBoxAboveFloorMeters\":" + expected + ",", canonical);
    }

    [Fact]
    public void Fixture_IsWhatTheWriterWritesAndHashesToTheExpectedValue()
    {
        GisRulesProfile profile = Profile("Station");
        profile.CoordinateMode = CoordinateExportMode.ConvertToTargetCrs;
        profile.FeatureTypes = new List<string> { "unit", "opening", "level" };
        profile.UnitGeometrySource = UnitGeometrySource.Floors;
        profile.UnitAttributeSource = UnitAttributeSource.Hybrid;
        profile.UnitCategories = new List<string> { "walkway", "retail" };
        profile.SimplifyStairUnits = true;
        profile.GeometryRepair.Enabled = true;
        profile.GeometryRepair.SimplifyToleranceMeters = 0.0000125;
        profile.SchemaProfiles = new List<SchemaProfile>();
        profile.ValidationPolicies = new List<ValidationPolicyProfile>();
        GisRulesStore store = NewStore(new GisRulesStoreOptions
        {
            Sleep = _ => { },
            Stamp = () => new GisRulesChanged { Utc = "2026-11-14T09:00:00Z", User = "daniel", RevitGeoSuite = "1.0.0.0" },
        });
        store.Apply(new[]
        {
            GisRulesOperation.UpsertRule(new MappingRule { RuleType = MappingRuleType.RoomCategory, MatchValue = "改札", ResolvedValue = "ticketing" }),
            GisRulesOperation.UpsertRule(Floor("Concourse", "walkway")),
            GisRulesOperation.UpsertRule(new MappingRule { RuleType = MappingRuleType.AcceptedOpeningFamily, MatchValue = "Gate Opening" }),
            GisRulesOperation.UpsertRule(Floor("Shop \"A\"", "retail")),
            GisRulesOperation.UpsertRule(new MappingRule { RuleType = MappingRuleType.FamilyCategory, MatchValue = "Escalator", ResolvedValue = "escalator" }),
            GisRulesOperation.UpsertProfile(profile),
        });
        string directory = FixtureDirectory();
        string written = File.ReadAllText(RulesPath, Encoding.UTF8);
        GisRulesFile file = GisRulesFile.Parse(written, "written");
        string hash = GisRulesHash.Compute(file.ToMappingRules().Rules, file.FindProfile("Station")!);
        if (Environment.GetEnvironmentVariable("REVITGEOSUITE_WRITE_FIXTURES") == "1")
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "gis-rules.json"), written, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(directory, "expected-hash.txt"), hash + "\n", new UTF8Encoding(false));
        }

        Assert.Equal(File.ReadAllText(Path.Combine(directory, "gis-rules.json"), Encoding.UTF8).Replace("\r\n", "\n"), written);
        Assert.Equal(File.ReadAllText(Path.Combine(directory, "expected-hash.txt"), Encoding.UTF8).Trim(), hash);
    }

    private static string Sha(string value)
    {
        using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(value)).Select(b => b.ToString("x2")));
    }

    private static string FixtureDirectory([CallerFilePath] string sourceFile = "")
    {
        return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(sourceFile)!, "..", "..", "Fixtures", "Conformance", "gis-rules"));
    }
}
