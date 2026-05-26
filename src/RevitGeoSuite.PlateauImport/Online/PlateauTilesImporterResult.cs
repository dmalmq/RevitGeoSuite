using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport.Online;

public sealed class PlateauTilesImporterResult
{
    public PlateauTilesImporterResult(int importedElementCount, int createdGroupCount, IReadOnlyList<string> warnings)
    {
        ImportedElementCount = importedElementCount;
        CreatedGroupCount = createdGroupCount;
        Warnings = warnings;
    }

    public int ImportedElementCount { get; }

    public int CreatedGroupCount { get; }

    public IReadOnlyList<string> Warnings { get; }
}
