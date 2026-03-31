using System;
using System.Collections.Generic;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.Core.Validation;

public sealed class CoordinateValidator
{
    private const double MinJapanLatitude = 20.0;
    private const double MaxJapanLatitude = 46.0;
    private const double MinJapanLongitude = 122.0;
    private const double MaxJapanLongitude = 154.0;
    private const double MinElevationMeters = -500.0;
    private const double MaxElevationMeters = 5000.0;
    private const double LargeOffsetThresholdMeters = 100000.0;

    private readonly ICrsRegistry crsRegistry;
    private readonly ICoordinateTransformer coordinateTransformer;
    private readonly IMeshCalculator meshCalculator;

    public CoordinateValidator(ICrsRegistry crsRegistry, ICoordinateTransformer coordinateTransformer, IMeshCalculator meshCalculator)
    {
        this.crsRegistry = crsRegistry ?? throw new ArgumentNullException(nameof(crsRegistry));
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
        this.meshCalculator = meshCalculator ?? throw new ArgumentNullException(nameof(meshCalculator));
    }

    public IReadOnlyCollection<ValidationResult> Validate(GeoProjectInfo? info)
    {
        List<ValidationResult> results = new List<ValidationResult>();

        if (info is null)
        {
            results.Add(new ValidationResult("missing-info", ValidationSeverity.Warning, "Geo project information is missing."));
            return results;
        }

        CrsDefinition? definition = null;
        if (info.ProjectCrs is null)
        {
            results.Add(new ValidationResult("missing-crs", ValidationSeverity.Warning, "CRS reference is missing."));
        }
        else if (!crsRegistry.TryGetByEpsgCode(info.ProjectCrs.EpsgCode, out definition) || definition is null)
        {
            results.Add(new ValidationResult("invalid-crs", ValidationSeverity.Warning, $"CRS EPSG:{info.ProjectCrs.EpsgCode} is not available in the registry."));
        }

        if (info.Origin is null)
        {
            results.Add(new ValidationResult("missing-origin", ValidationSeverity.Warning, "Project origin is missing."));
            return results;
        }

        GeographicCoordinate geographicCoordinate = new GeographicCoordinate(info.Origin.Latitude, info.Origin.Longitude);
        ValidateGeographicCoordinate(geographicCoordinate, info.Origin.ElevationMeters, results);

        if (definition is not null && info.ProjectCrs is not null)
        {
            ProjectedCoordinate projectedCoordinate = coordinateTransformer.Project(geographicCoordinate, info.ProjectCrs);
            ValidateProjectedCoordinate(projectedCoordinate, definition, results);
        }

        if (info.PrimaryMeshCode is not null)
        {
            string expectedMeshCode = meshCalculator.Calculate(info.Origin.Latitude, info.Origin.Longitude, JapanMeshLevel.Tertiary).Value;
            if (!string.Equals(expectedMeshCode, info.PrimaryMeshCode.Value, StringComparison.Ordinal))
            {
                results.Add(new ValidationResult("stale-mesh", ValidationSeverity.Warning, "Primary mesh code does not match the current canonical location state."));
            }
        }

        return results;
    }

    private static void ValidateGeographicCoordinate(GeographicCoordinate coordinate, double elevationMeters, ICollection<ValidationResult> results)
    {
        if (coordinate.Latitude < MinJapanLatitude || coordinate.Latitude > MaxJapanLatitude || coordinate.Longitude < MinJapanLongitude || coordinate.Longitude > MaxJapanLongitude)
        {
            results.Add(new ValidationResult("outside-japan-range", ValidationSeverity.Warning, "Origin latitude/longitude falls outside the expected Japan bounding box."));
        }

        if (elevationMeters < MinElevationMeters || elevationMeters > MaxElevationMeters)
        {
            results.Add(new ValidationResult("suspicious-elevation", ValidationSeverity.Warning, "Origin elevation falls outside the expected V1 soft-warning range."));
        }
    }

    private static void ValidateProjectedCoordinate(ProjectedCoordinate coordinate, CrsDefinition definition, ICollection<ValidationResult> results)
    {
        if (!coordinate.IsFinite)
        {
            results.Add(new ValidationResult("invalid-projected-coordinate", ValidationSeverity.Warning, "Projected coordinate values are not finite."));
            return;
        }

        if (coordinate.DistanceFromOriginMeters > LargeOffsetThresholdMeters)
        {
            results.Add(new ValidationResult(
                "large-offset",
                ValidationSeverity.Warning,
                $"Projected coordinate is more than 100 km from the CRS origin for EPSG:{definition.EpsgCode}."));
        }
    }
}
