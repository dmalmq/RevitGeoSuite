using System;
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
}
