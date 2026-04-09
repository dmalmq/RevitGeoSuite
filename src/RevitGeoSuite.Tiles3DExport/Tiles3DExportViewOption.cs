using Autodesk.Revit.DB;

namespace RevitGeoSuite.Tiles3DExport;

public sealed class Tiles3DExportViewOption
{
    public ElementId ViewId { get; set; } = ElementId.InvalidElementId;

    public string UniqueId { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}