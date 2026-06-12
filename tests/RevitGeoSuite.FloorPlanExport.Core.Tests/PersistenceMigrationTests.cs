using System;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.UI;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests;

public sealed class PersistenceMigrationTests
{
    [Fact]
    public void ExportDialogSettingsStore_LoadsLegacySettingsWhenCurrentFileIsMissing()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            string currentPath = Path.Combine(tempDirectory, "current", "settings.json");
            string legacyPath = Path.Combine(tempDirectory, "legacy", "settings.json");
            new ExportDialogSettingsStore(legacyPath).Save(new ExportDialogSettings
            {
                OutputDirectory = @"C:\Exports",
                TargetEpsg = 3857,
                UiLanguage = UiLanguage.Japanese,
            });

            ExportDialogSettings loaded = new ExportDialogSettingsStore(currentPath, legacyPath).Load();

            Assert.Equal(@"C:\Exports", loaded.OutputDirectory);
            Assert.Equal(3857, loaded.TargetEpsg);
            Assert.Equal(UiLanguage.Japanese, loaded.UiLanguage);
            Assert.False(File.Exists(currentPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void ExportProfileStore_LoadsLegacyProfilesWhenCurrentFileIsMissing()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            string currentPath = Path.Combine(tempDirectory, "current", "profiles.json");
            string legacyPath = Path.Combine(tempDirectory, "legacy", "profiles.json");
            new ExportProfileStore(legacyPath).SaveProfile("project-a", new ExportProfile
            {
                Name = "Legacy project profile",
                Scope = ExportProfileScope.Project,
                OutputDirectory = @"C:\LegacyExports",
                TargetEpsg = 6678,
            });

            ExportProfile profile = new ExportProfileStore(currentPath, legacyPath)
                .LoadWithDiagnostics("project-a")
                .Value
                .Single();

            Assert.Equal("Legacy project profile", profile.Name);
            Assert.Equal(ExportProfileScope.Project, profile.Scope);
            Assert.Equal(@"C:\LegacyExports", profile.OutputDirectory);
            Assert.Equal(6678, profile.TargetEpsg);
            Assert.False(File.Exists(currentPath));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"RevitGeoSuite.FloorPlanExport-MigrationTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
