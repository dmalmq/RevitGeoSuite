using System;
using RevitGeoSuite.Core.Coordinates;

namespace RevitGeoSuite.Georeference;

public sealed class ManualProjectBasePointResolver
{
    private readonly ICoordinateTransformer coordinateTransformer;

    public ManualProjectBasePointResolver(ICoordinateTransformer coordinateTransformer)
    {
        this.coordinateTransformer = coordinateTransformer ?? throw new ArgumentNullException(nameof(coordinateTransformer));
    }

    public ManualProjectBasePointSelection Resolve(double easting, double northing, CrsReference? projectCrs)
    {
        if (projectCrs is null)
        {
            throw new InvalidOperationException("Select a coordinate reference system before resolving the Project Base Point.");
        }

        var projected = new ProjectedCoordinate(easting, northing);
        if (!projected.IsFinite)
        {
            throw new InvalidOperationException("Project Base Point Easting and Northing must be finite numbers.");
        }

        GeographicCoordinate geographic = coordinateTransformer.Unproject(projected, projectCrs);
        if (!IsFinite(geographic.Latitude) || !IsFinite(geographic.Longitude))
        {
            throw new InvalidOperationException("The entered Project Base Point coordinates could not be converted to a valid geographic point.");
        }

        return new ManualProjectBasePointSelection
        {
            AnchorLatitude = geographic.Latitude,
            AnchorLongitude = geographic.Longitude,
            ProjectedCoordinate = projected
        };
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
