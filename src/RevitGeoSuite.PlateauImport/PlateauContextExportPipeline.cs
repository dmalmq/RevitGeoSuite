using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.RevitInterop.GeoPlacement;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Headless PLATEAU context export pipeline: turns a fully-resolved <see cref="ShapefileExportRequest"/>
/// (plus Revit model footprints + options) into Shapefile and/or DXF output using the Civil 3D shared
/// coordinates convention ((0,0) at the Survey Point, a marker at the Project Base Point). The Kiban
/// (GSI terrain) auto-scan is supplied as a delegate so callers can plug in their own caching scan.
/// </summary>
public sealed class PlateauContextExportPipeline
{
    private const int KibanPolygonClipBatchSize = 25;

    private readonly ContextGeometryBuilder geometryBuilder;
    private readonly ICoordinateTransformer? kibanCoordinateTransformer;
    private readonly CurrentProjectStateSummary currentState;
    private readonly Func<KibanScanRequest, KibanScanResult> scanKibanFolder;

    public PlateauContextExportPipeline(
        ContextGeometryBuilder geometryBuilder,
        ICoordinateTransformer? kibanCoordinateTransformer,
        CurrentProjectStateSummary currentState,
        Func<KibanScanRequest, KibanScanResult> scanKibanFolder)
    {
        this.geometryBuilder = geometryBuilder ?? throw new ArgumentNullException(nameof(geometryBuilder));
        this.kibanCoordinateTransformer = kibanCoordinateTransformer;
        this.currentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        this.scanKibanFolder = scanKibanFolder ?? throw new ArgumentNullException(nameof(scanKibanFolder));
    }

    /// <summary>
    /// Runs the full export to <paramref name="baseFileName"/> (".shp"/".dxf" derived from it), writing
    /// the requested formats/layers. Mirrors the legacy <c>PlateauImportWindow.RunExport</c>.
    /// </summary>
    public PlateauContextExportResult Run(
        string baseFileName,
        ShapefileExportRequest request,
        bool wantShapefile,
        bool wantDxf,
        bool includePlateauContext,
        bool includeKibanData,
        bool includeRevitModel,
        IReadOnlyList<RevitModelFootprintFeature> revitFootprints,
        IReadOnlyCollection<string>? acceptedLandUseClassNames,
        Action<PlateauExportProgress> progress)
    {
        if (wantShapefile && !wantDxf)
        {
            PlateauContextShapefileWriter.WriteResult? streamingShapefileResult = null;
            try
            {
                streamingShapefileResult = WriteShapefilesStreaming(
                    baseFileName,
                    request,
                    acceptedLandUseClassNames,
                    includePlateauContext,
                    includeKibanData,
                    includeRevitModel,
                    includeRevitModel ? revitFootprints : Array.Empty<RevitModelFootprintFeature>(),
                    progress);
            }
            catch (InvalidOperationException)
            {
                streamingShapefileResult = null;
            }

            return new PlateauContextExportResult(
                Array.Empty<string>(),
                streamingShapefileResult,
                dxfResult: null,
                isEmpty: streamingShapefileResult is null || streamingShapefileResult.FeatureCount == 0,
                request.HasKibanFolder);
        }

        PlateauOutlineDxfExportPackage exportPackage = BuildOutlineDxfExportPackage(
            request,
            acceptedLandUseClassNames,
            includeRevitModel ? revitFootprints : Array.Empty<RevitModelFootprintFeature>(),
            progress,
            out IReadOnlyList<string> outlineWarnings);

        bool plateauHasGeometry = exportPackage.Features.Count > 0
            || exportPackage.RoadAreas.Count > 0
            || exportPackage.KibanFeatures.Count > 0;
        bool kibanHasGeometry = exportPackage.KibanLineFeatures.Count > 0
            || exportPackage.KibanPolygonFeatures.Count > 0;
        bool revitHasGeometry = exportPackage.RevitModelFeatures.Count > 0;

        bool plateauForExport = includePlateauContext && plateauHasGeometry;
        bool kibanForExport = includeKibanData && kibanHasGeometry;
        bool revitForExport = includeRevitModel && revitHasGeometry;

        if (!plateauForExport && !kibanForExport && !revitForExport)
        {
            return new PlateauContextExportResult(outlineWarnings, shapefileResult: null, dxfResult: null, isEmpty: true, request.HasKibanFolder);
        }

        PlateauContextShapefileWriter.WriteResult? shapefileResult = null;
        if (wantShapefile)
        {
            PlateauOutlineDxfExportPackage shapefilePackage = BuildFilteredPackage(
                exportPackage,
                includePlateauContext: plateauForExport,
                includeKibanData: kibanForExport,
                includeRevitModel: revitForExport);
            try
            {
                shapefileResult = PlateauContextShapefileWriter.Write(
                    baseFileName,
                    shapefilePackage,
                    stage => progress(new PlateauExportProgress(stage)));
            }
            catch (InvalidOperationException)
            {
                shapefileResult = null;
            }
        }

        PlateauContextDxfExportService.WriteResult? dxfResult = null;
        if (wantDxf)
        {
            string dxfPath = Path.ChangeExtension(baseFileName, ".dxf");
            dxfResult = new PlateauContextDxfExportService().Write(
                dxfPath,
                exportPackage,
                includePlateauContext: plateauForExport,
                includeRevitModel: revitForExport,
                onStage: stage => progress(new PlateauExportProgress(stage)));
        }

        bool emitted = (shapefileResult is not null && shapefileResult.FeatureCount > 0)
            || (dxfResult is not null && (dxfResult.PolylineCount > 0 || dxfResult.AreaFillCount > 0));
        return new PlateauContextExportResult(
            outlineWarnings,
            shapefileResult,
            dxfResult,
            isEmpty: !emitted,
            request.HasKibanFolder);
    }

