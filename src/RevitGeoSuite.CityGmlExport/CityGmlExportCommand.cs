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

namespace RevitGeoSuite.CityGmlExport;

[Transaction(TransactionMode.Manual)]
public sealed class CityGmlExportCommand : IExternalCommand
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
        CityGmlExportStateService stateService = new CityGmlExportStateService(moduleStateStore);
        CityGmlExportState? state = documentHandle is null ? null : stateService.Load(documentHandle);

        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        CityGmlExportViewModel viewModel = new CityGmlExportViewModel(
            currentState,
            info,
            state,
            new CityGmlExportReferenceResolver(coordinateTransformer),
            CityGmlExportDocumentCatalog.CreateViewOptions(document),
            CityGmlExportDocumentCatalog.CreateLinkOptions(document),
            document?.ActiveView is View3D activeView && !activeView.IsTemplate ? activeView.UniqueId : null);

        CityGmlExportWindow window = new CityGmlExportWindow(
            viewModel,
            documentHandle,
            new CityGmlExportCoordinator(stateService: stateService));
        new WindowInteropHelper(window).Owner = uiApplication.MainWindowHandle;
        window.ShowDialog();

        if (!string.IsNullOrWhiteSpace(window.PendingModuleNavigationKey))
        {
            return new ModuleWindowNavigator().Navigate(commandData, window.PendingModuleNavigationKey!, ref message);
        }

        return Result.Succeeded;
    }
}

