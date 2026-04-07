using System;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportState
{
    public string LastExportPath { get; set; } = string.Empty;

    public string LastLodSetting { get; set; } = Tiles3DLevelOfDetail.Medium.ToString();

    public DateTime? LastExportDateUtc { get; set; }

    public Tiles3DExportReferenceSource LastReferenceSource { get; set; } = Tiles3DExportReferenceSource.WorkingProjectBasePoint;

    public int LastExportedElementCount { get; set; }

    public int LastExportedTriangleCount { get; set; }
}
