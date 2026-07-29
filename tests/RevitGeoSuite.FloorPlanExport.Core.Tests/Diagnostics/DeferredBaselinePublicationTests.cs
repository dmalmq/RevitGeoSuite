using System;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Core.Diagnostics;
using RevitGeoSuite.FloorPlanExport.Export;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Diagnostics;

public sealed class DeferredBaselinePublicationTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "revitgeosuite-baseline-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void CommitPendingBaseline_RebasesStagedPathsBeforeSaving()
    {
        string stagingRoot = Path.Combine(_root, ".package.staging");
        string destinationRoot = Path.Combine(_root, "package");
        string stagedArtifact = Path.Combine(stagingRoot, "gis", "building.gpkg");
        string finalArtifact = Path.Combine(destinationRoot, "gis", "building.gpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(finalArtifact)!);
        File.WriteAllText(finalArtifact, "gpkg");

        var snapshot = new ExportBaselineSnapshot
        {
            BaselineKey = "project__profile__cesium",
            Views = { new ExportBaselineViewSnapshot { ViewId = 1 } },
            Artifacts =
            {
                new ExportBaselineArtifactSnapshot
                {
                    ArtifactKey = "building",
                    OutputFilePath = stagedArtifact,
                },
            },
        };
        var report = new ExportDiagnosticsReport
        {
            OutputFiles = { new ExportDiagnosticsOutputFile { Path = stagedArtifact } },
        };
        var manifest = new ExportPackageManifest
        {
            PackageDirectory = Path.Combine(stagingRoot, "gis"),
            Files =
            {
                new ExportPackageManifestFile
                {
                    ArtifactKey = "building",
                    OutputFilePath = stagedArtifact,
                },
            },
        };
        var result = new FloorGeoPackageExportResult();
        result.SetPendingBaselineSnapshot(snapshot);
        result.SetPendingBaselineUpdate(report, manifest);
        var store = new ExportBaselineStore(Path.Combine(_root, "baselines"));

        result.CommitPendingBaseline(stagingRoot, destinationRoot, store);

        ExportBaselineLoadResult saved = store.Load(snapshot.BaselineKey);
        Assert.Equal(finalArtifact, Assert.Single(saved.Snapshot!.Artifacts).OutputFilePath);
        Assert.Equal(finalArtifact, Assert.Single(saved.Report!.OutputFiles).Path);
        Assert.Equal(Path.Combine(destinationRoot, "gis"), saved.Manifest!.PackageDirectory);
        Assert.Equal(finalArtifact, Assert.Single(saved.Manifest.Files).OutputFilePath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
