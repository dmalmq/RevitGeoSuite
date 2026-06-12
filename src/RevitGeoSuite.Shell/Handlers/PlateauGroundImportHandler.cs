using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Dem;
using RevitGeoSuite.Core.Plateau.Terrain;
using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>
/// <c>plateau.importGround</c> — builds a native Revit <see cref="Autodesk.Revit.DB.Architecture.TopographySurface"/>
/// from PLATEAU DEM at correct heights, anchored on the project's georeference. Two sources feed the
/// same grid builder + topography importer:
/// <list type="bullet">
/// <item><c>local</c> — scans a PLATEAU folder and builds a <see cref="DemSampler"/> from its Relief
/// (dem) surfaces. Orthometric, so no geoid offset.</item>
/// <item><c>online</c> — fetches PLATEAU terrain from Cesium Ion (quantized-mesh) over a radius around
/// the project and builds a <see cref="DemSampler"/> from it. Ellipsoidal, so a geoid offset applies.</item>
/// </list>
/// </summary>
public sealed class PlateauGroundImportHandler : IRpcHandler
{
    // Public PLATEAU terrain token bundled with the reference cesium app (Cesium Ion asset 3258112).
    // Overridable per request via "ionToken".
    private const string DefaultPlateauTerrainToken =
        "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJqdGkiOiJiODVhMmQ5OS1hOWZjLTQ3YmYtODlmNi1lNWUwY2MwOGUxYTMiLCJpZCI6MTQ5ODk3LCJpYXQiOjE2ODc5MzQ3NDN9.OG0mc3i7ZxGwHQjlMv3TRjiOvKWpzxglxmJRaUIykTY";

    private readonly JobManager jobs;

    public PlateauGroundImportHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.importGround";

