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

    /// <summary>The standard name Fix floor types renames the floor type to; empty when the name stays.</summary>
    public string NewName { get; set; } = string.Empty;

    /// <summary>name (the zone names are equal), oldName (a former name) or words (words in the name).</summary>
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

/// <summary>One accepted proposal: a floor type and the standard type it becomes.</summary>
[TsExport]
public sealed class GisAcceptedProposal
{
    public long FloorTypeId { get; set; }

    public string StandardTypeName { get; set; } = string.Empty;
}

/// <summary>Payload of <c>gisStandard.fixFloorTypes</c>: the proposals the user accepted.</summary>
[TsExport]
public sealed class GisFixFloorTypesRequest
{
    public GisAcceptedProposal[] Accepted { get; set; } = Array.Empty<GisAcceptedProposal>();
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

/// <summary>One cell the edited Parameters CSV changes.</summary>
[TsExport]
public sealed class GisParameterChangeItem
{
    public string ElementId { get; set; } = string.Empty;

    /// <summary>floor, room, stair, family or level.</summary>
    public string Kind { get; set; } = string.Empty;

    public string Column { get; set; } = string.Empty;

    public string OldValue { get; set; } = string.Empty;

    public string NewValue { get; set; } = string.Empty;
}

/// <summary>Where <c>gisParameters.write</c> wrote the table; Path is empty when the user cancelled.</summary>
[TsExport]
public sealed class GisParametersWriteResponse
{
    public string Path { get; set; } = string.Empty;

    public int RowCount { get; set; }
}

/// <summary>What the CSV the user picked would change; Path is empty when the user cancelled.</summary>
[TsExport]
public sealed class GisParametersReadResponse
{
    public string Path { get; set; } = string.Empty;

    public int RowCount { get; set; }

    public GisParameterChangeItem[] Changes { get; set; } = Array.Empty<GisParameterChangeItem>();

    /// <summary>Rows and cells that will not be written, one line each.</summary>
    public string[] Problems { get; set; } = Array.Empty<string>();
}

/// <summary>Payload of <c>gisParameters.apply</c>: the changes the user kept ticked.</summary>
[TsExport]
public sealed class GisParametersApplyRequest
{
    public GisParameterChangeItem[] Changes { get; set; } = Array.Empty<GisParameterChangeItem>();
}

/// <summary>Payload of <c>gisDrift.inspect</c>: the station folder's drift file, a path read before, or a file the user picks.</summary>
[TsExport]
public sealed class GisDriftInspectRequest
{
    /// <summary>A drift file to read again; empty for the station folder's file.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>True to let the user pick a drift file.</summary>
    public bool Pick { get; set; }
}

/// <summary>The GDB edits a drift file holds for the open model.</summary>
[TsExport]
public sealed class GisDriftInspectResponse
{
    /// <summary>True when the user closed the file picker; nothing else is set.</summary>
    public bool Cancelled { get; set; }

    /// <summary>station (Kanizu's file for this model), picked (chosen by hand) or none (no file yet).</summary>
    public string Source { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public string DocumentTitle { get; set; } = string.Empty;

    /// <summary>The open model's key (its model id in lower case); empty when it has none yet.</summary>
    public string ModelKey { get; set; } = string.Empty;

    /// <summary>The model the drift file was written for.</summary>
    public string FileModel { get; set; } = string.Empty;

    public string StationFolder { get; set; } = string.Empty;

    /// <summary>Where Kanizu writes this model's drift file; empty without a model id and station folder.</summary>
    public string ExpectedPath { get; set; } = string.Empty;

    public string AppliedVersion { get; set; } = string.Empty;

    public GisDriftChangeItem[] Changes { get; set; } = Array.Empty<GisDriftChangeItem>();

    /// <summary>Shape edits the page can apply, each previewed before anything changes.</summary>
    public GisShapeEditItem[] Shapes { get; set; } = Array.Empty<GisShapeEditItem>();

    /// <summary>Rows and shape edits that cannot be applied, one line each.</summary>
    public string[] Problems { get; set; } = Array.Empty<string>();
}

/// <summary>One shape edit from the GDB: the floor's outline now and the GDB's, in model metres for the page's drawing.</summary>
[TsExport]
public sealed class GisShapeEditItem
{
    /// <summary>The floor's UniqueId: what the page sends back to show or apply the edit.</summary>
    public string UniqueId { get; set; } = string.Empty;

    /// <summary>reshape</summary>
    public string Kind { get; set; } = string.Empty;

    public long ElementId { get; set; }

    /// <summary>The unit's name, or its unique id.</summary>
    public string Feature { get; set; } = string.Empty;

    public string Label { get; set; } = string.Empty;

    /// <summary>The floor's outline now: rings of [x, y] in metres, model coordinates.</summary>
    public double[][][] Before { get; set; } = Array.Empty<double[][]>();

    /// <summary>The GDB's outline: rings of [x, y] in metres, model coordinates.</summary>
    public double[][][] After { get; set; } = Array.Empty<double[][]>();
}

/// <summary>Payload of <c>gisDrift.showShape</c>: draw one edit's outlines over the floor's plan in Revit.</summary>
[TsExport]
public sealed class GisShapeShowRequest
{
    public string Path { get; set; } = string.Empty;

    public string UniqueId { get; set; } = string.Empty;
}

/// <summary>Where the outlines were drawn.</summary>
[TsExport]
public sealed class GisShapeShowResponse
{
    /// <summary>The plan view the outlines are drawn in; empty when no plan shows the floor's level.</summary>
    public string View { get; set; } = string.Empty;
}

/// <summary>Payload of <c>gisDrift.applyShapes</c>: the drift file and the shape edits the user kept ticked, by floor UniqueId.</summary>
[TsExport]
public sealed class GisShapeApplyRequest
{
    public string Path { get; set; } = string.Empty;

    public string[] UniqueIds { get; set; } = Array.Empty<string>();
}

/// <summary>One GDB value to accept into one Revit parameter.</summary>
[TsExport]
public sealed class GisDriftChangeItem
{
    public string UniqueId { get; set; } = string.Empty;

    /// <summary>unit, fixture, level, ...</summary>
    public string Layer { get; set; } = string.Empty;

    /// <summary>The GDB field.</summary>
    public string Field { get; set; } = string.Empty;

    /// <summary>The feature's name, or its unique id.</summary>
    public string Feature { get; set; } = string.Empty;

    public string Parameter { get; set; } = string.Empty;

    /// <summary>instance or type; a type parameter changes every element of the type.</summary>
    public string Binding { get; set; } = string.Empty;

    public string OldValue { get; set; } = string.Empty;

    public string NewValue { get; set; } = string.Empty;

    /// <summary>Revit changed the field too since the last apply; starts unticked.</summary>
    public bool Conflict { get; set; }

    public string Label { get; set; } = string.Empty;
}

/// <summary>Payload of <c>gisDrift.apply</c>: the drift file and the changes the user kept ticked.</summary>
[TsExport]
public sealed class GisDriftApplyRequest
{
    public string Path { get; set; } = string.Empty;

    public GisDriftChangeItem[] Changes { get; set; } = Array.Empty<GisDriftChangeItem>();
}
