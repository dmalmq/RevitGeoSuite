using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.PlateauImport;

public sealed class ContextGeometryBuilder
{
    private const double MetersToFeet = 1.0 / 0.3048d;
    private const double DefaultHeightMeters = 10.0d;
    private const double MinimumImportHeightMeters = 3.0d;

    public ContextImportPlan BuildPlan(PlateauCityModel cityModel, PlateauImportReferenceContext referenceContext)
    {
        if (cityModel is null)
        {
            throw new ArgumentNullException(nameof(cityModel));
        }

        if (referenceContext is null)
        {
            throw new ArgumentNullException(nameof(referenceContext));
        }

        if (cityModel.EpsgCode.HasValue && cityModel.EpsgCode.Value != referenceContext.ProjectCrs.EpsgCode)
        {
            throw new InvalidOperationException($"The CityGML file uses EPSG:{cityModel.EpsgCode.Value}, but the project reference context uses EPSG:{referenceContext.ProjectCrs.EpsgCode}. Lightweight V1 import requires matching CRS definitions.");
        }

        List<ContextSolidPlan> solids = new List<ContextSolidPlan>();
        foreach (PlateauBuildingFeature building in cityModel.Buildings)
        {
            List<PlateauCoordinate3D> ring = NormalizeRing(building.ExteriorRing);
            if (ring.Count < 3)
            {
                continue;
            }

            double minZ = ring.Min(point => point.Z);
            double maxZ = ring.Max(point => point.Z);
            double heightMeters = Math.Max(MinimumImportHeightMeters, maxZ - minZ);
            if (heightMeters <= MinimumImportHeightMeters)
            {
                heightMeters = DefaultHeightMeters;
            }

            solids.Add(new ContextSolidPlan
            {
                DisplayName = string.IsNullOrWhiteSpace(building.Name) ? building.Id : building.Name,
                SourceFeatureId = string.IsNullOrWhiteSpace(building.Id) ? Guid.NewGuid().ToString("N") : building.Id,
                FootprintPointsFeet = ring
                    .Select(point => (
                        XFeet: referenceContext.AnchorXFeet + ((point.X - referenceContext.AnchorProjectedCoordinate.Easting) * MetersToFeet),
                        YFeet: referenceContext.AnchorYFeet + ((point.Y - referenceContext.AnchorProjectedCoordinate.Northing) * MetersToFeet)))
                    .ToArray(),
                BaseElevationFeet = referenceContext.AnchorZFeet,
                HeightFeet = heightMeters * MetersToFeet
            });
        }

        return new ContextImportPlan
        {
            ReferenceContext = referenceContext,
            CityModel = cityModel,
            Solids = solids
        };
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
}
