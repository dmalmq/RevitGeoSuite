using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauCityModel
{
    public string SourcePath { get; set; } = string.Empty;

    public string SrsName { get; set; } = string.Empty;

    public int? EpsgCode { get; set; }

    public string? FileTileId { get; set; }

    public IReadOnlyCollection<PlateauBuildingFeature> Buildings { get; set; } = new PlateauBuildingFeature[0];
}
