using System;
using RevitGeoSuite.SharedUI.Web.Contracts;

// Typed RPC registry. The contracts generator turns these into `RpcMethods` / `RpcEvents` TS maps so
// the frontend's request()/on() are typed by method name. Add an entry when a method/event gets a
// typed contract; methods without an entry still work through the loose request()/on() fallback.
[assembly: RpcMethod("window.navigate", typeof(NavigateRequest), typeof(void))]
[assembly: RpcMethod("job.cancel", typeof(JobCancelRequest), typeof(JobCancelResponse))]
[assembly: RpcMethod("dialog.openFolder", typeof(DialogOpenFolderRequest), typeof(DialogOpenFolderResponse))]
[assembly: RpcMethod("dialog.openFile", typeof(DialogOpenFileRequest), typeof(DialogOpenFileResponse))]
[assembly: RpcMethod("readiness.getStatus", typeof(ReadinessStatusRequest), typeof(ReadinessStatusResponse))]
[assembly: RpcMethod("mesh.getOverlay", typeof(MeshOverlayRequest), typeof(MeshOverlayResponse))]
[assembly: RpcMethod("plateau.scanFolder", typeof(PlateauScanRequest), typeof(JobStarted))]
[assembly: RpcMethod("plateau.importTiles", typeof(PlateauImportRequest), typeof(JobStarted))]
[assembly: RpcMethod("gis.exportOptions", typeof(GisExportOptionsRequest), typeof(GisExportOptionsResponse))]
[assembly: RpcMethod("gis.export", typeof(GisExportRequest), typeof(JobStarted))]
[assembly: RpcMethod("plateau.areaLocation", typeof(PlateauAreaLocationRequest), typeof(PlateauAreaLocationResponse))]
[assembly: RpcMethod("plateau.onlineCatalog", typeof(PlateauOnlineCatalogRequest), typeof(JobStarted))]
[assembly: RpcMethod("plateau.onlineGrids", typeof(PlateauOnlineGridsRequest), typeof(JobStarted))]
[assembly: RpcMethod("plateau.onlineImport", typeof(PlateauOnlineImportRequest), typeof(JobStarted))]
[assembly: RpcMethod("osm.importBuildings", typeof(OsmBuildingsImportRequest), typeof(JobStarted))]
[assembly: RpcMethod("plateau.exportContext.prepare", typeof(PlateauContextExportRequest), typeof(JobStarted))]
[assembly: RpcMethod("plateau.exportContext", typeof(PlateauContextExportRequest), typeof(JobStarted))]
[assembly: RpcMethod("tiles3d.exportOptions", typeof(Tiles3DExportOptionsRequest), typeof(Tiles3DExportOptionsResponse))]
[assembly: RpcMethod("tiles3d.export.prepare", typeof(Tiles3DExportPrepareRequest), typeof(Tiles3DExportPrepareResponse))]
[assembly: RpcMethod("tiles3d.export", typeof(Tiles3DExportRequest), typeof(JobStarted))]
[assembly: RpcMethod("citygml.export.prepare", typeof(CityGmlExportPrepareRequest), typeof(CityGmlExportPrepareResponse))]
[assembly: RpcMethod("citygml.export", typeof(CityGmlExportRequest), typeof(JobStarted))]
[assembly: RpcMethod("floorplan.getInitialState", typeof(ExporterInitialStateRequest), typeof(ExporterInitialStateResponse))]
[assembly: RpcMethod("floorplan.preparePreview", typeof(ExporterSettingsPayload), typeof(PreviewInitialStateResponse))]
[assembly: RpcMethod("floorplan.run", typeof(ExporterSettingsPayload), typeof(ExporterRunResponse))]
[assembly: RpcMethod("floorplan.saveProfile", typeof(ExporterSaveProfileRequest), typeof(ExporterInitialStateResponse))]
[assembly: RpcMethod("floorplan.deleteProfile", typeof(ExporterDeleteProfileRequest), typeof(ExporterInitialStateResponse))]
[assembly: RpcMethod("floorplan.cancel", typeof(ExporterEmptyRequest), typeof(void))]
[assembly: RpcMethod("floorplan.preview.getInitialState", typeof(PreviewInitialStateRequest), typeof(PreviewInitialStateResponse))]
[assembly: RpcMethod("floorplan.preview.loadView", typeof(PreviewLoadViewRequest), typeof(PreviewViewPayload))]
[assembly: RpcMethod("floorplan.preview.assignCategory", typeof(PreviewAssignmentRequest), typeof(PreviewViewPayload))]
[assembly: RpcMethod("floorplan.preview.clearAssignment", typeof(PreviewClearAssignmentRequest), typeof(PreviewViewPayload))]
[assembly: RpcMethod("floorplan.preview.saveAssignments", typeof(ExporterEmptyRequest), typeof(PreviewViewPayload))]
[assembly: RpcMethod("floorplan.preview.discardAssignments", typeof(ExporterEmptyRequest), typeof(PreviewViewPayload))]
[assembly: RpcMethod("floorplan.execution.progress.getInitialState", typeof(ExecutionProgressInitialStateRequest), typeof(ExecutionProgressInitialStateResponse))]
[assembly: RpcMethod("floorplan.execution.result.getInitialState", typeof(ExportResultInitialStateRequest), typeof(ExportResultInitialStateResponse))]
[assembly: RpcMethod("floorplan.execution.result.openOutputFolder", typeof(ExporterEmptyRequest), typeof(ExecutionActionResponse))]
[assembly: RpcMethod("georeference.getAvailableCrs", typeof(AvailableCrsRequest), typeof(AvailableCrsResponse))]
[assembly: RpcMethod("georeference.getCrsOrigin", typeof(GeoreferenceCrsOriginRequest), typeof(GeoreferencePoint))]
[assembly: RpcMethod("georeference.getGridCandidates", typeof(GeoreferenceGridCandidatesRequest), typeof(GeoreferenceGridCandidatesResponse))]
[assembly: RpcMethod("georeference.resolveGridBasePoint", typeof(GeoreferenceResolveBasePointRequest), typeof(GeoreferenceBasePointResponse))]
[assembly: RpcMethod("georeference.getCurrentState", typeof(GeoreferenceCurrentStateRequest), typeof(GeoreferenceCurrentStateResponse))]
[assembly: RpcMethod("georeference.apply", typeof(GeoreferenceApplyRequest), typeof(GeoreferenceApplyResponse))]
[assembly: RpcMethod("georeference.revert", typeof(GeoreferenceRevertRequest), typeof(GeoreferenceRevertResponse))]
[assembly: RpcMethod("georeference.hasUndoSnapshot", typeof(GeoreferenceRevertRequest), typeof(GeoreferenceHasUndoResponse))]
[assembly: RpcMethod("localization.getAll", typeof(LocalizationGetAllRequest), typeof(LocalizationStrings))]
[assembly: RpcMethod("localization.setLanguage", typeof(LocalizationSetLanguageRequest), typeof(LocalizationSetLanguageResponse))]

[assembly: RpcEvent("navigate", typeof(NavigateEvent))]
[assembly: RpcEvent("job.progress", typeof(JobProgress))]
[assembly: RpcEvent("job.completed", typeof(JobCompleted))]
[assembly: RpcEvent("job.failed", typeof(JobFailed))]
[assembly: RpcEvent("floorplan.execution.progress.updated", typeof(ExecutionProgressPayload))]

namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Maps an RPC method name to its request/response contract types (assembly-level).</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RpcMethodAttribute : Attribute
{
    public RpcMethodAttribute(string method, Type requestType, Type responseType)
    {
        Method = method;
        RequestType = requestType;
        ResponseType = responseType;
    }

    public string Method { get; }
    public Type RequestType { get; }
    public Type ResponseType { get; }
}

/// <summary>Maps an RPC event name to its payload contract type (assembly-level).</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class RpcEventAttribute : Attribute
{
    public RpcEventAttribute(string method, Type payloadType)
    {
        Method = method;
        PayloadType = payloadType;
    }

    public string Method { get; }
    public Type PayloadType { get; }
}