    private static PlateauOutlineDxfExportPackage BuildFilteredPackage(
        PlateauOutlineDxfExportPackage source,
        bool includePlateauContext,
        bool includeKibanData,
        bool includeRevitModel)
    {
        return new PlateauOutlineDxfExportPackage(
            includePlateauContext ? source.Features : Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            includePlateauContext ? source.RoadAreas : Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
            includePlateauContext ? source.KibanFeatures : Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            includeKibanData ? source.KibanLineFeatures : Array.Empty<KibanLineExportFeature>(),
            includeKibanData ? source.KibanPolygonFeatures : Array.Empty<KibanPolygonExportFeature>(),
            includeRevitModel ? source.RevitModelFeatures : Array.Empty<RevitModelFootprintFeature>(),
            source.ProjectCrs,
            source.ProjectBasePointMarkerMetres,
            source.OriginOffsetMetres);
    }

    public PlateauOutlineDxfExportPackage BuildOutlineDxfExportPackage(
        ShapefileExportRequest request,
        IReadOnlyCollection<string>? acceptedLandUseClassNames,
        IReadOnlyList<RevitModelFootprintFeature>? revitModelFeatures,
        Action<PlateauExportProgress>? progress,
        out IReadOnlyList<string> warnings)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));

        progress?.Invoke(new PlateauExportProgress("Building polygon outlines"));
        if (!TryBuildOutlinePlan(request, out ContextImportPlan? plan, out IReadOnlyList<string> planWarnings) || plan is null)
        {
            warnings = Array.Empty<string>();
            return CreateEmptyOutlineDxfExportPackage();
        }

        PlateauDxfExportFrame dxfFrame = PlateauDxfExportFrame.Create(plan.ReferenceContext, currentState);
        List<string> exportWarnings = new List<string>(planWarnings);
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> rawOutlines = BuildOutlineFeatures(plan, dxfFrame, acceptedLandUseClassNames);
        progress?.Invoke(new PlateauExportProgress("Dissolving road areas"));
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(rawOutlines, exportWarnings);
        IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = rawOutlines
            .Where(outline => !string.Equals(outline.Layer, "PLATEAU_ROADS", StringComparison.Ordinal))
            .ToArray();

        IReadOnlyList<KibanLineExportFeature> kibanLineFeatures = BuildKibanLineFeatures(request, exportWarnings, progress);

        // Split sidewalks off the line stream — they're now exported as one-sided strip polygons.
        KibanLineExportFeature[] sidewalkLines = kibanLineFeatures
            .Where(line => string.Equals(line.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
            .ToArray();
        KibanLineExportFeature[] nonSidewalkLines = kibanLineFeatures
            .Where(line => !string.Equals(line.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
            .ToArray();

        IReadOnlyList<KibanPolygonExportFeature> kibanPolygonFeatures = BuildKibanPolygonFeatures(request, exportWarnings, progress);
        IReadOnlyList<KibanPolygonExportFeature> sidewalkStrips = BuildSidewalkStripPolygons(request, sidewalkLines, roadAreas, exportWarnings, progress);
        if (sidewalkStrips.Count > 0)
        {
            kibanPolygonFeatures = kibanPolygonFeatures.Concat(sidewalkStrips).ToArray();
        }

        warnings = exportWarnings.ToArray();
        return new PlateauOutlineDxfExportPackage(
            outlines,
            roadAreas,
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            nonSidewalkLines,
            kibanPolygonFeatures,
            revitModelFeatures ?? (IReadOnlyList<RevitModelFootprintFeature>)Array.Empty<RevitModelFootprintFeature>(),
            plan.ReferenceContext.ProjectCrs,
            dxfFrame.ProjectBasePointSharedMetres,
            dxfFrame.SurveyPointSharedMetres);
    }

    private static PlateauOutlineDxfExportPackage CreateEmptyOutlineDxfExportPackage()
    {
        return new PlateauOutlineDxfExportPackage(
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
            Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
            Array.Empty<KibanLineExportFeature>(),
            Array.Empty<KibanPolygonExportFeature>(),
            new CrsReference(),
            Vector3d.Zero,
            Vector3d.Zero);
    }

    public PlateauContextShapefileWriter.WriteResult WriteShapefilesStreaming(
        string shapefilePath,
        ShapefileExportRequest request,
        IReadOnlyCollection<string>? acceptedLandUseClassNames,
        bool includePlateauContext,
        bool includeKibanData,
        bool includeRevitModel,
        IReadOnlyList<RevitModelFootprintFeature> revitModelFeatures,
        Action<PlateauExportProgress>? progress = null)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (revitModelFeatures is null) throw new ArgumentNullException(nameof(revitModelFeatures));

        List<string> exportWarnings = new List<string>(request.ScanResult.WarningMessages);
        using PlateauContextShapefileWriter.StreamingWriteSession session = PlateauContextShapefileWriter.OpenStreaming(
            shapefilePath,
            request.ReferenceContext.ProjectCrs,
            exportWarnings);

        Dictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>> roadContextBySecondaryMesh =
            new Dictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>>(StringComparer.Ordinal);
        bool needsSidewalkRoadContext = includeKibanData
            && request.SelectedKibanLayerNames.Contains(PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparer.Ordinal);

        if (includePlateauContext || needsSidewalkRoadContext)
        {
            StreamPlateauShapefileBatches(
                request,
                acceptedLandUseClassNames,
                includePlateauContext,
                needsSidewalkRoadContext,
                session,
                roadContextBySecondaryMesh,
                exportWarnings,
                progress);
        }

        if (includeKibanData)
        {
            StreamKibanShapefileBatches(
                request,
                session,
                roadContextBySecondaryMesh,
                exportWarnings,
                progress);
        }

        if (includeRevitModel && revitModelFeatures.Count > 0)
        {
            progress?.Invoke(new PlateauExportProgress(
                "Writing Revit model shapefiles",
                string.Format(CultureInfo.InvariantCulture, "{0} feature(s)", revitModelFeatures.Count)));
            session.WriteRevitModelFeatures(revitModelFeatures);
        }

        return session.Complete();
    }

    private void StreamPlateauShapefileBatches(
        ShapefileExportRequest request,
        IReadOnlyCollection<string>? acceptedLandUseClassNames,
        bool writePlateauContext,
        bool collectRoadContext,
        PlateauContextShapefileWriter.StreamingWriteSession session,
        IDictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>> roadContextBySecondaryMesh,
        List<string> exportWarnings,
        Action<PlateauExportProgress>? progress)
    {
        string[] selectedTileIds = request.SelectedTileIds
            .Where(tileId => !string.IsNullOrWhiteSpace(tileId))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(tileId => tileId, StringComparer.Ordinal)
            .ToArray();
        HashSet<PlateauFeatureType> selectedTypes = new HashSet<PlateauFeatureType>(request.SelectedFeatureTypes);
        HashSet<PlateauFeatureType> buildTypes = new HashSet<PlateauFeatureType>(selectedTypes);
        if (collectRoadContext)
        {
            buildTypes.Add(PlateauFeatureType.Road);
        }

        bool writeRoads = writePlateauContext && selectedTypes.Contains(PlateauFeatureType.Road);
        PlateauDxfExportFrame dxfFrame = PlateauDxfExportFrame.Create(request.ReferenceContext, currentState);

        foreach (string tileId in selectedTileIds)
        {
            PlateauFolderScanResult batchScan = BuildFilteredScanForTiles(
                request.ScanResult,
                buildTypes,
                new[] { tileId });
            if (batchScan.CityModels.Count == 0)
            {
                continue;
            }

            progress?.Invoke(new PlateauExportProgress("Building PLATEAU shapefile batch", tileId));
            ContextImportPlan plan = geometryBuilder.BuildPlan(
                batchScan,
                request.ReferenceContext,
                buildTypes,
                new[] { tileId },
                PlateauGeometryImportMode.LightweightExtrusion);
            exportWarnings.AddRange(plan.WarningMessages);

            IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> rawOutlines = BuildOutlineFeatures(plan, dxfFrame, acceptedLandUseClassNames);
            IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas = PlateauRoadOutlineCleaner.DissolveRoads(rawOutlines, exportWarnings);
            if (collectRoadContext && roadAreas.Count > 0)
            {
                AddRoadContext(roadContextBySecondaryMesh, new[] { tileId }, roadAreas);
            }

            if (!writePlateauContext)
            {
                continue;
            }

            progress?.Invoke(new PlateauExportProgress("Writing PLATEAU shapefile batch", tileId));
            if (writeRoads)
            {
                session.WritePlateauRoadAreas(roadAreas);
            }

            session.WritePlateauOutlines(rawOutlines
                .Where(outline => !string.Equals(outline.Layer, "PLATEAU_ROADS", StringComparison.Ordinal))
                .ToArray());
        }
    }

    private void StreamKibanShapefileBatches(
        ShapefileExportRequest request,
        PlateauContextShapefileWriter.StreamingWriteSession session,
        IReadOnlyDictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>> roadContextBySecondaryMesh,
        List<string> warnings,
        Action<PlateauExportProgress>? progress)
    {
        ISet<string> selectedLayers = new HashSet<string>(request.SelectedKibanLayerNames, StringComparer.Ordinal);
        if (selectedLayers.Count == 0)
        {
            if (request.HasKibanFolder && request.HasKibanLayerOptions)
            {
                warnings.Add("GSI Kiban features skipped: no GSI layers are selected.");
            }

            return;
        }

        if (kibanCoordinateTransformer is null)
        {
            warnings.Add("GSI Kiban features skipped: coordinate transformer is not available.");
            return;
        }

        bool needsLineFeatures = selectedLayers.Contains(PlateauContextOutlinesDxfWriter.GsiSidewalksLayer)
            || selectedLayers.Contains(PlateauContextOutlinesDxfWriter.GsiRailwaysLayer);
        bool needsPolygonFeatures = selectedLayers.Contains(KibanGmlParser.WaterLayer)
            || selectedLayers.Contains(KibanGmlParser.LandUseLayer);

        IReadOnlyList<KibanParsedFeature>? sourceLineFeatures = request.KibanParsedFeatures;
        IReadOnlyList<KibanParsedPolygonFeature>? sourcePolygonFeatures = request.KibanParsedPolygonFeatures;
        if (request.HasKibanFolder
            && ((needsLineFeatures && (sourceLineFeatures is null || sourceLineFeatures.Count == 0))
                || (needsPolygonFeatures && (sourcePolygonFeatures is null || sourcePolygonFeatures.Count == 0))))
        {
            progress?.Invoke(new PlateauExportProgress("Scanning GSI Kiban folder"));
            KibanScanResult scanResult = scanKibanFolder(new KibanScanRequest(
                request.KibanFolderPath,
                BuildSecondaryMeshCodes(request.SelectedTileIds),
                request.AdditionalGreenLandUseTokens));
            sourceLineFeatures = scanResult.Features;
            sourcePolygonFeatures = scanResult.PolygonFeatures;
            if (scanResult.SkippedFileCount > 0 && sourceLineFeatures.Count == 0 && sourcePolygonFeatures.Count == 0)
            {
                warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "GSI Kiban folder was scanned during export, but no selected Kiban features were found. {0} file(s) outside the selected CityGML mesh set were skipped.",
                    scanResult.SkippedFileCount));
            }
        }

        string[] secondaryMeshCodes = BuildSecondaryMeshCodes(request.SelectedTileIds).ToArray();
        bool wroteLineFeature = false;
        bool wrotePolygonFeature = false;
        foreach (string secondaryMeshCode in secondaryMeshCodes)
        {
            string[] selectedTileIdsForMesh = BuildSelectedTileIdsForSecondaryMesh(request.SelectedTileIds, secondaryMeshCode);
            if (selectedTileIdsForMesh.Length == 0)
            {
                continue;
            }

            if (needsLineFeatures && sourceLineFeatures is not null && sourceLineFeatures.Count > 0)
            {
                KibanParsedFeature[] lineBatch = sourceLineFeatures
                    .Where(feature => selectedLayers.Contains(feature.Layer)
                        && string.Equals(feature.MeshCode, secondaryMeshCode, StringComparison.Ordinal))
                    .ToArray();
                if (lineBatch.Length > 0)
                {
                    progress?.Invoke(new PlateauExportProgress(
                        "Projecting GSI line batch",
                        string.Format(CultureInfo.InvariantCulture, "{0}: {1} feature(s)", secondaryMeshCode, lineBatch.Length)));
                    IReadOnlyList<KibanLineExportFeature> projectedLines = KibanGeometryConverter.ConvertToLines(
                        lineBatch,
                        selectedTileIdsForMesh,
                        request.ReferenceContext.ProjectCrs,
                        kibanCoordinateTransformer,
                        warnings);
                    KibanLineExportFeature[] railwayLines = projectedLines
                        .Where(feature => string.Equals(feature.Layer, PlateauContextOutlinesDxfWriter.GsiRailwaysLayer, StringComparison.Ordinal))
                        .ToArray();
                    if (railwayLines.Length > 0)
                    {
                        session.WriteKibanLines(railwayLines);
                        wroteLineFeature = true;
                    }

                    KibanLineExportFeature[] sidewalkLines = projectedLines
                        .Where(feature => string.Equals(feature.Layer, PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparison.Ordinal))
                        .ToArray();
                    if (sidewalkLines.Length > 0)
                    {
                        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadContext = roadContextBySecondaryMesh.TryGetValue(secondaryMeshCode, out List<PlateauContextOutlinesDxfWriter.AreaFeature>? roadAreas)
                            ? roadAreas
                            : Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>();
                        progress?.Invoke(new PlateauExportProgress(
                            "Building GSI sidewalk strips",
                            string.Format(CultureInfo.InvariantCulture, "{0}: {1} sidewalk line(s)", secondaryMeshCode, sidewalkLines.Length)));
                        IReadOnlyList<KibanPolygonExportFeature> sidewalkStrips = SidewalkStripBuilder.Build(
                            sidewalkLines,
                            roadContext,
                            selectedTileIdsForMesh,
                            request.ReferenceContext.ProjectCrs,
                            kibanCoordinateTransformer,
                            SidewalkStripOptions.Default,
                            warnings);
                        if (sidewalkStrips.Count > 0)
                        {
                            session.WriteKibanPolygons(sidewalkStrips);
                            wrotePolygonFeature = true;
                        }
                    }
                }
            }

            if (needsPolygonFeatures && sourcePolygonFeatures is not null && sourcePolygonFeatures.Count > 0)
            {
                KibanParsedPolygonFeature[] polygonBatch = sourcePolygonFeatures
                    .Where(feature => selectedLayers.Contains(feature.Layer)
                        && string.Equals(feature.MeshCode, secondaryMeshCode, StringComparison.Ordinal))
                    .ToArray();
                if (polygonBatch.Length > 0)
                {
                    int chunkCount = (int)Math.Ceiling(polygonBatch.Length / (double)KibanPolygonClipBatchSize);
                    for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                    {
                        int startIndex = chunkIndex * KibanPolygonClipBatchSize;
                        int count = Math.Min(KibanPolygonClipBatchSize, polygonBatch.Length - startIndex);
                        KibanParsedPolygonFeature[] polygonChunk = new KibanParsedPolygonFeature[count];
                        Array.Copy(polygonBatch, startIndex, polygonChunk, 0, count);

                        progress?.Invoke(new PlateauExportProgress(
                            "Clipping GSI polygon batch",
                            string.Format(
                                CultureInfo.InvariantCulture,
                                "{0}: {1}-{2} of {3} polygon(s)",
                                secondaryMeshCode,
                                startIndex + 1,
                                startIndex + count,
                                polygonBatch.Length)));
                        IReadOnlyList<KibanPolygonExportFeature> polygons = KibanGeometryConverter.ConvertToPolygons(
                            polygonChunk,
                            selectedTileIdsForMesh,
                            request.ReferenceContext.ProjectCrs,
                            kibanCoordinateTransformer,
                            warnings);
                        if (polygons.Count > 0)
                        {
                            session.WriteKibanPolygons(polygons);
                            wrotePolygonFeature = true;
                        }
                    }
                }
            }
        }

        if (request.HasKibanFolder && needsLineFeatures && !wroteLineFeature)
        {
            warnings.Add("GSI Kiban data was scanned, but no sidewalk or railway lines intersected the selected CityGML tile(s).");
        }

        if (request.HasKibanFolder && needsPolygonFeatures && !wrotePolygonFeature)
        {
            warnings.Add("GSI Kiban polygon data was scanned, but no polygons intersected the selected CityGML tile(s).");
        }
    }

    private static PlateauFolderScanResult BuildFilteredScanForTiles(
        PlateauFolderScanResult source,
        ISet<PlateauFeatureType> selectedTypes,
        IReadOnlyCollection<string> selectedTileIds)
    {
        HashSet<string> selectedTiles = new HashSet<string>(selectedTileIds, StringComparer.Ordinal);
        List<PlateauCityModel> cityModels = new List<PlateauCityModel>();
        foreach (PlateauCityModel cityModel in source.CityModels)
        {
            PlateauContextFeature[] features = cityModel.Features
                .Where(feature => selectedTypes.Contains(feature.FeatureType)
                    && IsTileSelectedForExport(ResolveTileIdForExport(feature, cityModel), selectedTiles))
                .ToArray();
            if (features.Length == 0)
            {
                continue;
            }

            cityModels.Add(new PlateauCityModel
            {
                SourcePath = cityModel.SourcePath,
                SrsName = cityModel.SrsName,
                EpsgCode = cityModel.EpsgCode,
                FileTileId = cityModel.FileTileId,
                Features = features
            });
        }

        return new PlateauFolderScanResult
        {
            FolderPath = source.FolderPath,
            SearchRootPath = source.SearchRootPath,
            IsRecursivePackageScan = source.IsRecursivePackageScan,
            SupportedFilePaths = cityModels.Select(model => model.SourcePath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            CityModels = cityModels,
            WarningMessages = Array.Empty<string>()
        };
    }

    private static void AddRoadContext(
        IDictionary<string, List<PlateauContextOutlinesDxfWriter.AreaFeature>> roadContextBySecondaryMesh,
        IReadOnlyCollection<string> selectedTileIds,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas)
    {
        foreach (string secondaryMeshCode in BuildSecondaryMeshCodes(selectedTileIds))
        {
            if (!roadContextBySecondaryMesh.TryGetValue(secondaryMeshCode, out List<PlateauContextOutlinesDxfWriter.AreaFeature>? existing))
            {
                existing = new List<PlateauContextOutlinesDxfWriter.AreaFeature>();
                roadContextBySecondaryMesh.Add(secondaryMeshCode, existing);
            }

            existing.AddRange(roadAreas);
        }
    }

    private static string[] BuildSelectedTileIdsForSecondaryMesh(
        IReadOnlyCollection<string> selectedTileIds,
        string secondaryMeshCode)
    {
        return selectedTileIds
            .Where(tileId => !string.IsNullOrWhiteSpace(tileId)
                && tileId.Trim().StartsWith(secondaryMeshCode, StringComparison.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string ResolveTileIdForExport(PlateauContextFeature feature, PlateauCityModel model)
    {
        if (!string.IsNullOrWhiteSpace(feature.TileId))
        {
            return feature.TileId;
        }

        if (!string.IsNullOrWhiteSpace(model.FileTileId))
        {
            return model.FileTileId!;
        }

        string fileName = Path.GetFileNameWithoutExtension(model.SourcePath) ?? string.Empty;
        return string.IsNullOrWhiteSpace(fileName) ? "unassigned" : fileName;
    }

    private static bool IsTileSelectedForExport(string tileId, ISet<string> selectedTileIds)
    {
        if (selectedTileIds.Contains(tileId))
        {
            return true;
        }

        if (tileId.Length == 6)
        {
            return selectedTileIds.Any(selectedTileId =>
                selectedTileId.Length > tileId.Length
                && selectedTileId.StartsWith(tileId, StringComparison.Ordinal));
        }

        return false;
    }

    private IReadOnlyList<KibanLineExportFeature> BuildKibanLineFeatures(ShapefileExportRequest request, List<string> warnings, Action<PlateauExportProgress>? progress = null)
    {
        bool hasKibanFolder = request.HasKibanFolder;
        IReadOnlyList<KibanParsedFeature>? sourceFeatures = request.KibanParsedFeatures;
        if ((sourceFeatures is null || sourceFeatures.Count == 0) && hasKibanFolder)
        {
            KibanScanResult scanResult = scanKibanFolder(new KibanScanRequest(
                request.KibanFolderPath,
                BuildSecondaryMeshCodes(request.SelectedTileIds),
                request.AdditionalGreenLandUseTokens));
            sourceFeatures = scanResult.Features;
            if (sourceFeatures.Count == 0)
            {
                string skipInfo = scanResult.SkippedFileCount > 0
                    ? string.Format(CultureInfo.InvariantCulture, " {0} file(s) outside the selected CityGML mesh set were skipped.", scanResult.SkippedFileCount)
                    : string.Empty;
                warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "GSI Kiban folder was scanned during export, but no sidewalk or railway features were found for the selected CityGML mesh set.{0}",
                    skipInfo));
                return Array.Empty<KibanLineExportFeature>();
            }
        }

        if (sourceFeatures is null || sourceFeatures.Count == 0)
        {
            return Array.Empty<KibanLineExportFeature>();
        }

        if (kibanCoordinateTransformer is null)
        {
            warnings.Add("GSI Kiban features skipped: coordinate transformer is not available.");
            return Array.Empty<KibanLineExportFeature>();
        }

        ISet<string> selectedLayers = new HashSet<string>(
            request.SelectedKibanLayerNames,
            StringComparer.Ordinal);

        if (selectedLayers.Count == 0)
        {
            if (hasKibanFolder && request.HasKibanLayerOptions)
            {
                warnings.Add("GSI Kiban features skipped: no GSI line layers are selected.");
            }

            return Array.Empty<KibanLineExportFeature>();
        }

        ISet<string> selectedTileIds = new HashSet<string>(
            request.SelectedTileIds,
            StringComparer.Ordinal);

        List<KibanParsedFeature> filteredFeatures = sourceFeatures
            .Where(feature => selectedLayers.Contains(feature.Layer)
                && !string.IsNullOrEmpty(feature.MeshCode)
                && selectedTileIds.Any(tileId => tileId.StartsWith(feature.MeshCode, StringComparison.Ordinal)))
            .ToList();

        if (filteredFeatures.Count == 0)
        {
            if (hasKibanFolder)
            {
                warnings.Add("GSI Kiban data was available, but no sidewalk or railway features matched the selected CityGML mesh set and GSI layer selection.");
            }

            return Array.Empty<KibanLineExportFeature>();
        }

        try
        {
            progress?.Invoke(new PlateauExportProgress(
                "Projecting GSI lines",
                string.Format(CultureInfo.InvariantCulture, "{0} feature(s)", filteredFeatures.Count)));
            IReadOnlyList<KibanLineExportFeature> lineFeatures = KibanGeometryConverter.ConvertToLines(
                filteredFeatures,
                selectedTileIds.ToArray(),
                request.ReferenceContext.ProjectCrs,
                kibanCoordinateTransformer,
                warnings);
            if (lineFeatures.Count == 0 && hasKibanFolder)
            {
                warnings.Add("GSI Kiban data was scanned, but no sidewalk or railway lines intersected the selected CityGML tile(s).");
            }

            return lineFeatures;
        }
        catch (Exception ex)
        {
            warnings.Add(string.Format(CultureInfo.InvariantCulture, "GSI Kiban feature conversion failed: {0}", ex.Message));
            return Array.Empty<KibanLineExportFeature>();
        }
    }

    private IReadOnlyList<KibanPolygonExportFeature> BuildKibanPolygonFeatures(ShapefileExportRequest request, List<string> warnings, Action<PlateauExportProgress>? progress = null)
    {
        bool hasKibanFolder = request.HasKibanFolder;
        IReadOnlyList<KibanParsedPolygonFeature>? sourceFeatures = request.KibanParsedPolygonFeatures;
        if ((sourceFeatures is null || sourceFeatures.Count == 0) && hasKibanFolder)
        {
            KibanScanResult scanResult = scanKibanFolder(new KibanScanRequest(
                request.KibanFolderPath,
                BuildSecondaryMeshCodes(request.SelectedTileIds),
                request.AdditionalGreenLandUseTokens));
            sourceFeatures = scanResult.PolygonFeatures;
            if (sourceFeatures.Count == 0)
            {
                return Array.Empty<KibanPolygonExportFeature>();
            }
        }

        if (sourceFeatures is null || sourceFeatures.Count == 0)
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        if (kibanCoordinateTransformer is null)
        {
            warnings.Add("GSI Kiban polygons skipped: coordinate transformer is not available.");
            return Array.Empty<KibanPolygonExportFeature>();
        }

        ISet<string> selectedLayers = new HashSet<string>(
            request.SelectedKibanLayerNames,
            StringComparer.Ordinal);
        string[] selectedPolygonLayers = selectedLayers
            .Where(layer => string.Equals(layer, KibanGmlParser.WaterLayer, StringComparison.Ordinal)
                || string.Equals(layer, KibanGmlParser.LandUseLayer, StringComparison.Ordinal))
            .ToArray();

        if (selectedPolygonLayers.Length == 0)
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        ISet<string> selectedTileIds = new HashSet<string>(
            request.SelectedTileIds,
            StringComparer.Ordinal);

        List<KibanParsedPolygonFeature> filteredFeatures = sourceFeatures
            .Where(feature => selectedLayers.Contains(feature.Layer)
                && !string.IsNullOrEmpty(feature.MeshCode)
                && selectedTileIds.Any(tileId => tileId.StartsWith(feature.MeshCode, StringComparison.Ordinal)))
            .ToList();

        if (filteredFeatures.Count == 0)
        {
            if (hasKibanFolder)
            {
                warnings.Add("GSI Kiban polygon data was available, but no polygons matched the selected CityGML mesh set and GSI layer selection.");
            }

            return Array.Empty<KibanPolygonExportFeature>();
        }

        try
        {
            progress?.Invoke(new PlateauExportProgress(
                "Clipping GSI polygons to tiles",
                string.Format(CultureInfo.InvariantCulture, "{0} polygon(s)", filteredFeatures.Count)));
            IReadOnlyList<KibanPolygonExportFeature> polygonFeatures = KibanGeometryConverter.ConvertToPolygons(
                filteredFeatures,
                selectedTileIds.ToArray(),
                request.ReferenceContext.ProjectCrs,
                kibanCoordinateTransformer,
                warnings);
            if (polygonFeatures.Count == 0 && hasKibanFolder)
            {
                warnings.Add("GSI Kiban polygon data was scanned, but no polygons intersected the selected CityGML tile(s).");
            }

            return polygonFeatures;
        }
        catch (Exception ex)
        {
            warnings.Add(string.Format(CultureInfo.InvariantCulture, "GSI Kiban polygon conversion failed: {0}", ex.Message));
            return Array.Empty<KibanPolygonExportFeature>();
        }
    }

    private IReadOnlyList<KibanPolygonExportFeature> BuildSidewalkStripPolygons(
        ShapefileExportRequest request,
        IReadOnlyList<KibanLineExportFeature> sidewalkLines,
        IReadOnlyList<PlateauContextOutlinesDxfWriter.AreaFeature> roadAreas,
        List<string> warnings,
        Action<PlateauExportProgress>? progress = null)
    {
        if (sidewalkLines.Count == 0)
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        if (!request.SelectedKibanLayerNames.Contains(PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, StringComparer.Ordinal))
        {
            return Array.Empty<KibanPolygonExportFeature>();
        }

        if (kibanCoordinateTransformer is null)
        {
            warnings.Add("GSI sidewalk-strip polygons skipped: coordinate transformer is not available.");
            return Array.Empty<KibanPolygonExportFeature>();
        }

        try
        {
            progress?.Invoke(new PlateauExportProgress(
                "Building GSI sidewalk strips",
                string.Format(CultureInfo.InvariantCulture, "{0} sidewalk line(s)", sidewalkLines.Count)));
            return SidewalkStripBuilder.Build(
                sidewalkLines,
                roadAreas,
                request.SelectedTileIds,
                request.ReferenceContext.ProjectCrs,
                kibanCoordinateTransformer,
                SidewalkStripOptions.Default,
                warnings);
        }
        catch (Exception ex)
        {
            warnings.Add(string.Format(CultureInfo.InvariantCulture, "GSI sidewalk-strip polygon generation failed: {0}", ex.Message));
            return Array.Empty<KibanPolygonExportFeature>();
        }
    }

    private bool TryBuildOutlinePlan(ShapefileExportRequest request, out ContextImportPlan? plan, out IReadOnlyList<string> warnings)
    {
        warnings = Array.Empty<string>();
        plan = null;
        if (request.SelectedFeatureTypes.Count == 0 || request.SelectedTileIds.Count == 0)
        {
            return false;
        }

        plan = geometryBuilder.BuildPlan(
            request.ScanResult,
            request.ReferenceContext,
            request.SelectedFeatureTypes,
            request.SelectedTileIds,
            PlateauGeometryImportMode.LightweightExtrusion);

        warnings = plan.WarningMessages.ToArray();
        return true;
    }

    private static IReadOnlyCollection<string> BuildSecondaryMeshCodes(IEnumerable<string> tileIds)
    {
        if (tileIds is null)
        {
            return Array.Empty<string>();
        }

        return tileIds
            .Where(tileId => !string.IsNullOrWhiteSpace(tileId))
            .Select(tileId => tileId.Trim())
            .Where(tileId => tileId.Length >= 6)
            .Select(tileId => tileId.Substring(0, 6))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    internal IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> BuildOutlineFeatures(
        ContextImportPlan plan,
        PlateauDxfExportFrame dxfFrame,
        IReadOnlyCollection<string>? acceptedLandUseClassNames)
    {
        if (dxfFrame is null) throw new ArgumentNullException(nameof(dxfFrame));
        return BuildOutlineFeatures(plan, dxfFrame.ToSharedPlanMetres, acceptedLandUseClassNames);
    }

    /// <summary>
    /// Projects each shape's footprint (Revit-internal feet) into planar metres using
    /// <paramref name="toPlanMetres"/>, grouped onto the DXF layer for its feature type. The
    /// export passes the shared/projected-coordinate frame; the lightweight 2D import
    /// (<see cref="PlateauContextDxfImporter"/>) passes a model-internal metre frame so the
    /// imported linework lands directly over the model.
    /// </summary>
    internal static IReadOnlyList<PlateauContextOutlinesDxfWriter.OutlineFeature> BuildOutlineFeatures(
        ContextImportPlan plan,
        Func<double, double, (double EastingMetres, double NorthingMetres)> toPlanMetres,
        IReadOnlyCollection<string>? acceptedLandUseClassNames)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (toPlanMetres is null) throw new ArgumentNullException(nameof(toPlanMetres));

        HashSet<string>? acceptedClassNames = acceptedLandUseClassNames is null
            ? null
            : new HashSet<string>(acceptedLandUseClassNames, StringComparer.Ordinal);

        List<PlateauContextOutlinesDxfWriter.OutlineFeature> outlines = new List<PlateauContextOutlinesDxfWriter.OutlineFeature>(plan.Shapes.Count);
        foreach (ContextShapePlan shape in plan.Shapes)
        {
            if (!PlateauContextOutlinesDxfWriter.LayerByFeatureType.TryGetValue(shape.FeatureType, out string? layer))
            {
                continue;
            }

            if (shape.FeatureType == PlateauFeatureType.LandUse
                && acceptedClassNames is not null
                && !acceptedClassNames.Contains(shape.ClassName ?? string.Empty))
            {
                continue;
            }

            (double X, double Y)[] vertices = new (double, double)[shape.FootprintPointsFeet.Count];
            int index = 0;
            foreach ((double xFeet, double yFeet) in shape.FootprintPointsFeet)
            {
                (double eastingMetres, double northingMetres) = toPlanMetres(xFeet, yFeet);
                vertices[index++] = (eastingMetres, northingMetres);
            }

            outlines.Add(new PlateauContextOutlinesDxfWriter.OutlineFeature(
                layer,
                vertices,
                shape.SourceFeatureId,
                shape.ClassCode,
                shape.ClassName));
        }

        return outlines;
    }
}

/// <summary>Outcome of a <see cref="PlateauContextExportPipeline.Run"/> call.</summary>
public sealed class PlateauContextExportResult
{
    public PlateauContextExportResult(
        IReadOnlyList<string> outlineWarnings,
        PlateauContextShapefileWriter.WriteResult? shapefileResult,
        PlateauContextDxfExportService.WriteResult? dxfResult,
        bool isEmpty,
        bool kibanFolderInvolved)
    {
        OutlineWarnings = outlineWarnings ?? Array.Empty<string>();
        ShapefileResult = shapefileResult;
        DxfResult = dxfResult;
        IsEmpty = isEmpty;
        KibanFolderInvolved = kibanFolderInvolved;
    }

    public IReadOnlyList<string> OutlineWarnings { get; }

    public PlateauContextShapefileWriter.WriteResult? ShapefileResult { get; }

    public PlateauContextDxfExportService.WriteResult? DxfResult { get; }

    public bool IsEmpty { get; }

    public bool KibanFolderInvolved { get; }
}
