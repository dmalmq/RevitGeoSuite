using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.FloorPlanExport.Commands;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.UI;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using RevitGeoSuite.Tiles3DExport;

namespace RevitGeoSuite.Shell.Handlers;

internal static class CesiumExportSupport
{
    public static T ReadPayload<T>(object? payload) where T : new()
    {
        if (payload is JObject jObject)
        {
            return jObject.ToObject<T>() ?? new T();
        }

        if (payload is null)
        {
            return new T();
        }

        return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(payload)) ?? new T();
    }

    /// <summary>
    /// Pairs each host-document level's persisted GIS <c>level_id</c> (written by the floor-plan
    /// export) with the 3D Tiles level key (slug of the level name). Levels without a persisted id
    /// are skipped — they exported no GIS features.
    /// </summary>
    public static List<CesiumPackageLevelMapEntry> BuildLevelMap(Document document)
    {
        var entries = new List<CesiumPackageLevelMapEntry>();
        IList<Element> levels = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .ToElements();
        foreach (Element element in levels)
        {
            if (element is not Level level)
            {
                continue;
            }

            string? gisLevelId = level.LookupParameter(SharedParameterManager.ImdfLevelIdParameterName)?.AsString();
            if (string.IsNullOrWhiteSpace(gisLevelId))
            {
                continue;
            }

            entries.Add(new CesiumPackageLevelMapEntry
            {
                GisLevelId = gisLevelId!.Trim(),
                TilesLevelKey = Tiles3DLevelMetadata.BuildLevelKey(level.Name),
                Name = level.Name ?? string.Empty,
            });
        }

        return entries;
    }
}

/// <summary>
/// <c>cesium.export.getState</c> — saved floor-plan profiles, viewer settings, and the
/// first-run flag for the combined "Export to Cesium" card.
/// </summary>
public sealed class CesiumExportStateHandler : IRpcHandler
{
    public string Method => "cesium.export.getState";

    public Task<object?> HandleAsync(object? payload)
    {
        return RevitContext.Instance.InvokeWithDocumentAsync<object?>(document =>
        {
            string projectKey = DocumentProjectKeyBuilder.Create(document);
            IReadOnlyList<ExportProfile> profiles = new ExportProfileStore().LoadWithDiagnostics(projectKey).Value;
            CesiumViewerSettings viewerSettings = new CesiumViewerSettingsStore().Load();

            return new CesiumExportStateResponse
            {
                FloorPlanProfiles = profiles.Select(profile => profile.Name).ToArray(),
                LastOutputFolder = profiles.FirstOrDefault()?.OutputDirectory ?? string.Empty,
                ViewerUrl = viewerSettings.ViewerUrl,
                HasToken = !string.IsNullOrEmpty(viewerSettings.Token),
                FirstRun = profiles.Count == 0,
            };
        });
    }
}

