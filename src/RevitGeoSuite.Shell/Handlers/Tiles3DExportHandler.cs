using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using RevitGeoSuite.Tiles3DExport;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>
/// Shared wiring for the 3D Tiles export handlers. Resolves the export reference and scope from
/// the active document, reusing <see cref="Tiles3DExportCoordinator"/> exactly like the legacy WPF command.
/// </summary>
internal static class Tiles3DExportSupport
{
    public const Tiles3DExportReferenceSource ReferenceSource = Tiles3DExportReferenceSource.WorkingProjectBasePoint;

    public static (Tiles3DExportCoordinator Coordinator, Tiles3DExportReferenceContext Reference, Tiles3DExportScopeSelection Scope) Resolve(
        Document document,
        RevitGeoSuite.RevitInterop.RevitDocumentHandle handle,
        RevitGeoSuite.RevitInterop.GeoPlacement.CurrentProjectStateSummary currentState,
        RevitGeoSuite.Core.ProjectMetadata.GeoProjectInfo? info,
        string requestedScope,
        string? selectedViewUniqueId)
    {
        var transformer = new CoordinateTransformer(new CrsRegistry());
        var resolver = new Tiles3DExportReferenceResolver(transformer);
        Tiles3DExportReferenceContext reference = resolver.Resolve(currentState, info, ReferenceSource)
            ?? throw new InvalidOperationException(
                "3D Tiles export needs a saved CRS and origin. Run Georeference Setup and apply it before exporting.");

        var coordinator = new Tiles3DExportCoordinator(coordinateTransformer: transformer);
        Tiles3DExportScopeSelection scope = ResolveScope(document, requestedScope, selectedViewUniqueId);
        return (coordinator, reference, scope);
    }

    public static (bool PreciseCrs, double GeoidOffset, string Scope, string[] SelectedLinkUniqueIds, string? SelectedViewUniqueId, Tiles3DLevelOfDetail LevelOfDetail) ReadOptions(JObject? payload)
    {
        var options = payload?["options"] as JObject;
        bool preciseCrs = options?.Value<bool?>("preciseCrs") ?? false;
        double geoidOffset = options?.Value<double?>("geoidOffset") ?? 0d;
        string scope = payload?.Value<string>("scope") ?? "whole";
        string? selectedViewUniqueId = options?.Value<string>("selectedViewUniqueId");
        Tiles3DLevelOfDetail levelOfDetail = ParseLevelOfDetail(options?.Value<string>("lod"));
        string[] selectedLinkUniqueIds = options?["selectedLinkUniqueIds"] is JArray arr
            ? arr.Values<string>().Where(s => s != null).Cast<string>().ToArray()
            : Array.Empty<string>();
        return (preciseCrs, geoidOffset, scope, selectedLinkUniqueIds, selectedViewUniqueId, levelOfDetail);
    }

    public static void ApplySelectedLinks(Tiles3DExportScopeSelection scope, Document document, string[] selectedLinkUniqueIds)
    {
        if (selectedLinkUniqueIds.Length == 0)
        {
            return;
        }

        var selectedSet = new HashSet<string>(selectedLinkUniqueIds, StringComparer.Ordinal);
        scope.SelectedLinkedModels = Tiles3DExportDocumentCatalog.CreateLinkOptions(document)
            .Where(l => selectedSet.Contains(l.UniqueId))
            .ToArray();
    }

    public static string[] ScopeWarnings(string scope)
    {
        return string.Equals(scope, "selection", StringComparison.OrdinalIgnoreCase)
            ? new[] { "Selected-element 3D Tiles export is not supported yet. Export the whole model or a selected 3D view." }
            : Array.Empty<string>();
    }

    private static Tiles3DExportScopeSelection ResolveScope(Document document, string requestedScope, string? selectedViewUniqueId)
    {
        if (string.Equals(requestedScope, "whole", StringComparison.OrdinalIgnoreCase))
        {
            return new Tiles3DExportScopeSelection { ScopeMode = Tiles3DExportScopeMode.WholeModel };
        }

        if (string.Equals(requestedScope, "selection", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Selected-element 3D Tiles export is not supported yet. Choose Whole Model or Selected 3D View.");
        }

        if (!string.Equals(requestedScope, "view", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unsupported 3D Tiles export scope '{requestedScope}'. Choose Whole Model or Selected 3D View.");
        }

        Tiles3DExportViewOption[] views = Tiles3DExportDocumentCatalog.CreateViewOptions(document).ToArray();
        Tiles3DExportViewOption? selectedView = !string.IsNullOrWhiteSpace(selectedViewUniqueId)
            ? views.FirstOrDefault(view => string.Equals(view.UniqueId, selectedViewUniqueId, StringComparison.Ordinal))
            : views.FirstOrDefault();

        if (selectedView is null)
        {
            throw new InvalidOperationException("Selected 3D View export needs a non-template 3D view in the current Revit document.");
        }

        return new Tiles3DExportScopeSelection
        {
            ScopeMode = Tiles3DExportScopeMode.Selected3DView,
            SelectedView = selectedView
        };
    }

    private static Tiles3DLevelOfDetail ParseLevelOfDetail(string? value)
    {
        return (value ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "1" or "coarse" => Tiles3DLevelOfDetail.Coarse,
            "2" or "medium" => Tiles3DLevelOfDetail.Medium,
            "3" or "fine" or "" => Tiles3DLevelOfDetail.Fine,
            _ => throw new InvalidOperationException($"Unsupported 3D Tiles LOD '{value}'. Choose coarse, medium, or fine.")
        };
    }
}

/// <summary>
/// <c>tiles3d.exportOptions</c> — returns available linked models for the current document.
/// </summary>
public sealed class Tiles3DExportOptionsHandler : IRpcHandler
{
    public string Method => "tiles3d.exportOptions";

    public Task<object?> HandleAsync(object? payload)
    {
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            Tiles3DLinkOption[] links = Tiles3DExportDocumentCatalog.CreateLinkOptions(document)
                .Select(l => new Tiles3DLinkOption
                {
                    UniqueId = l.UniqueId,
                    Title = l.Title,
                    Description = l.Description
                })
                .ToArray();

            Tiles3DViewOption[] views = Tiles3DExportDocumentCatalog.CreateViewOptions(document)
                .Select(v => new Tiles3DViewOption
                {
                    ViewId = v.ViewId,
                    UniqueId = v.UniqueId,
                    Title = v.Title,
                    Description = v.Description
                })
                .ToArray();

            return new Tiles3DExportOptionsResponse
            {
                Links = links,
                Views = views,
                DefaultViewUniqueId = views.FirstOrDefault()?.UniqueId ?? string.Empty
            };
        });
    }
}

