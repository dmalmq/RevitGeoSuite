using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using NetTopologySuite.Geometries;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Plateau.Dem;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.PlateauImport;

public sealed class ContextGeometryBuilder
{
    private const int Jgd2011GeographicEpsg = 6668;
    private const int Jgd2011CompoundHeightEpsg = 6697;
    private const double MetersToFeet = 1.0 / 0.3048d;
    private const double BridgePlanAreaEpsilon = 1e-6d;
    private const double BridgeMinorComponentAreaRatio = 0.05d;
    private readonly ICoordinateTransformer coordinateTransformer;

    public ContextGeometryBuilder(ICoordinateTransformer? coordinateTransformer = null)
    {
        this.coordinateTransformer = coordinateTransformer ?? new CoordinateTransformer(new CrsRegistry());
    }

    public ContextImportPlan BuildPlan(
        PlateauCityModel cityModel,
        PlateauImportReferenceContext referenceContext,
        PlateauGeometryImportMode geometryImportMode = PlateauGeometryImportMode.LightweightExtrusion)
    {
        if (cityModel is null)
        {
            throw new ArgumentNullException(nameof(cityModel));
        }

        PlateauFolderScanResult scanResult = new PlateauFolderScanResult
        {
            FolderPath = string.IsNullOrWhiteSpace(cityModel.SourcePath) ? string.Empty : (Path.GetDirectoryName(cityModel.SourcePath) ?? string.Empty),
            CityModels = new[] { cityModel }
        };
        return BuildPlan(scanResult, referenceContext, null, null, geometryImportMode);
    }

    public ContextImportPlan BuildPlan(
        PlateauFolderScanResult scanResult,
        PlateauImportReferenceContext referenceContext,
        IReadOnlyCollection<PlateauFeatureType>? selectedFeatureTypes,
        IReadOnlyCollection<string>? selectedTileIds,
        PlateauGeometryImportMode geometryImportMode = PlateauGeometryImportMode.LightweightExtrusion)
    {
        if (scanResult is null)
        {
            throw new ArgumentNullException(nameof(scanResult));
        }

        if (referenceContext is null)
        {
            throw new ArgumentNullException(nameof(referenceContext));
        }

        List<ResolvedContextFeature> resolvedFeatures = ResolveFeatures(scanResult);
        HashSet<PlateauFeatureType> selectedTypes = CreateSelectedTypeSet(resolvedFeatures, selectedFeatureTypes);
        HashSet<string> selectedTiles = CreateSelectedTileSet(resolvedFeatures, selectedTileIds);
        Dictionary<PlateauCityModel, TransformStrategy> transformStrategies = CreateTransformStrategies(scanResult.CityModels, referenceContext);
        Geometry? landUseClipRegion = BuildLandUseClipRegion(selectedTiles, referenceContext);

        List<ContextShapePlan> shapes = new List<ContextShapePlan>(resolvedFeatures.Count);
        List<string> warnings = new List<string>(scanResult.WarningMessages);
        int sourceFeatureCount = 0;
        int preparedSurfaceCount = 0;
        int preparedTriangleCount = 0;

        DemSampler? reliefSampler = null;
        if (geometryImportMode == PlateauGeometryImportMode.LightweightMassOnRelief)
        {
            reliefSampler = TryBuildReliefSampler(resolvedFeatures, selectedTypes, selectedTiles, transformStrategies, referenceContext, warnings);
        }

        foreach (ResolvedContextFeature resolvedFeature in resolvedFeatures)
        {
            PlateauCityModel cityModel = resolvedFeature.CityModel;
            PlateauContextFeature feature = resolvedFeature.Feature;
            string tileId = resolvedFeature.TileId;
            if (!selectedTypes.Contains(feature.FeatureType) || !IsTileSelected(tileId, selectedTiles))
            {
                continue;
            }

            sourceFeatureCount++;
            TransformStrategy transformStrategy = transformStrategies[cityModel];
            if (geometryImportMode == PlateauGeometryImportMode.DetailedDirectShape)
            {
                if (TryBuildDetailedShape(feature, cityModel.SourcePath, transformStrategy, referenceContext, tileId, warnings, out ContextShapePlan? shape))
                {
                    shapes.Add(shape!);
                    preparedSurfaceCount += shape!.SurfaceCount;
                    preparedTriangleCount += shape.Triangles.Count;
                }

                continue;
            }

            if (feature.FeatureType == PlateauFeatureType.Bridge
                && TryBuildBridgeFootprintShapes(feature, cityModel, transformStrategy, referenceContext, tileId, warnings, out IReadOnlyCollection<ContextShapePlan> bridgeShapes))
            {
                shapes.AddRange(bridgeShapes);
                continue;
            }

            PlateauCoordinate3D[] ring = NormalizeRing(feature.ExteriorRing);
            if (ring.Length < 3)
            {
                continue;
            }

            PlateauCoordinate3D[] transformedRing = new PlateauCoordinate3D[ring.Length];
            for (int index = 0; index < ring.Length; index++)
            {
                transformedRing[index] = TransformPoint(ring[index], transformStrategy);
            }

            IReadOnlyList<PlateauCoordinate3D[]> clippedRings = ShouldClipToTileGrid(feature.FeatureType)
                ? ClipLandUseRing(transformedRing, landUseClipRegion)
                : new[] { transformedRing };
            if (clippedRings.Count == 0)
            {
                continue;
            }

            (double minimumHeightMeters, double defaultHeightMeters) = GetHeightParameters(feature.FeatureType);
            (double baseElevationMeters, double heightMeters) = ResolveElevationAndHeight(
                feature,
                cityModel,
                transformedRing,
                minimumHeightMeters,
                defaultHeightMeters,
                warnings);

            if (geometryImportMode == PlateauGeometryImportMode.LightweightMassOnRelief
                && reliefSampler is not null
                && feature.FeatureType == PlateauFeatureType.Building)
            {
                double centroidX = 0d;
                double centroidY = 0d;
                for (int index = 0; index < transformedRing.Length; index++)
                {
                    centroidX += transformedRing[index].X;
                    centroidY += transformedRing[index].Y;
                }
                centroidX /= transformedRing.Length;
                centroidY /= transformedRing.Length;
                double groundZ = reliefSampler.SampleElevationOrNearest(centroidX, centroidY, out bool exactSample);
                if (!exactSample)
                {
                    warnings.Add(string.Format(
                        CultureInfo.InvariantCulture,
                        "{0}: footprint centroid was outside the Relief hull; using nearest-triangle elevation {1:F2} m.",
                        BuildFeatureLabel(feature, cityModel.SourcePath, tileId),
                        groundZ));
                }
                baseElevationMeters = groundZ;
            }

            int clippedPartIndex = 0;
            foreach (PlateauCoordinate3D[] partRing in clippedRings)
            {
                clippedPartIndex++;
                (double XFeet, double YFeet)[] footprintPointsFeet = new (double XFeet, double YFeet)[partRing.Length];
                for (int index = 0; index < partRing.Length; index++)
                {
                    footprintPointsFeet[index] = ToLocalFeet(partRing[index], referenceContext);
                }

                string sourceFeatureId = string.IsNullOrWhiteSpace(feature.Id) ? Guid.NewGuid().ToString("N") : feature.Id;
                if (clippedRings.Count > 1)
                {
                    sourceFeatureId = string.Concat(sourceFeatureId, ":part", clippedPartIndex.ToString(CultureInfo.InvariantCulture));
                }

                shapes.Add(new ContextShapePlan
                {
                    DisplayName = string.IsNullOrWhiteSpace(feature.Name) ? feature.Id : feature.Name,
                    SourceFeatureId = sourceFeatureId,
                    FeatureType = feature.FeatureType,
                    TileId = tileId,
                    SourceFilePath = cityModel.SourcePath,
                    GeometryMode = PlateauGeometryImportMode.LightweightExtrusion,
                    FootprintPointsFeet = footprintPointsFeet,
                    BaseElevationFeet = ToLocalElevationFeet(baseElevationMeters, referenceContext),
                    HeightFeet = heightMeters * MetersToFeet,
                    ClassCode = feature.ClassCode,
                    ClassName = feature.ClassName
                });
            }
        }

        return new ContextImportPlan
        {
            SourceFolderPath = scanResult.FolderPath,
            ReferenceContext = referenceContext,
            GeometryImportMode = geometryImportMode,
            SourceModels = scanResult.CityModels,
            SelectedFeatureTypes = selectedTypes.OrderBy(type => type).ToArray(),
            SelectedTileIds = selectedTiles.OrderBy(tileId => tileId, StringComparer.Ordinal).ToArray(),
            Shapes = shapes,
            WarningMessages = warnings,
            SourceFeatureCount = sourceFeatureCount,
            PreparedSurfaceCount = preparedSurfaceCount,
            PreparedTriangleCount = preparedTriangleCount
        };
    }

