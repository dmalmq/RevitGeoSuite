using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
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

            // 2) Scan + build geometry off the UI thread (pure parsing, no Revit API). Only the
            //    selected tiles' files are parsed, so parse memory scales with the selection instead
            //    of the whole municipality — parsing everything is what crashed large 3D imports.
            progress.Report(new JobProgress { Phase = "scanning", Percent = 10, Message = "Scanning folder…" });
            var scanService = new PlateauFolderScanService(new CityGmlParser());
            var scanResult = scanService.ScanFolder(
                path!,
                p =>
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
                },
                selectedTileIds: tileIds);

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

            // 3) Create the Revit geometry on the Revit API thread. The coordinator imports one tile
            //    per transaction (inside a TransactionGroup) so Revit can release memory between tiles
            //    instead of crashing on large selections; report each committed tile as progress.
            progress.Report(new JobProgress { Phase = "importing", Percent = 80, Message = "Creating Revit geometry…" });
            PlateauImportResult result = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
            {
                var handle = new RevitDocumentHandle(doc);
                var coordinator = new PlateauImportCoordinator(
                    new PlateauContextImporter(),
                    new PlateauImportStateService(new ModuleStateStorage()));
                return coordinator.Import(
                    handle,
                    plan,
                    PlateauImportReferenceSource.WorkingProjectBasePoint,
                    prep.ExistingState,
                    onProgress: (current, total, message) => progress.Report(new JobProgress
                    {
                        Phase = "importing",
                        Current = current,
                        Total = total,
                        Percent = 80 + (int)Math.Round(current / (double)Math.Max(1, total) * 20), // 80–100%
                        Message = message
                    }),
                    ct: ct);
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
    /// Imports the PLATEAU 2D basemap one grid (tile) at a time: a small per-grid DXF is written
    /// off-thread, then each is imported as its own CAD instance on the Revit API thread. Splitting
    /// the work keeps each <c>Document.Import</c> payload small and lets Revit release transient
    /// regeneration memory between grids, which avoids the crash a single combined DXF caused on
    /// large areas. The per-grid instances are grouped at the end when Revit permits; imported CAD
    /// usually cannot be grouped, so that step falls back to leaving them as named instances.
    /// </summary>
    private static async Task<object?> ImportAsDxfBasemapAsync(
        ContextImportPlan plan,
        IReadOnlyList<string> tileIds,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        var importer = new PlateauContextDxfImporter();
        var warnings = new List<string>();

        progress.Report(new JobProgress { Phase = "importing", Percent = 80, Message = "Building 2D outlines…" });
        string tempFolder = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", "Basemap_" + Guid.NewGuid().ToString("N"));

        try
        {
            // 1) Split the merged plan into one plan per grid and write a small DXF for each
            //    (pure file IO — stays off the Revit UI thread). Skip grids that produce nothing.
            List<ContextImportPlan> perGridPlans = SplitPlanByTile(plan);
            var prepared = new List<PreparedGridDxf>();
            int totalFeatures = 0;
            for (int i = 0; i < perGridPlans.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                ContextImportPlan gridPlan = perGridPlans[i];
                string tileId = gridPlan.SelectedTileIds.FirstOrDefault() ?? $"grid {i + 1}";
                string dxfPath = Path.Combine(tempFolder, $"PLATEAU {SanitizeFileName(tileId)}.dxf");

                PlateauContextDxfImporter.DxfBuildResult build = importer.WriteModelDxf(gridPlan, dxfPath);
                warnings.AddRange(build.Warnings);

                if (build.FeatureCount == 0)
                {
                    continue;
                }

                prepared.Add(new PreparedGridDxf(tileId, dxfPath, build.FeatureCount));
                totalFeatures += build.FeatureCount;

                progress.Report(new JobProgress
                {
                    Phase = "importing",
                    Current = i + 1,
                    Total = perGridPlans.Count,
                    Percent = 80 + (int)Math.Round((i + 1) / (double)Math.Max(1, perGridPlans.Count) * 10), // 80–90%
                    Message = $"Building 2D outlines… ({i + 1}/{perGridPlans.Count})"
                });
            }

            ct.ThrowIfCancellationRequested();

            if (prepared.Count == 0)
            {
                throw new InvalidOperationException(
                    warnings.FirstOrDefault()
                    ?? "The selected PLATEAU tiles produced no 2D outlines to import.");
            }

            // 2) Import each grid in its own transaction on the Revit thread, then group them.
            //    One thread hop keeps file IO off the UI thread; per-grid transactions inside a
            //    TransactionGroup give incremental commits plus a single undo step.
            DxfImportOutcome outcome = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
                ImportPreparedGrids(doc, importer, prepared, progress, ct)).ConfigureAwait(false);

            warnings.AddRange(outcome.Warnings);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Import complete" });

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                outcome.GroupCreated
                    ? "Imported {0} PLATEAU grid(s) as 2D DXF basemaps, grouped into one basemap group ({1} outlines)."
                    : "Imported {0} PLATEAU grid(s) as separate 2D DXF basemap instances ({1} outlines).",
                outcome.ImportedInstanceCount,
                totalFeatures);

            return (object?)new
            {
                tilesImported = outcome.ImportedInstanceCount,
                importedElements = totalFeatures,
                groups = outcome.GroupCreated ? 1 : 0,
                mode = "dxf",
                summary,
                warnings
            };
        }
        finally
        {
            TryDeleteTempFolder(tempFolder);
        }
    }

    /// <summary>
    /// Returns one shallow-copied plan per distinct <see cref="ContextShapePlan.TileId"/>, each
    /// carrying only that tile's shapes. Ordered by tile id for predictable layering.
    /// </summary>
    private static List<ContextImportPlan> SplitPlanByTile(ContextImportPlan plan)
    {
        return plan.Shapes
            .GroupBy(shape => shape.TileId, StringComparer.Ordinal)
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new ContextImportPlan
            {
                SourceFolderPath = plan.SourceFolderPath,
                ReferenceContext = plan.ReferenceContext,
                GeometryImportMode = plan.GeometryImportMode,
                SourceModels = plan.SourceModels,
                SelectedFeatureTypes = plan.SelectedFeatureTypes,
                SelectedTileIds = new[] { group.Key },
                Shapes = group.ToArray(),
                WarningMessages = Array.Empty<string>()
            })
            .ToList();
    }

    /// <summary>
    /// Imports each prepared grid DXF as its own CAD instance (one transaction each, via
    /// <see cref="PlateauContextDxfImporter.ImportDxf"/>), then best-effort groups the instances.
    /// Runs entirely on the Revit API thread.
    /// </summary>
    private static DxfImportOutcome ImportPreparedGrids(
        Document doc,
        PlateauContextDxfImporter importer,
        IReadOnlyList<PreparedGridDxf> prepared,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        var warnings = new List<string>();
        var importedIds = new List<ElementId>();

        using TransactionGroup transactionGroup = new TransactionGroup(doc, "Import PLATEAU 2D Basemap");
        transactionGroup.Start();

        for (int i = 0; i < prepared.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            PreparedGridDxf grid = prepared[i];
            try
            {
                // ImportDxf opens its own transaction because the doc isn't modifiable here.
                importedIds.Add(importer.ImportDxf(doc, grid.DxfPath));
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped PLATEAU grid {grid.TileId}: {ex.Message}");
            }

            progress.Report(new JobProgress
            {
                Phase = "importing",
                Current = i + 1,
                Total = prepared.Count,
                Percent = 90 + (int)Math.Round((i + 1) / (double)Math.Max(1, prepared.Count) * 10), // 90–100%
                Message = $"Importing grid {grid.TileId} ({i + 1}/{prepared.Count})…"
            });
        }

        if (importedIds.Count == 0)
        {
            transactionGroup.RollBack();
            throw new InvalidOperationException("None of the PLATEAU grids could be imported. Review the warnings list.");
        }

        bool grouped = importedIds.Count >= 2 && TryGroupBasemap(doc, importedIds, warnings);

        transactionGroup.Assimilate();
        return new DxfImportOutcome(importedIds.Count, grouped, warnings);
    }

    /// <summary>
    /// Attempts to put the imported basemap instances into a single named model group. Imported CAD
    /// usually cannot be grouped, so a failure is downgraded to a warning rather than aborting.
    /// </summary>
    private static bool TryGroupBasemap(Document doc, IReadOnlyList<ElementId> importedIds, ICollection<string> warnings)
    {
        using Transaction transaction = new Transaction(doc, "Group PLATEAU 2D Basemap");
        transaction.Start();
        try
        {
            Group group = doc.Create.NewGroup(importedIds.ToList());
            group.GroupType.Name = BuildUniqueGroupName(doc, "PLATEAU Basemap");
            transaction.Commit();
            return true;
        }
        catch (Exception ex)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            warnings.Add(
                $"Revit could not group the {importedIds.Count} basemap instances ({ex.Message}); " +
                "they were kept as separate per-grid instances named \"PLATEAU <grid>\".");
            return false;
        }
    }

    private static string BuildUniqueGroupName(Document doc, string preferredName)
    {
        HashSet<string> existing = new HashSet<string>(
            new FilteredElementCollector(doc).OfClass(typeof(GroupType)).Cast<GroupType>().Select(groupType => groupType.Name),
            StringComparer.OrdinalIgnoreCase);

        if (!existing.Contains(preferredName))
        {
            return preferredName;
        }

        for (int index = 2; index < 1000; index++)
        {
            string candidate = string.Format(CultureInfo.InvariantCulture, "{0} ({1})", preferredName, index);
            if (!existing.Contains(candidate))
            {
                return candidate;
            }
        }

        return preferredName + " " + Guid.NewGuid().ToString("N").Substring(0, 6);
    }

    private static string SanitizeFileName(string value)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return value;
    }

    private sealed class PreparedGridDxf
    {
        public PreparedGridDxf(string tileId, string dxfPath, int featureCount)
        {
            TileId = tileId;
            DxfPath = dxfPath;
            FeatureCount = featureCount;
        }

        public string TileId { get; }

        public string DxfPath { get; }

        public int FeatureCount { get; }
    }

    private sealed class DxfImportOutcome
    {
        public DxfImportOutcome(int importedInstanceCount, bool groupCreated, IReadOnlyList<string> warnings)
        {
            ImportedInstanceCount = importedInstanceCount;
            GroupCreated = groupCreated;
            Warnings = warnings;
        }

        public int ImportedInstanceCount { get; }

        public bool GroupCreated { get; }

        public IReadOnlyList<string> Warnings { get; }
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
