using RevitGeoSuite.Shell.Handlers;
using RevitGeoSuite.SharedUI.Shell;

namespace RevitGeoSuite.Shell;

/// <summary>
/// Registers every route's RPC handlers on the shared shell window so rail navigation between
/// GEO/IMP/EXP works regardless of which ribbon command opened the window. Window/localization/job
/// handlers are already registered by <see cref="WebShellWindow"/> itself. Revit-touching handlers
/// resolve the active document via <see cref="RevitContext"/> at execution time.
/// </summary>
internal static class WebShellHandlerRegistry
{
    public static void RegisterAll(WebRpcBridge bridge, JobManager jobs)
    {
        bridge.RegisterHandler(new EchoHandler());

        // Georeference route
        bridge.RegisterHandler(new MeshGetOverlayHandler());
        bridge.RegisterHandler(new ReadinessGetStatusHandler());
        bridge.RegisterHandler(new GeoreferenceGetCurrentStateHandler());
        bridge.RegisterHandler(new GeoreferenceGetCrsOriginHandler());
        bridge.RegisterHandler(new GeoreferenceGetGridCandidatesHandler());
        bridge.RegisterHandler(new GeoreferenceResolveGridBasePointHandler());
        bridge.RegisterHandler(new GeoreferenceApplyHandler());

        // Import route
        bridge.RegisterHandler(new DialogOpenFolderHandler());
        bridge.RegisterHandler(new PlateauScanFolderHandler(jobs));
        bridge.RegisterHandler(new PlateauImportTilesHandler(jobs));
        bridge.RegisterHandler(new PlateauOnlineCatalogHandler(jobs));
        bridge.RegisterHandler(new PlateauOnlineGridsHandler(jobs));
        bridge.RegisterHandler(new PlateauOnlineImportHandler(jobs));
        bridge.RegisterHandler(new PlateauCkanCatalogHandler(jobs));
        bridge.RegisterHandler(new PlateauCkanResourcesHandler());
        bridge.RegisterHandler(new PlateauCkanDownloadCheckHandler());
        bridge.RegisterHandler(new PlateauCkanDownloadHandler(jobs));

        // Export route
        bridge.RegisterHandler(new PlateauExportContextPrepareHandler(jobs));
        bridge.RegisterHandler(new PlateauExportContextHandler(jobs));
        bridge.RegisterHandler(new Tiles3DExportPrepareHandler());
        bridge.RegisterHandler(new Tiles3DExportHandler(jobs));
        bridge.RegisterHandler(new CityGmlExportPrepareHandler());
        bridge.RegisterHandler(new CityGmlExportHandler(jobs));
    }
}
