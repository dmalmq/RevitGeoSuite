using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.CityGmlExport;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>
/// Shared wiring for the CityGML export handlers. CityGML extraction is view-scoped, so the web shell
/// uses the active non-template 3D view (falling back to the first available one). Linked models are
/// not surfaced yet (host model only).
/// </summary>
internal static class CityGmlExportSupport
{
    public const CityGmlExportReferenceSource ReferenceSource = CityGmlExportReferenceSource.WorkingProjectBasePoint;

    public static (CityGmlExportCoordinator Coordinator, CityGmlExportReferenceContext Reference, CityGmlExportScopeSelection Scope) Resolve(
        Document document, CurrentProjectStateSummary currentState, GeoProjectInfo? info)
    {
        var transformer = new CoordinateTransformer(new CrsRegistry());
        var resolver = new CityGmlExportReferenceResolver(transformer);
        CityGmlExportReferenceContext reference = resolver.Resolve(currentState, info, ReferenceSource)
            ?? throw new InvalidOperationException(
                "CityGML export needs a saved CRS and origin. Run Georeference Setup and apply it before exporting.");

        View3D view = ResolveExport3DView(document)
            ?? throw new InvalidOperationException(
                "CityGML export needs a 3D view. Open a non-template 3D view in Revit, then try again.");

        var scope = new CityGmlExportScopeSelection
        {
            SelectedView = new CityGmlExportViewOption
            {
                ViewId = view.Id,
                UniqueId = view.UniqueId,
                Title = view.Name
            }
        };

        return (new CityGmlExportCoordinator(), reference, scope);
    }

    public static (string SchemaVersion, Dictionary<string, string>? CategoryOverrides, Dictionary<string, string>? CodelistOverrides) ReadOptions(JObject? payload)
    {
        var options = payload?["options"] as JObject;
        string schemaVersion = NormalizeSchemaVersion(options?.Value<string>("schemaVersion"));
        var categoryOverrides = (options?["categoryOverrides"] as JObject)?.ToObject<Dictionary<string, string>>();
        var codelistOverrides = (options?["codelistOverrides"] as JObject)?.ToObject<Dictionary<string, string>>();
        return (schemaVersion, categoryOverrides, codelistOverrides);
    }

    private static string NormalizeSchemaVersion(string? schemaVersion)
    {
        string value = (schemaVersion ?? "2.0").Trim();
        if (string.Equals(value, "2.0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, CityGmlExportProfile.LightweightCityGml20, StringComparison.OrdinalIgnoreCase))
        {
            return CityGmlExportProfile.LightweightCityGml20;
        }

        throw new InvalidOperationException($"Unsupported CityGML schema version '{value}'. This exporter currently writes CityGML 2.0 Lightweight.");
    }

    private static View3D? ResolveExport3DView(Document document)
    {
        if (document.ActiveView is View3D active && !active.IsTemplate)
        {
            return active;
        }

        return new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .FirstOrDefault(view => !view.IsTemplate);
    }
}

/// <summary><c>citygml.export.prepare</c> — dry-run preview (feature count + CRS + validation messages).</summary>
public sealed class CityGmlExportPrepareHandler : IRpcHandler
{
    public string Method => "citygml.export.prepare";

    public Task<object?> HandleAsync(object? payload)
    {
        (string schemaVersion, Dictionary<string, string>? categoryOverrides, Dictionary<string, string>? codelistOverrides) =
            CityGmlExportSupport.ReadOptions(payload as JObject);

        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            var (handle, currentState, info) = ExportHandlerSupport.ReadContext(document);
            var (coordinator, reference, scope) = CityGmlExportSupport.Resolve(document, currentState, info);

            CityGmlExportPreparationResult prep = coordinator.Prepare(handle, reference, scope, schemaVersion, categoryOverrides, codelistOverrides);
            CityGmlExportPackage package = prep.Package;

            return new CityGmlExportPrepareResponse
            {
                FeatureCount = package.Features.Count,
                Crs = $"EPSG:{package.ReferenceContext.ProjectCrs.EpsgCode}",
                Warnings = prep.ValidationMessages?.ToArray() ?? Array.Empty<string>()
            };
        });
    }
}

/// <summary><c>citygml.export</c> — runs the real CityGML export as a background job.</summary>
public sealed class CityGmlExportHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public CityGmlExportHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "citygml.export";

    public Task<object?> HandleAsync(object? payload)
    {
        var jobj = payload as JObject;
        var outputFolder = jobj?.Value<string>("outputFolder");
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            return Task.FromResult<object?>(new { error = "Output folder is required" });
        }

        (string schemaVersion, Dictionary<string, string>? categoryOverrides, Dictionary<string, string>? codelistOverrides) =
            CityGmlExportSupport.ReadOptions(jobj);

        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "preparing", Percent = 10, Message = "Extracting CityGML features…" });
            ct.ThrowIfCancellationRequested();

            object? result = await RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
            {
                var (handle, currentState, info) = ExportHandlerSupport.ReadContext(document);
                var (coordinator, reference, scope) = CityGmlExportSupport.Resolve(document, currentState, info);

                CityGmlExportPreparationResult prep = coordinator.Prepare(handle, reference, scope, schemaVersion, categoryOverrides, codelistOverrides);
                CityGmlExportState? existingState = new CityGmlExportStateService().Load(handle);
                CityGmlExportResult exportResult = coordinator.Export(
                    handle, prep.Package, outputFolder!, CityGmlExportSupport.ReferenceSource, scope, existingState, categoryOverrides, codelistOverrides);

                var warnings = new List<string>(prep.ValidationMessages ?? Array.Empty<string>());
                if (!exportResult.StatePersisted)
                {
                    warnings.Add("The project is read-only, so the export state was not saved back into the document.");
                }

                return new CityGmlExportResponse
                {
                    ExportedElements = prep.Package.Features.Count,
                    FeatureCount = prep.Package.Features.Count,
                    Files = 1,
                    Summary = exportResult.SummaryMessage,
                    ExportPath = exportResult.ExportPath,
                    Warnings = warnings.ToArray()
                };
            }).ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Export complete" });
            return result;
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }
}
