using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.PlateauImport;

public sealed class ContextGeometryBuilder
{
    private const int Jgd2011GeographicEpsg = 6668;
    private const int Jgd2011CompoundHeightEpsg = 6697;
    private const double MetersToFeet = 1.0 / 0.3048d;
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

        List<ContextShapePlan> shapes = new List<ContextShapePlan>(resolvedFeatures.Count);
        List<string> warnings = new List<string>(scanResult.WarningMessages);
        int sourceFeatureCount = 0;
        int preparedSurfaceCount = 0;
        int preparedTriangleCount = 0;

        foreach (ResolvedContextFeature resolvedFeature in resolvedFeatures)
        {
            PlateauCityModel cityModel = resolvedFeature.CityModel;
            PlateauContextFeature feature = resolvedFeature.Feature;
            string tileId = resolvedFeature.TileId;
            if (!selectedTypes.Contains(feature.FeatureType) || !selectedTiles.Contains(tileId))
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

            (double minimumHeightMeters, double defaultHeightMeters) = GetHeightParameters(feature.FeatureType);
            (double baseElevationMeters, double heightMeters) = ResolveElevationAndHeight(
                feature,
                cityModel,
                transformedRing,
                minimumHeightMeters,
                defaultHeightMeters,
                warnings);

            (double XFeet, double YFeet)[] footprintPointsFeet = new (double XFeet, double YFeet)[transformedRing.Length];
            for (int index = 0; index < transformedRing.Length; index++)
            {
                footprintPointsFeet[index] = ToLocalFeet(transformedRing[index], referenceContext);
            }

            shapes.Add(new ContextShapePlan
            {
                DisplayName = string.IsNullOrWhiteSpace(feature.Name) ? feature.Id : feature.Name,
                SourceFeatureId = string.IsNullOrWhiteSpace(feature.Id) ? Guid.NewGuid().ToString("N") : feature.Id,
                FeatureType = feature.FeatureType,
                TileId = tileId,
                SourceFilePath = cityModel.SourcePath,
                GeometryMode = PlateauGeometryImportMode.LightweightExtrusion,
                FootprintPointsFeet = footprintPointsFeet,
                BaseElevationFeet = ToLocalElevationFeet(baseElevationMeters, referenceContext),
                HeightFeet = heightMeters * MetersToFeet
            });
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

            if (surface.InteriorRings.Count > 0)
            {
                warnings.Add($"Detailed geometry ignored {surface.InteriorRings.Count} interior ring(s) for {BuildFeatureLabel(feature, sourcePath, tileId)} on surface '{surface.SurfaceId}'. Only the exterior boundary was imported in this first pass.");
            }

            if (!PlateauPolygonTriangulator.TryTriangulate(localPoints, out IReadOnlyCollection<ContextShapeTriangle> surfaceTriangles))
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
            Triangles = triangles
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

    private static (double XFeet, double YFeet) ToLocalFeet(PlateauCoordinate3D point, PlateauImportReferenceContext referenceContext)
    {
        double deltaEastFeet = (point.X - referenceContext.AnchorProjectedCoordinate.Easting) * MetersToFeet;
        double deltaNorthFeet = (point.Y - referenceContext.AnchorProjectedCoordinate.Northing) * MetersToFeet;
        return (
            XFeet: referenceContext.AnchorXFeet + (deltaEastFeet * referenceContext.SharedEastToLocalX) + (deltaNorthFeet * referenceContext.SharedNorthToLocalX),
            YFeet: referenceContext.AnchorYFeet + (deltaEastFeet * referenceContext.SharedEastToLocalY) + (deltaNorthFeet * referenceContext.SharedNorthToLocalY));
    }

    private static ContextShapePoint3D ToLocalPointFeet(PlateauCoordinate3D point, PlateauImportReferenceContext referenceContext)
    {
        (double xFeet, double yFeet) = ToLocalFeet(point, referenceContext);
        return new ContextShapePoint3D(xFeet, yFeet, ToLocalElevationFeet(point.Z, referenceContext));
    }

    private static double ToLocalElevationFeet(double pointElevationMeters, PlateauImportReferenceContext referenceContext)
    {
        return referenceContext.AnchorZFeet + ((pointElevationMeters - referenceContext.AnchorElevationMeters) * MetersToFeet);
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
