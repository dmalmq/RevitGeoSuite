using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauCityModel
{
    public string SourcePath { get; set; } = string.Empty;

    public string SrsName { get; set; } = string.Empty;

    public int? EpsgCode { get; set; }

    public string? FileTileId { get; set; }

    public IReadOnlyCollection<PlateauContextFeature> Features { get; set; } = new PlateauContextFeature[0];

    public IReadOnlyCollection<PlateauBuildingFeature> Buildings => Features.OfType<PlateauBuildingFeature>().ToArray();
}
