using System;
using System.Collections.Generic;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportState
{
    public string LastExportPath { get; set; } = string.Empty;

    public string LastLodSetting { get; set; } = Tiles3DLevelOfDetail.Medium.ToString();

    public DateTime? LastExportDateUtc { get; set; }

    public Tiles3DExportReferenceSource LastReferenceSource { get; set; } = Tiles3DExportReferenceSource.WorkingProjectBasePoint;

    public Tiles3DExportScopeMode LastScopeMode { get; set; } = Tiles3DExportScopeMode.WholeModel;

    public string LastViewUniqueId { get; set; } = string.Empty;

    public string LastViewName { get; set; } = string.Empty;

    public List<string> LastSelectedLinkUniqueIds { get; set; } = new List<string>();

    public List<string> LastSelectedLinkNames { get; set; } = new List<string>();

    public int LastExportedElementCount { get; set; }

    public int LastExportedTriangleCount { get; set; }

    public bool LastSplitByLevel { get; set; }

    public int LastExportedLevelCount { get; set; }

    public bool LastUsedPreciseCrsProjection { get; set; }

    public double LastGeoidHeightOffsetMeters { get; set; }
}