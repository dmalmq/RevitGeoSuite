namespace RevitGeoSuite.Core.Coordinates;

public interface ICoordinateTransformer
{
    ProjectedCoordinate Project(GeographicCoordinate coordinate, CrsReference targetCrs);

    GeographicCoordinate Unproject(ProjectedCoordinate coordinate, CrsReference sourceCrs);
}
