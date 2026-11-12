using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.FloorPlanExport.Export;

public interface IExportMetadataProvider
{
    ExportElementMetadata GetElementMetadata(Element element, ICollection<string> warnings);

    ExportLevelMetadata GetLevelMetadata(Level level, ICollection<string> warnings);

    /// <summary>
    /// The id of a level that a feature only refers to, such as the far end of a stair, or null
    /// when the level has none. Never writes to the model: the extractors call it with no
    /// transaction open, after the export has given the exported views' levels their ids.
    /// </summary>
    string? GetReferencedLevelId(Level level, ICollection<string> warnings);

    string? GetOptionalStringParameter(Element element, string parameterName);
}
