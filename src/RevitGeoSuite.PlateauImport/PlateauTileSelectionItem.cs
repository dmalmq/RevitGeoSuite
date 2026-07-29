namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauTileSelectionItem : SelectableOptionBase
{
    public string TileId { get; set; } = string.Empty;

    public int FeatureCount { get; set; }

    public int SourceFileCount { get; set; }

    public string SourceFilesSummary { get; set; } = string.Empty;

    public bool IsSuggested { get; set; }
}
