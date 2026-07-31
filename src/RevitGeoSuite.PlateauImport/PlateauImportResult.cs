using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportResult
{
    public int ImportedElementCount { get; set; }

    public int CreatedGroupCount { get; set; }

    public PlateauImportState UpdatedState { get; set; } = new PlateauImportState();

    public string SummaryMessage { get; set; } = string.Empty;

    public IReadOnlyCollection<string> WarningMessages { get; set; } = new string[0];
}
