using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.MeshInspector;

[Transaction(TransactionMode.Manual)]
public sealed class MeshInspectorCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiApplication = commandData.Application;
        Document? document = uiApplication.ActiveUIDocument?.Document;

        GeoProjectInfoStorage geoProjectInfoStore = new GeoProjectInfoStorage();
        ProjectLocationReader projectLocationReader = new ProjectLocationReader(geoProjectInfoStore, moduleStateStore: new ModuleStateStorage());
        CurrentProjectStateSummary currentState = projectLocationReader.Read(document);
        RevitDocumentHandle? documentHandle = document is null ? null : new RevitDocumentHandle(document);
        GeoProjectInfo? info = documentHandle is null ? null : geoProjectInfoStore.Load(documentHandle);

        MeshInspectorService service = new MeshInspectorService(new JapanMeshCalculator());
        MeshInspectorViewModel viewModel = new MeshInspectorViewModel(service, currentState, info);
        MeshInspectorWindow window = new MeshInspectorWindow(viewModel, documentHandle, new MeshCodePersistenceService(geoProjectInfoStore));
        new WindowInteropHelper(window).Owner = uiApplication.MainWindowHandle;
        window.ShowDialog();
        return Result.Succeeded;
    }
}
