using System;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.PlateauImport.Online;

[Transaction(TransactionMode.Manual)]
public sealed class PlateauOnlineImportCommand : IExternalCommand
{
    public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
    {
        try
        {
            return ExecuteCore(commandData, ref message);
        }
        catch (Exception ex)
        {
            message = FormatDiagnosticMessage(ex);
            return Result.Failed;
        }
    }

    private static string FormatDiagnosticMessage(Exception ex)
    {
        Exception inner = ex;
        while (inner.InnerException is not null) inner = inner.InnerException;
        string[] frames = (inner.StackTrace ?? string.Empty)
            .Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        string topFrames = string.Join(Environment.NewLine, frames.Take(3));
        return $"{inner.GetType().FullName}: {inner.Message}{Environment.NewLine}{topFrames}";
    }

    private static Result ExecuteCore(ExternalCommandData commandData, ref string message)
    {
        UIApplication uiApplication = commandData.Application;
        Document? document = uiApplication.ActiveUIDocument?.Document;
        if (document is null)
        {
            message = "Open a Revit project before importing PLATEAU data.";
            return Result.Failed;
        }

        GeoProjectInfoStorage geoProjectInfoStore = new GeoProjectInfoStorage();
        ModuleStateStorage moduleStateStore = new ModuleStateStorage();
        ProjectLocationReader projectLocationReader = new ProjectLocationReader(geoProjectInfoStore, moduleStateStore: moduleStateStore);
        CurrentProjectStateSummary currentState = projectLocationReader.Read(document);
        RevitDocumentHandle documentHandle = new RevitDocumentHandle(document);
        GeoProjectInfo? info = geoProjectInfoStore.Load(documentHandle);

        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        PlateauImportReferenceResolver referenceResolver = new PlateauImportReferenceResolver(
            coordinateTransformer,
            new RevitPlateauImportLocalBasisProvider(document));
        PlateauImportReferenceContext? referenceContext = referenceResolver.Resolve(currentState, info, PlateauImportReferenceSource.CanonicalOrigin);
        if (referenceContext is null)
        {
            message = "Could not resolve the project's georeference. Run the Georeference command first.";
            return Result.Failed;
        }

        PlateauHttpClient apiHttpClient = new PlateauHttpClient();
        PlateauApiClient apiClient = new PlateauApiClient(apiHttpClient);
        PlateauAreaGeometryService geometryService = new PlateauAreaGeometryService(apiHttpClient);
        PlateauOnlineImportViewModel viewModel = new PlateauOnlineImportViewModel(
            apiClient,
            geometryService,
            () =>
            {
                EcefToProjectTransformer transformer = CreateOnlineEcefTransformer(coordinateTransformer, referenceContext);
                IDracoMeshDecoder draco = NativeDracoMeshDecoder.IsAvailable()
                    ? new NativeDracoMeshDecoder()
                    : (IDracoMeshDecoder)new MissingDracoMeshDecoder();
                return new PlateauTilesetDownloader(new PlateauHttpClient(), new GltfMeshDecoder(draco), transformer);
            });

        const double feetToMeters = 0.3048d;
        // Civil 3D shared-coordinates convention: (0,0,0) of the DXF should land at the
        // Survey Point, and the labelled marker sits at the Project Base Point.
        Vector3d projectBasePointMeters = new Vector3d(
            currentState.ProjectBasePoint.XFeet * feetToMeters,
            currentState.ProjectBasePoint.YFeet * feetToMeters,
            currentState.ProjectBasePoint.ZFeet * feetToMeters);
        Vector3d surveyPointMeters = new Vector3d(
            currentState.SurveyPoint.XFeet * feetToMeters,
            currentState.SurveyPoint.YFeet * feetToMeters,
            currentState.SurveyPoint.ZFeet * feetToMeters);

        PlateauOnlineImportWindow window = new PlateauOnlineImportWindow(
            viewModel,
            ecefTransformerFactory: () => CreateOnlineEcefTransformer(coordinateTransformer, referenceContext),
            dracoDecoderFactory: () => NativeDracoMeshDecoder.IsAvailable()
                ? new NativeDracoMeshDecoder()
                : (IDracoMeshDecoder)new MissingDracoMeshDecoder(),
            withTransaction: configure => RunImportTransaction(document, configure),
            projectBasePointMeters: projectBasePointMeters,
            surveyPointMeters: surveyPointMeters);

        new WindowInteropHelper(window).Owner = uiApplication.MainWindowHandle;
        window.ShowDialog();
        return Result.Succeeded;
    }

    private static EcefToProjectTransformer CreateOnlineEcefTransformer(
        CoordinateTransformer coordinateTransformer,
        PlateauImportReferenceContext referenceContext)
    {
        return new EcefToProjectTransformer(
            coordinateTransformer,
            referenceContext.ProjectCrs,
            referenceContext.AnchorProjectedCoordinate,
            referenceContext.AnchorElevationMeters,
            referenceContext.AnchorXFeet,
            referenceContext.AnchorYFeet,
            referenceContext.AnchorZFeet,
            referenceContext.SharedEastToLocalX,
            referenceContext.SharedEastToLocalY,
            referenceContext.SharedNorthToLocalX,
            referenceContext.SharedNorthToLocalY);
    }

    private static PlateauTilesImporterResult RunImportTransaction(Document document, Action<PlateauOnlineImportContext> configure)
    {
        PlateauOnlineImportContext ctx = new PlateauOnlineImportContext();
        configure(ctx);
        if (ctx.Buildings is null)
        {
            return new PlateauTilesImporterResult(0, 0, new[] { "No buildings dataset was provided." });
        }

        using Transaction tx = new Transaction(document, "Import PLATEAU Online");
        tx.Start();
        try
        {
            PlateauTilesImporter importer = new PlateauTilesImporter();
            PlateauTilesImporterResult result = importer.Import(document, ctx.Buildings, ctx.Mode);
            tx.Commit();
            return result;
        }
        catch
        {
            tx.RollBack();
            throw;
        }
    }
}
