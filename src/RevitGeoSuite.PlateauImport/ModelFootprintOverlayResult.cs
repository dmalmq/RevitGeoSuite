namespace RevitGeoSuite.PlateauImport;

public sealed class ModelFootprintOverlayResult
{
    public string GeoJson { get; set; } = string.Empty;

    public string StatusMessage { get; set; } = string.Empty;

    public int IncludedElementCount { get; set; }

    public bool HasOverlay => !string.IsNullOrWhiteSpace(GeoJson);
}