    /// <summary>
    /// Builds a <see cref="DemSampler"/> from the Relief (dem) surfaces in <paramref name="scanResult"/>,
    /// in the model's projected CRS. Reuses the same feature-resolution and transform pipeline as
    /// <see cref="BuildPlan(PlateauFolderScanResult, PlateauImportReferenceContext, IReadOnlyCollection{PlateauFeatureType}?, IReadOnlyCollection{string}?, PlateauGeometryImportMode)"/>,
    /// so ground sampled here lines up exactly with imported context geometry. Returns <c>null</c>
    /// when the selection contains no Relief surfaces.
    /// </summary>
    public DemSampler? BuildReliefSampler(
        PlateauFolderScanResult scanResult,
        PlateauImportReferenceContext referenceContext,
        IReadOnlyCollection<string>? selectedTileIds,
        ICollection<string> warnings)
    {
        if (scanResult is null) throw new ArgumentNullException(nameof(scanResult));
        if (referenceContext is null) throw new ArgumentNullException(nameof(referenceContext));
        if (warnings is null) throw new ArgumentNullException(nameof(warnings));

        List<ResolvedContextFeature> resolvedFeatures = ResolveFeatures(scanResult);
        HashSet<string> selectedTiles = CreateSelectedTileSet(resolvedFeatures, selectedTileIds);
        Dictionary<PlateauCityModel, TransformStrategy> transformStrategies = CreateTransformStrategies(scanResult.CityModels, referenceContext);
        HashSet<PlateauFeatureType> reliefOnly = new HashSet<PlateauFeatureType> { PlateauFeatureType.Relief };
        return TryBuildReliefSampler(resolvedFeatures, reliefOnly, selectedTiles, transformStrategies, referenceContext, warnings);
    }

    private const int MaxReliefTriangles = 10_000_000;

    private (double MinX, double MinY, double MaxX, double MaxY)? ComputeSelectedTileBounds(
        ISet<string> selectedTiles,
        PlateauImportReferenceContext referenceContext)
    {
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;
        bool any = false;

        foreach (string tileId in selectedTiles)
        {
            string trimmed = tileId?.Trim() ?? string.Empty;
            if (trimmed.Length != 6 && trimmed.Length != 8) continue;

            bool allDigits = true;
            for (int i = 0; i < trimmed.Length; i++)
            {
                if (!char.IsDigit(trimmed[i])) { allDigits = false; break; }
            }
            if (!allDigits) continue;

            MeshBounds meshBounds;
            try
            {
                meshBounds = meshCalculator.GetBounds(new MeshCode { Value = trimmed });
            }
            catch (ArgumentException)
            {
                continue;
            }

            try
            {
                ProjectedCoordinate sw = coordinateTransformer.Project(
                    new GeographicCoordinate(meshBounds.SouthLatitude, meshBounds.WestLongitude),
                    referenceContext.ProjectCrs);
                ProjectedCoordinate ne = coordinateTransformer.Project(
                    new GeographicCoordinate(meshBounds.NorthLatitude, meshBounds.EastLongitude),
                    referenceContext.ProjectCrs);

                double loX = Math.Min(sw.Easting, ne.Easting);
                double hiX = Math.Max(sw.Easting, ne.Easting);
                double loY = Math.Min(sw.Northing, ne.Northing);
                double hiY = Math.Max(sw.Northing, ne.Northing);

                if (loX < minX) minX = loX;
                if (loY < minY) minY = loY;
                if (hiX > maxX) maxX = hiX;
                if (hiY > maxY) maxY = hiY;
                any = true;
            }
            catch
            {
            }
        }

        return any ? (minX, minY, maxX, maxY) : null;
    }

