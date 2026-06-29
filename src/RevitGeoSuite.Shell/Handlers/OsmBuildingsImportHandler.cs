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
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.Plateau.Terrain;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.PlateauImport.Online;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

public sealed class OsmBuildingsImportHandler : IRpcHandler
{
    private const double FeetToMeters = 0.3048d;
    private const double EarthRadiusMeters = 6378137d;
    private const string ImportModeSolids = "solids";
    private const string ImportModeDxf = "dxf";

    private readonly JobManager jobs;

    public OsmBuildingsImportHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "osm.importBuildings";

    public Task<object?> HandleAsync(object? payload)
    {
        JObject? request = payload as JObject;
        string ionToken = request?.Value<string>("ionToken")?.Trim() ?? string.Empty;
        double radiusMeters = request?.Value<double?>("radiusMeters") ?? 500d;
        string mode = request?.Value<string>("mode")?.Trim().ToLowerInvariant() ?? ImportModeSolids;

        if (string.IsNullOrWhiteSpace(ionToken))
        {
            throw new InvalidOperationException(
                "A Cesium Ion access token is required. Get a free token at https://ion.cesium.com/tokens");
        }

        if (mode != ImportModeSolids && mode != ImportModeDxf)
        {
            throw new InvalidOperationException($"Unsupported import mode '{mode}'.");
        }

        radiusMeters = Math.Max(50, Math.Min(5000, radiusMeters));

        string jobId = jobs.Start(async (ct, progress) =>
        {
            if (mode == ImportModeSolids && !NativeDracoMeshDecoder.IsAvailable())
            {
                throw new InvalidOperationException(MissingDracoMeshDecoder.MissingMessage);
            }

            CoordinateTransformer coordinateTransformer = new CoordinateTransformer(new CrsRegistry());

            progress.Report(new JobProgress { Phase = "preparing", Percent = 0, Message = "Resolving georeference…" });
            PlateauImportReferenceContext referenceContext = await ResolveReferenceContextAsync(coordinateTransformer).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            BoundingBoxDegrees bbox = ComputeBoundingBox(
                referenceContext.AnchorLatitude,
                referenceContext.AnchorLongitude,
                radiusMeters);

            progress.Report(new JobProgress { Phase = "resolving", Percent = 10, Message = "Connecting to Cesium Ion…" });
            CesiumTerrainHttpTransport transport = new CesiumTerrainHttpTransport();
            CesiumIonTilesetSource source = await CesiumIonTilesetEndpoint.ResolveAsync(
                CesiumIonTilesetEndpoint.OsmBuildingsAssetId,
                ionToken,
                transport,
                ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            EcefToProjectTransformer ecefTransformer = CreateEcefTransformer(coordinateTransformer, referenceContext);
            IDracoMeshDecoder dracoDecoder = NativeDracoMeshDecoder.IsAvailable()
                ? new NativeDracoMeshDecoder()
                : (IDracoMeshDecoder)new MissingDracoMeshDecoder();

            AuthenticatedPlateauHttpClient authenticatedClient = new AuthenticatedPlateauHttpClient(source.BearerToken ?? string.Empty);
            PlateauTilesetDownloader downloader = new PlateauTilesetDownloader(
                authenticatedClient,
                new GltfMeshDecoder(dracoDecoder),
                ecefTransformer);

            progress.Report(new JobProgress { Phase = "downloading", Percent = 20, Message = "Downloading OSM Buildings…" });
            PlateauTilesetModel buildings = await downloader.DownloadAsync(
                source.TilesetUrl,
                "OSM Buildings",
                bbox,
                new Progress<PlateauTilesetDownloadProgress>(p =>
                {
                    int percent = 20 + (int)Math.Round(Math.Max(0.0, Math.Min(1.0, p.Fraction)) * 60.0);
                    progress.Report(new JobProgress
                    {
                        Phase = "downloading",
                        Current = p.Completed,
                        Total = p.Total,
                        Percent = Math.Min(80, percent),
                        Message = p.CurrentItem
                    });
                }),
                ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            if (mode == ImportModeDxf)
            {
                return await ImportAsDxfAsync(buildings, referenceContext, progress, ct).ConfigureAwait(false);
            }

            progress.Report(new JobProgress { Phase = "importing", Percent = 85, Message = "Creating Revit geometry…" });
            PlateauTilesImporterResult result = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
            {
                using Transaction tx = new Transaction(doc, "Import OSM Buildings");
                tx.Start();
                try
                {
                    PlateauTilesImporter importer = new PlateauTilesImporter();
                    PlateauTilesImporterResult r = importer.Import(doc, buildings, PlateauOnlineGeometryMode.Lod2Untextured);
                    tx.Commit();
                    return r;
                }
                catch
                {
                    tx.RollBack();
                    throw;
                }
            }).ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "OSM Buildings import complete" });

            return (object?)new OsmBuildingsImportResponse
            {
                ImportedElements = result.ImportedElementCount,
                Groups = result.CreatedGroupCount,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "Imported {0} OSM building(s) in {1} group(s) within {2}m of the project anchor.",
                    result.ImportedElementCount,
                    result.CreatedGroupCount,
                    radiusMeters),
                Warnings = downloader.Warnings.Concat(result.Warnings).ToArray()
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static async Task<object?> ImportAsDxfAsync(
        PlateauTilesetModel buildings,
        PlateauImportReferenceContext referenceContext,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        PlateauOnlineFootprintBuilder footprintBuilder = new PlateauOnlineFootprintBuilder();
        List<string> warnings = new List<string>();
        List<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = footprintBuilder.Build(buildings, warnings).ToList();

        if (outlines.Count == 0)
        {
            throw new InvalidOperationException("No OSM building footprints were found in the selected area.");
        }

        progress.Report(new JobProgress { Phase = "building", Percent = 85, Message = "Writing 2D basemap DXF…" });
        PlateauContextDxfImporter importer = new PlateauContextDxfImporter();
        string tempFolder = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", "OsmBasemap_" + Guid.NewGuid().ToString("N"));
        string dxfPath = Path.Combine(tempFolder, "OSM Buildings Basemap.dxf");

        try
        {
            PlateauContextDxfImporter.DxfBuildResult build = importer.WriteOutlineDxf(
                outlines,
                new List<PlateauContextOutlinesDxfWriter.AreaFeature>(),
                new List<PlateauContextOutlinesDxfWriter.LineFeature>(),
                referenceContext,
                dxfPath,
                warnings);
            ct.ThrowIfCancellationRequested();

            if (build.FeatureCount == 0)
            {
                throw new InvalidOperationException("The selected area produced no 2D basemap geometry to import.");
            }

            progress.Report(new JobProgress { Phase = "importing", Percent = 92, Message = "Importing DXF basemap…" });
            await RevitContext.Instance.InvokeWithDocumentAsync(doc => importer.ImportDxf(doc, dxfPath)).ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "OSM basemap import complete" });

            return (object?)new OsmBuildingsImportResponse
            {
                ImportedElements = build.FeatureCount,
                Groups = 0,
                Summary = string.Format(
                    CultureInfo.InvariantCulture,
                    "Imported {0} OSM building footprint(s) as a 2D DXF basemap.",
                    build.FeatureCount),
                Warnings = build.Warnings.Distinct(StringComparer.Ordinal).ToArray()
            };
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempFolder))
                    Directory.Delete(tempFolder, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }

