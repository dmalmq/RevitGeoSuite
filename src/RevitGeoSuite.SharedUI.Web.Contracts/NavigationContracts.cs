namespace RevitGeoSuite.SharedUI.Web.Contracts;

[TsExport]
public sealed class NavigateRequest
{
    public string Route { get; set; } = string.Empty;
}

[TsExport]
public sealed class NavigateEvent
{
    public string Route { get; set; } = string.Empty;
}

/// <summary>Emitted when the active Revit document changes so the modeless shell refreshes its status and document binding.</summary>
[TsExport]
public sealed class DocumentChangedEvent
{
    public string DocumentId { get; set; } = string.Empty;

    public string DocumentTitle { get; set; } = string.Empty;
}