    private DemSampler? TryBuildReliefSampler(
        IReadOnlyList<ResolvedContextFeature> resolvedFeatures,
        ISet<PlateauFeatureType> selectedTypes,
        ISet<string> selectedTiles,
        IReadOnlyDictionary<PlateauCityModel, TransformStrategy> transformStrategies,
        PlateauImportReferenceContext referenceContext,
        ICollection<string> warnings)
    {
        (double MinX, double MinY, double MaxX, double MaxY)? clipBounds = ComputeSelectedTileBounds(selectedTiles, referenceContext);

        int totalCount = 0;
        foreach (ResolvedContextFeature resolvedFeature in resolvedFeatures)
        {
            PlateauContextFeature feature = resolvedFeature.Feature;
            if (feature.FeatureType != PlateauFeatureType.Relief) continue;
            if (!selectedTypes.Contains(feature.FeatureType) || !IsTileSelected(resolvedFeature.TileId, selectedTiles)) continue;

            foreach (PlateauGeometrySurface surface in feature.GeometrySurfaces)
            {
                PlateauCoordinate3D[] ring = NormalizeRing(surface.ExteriorRing);
                if (ring.Length < 3) continue;
                totalCount += ring.Length - 2;
            }
        }

        if (totalCount == 0)
        {
            warnings.Add("Mass on Relief mode is selected but no Relief surfaces were found in the selection; falling back to building min-Z elevation.");
            return null;
        }

        int stride = totalCount > MaxReliefTriangles ? (totalCount + MaxReliefTriangles - 1) / MaxReliefTriangles : 1;
        int capacity = stride > 1 ? MaxReliefTriangles : totalCount;
        List<(Vector3d A, Vector3d B, Vector3d C)> triangles = new List<(Vector3d, Vector3d, Vector3d)>(capacity);
        int emitted = 0;
        int skippedOutside = 0;
        int cursor = 0;

        foreach (ResolvedContextFeature resolvedFeature in resolvedFeatures)
        {
            PlateauContextFeature feature = resolvedFeature.Feature;
            if (feature.FeatureType != PlateauFeatureType.Relief) continue;
            if (!selectedTypes.Contains(feature.FeatureType) || !IsTileSelected(resolvedFeature.TileId, selectedTiles)) continue;

            TransformStrategy transformStrategy = transformStrategies[resolvedFeature.CityModel];
            foreach (PlateauGeometrySurface surface in feature.GeometrySurfaces)
            {
                PlateauCoordinate3D[] ring = NormalizeRing(surface.ExteriorRing);
                if (ring.Length < 3) continue;

                Vector3d[] transformed = new Vector3d[ring.Length];
                for (int index = 0; index < ring.Length; index++)
                {
                    PlateauCoordinate3D projected = TransformPoint(ring[index], transformStrategy);
                    transformed[index] = new Vector3d(projected.X, projected.Y, projected.Z);
                }

                for (int i = 1; i < transformed.Length - 1; i++)
                {
                    if (clipBounds.HasValue)
                    {
                        double cx = (transformed[0].X + transformed[i].X + transformed[i + 1].X) / 3.0;
                        double cy = (transformed[0].Y + transformed[i].Y + transformed[i + 1].Y) / 3.0;
                        if (cx < clipBounds.Value.MinX || cx > clipBounds.Value.MaxX ||
                            cy < clipBounds.Value.MinY || cy > clipBounds.Value.MaxY)
                        {
                            skippedOutside++;
                            continue;
                        }
                    }

                    if (cursor % stride == 0)
                    {
                        triangles.Add((transformed[0], transformed[i], transformed[i + 1]));
                        emitted++;
                    }
                    cursor++;
                }
            }
        }

        if (stride > 1)
        {
            warnings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Relief data contained {0:N0} triangles; subsampled to {1:N0} (1 in {2}) to stay within memory limits. Ground elevation is uniformly covered but coarser.",
                totalCount,
                emitted,
                stride));
        }

        if (skippedOutside > 0)
        {
            warnings.Add(string.Format(
                CultureInfo.InvariantCulture,
                "Skipped {0:N0} relief triangles outside the selected tile bounds.",
                skippedOutside));
        }

        if (triangles.Count == 0)
        {
            warnings.Add("No relief triangles fell within the selected tile bounds; falling back to building min-Z elevation.");
            return null;
        }

        return new DemSampler(triangles);
    }

    private bool TryBuildBridgeFootprintShapes(
        PlateauContextFeature feature,
        PlateauCityModel cityModel,
        TransformStrategy transformStrategy,
        PlateauImportReferenceContext referenceContext,
        string tileId,
        ICollection<string> warnings,
        out IReadOnlyCollection<ContextShapePlan> shapes)
    {
        shapes = Array.Empty<ContextShapePlan>();
        IReadOnlyCollection<PlateauGeometrySurface> sourceSurfaces = GetHighestLodSurfaces(feature.GeometrySurfaces);
        if (sourceSurfaces.Count == 0)
        {
            return false;
        }

        GeometryFactory geometryFactory = new GeometryFactory();
        List<Geometry> surfacePolygons = new List<Geometry>(sourceSurfaces.Count);
        List<PlateauCoordinate3D> transformedElevationPoints = new List<PlateauCoordinate3D>();
        foreach (PlateauGeometrySurface surface in sourceSurfaces)
        {
            PlateauCoordinate3D[] ring = NormalizeRing(surface.ExteriorRing);
            if (ring.Length < 3)
            {
                continue;
            }

            PlateauCoordinate3D[] transformedRing = new PlateauCoordinate3D[ring.Length];
            for (int index = 0; index < ring.Length; index++)
            {
                transformedRing[index] = TransformPoint(ring[index], transformStrategy);
            }

            Geometry? polygon = CreateBridgeSurfacePolygon(geometryFactory, transformedRing);
            if (polygon is null || polygon.IsEmpty)
            {
                continue;
            }

            AddPolygonalGeometries(polygon, surfacePolygons);
            transformedElevationPoints.AddRange(transformedRing);
        }

        if (surfacePolygons.Count == 0)
        {
            return false;
        }

        Geometry unioned;
        try
        {
            unioned = geometryFactory.CreateGeometryCollection(surfacePolygons.ToArray()).Union();
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            warnings.Add($"Bridge footprint dissolve failed for {BuildFeatureLabel(feature, cityModel.SourcePath, tileId)} ({ex.Message}); using separate valid surface footprints.");
            unioned = geometryFactory.CreateGeometryCollection(surfacePolygons.ToArray());
        }

        List<Polygon> footprintPolygons = new List<Polygon>();
        AddPolygons(unioned, footprintPolygons);
        if (footprintPolygons.Count == 0)
        {
            return false;
        }

        footprintPolygons.Sort(CompareBridgeFootprintPolygons);
        double largestArea = footprintPolygons[0].Area;
        List<Polygon> selectedPolygons = new List<Polygon>(footprintPolygons.Count);
        for (int index = 0; index < footprintPolygons.Count; index++)
        {
            Polygon polygon = footprintPolygons[index];
            if (polygon.Area <= BridgePlanAreaEpsilon)
            {
                continue;
            }

            if (index == 0 || polygon.Area >= largestArea * BridgeMinorComponentAreaRatio)
            {
                selectedPolygons.Add(polygon);
            }
        }

        if (selectedPolygons.Count == 0)
        {
            selectedPolygons.Add(footprintPolygons[0]);
        }

        (double minimumHeightMeters, double defaultHeightMeters) = GetHeightParameters(feature.FeatureType);
        (double baseElevationMeters, double heightMeters) = ResolveElevationAndHeight(
            feature,
            cityModel,
            transformedElevationPoints,
            minimumHeightMeters,
            defaultHeightMeters,
            warnings);

        List<ContextShapePlan> bridgeShapes = new List<ContextShapePlan>(selectedPolygons.Count);
        bool needsSuffix = selectedPolygons.Count > 1;
        for (int index = 0; index < selectedPolygons.Count; index++)
        {
            Polygon polygon = selectedPolygons[index];
            (double XFeet, double YFeet)[] footprintPointsFeet = BuildFootprintPointsFeet(polygon, referenceContext);
            if (footprintPointsFeet.Length < 3)
            {
                continue;
            }

            string sourceFeatureId = string.IsNullOrWhiteSpace(feature.Id) ? Guid.NewGuid().ToString("N") : feature.Id;
            string displayName = string.IsNullOrWhiteSpace(feature.Name) ? sourceFeatureId : feature.Name;
            if (needsSuffix)
            {
                sourceFeatureId = string.Format(CultureInfo.InvariantCulture, "{0}::{1}", sourceFeatureId, index + 1);
                displayName = string.Format(CultureInfo.InvariantCulture, "{0} [{1}]", displayName, index + 1);
            }

            bridgeShapes.Add(new ContextShapePlan
            {
                DisplayName = displayName,
                SourceFeatureId = sourceFeatureId,
                FeatureType = feature.FeatureType,
                TileId = tileId,
                SourceFilePath = cityModel.SourcePath,
                GeometryMode = PlateauGeometryImportMode.LightweightExtrusion,
                FootprintPointsFeet = footprintPointsFeet,
                BaseElevationFeet = ToLocalElevationFeet(baseElevationMeters, referenceContext),
                HeightFeet = heightMeters * MetersToFeet,
                ClassCode = feature.ClassCode,
                ClassName = feature.ClassName
            });
        }

        shapes = bridgeShapes;
        return bridgeShapes.Count > 0;
    }

    private bool TryBuildDetailedShape(
        PlateauContextFeature feature,
        string sourcePath,
        TransformStrategy transformStrategy,
        PlateauImportReferenceContext referenceContext,
        string tileId,
        ICollection<string> warnings,
        out ContextShapePlan? shape)
    {
        shape = null;
        List<ContextShapeTriangle> triangles = new List<ContextShapeTriangle>();
        int importedSurfaceCount = 0;
        foreach (PlateauGeometrySurface surface in feature.GeometrySurfaces)
        {
            PlateauCoordinate3D[] normalizedRing = NormalizeRing(surface.ExteriorRing);
            if (normalizedRing.Length < 3)
            {
                continue;
            }

            List<ContextShapePoint3D> localPoints = new List<ContextShapePoint3D>(normalizedRing.Length);
            for (int index = 0; index < normalizedRing.Length; index++)
            {
                PlateauCoordinate3D transformedPoint = TransformPoint(normalizedRing[index], transformStrategy);
                localPoints.Add(ToLocalPointFeet(transformedPoint, referenceContext));
            }

            if (localPoints.Count < 3)
            {
                warnings.Add($"Skipped a detailed surface for {BuildFeatureLabel(feature, sourcePath, tileId)} because it collapsed below the minimum point count after coordinate conversion.");
                continue;
            }

            List<List<ContextShapePoint3D>> interiorPointRings = new List<List<ContextShapePoint3D>>();
            foreach (IReadOnlyCollection<PlateauCoordinate3D> interiorRing in surface.InteriorRings)
            {
                PlateauCoordinate3D[] normalizedInteriorRing = NormalizeRing(interiorRing);
                if (normalizedInteriorRing.Length < 3)
                {
                    continue;
                }

                List<ContextShapePoint3D> localInteriorPoints = new List<ContextShapePoint3D>(normalizedInteriorRing.Length);
                for (int index = 0; index < normalizedInteriorRing.Length; index++)
                {
                    PlateauCoordinate3D transformedPoint = TransformPoint(normalizedInteriorRing[index], transformStrategy);
                    localInteriorPoints.Add(ToLocalPointFeet(transformedPoint, referenceContext));
                }

                if (localInteriorPoints.Count >= 3)
                {
                    interiorPointRings.Add(localInteriorPoints);
                }
            }

            IReadOnlyCollection<ContextShapeTriangle> surfaceTriangles;
            bool triangulated = interiorPointRings.Count > 0
                ? PlateauPolygonTriangulator.TryTriangulate(localPoints, interiorPointRings, out surfaceTriangles)
                : PlateauPolygonTriangulator.TryTriangulate(localPoints, out surfaceTriangles);
            if (!triangulated)
            {
                warnings.Add($"Skipped a detailed surface for {BuildFeatureLabel(feature, sourcePath, tileId)} because the polygon could not be triangulated safely.");
                continue;
            }

            triangles.AddRange(surfaceTriangles);
            importedSurfaceCount++;
        }

        if (triangles.Count == 0)
        {
            warnings.Add($"Skipped {BuildFeatureLabel(feature, sourcePath, tileId)} because none of its detailed surfaces could be triangulated into importable geometry.");
            return false;
        }

        shape = new ContextShapePlan
        {
            DisplayName = string.IsNullOrWhiteSpace(feature.Name) ? feature.Id : feature.Name,
            SourceFeatureId = string.IsNullOrWhiteSpace(feature.Id) ? Guid.NewGuid().ToString("N") : feature.Id,
            FeatureType = feature.FeatureType,
            TileId = tileId,
            SourceFilePath = sourcePath,
            GeometryMode = PlateauGeometryImportMode.DetailedDirectShape,
            SurfaceCount = importedSurfaceCount,
            Triangles = triangles,
            ClassCode = feature.ClassCode,
            ClassName = feature.ClassName
        };
        return true;
    }

    private static (double BaseElevationMeters, double HeightMeters) ResolveElevationAndHeight(
        PlateauContextFeature feature,
        PlateauCityModel cityModel,
        IReadOnlyList<PlateauCoordinate3D> transformedRing,
        double minimumHeightMeters,
        double defaultHeightMeters,
        ICollection<string> warnings)
    {
        double minZ = double.PositiveInfinity;
        double maxZ = double.NegativeInfinity;
        for (int index = 0; index < transformedRing.Count; index++)
        {
            PlateauCoordinate3D point = transformedRing[index];
            if (point.Z < minZ)
            {
                minZ = point.Z;
            }

            if (point.Z > maxZ)
            {
                maxZ = point.Z;
            }
        }

        double baseElevationMeters = double.IsPositiveInfinity(minZ) ? 0d : minZ;
        double topElevationMeters = double.IsNegativeInfinity(maxZ) ? 0d : maxZ;

        if (feature is PlateauBuildingFeature buildingFeature)
        {
            if (buildingFeature.BaseElevationMeters.HasValue)
            {
                baseElevationMeters = buildingFeature.BaseElevationMeters.Value;
            }

            if (buildingFeature.TopElevationMeters.HasValue)
            {
                topElevationMeters = Math.Max(buildingFeature.TopElevationMeters.Value, baseElevationMeters);
            }
        }

        double heightMeters = Math.Max(minimumHeightMeters, topElevationMeters - baseElevationMeters);
        if (heightMeters <= minimumHeightMeters)
        {
            heightMeters = defaultHeightMeters;
            if (feature is PlateauBuildingFeature)
            {
                warnings.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Used the default {0:F1} m height for building '{1}' in '{2}' because no usable roof/top elevation could be derived from the CityGML geometry.",
                    defaultHeightMeters,
                    string.IsNullOrWhiteSpace(feature.Name) ? feature.Id : feature.Name,
                    Path.GetFileName(cityModel.SourcePath)));
            }
        }

        return (baseElevationMeters, heightMeters);
    }

    private static string ResolveTileId(PlateauContextFeature feature, PlateauCityModel? model)
    {
        if (!string.IsNullOrWhiteSpace(feature.TileId))
        {
            return feature.TileId;
        }

        if (model is not null)
        {
            string modelTileId = model.FileTileId ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(modelTileId))
            {
                return modelTileId;
            }
        }

        string fileName = model is null
            ? string.Empty
            : (Path.GetFileNameWithoutExtension(model.SourcePath) ?? string.Empty);
        return string.IsNullOrWhiteSpace(fileName) ? "unassigned" : fileName;
    }

    private Dictionary<PlateauCityModel, TransformStrategy> CreateTransformStrategies(
        IReadOnlyCollection<PlateauCityModel> cityModels,
        PlateauImportReferenceContext referenceContext)
    {
        Dictionary<PlateauCityModel, TransformStrategy> strategies = new Dictionary<PlateauCityModel, TransformStrategy>(cityModels.Count);
        foreach (PlateauCityModel cityModel in cityModels)
        {
            if (strategies.ContainsKey(cityModel))
            {
                continue;
            }

            strategies[cityModel] = ResolveTransformStrategy(cityModel, referenceContext);
        }

        return strategies;
    }

    private TransformStrategy ResolveTransformStrategy(PlateauCityModel cityModel, PlateauImportReferenceContext referenceContext)
    {
        if (!cityModel.EpsgCode.HasValue || cityModel.EpsgCode.Value == referenceContext.ProjectCrs.EpsgCode)
        {
            return TransformStrategy.Identity(referenceContext.ProjectCrs);
        }

        int sourceEpsg = cityModel.EpsgCode.Value;
        if (IsSupportedProjectedJgd2011(sourceEpsg))
        {
            return TransformStrategy.ProjectedJgd2011(
                new CrsReference { EpsgCode = sourceEpsg, NameSnapshot = cityModel.SrsName },
                referenceContext.ProjectCrs);
        }

        if (IsSupportedGeographicJgd2011(sourceEpsg))
        {
            return TransformStrategy.GeographicJgd2011(referenceContext.ProjectCrs);
        }

        throw new InvalidOperationException($"The CityGML file uses EPSG:{sourceEpsg}, which is not supported yet for PLATEAU import. Supported file CRSs are EPSG:6668, EPSG:6697, and projected Japanese zones EPSG:6669-6687.");
    }

    private PlateauCoordinate3D TransformPoint(PlateauCoordinate3D point, TransformStrategy transformStrategy)
    {
        switch (transformStrategy.Kind)
        {
            case TransformKind.Identity:
                return point;
            case TransformKind.ProjectedJgd2011:
                GeographicCoordinate geographic = coordinateTransformer.Unproject(
                    new ProjectedCoordinate(point.X, point.Y),
                    transformStrategy.SourceCrs!);
                ProjectedCoordinate projected = coordinateTransformer.Project(geographic, transformStrategy.TargetCrs);
                return new PlateauCoordinate3D(projected.Easting, projected.Northing, point.Z);
            case TransformKind.GeographicJgd2011:
                GeographicCoordinate geographicPoint = new GeographicCoordinate(point.X, point.Y);
                ProjectedCoordinate projectedPoint = coordinateTransformer.Project(geographicPoint, transformStrategy.TargetCrs);
                return new PlateauCoordinate3D(projectedPoint.Easting, projectedPoint.Northing, point.Z);
            default:
                throw new InvalidOperationException("The requested PLATEAU coordinate transform mode is not supported.");
        }
    }

    private static List<ResolvedContextFeature> ResolveFeatures(PlateauFolderScanResult scanResult)
    {
        List<ResolvedContextFeature> resolvedFeatures = new List<ResolvedContextFeature>();
        foreach (PlateauCityModel cityModel in scanResult.CityModels)
        {
            foreach (PlateauContextFeature feature in cityModel.Features)
            {
                resolvedFeatures.Add(new ResolvedContextFeature(cityModel, feature, ResolveTileId(feature, cityModel)));
            }
        }

        return resolvedFeatures;
    }

    private static HashSet<PlateauFeatureType> CreateSelectedTypeSet(
        IReadOnlyCollection<ResolvedContextFeature> resolvedFeatures,
        IReadOnlyCollection<PlateauFeatureType>? selectedFeatureTypes)
    {
        if (selectedFeatureTypes is not null && selectedFeatureTypes.Count > 0)
        {
            return new HashSet<PlateauFeatureType>(selectedFeatureTypes);
        }

        HashSet<PlateauFeatureType> selectedTypes = new HashSet<PlateauFeatureType>();
        foreach (ResolvedContextFeature resolvedFeature in resolvedFeatures)
        {
            selectedTypes.Add(resolvedFeature.Feature.FeatureType);
        }

        return selectedTypes;
    }

    private Geometry? BuildLandUseClipRegion(
        ICollection<string> selectedTileIds,
        PlateauImportReferenceContext referenceContext)
    {
        if (selectedTileIds.Count == 0)
        {
            return null;
        }

        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        GeometryFactory geometryFactory = new GeometryFactory();
        List<Polygon> tilePolygons = new List<Polygon>(selectedTileIds.Count);

        foreach (string tileId in selectedTileIds)
        {
            string trimmed = tileId?.Trim() ?? string.Empty;
            if (trimmed.Length == 0 || (trimmed.Length != 6 && trimmed.Length != 8))
            {
                continue;
            }

            bool isAllDigits = true;
            for (int index = 0; index < trimmed.Length; index++)
            {
                if (!char.IsDigit(trimmed[index]))
                {
                    isAllDigits = false;
                    break;
                }
            }
            if (!isAllDigits)
            {
                continue;
            }

            MeshBounds meshBounds;
            try
            {
                meshBounds = meshCalculator.GetBounds(new MeshCode { Value = trimmed });
            }
            catch (ArgumentException)
            {
                continue;
            }

            Polygon? projected = TryProjectMeshBoundsToProjectCrs(meshBounds, referenceContext.ProjectCrs, geometryFactory);
            if (projected is not null && !projected.IsEmpty)
            {
                tilePolygons.Add(projected);
            }
        }

        if (tilePolygons.Count == 0)
        {
            return null;
        }

        Geometry union = tilePolygons.Count == 1
            ? tilePolygons[0]
            : geometryFactory.BuildGeometry(tilePolygons).Union();

        return union.IsEmpty ? null : union;
    }

    private Polygon? TryProjectMeshBoundsToProjectCrs(MeshBounds meshBounds, CrsReference projectCrs, GeometryFactory geometryFactory)
    {
        const int samplesPerEdge = 12;
        List<Coordinate> ring = new List<Coordinate>((samplesPerEdge * 4) + 1);

        double minLat = Math.Min(meshBounds.SouthLatitude, meshBounds.NorthLatitude);
        double maxLat = Math.Max(meshBounds.SouthLatitude, meshBounds.NorthLatitude);
        double minLon = Math.Min(meshBounds.WestLongitude, meshBounds.EastLongitude);
        double maxLon = Math.Max(meshBounds.WestLongitude, meshBounds.EastLongitude);

        for (int i = 0; i < samplesPerEdge; i++)
        {
            double t = (double)i / samplesPerEdge;
            AppendProjectedCoordinate(ring, minLat, minLon + (t * (maxLon - minLon)), projectCrs);
        }
        for (int i = 0; i < samplesPerEdge; i++)
        {
            double t = (double)i / samplesPerEdge;
            AppendProjectedCoordinate(ring, minLat + (t * (maxLat - minLat)), maxLon, projectCrs);
        }
        for (int i = 0; i < samplesPerEdge; i++)
        {
            double t = (double)i / samplesPerEdge;
            AppendProjectedCoordinate(ring, maxLat, maxLon - (t * (maxLon - minLon)), projectCrs);
        }
        for (int i = 0; i < samplesPerEdge; i++)
        {
            double t = (double)i / samplesPerEdge;
            AppendProjectedCoordinate(ring, maxLat - (t * (maxLat - minLat)), minLon, projectCrs);
        }

        if (ring.Count < 3)
        {
            return null;
        }

        if (!ring[0].Equals2D(ring[ring.Count - 1]))
        {
            ring.Add(new Coordinate(ring[0].X, ring[0].Y));
        }

        try
        {
            LinearRing shell = geometryFactory.CreateLinearRing(ring.ToArray());
            Polygon polygon = geometryFactory.CreatePolygon(shell);
            return polygon.IsValid ? polygon : polygon.Buffer(0d) as Polygon;
        }
        catch (Exception ex) when (ex is ArgumentException || ex is TopologyException)
        {
            return null;
        }
    }

    private void AppendProjectedCoordinate(List<Coordinate> ring, double latitude, double longitude, CrsReference projectCrs)
    {
        try
        {
            ProjectedCoordinate projected = coordinateTransformer.Project(
                new GeographicCoordinate(latitude, longitude),
                projectCrs);
            ring.Add(new Coordinate(projected.Easting, projected.Northing));
        }
        catch
        {
        }
    }

    private static bool ShouldClipToTileGrid(PlateauFeatureType featureType)
    {
        return featureType == PlateauFeatureType.LandUse || featureType == PlateauFeatureType.Relief;
    }

    private static IReadOnlyList<PlateauCoordinate3D[]> ClipLandUseRing(
        PlateauCoordinate3D[] transformedRing,
        Geometry? clipRegion)
    {
        if (clipRegion is null || clipRegion.IsEmpty)
        {
            return new[] { transformedRing };
        }

        GeometryFactory geometryFactory = clipRegion.Factory;
        Coordinate[] ringCoords = new Coordinate[transformedRing.Length + 1];
        for (int index = 0; index < transformedRing.Length; index++)
        {
            ringCoords[index] = new Coordinate(transformedRing[index].X, transformedRing[index].Y);
        }
        ringCoords[transformedRing.Length] = new Coordinate(transformedRing[0].X, transformedRing[0].Y);

        Polygon polygon;
        try
        {
            LinearRing shell = geometryFactory.CreateLinearRing(ringCoords);
            polygon = geometryFactory.CreatePolygon(shell);
            if (!polygon.IsValid)
            {
                if (polygon.Buffer(0d) is Polygon repaired)
                {
                    polygon = repaired;
                }
            }
        }
        catch (Exception ex) when (ex is ArgumentException || ex is TopologyException)
        {
            return Array.Empty<PlateauCoordinate3D[]>();
        }

        Geometry intersection;
        try
        {
            intersection = polygon.Intersection(clipRegion);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return Array.Empty<PlateauCoordinate3D[]>();
        }

        if (intersection.IsEmpty)
        {
            return Array.Empty<PlateauCoordinate3D[]>();
        }

        List<PlateauCoordinate3D[]> rings = new List<PlateauCoordinate3D[]>();
        ExtractRings(intersection, transformedRing, rings);
        return rings;
    }

    private static void ExtractRings(Geometry geometry, PlateauCoordinate3D[] sourceRing, List<PlateauCoordinate3D[]> rings)
    {
        if (geometry.IsEmpty)
        {
            return;
        }

        if (geometry is Polygon polygon)
        {
            if (polygon.ExteriorRing is null || polygon.ExteriorRing.NumPoints < 4)
            {
                return;
            }

            double averageZ = 0d;
            for (int i = 0; i < sourceRing.Length; i++)
            {
                averageZ += sourceRing[i].Z;
            }
            averageZ /= sourceRing.Length;

            Coordinate[] coords = polygon.ExteriorRing.Coordinates;
            PlateauCoordinate3D[] ring = new PlateauCoordinate3D[coords.Length];
            for (int index = 0; index < coords.Length; index++)
            {
                ring[index] = new PlateauCoordinate3D(coords[index].X, coords[index].Y, averageZ);
            }
            rings.Add(ring);
            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            ExtractRings(geometry.GetGeometryN(index), sourceRing, rings);
        }
    }

    private static HashSet<string> CreateSelectedTileSet(
        IReadOnlyCollection<ResolvedContextFeature> resolvedFeatures,
        IReadOnlyCollection<string>? selectedTileIds)
    {
        if (selectedTileIds is not null && selectedTileIds.Count > 0)
        {
            return new HashSet<string>(selectedTileIds, StringComparer.Ordinal);
        }

        HashSet<string> selectedTiles = new HashSet<string>(StringComparer.Ordinal);
        foreach (ResolvedContextFeature resolvedFeature in resolvedFeatures)
        {
            selectedTiles.Add(resolvedFeature.TileId);
        }

        return selectedTiles;
    }

    private static bool IsTileSelected(string tileId, ISet<string> selectedTileIds)
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

    private static (double XFeet, double YFeet) ToLocalFeet(PlateauCoordinate3D point, PlateauImportReferenceContext referenceContext)
    {
        return PlateauReferenceFrame.ToLocalFeet(point.X, point.Y, referenceContext);
    }

    private static ContextShapePoint3D ToLocalPointFeet(PlateauCoordinate3D point, PlateauImportReferenceContext referenceContext)
    {
        (double xFeet, double yFeet) = ToLocalFeet(point, referenceContext);
        return new ContextShapePoint3D(xFeet, yFeet, ToLocalElevationFeet(point.Z, referenceContext));
    }

    private static double ToLocalElevationFeet(double pointElevationMeters, PlateauImportReferenceContext referenceContext)
    {
        return PlateauReferenceFrame.ToLocalElevationFeet(pointElevationMeters, referenceContext);
    }

    private static IReadOnlyCollection<PlateauGeometrySurface> GetHighestLodSurfaces(IReadOnlyCollection<PlateauGeometrySurface> surfaces)
    {
        if (surfaces is null || surfaces.Count == 0)
        {
            return Array.Empty<PlateauGeometrySurface>();
        }

        int highestLod = 0;
        foreach (PlateauGeometrySurface surface in surfaces)
        {
            if (surface.Lod > highestLod)
            {
                highestLod = surface.Lod;
            }
        }

        List<PlateauGeometrySurface> highest = new List<PlateauGeometrySurface>();
        foreach (PlateauGeometrySurface surface in surfaces)
        {
            if (surface.Lod == highestLod)
            {
                highest.Add(surface);
            }
        }

        return highest;
    }

    private static Geometry? CreateBridgeSurfacePolygon(GeometryFactory geometryFactory, IReadOnlyList<PlateauCoordinate3D> ring)
    {
        if (ring.Count < 3)
        {
            return null;
        }

        List<Coordinate> coordinates = new List<Coordinate>(ring.Count + 1);
        for (int index = 0; index < ring.Count; index++)
        {
            Coordinate coordinate = new Coordinate(ring[index].X, ring[index].Y);
            if (coordinates.Count == 0 || !SameCoordinate(coordinates[coordinates.Count - 1], coordinate))
            {
                coordinates.Add(coordinate);
            }
        }

        while (coordinates.Count > 1 && SameCoordinate(coordinates[0], coordinates[coordinates.Count - 1]))
        {
            coordinates.RemoveAt(coordinates.Count - 1);
        }

        if (coordinates.Count < 3 || Math.Abs(ComputeSignedArea(coordinates)) <= BridgePlanAreaEpsilon)
        {
            return null;
        }

        coordinates.Add(new Coordinate(coordinates[0]));
        try
        {
            Polygon polygon = geometryFactory.CreatePolygon(geometryFactory.CreateLinearRing(coordinates.ToArray()));
            if (polygon.IsEmpty || polygon.Area <= BridgePlanAreaEpsilon)
            {
                return null;
            }

            Geometry geometry = polygon.IsValid ? polygon : polygon.Buffer(0d);
            return geometry.IsEmpty || geometry.Area <= BridgePlanAreaEpsilon ? null : geometry;
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    private static void AddPolygonalGeometries(Geometry geometry, ICollection<Geometry> polygons)
    {
        if (geometry is Polygon polygon)
        {
            polygons.Add(polygon);
            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            Geometry child = geometry.GetGeometryN(index);
            AddPolygonalGeometries(child, polygons);
        }
    }

    private static void AddPolygons(Geometry geometry, ICollection<Polygon> polygons)
    {
        if (geometry is Polygon polygon)
        {
            polygons.Add(polygon);
            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            Geometry child = geometry.GetGeometryN(index);
            AddPolygons(child, polygons);
        }
    }

    private static (double XFeet, double YFeet)[] BuildFootprintPointsFeet(Polygon polygon, PlateauImportReferenceContext referenceContext)
    {
        if (polygon.ExteriorRing is null)
        {
            return Array.Empty<(double XFeet, double YFeet)>();
        }

        Coordinate[] coordinates = polygon.ExteriorRing.Coordinates;
        int count = coordinates.Length;
        if (count > 1 && SameCoordinate(coordinates[0], coordinates[count - 1]))
        {
            count--;
        }

        if (count < 3)
        {
            return Array.Empty<(double XFeet, double YFeet)>();
        }

        (double XFeet, double YFeet)[] points = new (double XFeet, double YFeet)[count];
        for (int index = 0; index < count; index++)
        {
            points[index] = ToLocalFeet(new PlateauCoordinate3D(coordinates[index].X, coordinates[index].Y, 0d), referenceContext);
        }

        return points;
    }

    private static int CompareBridgeFootprintPolygons(Polygon left, Polygon right)
    {
        int comparison = right.Area.CompareTo(left.Area);
        if (comparison != 0)
        {
            return comparison;
        }

        Envelope leftEnvelope = left.EnvelopeInternal;
        Envelope rightEnvelope = right.EnvelopeInternal;
        comparison = leftEnvelope.MinX.CompareTo(rightEnvelope.MinX);
        if (comparison != 0)
        {
            return comparison;
        }

        return leftEnvelope.MinY.CompareTo(rightEnvelope.MinY);
    }

    private static double ComputeSignedArea(IReadOnlyList<Coordinate> coordinates)
    {
        double areaTwice = 0d;
        for (int index = 0; index < coordinates.Count; index++)
        {
            Coordinate current = coordinates[index];
            Coordinate next = coordinates[(index + 1) % coordinates.Count];
            areaTwice += (current.X * next.Y) - (next.X * current.Y);
        }

        return areaTwice * 0.5d;
    }

    private static bool SameCoordinate(Coordinate left, Coordinate right)
    {
        return left.X == right.X && left.Y == right.Y;
    }

    private static bool IsSupportedProjectedJgd2011(int epsgCode)
    {
        return epsgCode >= 6669 && epsgCode <= 6687;
    }

    private static bool IsSupportedGeographicJgd2011(int epsgCode)
    {
        return epsgCode == Jgd2011GeographicEpsg || epsgCode == Jgd2011CompoundHeightEpsg;
    }

    private static PlateauCoordinate3D[] NormalizeRing(IReadOnlyCollection<PlateauCoordinate3D> ring)
    {
        if (ring is null || ring.Count == 0)
        {
            return Array.Empty<PlateauCoordinate3D>();
        }

        PlateauCoordinate3D[] points = ring as PlateauCoordinate3D[] ?? ring.ToArray();
        if (points.Length > 1)
        {
            PlateauCoordinate3D first = points[0];
            PlateauCoordinate3D last = points[points.Length - 1];
            if (AreEqual(first, last))
            {
                PlateauCoordinate3D[] trimmed = new PlateauCoordinate3D[points.Length - 1];
                Array.Copy(points, trimmed, trimmed.Length);
                return trimmed;
            }
        }

        return points;
    }

    private static bool AreEqual(PlateauCoordinate3D left, PlateauCoordinate3D right)
    {
        return left.X == right.X && left.Y == right.Y && left.Z == right.Z;
    }

    private static string BuildFeatureLabel(PlateauContextFeature feature, string sourcePath, string tileId)
    {
        string displayName = string.IsNullOrWhiteSpace(feature.Name) ? feature.Id : feature.Name;
        string sourceFileName = string.IsNullOrWhiteSpace(sourcePath)
            ? "unknown file"
            : Path.GetFileName(sourcePath);
        return $"'{displayName}' in tile {tileId} ({sourceFileName})";
    }

    private static (double MinimumHeightMeters, double DefaultHeightMeters) GetHeightParameters(PlateauFeatureType featureType)
    {
        switch (featureType)
        {
            case PlateauFeatureType.Building:
                return (3.0d, 10.0d);
            case PlateauFeatureType.Bridge:
                return (0.5d, 2.0d);
            case PlateauFeatureType.Road:
                return (0.2d, 0.5d);
            case PlateauFeatureType.Sidewalk:
                return (0.05d, 0.15d);
            case PlateauFeatureType.Vegetation:
                return (1.0d, 3.0d);
            case PlateauFeatureType.Relief:
                return (0.2d, 1.0d);
            default:
                return (3.0d, 10.0d);
        }
    }

    private sealed class ResolvedContextFeature
    {
        public ResolvedContextFeature(PlateauCityModel cityModel, PlateauContextFeature feature, string tileId)
        {
            CityModel = cityModel;
            Feature = feature;
            TileId = tileId;
        }

        public PlateauCityModel CityModel { get; }

        public PlateauContextFeature Feature { get; }

        public string TileId { get; }
    }

    private sealed class TransformStrategy
    {
        private TransformStrategy(TransformKind kind, CrsReference? sourceCrs, CrsReference targetCrs)
        {
            Kind = kind;
            SourceCrs = sourceCrs;
            TargetCrs = targetCrs;
        }

        public TransformKind Kind { get; }

        public CrsReference? SourceCrs { get; }

        public CrsReference TargetCrs { get; }

        public static TransformStrategy Identity(CrsReference targetCrs)
        {
            return new TransformStrategy(TransformKind.Identity, null, targetCrs);
        }

        public static TransformStrategy ProjectedJgd2011(CrsReference sourceCrs, CrsReference targetCrs)
        {
            return new TransformStrategy(TransformKind.ProjectedJgd2011, sourceCrs, targetCrs);
        }

        public static TransformStrategy GeographicJgd2011(CrsReference targetCrs)
        {
            return new TransformStrategy(TransformKind.GeographicJgd2011, null, targetCrs);
        }
    }

    private enum TransformKind
    {
        Identity,
        ProjectedJgd2011,
        GeographicJgd2011
    }
}
