using Autodesk.Revit.DB;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlExportViewOption
{
    public ElementId ViewId { get; set; } = ElementId.InvalidElementId;

    public string UniqueId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
