namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportResult
{
    public Tiles3DExportState UpdatedState { get; set; } = new Tiles3DExportState();

    public string TilesetPath { get; set; } = string.Empty;

    public string ContentPath { get; set; } = string.Empty;

    public bool StatePersisted { get; set; }

    public string SummaryMessage { get; set; } = string.Empty;
}
