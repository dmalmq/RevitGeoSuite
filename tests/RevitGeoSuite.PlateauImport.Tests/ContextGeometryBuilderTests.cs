using System;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class ContextGeometryBuilderTests
{
    [Fact]
    public void BuildPlan_converts_projected_fixture_geometry_into_local_feet()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-origin-context.gml");
        PlateauCityModel model = new CityGmlParser().ParseFile(fixturePath);
        ContextGeometryBuilder builder = new ContextGeometryBuilder();
        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            Title = "Working Project Base Point",
            Description = "Test reference",
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorLatitude = 36d,
            AnchorLongitude = 139.833333333333d,
            AnchorElevationMeters = 0d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 0d
        };

        ContextImportPlan plan = builder.BuildPlan(model, referenceContext);
        ContextSolidPlan firstSolid = plan.Solids.First();
        var firstPoint = firstSolid.FootprintPointsFeet.First();

        Assert.Equal(2, plan.Solids.Count);
        Assert.Equal(328.084, firstPoint.XFeet, 3);
        Assert.Equal(492.126, firstPoint.YFeet, 3);
        Assert.Equal(0d, firstSolid.BaseElevationFeet, 3);
        Assert.Equal(32.808, firstSolid.HeightFeet, 3);
    }

    [Fact]
    public void BuildPlan_uses_elevated_ground_surface_when_parser_finds_nonzero_feature_base_elevation()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-elevated-ground.gml");
        PlateauCityModel model = new CityGmlParser().ParseFile(fixturePath);
        ContextGeometryBuilder builder = new ContextGeometryBuilder();
        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            Title = "Working Project Base Point",
            Description = "Test reference",
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorLatitude = 36d,
            AnchorLongitude = 139.833333333333d,
            AnchorElevationMeters = 40d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 1000d
        };

        ContextImportPlan plan = builder.BuildPlan(model, referenceContext);
        ContextSolidPlan firstSolid = Assert.Single(plan.Solids);

        Assert.Equal(1000d + ((42d - 40d) / 0.3048d), firstSolid.BaseElevationFeet, 3);
        Assert.Equal(49.213, firstSolid.HeightFeet, 3);
    }

    [Fact]
    public void BuildPlan_preserves_all_traffic_area_polygons_with_their_own_elevations()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-road-trafficarea-elevated.gml");
        PlateauCityModel model = new CityGmlParser().ParseFile(fixturePath);
        ContextGeometryBuilder builder = new ContextGeometryBuilder();
        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            Title = "Working Project Base Point",
            Description = "Test reference",
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorElevationMeters = 40d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 1000d
        };

        ContextImportPlan plan = builder.BuildPlan(model, referenceContext);

        Assert.Equal(2, plan.Solids.Count);
        Assert.Equal(new[] { 1000d + ((40.82d - 40d) / 0.3048d), 1000d + ((41.15d - 40d) / 0.3048d) }, plan.Solids.Select(solid => solid.BaseElevationFeet).ToArray());
        Assert.Equal(new[] { 1.640d, 1.640d }, plan.Solids.Select(solid => solid.HeightFeet).ToArray());
    }

    [Fact]
    public void BuildPlan_translates_absolute_source_elevation_relative_to_anchor_elevation()
    {
        PlateauFolderScanResult scanResult = new PlateauFolderScanResult
        {
            CityModels = new[]
            {
                new PlateauCityModel
                {
                    EpsgCode = 6677,
                    Features = new PlateauContextFeature[]
                    {
                        CreateFeature(
                            PlateauFeatureType.Building,
                            "elev-bldg",
                            "Elevated",
                            "tile-a",
                            new PlateauCoordinate3D(100.0, 150.0, 55.0),
                            new PlateauCoordinate3D(110.0, 150.0, 55.0),
                            new PlateauCoordinate3D(110.0, 160.0, 55.0),
                            new PlateauCoordinate3D(100.0, 160.0, 55.0),
                            new PlateauCoordinate3D(100.0, 150.0, 55.0))
                    }
                }
            }
        };
        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorElevationMeters = 42d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 1000d
        };

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(scanResult, referenceContext, new[] { PlateauFeatureType.Building }, new[] { "tile-a" });
        ContextSolidPlan solid = plan.Solids.Single();

        Assert.Equal(1000d + ((55d - 42d) / 0.3048d), solid.BaseElevationFeet, 3);
        Assert.Equal(32.808, solid.HeightFeet, 3);
    }

    [Fact]
    public void BuildPlan_applies_shared_to_local_basis_when_reference_context_is_rotated()
    {
        PlateauFolderScanResult scanResult = new PlateauFolderScanResult
        {
            CityModels = new[]
            {
                new PlateauCityModel
                {
                    EpsgCode = 6677,
                    Features = new PlateauContextFeature[]
                    {
                        CreateFeature(
                            PlateauFeatureType.Building,
                            "rotated-bldg",
                            "Rotated",
                            "tile-a",
                            new PlateauCoordinate3D(100.0, 150.0, 0),
                            new PlateauCoordinate3D(110.0, 150.0, 0),
                            new PlateauCoordinate3D(110.0, 160.0, 0),
                            new PlateauCoordinate3D(100.0, 160.0, 0),
                            new PlateauCoordinate3D(100.0, 150.0, 0))
                    }
                }
            }
        };
        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorElevationMeters = 0d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 0d,
            SharedEastToLocalX = 0d,
            SharedEastToLocalY = 1d,
            SharedNorthToLocalX = -1d,
            SharedNorthToLocalY = 0d
        };

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(scanResult, referenceContext, new[] { PlateauFeatureType.Building }, new[] { "tile-a" });
        var firstPoint = plan.Solids.Single().FootprintPointsFeet.First();

        Assert.Equal(-492.126, firstPoint.XFeet, 3);
        Assert.Equal(328.084, firstPoint.YFeet, 3);
    }

    [Fact]
    public void BuildPlan_transforms_epsg_6697_geographic_points_into_project_crs()
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        ContextGeometryBuilder builder = new ContextGeometryBuilder(coordinateTransformer);
        PlateauCityModel model = new PlateauCityModel
        {
            EpsgCode = 6697,
            SrsName = "urn:ogc:def:crs:EPSG::6697",
            Features = new PlateauContextFeature[]
            {
                CreateFeature(
                    PlateauFeatureType.Building,
                    "bldg-6697",
                    "Sample Geographic Building",
                    "tile-a",
                    new PlateauCoordinate3D(35.6800, 139.7700, 0),
                    new PlateauCoordinate3D(35.6800, 139.7702, 0),
                    new PlateauCoordinate3D(35.6802, 139.7702, 0),
                    new PlateauCoordinate3D(35.6802, 139.7700, 0),
                    new PlateauCoordinate3D(35.6800, 139.7700, 0))
            }
        };
        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorElevationMeters = 0d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 0d
        };

        ContextImportPlan plan = builder.BuildPlan(model, referenceContext);
        var firstPoint = plan.Solids.Single().FootprintPointsFeet.First();
        ProjectedCoordinate expectedProjected = coordinateTransformer.Project(new GeographicCoordinate(35.6800, 139.7700), referenceContext.ProjectCrs);

        Assert.Single(plan.Solids);
        Assert.Equal(expectedProjected.Easting / 0.3048d, firstPoint.XFeet, 3);
        Assert.Equal(expectedProjected.Northing / 0.3048d, firstPoint.YFeet, 3);
    }

    [Fact]
    public void BuildPlan_rejects_unsupported_file_crs()
    {
        PlateauCityModel model = new PlateauCityModel
        {
            EpsgCode = 4326,
            Features = new PlateauContextFeature[]
            {
                CreateFeature(
                    PlateauFeatureType.Building,
                    "bldg-1",
                    "Test",
                    "tile-a",
                    new PlateauCoordinate3D(35.0, 139.0, 0),
                    new PlateauCoordinate3D(35.0, 139.1, 0),
                    new PlateauCoordinate3D(35.1, 139.1, 0),
                    new PlateauCoordinate3D(35.1, 139.0, 0),
                    new PlateauCoordinate3D(35.0, 139.0, 0))
            }
        };
        PlateauImportReferenceContext referenceContext = new PlateauImportReferenceContext
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d)
        };

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => new ContextGeometryBuilder().BuildPlan(model, referenceContext));

        Assert.Contains("EPSG:4326", ex.Message);
        Assert.Contains("EPSG:6697", ex.Message);
    }

    private static PlateauContextFeature CreateFeature(PlateauFeatureType featureType, string id, string name, string tileId, params PlateauCoordinate3D[] ring)
    {
        return featureType == PlateauFeatureType.Building
            ? new PlateauBuildingFeature
            {
                Id = id,
                Name = name,
                TileId = tileId,
                ExteriorRing = ring
            }
            : new PlateauContextFeature
            {
                FeatureType = featureType,
                Id = id,
                Name = name,
                TileId = tileId,
                ExteriorRing = ring
            };
    }
}
