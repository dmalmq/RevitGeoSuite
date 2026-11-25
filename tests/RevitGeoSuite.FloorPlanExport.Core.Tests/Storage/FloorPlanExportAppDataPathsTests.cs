using System;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Core;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Storage;

[Collection("FloorPlanAppDataEnv")]
public sealed class FloorPlanExportAppDataPathsTests
{
    [Fact]
    public void The_override_variable_moves_every_per_pc_file_out_of_appdata()
    {
        string root = Path.Combine(Path.GetTempPath(), "rgs-floorplan-data-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable);
        string? previousLegacy = Environment.GetEnvironmentVariable(FloorPlanExportAppDataPaths.LegacyRootOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable, root);
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.LegacyRootOverrideVariable, null);

            Assert.Equal("KANIZU_REVIT_FLOORPLAN_DATA", FloorPlanExportAppDataPaths.RootOverrideVariable);
            Assert.Equal("REVITGEOSUITE_FLOORPLAN_DATA", FloorPlanExportAppDataPaths.LegacyRootOverrideVariable);
            Assert.Equal(Path.Combine(root, "KanizuRevit", "FloorPlanExport", "profiles.json"), FloorPlanExportAppDataPaths.CurrentProfilesFilePath);
            Assert.Equal(Path.Combine(root, "RevitGeoSuite", "FloorPlanExport", "profiles.json"), FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteProfilesFilePath);
            Assert.Equal(Path.Combine(root, "RevitGeoExporter", "profiles.json"), FloorPlanExportAppDataPaths.LegacyProfilesFilePath);
            Assert.StartsWith(root, FloorPlanExportAppDataPaths.CurrentMappingRulesDirectory, StringComparison.Ordinal);
            Assert.StartsWith(root, FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteMappingRulesDirectory, StringComparison.Ordinal);
            Assert.StartsWith(root, FloorPlanExportAppDataPaths.CurrentBaselinesDirectory, StringComparison.Ordinal);
            Assert.StartsWith(root, FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteBaselinesDirectory, StringComparison.Ordinal);
            Assert.StartsWith(root, FloorPlanExportAppDataPaths.CurrentNoticesFilePath, StringComparison.Ordinal);
            Assert.StartsWith(root, FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteNoticesFilePath, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable, previous);
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.LegacyRootOverrideVariable, previousLegacy);
        }
    }

    [Fact]
    public void Legacy_override_variable_is_honored_as_fallback_and_new_variable_wins_when_both_are_set()
    {
        string newRoot = Path.Combine(Path.GetTempPath(), "kanizu-floorplan-data-" + Guid.NewGuid().ToString("N"));
        string legacyRoot = Path.Combine(Path.GetTempPath(), "rgs-floorplan-legacy-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable);
        string? previousLegacy = Environment.GetEnvironmentVariable(FloorPlanExportAppDataPaths.LegacyRootOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable, null);
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.LegacyRootOverrideVariable, legacyRoot);
            Assert.Equal(Path.Combine(legacyRoot, "KanizuRevit", "FloorPlanExport", "settings.json"), FloorPlanExportAppDataPaths.CurrentSettingsFilePath);

            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable, newRoot);
            Assert.Equal(Path.Combine(newRoot, "KanizuRevit", "FloorPlanExport", "settings.json"), FloorPlanExportAppDataPaths.CurrentSettingsFilePath);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable, previous);
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.LegacyRootOverrideVariable, previousLegacy);
        }
    }
}
