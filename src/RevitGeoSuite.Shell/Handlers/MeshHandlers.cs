using System;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

public sealed class MeshGetOverlayHandler : IRpcHandler
{
    public string Method => "mesh.getOverlay";

    public Task<object?> HandleAsync(object? payload)
    {
        // Runs on the Revit API thread against the *current* active document (never a stale capture).
        return RevitContext.Instance.InvokeAsync<object?>(app =>
        {
            Document? document = app.ActiveUIDocument?.Document;
            try
            {
                var geoProjectInfoStore = new GeoProjectInfoStorage();
                var projectLocationReader = new ProjectLocationReader(geoProjectInfoStore, moduleStateStore: new ModuleStateStorage());
                var currentState = projectLocationReader.Read(document);

                var documentHandle = document is null ? null : new RevitDocumentHandle(document);
                var info = documentHandle is null ? null : geoProjectInfoStore.Load(documentHandle);

                var meshCalculator = new JapanMeshCalculator();
                var meshService = new MeshInspector.MeshInspectorService(meshCalculator);
                var summary = meshService.BuildSummary(currentState, info);

                return new MeshOverlayResponse
                {
                    PrimaryMeshCode = summary.PrimaryMeshCode,
                    NeighborMeshCodes = summary.NeighborMeshCodes?.ToArray() ?? Array.Empty<string>(),
                    OverlayGeoJson = summary.OverlayGeoJson,
                    CenterLatitude = summary.CenterLatitude,
                    CenterLongitude = summary.CenterLongitude,
                    StatusMessage = summary.StatusMessage
                };
            }
            catch (Exception ex)
            {
                return new MeshOverlayResponse
                {
                    Error = ex.Message,
                    NeighborMeshCodes = Array.Empty<string>(),
                    StatusMessage = "Failed to calculate mesh overlay"
                };
            }
        });
    }
}
