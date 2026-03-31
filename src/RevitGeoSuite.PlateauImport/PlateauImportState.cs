using System;
using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportState
{
    public List<string> ImportedTileIds { get; set; } = new List<string>();

    public DateTime? LastImportDateUtc { get; set; }

    public string LastImportedFilePath { get; set; } = string.Empty;

    public PlateauImportReferenceSource LastReferenceSource { get; set; } = PlateauImportReferenceSource.WorkingProjectBasePoint;

    public int LastImportedFeatureCount { get; set; }
}