    private static async Task<PlateauImportReferenceContext> ResolveReferenceContextAsync(CoordinateTransformer coordinateTransformer)
    {
        PlateauImportReferenceContext? referenceContext = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GeoProjectInfoStorage geoStore = new GeoProjectInfoStorage();
            ModuleStateStorage moduleStateStore = new ModuleStateStorage();
            ProjectLocationReader reader = new ProjectLocationReader(geoStore, moduleStateStore: moduleStateStore);
            CurrentProjectStateSummary currentState = reader.Read(doc);
            RevitDocumentHandle handle = new RevitDocumentHandle(doc);
            GeoProjectInfo? info = geoStore.Load(handle);
            PlateauImportReferenceResolver resolver = new PlateauImportReferenceResolver(
                coordinateTransformer,
                new RevitPlateauImportLocalBasisProvider(doc));
            return resolver.Resolve(currentState, info, PlateauImportReferenceSource.CanonicalOrigin);
        }).ConfigureAwait(false);

        return referenceContext ?? throw new InvalidOperationException(
            "This project isn't georeferenced yet. Complete Georeference Setup before importing OSM Buildings.");
    }

    private static EcefToProjectTransformer CreateEcefTransformer(
        CoordinateTransformer coordinateTransformer,
        PlateauImportReferenceContext referenceContext)
    {
        return new EcefToProjectTransformer(
            coordinateTransformer,
            referenceContext.ProjectCrs,
            referenceContext.AnchorProjectedCoordinate,
            referenceContext.AnchorElevationMeters,
            referenceContext.AnchorXFeet,
            referenceContext.AnchorYFeet,
            referenceContext.AnchorZFeet,
            referenceContext.SharedEastToLocalX,
            referenceContext.SharedEastToLocalY,
            referenceContext.SharedNorthToLocalX,
            referenceContext.SharedNorthToLocalY);
    }

    private static BoundingBoxDegrees ComputeBoundingBox(double anchorLatitude, double anchorLongitude, double radiusMeters)
    {
        double latDelta = radiusMeters / EarthRadiusMeters * (180.0 / Math.PI);
        double lonDelta = radiusMeters / (EarthRadiusMeters * Math.Cos(anchorLatitude * Math.PI / 180.0)) * (180.0 / Math.PI);
        return new BoundingBoxDegrees(
            anchorLongitude - lonDelta,
            anchorLatitude - latDelta,
            anchorLongitude + lonDelta,
            anchorLatitude + latDelta);
    }
}
