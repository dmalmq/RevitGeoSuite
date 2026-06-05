namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Coarse progress report emitted while a PLATEAU context export runs. Kept as a top-level public
/// type so the shared <see cref="PlateauContextExportPipeline"/> and web shell handlers can report
/// progress without UI-specific state.
/// </summary>
public readonly struct PlateauExportProgress
{
    public PlateauExportProgress(string stage, string? detail = null)
    {
        Stage = stage ?? string.Empty;
        Detail = detail;
    }

    public string Stage { get; }

    public string? Detail { get; }
}
