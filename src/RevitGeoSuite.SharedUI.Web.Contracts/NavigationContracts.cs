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
