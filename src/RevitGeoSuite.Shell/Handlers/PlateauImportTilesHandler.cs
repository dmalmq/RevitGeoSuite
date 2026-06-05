using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

public sealed class PlateauImportTilesHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public PlateauImportTilesHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.importTiles";

    public Task<object?> HandleAsync(object? payload)
    {
        var jobj = payload as JObject;
        var path = jobj?.Value<string>("path");
        var tileIds = jobj?.Value<JArray>("tileIds")?.Select(t => t.ToString()).ToList();
        // "solids" (default) builds 3D DirectShapes; "dxf" imports a lightweight 2D CAD basemap.
        var mode = jobj?.Value<string>("mode");

        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<object?>(new { error = "Path is required" });
        }
        if (tileIds == null || tileIds.Count == 0)
        {
            return Task.FromResult<object?>(new { error = "At least one tile ID is required" });
        }

        string jobId = jobs.Start(async (ct, progress) =>
        {
            var transformer = new CoordinateTransformer(new CrsRegistry());

            // 1) Resolve the georeference anchor + existing import state on the Revit API thread.
            progress.Report(new JobProgress { Phase = "preparing", Percent = 0, Message = "Resolving georeference…" });
            ImportPrep prep = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
            {
                var geoStore = new GeoProjectInfoStorage();
                var moduleStateStore = new ModuleStateStorage();
                var reader = new ProjectLocationReader(geoStore, moduleStateStore: moduleStateStore);
                var currentState = reader.Read(doc);
                var handle = new RevitDocumentHandle(doc);
                var info = geoStore.Load(handle);
                var resolver = new PlateauImportReferenceResolver(transformer, new RevitPlateauImportLocalBasisProvider(doc));
                var referenceContext = resolver.Resolve(currentState, info, PlateauImportReferenceSource.WorkingProjectBasePoint);
                var existingState = new PlateauImportStateService(moduleStateStore).Load(handle);
                return new ImportPrep(referenceContext, existingState);
            }).ConfigureAwait(false);

            if (prep.ReferenceContext is null)
            {
                throw new InvalidOperationException(
                    "This project isn't georeferenced yet. Complete Georeference Setup (CRS + Project Base Point) before importing PLATEAU context.");
            }

            ct.ThrowIfCancellationRequested();

            // 2) Scan + build geometry off the UI thread (pure parsing, no Revit API).
            progress.Report(new JobProgress { Phase = "scanning", Percent = 10, Message = "Scanning folder…" });
            var scanService = new PlateauFolderScanService(new CityGmlParser());
            var scanResult = scanService.ScanFolder(path!, p =>
            {
                ct.ThrowIfCancellationRequested();
                progress.Report(new JobProgress
                {
                    Phase = "scanning",
                    Current = p.Current,
                    Total = p.Total,
                    Percent = 10 + (int)Math.Round(p.Percent * 0.5), // scan occupies 10–60%
                    Message = p.CurrentFileName
                });
            });

            ct.ThrowIfCancellationRequested();
            progress.Report(new JobProgress { Phase = "building", Percent = 65, Message = "Building geometry…" });
            ContextImportPlan plan = new ContextGeometryBuilder(transformer).BuildPlan(
                scanResult,
                prep.ReferenceContext,
                selectedFeatureTypes: null,
                selectedTileIds: tileIds,
                PlateauGeometryImportMode.LightweightExtrusion);

            ct.ThrowIfCancellationRequested();

            // 3a) Lightweight path: render the same outlines the export uses and import them as a
            // single flat 2D CAD basemap instead of 3D solids.
            if (string.Equals(mode, "dxf", StringComparison.OrdinalIgnoreCase))
            {
                return await ImportAsDxfBasemapAsync(plan, tileIds!, progress, ct).ConfigureAwait(false);
            }

            // 3) Create the Revit geometry inside a transaction on the Revit API thread.
            progress.Report(new JobProgress { Phase = "importing", Percent = 80, Message = "Creating Revit geometry…" });
            PlateauImportResult result = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
            {
                var handle = new RevitDocumentHandle(doc);
                var coordinator = new PlateauImportCoordinator(
                    new PlateauContextImporter(),
                    new PlateauImportStateService(new ModuleStateStorage()));
                return coordinator.Import(handle, plan, PlateauImportReferenceSource.WorkingProjectBasePoint, prep.ExistingState);
            }).ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Import complete" });

            return (object?)new
            {
                tilesImported = tileIds.Count,
                importedElements = result.ImportedElementCount,
                groups = result.CreatedGroupCount,
                summary = result.SummaryMessage,
                warnings = result.WarningMessages
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    /// <summary>
    /// Writes the model-frame DXF for <paramref name="plan"/> off-thread, then imports it on the
    /// Revit API thread as one 2D CAD basemap. The temp DXF is embedded by <c>Document.Import</c>,
    /// so it is deleted afterwards regardless of outcome.
    /// </summary>
    private static async Task<object?> ImportAsDxfBasemapAsync(
        ContextImportPlan plan,
        IReadOnlyList<string> tileIds,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        var importer = new PlateauContextDxfImporter();

        progress.Report(new JobProgress { Phase = "importing", Percent = 80, Message = "Building 2D outlines…" });
        string tempFolder = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", "Basemap_" + Guid.NewGuid().ToString("N"));
        string dxfPath = Path.Combine(tempFolder, "PLATEAU Basemap.dxf");
        bool imported = false;

        try
        {
            PlateauContextDxfImporter.DxfBuildResult build = importer.WriteModelDxf(plan, dxfPath);
            ct.ThrowIfCancellationRequested();

            if (build.FeatureCount == 0)
            {
                throw new InvalidOperationException(
                    build.Warnings.FirstOrDefault()
                    ?? "The selected PLATEAU tiles produced no 2D outlines to import.");
            }

            // ImportDxf opens its own transaction when the document is not already modifiable.
            progress.Report(new JobProgress { Phase = "importing", Percent = 90, Message = "Importing DXF basemap…" });
            await RevitContext.Instance.InvokeWithDocumentAsync(doc => importer.ImportDxf(doc, dxfPath)).ConfigureAwait(false);
            imported = true;

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Import complete" });

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Imported {0} PLATEAU outline(s) as a single 2D DXF basemap from {1} tile(s).",
                build.FeatureCount,
                tileIds.Count);

            return (object?)new
            {
                tilesImported = tileIds.Count,
                importedElements = build.FeatureCount,
                groups = 0,
                mode = "dxf",
                summary,
                warnings = build.Warnings
            };
        }
        finally
        {
            if (imported)
            {
                TryDeleteTempFolder(tempFolder);
            }
        }
    }

    private static void TryDeleteTempFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; a leftover temp DXF is harmless (the geometry is embedded in the model).
        }
    }

    private sealed class ImportPrep
    {
        public ImportPrep(PlateauImportReferenceContext? referenceContext, PlateauImportState? existingState)
        {
            ReferenceContext = referenceContext;
            ExistingState = existingState;
        }

        public PlateauImportReferenceContext? ReferenceContext { get; }

        public PlateauImportState? ExistingState { get; }
    }
}
