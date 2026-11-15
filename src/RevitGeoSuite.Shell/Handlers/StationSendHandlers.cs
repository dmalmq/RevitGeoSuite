using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.UI;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using RevitGeoSuite.Tiles3DExport;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>
/// <c>station.send.getState</c>: the station folder the model remembers and the profile a send uses.
/// </summary>
public sealed class StationSendStateHandler : IRpcHandler
{
    public string Method => "station.send.getState";

    public Task<object?> HandleAsync(object? payload)
    {
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            GeoProjectInfo? info = new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document));
            return new StationSendStateResponse
            {
                StationFolder = info?.GisStationFolder?.Trim() ?? string.Empty,
                ProfileName = StationSend.ProfileName(document),
                ModelName = Path.GetFileName(document.PathName ?? string.Empty) is { Length: > 0 } name ? name : document.Title,
            };
        });
    }
}

/// <summary>
/// <c>station.send.run</c>: Send to Kanizu. A background job that writes the model's GIS package
/// with the level and room indexes, and no 3D Tiles, as a new version in the station folder, then
/// counts what changed since the version before it. The page runs the audit first.
/// </summary>
public sealed class StationSendRunHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public StationSendRunHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "station.send.run";

    public Task<object?> HandleAsync(object? payload)
    {
        StationSendRunRequest request = CesiumExportSupport.ReadPayload<StationSendRunRequest>(payload);
        string stationFolder = request.StationFolder?.Trim() ?? string.Empty;
        if (stationFolder.Length == 0)
        {
            throw new ArgumentException("Choose the station folder first.");
        }

        string jobId = jobs.Start(async (ct, progress) =>
        {
            Stopwatch clock = Stopwatch.StartNew();
            var builder = new CesiumPackageLayoutBuilder();
            CesiumPackageLayout staging = CesiumStationFolder.CreateStagingLayout(stationFolder);
            try
            {
                var warnings = new List<string>();
                progress.Report(new JobProgress { Phase = "gis", Percent = 10, Message = "Exporting floor plans…" });
                StationSend.Step step = await RevitContext.Instance
                    .InvokeWithDocumentAsync(document => StationSend.Run(document, stationFolder, staging, warnings, ct))
                    .ConfigureAwait(false);

                progress.Report(new JobProgress { Phase = "manifest", Percent = 80, Message = "Writing package manifest…" });
                CesiumPackageManifest manifest = builder.WriteManifest(staging, step.Inputs);
                CesiumPackageLayout layout = CesiumStationFolder.Publish(staging, stationFolder, manifest);
                step.Gis.ExportResult.CommitPendingBaseline(staging.RootDirectory, layout.RootDirectory);

                progress.Report(new JobProgress { Phase = "compare", Percent = 90, Message = "Comparing with the last send…" });
                string? previous = CesiumStationFolder.PreviousVersion(layout.RootDirectory);
                GisPackageChanges changes = GisPackageChanges.Compare(
                    previous is null ? null : new CesiumPackageLayout(previous).GisDirectory,
                    layout.GisDirectory);

                progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Sent" });
                return (object?)new StationSendRunResponse
                {
                    VersionFolder = layout.RootDirectory,
                    HasPrevious = changes.HasPrevious,
                    ElementsAdded = changes.ElementsAdded,
                    ElementsRemoved = changes.ElementsRemoved,
                    ElementsEdited = changes.ElementsEdited,
                    ElementsChanged = changes.ElementsChanged,
                    Layers = changes.Layers
                        .Where(layer => layer.Changed > 0)
                        .Select(layer => new StationSendLayerChange { Layer = layer.Layer, Added = layer.Added, Removed = layer.Removed, Edited = layer.Edited })
                        .ToArray(),
                    Seconds = Math.Round(clock.Elapsed.TotalSeconds, 1),
                    Warnings = warnings.ToArray(),
                };
            }
            finally
            {
                builder.DeleteStagingLayout(staging);
            }
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }
}

/// <summary>The Revit side of Send to Kanizu, on Revit's thread.</summary>
internal static class StationSend
{
    /// <summary>The profile the Cesium export starts with: the model's first.</summary>
    public static string ProfileName(Document document)
    {
        string projectKey = DocumentProjectKeyBuilder.Create(document);
        return new ExportProfileStore(rules: GisRulesSourceFactory.For(document))
            .LoadWithDiagnostics(projectKey).Value
            .FirstOrDefault()?.Name ?? string.Empty;
    }

    public static Step Run(Document document, string stationFolder, CesiumPackageLayout staging, List<string> warnings, CancellationToken ct)
    {
        string profileName = ProfileName(document);
        if (profileName.Length == 0)
        {
            throw new InvalidOperationException("This model has no floor plan export profile yet. Save one on Export > Floor plans, then send again.");
        }

        var (handle, currentState, info) = ExportHandlerSupport.ReadContext(document);
        // Resolve first: a model without a georeference fails here, before anything is written.
        var (coordinator, reference, _) = Tiles3DExportSupport.Resolve(document, handle, currentState, info, "whole", null);

        new ModelIdentityService().RecordStationFolder(document, stationFolder, warnings);
        GisStepResult gis = CesiumExportSupport.RunGisExport(document, profileName, staging.GisDirectory, "station", warnings, ct);

        // The levels the GIS export used, by UniqueId, each with its count of features.
        Dictionary<string, string> levelIdByUniqueId = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(level => (level.UniqueId, LevelId: level.LookupParameter(SharedParameterManager.ImdfLevelIdParameterName)?.AsString()?.Trim()))
            .Where(level => !string.IsNullOrEmpty(level.UniqueId) && !string.IsNullOrEmpty(level.LevelId))
            .ToDictionary(level => level.UniqueId, level => level.LevelId!, StringComparer.Ordinal);
        IReadOnlyDictionary<string, int> features = GisPackageChanges.FeaturesPerLevel(staging.GisDirectory);

        Tiles3DIndexPackage indexes = coordinator.PrepareIndexes(handle, reference, levelIdByUniqueId.Keys.ToList(), geoidHeightOffsetMeters: null, warnings);
        foreach (Tiles3DLevelGroup level in indexes.Levels)
        {
            level.ElementCount = features.TryGetValue(levelIdByUniqueId[level.LevelUniqueIds[0]], out int count) ? count : 0;
        }

        indexes.Write(staging.TilesDirectory);
        Tiles3DExportState? tilesState = new Tiles3DExportStateService().Load(handle);
        Tiles3DExportReferenceContext context = indexes.ReferenceContext;
        return new Step
        {
            Gis = gis,
            Inputs = new CesiumPackageBuildInputs
            {
                BuildingId = CesiumBuildingIdentity.CreateId(gis.ProjectKey, gis.BuildingName),
                BuildingName = gis.BuildingName,
                SourceModel = gis.SourceModel,
                DocumentKey = gis.ProjectKey,
                GeneratorVersion = typeof(StationSend).Assembly.GetName().Version?.ToString() ?? string.Empty,
                ProjectEpsg = context.ProjectCrs.EpsgCode,
                CoordinateMode = gis.CoordinateMode,
                GisEpsg = gis.GisEpsg,
                AnchorLat = context.AnchorLatitude,
                AnchorLon = context.AnchorLongitude,
                AnchorElevationMeters = context.AnchorElevationMeters,
                GeoidOffsetMeters = indexes.GeoidHeightOffsetMeters,
                GeoidModel = indexes.GeoidModel,
                HeightDatum = tilesState?.LastHeightDatum,
                AnchorEasting = context.AnchorProjectedCoordinate.Easting,
                AnchorNorthing = context.AnchorProjectedCoordinate.Northing,
                ModelId = new ModelIdentityService().GetOrCreate(document, warnings),
                LevelMap = gis.LevelMap,
                GisLayers = new List<string> { "unit", "detail", "opening", "fixture", "level" },
                GisRules = gis.Rules,
            },
        };
    }

    internal sealed class Step
    {
        public GisStepResult Gis = new();
        public CesiumPackageBuildInputs Inputs = new();
    }
}
