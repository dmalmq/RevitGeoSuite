using System;
using System.Collections.Generic;
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
        ContextShapePlan firstShape = plan.Shapes.First();
        var firstPoint = firstShape.FootprintPointsFeet.First();

        Assert.Equal(2, plan.Shapes.Count);
        Assert.Equal(328.084, firstPoint.XFeet, 3);
        Assert.Equal(492.126, firstPoint.YFeet, 3);
        Assert.Equal(0d, firstShape.BaseElevationFeet, 3);
        Assert.Equal(32.808, firstShape.HeightFeet, 3);
        Assert.Equal(PlateauGeometryImportMode.LightweightExtrusion, plan.GeometryImportMode);
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
        ContextShapePlan firstShape = Assert.Single(plan.Shapes);

        Assert.Equal(1000d + ((42d - 40d) / 0.3048d), firstShape.BaseElevationFeet, 3);
        Assert.Equal(49.213, firstShape.HeightFeet, 3);
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

        Assert.Equal(2, plan.Shapes.Count);
        double[] expectedBaseElevations = { 1000d + ((40.82d - 40d) / 0.3048d), 1000d + ((41.15d - 40d) / 0.3048d) };
        double[] actualBaseElevations = plan.Shapes.Select(shape => shape.BaseElevationFeet).ToArray();
        for (int index = 0; index < expectedBaseElevations.Length; index++)
        {
            Assert.Equal(expectedBaseElevations[index], actualBaseElevations[index], 12);
        }
        double[] expectedHeights = { 0.5d / 0.3048d, 0.5d / 0.3048d };
        double[] actualHeights = plan.Shapes.Select(shape => shape.HeightFeet).ToArray();
        for (int index = 0; index < expectedHeights.Length; index++)
        {
            Assert.Equal(expectedHeights[index], actualHeights[index], 12);
        }
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
        ContextShapePlan shape = plan.Shapes.Single();

        Assert.Equal(1000d + ((55d - 42d) / 0.3048d), shape.BaseElevationFeet, 3);
        Assert.Equal(32.808, shape.HeightFeet, 3);
    }

    [Fact]
    public void BuildPlan_records_warning_when_building_falls_back_to_default_height()
    {
        PlateauFolderScanResult scanResult = new PlateauFolderScanResult
        {
            CityModels = new[]
            {
                new PlateauCityModel
                {
                    SourcePath = @"C:\fixtures\flat-only.gml",
                    EpsgCode = 6677,
                    Features = new PlateauContextFeature[]
                    {
                        new PlateauBuildingFeature
                        {
                            Id = "flat-bldg",
                            Name = "Flat Only Building",
                            TileId = "tile-a",
                            ExteriorRing = new[]
                            {
                                new PlateauCoordinate3D(100.0, 150.0, 0.0),
                                new PlateauCoordinate3D(110.0, 150.0, 0.0),
                                new PlateauCoordinate3D(110.0, 160.0, 0.0),
                                new PlateauCoordinate3D(100.0, 160.0, 0.0),
                                new PlateauCoordinate3D(100.0, 150.0, 0.0)
                            },
                            BaseElevationMeters = 0.0,
                            TopElevationMeters = 0.0,
                            GeometrySurfaces = new[]
                            {
                                CreateSurface(
                                    "flat-surface",
                                    0,
                                    new PlateauCoordinate3D(100.0, 150.0, 0.0),
                                    new PlateauCoordinate3D(110.0, 150.0, 0.0),
                                    new PlateauCoordinate3D(110.0, 160.0, 0.0),
                                    new PlateauCoordinate3D(100.0, 160.0, 0.0),
                                    new PlateauCoordinate3D(100.0, 150.0, 0.0))
                            }
                        }
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
            AnchorZFeet = 0d
        };

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(scanResult, referenceContext, new[] { PlateauFeatureType.Building }, new[] { "tile-a" });
        ContextShapePlan shape = Assert.Single(plan.Shapes);

        Assert.Equal(32.808, shape.HeightFeet, 3);
        Assert.Contains(plan.WarningMessages, warning => warning.Contains("default 10.0 m height", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(plan.WarningMessages, warning => warning.Contains("Flat Only Building", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_default_selection_uses_model_tile_id_when_feature_tile_id_is_blank()
    {
        PlateauFolderScanResult scanResult = new PlateauFolderScanResult
        {
            CityModels = new[]
            {
                new PlateauCityModel
                {
                    SourcePath = @"C:\fixtures\533925.gml",
                    FileTileId = "533925",
                    EpsgCode = 6677,
                    Features = new PlateauContextFeature[]
                    {
                        CreateFeature(
                            PlateauFeatureType.Building,
                            "model-tile-bldg",
                            "Model Tile Building",
                            string.Empty,
                            new PlateauCoordinate3D(100.0, 150.0, 0.0),
                            new PlateauCoordinate3D(110.0, 150.0, 0.0),
                            new PlateauCoordinate3D(110.0, 160.0, 0.0),
                            new PlateauCoordinate3D(100.0, 160.0, 0.0),
                            new PlateauCoordinate3D(100.0, 150.0, 0.0))
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
            AnchorZFeet = 0d
        };

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(scanResult, referenceContext, null, null);
        ContextShapePlan shape = Assert.Single(plan.Shapes);

        Assert.Equal(new[] { "533925" }, plan.SelectedTileIds);
        Assert.Equal("533925", shape.TileId);
        Assert.Equal(1, plan.SourceFeatureCount);
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
        var firstPoint = plan.Shapes.Single().FootprintPointsFeet.First();

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
        var firstPoint = plan.Shapes.Single().FootprintPointsFeet.First();
        ProjectedCoordinate expectedProjected = coordinateTransformer.Project(new GeographicCoordinate(35.6800, 139.7700), referenceContext.ProjectCrs);

        Assert.Single(plan.Shapes);
        Assert.Equal(expectedProjected.Easting / 0.3048d, firstPoint.XFeet, 3);
        Assert.Equal(expectedProjected.Northing / 0.3048d, firstPoint.YFeet, 3);
    }

    [Fact]
    public void BuildPlan_detailed_mode_triangulates_highest_lod_surfaces_into_context_shape()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-detailed-building.gml");
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

        ContextImportPlan plan = builder.BuildPlan(model, referenceContext, PlateauGeometryImportMode.DetailedDirectShape);
        ContextShapePlan shape = Assert.Single(plan.Shapes);

        Assert.Equal(PlateauGeometryImportMode.DetailedDirectShape, plan.GeometryImportMode);
        Assert.Equal(PlateauGeometryImportMode.DetailedDirectShape, shape.GeometryMode);
        Assert.Equal(7, shape.SurfaceCount);
        Assert.Equal(7, plan.PreparedSurfaceCount);
        Assert.True(shape.Triangles.Count >= 8);
        Assert.Equal(shape.Triangles.Count, plan.PreparedTriangleCount);
        Assert.All(shape.Triangles, triangle => Assert.True(triangle.A.ZFeet >= 1000d));
    }

    [Fact]
    public void BuildPlan_detailed_mode_warns_when_inner_rings_are_ignored()
    {
        PlateauCityModel model = new PlateauCityModel
        {
            EpsgCode = 6677,
            Features = new PlateauContextFeature[]
            {
                new PlateauContextFeature
                {
                    FeatureType = PlateauFeatureType.Relief,
                    Id = "with-hole",
                    Name = "With Hole",
                    TileId = "tile-a",
                    ExteriorRing = new[]
                    {
                        new PlateauCoordinate3D(0, 0, 0),
                        new PlateauCoordinate3D(10, 0, 0),
                        new PlateauCoordinate3D(10, 10, 0),
                        new PlateauCoordinate3D(0, 10, 0),
                        new PlateauCoordinate3D(0, 0, 0)
                    },
                    GeometrySurfaces = new[]
                    {
                        new PlateauGeometrySurface
                        {
                            SurfaceId = "surface-with-hole",
                            Lod = 1,
                            ExteriorRing = new[]
                            {
                                new PlateauCoordinate3D(0, 0, 0),
                                new PlateauCoordinate3D(10, 0, 0),
                                new PlateauCoordinate3D(10, 10, 0),
                                new PlateauCoordinate3D(0, 10, 0),
                                new PlateauCoordinate3D(0, 0, 0)
                            },
                            InteriorRings = new IReadOnlyCollection<PlateauCoordinate3D>[]
                            {
                                new PlateauCoordinate3D[]
                                {
                                    new PlateauCoordinate3D(3, 3, 0),
                                    new PlateauCoordinate3D(7, 3, 0),
                                    new PlateauCoordinate3D(7, 7, 0),
                                    new PlateauCoordinate3D(3, 7, 0),
                                    new PlateauCoordinate3D(3, 3, 0)
                                }
                            }
                        }
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
            AnchorZFeet = 0d
        };

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(model, referenceContext, PlateauGeometryImportMode.DetailedDirectShape);

        Assert.Single(plan.Shapes);
        Assert.True(plan.PreparedTriangleCount > 0);
        Assert.Contains(plan.WarningMessages, warning => warning.Contains("interior ring", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_detailed_mode_transforms_geographic_points_into_local_triangles()
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
                new PlateauContextFeature
                {
                    FeatureType = PlateauFeatureType.Bridge,
                    Id = "bridge-6697",
                    Name = "Geographic Surface",
                    TileId = "tile-a",
                    ExteriorRing = new[]
                    {
                        new PlateauCoordinate3D(35.6800, 139.7700, 0),
                        new PlateauCoordinate3D(35.6800, 139.7702, 0),
                        new PlateauCoordinate3D(35.6802, 139.7702, 0),
                        new PlateauCoordinate3D(35.6802, 139.7700, 0),
                        new PlateauCoordinate3D(35.6800, 139.7700, 0)
                    },
                    GeometrySurfaces = new[]
                    {
                        CreateSurface(
                            "geo-surface",
                            1,
                            new PlateauCoordinate3D(35.6800, 139.7700, 0),
                            new PlateauCoordinate3D(35.6800, 139.7702, 0),
                            new PlateauCoordinate3D(35.6802, 139.7702, 0),
                            new PlateauCoordinate3D(35.6802, 139.7700, 0),
                            new PlateauCoordinate3D(35.6800, 139.7700, 0))
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
            AnchorZFeet = 0d
        };

        ContextImportPlan plan = builder.BuildPlan(model, referenceContext, PlateauGeometryImportMode.DetailedDirectShape);
        ContextShapePlan shape = Assert.Single(plan.Shapes);
        ProjectedCoordinate expectedProjected = coordinateTransformer.Project(new GeographicCoordinate(35.6800, 139.7700), referenceContext.ProjectCrs);

        Assert.True(shape.Triangles.SelectMany(GetVertices).Any(point =>
            Math.Abs(point.XFeet - (expectedProjected.Easting / 0.3048d)) < 0.01d
            && Math.Abs(point.YFeet - (expectedProjected.Northing / 0.3048d)) < 0.01d));
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


    [Fact]
    public void BuildPlan_transforms_mixed_crs_models_with_a_shared_transform_strategy_cache()
    {
        CrsRegistry crsRegistry = new CrsRegistry();
        CoordinateTransformer coordinateTransformer = new CoordinateTransformer(crsRegistry);
        ContextGeometryBuilder builder = new ContextGeometryBuilder(coordinateTransformer);
        PlateauFolderScanResult scanResult = new PlateauFolderScanResult
        {
            CityModels = new PlateauCityModel[]
            {
                new PlateauCityModel
                {
                    EpsgCode = 6677,
                    Features = new PlateauContextFeature[]
                    {
                        CreateFeature(
                            PlateauFeatureType.Building,
                            "projected-bldg",
                            "Projected Building",
                            "tile-a",
                            new PlateauCoordinate3D(100.0, 150.0, 0),
                            new PlateauCoordinate3D(110.0, 150.0, 0),
                            new PlateauCoordinate3D(110.0, 160.0, 0),
                            new PlateauCoordinate3D(100.0, 160.0, 0),
                            new PlateauCoordinate3D(100.0, 150.0, 0))
                    }
                },
                new PlateauCityModel
                {
                    EpsgCode = 6697,
                    SrsName = "urn:ogc:def:crs:EPSG::6697",
                    Features = new PlateauContextFeature[]
                    {
                        CreateFeature(
                            PlateauFeatureType.Bridge,
                            "geographic-bridge",
                            "Geographic Bridge",
                            "tile-b",
                            new PlateauCoordinate3D(35.6800, 139.7700, 0),
                            new PlateauCoordinate3D(35.6800, 139.7702, 0),
                            new PlateauCoordinate3D(35.6802, 139.7702, 0),
                            new PlateauCoordinate3D(35.6802, 139.7700, 0),
                            new PlateauCoordinate3D(35.6800, 139.7700, 0))
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
            AnchorZFeet = 0d
        };

        ContextImportPlan plan = builder.BuildPlan(scanResult, referenceContext, null, null);
        ContextShapePlan projectedShape = Assert.Single(plan.Shapes.Where(shape => shape.SourceFeatureId == "projected-bldg"));
        ContextShapePlan geographicShape = Assert.Single(plan.Shapes.Where(shape => shape.SourceFeatureId == "geographic-bridge"));
        ProjectedCoordinate expectedProjected = coordinateTransformer.Project(new GeographicCoordinate(35.6800, 139.7700), referenceContext.ProjectCrs);

        Assert.Equal(2, plan.Shapes.Count);
        Assert.Equal(328.084, projectedShape.FootprintPointsFeet.First().XFeet, 3);
        Assert.Equal(expectedProjected.Easting / 0.3048d, geographicShape.FootprintPointsFeet.First().XFeet, 3);
        Assert.Equal(expectedProjected.Northing / 0.3048d, geographicShape.FootprintPointsFeet.First().YFeet, 3);
    }
    private static PlateauContextFeature CreateFeature(PlateauFeatureType featureType, string id, string name, string tileId, params PlateauCoordinate3D[] ring)
    {
        PlateauGeometrySurface surface = CreateSurface(id + "-surface", 0, ring);
        return featureType == PlateauFeatureType.Building
            ? new PlateauBuildingFeature
            {
                Id = id,
                Name = name,
                TileId = tileId,
                ExteriorRing = ring,
                GeometrySurfaces = new[] { surface }
            }
            : new PlateauContextFeature
            {
                FeatureType = featureType,
                Id = id,
                Name = name,
                TileId = tileId,
                ExteriorRing = ring,
                GeometrySurfaces = new[] { surface }
            };
    }

    private static PlateauGeometrySurface CreateSurface(string surfaceId, int lod, params PlateauCoordinate3D[] ring)
    {
        return new PlateauGeometrySurface
        {
            SurfaceId = surfaceId,
            Lod = lod,
            ExteriorRing = ring
        };
    }

    private static IEnumerable<ContextShapePoint3D> GetVertices(ContextShapeTriangle triangle)
    {
        yield return triangle.A;
        yield return triangle.B;
        yield return triangle.C;
    }
}
