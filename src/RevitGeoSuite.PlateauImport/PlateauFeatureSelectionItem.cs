namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauFeatureSelectionItem : SelectableOptionBase
{
    public PlateauFeatureType FeatureType { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public int FeatureCount { get; set; }

    public int SourceFileCount { get; set; }

    public string SourceFilesSummary { get; set; } = string.Empty;
}
