using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Dem;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauGroundSurfaceBuilderTests
{
    private const double MetersToFeet = 1.0 / 0.3048d;

    // A flat 100×100 m plane at elevation 50 m, split into two triangles.
    private static DemSampler FlatPlaneSampler(double elevationMeters = 50d)
    {
        Vector3d a = new Vector3d(0d, 0d, elevationMeters);
        Vector3d b = new Vector3d(100d, 0d, elevationMeters);
        Vector3d c = new Vector3d(100d, 100d, elevationMeters);
        Vector3d d = new Vector3d(0d, 100d, elevationMeters);
        return new DemSampler(new[] { (a, b, c), (a, c, d) });
    }

    private static PlateauImportReferenceContext IdentityContext()
    {
        return new PlateauImportReferenceContext
        {
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorElevationMeters = 0d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 0d,
            SharedEastToLocalX = 1d,
            SharedEastToLocalY = 0d,
            SharedNorthToLocalX = 0d,
            SharedNorthToLocalY = 1d
        };
    }

    [Fact]
    public void Build_samples_a_full_grid_at_the_correct_height()
    {
        PlateauGroundSurfaceBuilder.GroundSurfaceResult result =
            new PlateauGroundSurfaceBuilder().Build(FlatPlaneSampler(), IdentityContext(), gridSpacingMeters: 25d, geoidOffsetMeters: 0d);

        // 0..100 at 25 m spacing → 5 columns × 5 rows, all inside the plane.
        Assert.Equal(25, result.PointCount);
        Assert.Equal(0, result.SkippedSampleCount);
        Assert.Equal(25d, result.EffectiveSpacingMeters, 9);
        Assert.All(result.Points, point => Assert.Equal(50d * MetersToFeet, point.ZFeet, 6));

        // The centre grid node (50 m, 50 m) maps to (50/0.3048, 50/0.3048) ft.
        Assert.Contains(result.Points, point =>
            Math.Abs(point.XFeet - 50d * MetersToFeet) < 1e-6 &&
            Math.Abs(point.YFeet - 50d * MetersToFeet) < 1e-6);
    }

    [Fact]
    public void Build_subtracts_the_geoid_offset_from_each_height()
    {
        PlateauGroundSurfaceBuilder.GroundSurfaceResult result =
            new PlateauGroundSurfaceBuilder().Build(FlatPlaneSampler(), IdentityContext(), gridSpacingMeters: 50d, geoidOffsetMeters: 10d);

        Assert.NotEmpty(result.Points);
        Assert.All(result.Points, point => Assert.Equal((50d - 10d) * MetersToFeet, point.ZFeet, 6));
    }

    [Fact]
    public void Build_coarsens_the_grid_to_respect_the_point_cap()
    {
        // Cap of 16 over a 100×100 area forces spacing up to sqrt(10000/16) = 25 m.
        PlateauGroundSurfaceBuilder.GroundSurfaceResult result =
            new PlateauGroundSurfaceBuilder(maxPoints: 16).Build(FlatPlaneSampler(), IdentityContext(), gridSpacingMeters: 5d, geoidOffsetMeters: 0d);

        Assert.True(result.EffectiveSpacingMeters > result.RequestedSpacingMeters);
        Assert.Equal(25d, result.EffectiveSpacingMeters, 6);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Build_caps_each_axis_for_an_elongated_extent_without_throwing()
    {
        // 100 km × 100 m flat strip: the total-point cap alone would let one axis request a giant
        // array (this is what threw "Array dimensions exceeded supported range"). Each axis must be
        // capped instead.
        Vector3d a = new Vector3d(0d, 0d, 10d);
        Vector3d b = new Vector3d(100_000d, 0d, 10d);
        Vector3d c = new Vector3d(100_000d, 100d, 10d);
        Vector3d d = new Vector3d(0d, 100d, 10d);
        DemSampler sampler = new DemSampler(new[] { (a, b, c), (a, c, d) });

        PlateauGroundSurfaceBuilder.GroundSurfaceResult result =
            new PlateauGroundSurfaceBuilder().Build(sampler, IdentityContext(), gridSpacingMeters: 10d, geoidOffsetMeters: 0d);

        Assert.True(result.PointCount > 0);
        Assert.True(result.PointCount <= 40_000);
        Assert.Contains(result.Warnings, warning => warning.Contains("elongated"));
        Assert.All(result.Points, point => Assert.Equal(10d * MetersToFeet, point.ZFeet, 6));
    }

    [Fact]
    public void Build_returns_no_points_for_an_empty_sampler()
    {
        DemSampler empty = new DemSampler(Array.Empty<(Vector3d, Vector3d, Vector3d)>());

        PlateauGroundSurfaceBuilder.GroundSurfaceResult result =
            new PlateauGroundSurfaceBuilder().Build(empty, IdentityContext(), gridSpacingMeters: 10d, geoidOffsetMeters: 0d);

        Assert.Equal(0, result.PointCount);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Build_with_geoid_model_matches_ecef_to_project_transformer_vertical_datum_in_tokyo()
    {
        const double tokyoLat = 35.681236d;
        const double tokyoLon = 139.767125d;
        const double orthometricAnchorMeters = 4.0d;
        double geoidUndulation = Egm2008GeoidModel.Instance.GetUndulationMeters(tokyoLat, tokyoLon);
        double ellipsoidalGroundHeight = orthometricAnchorMeters + geoidUndulation;

        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 Zone IX" },
            AnchorLatitude = tokyoLat,
            AnchorLongitude = tokyoLon,
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorElevationMeters = orthometricAnchorMeters,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 0d,
            SharedEastToLocalX = 1d,
            SharedEastToLocalY = 0d,
            SharedNorthToLocalX = 0d,
            SharedNorthToLocalY = 1d
        };

        PlateauGroundSurfaceBuilder.GroundSurfaceResult ground = new PlateauGroundSurfaceBuilder()
            .Build(FlatPlaneSampler(ellipsoidalGroundHeight), referenceContext, gridSpacingMeters: 50d, geoidModel: Egm2008GeoidModel.Instance);

        EcefToProjectTransformer buildingTransformer = new EcefToProjectTransformer(
            new ZeroCoordinateTransformer(),
            referenceContext.ProjectCrs,
            referenceContext.AnchorProjectedCoordinate,
            referenceContext.AnchorElevationMeters,
            referenceContext.AnchorXFeet,
            referenceContext.AnchorYFeet,
            referenceContext.AnchorZFeet,
            referenceContext.SharedEastToLocalX,
            referenceContext.SharedEastToLocalY,
            referenceContext.SharedNorthToLocalX,
            referenceContext.SharedNorthToLocalY);

        Vector3d buildingBaseLocal = buildingTransformer.TransformEcefToProject(
            EcefGeodeticConverter.ToEcef(new GeodeticCoordinate(tokyoLat, tokyoLon, ellipsoidalGroundHeight)));

        Assert.NotEmpty(ground.Points);
        double groundZMeters = ground.Points[0].ZFeet * 0.3048d;
        Assert.InRange(Math.Abs(buildingBaseLocal.Z - groundZMeters), 0d, 0.01d);
        Assert.InRange(groundZMeters, -0.01d, 0.01d);
    }

    private sealed class ZeroCoordinateTransformer : ICoordinateTransformer
    {
        public ProjectedCoordinate Project(GeographicCoordinate coordinate, CrsReference targetCrs) => new ProjectedCoordinate(0d, 0d);
        public GeographicCoordinate Unproject(ProjectedCoordinate coordinate, CrsReference sourceCrs) => throw new NotSupportedException();
    }
}
