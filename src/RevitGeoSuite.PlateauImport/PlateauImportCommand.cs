using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Tiles;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.PlateauImport;

[Transaction(TransactionMode.Manual)]
public sealed class PlateauImportCommand : IExternalCommand
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
        PlateauImportStateService stateService = new PlateauImportStateService(moduleStateStore);
        PlateauImportState? state = documentHandle is null ? null : stateService.Load(documentHandle);

        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        PlateauImportViewModel viewModel = new PlateauImportViewModel(
            currentState,
            info,
            state,
            new PlateauImportReferenceResolver(coordinateTransformer),
            new PlateauTileIndex(),
            new CityGmlParser(),
            new ContextGeometryBuilder());

        PlateauImportWindow window = new PlateauImportWindow(
            viewModel,
            documentHandle,
            new PlateauImportCoordinator(new PlateauContextImporter(), stateService));
        new WindowInteropHelper(window).Owner = uiApplication.MainWindowHandle;
        window.ShowDialog();
        return Result.Succeeded;
    }
}
