using System;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Validation;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using RevitGeoSuite.Validation;

namespace RevitGeoSuite.Shell.Handlers;

public sealed class ReadinessGetStatusHandler : IRpcHandler
{
    public string Method => "readiness.getStatus";

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

                var crsRegistry = new CrsRegistry();
                var coordinateTransformer = new CoordinateTransformer(crsRegistry);
                var coordinateValidator = new CoordinateValidator(crsRegistry, coordinateTransformer, new JapanMeshCalculator());
                var projectHealthChecker = new ProjectHealthChecker(coordinateValidator);

                var summary = projectHealthChecker.BuildSummary(currentState, info);

                return new ReadinessStatusResponse
                {
                    Status = summary.StatusTitle,
                    StatusMessage = summary.StatusMessage,
                    Findings = summary.Findings.Select(f => new ReadinessFinding
                    {
                        Code = f.Code,
                        Severity = f.Severity.ToString().ToLower(),
                        Message = f.Message
                    }).ToArray(),
                    ExportReadiness = new ReadinessExportSummary
                    {
                        Status = summary.ExportReadiness.Status.ToString().ToLower(),
                        StatusTitle = summary.ExportReadiness.StatusTitle,
                        StatusMessage = summary.ExportReadiness.StatusMessage,
                        Items = summary.ExportReadiness.Items.Select(item => new ReadinessExportItem
                        {
                            Title = item.Title,
                            IsSatisfied = item.IsSatisfied,
                            Detail = item.Detail,
                            StatusText = item.StatusText
                        }).ToArray()
                    }
                };
            }
            catch (Exception ex)
            {
                return new ReadinessStatusResponse
                {
                    Error = ex.Message,
                    Status = "Unknown",
                    StatusMessage = "Failed to check readiness",
                    Findings = Array.Empty<ReadinessFinding>(),
                    ExportReadiness = new ReadinessExportSummary
                    {
                        Status = "blocked",
                        StatusTitle = "Blocked",
                        StatusMessage = "Failed to check readiness",
                        Items = Array.Empty<ReadinessExportItem>()
                    }
                };
            }
        });
    }
}
