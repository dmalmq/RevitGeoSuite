using System;
using System.IO;
using RevitGeoSuite.SharedUI.Localization;
using RevitGeoSuite.SharedUI.Shell;
using Xunit;

namespace RevitGeoSuite.SharedUI.Tests;

public sealed class UiSettingsStoreTests : IDisposable
{
    private readonly string testRoot;

    public UiSettingsStoreTests()
    {
        testRoot = Path.Combine(Path.GetTempPath(), "kanizu-ui-settings-tests", Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(testRoot))
            {
                Directory.Delete(testRoot, recursive: true);
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
            Path.Combine("KanizuRevit", "ui-settings.json"),
            UiSettingsStore.DefaultSettingsFilePath,
            StringComparison.Ordinal);
        Assert.EndsWith(
            Path.Combine("RevitGeoSuite", "ui-settings.json"),
            UiSettingsStore.LegacySettingsFilePath,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Load_FallsBackToLegacyFileWhenNewFileIsMissing_AndPrefersNewWhenPresent()
    {
        string newPath = Path.Combine(testRoot, "KanizuRevit", "ui-settings.json");
        string legacyPath = Path.Combine(testRoot, "RevitGeoSuite", "ui-settings.json");

        new UiSettingsStore(legacyPath).Save(UiLanguage.Japanese);

        UiSettingsStore store = new(newPath, legacyPath);
        Assert.Equal(UiLanguage.Japanese, store.Load());
        Assert.False(File.Exists(newPath));

        store.Save(UiLanguage.English);
        Assert.True(File.Exists(newPath));
        Assert.Equal(UiLanguage.English, store.Load());
    }

    [Fact]
    public void WebShellEnvironment_PreservesVirtualHostAndUserDataFolderForLocalStorage()
    {
        Assert.Equal("ui.revitgeosuite.local", WebShellEnvironment.HostName);
        Assert.EndsWith(
            Path.Combine("RevitGeoSuite", "WebView2"),
            WebShellEnvironment.GetUserDataFolder(testRoot),
            StringComparison.Ordinal);
    }
}
