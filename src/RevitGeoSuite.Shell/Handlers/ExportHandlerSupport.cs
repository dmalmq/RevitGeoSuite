using Autodesk.Revit.DB;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>
/// Shared helpers for the web-shell export handlers. Reads the current project state + stored
/// GeoProjectInfo the same way the legacy export commands do, so the export coordinators receive
/// identical inputs.
/// </summary>
internal static class ExportHandlerSupport
{
    public static (RevitDocumentHandle Handle, CurrentProjectStateSummary CurrentState, GeoProjectInfo? Info) ReadContext(Document document)
    {
        var geoProjectInfoStore = new GeoProjectInfoStorage();
        var projectLocationReader = new ProjectLocationReader(geoProjectInfoStore, moduleStateStore: new ModuleStateStorage());
        CurrentProjectStateSummary currentState = projectLocationReader.Read(document);

        var handle = new RevitDocumentHandle(document);
        GeoProjectInfo? info = geoProjectInfoStore.Load(handle);
        return (handle, currentState, info);
    }
}
