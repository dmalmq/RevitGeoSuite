using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.Validation;

[Transaction(TransactionMode.Manual)]
public sealed class ValidationCommand : IExternalCommand
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

        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        CoordinateValidator coordinateValidator = new CoordinateValidator(crsRegistry, coordinateTransformer, new JapanMeshCalculator());
        ProjectHealthChecker projectHealthChecker = new ProjectHealthChecker(coordinateValidator);

        ProjectHealthSummary summary = projectHealthChecker.BuildSummary(currentState, info);
        ValidationWindow window = new ValidationWindow(new ValidationViewModel(summary));
        new WindowInteropHelper(window).Owner = uiApplication.MainWindowHandle;
        window.ShowDialog();
        return Result.Succeeded;
    }
}
