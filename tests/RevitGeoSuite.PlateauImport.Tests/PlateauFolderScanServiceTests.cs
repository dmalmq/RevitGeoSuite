using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauFolderScanServiceTests
{
    [Fact]
    public void ScanFolder_reads_top_level_supported_files_from_selected_folder()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauFolderScanResult result = new PlateauFolderScanService(new CityGmlParser()).ScanFolder(fixtureFolder);

        Assert.Equal(fixtureFolder, result.FolderPath);
        Assert.Equal(fixtureFolder, result.SearchRootPath);
        Assert.False(result.IsRecursivePackageScan);
        Assert.Equal(6, result.SupportedFilePaths.Count);
        Assert.Equal(5, result.CityModels.Count);
        Assert.Equal(6, result.CityModels.SelectMany(model => model.Features).Count());
        Assert.Equal(new[] { "53394536", "53394537", "54394536" }, result.CityModels.SelectMany(model => model.Features).Select(feature => feature.TileId).Distinct().OrderBy(tileId => tileId).ToArray());
        Assert.DoesNotContain(result.CityModels, model => model.SourcePath.Contains("Nested", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.WarningMessages, warning => warning.Contains("unsupported_notes.xml", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanFolder_returns_models_and_warnings_in_deterministic_discovery_order()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        PlateauFolderScanResult result = new PlateauFolderScanService(new CityGmlParser()).ScanFolder(fixtureFolder);

        Assert.Equal(
            new[]
            {
                "53394536_bldg_sample.gml",
                "53394536_tran_sample.gml",
                "53394536_veg_sample.gml",
                "53394537_dem_sample.gml",
                "54394536_brid_sample.gml"
            },
            result.CityModels.Select(model => Path.GetFileName(model.SourcePath)).ToArray());
        Assert.Equal(
            new[]
            {
                "unsupported_notes.xml"
            },
            result.WarningMessages.Select(GetWarningFileName).ToArray());
    }

    [Fact]
    public void ScanFolder_uses_udx_package_root_when_present()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "PackageRoot", "13104_shinjuku-ku_pref_2023_citygml_2_op");
        PlateauFolderScanResult result = new PlateauFolderScanService(new CityGmlParser()).ScanFolder(fixtureFolder);

        Assert.Equal(fixtureFolder, result.FolderPath);
        Assert.EndsWith("\\udx", result.SearchRootPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(result.IsRecursivePackageScan);
        Assert.Equal(5, result.SupportedFilePaths.Count);
        Assert.Equal(5, result.CityModels.Count);
        Assert.Equal(6, result.CityModels.SelectMany(model => model.Features).Count());
        Assert.Contains(result.SupportedFilePaths, path => path.Contains("udx\\bldg\\53394536_bldg_sample.gml", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.SupportedFilePaths, path => path.Contains("udx\\brid\\54394536_brid_sample.gml", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(result.SupportedFilePaths, path => path.Contains("metadata", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ScanFolder_reports_progress_for_each_supported_file()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string tempFolder = CopyFixtureToTemp(fixtureFolder);
        try
        {
            List<PlateauScanProgress> reported = new List<PlateauScanProgress>();

            PlateauFolderScanResult result = new PlateauFolderScanService(new CityGmlParser()).ScanFolder(tempFolder, reported.Add);

            Assert.NotEmpty(reported);
            Assert.Equal(PlateauScanPhase.Enumerating, reported[0].Phase);
            Assert.Contains(reported, progress => progress.Phase == PlateauScanPhase.Parsing && progress.Current == 0 && progress.Total == result.SupportedFilePaths.Count);
            Assert.Equal(result.SupportedFilePaths.Count, reported.Count(progress => progress.Phase == PlateauScanPhase.Parsing && progress.Current > 0));
            PlateauScanProgress lastProgress = reported[reported.Count - 1];
            Assert.Equal(PlateauScanPhase.Completed, lastProgress.Phase);
            Assert.Equal(result.SupportedFilePaths.Count, lastProgress.Current);
            Assert.Equal(result.SupportedFilePaths.Count, lastProgress.Total);
        }
        finally
        {
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public void ScanFolder_reuses_unchanged_session_cache()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string tempFolder = CopyFixtureToTemp(fixtureFolder);
        try
        {
            PlateauFolderScanService service = new PlateauFolderScanService(new CityGmlParser());

            PlateauFolderScanResult first = service.ScanFolder(tempFolder);
            PlateauFolderScanResult second = service.ScanFolder(tempFolder);

            Assert.False(first.IsFromCache);
            Assert.True(second.IsFromCache);
            Assert.Same(first.CityModels, second.CityModels);
            Assert.Same(first.WarningMessages, second.WarningMessages);
        }
        finally
        {
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public void ScanFolder_invalidates_cache_when_file_timestamp_changes()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string tempFolder = CopyFixtureToTemp(fixtureFolder);
        try
        {
            PlateauFolderScanService service = new PlateauFolderScanService(new CityGmlParser());
            string changedFile = Directory.EnumerateFiles(tempFolder, "*.gml").First();

            PlateauFolderScanResult first = service.ScanFolder(tempFolder);
            PlateauFolderScanResult second = service.ScanFolder(tempFolder);
            File.SetLastWriteTimeUtc(changedFile, DateTime.UtcNow.AddMinutes(5));
            PlateauFolderScanResult third = service.ScanFolder(tempFolder);

            Assert.False(first.IsFromCache);
            Assert.True(second.IsFromCache);
            Assert.False(third.IsFromCache);
        }
        finally
        {
            Directory.Delete(tempFolder, recursive: true);
        }
    }

    [Fact]
    public void ScanFolder_does_not_reuse_cache_for_different_folder_path()
    {
        string fixtureFolder = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport");
        string firstFolder = CopyFixtureToTemp(fixtureFolder);
        string secondFolder = CopyFixtureToTemp(fixtureFolder);
        try
        {
            PlateauFolderScanService service = new PlateauFolderScanService(new CityGmlParser());

            PlateauFolderScanResult first = service.ScanFolder(firstFolder);
            PlateauFolderScanResult second = service.ScanFolder(secondFolder);

            Assert.False(first.IsFromCache);
            Assert.False(second.IsFromCache);
        }
        finally
        {
            Directory.Delete(firstFolder, recursive: true);
            Directory.Delete(secondFolder, recursive: true);
        }
    }

    private static string GetWarningFileName(string warning)
    {
        int start = warning.IndexOf("'", StringComparison.Ordinal);
        int end = warning.IndexOf('\'', start + 1);
        return start >= 0 && end > start
            ? warning.Substring(start + 1, end - start - 1)
            : warning;
    }

    private static string CopyFixtureToTemp(string sourceFolder)
    {
        string tempFolder = Path.Combine(Path.GetTempPath(), "RevitGeoSuitePlateauScanCacheTests", Guid.NewGuid().ToString("N"));
        CopyDirectory(sourceFolder, tempFolder);
        return tempFolder;
    }

    private static void CopyDirectory(string sourceFolder, string targetFolder)
    {
        Directory.CreateDirectory(targetFolder);
        foreach (string filePath in Directory.EnumerateFiles(sourceFolder))
        {
            File.Copy(filePath, Path.Combine(targetFolder, Path.GetFileName(filePath)));
        }

        foreach (string sourceSubfolder in Directory.EnumerateDirectories(sourceFolder))
        {
            CopyDirectory(sourceSubfolder, Path.Combine(targetFolder, Path.GetFileName(sourceSubfolder)));
        }
    }
}
