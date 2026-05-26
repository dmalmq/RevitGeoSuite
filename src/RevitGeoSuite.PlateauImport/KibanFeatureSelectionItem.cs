namespace RevitGeoSuite.PlateauImport;

public sealed class KibanFeatureSelectionItem : SelectableOptionBase
{
    public string LayerName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int FeatureCount { get; set; }
}