/// <summary>
/// <c>cesium.export.run</c> — background job that runs the 3D Tiles export into
/// <c>&lt;outputFolder&gt;/tiles/</c>, the floor-plan GIS export into <c>&lt;outputFolder&gt;/gis/</c>,
/// writes <c>cesium-package.json</c>, and optionally pushes the package to the viewer.
/// Only the two export steps touch the Revit API; manifest assembly and the HTTP push run
/// on the job thread (WebView2 shares Revit's UI thread, so nothing heavy may run there).
/// </summary>
public sealed class CesiumExportRunHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public CesiumExportRunHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "cesium.export.run";

    public Task<object?> HandleAsync(object? payload)
    {
        CesiumExportRunRequest request = CesiumExportSupport.ReadPayload<CesiumExportRunRequest>(payload);
        if (string.IsNullOrWhiteSpace(request.OutputFolder))
        {
            return Task.FromResult<object?>(new { error = "Output folder is required" });
        }

        if (string.IsNullOrWhiteSpace(request.FloorPlanProfileName))
        {
            return Task.FromResult<object?>(new { error = "Floor plan profile is required" });
        }

        string jobId = jobs.Start(async (ct, progress) =>
        {
            var builder = new CesiumPackageLayoutBuilder();
            CesiumPackageLayout stagingLayout = builder.CreateStagingLayout(request.OutputFolder);
            CesiumPackageLayout layout = stagingLayout;
            try
            {
                var warnings = new List<string>();

                progress.Report(new JobProgress { Phase = "tiles", Percent = 5, Message = "Exporting 3D Tiles…" });
            ct.ThrowIfCancellationRequested();

            Tiles3DStepResult tiles = await RevitContext.Instance
                .InvokeWithDocumentAsync(document => RunTilesExport(document, request, layout, warnings))
                .ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "gis", Percent = 45, Message = "Exporting floor plans…" });
            ct.ThrowIfCancellationRequested();

            GisStepResult gis = await RevitContext.Instance
                .InvokeWithDocumentAsync(document => RunGisExport(document, request, layout, warnings, ct))
                .ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "manifest", Percent = 85, Message = "Writing package manifest…" });

                builder.WriteManifest(layout, new CesiumPackageBuildInputs
                {
                BuildingId = CesiumBuildingIdentity.CreateId(gis.ProjectKey, gis.BuildingName),
                BuildingName = gis.BuildingName,
                SourceModel = gis.SourceModel,
                DocumentKey = gis.ProjectKey,
                GeneratorVersion = typeof(CesiumExportRunHandler).Assembly.GetName().Version?.ToString() ?? string.Empty,
                ProjectEpsg = tiles.ProjectEpsg,
                CoordinateMode = gis.CoordinateMode,
                GisEpsg = gis.GisEpsg,
                AnchorLat = tiles.AnchorLat,
                AnchorLon = tiles.AnchorLon,
                AnchorElevationMeters = tiles.AnchorElevationMeters,
                GeoidOffsetMeters = tiles.GeoidOffsetMeters,
                LevelMap = gis.LevelMap,
                GisLayers = new List<string> { "unit", "detail", "opening", "fixture", "level" },
                });

                string stagedRoot = layout.RootDirectory;
                layout = builder.PublishLayout(layout, request.OutputFolder);
                tiles.TilesetPath = RebasePackagePath(tiles.TilesetPath, stagedRoot, layout.RootDirectory);
                gis.ArtifactPaths = gis.ArtifactPaths
                    .Select(path => RebasePackagePath(path, stagedRoot, layout.RootDirectory))
                    .ToList();

            bool pushed = false;
            string pushMessage = string.Empty;
            if (request.Push)
            {
                progress.Report(new JobProgress { Phase = "push", Percent = 90, Message = "Pushing to Cesium viewer…" });
                CesiumViewerSettings viewerSettings = new CesiumViewerSettingsStore().Load();
                using var pushClient = new CesiumViewerPushClient();
                CesiumViewerPushResult pushResult = await pushClient.PushAsync(
                    new CesiumViewerPushRequest
                    {
                        ViewerUrl = viewerSettings.ViewerUrl,
                        PackageRoot = layout.RootDirectory,
                        Token = viewerSettings.Token,
                    },
                    ct).ConfigureAwait(false);
                pushed = pushResult.Status == CesiumViewerPushStatus.Success;
                pushMessage = pushResult.Message;
                if (!pushed)
                {
                    warnings.Add(pushResult.Message);
                }
            }

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Export complete" });

                return (object?)new CesiumExportRunResponse
                {
                    PackageRoot = layout.RootDirectory,
                    ManifestPath = layout.ManifestPath,
                    TilesetPath = tiles.TilesetPath,
                    GisArtifacts = gis.ArtifactPaths.ToArray(),
                    Pushed = pushed,
                    PushMessage = pushMessage,
                    Summary = $"Package written to {layout.RootDirectory}",
                    Warnings = warnings.ToArray(),
                };
            }
            finally
            {
                builder.DeleteStagingLayout(stagingLayout);
            }
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static string RebasePackagePath(string path, string sourceRoot, string destinationRoot)
    {
        string relative = Path.GetFullPath(path).Substring(
            Path.GetFullPath(sourceRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(destinationRoot, relative);
    }

    private sealed class Tiles3DStepResult
    {
        public string TilesetPath = string.Empty;
        public int ProjectEpsg;
        public double AnchorLat;
        public double AnchorLon;
        public double AnchorElevationMeters;
        public double? GeoidOffsetMeters;
    }

    private sealed class GisStepResult
    {
        public string ProjectKey = string.Empty;
        public string BuildingName = string.Empty;
        public string SourceModel = string.Empty;
        public string CoordinateMode = string.Empty;
        public int GisEpsg;
        public List<string> ArtifactPaths = new();
        public List<CesiumPackageLevelMapEntry> LevelMap = new();
    }

    private static Tiles3DStepResult RunTilesExport(
        Document document,
        CesiumExportRunRequest request,
        CesiumPackageLayout layout,
        List<string> warnings)
    {
        var (handle, currentState, info) = ExportHandlerSupport.ReadContext(document);
        var (coordinator, reference, scopeSelection) = Tiles3DExportSupport.Resolve(
            document, handle, currentState, info, request.Scope, request.SelectedViewUniqueId);
        Tiles3DExportSupport.ApplySelectedLinks(scopeSelection, document, request.SelectedLinkUniqueIds);

        Tiles3DLevelOfDetail lod = (request.Lod ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "1" or "coarse" => Tiles3DLevelOfDetail.Coarse,
            "2" or "medium" => Tiles3DLevelOfDetail.Medium,
            _ => Tiles3DLevelOfDetail.Fine,
        };

        Tiles3DExportPreparationResult prep = coordinator.Prepare(
            handle, reference, scopeSelection, lod, request.PreciseCrs, request.GeoidOffset);
        Tiles3DExportState? existingState = new Tiles3DExportStateService().Load(handle);
        Tiles3DExportResult exportResult = coordinator.Export(
            handle, prep.Package, layout.TilesDirectory, Tiles3DExportSupport.ReferenceSource, scopeSelection, existingState);

        warnings.AddRange(Tiles3DExportSupport.ScopeWarnings(request.Scope));
        warnings.AddRange(prep.Warnings);

        Tiles3DExportReferenceContext context = prep.Package.ReferenceContext;
        return new Tiles3DStepResult
        {
            TilesetPath = exportResult.TilesetPath,
            ProjectEpsg = context.ProjectCrs.EpsgCode,
            AnchorLat = context.AnchorLatitude,
            AnchorLon = context.AnchorLongitude,
            AnchorElevationMeters = context.AnchorElevationMeters,
            GeoidOffsetMeters = prep.Package.GeoidHeightOffsetMeters,
        };
    }

    private static GisStepResult RunGisExport(
        Document document,
        CesiumExportRunRequest request,
        CesiumPackageLayout layout,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        string projectKey = DocumentProjectKeyBuilder.Create(document);
        IReadOnlyList<ExportProfile> profiles = new ExportProfileStore().LoadWithDiagnostics(projectKey).Value;
        ExportProfile profile = profiles.FirstOrDefault(candidate =>
                string.Equals(candidate.Name, request.FloorPlanProfileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Floor plan profile '{request.FloorPlanProfileName}' was not found for this project.");

        ModelCoordinateInfo coordinateInfo = new ModelCoordinateInfoReader().Read(document);
        var exporter = new FloorPlanHeadlessExporter(document, uiDocument: null);
        FloorGeoPackageExportResult result = exporter.Export(
            profile,
            coordinateInfo,
            outputDirectoryOverride: layout.GisDirectory,
            packagingModeOverride: PackagingMode.PerBuildingGeoPackage,
            outputFormatOverride: ExportFormat.GeoPackage,
            progressCallback: null,
            cancellationToken: cancellationToken);
        warnings.AddRange(result.Warnings);

        ExportDialogSettings settings = profile.ToSettings();
        return new GisStepResult
        {
            ProjectKey = projectKey,
            BuildingName = DocumentProjectKeyBuilder.CreateDisplayName(document),
            SourceModel = Path.GetFileName(document.PathName ?? string.Empty),
            CoordinateMode = settings.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs
                ? "ConvertToTargetCrs"
                : "SharedCoordinates",
            GisEpsg = settings.TargetEpsg,
            ArtifactPaths = result.ArtifactResults.Select(artifact => artifact.OutputFilePath).ToList(),
            LevelMap = CesiumExportSupport.BuildLevelMap(document),
        };
    }
}

/// <summary>
/// <c>cesium.push</c> — background job that wraps an existing export folder (3D Tiles bundle
/// or floor-plan GIS output) in a <c>cesium-package.json</c> and pushes it to the viewer.
/// Backs the per-dialog "Send to Cesium viewer" post-export action; partial packages
/// (tiles-only / GIS-only) are expected and handled by the viewer via <c>building.id</c>.
/// </summary>
public sealed class CesiumPushHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public CesiumPushHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "cesium.push";

    public Task<object?> HandleAsync(object? payload)
    {
        CesiumPushRequest request = CesiumExportSupport.ReadPayload<CesiumPushRequest>(payload);
        if (string.IsNullOrWhiteSpace(request.Folder) || !Directory.Exists(request.Folder))
        {
            return Task.FromResult<object?>(new { error = "A valid export folder is required" });
        }

        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "manifest", Percent = 10, Message = "Describing export…" });

            // Building identity + level map need Revit reads; everything else stays off the UI thread.
            CesiumPackageBuildInputs inputs = await RevitContext.Instance
                .InvokeWithDocumentAsync(document =>
                {
                    string projectKey = DocumentProjectKeyBuilder.Create(document);
                    string buildingName = DocumentProjectKeyBuilder.CreateDisplayName(document);
                    return new CesiumPackageBuildInputs
                    {
                        BuildingId = CesiumBuildingIdentity.CreateId(projectKey, buildingName),
                        BuildingName = buildingName,
                        SourceModel = Path.GetFileName(document.PathName ?? string.Empty),
                        DocumentKey = projectKey,
                        GeneratorVersion = typeof(CesiumPushHandler).Assembly.GetName().Version?.ToString() ?? string.Empty,
                        LevelMap = CesiumExportSupport.BuildLevelMap(document),
                    };
                })
                .ConfigureAwait(false);

            CesiumPackageFolderComposer.ComposeFromFolder(request.Folder, inputs);

            progress.Report(new JobProgress { Phase = "push", Percent = 40, Message = "Pushing to Cesium viewer…" });
            CesiumViewerSettings viewerSettings = new CesiumViewerSettingsStore().Load();
            using var pushClient = new CesiumViewerPushClient();
            CesiumViewerPushResult pushResult = await pushClient.PushAsync(
                new CesiumViewerPushRequest
                {
                    ViewerUrl = viewerSettings.ViewerUrl,
                    PackageRoot = request.Folder,
                    Token = viewerSettings.Token,
                },
                ct).ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = pushResult.Message });
            return (object?)new CesiumPushResponse
            {
                Pushed = pushResult.Status == CesiumViewerPushStatus.Success,
                Message = pushResult.Message,
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }
}

