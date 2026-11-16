using System;
using System.IO;
using System.Linq;
using RevitGeoSuite.CesiumHandoff;
using Xunit;

namespace RevitGeoSuite.CesiumHandoff.Tests;

public sealed class CesiumStationFolderTests : IDisposable
{
    private readonly string _station;

    public CesiumStationFolderTests()
    {
        _station = Path.Combine(Path.GetTempPath(), "cesium-station-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_station))
            {
                Directory.Delete(_station, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static CesiumPackageBuildInputs Inputs(string? modelId, string documentKey = "doc-key")
    {
        return new CesiumPackageBuildInputs
        {
            BuildingId = "tokyo-station-1a2b3c4d",
            BuildingName = "Tokyo Station",
            SourceModel = "Tokyo.rvt",
            DocumentKey = documentKey,
            GeneratorVersion = "1.0.0",
            ModelId = modelId,
        };
    }

    private CesiumPackageManifest Stage(CesiumPackageLayout layout, string? modelId, string tileset = "{}")
    {
        File.WriteAllText(Path.Combine(layout.TilesDirectory, "tileset.json"), tileset);
        return new CesiumPackageLayoutBuilder().WriteManifest(layout, Inputs(modelId));
    }

    [Fact]
    public void Staging_is_hidden_under_the_revit_folder()
    {
        CesiumPackageLayout staging = CesiumStationFolder.CreateStagingLayout(_station);

        Assert.Equal(Path.Combine(Path.GetFullPath(_station), "revit"), Path.GetDirectoryName(staging.RootDirectory));
        Assert.StartsWith(".tmp-", Path.GetFileName(staging.RootDirectory));
        Assert.True(Directory.Exists(staging.TilesDirectory));
    }

    [Fact]
    public void Publish_renames_the_staged_package_into_a_time_and_hash_named_version()
    {
        CesiumPackageLayout staging = CesiumStationFolder.CreateStagingLayout(_station);
        CesiumPackageManifest manifest = Stage(staging, "8D7F3C1E-2B4A-4F1E-9A51-0D3C2E1F4B6A");

        CesiumPackageLayout published = CesiumStationFolder.Publish(staging, _station, manifest);

        Assert.False(Directory.Exists(staging.RootDirectory));
        Assert.True(File.Exists(published.ManifestPath));
        Assert.True(File.Exists(Path.Combine(published.TilesDirectory, "tileset.json")));
        string modelDirectory = Path.GetDirectoryName(published.RootDirectory)!;
        Assert.Equal("tokyo-station-8d7f3c1e", Path.GetFileName(modelDirectory));
        Assert.Equal(
            CesiumStationFolder.VersionFolderName(manifest.CreatedUtc, manifest.ContentHash),
            Path.GetFileName(published.RootDirectory));
        Assert.Empty(Directory.GetDirectories(Path.Combine(_station, "revit"), ".tmp-*"));
    }

    [Fact]
    public void Publish_keeps_earlier_versions_and_never_overwrites_one()
    {
        CesiumPackageLayout first = CesiumStationFolder.CreateStagingLayout(_station);
        CesiumPackageManifest firstManifest = Stage(first, "model-a");
        CesiumPackageLayout firstPublished = CesiumStationFolder.Publish(first, _station, firstManifest);

        CesiumPackageLayout second = CesiumStationFolder.CreateStagingLayout(_station);
        CesiumPackageManifest secondManifest = Stage(second, "model-a");
        secondManifest.CreatedUtc = firstManifest.CreatedUtc;
        secondManifest.ContentHash = firstManifest.ContentHash;
        CesiumPackageLayout secondPublished = CesiumStationFolder.Publish(second, _station, secondManifest);

        Assert.NotEqual(firstPublished.RootDirectory, secondPublished.RootDirectory);
        Assert.EndsWith("-2", secondPublished.RootDirectory);
        Assert.True(File.Exists(firstPublished.ManifestPath));
        Assert.Equal(2, Directory.GetDirectories(Path.GetDirectoryName(firstPublished.RootDirectory)!).Length);
    }

    private CesiumPackageLayout PublishGis(string modelId, DateTime createdUtc, bool withGis = true, string documentKey = "doc-key")
    {
        CesiumPackageLayout staging = CesiumStationFolder.CreateStagingLayout(_station);
        if (withGis)
        {
            File.WriteAllText(Path.Combine(staging.GisDirectory, "station.gpkg"), createdUtc.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        else
        {
            Directory.Delete(staging.GisDirectory);
            File.WriteAllText(Path.Combine(staging.TilesDirectory, "tileset.json"), "{}");
        }

        CesiumPackageManifest manifest = new CesiumPackageLayoutBuilder().WriteManifest(staging, Inputs(modelId, documentKey));
        manifest.CreatedUtc = createdUtc;
        File.WriteAllText(staging.ManifestPath, CesiumPackageManifestSerializer.Serialize(manifest));
        return CesiumStationFolder.Publish(staging, _station, manifest);
    }

    [Fact]
    public void Previous_version_is_the_newest_older_one_with_gis()
    {
        DateTime start = new(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc);
        CesiumPackageLayout oldest = PublishGis("model-a", start);
        CesiumPackageLayout middle = PublishGis("model-a", start.AddHours(1));
        PublishGis("model-a", start.AddHours(2), withGis: false);
        CesiumPackageLayout newest = PublishGis("model-a", start.AddHours(3));
        Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(newest.RootDirectory)!, ".tmp-x", "gis"));

        Assert.Equal(middle.RootDirectory, CesiumStationFolder.PreviousVersion(newest.RootDirectory));
        Assert.Equal(oldest.RootDirectory, CesiumStationFolder.PreviousVersion(middle.RootDirectory));
        Assert.Null(CesiumStationFolder.PreviousVersion(oldest.RootDirectory));
    }

    [Fact]
    public void Previous_version_skips_a_folder_without_a_readable_manifest()
    {
        DateTime start = new(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc);
        CesiumPackageLayout first = PublishGis("model-a", start);
        CesiumPackageLayout second = PublishGis("model-a", start.AddHours(1));
        string stray = Path.Combine(Path.GetDirectoryName(first.RootDirectory)!, "20990101T000000Z-copy");
        Directory.CreateDirectory(Path.Combine(stray, "gis"));
        File.WriteAllText(Path.Combine(stray, "cesium-package.json"), "not json");
        string empty = Path.Combine(Path.GetDirectoryName(first.RootDirectory)!, "20990101T000000Z-null");
        Directory.CreateDirectory(Path.Combine(empty, "gis"));
        File.WriteAllText(Path.Combine(empty, "cesium-package.json"), "null");

        Assert.Equal(first.RootDirectory, CesiumStationFolder.PreviousVersion(second.RootDirectory));
    }

    [Fact]
    public void A_folder_is_missing_new_or_a_station_folder()
    {
        Assert.Equal(CesiumStationFolder.FolderKind.Missing, CesiumStationFolder.Classify(Path.Combine(_station, "nowhere")));
        Assert.Equal(CesiumStationFolder.FolderKind.Missing, CesiumStationFolder.Classify(" "));
        Directory.CreateDirectory(_station);
        Assert.Equal(CesiumStationFolder.FolderKind.New, CesiumStationFolder.Classify(_station));
        Directory.CreateDirectory(Path.Combine(_station, "revit", "some-project-files"));
        Assert.Equal(CesiumStationFolder.FolderKind.New, CesiumStationFolder.Classify(_station));
        // A version set aside (hidden) is not a version Kanizu reads.
        string hidden = Path.Combine(_station, "revit", "some-model", ".set-aside");
        Directory.CreateDirectory(hidden);
        File.WriteAllText(Path.Combine(hidden, "cesium-package.json"), "{}");
        Assert.Equal(CesiumStationFolder.FolderKind.New, CesiumStationFolder.Classify(_station));
        PublishGis("model-a", new DateTime(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc));
        Assert.Equal(CesiumStationFolder.FolderKind.Station, CesiumStationFolder.Classify(_station));
    }

    [Fact]
    public void Latest_source_is_the_newest_version_of_that_model()
    {
        DateTime start = new(2026, 11, 15, 1, 0, 0, DateTimeKind.Utc);
        Assert.Null(CesiumStationFolder.LatestSource(_station, "model-a"));
        PublishGis("model-a", start.AddHours(2), documentKey: "path:B");
        PublishGis("model-a", start, documentKey: "path:A");
        PublishGis("model-b", start.AddHours(3), documentKey: "path:C");

        CesiumPackageSource? latest = CesiumStationFolder.LatestSource(_station, "model-a");
        Assert.NotNull(latest);
        Assert.Equal(("model-a", "path:B"), (latest!.ModelId, latest.DocumentKey));
        Assert.Null(CesiumStationFolder.LatestSource(_station, "model-c"));
    }

    [Fact]
    public void Rules_differ_on_hash_or_profile_and_never_without_a_hash()
    {
        CesiumPackageGisRules Rules(string hash, string profile) => new() { Source = "station", Hash = hash, Profile = profile };

        Assert.False(CesiumPackageGisRules.Differ(Rules("h1", "station"), Rules("H1", "station")));
        Assert.True(CesiumPackageGisRules.Differ(Rules("h1", "station"), Rules("h2", "station")));
        Assert.True(CesiumPackageGisRules.Differ(Rules("h1", "station"), Rules("h1", "concourse")));
        Assert.False(CesiumPackageGisRules.Differ(Rules("", "station"), Rules("h2", "other")));
        Assert.False(CesiumPackageGisRules.Differ(null, Rules("h2", "station")));
    }

    [Fact]
    public void Publish_refuses_a_package_without_a_manifest()
    {
        CesiumPackageLayout staging = CesiumStationFolder.CreateStagingLayout(_station);

        Assert.Throws<InvalidOperationException>(() =>
            CesiumStationFolder.Publish(staging, _station, new CesiumPackageManifest()));
    }

    [Fact]
    public void Model_folder_falls_back_to_the_building_id_without_a_model_id()
    {
        var manifest = new CesiumPackageManifest
        {
            Building = new CesiumPackageBuilding { Id = "tokyo-station-1a2b3c4d", Name = "Tokyo Station" },
            Source = new CesiumPackageSource(),
        };

        Assert.Equal("tokyo-station-1a2b3c4d", CesiumStationFolder.ModelFolderName(manifest));
    }

    [Fact]
    public void Version_names_sort_by_time()
    {
        string earlier = CesiumStationFolder.VersionFolderName(new DateTime(2026, 11, 8, 9, 5, 7, DateTimeKind.Utc), "ABCDEF0123456789");
        string later = CesiumStationFolder.VersionFolderName(new DateTime(2026, 11, 8, 10, 0, 0, DateTimeKind.Utc), "0000000000");

        Assert.Equal("20261108T090507Z-abcdef01", earlier);
        Assert.True(string.CompareOrdinal(earlier, later) < 0);
        Assert.Equal(new[] { earlier, later }, new[] { later, earlier }.OrderBy(name => name, StringComparer.Ordinal));
    }
}
