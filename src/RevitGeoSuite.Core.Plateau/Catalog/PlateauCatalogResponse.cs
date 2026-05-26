using System.Collections.Generic;
using Newtonsoft.Json;

namespace RevitGeoSuite.Core.Plateau.Catalog;

public sealed class PlateauCatalogResponse
{
    [JsonProperty("datasets")]
    public List<PlateauDatasetEntry>? Datasets { get; set; }

    [JsonProperty("latest_datasets")]
    public List<PlateauDatasetEntry>? LatestDatasets { get; set; }
}
