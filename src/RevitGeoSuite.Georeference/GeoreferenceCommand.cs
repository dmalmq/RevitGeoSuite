using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.Core.Workflow;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Navigation;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.Georeference;

[Transaction(TransactionMode.Manual)]
public sealed class GeoreferenceCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        UIApplication uiApplication = commandData.Application;
        Document? document = uiApplication.ActiveUIDocument?.Document;

        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        CoordinateValidator coordinateValidator = new CoordinateValidator(crsRegistry, coordinateTransformer, new JapanMeshCalculator());
        PlacementPreviewService placementPreviewService = new PlacementPreviewService(coordinateValidator);
        SplitSurveyProjectBasePointPreviewService splitPreviewService = new SplitSurveyProjectBasePointPreviewService(coordinateValidator);
        GeoProjectInfoStorage geoProjectInfoStore = new GeoProjectInfoStorage();
        ModuleStateStorage moduleStateStore = new ModuleStateStorage();
        ProjectLocationReader reader = new ProjectLocationReader(geoProjectInfoStore, moduleStateStore: moduleStateStore);
        CurrentProjectStateSummary currentState = reader.Read(document);
        GeoreferenceViewModel viewModel = new GeoreferenceViewModel(
            currentState,
            crsRegistry.GetAvailableDefinitions(),
            coordinateTransformer,
            placementPreviewService,
            splitPreviewService);

        RevitGeoPlacementService placementService = new RevitGeoPlacementService(new ProjectLocationWriter(), geoProjectInfoStore, moduleStateStore: moduleStateStore);
        GeoreferenceApplyCoordinator applyCoordinator = new GeoreferenceApplyCoordinator(placementService);
        SplitSurveyProjectBasePointApplyCoordinator splitApplyCoordinator = new SplitSurveyProjectBasePointApplyCoordinator(new SplitSurveyProjectBasePointService(geoProjectInfoStore, moduleStateStore: moduleStateStore));
        RevitDocumentHandle? documentHandle = document is null ? null : new RevitDocumentHandle(document);

        GeoreferenceWindow window = new GeoreferenceWindow(viewModel, applyCoordinator, splitApplyCoordinator, documentHandle);
        new WindowInteropHelper(window).Owner = uiApplication.MainWindowHandle;
        window.ShowDialog();

        if (!string.IsNullOrWhiteSpace(window.PendingModuleNavigationKey))
        {
            return new ModuleWindowNavigator().Navigate(commandData, window.PendingModuleNavigationKey!, ref message);
        }

        return Result.Succeeded;
    }
}
