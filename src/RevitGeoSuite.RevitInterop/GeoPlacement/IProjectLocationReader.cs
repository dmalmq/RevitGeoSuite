using Autodesk.Revit.DB;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public interface IProjectLocationReader
{
    CurrentProjectStateSummary Read(Document? document);
}
