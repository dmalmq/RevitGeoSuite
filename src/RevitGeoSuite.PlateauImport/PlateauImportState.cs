using System;
using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauImportState
{
    public List<string> ImportedTileIds { get; set; } = new List<string>();

    public DateTime? LastImportDateUtc { get; set; }

    public string LastImportedFilePath { get; set; } = string.Empty;

    public string LastImportedFolderPath { get; set; } = string.Empty;

    public PlateauImportReferenceSource LastReferenceSource { get; set; } = PlateauImportReferenceSource.WorkingProjectBasePoint;

    public PlateauGeometryImportMode LastGeometryImportMode { get; set; } = PlateauGeometryImportMode.LightweightExtrusion;

    public int LastImportedFeatureCount { get; set; }

    public int LastImportedGroupCount { get; set; }

    public List<string> LastSelectedTileIds { get; set; } = new List<string>();

    public List<string> LastSelectedFeatureTypes { get; set; } = new List<string>();

    public string LastImportSummary { get; set; } = string.Empty;
}
