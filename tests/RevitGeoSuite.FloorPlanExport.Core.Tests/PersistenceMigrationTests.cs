using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.Diagnostics;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.UI;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests;

[Collection("FloorPlanAppDataEnv")]
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

    [Fact]
    public void DefaultAppDataStores_FallBackToLegacyRevitGeoSuiteAndPreferKanizuRevitWhenPresent()
    {
        string root = CreateTempDirectory();
        string? previous = Environment.GetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable);
        string? previousLegacy = Environment.GetEnvironmentVariable(FloorPlanExportAppDataPaths.LegacyRootOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable, root);
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.LegacyRootOverrideVariable, null);

            // Seed legacy %APPDATA%\RevitGeoSuite\FloorPlanExport files while KanizuRevit folder is absent.
            new ExportDialogSettingsStore(FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteSettingsFilePath).Save(new ExportDialogSettings
            {
                OutputDirectory = @"C:\LegacyRgs",
                TargetEpsg = 6677,
            });
            new ExportProfileStore(FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteProfilesFilePath).SaveProfile("project-a", new ExportProfile
            {
                Name = "Legacy RGS Profile",
                Scope = ExportProfileScope.Project,
                OutputDirectory = @"C:\LegacyRgsProfiles",
                TargetEpsg = 6677,
            });
            new MappingRuleStore(FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteMappingRulesDirectory).Save(
                "project-a",
                ProjectMappingRules.Create(new Dictionary<string, string> { ["Old Floor"] = "walkway" }, null, null, null));
            new ExportBaselineStore(FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteBaselinesDirectory).Save(
                "project-a__default",
                new ExportDiagnosticsReport { OutputFiles = { new ExportDiagnosticsOutputFile { FeatureType = "unit", Path = "unit.gpkg" } } },
                new ExportPackageManifest { Files = { new ExportPackageManifestFile { RelativePath = "unit.gpkg", Kind = "gpkg", IsArtifact = true } } },
                new ExportBaselineSnapshot { BaselineKey = "project-a__default", ConfigurationFingerprint = "legacy-rgs", Views = { new ExportBaselineViewSnapshot { ViewId = 1, ViewName = "1F", ContentFingerprint = "v1" } } });
            new GisStandardNoticeStore(FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteNoticesFilePath).Skip("model-legacy", 2);

            // 1) Read fallback hits legacy RevitGeoSuite when KanizuRevit is absent.
            Assert.Equal(@"C:\LegacyRgs", new ExportDialogSettingsStore().Load().OutputDirectory);
            Assert.Equal("Legacy RGS Profile", new ExportProfileStore().LoadWithDiagnostics("project-a").Value.Single().Name);
            Assert.Equal("walkway", new MappingRuleStore().Load("project-a").FloorCategoryOverrides["Old Floor"]);
            Assert.Equal("legacy-rgs", new ExportBaselineStore().Load("project-a__default").Snapshot?.ConfigurationFingerprint);
            Assert.True(new GisStandardNoticeStore().IsSkipped("model-legacy", 2));

            // 2) Writing goes to new KanizuRevit folder and subsequent reads prefer KanizuRevit over legacy.
            new ExportDialogSettingsStore().Save(new ExportDialogSettings { OutputDirectory = @"C:\NewKanizu", TargetEpsg = 6677 });
            Assert.True(File.Exists(FloorPlanExportAppDataPaths.CurrentSettingsFilePath));
            Assert.Equal(@"C:\NewKanizu", new ExportDialogSettingsStore().Load().OutputDirectory);

            new ExportProfileStore().SaveProfile("project-a", new ExportProfile
            {
                Name = "New Kanizu Profile",
                Scope = ExportProfileScope.Project,
                OutputDirectory = @"C:\NewKanizuProfiles",
                TargetEpsg = 6677,
            });
            Assert.True(File.Exists(FloorPlanExportAppDataPaths.CurrentProfilesFilePath));
            Assert.Contains(new ExportProfileStore().LoadWithDiagnostics("project-a").Value, p => p.Name == "New Kanizu Profile");

            new MappingRuleStore().Save(
                "project-a",
                ProjectMappingRules.Create(new Dictionary<string, string> { ["New Floor"] = "retail" }, null, null, null));
            Assert.Equal("retail", new MappingRuleStore().Load("project-a").FloorCategoryOverrides["New Floor"]);
            Assert.False(new MappingRuleStore().Load("project-a").FloorCategoryOverrides.ContainsKey("Old Floor"));

            new ExportBaselineStore().Save(
                "project-a__default",
                new ExportDiagnosticsReport { OutputFiles = { new ExportDiagnosticsOutputFile { FeatureType = "unit", Path = "unit.gpkg" } } },
                new ExportPackageManifest { Files = { new ExportPackageManifestFile { RelativePath = "unit.gpkg", Kind = "gpkg", IsArtifact = true } } },
                new ExportBaselineSnapshot { BaselineKey = "project-a__default", ConfigurationFingerprint = "new-kanizu", Views = { new ExportBaselineViewSnapshot { ViewId = 1, ViewName = "1F", ContentFingerprint = "v2" } } });
            Assert.Equal("new-kanizu", new ExportBaselineStore().Load("project-a__default").Snapshot?.ConfigurationFingerprint);

            new GisStandardNoticeStore().Skip("model-new", 3);
            Assert.True(File.Exists(FloorPlanExportAppDataPaths.CurrentNoticesFilePath));
            Assert.True(new GisStandardNoticeStore().IsSkipped("model-new", 3));
        }
        finally
        {
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable, previous);
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.LegacyRootOverrideVariable, previousLegacy);
            Directory.Delete(root, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"RevitGeoSuite.FloorPlanExport-MigrationTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
