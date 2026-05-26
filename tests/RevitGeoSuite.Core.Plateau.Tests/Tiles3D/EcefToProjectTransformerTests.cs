using System;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests.Tiles3D;

public sealed class EcefToProjectTransformerTests
{
    [Fact]
    public void TransformEcefToProject_converts_projected_coordinate_to_revit_local_metres_when_anchor_is_configured()
    {
        FakeCoordinateTransformer coordinateTransformer = new FakeCoordinateTransformer(new ProjectedCoordinate(150d, 275d));
        EcefToProjectTransformer transformer = new EcefToProjectTransformer(
            coordinateTransformer,
            new CrsReference { EpsgCode = 6677, NameSnapshot = "Test CRS" },
            new ProjectedCoordinate(100d, 200d),
            anchorElevationMeters: 50d,
            anchorXFeet: 10d,
            anchorYFeet: 20d,
            anchorZFeet: 30d,
            sharedEastToLocalX: 0d,
            sharedEastToLocalY: 1d,
            sharedNorthToLocalX: -1d,
            sharedNorthToLocalY: 0d);

        Vector3d ecef = EcefGeodeticConverter.ToEcef(new GeodeticCoordinate(0d, 0d, 80d));

        Vector3d local = transformer.TransformEcefToProject(ecef);

        Assert.Equal(-71.952d, local.X, 6);
        Assert.Equal(56.096d, local.Y, 6);
        Assert.Equal(39.144d, local.Z, 6);
    }

    private sealed class FakeCoordinateTransformer : ICoordinateTransformer
    {
        private readonly ProjectedCoordinate projectedCoordinate;

        public FakeCoordinateTransformer(ProjectedCoordinate projectedCoordinate)
        {
            this.projectedCoordinate = projectedCoordinate;
        }

        public ProjectedCoordinate Project(GeographicCoordinate coordinate, CrsReference targetCrs)
        {
            return projectedCoordinate;
        }

        public GeographicCoordinate Unproject(ProjectedCoordinate coordinate, CrsReference sourceCrs)
        {
            throw new NotSupportedException();
        }
    }
}
