namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportResult
{
    public int ImportedElementCount { get; set; }

    public PlateauImportState UpdatedState { get; set; } = new PlateauImportState();

    public string SummaryMessage { get; set; } = string.Empty;
}