/// <summary>
/// <c>cesium.settings.get</c> / <c>cesium.settings.save</c> — viewer URL + token. The token is
/// never echoed back; the UI only sees whether one is stored.
/// </summary>
public sealed class CesiumViewerSettingsGetHandler : IRpcHandler
{
    public string Method => "cesium.settings.get";

    public Task<object?> HandleAsync(object? payload)
    {
        CesiumViewerSettings settings = new CesiumViewerSettingsStore().Load();
        return Task.FromResult<object?>(new CesiumViewerSettingsPayload
        {
            ViewerUrl = settings.ViewerUrl,
            Token = null,
            HasToken = !string.IsNullOrEmpty(settings.Token),
        });
    }
}

public sealed class CesiumViewerSettingsSaveHandler : IRpcHandler
{
    public string Method => "cesium.settings.save";

    public Task<object?> HandleAsync(object? payload)
    {
        CesiumViewerSettingsPayload request = CesiumExportSupport.ReadPayload<CesiumViewerSettingsPayload>(payload);
        var store = new CesiumViewerSettingsStore();
        CesiumViewerSettings existing = store.Load();

        var updated = new CesiumViewerSettings
        {
            ViewerUrl = string.IsNullOrWhiteSpace(request.ViewerUrl)
                ? CesiumViewerSettings.DefaultViewerUrl
                : request.ViewerUrl.Trim(),
            // null = keep the stored token; empty string = clear it.
            Token = request.Token is null
                ? existing.Token
                : (request.Token.Length == 0 ? null : request.Token),
        };
        store.Save(updated);

        return Task.FromResult<object?>(new CesiumViewerSettingsPayload
        {
            ViewerUrl = updated.ViewerUrl,
            Token = null,
            HasToken = !string.IsNullOrEmpty(updated.Token),
        });
    }
}
