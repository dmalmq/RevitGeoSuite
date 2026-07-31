using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauContextImportExecutionResult
{
    public int ImportedElementCount { get; set; }

    public int CreatedGroupCount { get; set; }

    public IReadOnlyCollection<string> WarningMessages { get; set; } = new string[0];
}