/// <summary>
/// <c>tiles3d.export.prepare</c> — dry-run preview (element/triangle counts + CRS) without writing files.
/// </summary>
public sealed class Tiles3DExportPrepareHandler : IRpcHandler
{
    public string Method => "tiles3d.export.prepare";

    public Task<object?> HandleAsync(object? payload)
    {
        (bool preciseCrs, double geoidOffset, string scope, string[] selectedLinkUniqueIds, string? selectedViewUniqueId, Tiles3DLevelOfDetail levelOfDetail) =
            Tiles3DExportSupport.ReadOptions(payload as JObject);

        // Geometry extraction touches the Revit API, so the preview runs on the API thread.
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            var (handle, currentState, info) = ExportHandlerSupport.ReadContext(document);
            var (coordinator, reference, scopeSelection) = Tiles3DExportSupport.Resolve(document, handle, currentState, info, scope, selectedViewUniqueId);
            Tiles3DExportSupport.ApplySelectedLinks(scopeSelection, document, selectedLinkUniqueIds);

            Tiles3DExportPreparationResult prep = coordinator.Prepare(handle, reference, scopeSelection, levelOfDetail, preciseCrs, geoidOffset);
            Tiles3DExportPackage package = prep.Package;

            return new Tiles3DExportPrepareResponse
            {
                ElementCount = package.ElementCount,
                TriangleCount = package.TriangleCount,
                Crs = $"EPSG:{package.ReferenceContext.ProjectCrs.EpsgCode}",
                Warnings = Tiles3DExportSupport.ScopeWarnings(scope)
            };
        });
    }
}

/// <summary>
/// <c>tiles3d.export</c> — runs the real 3D Tiles export as a background job (re-prepares then writes
/// the tileset bundle on the Revit API thread).
/// </summary>
public sealed class Tiles3DExportHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public Tiles3DExportHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "tiles3d.export";

    public Task<object?> HandleAsync(object? payload)
    {
        var jobj = payload as JObject;
        var outputFolder = jobj?.Value<string>("outputFolder");
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            return Task.FromResult<object?>(new { error = "Output folder is required" });
        }

        (bool preciseCrs, double geoidOffset, string scope, string[] selectedLinkUniqueIds, string? selectedViewUniqueId, Tiles3DLevelOfDetail levelOfDetail) =
            Tiles3DExportSupport.ReadOptions(jobj);

        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "preparing", Percent = 10, Message = "Extracting model geometry…" });
            ct.ThrowIfCancellationRequested();

            object? result = await RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
            {
                var (handle, currentState, info) = ExportHandlerSupport.ReadContext(document);
                var (coordinator, reference, scopeSelection) = Tiles3DExportSupport.Resolve(document, handle, currentState, info, scope, selectedViewUniqueId);
                Tiles3DExportSupport.ApplySelectedLinks(scopeSelection, document, selectedLinkUniqueIds);

                Tiles3DExportPreparationResult prep = coordinator.Prepare(handle, reference, scopeSelection, levelOfDetail, preciseCrs, geoidOffset);
                Tiles3DExportState? existingState = new Tiles3DExportStateService().Load(handle);
                Tiles3DExportResult exportResult = coordinator.Export(
                    handle, prep.Package, outputFolder!, Tiles3DExportSupport.ReferenceSource, scopeSelection, existingState);

                var warnings = new List<string>(Tiles3DExportSupport.ScopeWarnings(scope));
                if (!exportResult.StatePersisted)
                {
                    warnings.Add("The project is read-only, so the export state was not saved back into the document.");
                }

                return new Tiles3DExportResponse
                {
                    ExportedElements = prep.Package.ElementCount,
                    TriangleCount = prep.Package.TriangleCount,
                    Files = 2,
                    Summary = exportResult.SummaryMessage,
                    TilesetPath = exportResult.TilesetPath,
                    Warnings = warnings.ToArray()
                };
            }).ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Export complete" });
            return result;
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }
}
