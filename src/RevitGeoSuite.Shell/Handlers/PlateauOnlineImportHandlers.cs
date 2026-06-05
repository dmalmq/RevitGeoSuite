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
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.Core.Plateau.Mvt;
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

public sealed class PlateauOnlineCatalogHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public PlateauOnlineCatalogHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.onlineCatalog";

    public Task<object?> HandleAsync(object? payload)
    {
        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "loading", Percent = 5, Message = "Loading PLATEAU catalog…" });

            PlateauHttpClient httpClient = new PlateauHttpClient();
            PlateauApiClient apiClient = new PlateauApiClient(httpClient);
            PlateauCatalog catalog = await apiClient.FetchCatalogAsync(ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            progress.Report(new JobProgress { Phase = "indexing", Percent = 80, Message = "Indexing municipalities…" });

            PlateauOnlineArea[] areas = catalog.AreaOptions
                .Select(area => BuildArea(catalog, area))
                .OrderBy(area => area.DisplayLabel, StringComparer.Ordinal)
                .ToArray();

            ct.ThrowIfCancellationRequested();
            progress.Report(new JobProgress { Phase = "suggesting", Percent = 90, Message = "Resolving project location…" });
            PlateauOnlineSuggestion? suggestion = await TryResolveSuggestionAsync(apiClient, catalog, ct).ConfigureAwait(false);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Catalog loaded" });

            return (object?)new PlateauOnlineCatalogResponse
            {
                Areas = areas,
                Suggestion = suggestion,
                AreaCount = areas.Length,
                DatasetCount = catalog.Datasets.Count,
                DracoAvailable = NativeDracoMeshDecoder.IsAvailable(),
                DracoMessage = NativeDracoMeshDecoder.IsAvailable() ? string.Empty : MissingDracoMeshDecoder.MissingMessage
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static PlateauOnlineArea BuildArea(PlateauCatalog catalog, PlateauAreaOption area)
    {
        AreaSearchOption option = PlateauOnlineAreaSearch.BuildOption(area);
        double? latitude = null;
        double? longitude = null;
        if (MunicipalityCentroids.TryGet(area.Code, out double centroidLat, out double centroidLon))
        {
            latitude = centroidLat;
            longitude = centroidLon;
        }

        return new PlateauOnlineArea
        {
            Code = area.Code,
            Aliases = area.Aliases.ToArray(),
            Label = area.Label,
            Prefecture = area.Pref,
            City = area.City,
            Ward = area.Ward,
            DisplayLabel = option.DisplayLabel,
            CodeLabel = option.CodeLabel,
            SearchTokens = option.SearchTokens,
            HasBuildings = PlateauOnlineSuggestionResolver.HasBuildingDataset(catalog, area),
            Latitude = latitude,
            Longitude = longitude
        };
    }

    private static async Task<PlateauOnlineSuggestion?> TryResolveSuggestionAsync(
        PlateauApiClient apiClient,
        PlateauCatalog catalog,
        CancellationToken cancellationToken)
    {
        PlateauOnlineProjectPoint? projectPoint;
        try
        {
            CoordinateTransformer coordinateTransformer = new CoordinateTransformer(new CrsRegistry());
            projectPoint = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
            {
                GeoProjectInfoStorage geoStore = new GeoProjectInfoStorage();
                ModuleStateStorage moduleStateStore = new ModuleStateStorage();
                ProjectLocationReader reader = new ProjectLocationReader(geoStore, moduleStateStore: moduleStateStore);
                CurrentProjectStateSummary currentState = reader.Read(doc);
                RevitDocumentHandle handle = new RevitDocumentHandle(doc);
                GeoProjectInfo? info = geoStore.Load(handle);
                return PlateauOnlineSuggestionResolver.ResolveProjectPoint(currentState, info, coordinateTransformer);
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (projectPoint is null)
        {
            return null;
        }

        string? municipalityCode;
        try
        {
            municipalityCode = await apiClient
                .ReverseGeocodeMunicipalityCodeAsync(projectPoint.Latitude, projectPoint.Longitude, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            municipalityCode = null;
        }

        PlateauOnlineSamplePoint[] samplePoints = PlateauOnlineNearbyAreaDetector.GenerateSamplePoints(
            projectPoint.Latitude, projectPoint.Longitude);

        PlateauOnlineSampleResult[] sampleResults;
        try
        {
            Task<string?>[] sampleTasks = samplePoints
                .Select(sp => apiClient.ReverseGeocodeMunicipalityCodeAsync(sp.Latitude, sp.Longitude, cancellationToken))
                .ToArray();
            string?[] sampleCodes = await Task.WhenAll(sampleTasks).ConfigureAwait(false);
            sampleResults = samplePoints
                .Select((sp, i) => new PlateauOnlineSampleResult(sp, sampleCodes[i]))
                .ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            sampleResults = Array.Empty<PlateauOnlineSampleResult>();
        }

        PlateauOnlineNearbyArea[] nearbyAreas = PlateauOnlineNearbyAreaDetector.ResolveNearbyAreas(
            catalog, municipalityCode, sampleResults, projectPoint);

        if (nearbyAreas.Length == 0)
        {
            return null;
        }

        PlateauOnlineNearbyArea primary = nearbyAreas[0];
        return new PlateauOnlineSuggestion
        {
            AreaCode = primary.Area.Code,
            AreaLabel = primary.Area.Label,
            DisplayLabel = primary.DisplayLabel,
            CodeLabel = primary.CodeLabel,
            Source = projectPoint.Source,
            Message = projectPoint.Message,
            Latitude = projectPoint.Latitude,
            Longitude = projectPoint.Longitude,
            Areas = nearbyAreas
                .Select(a => new PlateauOnlineSuggestionArea
                {
                    AreaCode = a.Area.Code,
                    DisplayLabel = a.DisplayLabel,
                    CodeLabel = a.CodeLabel,
                    NearestDistanceMeters = a.NearestDistanceMeters
                })
                .ToArray()
        };
    }
}

public sealed class PlateauOnlineGridsHandler : IRpcHandler
{
    private const int MaxGeneratedGridCount = 5000;

    private readonly JobManager jobs;

    public PlateauOnlineGridsHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.onlineGrids";

    public Task<object?> HandleAsync(object? payload)
    {
        JObject? request = payload as JObject;
        string[] areaCodes = ParseAreaCodes(request);
        if (areaCodes.Length == 0)
        {
            throw new InvalidOperationException("At least one area code is required.");
        }

        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "catalog", Percent = 10, Message = "Loading PLATEAU catalog…" });

            PlateauHttpClient httpClient = new PlateauHttpClient();
            PlateauApiClient apiClient = new PlateauApiClient(httpClient);
            PlateauCatalog catalog = await apiClient.FetchCatalogAsync(ct).ConfigureAwait(false);

            PlateauAreaGeometryService geometryService = new PlateauAreaGeometryService(httpClient);

            SortedDictionary<string, GridMergeEntry> mergedGrids = new SortedDictionary<string, GridMergeEntry>(StringComparer.Ordinal);
            List<string> resolvedAreaCodes = new List<string>(areaCodes.Length);
            List<string> resolvedAreaLabels = new List<string>(areaCodes.Length);
            string primaryAreaCode = areaCodes[0];
            string primaryAreaLabel = string.Empty;
            string datasetLod = string.Empty;
            bool? datasetTexture = null;

            for (int areaIndex = 0; areaIndex < areaCodes.Length; areaIndex++)
            {
                string code = areaCodes[areaIndex];
                PlateauAreaOption area = catalog.AreaOptions.FirstOrDefault(candidate => candidate.MatchesCode(code))
                    ?? throw new InvalidOperationException($"PLATEAU area '{code}' was not found in the online catalog.");
                PlateauDatasetEntry bldgEntry = SelectBuildingDataset(catalog, area);
                resolvedAreaCodes.Add(area.Code);
                resolvedAreaLabels.Add(area.Label);

                if (areaIndex == 0)
                {
                    primaryAreaCode = area.Code;
                    primaryAreaLabel = area.Label;
                    datasetLod = bldgEntry.Lod ?? string.Empty;
                    datasetTexture = bldgEntry.Texture;
                }

                int areaPercent = 20 + (int)Math.Round((areaIndex + 1.0) / areaCodes.Length * 50.0);
                progress.Report(new JobProgress { Phase = "bounds", Percent = areaPercent, Message = $"Loading grid bounds for {area.Label}…" });

                PlateauAreaBounds? bounds = await geometryService.GetBoundsAsync(area, catalog, ct).ConfigureAwait(false);
                if (bounds is null) continue;

                MergeGridsForArea(mergedGrids, bounds, bldgEntry, area.Code, area.Label);
            }

            progress.Report(new JobProgress { Phase = "grids", Percent = 85, Message = "Building selectable grid overlay…" });

            PlateauOnlineGrid[] grids = mergedGrids
                .Select(pair => new PlateauOnlineGrid
                {
                    Id = pair.Key,
                    Label = pair.Key,
                    Lod = pair.Value.Lod,
                    Texture = pair.Value.Texture,
                    Geometry = BuildTileGeometry(pair.Value.Bounds),
                    AreaCodes = pair.Value.AreaCodes.ToArray(),
                    AreaLabels = pair.Value.AreaLabels.ToArray()
                })
                .ToArray();

            if (grids.Length == 0)
            {
                throw new InvalidOperationException("No selectable PLATEAU grids were found for the selected areas.");
            }

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Online grids loaded" });
            return (object?)new PlateauOnlineGridsResponse
            {
                AreaCode = primaryAreaCode,
                AreaLabel = primaryAreaLabel,
                AreaCodes = resolvedAreaCodes.ToArray(),
                AreaLabels = resolvedAreaLabels.ToArray(),
                Grids = grids,
                GridCount = grids.Length,
                DatasetLod = datasetLod,
                DatasetTexture = datasetTexture
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static string[] ParseAreaCodes(JObject? request)
    {
        string[] multiCodes = request?["areaCodes"]?
            .Values<string>()
            .Select(c => c?.Trim() ?? string.Empty)
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();

        if (multiCodes.Length > 0) return multiCodes;

        string? single = request?.Value<string>("areaCode");
        if (!string.IsNullOrWhiteSpace(single)) return new[] { single!.Trim() };

        return Array.Empty<string>();
    }

    private static void MergeGridsForArea(
        SortedDictionary<string, GridMergeEntry> mergedGrids,
        PlateauAreaBounds bounds,
        PlateauDatasetEntry dataset,
        string areaCode,
        string areaLabel)
    {
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        int guard = 0;

        double latCursor = bounds.SouthDeg;
        while (latCursor < bounds.NorthDeg && guard++ < MaxGeneratedGridCount)
        {
            double sampleLat = GetSampleCoordinate(bounds.SouthDeg, bounds.NorthDeg, latCursor);
            MeshBounds rowBounds = meshCalculator.GetBounds(meshCalculator.Calculate(sampleLat, GetSampleCoordinate(bounds.WestDeg, bounds.EastDeg, bounds.WestDeg)));

            double lonCursor = bounds.WestDeg;
            while (lonCursor < bounds.EastDeg && guard++ < MaxGeneratedGridCount)
            {
                double sampleLon = GetSampleCoordinate(bounds.WestDeg, bounds.EastDeg, lonCursor);
                MeshCode meshCode = meshCalculator.Calculate(sampleLat, sampleLon, JapanMeshLevel.Tertiary);
                MeshBounds meshBounds = meshCalculator.GetBounds(meshCode);
                if (Intersects(meshBounds, bounds))
                {
                    if (mergedGrids.TryGetValue(meshCode.Value, out GridMergeEntry? existing))
                    {
                        if (!existing.AreaCodes.Contains(areaCode, StringComparer.Ordinal))
                        {
                            existing.AreaCodes.Add(areaCode);
                            existing.AreaLabels.Add(areaLabel);
                        }
                    }
                    else
                    {
                        mergedGrids[meshCode.Value] = new GridMergeEntry
                        {
                            Bounds = meshBounds,
                            Lod = dataset.Lod ?? string.Empty,
                            Texture = dataset.Texture,
                            AreaCodes = new List<string> { areaCode },
                            AreaLabels = new List<string> { areaLabel }
                        };
                    }
                }

                lonCursor = Math.Max(meshBounds.EastLongitude, lonCursor + 1e-6);
            }

            latCursor = Math.Max(rowBounds.NorthLatitude, latCursor + 1e-6);
        }
    }

    private static PlateauDatasetEntry SelectBuildingDataset(PlateauCatalog catalog, PlateauAreaOption area)
    {
        PlateauDatasetSelector selector = new PlateauDatasetSelector
        {
            TexturePreference = PlateauTexturePreference.PreferUntextured
        };

        PlateauDatasetEntry? entry = selector.SelectByTypes(catalog, area, new[] { "bldg" }).FirstOrDefault();
        return entry ?? throw new InvalidOperationException($"No bldg dataset was found for {area.Label}.");
    }

    private sealed class GridMergeEntry
    {
        public MeshBounds Bounds { get; set; }
        public string Lod { get; set; } = string.Empty;
        public bool? Texture { get; set; }
        public List<string> AreaCodes { get; set; } = new();
        public List<string> AreaLabels { get; set; } = new();
    }

    private static double GetSampleCoordinate(double min, double max, double cursor)
    {
        if (max <= min)
        {
            return min;
        }

        const double epsilon = 1e-9;
        double sample = cursor + epsilon;
        if (sample <= min)
        {
            sample = min + Math.Min(epsilon, (max - min) / 2d);
        }

        if (sample >= max)
        {
            sample = min + ((max - min) / 2d);
        }

        return sample;
    }

    private static bool Intersects(MeshBounds meshBounds, PlateauAreaBounds areaBounds)
    {
        return meshBounds.EastLongitude > areaBounds.WestDeg
            && meshBounds.WestLongitude < areaBounds.EastDeg
            && meshBounds.NorthLatitude > areaBounds.SouthDeg
            && meshBounds.SouthLatitude < areaBounds.NorthDeg;
    }

    private static object BuildTileGeometry(MeshBounds bounds)
    {
        return new
        {
            type = "Polygon",
            coordinates = new[]
            {
                new[]
                {
                    new[] { bounds.WestLongitude, bounds.SouthLatitude },
                    new[] { bounds.EastLongitude, bounds.SouthLatitude },
                    new[] { bounds.EastLongitude, bounds.NorthLatitude },
                    new[] { bounds.WestLongitude, bounds.NorthLatitude },
                    new[] { bounds.WestLongitude, bounds.SouthLatitude }
                }
            }
        };
    }
}

public sealed class PlateauOnlineImportHandler : IRpcHandler
{
    private const double FeetToMeters = 0.3048d;
    private const string ImportModeSolids = "solids";
    private const string ImportModeDxf = "dxf";
    private const string CategoryBuildings = "buildings";
    private const string CategoryRoads = "roads";
    private const string CategoryLandUse = "landuse";

    private readonly JobManager jobs;

    public PlateauOnlineImportHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.onlineImport";

    public Task<object?> HandleAsync(object? payload)
    {
        JObject? request = payload as JObject;
        AreaGridSelection[] selections = ParseAreaSelections(request);
        OnlineImportOptions options = ParseImportOptions(request);
        if (selections.Length == 0)
        {
            throw new InvalidOperationException("Select at least one PLATEAU online grid before importing.");
        }

        ValidateGridIds(selections.SelectMany(selection => selection.GridIds).Distinct(StringComparer.Ordinal).ToArray());

        string jobId = jobs.Start(async (ct, progress) =>
        {
            if ((options.Mode == ImportModeSolids || options.IncludeBuildings) && !NativeDracoMeshDecoder.IsAvailable())
            {
                throw new InvalidOperationException(MissingDracoMeshDecoder.MissingMessage);
            }

            CoordinateTransformer coordinateTransformer = new CoordinateTransformer(new CrsRegistry());

            progress.Report(new JobProgress { Phase = "preparing", Percent = 0, Message = "Resolving georeference…" });
            PlateauImportReferenceContext referenceContext = await ResolveReferenceContextAsync(coordinateTransformer).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            progress.Report(new JobProgress { Phase = "catalog", Percent = 10, Message = "Loading PLATEAU catalog…" });
            PlateauHttpClient apiHttpClient = new PlateauHttpClient();
            PlateauApiClient apiClient = new PlateauApiClient(apiHttpClient);
            PlateauCatalog catalog = await apiClient.FetchCatalogAsync(ct).ConfigureAwait(false);

            EcefToProjectTransformer ecefTransformer = CreateOnlineEcefTransformer(coordinateTransformer, referenceContext);

            if (options.Mode == ImportModeDxf)
            {
                return await ImportOnlineBasemapAsync(
                    selections,
                    catalog,
                    referenceContext,
                    coordinateTransformer,
                    ecefTransformer,
                    options,
                    progress,
                    ct).ConfigureAwait(false);
            }

            IDracoMeshDecoder dracoDecoder = new NativeDracoMeshDecoder();
            List<AreaDownloadResult> downloads = new List<AreaDownloadResult>(selections.Length);
            for (int index = 0; index < selections.Length; index++)
            {
                AreaGridSelection selection = selections[index];
                PlateauAreaOption area = catalog.AreaOptions.FirstOrDefault(candidate => candidate.MatchesCode(selection.AreaCode))
                    ?? throw new InvalidOperationException($"PLATEAU area '{selection.AreaCode}' was not found in the online catalog.");

                PlateauDatasetEntry bldgEntry = SelectBuildingDataset(catalog, area);
                ct.ThrowIfCancellationRequested();

                int basePercent = 20 + (int)Math.Round(index / (double)selections.Length * 60.0);
                int spanPercent = Math.Max(1, (int)Math.Round(60.0 / selections.Length));
                progress.Report(new JobProgress { Phase = "downloading", Percent = basePercent, Message = $"Downloading buildings for {area.Label}…" });

                PlateauTilesetDownloader downloader = new PlateauTilesetDownloader(
                    new PlateauHttpClient(),
                    new GltfMeshDecoder(dracoDecoder),
                    ecefTransformer);

                PlateauTilesetModel buildings = await downloader.DownloadAsync(
                    bldgEntry,
                    area.Code,
                    selection.GridIds,
                    new Progress<PlateauTilesetDownloadProgress>(p =>
                    {
                        int percent = basePercent + (int)Math.Round(Math.Max(0.0, Math.Min(1.0, p.Fraction)) * spanPercent);
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

                buildings = FilterBuildingsToSelectedGrids(buildings, selection.GridIds, referenceContext, coordinateTransformer);
                downloads.Add(new AreaDownloadResult(area, buildings, downloader.Warnings.ToArray()));
            }

            ct.ThrowIfCancellationRequested();
            progress.Report(new JobProgress { Phase = "importing", Percent = 85, Message = "Creating Revit geometry…" });

            PlateauOnlineImportAreaResult[] areaResults = await ImportOnRevitThreadAsync(
                downloads,
                PlateauOnlineGeometryMode.Lod2Untextured).ConfigureAwait(false);
            int importedElements = areaResults.Sum(result => result.ImportedElements);
            int groups = areaResults.Sum(result => result.Groups);
            string[] warnings = areaResults.SelectMany(result => result.Warnings).ToArray();

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Online import complete" });

            return (object?)new PlateauOnlineImportResponse
            {
                AreaCode = areaResults.Length > 0 ? areaResults[0].AreaCode : string.Empty,
                AreaLabel = areaResults.Length > 0 ? areaResults[0].AreaLabel : string.Empty,
                ImportedElements = importedElements,
                Groups = groups,
                Summary = $"Imported {importedElements} element(s) in {groups} group(s) across {areaResults.Length} dataset(s).",
                Warnings = warnings,
                AreaResults = areaResults,
                Mode = ImportModeSolids
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static OnlineImportOptions ParseImportOptions(JObject? request)
    {
        string mode = request?.Value<string>("mode")?.Trim().ToLowerInvariant() ?? ImportModeSolids;
        if (mode.Length == 0)
        {
            mode = ImportModeSolids;
        }

        if (mode != ImportModeSolids && mode != ImportModeDxf)
        {
            throw new InvalidOperationException($"Unsupported PLATEAU online import mode '{mode}'.");
        }

        bool hasExplicitCategories = request?["categories"] is not null;
        string[] categories = request?["categories"]?
            .Values<string>()
            .Select(value => value?.Trim().ToLowerInvariant() ?? string.Empty)
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();

        if (categories.Length == 0 && !hasExplicitCategories)
        {
            categories = mode == ImportModeDxf
                ? new[] { CategoryBuildings, CategoryRoads, CategoryLandUse }
                : new[] { CategoryBuildings };
        }

        HashSet<string> categorySet = new HashSet<string>(categories, StringComparer.Ordinal);
        string[] supported = { CategoryBuildings, CategoryRoads, CategoryLandUse };
        string? unsupported = categorySet.FirstOrDefault(category => !supported.Contains(category, StringComparer.Ordinal));
        if (unsupported is not null)
        {
            throw new InvalidOperationException($"Unsupported PLATEAU online import category '{unsupported}'.");
        }

        if (mode == ImportModeDxf && categorySet.Count == 0)
        {
            throw new InvalidOperationException("Select at least one PLATEAU online basemap category.");
        }

        return new OnlineImportOptions(
            mode,
            categorySet.Contains(CategoryBuildings),
            categorySet.Contains(CategoryRoads),
            categorySet.Contains(CategoryLandUse));
    }

    private static async Task<object?> ImportOnlineBasemapAsync(
        IReadOnlyList<AreaGridSelection> selections,
        PlateauCatalog catalog,
        PlateauImportReferenceContext referenceContext,
        ICoordinateTransformer coordinateTransformer,
        EcefToProjectTransformer ecefTransformer,
        OnlineImportOptions options,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        List<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = new List<PlateauContextOutlinesDxfWriter.OutlineFeature>();
        List<PlateauContextOutlinesDxfWriter.AreaFeature> areas = new List<PlateauContextOutlinesDxfWriter.AreaFeature>();
        List<PlateauContextOutlinesDxfWriter.LineFeature> lines = new List<PlateauContextOutlinesDxfWriter.LineFeature>();
        List<string> allWarnings = new List<string>();
        List<PlateauOnlineImportAreaResult> areaResults = new List<PlateauOnlineImportAreaResult>(selections.Count);

        IDracoMeshDecoder? dracoDecoder = options.IncludeBuildings ? new NativeDracoMeshDecoder() : null;
        PlateauOnlineFootprintBuilder footprintBuilder = new PlateauOnlineFootprintBuilder();

        for (int index = 0; index < selections.Count; index++)
        {
            AreaGridSelection selection = selections[index];
            PlateauAreaOption area = catalog.AreaOptions.FirstOrDefault(candidate => candidate.MatchesCode(selection.AreaCode))
                ?? throw new InvalidOperationException($"PLATEAU area '{selection.AreaCode}' was not found in the online catalog.");

            int basePercent = 20 + (int)Math.Round(index / (double)selections.Count * 60.0);
            int spanPercent = Math.Max(1, (int)Math.Round(60.0 / selections.Count));
            List<string> areaWarnings = new List<string>();
            int startOutlineCount = outlines.Count;
            int startAreaCount = areas.Count;
            int startLineCount = lines.Count;

            MvtGridBounds[] gridBounds = BuildMvtGridBounds(selection.GridIds);

            if (options.IncludeBuildings)
            {
                progress.Report(new JobProgress { Phase = "downloading", Percent = basePercent, Message = $"Downloading building footprints for {area.Label}…" });
                PlateauDatasetEntry bldgEntry = SelectBuildingDataset(catalog, area);
                PlateauTilesetDownloader downloader = new PlateauTilesetDownloader(
                    new PlateauHttpClient(),
                    new GltfMeshDecoder(dracoDecoder!),
                    ecefTransformer);

                PlateauTilesetModel buildings = await downloader.DownloadAsync(
                    bldgEntry,
                    area.Code,
                    selection.GridIds,
                    new Progress<PlateauTilesetDownloadProgress>(p =>
                    {
                        int percent = basePercent + (int)Math.Round(Math.Max(0.0, Math.Min(1.0, p.Fraction)) * Math.Max(1, spanPercent / 2.0));
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

                buildings = FilterBuildingsToSelectedGrids(buildings, selection.GridIds, referenceContext, coordinateTransformer);
                areaWarnings.AddRange(downloader.Warnings);
                outlines.AddRange(footprintBuilder.Build(buildings, areaWarnings));
            }

            if (options.IncludeRoads)
            {
                progress.Report(new JobProgress { Phase = "downloading", Percent = Math.Min(80, basePercent + (spanPercent / 2)), Message = $"Downloading road basemap for {area.Label}…" });
                await AddMvtCategoryAsync(
                    catalog,
                    area,
                    gridBounds,
                    ecefTransformer,
                    "tran",
                    PlateauOnlineBasemapFeatureBuilder.RoadsLayer,
                    "roads",
                    areas,
                    lines,
                    areaWarnings,
                    ct).ConfigureAwait(false);
            }

            if (options.IncludeLandUse)
            {
                progress.Report(new JobProgress { Phase = "downloading", Percent = Math.Min(80, basePercent + spanPercent), Message = $"Downloading land-use basemap for {area.Label}…" });
                await AddMvtCategoryAsync(
                    catalog,
                    area,
                    gridBounds,
                    ecefTransformer,
                    "luse",
                    PlateauOnlineBasemapFeatureBuilder.LandUseLayer,
                    "land use",
                    areas,
                    lines,
                    areaWarnings,
                    ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            int areaFeatureCount =
                (outlines.Count - startOutlineCount)
                + (areas.Count - startAreaCount)
                + (lines.Count - startLineCount);
            allWarnings.AddRange(areaWarnings);
            areaResults.Add(new PlateauOnlineImportAreaResult
            {
                AreaCode = area.Code,
                AreaLabel = area.Label,
                ImportedElements = areaFeatureCount,
                Groups = 0,
                Warnings = areaWarnings.ToArray()
            });
        }

        progress.Report(new JobProgress { Phase = "building", Percent = 85, Message = "Writing 2D basemap DXF…" });
        PlateauContextDxfImporter importer = new PlateauContextDxfImporter();
        string tempFolder = Path.Combine(Path.GetTempPath(), "RevitGeoSuite", "OnlineBasemap_" + Guid.NewGuid().ToString("N"));
        string dxfPath = Path.Combine(tempFolder, "PLATEAU Online Basemap.dxf");
        bool imported = false;

        try
        {
            PlateauContextDxfImporter.DxfBuildResult build = importer.WriteOutlineDxf(
                outlines,
                areas,
                lines,
                referenceContext,
                dxfPath,
                allWarnings);
            ct.ThrowIfCancellationRequested();

            if (build.FeatureCount == 0)
            {
                throw new InvalidOperationException(
                    build.Warnings.FirstOrDefault()
                    ?? "The selected PLATEAU online grids produced no 2D basemap geometry to import.");
            }

            progress.Report(new JobProgress { Phase = "importing", Percent = 92, Message = "Importing DXF basemap…" });
            await RevitContext.Instance.InvokeWithDocumentAsync(doc => importer.ImportDxf(doc, dxfPath)).ConfigureAwait(false);
            imported = true;

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Online basemap import complete" });

            string[] warnings = build.Warnings.Distinct(StringComparer.Ordinal).ToArray();
            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Imported {0} online PLATEAU basemap feature(s) as a single 2D DXF from {1} area(s).",
                build.FeatureCount,
                areaResults.Count);

            return (object?)new PlateauOnlineImportResponse
            {
                AreaCode = areaResults.Count > 0 ? areaResults[0].AreaCode : string.Empty,
                AreaLabel = areaResults.Count > 0 ? areaResults[0].AreaLabel : string.Empty,
                ImportedElements = build.FeatureCount,
                Groups = 0,
                Summary = summary,
                Warnings = warnings,
                AreaResults = areaResults.ToArray(),
                Mode = ImportModeDxf
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

    private static async Task AddMvtCategoryAsync(
        PlateauCatalog catalog,
        PlateauAreaOption area,
        IReadOnlyList<MvtGridBounds> gridBounds,
        EcefToProjectTransformer ecefTransformer,
        string typeEn,
        string layer,
        string label,
        ICollection<PlateauContextOutlinesDxfWriter.AreaFeature> areas,
        ICollection<PlateauContextOutlinesDxfWriter.LineFeature> lines,
        ICollection<string> warnings,
        CancellationToken ct)
    {
        if (gridBounds.Count == 0)
        {
            warnings.Add($"Skipped {label} MVT for {area.Label}: no selected grid bounds were available.");
            return;
        }

        PlateauDatasetEntry? dataset = SelectMvtDataset(catalog, area, typeEn);
        if (dataset is null)
        {
            warnings.Add($"No {label} MVT dataset was found for {area.Label}.");
            return;
        }

        string? tileJsonUrl = dataset.PreferredUrl;
        if (string.IsNullOrWhiteSpace(tileJsonUrl))
        {
            warnings.Add($"Skipped {label} MVT for {area.Label}: dataset has no TileJSON URL.");
            return;
        }

        MvtTileJson tileJson;
        PlateauHttpClient httpClient = new PlateauHttpClient();
        try
        {
            string json = await httpClient.GetStringAsync(new Uri(tileJsonUrl), ct).ConfigureAwait(false);
            tileJson = MvtTileJson.Parse(json);
        }
        catch (Exception ex) when (!(ex is OperationCanceledException))
        {
            warnings.Add($"Skipped {label} MVT for {area.Label}: TileJSON could not be loaded ({ex.Message}).");
            return;
        }

        int startAreaCount = areas.Count;
        int startLineCount = lines.Count;
        MvtFeatureDownloader downloader = new MvtFeatureDownloader(httpClient);
        MvtProjectedFeatures projected = await downloader.DownloadAsync(
            tileJson,
            gridBounds,
            ecefTransformer,
            warnings,
            zoomCap: 16,
            maxTiles: 4096,
            cancellationToken: ct).ConfigureAwait(false);

        PlateauOnlineBasemapFeatureBuilder.AddMvtFeatures(
            projected,
            layer,
            string.Concat(area.Code, "-", typeEn),
            areas,
            lines,
            warnings);

        if (areas.Count == startAreaCount && lines.Count == startLineCount)
        {
            warnings.Add($"No {label} MVT geometry intersected the selected grids for {area.Label}.");
        }
    }

    private static PlateauDatasetEntry? SelectMvtDataset(PlateauCatalog catalog, PlateauAreaOption area, string typeEn)
    {
        PlateauDatasetSelector selector = new PlateauDatasetSelector
        {
            TexturePreference = PlateauTexturePreference.PreferUntextured
        };

        return selector.SelectMvtByTypes(catalog, area, new[] { typeEn }).FirstOrDefault();
    }

    private static MvtGridBounds[] BuildMvtGridBounds(IReadOnlyCollection<string> gridIds)
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        return gridIds
            .Where(gridId => !string.IsNullOrWhiteSpace(gridId))
            .Distinct(StringComparer.Ordinal)
            .Select(gridId => calculator.GetBounds(new MeshCode { Value = gridId }))
            .Select(bounds => new MvtGridBounds(
                bounds.WestLongitude,
                bounds.SouthLatitude,
                bounds.EastLongitude,
                bounds.NorthLatitude))
            .ToArray();
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
            // Best-effort cleanup; Document.Import embeds the CAD import geometry in the model.
        }
    }

    private static AreaGridSelection[] ParseAreaSelections(JObject? request)
    {
        List<AreaGridSelection> result = new List<AreaGridSelection>();
        JToken? areaSelectionsToken = request?["areaSelections"];
        if (areaSelectionsToken is not null)
        {
            foreach (JObject selection in areaSelectionsToken.OfType<JObject>())
            {
                string? areaCode = selection.Value<string>("areaCode");
                string[] gridIds = ParseGridIds(selection);
                if (!string.IsNullOrWhiteSpace(areaCode) && gridIds.Length > 0)
                {
                    result.Add(new AreaGridSelection(areaCode!.Trim(), gridIds));
                }
            }
        }

        if (result.Count == 0)
        {
            string[] gridIds = ParseGridIds(request);
            if (gridIds.Length == 0)
            {
                return Array.Empty<AreaGridSelection>();
            }

            string[] areaCodes = request?["areaCodes"]?
                .Values<string>()
                .Select(areaCode => areaCode?.Trim() ?? string.Empty)
                .Where(areaCode => areaCode.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToArray()
                ?? Array.Empty<string>();

            if (areaCodes.Length == 0)
            {
                string? areaCode = request?.Value<string>("areaCode");
                if (!string.IsNullOrWhiteSpace(areaCode))
                {
                    areaCodes = new[] { areaCode!.Trim() };
                }
            }

            result.AddRange(areaCodes.Select(areaCode => new AreaGridSelection(areaCode, gridIds)));
        }

        return result
            .GroupBy(selection => selection.AreaCode, StringComparer.Ordinal)
            .Select(group => new AreaGridSelection(
                group.Key,
                group.SelectMany(selection => selection.GridIds).Distinct(StringComparer.Ordinal).ToArray()))
            .Where(selection => selection.GridIds.Length > 0)
            .ToArray();
    }

    private static string[] ParseGridIds(JToken? token)
    {
        return token?["gridIds"]?
            .Values<string>()
            .Select(gridId => gridId?.Trim() ?? string.Empty)
            .Where(gridId => gridId.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray()
            ?? Array.Empty<string>();
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
            "This project isn't georeferenced yet. Complete Georeference Setup before importing PLATEAU Online data.");
    }

    private static PlateauDatasetEntry SelectBuildingDataset(PlateauCatalog catalog, PlateauAreaOption area)
    {
        PlateauDatasetSelector selector = new PlateauDatasetSelector
        {
            TexturePreference = PlateauTexturePreference.PreferUntextured
        };

        PlateauDatasetEntry? entry = selector.SelectByTypes(catalog, area, new[] { "bldg" }).FirstOrDefault();
        return entry ?? throw new InvalidOperationException($"No bldg dataset was found for {area.Label}.");
    }

    private static void ValidateGridIds(IReadOnlyCollection<string> gridIds)
    {
        JapanMeshCalculator calculator = new JapanMeshCalculator();
        foreach (string gridId in gridIds)
        {
            try
            {
                _ = calculator.GetBounds(new MeshCode { Value = gridId });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Invalid PLATEAU grid '{gridId}': {ex.Message}", ex);
            }
        }
    }

    private static PlateauTilesetModel FilterBuildingsToSelectedGrids(
        PlateauTilesetModel buildings,
        IReadOnlyCollection<string> selectedGridIds,
        PlateauImportReferenceContext referenceContext,
        ICoordinateTransformer coordinateTransformer)
    {
        if (selectedGridIds.Count == 0)
        {
            return buildings;
        }

        HashSet<string> selected = new HashSet<string>(selectedGridIds, StringComparer.Ordinal);
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        List<PlateauTilesetFeature> features = buildings.Features
            .Where(feature => FeatureTouchesSelectedGrid(feature, selected, meshCalculator, referenceContext, coordinateTransformer))
            .ToList();

        return new PlateauTilesetModel(
            buildings.SourceUrl,
            buildings.TypeEn,
            buildings.Lod,
            buildings.Texture,
            buildings.AreaCode,
            features);
    }

    private static bool FeatureTouchesSelectedGrid(
        PlateauTilesetFeature feature,
        HashSet<string> selectedGridIds,
        JapanMeshCalculator meshCalculator,
        PlateauImportReferenceContext referenceContext,
        ICoordinateTransformer coordinateTransformer)
    {
        foreach (PlateauTilesetTriangle triangle in feature.Triangles)
        {
            if (PointTouchesSelectedGrid(triangle.A, selectedGridIds, meshCalculator, referenceContext, coordinateTransformer)
                || PointTouchesSelectedGrid(triangle.B, selectedGridIds, meshCalculator, referenceContext, coordinateTransformer)
                || PointTouchesSelectedGrid(triangle.C, selectedGridIds, meshCalculator, referenceContext, coordinateTransformer))
            {
                return true;
            }
        }

        return false;
    }

    private static bool PointTouchesSelectedGrid(
        Vector3d projectPointMeters,
        HashSet<string> selectedGridIds,
        JapanMeshCalculator meshCalculator,
        PlateauImportReferenceContext referenceContext,
        ICoordinateTransformer coordinateTransformer)
    {
        if (!TryConvertProjectPointToGeographic(
            projectPointMeters,
            referenceContext,
            coordinateTransformer,
            out GeographicCoordinate geographic))
        {
            // A degenerate local basis should not silently drop imported data.
            return true;
        }

        string meshId = meshCalculator.Calculate(geographic.Latitude, geographic.Longitude, JapanMeshLevel.Tertiary).Value;
        return selectedGridIds.Contains(meshId);
    }

    private static bool TryConvertProjectPointToGeographic(
        Vector3d projectPointMeters,
        PlateauImportReferenceContext referenceContext,
        ICoordinateTransformer coordinateTransformer,
        out GeographicCoordinate geographic)
    {
        geographic = default;

        double deltaLocalX = projectPointMeters.X - (referenceContext.AnchorXFeet * FeetToMeters);
        double deltaLocalY = projectPointMeters.Y - (referenceContext.AnchorYFeet * FeetToMeters);
        double determinant =
            (referenceContext.SharedEastToLocalX * referenceContext.SharedNorthToLocalY)
            - (referenceContext.SharedNorthToLocalX * referenceContext.SharedEastToLocalY);
        if (Math.Abs(determinant) < 1e-12)
        {
            return false;
        }

        double deltaEast =
            ((deltaLocalX * referenceContext.SharedNorthToLocalY)
            - (referenceContext.SharedNorthToLocalX * deltaLocalY))
            / determinant;
        double deltaNorth =
            ((referenceContext.SharedEastToLocalX * deltaLocalY)
            - (deltaLocalX * referenceContext.SharedEastToLocalY))
            / determinant;

        ProjectedCoordinate projected = new ProjectedCoordinate(
            referenceContext.AnchorProjectedCoordinate.Easting + deltaEast,
            referenceContext.AnchorProjectedCoordinate.Northing + deltaNorth);
        geographic = coordinateTransformer.Unproject(projected, referenceContext.ProjectCrs);
        return true;
    }

    private static EcefToProjectTransformer CreateOnlineEcefTransformer(
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

    private static Task<PlateauOnlineImportAreaResult[]> ImportOnRevitThreadAsync(
        IReadOnlyList<AreaDownloadResult> downloads,
        PlateauOnlineGeometryMode mode)
    {
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            using Transaction tx = new Transaction(doc, "Import PLATEAU Online");
            tx.Start();
            try
            {
                PlateauTilesImporter importer = new PlateauTilesImporter();
                List<PlateauOnlineImportAreaResult> results = new List<PlateauOnlineImportAreaResult>(downloads.Count);
                foreach (AreaDownloadResult download in downloads)
                {
                    PlateauTilesImporterResult result = importer.Import(doc, download.Buildings, mode);
                    results.Add(new PlateauOnlineImportAreaResult
                    {
                        AreaCode = download.Area.Code,
                        AreaLabel = download.Area.Label,
                        ImportedElements = result.ImportedElementCount,
                        Groups = result.CreatedGroupCount,
                        Warnings = download.DownloadWarnings.Concat(result.Warnings).ToArray()
                    });
                }

                tx.Commit();
                return results.ToArray();
            }
            catch
            {
                tx.RollBack();
                throw;
            }
        });
    }

    private sealed class OnlineImportOptions
    {
        public OnlineImportOptions(string mode, bool includeBuildings, bool includeRoads, bool includeLandUse)
        {
            Mode = mode ?? ImportModeSolids;
            IncludeBuildings = includeBuildings;
            IncludeRoads = includeRoads;
            IncludeLandUse = includeLandUse;
        }

        public string Mode { get; }

        public bool IncludeBuildings { get; }

        public bool IncludeRoads { get; }

        public bool IncludeLandUse { get; }
    }

    private sealed class AreaGridSelection
    {
        public AreaGridSelection(string areaCode, IReadOnlyCollection<string> gridIds)
        {
            AreaCode = areaCode ?? string.Empty;
            GridIds = gridIds?.ToArray() ?? Array.Empty<string>();
        }

        public string AreaCode { get; }

        public string[] GridIds { get; }
    }

    private sealed class AreaDownloadResult
    {
        public AreaDownloadResult(
            PlateauAreaOption area,
            PlateauTilesetModel buildings,
            IReadOnlyCollection<string> downloadWarnings)
        {
            Area = area ?? throw new ArgumentNullException(nameof(area));
            Buildings = buildings ?? throw new ArgumentNullException(nameof(buildings));
            DownloadWarnings = downloadWarnings?.ToArray() ?? Array.Empty<string>();
        }

        public PlateauAreaOption Area { get; }

        public PlateauTilesetModel Buildings { get; }

        public string[] DownloadWarnings { get; }
    }
}
