namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportResult
{
    public CityGmlExportState UpdatedState { get; set; } = new CityGmlExportState();

    public string ExportPath { get; set; } = string.Empty;

    public bool StatePersisted { get; set; }

    public string SummaryMessage { get; set; } = string.Empty;
}

