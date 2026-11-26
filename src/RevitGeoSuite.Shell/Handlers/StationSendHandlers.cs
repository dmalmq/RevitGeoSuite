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
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.UI;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Localization;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using RevitGeoSuite.Shell.Standards;
using RevitGeoSuite.Tiles3DExport;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>
/// <c>station.send.getState</c>: the station folder the model remembers and the profile a send uses.
/// </summary>
public sealed class StationSendStateHandler : IRpcHandler
{
    public string Method => "station.send.getState";

    public async Task<object?> HandleAsync(object? payload)
    {
        StationSendStateRequest request = CesiumExportSupport.ReadPayload<StationSendStateRequest>(payload);
        // Revit's thread reads only the model; the folder is read after, so a slow share never
        // holds Revit up.
        var model = await RevitContext.Instance.InvokeWithDocumentAsync(document =>
        {
            GeoProjectInfo? info = new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document));
            return (
                Remembered: info?.GisStationFolder?.Trim() ?? string.Empty,
                ModelId: info?.ModelId,
                DocumentKey: DocumentProjectKeyBuilder.Create(document),
                IsWorkshared: document.IsWorkshared);
        }).ConfigureAwait(false);

        string folder = string.IsNullOrWhiteSpace(request.StationFolder) ? model.Remembered : request.StationFolder!.Trim();
        // The profile the send will use: the one of the folder it sends to, read off Revit's thread.
        string profileName = StationSend.ProfileName(folder, model.DocumentKey);
        // A workshared model's document key hashes each user's local copy, so every colleague's
        // send would look like another file; Save As there makes a new central, which is rare.
        CesiumPackageSource? last = folder.Length == 0 || model.IsWorkshared || string.IsNullOrWhiteSpace(model.ModelId)
            ? null
            : CesiumStationFolder.LatestSource(folder, model.ModelId!);
        return new StationSendStateResponse
        {
            StationFolder = folder,
            ProfileName = profileName,
            DocumentKey = model.DocumentKey,
            FolderKind = folder.Length == 0 ? string.Empty : StationSend.KindName(CesiumStationFolder.Classify(folder)),
            LastSentFromAnotherFile = last is not null && last.DocumentKey.Length > 0
                && !string.Equals(last.DocumentKey, model.DocumentKey, StringComparison.Ordinal),
        };
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

    public async Task<object?> HandleAsync(object? payload)
    {
        StationSendRunRequest request = CesiumExportSupport.ReadPayload<StationSendRunRequest>(payload);
        string stationFolder = request.StationFolder?.Trim() ?? string.Empty;
        // The RPC arrives on the UI thread; a slow or unreachable share is checked off it. The await
        // comes back to the UI thread on purpose: JobManager.Start captures its context for events.
        CesiumStationFolder.FolderKind kind = await Task.Run(() => CesiumStationFolder.Classify(stationFolder));
        switch (kind)
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
            return new JobStarted { JobId = jobId };
        }
        catch
        {
            Interlocked.Exchange(ref running, 0);
            throw;
        }
    }

    private static int running;

    private static string Text(string key) => UiLocalizer.Instance.Get(key);

    /// <summary>
    /// Puts the model's station folder back after a failed send: the folder it had, or none when
    /// this was its first send. Null when that worked or nothing had changed; otherwise a sentence
    /// for the error, so the user knows the model now points at the new folder. Never throws.
    /// </summary>
    private static async Task<string?> RestoreFolderAsync(Document bound, string documentKey, string? before, string attempted)
    {
        string? previous = string.IsNullOrWhiteSpace(before) ? null : before!.Trim();
        if (previous is not null && string.Equals(previous, attempted, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // With no folder before, the failure names the folder the model is still left pointing at.
        string message = previous is null ? Text("Send.Error.ForgetFailed") : Text("Send.Error.RestoreFailed");
        string shown = previous ?? attempted;
        try
        {
            var restoreWarnings = new List<string>();
            bool restored = await RevitContext.Instance
                .InvokeWithDocumentAsync(document => StationSend.Restore(document, bound, documentKey, previous, restoreWarnings))
                .ConfigureAwait(false);
            return restored ? null : string.Format(message, shown, string.Join(" ", restoreWarnings));
        }
        catch (Exception ex)
        {
            return string.Format(message, shown, ex.Message);
        }
    }

    private static async Task<StationSendRunResponse> SendAsync(string stationFolder, string documentKey, CancellationToken ct, IProgress<JobProgress> progress)
    {
        Stopwatch clock = Stopwatch.StartNew();
        var builder = new CesiumPackageLayoutBuilder();
        CesiumPackageLayout staging = CesiumStationFolder.CreateStagingLayout(stationFolder);
        try
        {
            var warnings = new List<string>();
            progress.Report(new JobProgress { Phase = "gis", Percent = 10, Message = Text("Send.Progress.Gis") });

            // The model records the station folder before the export, because the export reads the
            // model's gis-model.json from the folder the model remembers (and records its views,
            // EPSG and links there). Until the version is published, any failure puts back the folder
            // the model had; what the export wrote to the new folder's gis-model.json stays.
            // The send stays bound to this Document: every later step on Revit's thread checks it is
            // still the active one by Document.Equals, as well as by its key.
            (string? before, Document bound) = await RevitContext.Instance
                .InvokeWithDocumentAsync(document => (StationSend.RememberedFolder(document, documentKey), document))
                .ConfigureAwait(false);
            StationSend.Step step;
            CesiumPackageManifest manifest;
            CesiumPackageLayout layout;
            var attempt = new StationSend.Attempt();
            try
            {
                step = await RevitContext.Instance
                    .InvokeWithDocumentAsync(document => StationSend.Run(document, bound, documentKey, stationFolder, staging, attempt, warnings, ct))
                    .ConfigureAwait(false);

                // Closing the shell cancels the job, also after the export's last check. Publishing is
                // the point of no return, so a cancel up to here restores the folder like any failure.
                ct.ThrowIfCancellationRequested();
                progress.Report(new JobProgress { Phase = "manifest", Percent = 80, Message = Text("Send.Progress.Manifest") });
                manifest = builder.WriteManifest(staging, step.Inputs);
                ct.ThrowIfCancellationRequested();
                layout = CesiumStationFolder.Publish(staging, stationFolder, manifest);
            }
            catch (Exception ex)
            {
                // The restore is its own invoke, after the export's transactions have ended. Only a
                // send that got as far as recording the folder has anything to put back.
                string? restoreFailure = attempt.Recorded
                    ? await RestoreFolderAsync(bound, documentKey, before, stationFolder).ConfigureAwait(false)
                    : null;
                if (restoreFailure is null)
                {
                    throw;
                }

                // Not an OperationCanceledException even when cancelled: JobManager reports those as
                // a bare "Cancelled", and the user must learn the model still points at the new folder.
                throw new InvalidOperationException($"{ex.Message} {restoreFailure}", ex);
            }

            // The version is on the share from here on: a failed count must not read as a failed send.
            GisStandardNotifier.Instance.NoteExportPublished();
            progress.Report(new JobProgress { Phase = "compare", Percent = 90, Message = Text("Send.Progress.Compare") });
            var response = new StationSendRunResponse { VersionFolder = layout.RootDirectory };
            try
            {
                step.Gis.ExportResult.CommitPendingBaseline(staging.RootDirectory, layout.RootDirectory);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                warnings.Add(string.Format(Text("Send.Warning.BaselineNotSaved"), ex.GetType().Name + ": " + ex.Message));
            }

            try
            {
                string? previous = CesiumStationFolder.PreviousVersion(layout.RootDirectory);
                GisPackageChanges changes = GisPackageChanges.Compare(
                    previous is null ? null : new CesiumPackageLayout(previous).GisDirectory,
                    layout.GisDirectory);
                CesiumPackageGisRules? previousRules = previous is null ? null : CesiumStationFolder.TryReadManifest(previous)?.Gis?.Rules;
                CesiumPackageGisRules? rules = manifest.Gis?.Rules;
                response.ChangesCounted = true;
                response.HasPrevious = changes.HasPrevious;
                response.RulesDiffer = CesiumPackageGisRules.Differ(previousRules, rules);
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
                warnings.Add(string.Format(Text("Send.Warning.NotCounted"), ex.GetType().Name + ": " + ex.Message));
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

    /// <summary>
    /// The profile <paramref name="stationFolder"/> gives the model with <paramref name="projectKey"/>,
    /// read without the model and without recording the folder: the same rules the send reads once
    /// the folder is recorded. It touches no Revit API, so it can run off Revit's thread.
    /// </summary>
    public static string ProfileName(string stationFolder, string projectKey)
    {
        return new ExportProfileStore(rules: GisRulesSource.Resolve(stationFolder, projectKey))
            .LoadWithDiagnostics(projectKey).Value
            .FirstOrDefault()?.Name ?? string.Empty;
    }

    public static string KindName(CesiumStationFolder.FolderKind kind) => kind switch
    {
        CesiumStationFolder.FolderKind.Missing => "missing",
        CesiumStationFolder.FolderKind.New => "new",
        _ => "station",
    };

    /// <summary>
    /// The page read its state from one model; the shell is modeless, so another may be active now.
    /// Once a send has started, it is also bound to the Document it started on.
    /// </summary>
    private static void RequireDocument(Document document, string documentKey, Document? bound = null)
    {
        if (!IsDocument(document, documentKey, bound))
        {
            throw new InvalidOperationException(UiLocalizer.Instance.Get("Send.Error.OtherModel"));
        }
    }

    private static bool IsDocument(Document document, string documentKey, Document? bound) =>
        (bound is null || DocumentIdentity.IsSameDocument(document, bound))
        && string.Equals(DocumentProjectKeyBuilder.Create(document), documentKey, StringComparison.Ordinal);

    /// <summary>The station folder the model remembers before this send, after checking it is the page's model.</summary>
    public static string? RememberedFolder(Document document, string documentKey)
    {
        RequireDocument(document, documentKey);
        return new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document))?.GisStationFolder?.Trim();
    }

    /// <summary>
    /// Records <paramref name="folder"/> again, or clears the folder when it is null (the model had
    /// none). Both open their own transaction, are a no-op when nothing would change, and write only
    /// the model's GeoSuite record (not gis-model.json). False, with its warning, when the model is no
    /// longer active or is read-only.
    /// </summary>
    public static bool Restore(Document document, Document bound, string documentKey, string? folder, List<string> warnings)
    {
        if (!IsDocument(document, documentKey, bound))
        {
            warnings.Add(UiLocalizer.Instance.Get("Send.Error.OtherModel"));
            return false;
        }

        var identity = new ModelIdentityService();
        return folder is null
            ? identity.ForgetStationFolder(document, warnings)
            : identity.RecordStationFolder(document, folder, warnings);
    }

    /// <summary>How far a send got on Revit's side: whether the model now records the new folder.</summary>
    internal sealed class Attempt
    {
        public bool Recorded;
    }

    public static Step Run(Document document, Document bound, string documentKey, string stationFolder, CesiumPackageLayout staging, Attempt attempt, List<string> warnings, CancellationToken ct)
    {
        RequireDocument(document, documentKey, bound);

        var (handle, currentState, info) = ExportHandlerSupport.ReadContext(document);
        // Resolve first: a model without a georeference fails here, before anything is written.
        var (coordinator, reference, _) = Tiles3DExportSupport.Resolve(document, handle, currentState, info, "whole", null);

        // A station with no profile for this model fails before the model gets an id or records it.
        if (ProfileName(stationFolder, DocumentProjectKeyBuilder.Create(document)).Length == 0)
        {
            throw new InvalidOperationException(UiLocalizer.Instance.Get("Send.NoProfile"));
        }

        // The station's gis-rules.json and gis-model.json apply only to a model that has both a station
        // folder and a model id, so a model sending for the first time gets its id before the export.
        string? modelId = new ModelIdentityService().GetOrCreate(document, warnings);

        // The export reads gis-model.json from the recorded folder; if the model cannot record this
        // one (read-only, busy), it would export with the old folder's settings into the new folder.
        var recordWarnings = new List<string>();
        if (!new ModelIdentityService().RecordStationFolder(document, stationFolder, recordWarnings))
        {
            throw new InvalidOperationException(string.Format(UiLocalizer.Instance.Get("Send.Error.RecordFailed"), stationFolder, string.Join(" ", recordWarnings)).Trim());
        }

        attempt.Recorded = true;

        // The profile comes from the station just recorded: a model moving to another station uses
        // that station's profiles. With none, the send fails here and the old folder is put back.
        string profileName = ProfileName(document);
        if (profileName.Length == 0)
        {
            throw new InvalidOperationException(UiLocalizer.Instance.Get("Send.NoProfile"));
        }

        GisStepResult gis = CesiumExportSupport.RunGisExport(document, profileName, staging.GisDirectory, "station", warnings, ct);

        // The levels the GIS export used, by UniqueId, each with its count of features.
        Dictionary<string, string> levelIdByUniqueId = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(level => (level.UniqueId, LevelId: level.LookupParameter(SharedParameterManager.ImdfLevelIdParameterName)?.AsString()?.Trim()))
            .Where(level => !string.IsNullOrEmpty(level.UniqueId) && !string.IsNullOrEmpty(level.LevelId))
            .ToDictionary(level => level.UniqueId, level => level.LevelId!, StringComparer.Ordinal);
        IReadOnlyDictionary<string, int> features = GisPackageChanges.FeaturesPerLevel(staging.GisDirectory);

        Tiles3DIndexPackage indexes = coordinator.PrepareIndexes(handle, reference, levelIdByUniqueId.Keys.ToList(), gis.LinkInstanceIds, geoidHeightOffsetMeters: null, warnings);
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
                ModelId = modelId,
                ModelRole = GisStandardStateReader.ReadModelRole(document),
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
