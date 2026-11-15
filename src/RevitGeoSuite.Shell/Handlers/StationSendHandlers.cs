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
using RevitGeoSuite.SharedUI.Localization;
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
        StationSendStateRequest request = CesiumExportSupport.ReadPayload<StationSendStateRequest>(payload);
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            GeoProjectInfo? info = new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document));
            string remembered = info?.GisStationFolder?.Trim() ?? string.Empty;
            string folder = string.IsNullOrWhiteSpace(request.StationFolder) ? remembered : request.StationFolder!.Trim();
            string documentKey = DocumentProjectKeyBuilder.Create(document);
            string? modelId = info?.ModelId;
            CesiumPackageSource? last = folder.Length == 0 || string.IsNullOrWhiteSpace(modelId)
                ? null
                : CesiumStationFolder.LatestSource(folder, modelId!);
            return new StationSendStateResponse
            {
                StationFolder = folder,
                ProfileName = StationSend.ProfileName(document),
                DocumentKey = documentKey,
                FolderKind = folder.Length == 0 ? string.Empty : StationSend.KindName(CesiumStationFolder.Classify(folder)),
                LastSentFromAnotherFile = last is not null && last.DocumentKey.Length > 0
                    && !string.Equals(last.DocumentKey, documentKey, StringComparison.Ordinal),
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
        switch (CesiumStationFolder.Classify(stationFolder))
        {
            case CesiumStationFolder.FolderKind.Missing:
                throw new ArgumentException(stationFolder.Length == 0
                    ? Text("Send.Error.NoFolder")
                    : string.Format(Text("Send.Error.FolderMissing"), stationFolder));
            case CesiumStationFolder.FolderKind.New when !request.ConfirmNewFolder:
                throw new ArgumentException(string.Format(Text("Send.Error.FolderNew"), stationFolder));
        }

        // One send at a time: a second ribbon click or a reload mid-send must not start another.
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0)
        {
            throw new InvalidOperationException(Text("Send.Error.Busy"));
        }

        try
        {
            string jobId = jobs.Start(async (ct, progress) =>
            {
                try
                {
                    return (object?)await SendAsync(stationFolder, request.DocumentKey ?? string.Empty, ct, progress).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref running, 0);
                }
            });
            return Task.FromResult<object?>(new JobStarted { JobId = jobId });
        }
        catch
        {
            Interlocked.Exchange(ref running, 0);
            throw;
        }
    }

    private static int running;

    private static string Text(string key) => UiLocalizer.Instance.Get(key);

    private static async Task<StationSendRunResponse> SendAsync(string stationFolder, string documentKey, CancellationToken ct, IProgress<JobProgress> progress)
    {
        Stopwatch clock = Stopwatch.StartNew();
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout staging = CesiumStationFolder.CreateStagingLayout(stationFolder);
        try
        {
            var warnings = new List<string>();
            progress.Report(new JobProgress { Phase = "gis", Percent = 10, Message = Text("Send.Progress.Gis") });
            StationSend.Step step = await RevitContext.Instance
                .InvokeWithDocumentAsync(document => StationSend.Run(document, documentKey, stationFolder, staging, warnings, ct))
                .ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "manifest", Percent = 80, Message = Text("Send.Progress.Manifest") });
            CesiumPackageManifest manifest = builder.WriteManifest(staging, step.Inputs);
            CesiumPackageLayout layout = CesiumStationFolder.Publish(staging, stationFolder, manifest);
            step.Gis.ExportResult.CommitPendingBaseline(staging.RootDirectory, layout.RootDirectory);

            // The version is on the share from here on: a failed count must not read as a failed send.
            progress.Report(new JobProgress { Phase = "compare", Percent = 90, Message = Text("Send.Progress.Compare") });
            var response = new StationSendRunResponse { VersionFolder = layout.RootDirectory };
            try
            {
                string? previous = CesiumStationFolder.PreviousVersion(layout.RootDirectory);
                GisPackageChanges changes = GisPackageChanges.Compare(
                    previous is null ? null : new CesiumPackageLayout(previous).GisDirectory,
                    layout.GisDirectory);
                string? previousRules = previous is null ? null : CesiumStationFolder.TryReadManifest(previous)?.Gis?.Rules?.Hash;
                string? rules = manifest.Gis?.Rules?.Hash;
                response.ChangesCounted = true;
                response.HasPrevious = changes.HasPrevious;
                response.RulesDiffer = !string.IsNullOrEmpty(previousRules) && !string.IsNullOrEmpty(rules)
                    && !string.Equals(previousRules, rules, StringComparison.OrdinalIgnoreCase);
                response.ElementsAdded = changes.ElementsAdded;
                response.ElementsRemoved = changes.ElementsRemoved;
                response.ElementsEdited = changes.ElementsEdited;
                response.ElementsChanged = changes.ElementsChanged;
                response.Layers = changes.Layers
                    .Where(layer => layer.Changed > 0)
                    .Select(layer => new StationSendLayerChange { Layer = layer.Layer, Added = layer.Added, Removed = layer.Removed, Edited = layer.Edited })
                    .ToArray();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add(string.Format(Text("Send.Warning.NotCounted"), ex.Message));
            }

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = Text("Send.Progress.Done") });
            response.Seconds = Math.Round(clock.Elapsed.TotalSeconds, 1);
            response.Warnings = warnings.ToArray();
            return response;
        }
        finally
        {
            builder.DeleteStagingLayout(staging);
        }
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

    public static string KindName(CesiumStationFolder.FolderKind kind) => kind switch
    {
        CesiumStationFolder.FolderKind.Missing => "missing",
        CesiumStationFolder.FolderKind.New => "new",
        _ => "station",
    };

    public static Step Run(Document document, string documentKey, string stationFolder, CesiumPackageLayout staging, List<string> warnings, CancellationToken ct)
    {
        // The page read its state from one model; the shell is modeless, so another may be active now.
        if (!string.Equals(DocumentProjectKeyBuilder.Create(document), documentKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(UiLocalizer.Instance.Get("Send.Error.OtherModel"));
        }

        string profileName = ProfileName(document);
        if (profileName.Length == 0)
        {
            throw new InvalidOperationException(UiLocalizer.Instance.Get("Send.NoProfile"));
        }

        var (handle, currentState, info) = ExportHandlerSupport.ReadContext(document);
        // Resolve first: a model without a georeference fails here, before anything is written.
        var (coordinator, reference, _) = Tiles3DExportSupport.Resolve(document, handle, currentState, info, "whole", null);

        // The export reads the model's gis-model.json from the station folder the model remembers,
        // so the folder is recorded first. The page checked it exists, and a folder without Kanizu
        // versions was confirmed. A failed export puts the folder the model had back.
        var identity = new ModelIdentityService();
        string? before = new GeoProjectInfoStorage().Load(handle)?.GisStationFolder;
        identity.RecordStationFolder(document, stationFolder, warnings);
        GisStepResult gis;
        try
        {
            gis = CesiumExportSupport.RunGisExport(document, profileName, staging.GisDirectory, "station", warnings, ct);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(before))
            {
                identity.RecordStationFolder(document, before!, new List<string>());
            }

            throw;
        }

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
            level.ElementCount = level.LevelUniqueIds
                .Select(uniqueId => levelIdByUniqueId[uniqueId])
                .Distinct(StringComparer.Ordinal)
                .Sum(levelId => features.TryGetValue(levelId, out int count) ? count : 0);
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
