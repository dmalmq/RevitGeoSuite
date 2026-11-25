using System;
using System.IO;
using RevitGeoSuite.CesiumHandoff;
using Xunit;

namespace RevitGeoSuite.CesiumHandoff.Tests;

public sealed class CesiumViewerSettingsStoreTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _settingsPath;

    public CesiumViewerSettingsStoreTests()
    {
        _testRoot = Path.Combine(
            Path.GetTempPath(),
            "cesium-settings-tests",
            Guid.NewGuid().ToString("N"));
        _settingsPath = Path.Combine(_testRoot, "cesium-viewer.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Default_and_legacy_paths_target_kanizu_revit_and_revit_geo_suite()
    {
        Assert.EndsWith(
            Path.Combine("KanizuRevit", "CesiumHandoff", "viewer-settings.json"),
            CesiumViewerSettingsStore.DefaultSettingsFilePath,
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("RevitGeoSuite", "CesiumHandoff", "viewer-settings.json"),
            CesiumViewerSettingsStore.LegacySettingsFilePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_ReturnsDefaultsWhenFileMissing()
    {
        var store = new CesiumViewerSettingsStore(_settingsPath);
        CesiumViewerSettings settings = store.Load();

        Assert.Equal("http://localhost:3001", settings.ViewerUrl);
        Assert.Null(settings.Token);
    }

    [Fact]
    public void SaveThenLoad_RoundTrips()
    {
        var store = new CesiumViewerSettingsStore(_settingsPath);
        store.Save(new CesiumViewerSettings
        {
            ViewerUrl = "http://viewer.example:8080",
            Token = "abc",
        });

        CesiumViewerSettings loaded = new CesiumViewerSettingsStore(_settingsPath).Load();
        Assert.Equal("http://viewer.example:8080", loaded.ViewerUrl);
        Assert.Equal("abc", loaded.Token);
    }

    [Fact]
    public void Load_FallsBackToLegacyFileWhenNewFileIsMissing_AndPrefersNewWhenPresent()
    {
        string newPath = Path.Combine(_testRoot, "KanizuRevit", "CesiumHandoff", "viewer-settings.json");
        string legacyPath = Path.Combine(_testRoot, "RevitGeoSuite", "CesiumHandoff", "viewer-settings.json");

        new CesiumViewerSettingsStore(legacyPath).Save(new CesiumViewerSettings
        {
            ViewerUrl = "http://legacy-viewer.example:3001",
            Token = "legacy-token",
        });

        var store = new CesiumViewerSettingsStore(newPath, legacyPath);

        // Falls back to legacy when new file does not exist yet.
        CesiumViewerSettings fromLegacy = store.Load();
        Assert.Equal("http://legacy-viewer.example:3001", fromLegacy.ViewerUrl);
        Assert.Equal("legacy-token", fromLegacy.Token);
        Assert.False(File.Exists(newPath));

        // Saving writes to new path and subsequent reads prefer new over legacy.
        store.Save(new CesiumViewerSettings
        {
            ViewerUrl = "http://kanizu-viewer.example:4001",
            Token = "new-token",
        });
        Assert.True(File.Exists(newPath));

        CesiumViewerSettings fromNew = store.Load();
        Assert.Equal("http://kanizu-viewer.example:4001", fromNew.ViewerUrl);
        Assert.Equal("new-token", fromNew.Token);
    }

    [Fact]
    public void Load_CorruptFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, "{ not json ");

        CesiumViewerSettings settings = new CesiumViewerSettingsStore(_settingsPath).Load();
        Assert.Equal("http://localhost:3001", settings.ViewerUrl);
    }
}
