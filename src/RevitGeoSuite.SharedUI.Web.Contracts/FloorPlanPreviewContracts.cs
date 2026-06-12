using System.Collections.Generic;

namespace RevitGeoSuite.SharedUI.Web.Contracts;

[TsExport]
public sealed class PreviewInitialStateRequest
{
}

[TsExport]
public sealed class PreviewInitialStateResponse
{
    public string Language { get; set; } = "english";

    public string CoordinateSummary { get; set; } = string.Empty;

    public List<PreviewViewOption> Views { get; set; } = new();

    public List<string> SupportedCategories { get; set; } = new();

    public PreviewAssignmentSummaryPayload AssignmentSummary { get; set; } = new();

    public PreviewViewPayload? CurrentView { get; set; }

    public int ReadinessWarningCount { get; set; }

    public int ReadinessUnassignedFloorTypeCount { get; set; }

    public int ReadinessIssueCount { get; set; }
}

[TsExport]
public sealed class PreviewLoadViewRequest
{
    public long ViewId { get; set; }
}

[TsExport]
public sealed class PreviewAssignmentRequest
{
    public List<string> FloorTypeNames { get; set; } = new();

    public string Category { get; set; } = string.Empty;
}

[TsExport]
public sealed class PreviewClearAssignmentRequest
{
    public List<string> FloorTypeNames { get; set; } = new();
}

[TsExport]
public sealed class PreviewAssignmentSummaryPayload
{
    public string SourceLabel { get; set; } = string.Empty;

    public string PendingMessage { get; set; } = string.Empty;

    public bool HasPendingAssignments { get; set; }

    public int FloorTypeCount { get; set; }

    public int AssignedFloorTypeCount { get; set; }

    public int UnassignedFloorTypeCount { get; set; }

    public List<PreviewCategoryAssignmentRowPayload> Rows { get; set; } = new();
}

[TsExport]
public sealed class PreviewCategoryAssignmentRowPayload
{
    public string FloorTypeName { get; set; } = string.Empty;

    public string Category { get; set; } = string.Empty;

    public string ParsedCandidate { get; set; } = string.Empty;

    public int UnitCount { get; set; }

    public int ViewCount { get; set; }

    public List<string> ViewNames { get; set; } = new();

    public List<string> SampleUnits { get; set; } = new();

    public bool UsesOverride { get; set; }

    public bool IsUnassigned { get; set; }

    public string Status { get; set; } = string.Empty;
}

[TsExport]
public sealed class PreviewViewOption
{
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;
}

[TsExport]
public sealed class PreviewViewPayload
{
    public long ViewId { get; set; }

    public string ViewName { get; set; } = string.Empty;

    public string LevelName { get; set; } = string.Empty;

    public string QuickSummary { get; set; } = string.Empty;

    public string Instruction { get; set; } = string.Empty;

    public PreviewBoundsPayload Bounds { get; set; } = new();

    public List<PreviewFeaturePayload> Features { get; set; } = new();

    public List<PreviewLegendPayload> Legend { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public List<PreviewUnassignedFloorPayload> UnassignedFloors { get; set; } = new();

    public string AssignmentSourceLabel { get; set; } = string.Empty;

    public string AssignmentPendingMessage { get; set; } = string.Empty;

    public bool HasPendingAssignments { get; set; }
}

[TsExport]
public sealed class PreviewBoundsPayload
{
    public double MinX { get; set; }

    public double MinY { get; set; }

    public double MaxX { get; set; }

    public double MaxY { get; set; }

    public bool IsEmpty { get; set; }
}

[TsExport]
public sealed class PreviewPointPayload
{
    public double X { get; set; }

    public double Y { get; set; }
}

[TsExport]
public sealed class PreviewFeaturePayload
{
    public int Index { get; set; }

    public string FeatureType { get; set; } = string.Empty;

    public string GeometryType { get; set; } = string.Empty;

    public List<List<PreviewPointPayload>> Rings { get; set; } = new();

    public List<PreviewPointPayload> Points { get; set; } = new();

    public string Category { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Restriction { get; set; } = string.Empty;

    public string ExportId { get; set; } = string.Empty;

    public string SourceLabel { get; set; } = string.Empty;

    public string FillColor { get; set; } = string.Empty;

    public string StrokeColor { get; set; } = string.Empty;

    public bool HasWarning { get; set; }

    public bool IsUnassignedFloor { get; set; }

    public bool UsesFloorCategoryOverride { get; set; }

    public bool SupportsFloorCategoryAssignment { get; set; }

    public string FloorTypeName { get; set; } = string.Empty;

    public string ParsedZoneCandidate { get; set; } = string.Empty;

    public string SearchText { get; set; } = string.Empty;
}

[TsExport]
public sealed class PreviewLegendPayload
{
    public string Label { get; set; } = string.Empty;

    public int Count { get; set; }

    public string FillColor { get; set; } = string.Empty;
}

[TsExport]
public sealed class PreviewUnassignedFloorPayload
{
    public string FloorTypeName { get; set; } = string.Empty;

    public string ParsedCandidate { get; set; } = string.Empty;

    public int UnitCount { get; set; }
}
