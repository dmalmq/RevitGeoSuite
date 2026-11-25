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
            sharedNorthToLocalY: 0d,
            outputVerticalDatum: VerticalDatum.Ellipsoidal);

        Vector3d ecef = EcefGeodeticConverter.ToEcef(new GeodeticCoordinate(0d, 0d, 80d));

        Vector3d local = transformer.TransformEcefToProject(ecef);

        Assert.Equal(-71.952d, local.X, 6);
        Assert.Equal(56.096d, local.Y, 6);
        Assert.Equal(39.144d, local.Z, 6);
    }

    [Fact]
    public void TransformEcefToProject_subtracts_geoid_undulation_in_tokyo_so_buildings_sit_flush_on_orthometric_anchor()
    {
        // Tokyo Station (Chiyoda): orthometric ground elevation 4.0 m T.P.
        // WGS84 ellipsoidal height in 3D Tiles = H_orthometric + N_geoid (~4.0 + 36.7 = ~40.7 m).
        const double tokyoLat = 35.681236d;
        const double tokyoLon = 139.767125d;
        const double orthometricGroundMeters = 4.0d;
        double geoidUndulationMeters = Egm2008Geoid.GetUndulationMeters(tokyoLat, tokyoLon);
        Assert.InRange(geoidUndulationMeters, 36.0d, 37.5d);

        FakeCoordinateTransformer coordinateTransformer = new FakeCoordinateTransformer(new ProjectedCoordinate(0d, 0d));
        EcefToProjectTransformer transformer = new EcefToProjectTransformer(
            coordinateTransformer,
            new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 Zone IX" },
            new ProjectedCoordinate(0d, 0d),
            anchorElevationMeters: orthometricGroundMeters,
            anchorXFeet: 0d,
            anchorYFeet: 0d,
            anchorZFeet: 0d,
            sharedEastToLocalX: 1d,
            sharedEastToLocalY: 0d,
            sharedNorthToLocalX: 0d,
            sharedNorthToLocalY: 1d);

        Vector3d buildingBaseEcef = EcefGeodeticConverter.ToEcef(
            new GeodeticCoordinate(tokyoLat, tokyoLon, orthometricGroundMeters + geoidUndulationMeters));

        Vector3d local = transformer.TransformEcefToProject(buildingBaseEcef);

        // Must sit flush at ~0 m relative offset (NOT floating +36.7 m above ground!).
        Assert.Equal(VerticalDatum.Orthometric, transformer.OutputVerticalDatum);
        Assert.InRange(local.Z, -0.01d, 0.01d);
    }

    [Fact]
    public void TransformEcefToProject_respects_custom_geoid_model_and_preserves_orthometric_geographic_inputs()
    {
        const double customUndulation = 36.82d; // e.g., JPGEO2024 value at a known point
        FakeCoordinateTransformer coordinateTransformer = new FakeCoordinateTransformer(new ProjectedCoordinate(10d, 20d));
        EcefToProjectTransformer transformer = new EcefToProjectTransformer(
            coordinateTransformer,
            new CrsReference { EpsgCode = 6677 },
            altitudeAnchorMeters: 10d,
            outputVerticalDatum: VerticalDatum.Orthometric,
            geoidModel: new ConstantGeoidModel(customUndulation, "JPGEO2024-Test"));

        Vector3d ecef = EcefGeodeticConverter.ToEcef(new GeodeticCoordinate(35.6895d, 139.6917d, 10d + customUndulation));
        Vector3d fromEcef = transformer.TransformEcefToProject(ecef);
        Assert.Equal(0d, fromEcef.Z, 6);

        // Orthometric geographic input (default for 2D/orthometric callers like MVT) must not subtract geoid twice.
        Vector3d fromOrthometricGeo = transformer.TransformGeographicToProject(35.6895d, 139.6917d, 10d);
        Assert.Equal(0d, fromOrthometricGeo.Z, 6);
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
