using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauFolderScanResult
{
    public string FolderPath { get; set; } = string.Empty;

    public string SearchRootPath { get; set; } = string.Empty;

    public bool IsRecursivePackageScan { get; set; }

    public IReadOnlyCollection<string> SupportedFilePaths { get; set; } = new string[0];

    public IReadOnlyCollection<PlateauCityModel> CityModels { get; set; } = new PlateauCityModel[0];

    public IReadOnlyCollection<string> WarningMessages { get; set; } = new string[0];
}
