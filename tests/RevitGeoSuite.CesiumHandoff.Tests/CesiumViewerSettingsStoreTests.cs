using System;
using System.IO;
using RevitGeoSuite.CesiumHandoff;
using Xunit;

namespace RevitGeoSuite.CesiumHandoff.Tests;

public sealed class CesiumViewerSettingsStoreTests : IDisposable
{
    private readonly string _settingsPath;

    public CesiumViewerSettingsStoreTests()
    {
        _settingsPath = Path.Combine(
            Path.GetTempPath(),
            "cesium-settings-tests",
            Guid.NewGuid().ToString("N"),
            "cesium-viewer.json");
    }

    public void Dispose()
    {
        string? directory = Path.GetDirectoryName(_settingsPath);
        try
        {
            if (directory != null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (IOException)
        {
        }
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
    public void Load_CorruptFileFallsBackToDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, "{ not json ");

        CesiumViewerSettings settings = new CesiumViewerSettingsStore(_settingsPath).Load();
        Assert.Equal("http://localhost:3001", settings.ViewerUrl);
    }
}
