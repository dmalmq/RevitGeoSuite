using System;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Core;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Storage;

public sealed class FloorPlanExportAppDataPathsTests
{
    [Fact]
    public void The_override_variable_moves_every_per_pc_file_out_of_appdata()
    {
        string root = Path.Combine(Path.GetTempPath(), "rgs-floorplan-data-" + Guid.NewGuid().ToString("N"));
        string? previous = Environment.GetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable);
        try
        {
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable, root);

            Assert.Equal(Path.Combine(root, "RevitGeoSuite", "FloorPlanExport", "profiles.json"), FloorPlanExportAppDataPaths.CurrentProfilesFilePath);
            Assert.StartsWith(root, FloorPlanExportAppDataPaths.CurrentMappingRulesDirectory, StringComparison.Ordinal);
            Assert.StartsWith(root, FloorPlanExportAppDataPaths.LegacyProfilesFilePath, StringComparison.Ordinal);
            Assert.StartsWith(root, FloorPlanExportAppDataPaths.CurrentBaselinesDirectory, StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(FloorPlanExportAppDataPaths.RootOverrideVariable, previous);
        }
    }
}
