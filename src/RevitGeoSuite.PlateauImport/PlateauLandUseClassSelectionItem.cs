namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauLandUseClassSelectionItem : SelectableOptionBase
{
    public string ClassCode { get; set; } = string.Empty;

    public string ClassName { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public int FeatureCount { get; set; }
}
