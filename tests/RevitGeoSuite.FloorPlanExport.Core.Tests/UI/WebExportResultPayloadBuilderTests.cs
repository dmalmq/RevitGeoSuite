using System;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.UI;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.UI;

public sealed class WebExportResultPayloadBuilderTests
{
    [Fact]
    public void Build_FallsBackToArtifactDirectoryWhenRequestedDirectoryIsMissing()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            string staleRequestedDirectory = Path.Combine(tempDirectory, "missing");
            string artifactDirectory = Path.Combine(tempDirectory, "actual");
            Directory.CreateDirectory(artifactDirectory);

            FloorGeoPackageExportResult result = new();
            result.AddArtifactResult(new ExportArtifactResult(
                artifactKey: "view:1",
                artifactName: "Level 1",
                outputFilePath: Path.Combine(artifactDirectory, "Level_1.gpkg"),
                packagingMode: PackagingMode.PerViewGeoPackage,
                disposition: ArtifactDisposition.Written,
                contributingViewIds: new[] { 1L },
                contributingViewNames: new[] { "Level 1" },
                contributingLevelNames: new[] { "Level 1" },
                layerNames: new[] { "unit" },
                featureCount: 1));

            var payload = WebExportResultPayloadBuilder.Build(result, staleRequestedDirectory, UiLanguage.English);

            Assert.Equal(artifactDirectory, payload.OutputDirectory);
            Assert.True(payload.CanOpenOutputDirectory);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Build_KeepsRequestedDirectoryWhenItExists()
    {
        string tempDirectory = CreateTempDirectory();
        try
        {
            string requestedDirectory = Path.Combine(tempDirectory, "requested");
            string artifactDirectory = Path.Combine(tempDirectory, "actual");
            Directory.CreateDirectory(requestedDirectory);
            Directory.CreateDirectory(artifactDirectory);

            FloorGeoPackageExportResult result = new();
            result.AddArtifactResult(new ExportArtifactResult(
                artifactKey: "view:1",
                artifactName: "Level 1",
                outputFilePath: Path.Combine(artifactDirectory, "Level_1.gpkg"),
                packagingMode: PackagingMode.PerViewGeoPackage,
                disposition: ArtifactDisposition.Written,
                contributingViewIds: new[] { 1L },
                contributingViewNames: new[] { "Level 1" },
                contributingLevelNames: new[] { "Level 1" },
                layerNames: new[] { "unit" },
                featureCount: 1));

            var payload = WebExportResultPayloadBuilder.Build(result, requestedDirectory, UiLanguage.English);

            Assert.Equal(requestedDirectory, payload.OutputDirectory);
            Assert.True(payload.CanOpenOutputDirectory);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), $"RevitGeoSuite.FloorPlanExport-ResultPayloadTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }
}
