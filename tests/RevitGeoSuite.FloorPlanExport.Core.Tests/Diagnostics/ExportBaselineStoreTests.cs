using System;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Core.Diagnostics;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Diagnostics;

public sealed class ExportBaselineStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsDiagnosticsManifestAndSnapshot()
    {
        string rootDirectory = CreateTempDirectory();
        try
        {
            ExportBaselineStore store = new(rootDirectory);
            ExportDiagnosticsReport report = new()
            {
                SourceModelName = "Model",
                OutputFiles = { new ExportDiagnosticsOutputFile { FeatureType = "unit", Path = "unit.gpkg" } },
            };
            ExportPackageManifest manifest = new()
            {
                SourceModelName = "Model",
                Files = { new ExportPackageManifestFile { RelativePath = "unit.gpkg", Kind = "gpkg", IsArtifact = true } },
            };
            ExportBaselineSnapshot snapshot = new()
            {
                BaselineKey = "project__profile",
                ConfigurationFingerprint = "abc123",
                Views = { new ExportBaselineViewSnapshot { ViewId = 1, ViewName = "Level 1", ContentFingerprint = "view-hash" } },
            };

            store.Save("project__profile", report, manifest, snapshot);
            ExportBaselineLoadResult loaded = store.Load("project__profile");

            Assert.NotNull(loaded.Report);
            Assert.NotNull(loaded.Manifest);
            Assert.NotNull(loaded.Snapshot);
            Assert.Equal("abc123", loaded.Snapshot!.ConfigurationFingerprint);
            Assert.Equal("unit.gpkg", loaded.Manifest!.Files[0].RelativePath);
            Assert.Equal("unit", loaded.Report!.OutputFiles[0].FeatureType);
        }
        finally
        {
            Directory.Delete(rootDirectory, recursive: true);
        }
    }

    [Fact]
    public void Load_FallsBackToLegacyDirectoryWhenCurrentBaselineIsMissing()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            string currentDirectory = Path.Combine(tempDirectory, "current");
            string legacyDirectory = Path.Combine(tempDirectory, "legacy");
            ExportDiagnosticsReport report = new()
            {
                OutputFiles = { new ExportDiagnosticsOutputFile { FeatureType = "unit", Path = "unit.gpkg" } },
            };
            ExportPackageManifest manifest = new()
            {
                Files = { new ExportPackageManifestFile { RelativePath = "unit.gpkg", Kind = "gpkg", IsArtifact = true } },
            };
            ExportBaselineSnapshot snapshot = new()
            {
                BaselineKey = "project__profile",
                ConfigurationFingerprint = "legacy-hash",
                Views = { new ExportBaselineViewSnapshot { ViewId = 1, ViewName = "Level 1", ContentFingerprint = "view-hash" } },
            };
            new ExportBaselineStore(legacyDirectory).Save("project__profile", report, manifest, snapshot);

            ExportBaselineLoadResult loaded = new ExportBaselineStore(currentDirectory, legacyDirectory).Load("project__profile");

            Assert.NotNull(loaded.Report);
            Assert.NotNull(loaded.Manifest);
            Assert.NotNull(loaded.Snapshot);
            Assert.Equal("legacy-hash", loaded.Snapshot!.ConfigurationFingerprint);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"RevitGeoSuite.FloorPlanExport-BaselineTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
