using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Navigation;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.Tiles3DExport;

[Transaction(TransactionMode.Manual)]
public sealed class Tiles3DExportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiApplication = commandData.Application;
        Document? document = uiApplication.ActiveUIDocument?.Document;

        GeoProjectInfoStorage geoProjectInfoStore = new GeoProjectInfoStorage();
        ModuleStateStorage moduleStateStore = new ModuleStateStorage();
        ProjectLocationReader projectLocationReader = new ProjectLocationReader(geoProjectInfoStore, moduleStateStore: moduleStateStore);
        CurrentProjectStateSummary currentState = projectLocationReader.Read(document);
        RevitDocumentHandle? documentHandle = document is null ? null : new RevitDocumentHandle(document);
        GeoProjectInfo? info = documentHandle is null ? null : geoProjectInfoStore.Load(documentHandle);
        Tiles3DExportStateService stateService = new Tiles3DExportStateService(moduleStateStore);
        Tiles3DExportState? state = documentHandle is null ? null : stateService.Load(documentHandle);

        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        Tiles3DExportViewModel viewModel = new Tiles3DExportViewModel(
            currentState,
            info,
            state,
            new Tiles3DExportReferenceResolver(coordinateTransformer));

        Tiles3DExportWindow window = new Tiles3DExportWindow(
            viewModel,
            documentHandle,
            new Tiles3DExportCoordinator(stateService: stateService));
        new WindowInteropHelper(window).Owner = uiApplication.MainWindowHandle;
        window.ShowDialog();

        if (!string.IsNullOrWhiteSpace(window.PendingModuleNavigationKey))
        {
            return new ModuleWindowNavigator().Navigate(commandData, window.PendingModuleNavigationKey!, ref message);
        }

        return Result.Succeeded;
    }
}

