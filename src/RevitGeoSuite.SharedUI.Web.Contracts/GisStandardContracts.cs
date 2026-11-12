using System;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Payload of <c>gisStandard.inspect</c> and <c>gisStandard.setup</c>: nothing to choose.</summary>
[TsExport]
public sealed class GisStandardEmptyRequest
{
}

/// <summary>Where the active model stands against the Kanizu GIS standard, and what the tools would change.</summary>
[TsExport]
public sealed class GisStandardInspectResponse
{
    public string DocumentTitle { get; set; } = string.Empty;

    /// <summary>The standard version this RevitGeoSuite ships.</summary>
    public int StandardVersion { get; set; }

    /// <summary>The model's <c>gis_standard_version</c>; empty when it was never set up.</summary>
    public string ModelVersion { get; set; } = string.Empty;

    /// <summary>The export's own check: one line on whether the model exports.</summary>
    public string ExportSummary { get; set; } = string.Empty;

    /// <summary>The export check's issues that stop the export.</summary>
    public string[] ExportBlockers { get; set; } = Array.Empty<string>();

    public GisAuditFindingItem[] Findings { get; set; } = Array.Empty<GisAuditFindingItem>();

    /// <summary>Set up model's preview, one line per change; empty when the model is on the standard.</summary>
    public string[] SetupLines { get; set; } = Array.Empty<string>();

    public GisFloorTypeProposalItem[] Proposals { get; set; } = Array.Empty<GisFloorTypeProposalItem>();

    /// <summary>Floor types that need a category but match no standard type.</summary>
    public GisUnmatchedFloorTypeItem[] Unmatched { get; set; } = Array.Empty<GisUnmatchedFloorTypeItem>();

    /// <summary>Why Fix floor types cannot write yet, such as gis_category not bound; empty when it can.</summary>
    public string FixBlocker { get; set; } = string.Empty;
}

/// <summary>One audit finding: something that would need fixing by hand after a GDB export.</summary>
[TsExport]
public sealed class GisAuditFindingItem
{
    /// <summary>error, warning or info.</summary>
    public string Severity { get; set; } = string.Empty;

    /// <summary>standard, parameters, floor types, floors, levels or families.</summary>
    public string Topic { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public long[] ElementIds { get; set; } = Array.Empty<long>();
}

/// <summary>Fix floor types' proposal: give a floor type the category of the standard type it matches.</summary>
[TsExport]
public sealed class GisFloorTypeProposalItem
{
    public long FloorTypeId { get; set; }

    public string FloorTypeName { get; set; } = string.Empty;

    public int FloorCount { get; set; }

    public string StandardTypeName { get; set; } = string.Empty;

    public string GisCategory { get; set; } = string.Empty;

    public string GisRestriction { get; set; } = string.Empty;

    public string Material { get; set; } = string.Empty;

    /// <summary>name (the zone names are equal) or words (words in the name).</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>A floor type that needs a category but matches no standard type.</summary>
[TsExport]
public sealed class GisUnmatchedFloorTypeItem
{
    public long FloorTypeId { get; set; }

    public string Name { get; set; } = string.Empty;

    public long[] FloorIds { get; set; } = Array.Empty<long>();
}

/// <summary>Payload of <c>gisStandard.fixFloorTypes</c>: the floor types whose proposal the user accepted.</summary>
[TsExport]
public sealed class GisFixFloorTypesRequest
{
    public long[] FloorTypeIds { get; set; } = Array.Empty<long>();
}

/// <summary>What <c>gisStandard.setup</c> or <c>gisStandard.fixFloorTypes</c> changed, one line per change.</summary>
[TsExport]
public sealed class GisStandardApplyResponse
{
    public string[] Log { get; set; } = Array.Empty<string>();

    /// <summary>Accepted changes that no longer apply to the model as it is now, one line each.</summary>
    public string[] Skipped { get; set; } = Array.Empty<string>();
}

/// <summary>Payload of <c>gisStandard.select</c>: the elements to select and show in Revit.</summary>
[TsExport]
public sealed class GisSelectElementsRequest
{
    public long[] ElementIds { get; set; } = Array.Empty<long>();
}

/// <summary>How many of the requested elements are still in the model and now selected.</summary>
[TsExport]
public sealed class GisSelectElementsResponse
{
    public int Selected { get; set; }
}
