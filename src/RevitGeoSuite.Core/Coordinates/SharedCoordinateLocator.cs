using System;

namespace RevitGeoSuite.Core.Coordinates;

/// <summary>
/// Converts Revit shared coordinates to latitude and longitude. Shared east and north are absolute
/// values in the project CRS, so they are never applied as offsets from a site or stored origin.
/// </summary>
public sealed class SharedCoordinateLocator
{
    private readonly ICrsRegistry crsRegistry;
    private readonly ICoordinateTransformer coordinateTransformer;

    public SharedCoordinateLocator(ICrsRegistry crsRegistry)
    {
        this.crsRegistry = crsRegistry ?? throw new ArgumentNullException(nameof(crsRegistry));
        coordinateTransformer = new CoordinateTransformer(crsRegistry);
    }

    /// <summary>Returns null when the CRS is missing or unsupported, or the position is not finite.</summary>
    public GeographicCoordinate? Locate(CrsReference? crs, double eastingMeters, double northingMeters)
    {
        if (crs is null
            || !IsFinite(eastingMeters)
            || !IsFinite(northingMeters)
            || !crsRegistry.TryGetByEpsgCode(crs.EpsgCode, out _))
        {
            return null;
        }

        GeographicCoordinate located = coordinateTransformer.Unproject(
            new ProjectedCoordinate(eastingMeters, northingMeters),
            crs);

        return IsFinite(located.Latitude) && IsFinite(located.Longitude) ? located : null;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
