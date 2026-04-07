using System;
using System.Collections.Generic;
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

    public ContextImportPlan BuildPlan(PlateauCityModel cityModel, PlateauImportReferenceContext referenceContext)
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
        return BuildPlan(scanResult, referenceContext, null, null);
    }

    public ContextImportPlan BuildPlan(
        PlateauFolderScanResult scanResult,
        PlateauImportReferenceContext referenceContext,
        IReadOnlyCollection<PlateauFeatureType>? selectedFeatureTypes,
        IReadOnlyCollection<string>? selectedTileIds)
    {
        if (scanResult is null)
        {
            throw new ArgumentNullException(nameof(scanResult));
        }

        if (referenceContext is null)
        {
            throw new ArgumentNullException(nameof(referenceContext));
        }

        HashSet<PlateauFeatureType> selectedTypes = new HashSet<PlateauFeatureType>(
            (selectedFeatureTypes is null || selectedFeatureTypes.Count == 0)
                ? scanResult.CityModels.SelectMany(model => model.Features).Select(feature => feature.FeatureType)
                : selectedFeatureTypes);
        HashSet<string> selectedTiles = new HashSet<string>(
            (selectedTileIds is null || selectedTileIds.Count == 0)
                ? scanResult.CityModels.SelectMany(model => model.Features).Select(feature => ResolveTileId(feature, model: null))
                : selectedTileIds,
            StringComparer.Ordinal);

        List<ContextSolidPlan> solids = new List<ContextSolidPlan>();
        int sourceFeatureCount = 0;

        foreach (PlateauCityModel cityModel in scanResult.CityModels)
        {
            foreach (PlateauContextFeature feature in cityModel.Features)
            {
                string tileId = ResolveTileId(feature, cityModel);
                if (!selectedTypes.Contains(feature.FeatureType) || !selectedTiles.Contains(tileId))
                {
                    continue;
                }

                sourceFeatureCount++;
                List<PlateauCoordinate3D> ring = NormalizeRing(feature.ExteriorRing);
                if (ring.Count < 3)
                {
                    continue;
                }

                List<PlateauCoordinate3D> transformedRing = ring
                    .Select(point => TransformPoint(point, cityModel, referenceContext))
                    .ToList();
                (double minimumHeightMeters, double defaultHeightMeters) = GetHeightParameters(feature.FeatureType);
                double minZ = transformedRing.Min(point => point.Z);
                double maxZ = transformedRing.Max(point => point.Z);
                double heightMeters = Math.Max(minimumHeightMeters, maxZ - minZ);
                if (heightMeters <= minimumHeightMeters)
                {
                    heightMeters = defaultHeightMeters;
                }

                solids.Add(new ContextSolidPlan
                {
                    DisplayName = string.IsNullOrWhiteSpace(feature.Name) ? feature.Id : feature.Name,
                    SourceFeatureId = string.IsNullOrWhiteSpace(feature.Id) ? Guid.NewGuid().ToString("N") : feature.Id,
                    FeatureType = feature.FeatureType,
                    TileId = tileId,
                    SourceFilePath = cityModel.SourcePath,
                    FootprintPointsFeet = transformedRing
                        .Select(point => ToLocalFeet(point, referenceContext))
                        .ToArray(),
                    BaseElevationFeet = ToLocalElevationFeet(minZ, referenceContext),
                    HeightFeet = heightMeters * MetersToFeet
                });
            }
        }

        return new ContextImportPlan
        {
            SourceFolderPath = scanResult.FolderPath,
            ReferenceContext = referenceContext,
            SourceModels = scanResult.CityModels,
            SelectedFeatureTypes = selectedTypes.OrderBy(type => type).ToArray(),
            SelectedTileIds = selectedTiles.OrderBy(tileId => tileId, StringComparer.Ordinal).ToArray(),
            Solids = solids,
            WarningMessages = scanResult.WarningMessages,
            SourceFeatureCount = sourceFeatureCount
        };
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

    private static (double XFeet, double YFeet) ToLocalFeet(PlateauCoordinate3D point, PlateauImportReferenceContext referenceContext)
    {
        double deltaEastFeet = (point.X - referenceContext.AnchorProjectedCoordinate.Easting) * MetersToFeet;
        double deltaNorthFeet = (point.Y - referenceContext.AnchorProjectedCoordinate.Northing) * MetersToFeet;
        return (
            XFeet: referenceContext.AnchorXFeet + (deltaEastFeet * referenceContext.SharedEastToLocalX) + (deltaNorthFeet * referenceContext.SharedNorthToLocalX),
            YFeet: referenceContext.AnchorYFeet + (deltaEastFeet * referenceContext.SharedEastToLocalY) + (deltaNorthFeet * referenceContext.SharedNorthToLocalY));
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

        throw new InvalidOperationException($"The CityGML file uses EPSG:{sourceEpsg}, which is not supported yet for lightweight PLATEAU import. Supported file CRSs are EPSG:6668, EPSG:6697, and projected Japanese zones EPSG:6669-6687.");
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
}



