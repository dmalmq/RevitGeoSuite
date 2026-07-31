using System;
using System.IO;
using RevitGeoSuite.FloorPlanExport.Core.Diagnostics;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.Core.Models;
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

    [Fact]
    public void ReusableArtifact_IsMatchedByIdentityAndCopiedAcrossStagingRoots()
    {
        string publishedPath = Path.Combine(_root, "package", "gis", "building.gpkg");
        string stagedPath = Path.Combine(_root, ".package.staging", "gis", "building.gpkg");
        Directory.CreateDirectory(Path.GetDirectoryName(publishedPath)!);
        File.WriteAllText(publishedPath, "gpkg");
        var snapshot = new ExportBaselineSnapshot
        {
            Artifacts =
            {
                new ExportBaselineArtifactSnapshot
                {
                    ArtifactKey = "building",
                    PackagingMode = "PerBuildingGeoPackage",
                    OutputFilePath = publishedPath,
                },
            },
        };

        string? reuseSource = ExportArtifactReuse.FindReusableArtifactPath(
            snapshot, "building", "PerBuildingGeoPackage");
        ExportArtifactReuse.CopyReusableArtifact(reuseSource!, stagedPath);

        Assert.Equal(publishedPath, reuseSource);
        Assert.Equal("gpkg", File.ReadAllText(stagedPath));
    }

    [Fact]
    public void ConfigurationFingerprint_ChangesAcrossOutputFormatTransitions()
    {
        string shapefileInput = ExportArtifactReuse.BuildOutputFormatFingerprintInput(ExportFormat.Shapefile);
        string geoPackageInput = ExportArtifactReuse.BuildOutputFormatFingerprintInput(ExportFormat.GeoPackage);

        Assert.NotEqual(shapefileInput, geoPackageInput);
    }

    [Fact]
    public void CopyReusableArtifact_CopiesShapefileComponents()
    {
        string publishedPath = Path.Combine(_root, "package", "gis", "unit.shp");
        string stagedPath = Path.Combine(_root, ".package.staging", "gis", "unit.shp");
        Directory.CreateDirectory(Path.GetDirectoryName(publishedPath)!);
        foreach (string extension in new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg" })
        {
            File.WriteAllText(Path.ChangeExtension(publishedPath, extension), extension);
        }

        ExportArtifactReuse.CopyReusableArtifact(publishedPath, stagedPath);

        foreach (string extension in new[] { ".shp", ".shx", ".dbf", ".prj", ".cpg" })
        {
            Assert.Equal(extension, File.ReadAllText(Path.ChangeExtension(stagedPath, extension)));
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