    public Task<object?> HandleAsync(object? payload)
    {
        JObject? jobj = payload as JObject;
        string? path = jobj?.Value<string>("path");
        List<string>? tileIds = jobj?.Value<JArray>("tileIds")?.Select(t => t.ToString()).ToList();
        string source = jobj?.Value<string>("source") ?? "local";
        bool isOnline = string.Equals(source, "online", StringComparison.OrdinalIgnoreCase);
        double gridSpacingMeters = jobj?.Value<double?>("gridSpacingMeters") ?? PlateauGroundSurfaceBuilder.DefaultGridSpacingMeters;
        double radiusMeters = jobj?.Value<double?>("radiusMeters") ?? 600d;
        double geoidOffsetMeters = jobj?.Value<double?>("geoidOffsetMeters") ?? 0d;
        string ionToken = jobj?.Value<string>("ionToken") is string t && !string.IsNullOrWhiteSpace(t) ? t : DefaultPlateauTerrainToken;

        if (!isOnline && !string.Equals(source, "local", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<object?>(new { error = $"Unknown ground source '{source}'." });
        }
        if (!isOnline && string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult<object?>(new { error = "Path is required" });
        }
        if (!isOnline && (tileIds == null || tileIds.Count == 0))
        {
            return Task.FromResult<object?>(new { error = "At least one tile ID is required" });
        }

        string jobId = jobs.Start(async (ct, progress) =>
        {
            var transformer = new CoordinateTransformer(new CrsRegistry());

            // 1) Resolve the georeference anchor on the Revit API thread.
            progress.Report(new JobProgress { Phase = "preparing", Percent = 0, Message = "Resolving georeference…" });
            PlateauImportReferenceContext? referenceContext = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
            {
                var geoStore = new GeoProjectInfoStorage();
                var moduleStateStore = new ModuleStateStorage();
                var reader = new ProjectLocationReader(geoStore, moduleStateStore: moduleStateStore);
                var currentState = reader.Read(doc);
                var handle = new RevitDocumentHandle(doc);
                var info = geoStore.Load(handle);
                var resolver = new PlateauImportReferenceResolver(transformer, new RevitPlateauImportLocalBasisProvider(doc));
                return resolver.Resolve(currentState, info, PlateauImportReferenceSource.WorkingProjectBasePoint);
            }).ConfigureAwait(false);

            if (referenceContext is null)
            {
                throw new InvalidOperationException(
                    "This project isn't georeferenced yet. Complete Georeference Setup (CRS + Project Base Point) before importing ground.");
            }

            ct.ThrowIfCancellationRequested();

            // 2) Build a DEM sampler from the chosen source (off the Revit thread).
            var warnings = new List<string>();
            DemSampler? sampler;
            double effectiveGeoidOffset;
            if (isOnline)
            {
                // Cesium/PLATEAU terrain heights are ellipsoidal — apply the geoid offset.
                effectiveGeoidOffset = geoidOffsetMeters;
                progress.Report(new JobProgress { Phase = "downloading", Percent = 15, Message = "Resolving PLATEAU terrain…" });
                var transport = new CesiumTerrainHttpTransport();
                CesiumTerrainSource terrainSource = await RunStageAsync("terrain-endpoint", () =>
                    CesiumIonTerrainEndpoint.ResolveAsync(CesiumIonTerrainEndpoint.PlateauTerrainAssetId, ionToken, transport, ct)).ConfigureAwait(false);

                (double west, double south, double east, double north) =
                    BoundingBox(referenceContext.AnchorLatitude, referenceContext.AnchorLongitude, radiusMeters);
                var tileProgress = new Progress<string>(message =>
                    progress.Report(new JobProgress { Phase = "downloading", Percent = 50, Message = message }));
                sampler = await RunStageAsync("terrain-sampler", () => new CesiumTerrainSampler(transport).BuildAsync(
                    terrainSource, west, south, east, north, transformer, referenceContext.ProjectCrs, warnings, tileProgress, ct)).ConfigureAwait(false);
                if (sampler is null || sampler.IsEmpty)
                {
                    string detail = warnings.Count > 0
                        ? " (" + string.Join(" | ", warnings.Skip(Math.Max(0, warnings.Count - 2))) + ")"
                        : string.Empty;
                    throw new InvalidOperationException(
                        "No PLATEAU terrain was available for this area. Try a larger radius, or check the Cesium Ion token." + detail);
                }
            }
            else
            {
                // Local PLATEAU dem is orthometric (same datum as the model anchor), so no geoid offset.
                effectiveGeoidOffset = 0d;
                progress.Report(new JobProgress { Phase = "scanning", Percent = 10, Message = "Scanning folder…" });
                PlateauFolderScanResult scanResult = RunStage("scan", () =>
                {
                    var scanService = new PlateauFolderScanService(new CityGmlParser());
                    return scanService.ScanFolder(path!, p =>
                    {
                        ct.ThrowIfCancellationRequested();
                        progress.Report(new JobProgress
                        {
                            Phase = "scanning",
                            Current = p.Current,
                            Total = p.Total,
                            Percent = 10 + (int)Math.Round(p.Percent * 0.45), // scan occupies 10–55%
                            Message = p.CurrentFileName
                        });
                    });
                });

                ct.ThrowIfCancellationRequested();
                progress.Report(new JobProgress { Phase = "building", Percent = 60, Message = "Reading DEM surfaces…" });
                sampler = RunStage("relief-sampler", () =>
                    new ContextGeometryBuilder(transformer).BuildReliefSampler(scanResult, referenceContext, tileIds, warnings));
                if (sampler is null || sampler.IsEmpty)
                {
                    throw new InvalidOperationException(
                        "The selected tiles contain no DEM (Relief) surfaces. Pick tiles that include a 'dem' dataset, or download it for this area.");
                }
            }

            ct.ThrowIfCancellationRequested();

            // 3) Grid-sample the ground into Revit-frame points.
            progress.Report(new JobProgress { Phase = "building", Percent = 75, Message = "Sampling ground grid…" });
            PlateauGroundSurfaceBuilder.GroundSurfaceResult ground = RunStage("ground-grid", () =>
                new PlateauGroundSurfaceBuilder().Build(sampler, referenceContext, gridSpacingMeters, effectiveGeoidOffset));
            warnings.AddRange(ground.Warnings);

            if (ground.PointCount < 3)
            {
                throw new InvalidOperationException(
                    "Not enough ground points fell inside the DEM coverage to build a surface. Try a finer grid or different tiles.");
            }

            ct.ThrowIfCancellationRequested();

            // 4) Create the TopographySurface on the Revit API thread.
            progress.Report(new JobProgress { Phase = "importing", Percent = 88, Message = $"Creating topography from {ground.PointCount:N0} points…" });
            PlateauTopographyImporter.TopographyImportResult result = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
                RunStage("topography", () => new PlateauTopographyImporter().Import(doc, ground.Points, warnings))).ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Ground import complete" });

            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Created a topography surface from {0:N0} DEM points at {1:0.##} m spacing.",
                result.PointCount,
                ground.EffectiveSpacingMeters);

            return (object?)new
            {
                mode = "ground",
                surfaceId = result.SurfaceId.Value,
                pointCount = result.PointCount,
                replaced = result.ReplacedSurfaceCount,
                spacingMeters = ground.EffectiveSpacingMeters,
                summary,
                warnings = result.Warnings
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    // Tags failures with the stage and the first RevitGeoSuite stack frame so a low-context CLR
    // message (e.g. "Array dimensions exceeded supported range") still pinpoints where it threw.
    private static T RunStage<T>(string stage, Func<T> work)
    {
        try
        {
            return work();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[{stage}] {ex.GetType().Name}: {ex.Message} @ {FirstAppFrame(ex)}", ex);
        }
    }

    private static async Task<T> RunStageAsync<T>(string stage, Func<Task<T>> work)
    {
        try
        {
            return await work().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"[{stage}] {ex.GetType().Name}: {ex.Message} @ {FirstAppFrame(ex)}", ex);
        }
    }

    // Lon/lat box of the given metric radius around a point (small-area equirectangular approximation).
    private static (double West, double South, double East, double North) BoundingBox(double latitudeDegrees, double longitudeDegrees, double radiusMeters)
    {
        const double metersPerDegreeLat = 111320d;
        double deltaLat = radiusMeters / metersPerDegreeLat;
        double cosLat = Math.Cos(latitudeDegrees * Math.PI / 180d);
        double deltaLon = radiusMeters / (metersPerDegreeLat * Math.Max(0.01d, Math.Abs(cosLat)));
        return (longitudeDegrees - deltaLon, latitudeDegrees - deltaLat, longitudeDegrees + deltaLon, latitudeDegrees + deltaLat);
    }

    private static string FirstAppFrame(Exception ex)
    {
        string? stack = ex.StackTrace;
        if (string.IsNullOrEmpty(stack))
        {
            return "no stack";
        }

        foreach (string line in stack!.Split('\n'))
        {
            if (line.IndexOf("RevitGeoSuite", StringComparison.Ordinal) >= 0)
            {
                return line.Trim();
            }
        }

        return stack!.Split('\n')[0].Trim();
    }
}
