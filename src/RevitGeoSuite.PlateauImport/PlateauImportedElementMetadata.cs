namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportedElementMetadata
{
    public string SourceFeatureId { get; set; } = string.Empty;

    public string TileId { get; set; } = string.Empty;

    public PlateauFeatureType FeatureType { get; set; }
}
