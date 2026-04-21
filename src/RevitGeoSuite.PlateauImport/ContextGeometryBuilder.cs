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
            FolderPath = Path.GetDirectoryName(cityModel.SourcePath) ?? string.Empty,
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

        ResolvedContextFeature[] resolvedFeatures = ResolveFeatures(scanResult).ToArray();
        HashSet<PlateauFeatureType> selectedTypes = new HashSet<PlateauFeatureType>(
            (selectedFeatureTypes is null || selectedFeatureTypes.Count == 0)
                ? resolvedFeatures.Select(item => item.Feature.FeatureType)
                : selectedFeatureTypes);
        HashSet<string> selectedTiles = new HashSet<string>(
            (selectedTileIds is null || selectedTileIds.Count == 0)
                ? resolvedFeatures.Select(item => item.TileId)
                : selectedTileIds,
            StringComparer.Ordinal);

        List<ContextShapePlan> shapes = new List<ContextShapePlan>();
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
            if (geometryImportMode == PlateauGeometryImportMode.DetailedDirectShape)
            {
                if (TryBuildDetailedShape(feature, cityModel, referenceContext, tileId, warnings, out ContextShapePlan? shape))
                {
                    shapes.Add(shape!);
                    preparedSurfaceCount += shape!.SurfaceCount;
                    preparedTriangleCount += shape.Triangles.Count;
                }

                continue;
            }

            List<PlateauCoordinate3D> ring = NormalizeRing(feature.ExteriorRing);
            if (ring.Count < 3)
            {
                continue;
            }

            List<PlateauCoordinate3D> transformedRing = ring
                .Select(point => TransformPoint(point, cityModel, referenceContext))
                .ToList();
            (double minimumHeightMeters, double defaultHeightMeters) = GetHeightParameters(feature.FeatureType);
            (double baseElevationMeters, double heightMeters) = ResolveElevationAndHeight(
                feature,
                cityModel,
                transformedRing,
                minimumHeightMeters,
                defaultHeightMeters,
                warnings);

            shapes.Add(new ContextShapePlan
            {
                DisplayName = string.IsNullOrWhiteSpace(feature.Name) ? feature.Id : feature.Name,
                SourceFeatureId = string.IsNullOrWhiteSpace(feature.Id) ? Guid.NewGuid().ToString("N") : feature.Id,
                FeatureType = feature.FeatureType,
                TileId = tileId,
                SourceFilePath = cityModel.SourcePath,
                GeometryMode = PlateauGeometryImportMode.LightweightExtrusion,
                FootprintPointsFeet = transformedRing
                    .Select(point => ToLocalFeet(point, referenceContext))
                    .ToArray(),
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
        PlateauCityModel cityModel,
        PlateauImportReferenceContext referenceContext,
        string tileId,
        ICollection<string> warnings,
        out ContextShapePlan? shape)
    {
        shape = null;
        PlateauGeometrySurface[] surfaces = feature.GeometrySurfaces
            .Where(surface => NormalizeRing(surface.ExteriorRing).Count >= 3)
            .ToArray();
        if (surfaces.Length == 0)
        {
            warnings.Add($"Skipped {BuildFeatureLabel(feature, cityModel.SourcePath, tileId)} because no usable highest-LOD surfaces were available for detailed geometry import.");
            return false;
        }

        List<ContextShapeTriangle> triangles = new List<ContextShapeTriangle>();
        int importedSurfaceCount = 0;
        foreach (PlateauGeometrySurface surface in surfaces)
        {
            List<ContextShapePoint3D> localPoints = NormalizeRing(surface.ExteriorRing)
                .Select(point => TransformPoint(point, cityModel, referenceContext))
                .Select(point => ToLocalPointFeet(point, referenceContext))
                .ToList();
            if (localPoints.Count < 3)
            {
                warnings.Add($"Skipped a detailed surface for {BuildFeatureLabel(feature, cityModel.SourcePath, tileId)} because it collapsed below the minimum point count after coordinate conversion.");
                continue;
            }

            if (surface.InteriorRings.Count > 0)
            {
                warnings.Add($"Detailed geometry ignored {surface.InteriorRings.Count} interior ring(s) for {BuildFeatureLabel(feature, cityModel.SourcePath, tileId)} on surface '{surface.SurfaceId}'. Only the exterior boundary was imported in this first pass.");
            }

            if (!PlateauPolygonTriangulator.TryTriangulate(localPoints, out IReadOnlyCollection<ContextShapeTriangle> surfaceTriangles))
            {
                warnings.Add($"Skipped a detailed surface for {BuildFeatureLabel(feature, cityModel.SourcePath, tileId)} because the polygon could not be triangulated safely.");
                continue;
            }

            triangles.AddRange(surfaceTriangles);
            importedSurfaceCount++;
        }

        if (triangles.Count == 0)
        {
            warnings.Add($"Skipped {BuildFeatureLabel(feature, cityModel.SourcePath, tileId)} because none of its detailed surfaces could be triangulated into importable geometry.");
            return false;
        }

        shape = new ContextShapePlan
        {
            DisplayName = string.IsNullOrWhiteSpace(feature.Name) ? feature.Id : feature.Name,
            SourceFeatureId = string.IsNullOrWhiteSpace(feature.Id) ? Guid.NewGuid().ToString("N") : feature.Id,
            FeatureType = feature.FeatureType,
            TileId = tileId,
            SourceFilePath = cityModel.SourcePath,
            GeometryMode = PlateauGeometryImportMode.DetailedDirectShape,
            SurfaceCount = importedSurfaceCount,
            Triangles = triangles
        };
        return true;
    }

    private static (double BaseElevationMeters, double HeightMeters) ResolveElevationAndHeight(
        PlateauContextFeature feature,
        PlateauCityModel cityModel,
        IReadOnlyCollection<PlateauCoordinate3D> transformedRing,
        double minimumHeightMeters,
        double defaultHeightMeters,
        ICollection<string> warnings)
    {
        double minZ = transformedRing.Min(point => point.Z);
        double maxZ = transformedRing.Max(point => point.Z);
        double baseElevationMeters = minZ;
        double topElevationMeters = maxZ;

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

    private static IEnumerable<ResolvedContextFeature> ResolveFeatures(PlateauFolderScanResult scanResult)
    {
        foreach (PlateauCityModel cityModel in scanResult.CityModels)
        {
            foreach (PlateauContextFeature feature in cityModel.Features)
            {
                yield return new ResolvedContextFeature(cityModel, feature, ResolveTileId(feature, cityModel));
            }
        }
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

    private PlateauCoordinate3D TransformPoint(
        PlateauCoordinate3D point,
        PlateauCityModel cityModel,
        PlateauImportReferenceContext referenceContext)
    {
        if (!cityModel.EpsgCode.HasValue || cityModel.EpsgCode.Value == referenceContext.ProjectCrs.EpsgCode)
        {
            return point;
        }

        int sourceEpsg = cityModel.EpsgCode.Value;
        if (IsSupportedProjectedJgd2011(sourceEpsg))
        {
            GeographicCoordinate geographic = coordinateTransformer.Unproject(
                new ProjectedCoordinate(point.X, point.Y),
                new CrsReference { EpsgCode = sourceEpsg, NameSnapshot = cityModel.SrsName });
            ProjectedCoordinate projected = coordinateTransformer.Project(geographic, referenceContext.ProjectCrs);
            return new PlateauCoordinate3D(projected.Easting, projected.Northing, point.Z);
        }

        if (IsSupportedGeographicJgd2011(sourceEpsg))
        {
            GeographicCoordinate geographic = new GeographicCoordinate(point.X, point.Y);
            ProjectedCoordinate projected = coordinateTransformer.Project(geographic, referenceContext.ProjectCrs);
            return new PlateauCoordinate3D(projected.Easting, projected.Northing, point.Z);
        }

        throw new InvalidOperationException($"The CityGML file uses EPSG:{sourceEpsg}, which is not supported yet for PLATEAU import. Supported file CRSs are EPSG:6668, EPSG:6697, and projected Japanese zones EPSG:6669-6687.");
    }

    private static bool IsSupportedProjectedJgd2011(int epsgCode)
    {
        return epsgCode >= 6669 && epsgCode <= 6687;
    }

    private static bool IsSupportedGeographicJgd2011(int epsgCode)
    {
        return epsgCode == Jgd2011GeographicEpsg || epsgCode == Jgd2011CompoundHeightEpsg;
    }

    private static List<PlateauCoordinate3D> NormalizeRing(IReadOnlyCollection<PlateauCoordinate3D> ring)
    {
        List<PlateauCoordinate3D> points = ring?.ToList() ?? new List<PlateauCoordinate3D>();
        if (points.Count > 1)
        {
            PlateauCoordinate3D first = points[0];
            PlateauCoordinate3D last = points[points.Count - 1];
            if (first.X == last.X && first.Y == last.Y && first.Z == last.Z)
            {
                points.RemoveAt(points.Count - 1);
            }
        }

        return points;
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

    private sealed record ResolvedContextFeature(PlateauCityModel CityModel, PlateauContextFeature Feature, string TileId);
}
