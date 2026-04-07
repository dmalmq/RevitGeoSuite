using System.Collections.Generic;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportPreparationResult
{
    public Tiles3DExportPackage Package { get; set; } = new Tiles3DExportPackage();

    public IReadOnlyCollection<DetailRow> PreparedRows { get; set; } = new DetailRow[0];

    public IReadOnlyCollection<string> FeatureNames { get; set; } = new string[0];

    public string StatusMessage { get; set; } = string.Empty;
}
