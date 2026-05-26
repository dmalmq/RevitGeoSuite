using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
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
    public void BuildPlan_uses_dissolved_highest_lod_bridge_surfaces_for_lightweight_footprint()
    {
        PlateauCoordinate3D[] cutTriangle = new[]
        {
            new PlateauCoordinate3D(1d, 1d, 0d),
            new PlateauCoordinate3D(2d, 1d, 0d),
            new PlateauCoordinate3D(1d, 2d, 0d),
            new PlateauCoordinate3D(1d, 1d, 0d)
        };
        PlateauCityModel model = new PlateauCityModel
        {
            EpsgCode = 6677,
            Features = new PlateauContextFeature[]
            {
                new PlateauContextFeature
                {
                    FeatureType = PlateauFeatureType.Bridge,
                    Id = "bridge-deck",
                    Name = "Bridge Deck",
                    TileId = "tile-a",
                    ExteriorRing = cutTriangle,
                    GeometrySurfaces = new[]
                    {
                        CreateSurface(
                            "deck-triangle-a",
                            2,
                            new PlateauCoordinate3D(0d, 0d, 10d),
                            new PlateauCoordinate3D(20d, 0d, 10d),
                            new PlateauCoordinate3D(20d, 10d, 10d),
                            new PlateauCoordinate3D(0d, 0d, 10d)),
                        CreateSurface(
                            "deck-triangle-b",
                            2,
                            new PlateauCoordinate3D(0d, 0d, 10d),
                            new PlateauCoordinate3D(20d, 10d, 10d),
                            new PlateauCoordinate3D(0d, 10d, 10d),
                            new PlateauCoordinate3D(0d, 0d, 10d)),
                        CreateSurface("ground-cut-triangle", 2, cutTriangle)
                    }
                }
            }
        };

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(model, BuildIdentityReferenceContext());
        ContextShapePlan bridgeShape = Assert.Single(plan.Shapes);
        double[] xMetres = bridgeShape.FootprintPointsFeet.Select(point => point.XFeet * 0.3048d).ToArray();
        double[] yMetres = bridgeShape.FootprintPointsFeet.Select(point => point.YFeet * 0.3048d).ToArray();

        Assert.Equal("bridge-deck", bridgeShape.SourceFeatureId);
        Assert.Equal(PlateauFeatureType.Bridge, bridgeShape.FeatureType);
        Assert.Equal(200d, ComputeFootprintAreaMeters(bridgeShape), 6);
        Assert.Equal(0d, xMetres.Min(), 6);
        Assert.Equal(20d, xMetres.Max(), 6);
        Assert.Equal(0d, yMetres.Min(), 6);
        Assert.Equal(10d, yMetres.Max(), 6);
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
    public void BuildPlan_drops_secondary_mesh_land_use_when_polygon_lies_outside_selected_tertiary_tile()
    {
        PlateauFolderScanResult scanResult = new PlateauFolderScanResult
        {
            CityModels = new[]
            {
                new PlateauCityModel
                {
                    SourcePath = @"C:\fixtures\533945_luse_6697_op.gml",
                    FileTileId = "533945",
                    EpsgCode = 6677,
                    Features = new PlateauContextFeature[]
                    {
                        CreateFeature(
                            PlateauFeatureType.LandUse,
                            "luse-1",
                            "Land Use",
                            "533945",
                            new PlateauCoordinate3D(100.0, 150.0, 0.0),
                            new PlateauCoordinate3D(110.0, 150.0, 0.0),
                            new PlateauCoordinate3D(110.0, 160.0, 0.0),
                            new PlateauCoordinate3D(100.0, 160.0, 0.0),
                            new PlateauCoordinate3D(100.0, 150.0, 0.0))
                    }
                }
            }
        };

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(
            scanResult,
            BuildIdentityReferenceContext(),
            new[] { PlateauFeatureType.LandUse },
            new[] { "53394536" });

        Assert.Empty(plan.Shapes);
    }

    [Fact]
    public void BuildPlan_clips_secondary_mesh_land_use_to_selected_tertiary_tile_when_polygon_is_inside_it()
    {
        const string tertiaryTileId = "53394536";
        JapanMeshCalculator meshCalculator = new JapanMeshCalculator();
        MeshBounds tileBounds = meshCalculator.GetBounds(new MeshCode { Value = tertiaryTileId });
        double centerLatitude = (tileBounds.SouthLatitude + tileBounds.NorthLatitude) / 2d;
        double centerLongitude = (tileBounds.WestLongitude + tileBounds.EastLongitude) / 2d;
        CoordinateTransformer transformer = new CoordinateTransformer(new CrsRegistry());
        CrsReference projectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" };
        ProjectedCoordinate center = transformer.Project(new GeographicCoordinate(centerLatitude, centerLongitude), projectCrs);
        const double halfSpanMetres = 50d;

        PlateauFolderScanResult scanResult = new PlateauFolderScanResult
        {
            CityModels = new[]
            {
                new PlateauCityModel
                {
                    SourcePath = @"C:\fixtures\533945_luse_6697_op.gml",
                    FileTileId = "533945",
                    EpsgCode = 6677,
                    Features = new PlateauContextFeature[]
                    {
                        CreateFeature(
                            PlateauFeatureType.LandUse,
                            "luse-inside",
                            "Land Use Inside",
                            "533945",
                            new PlateauCoordinate3D(center.Easting - halfSpanMetres, center.Northing - halfSpanMetres, 0.0),
                            new PlateauCoordinate3D(center.Easting + halfSpanMetres, center.Northing - halfSpanMetres, 0.0),
                            new PlateauCoordinate3D(center.Easting + halfSpanMetres, center.Northing + halfSpanMetres, 0.0),
                            new PlateauCoordinate3D(center.Easting - halfSpanMetres, center.Northing + halfSpanMetres, 0.0),
                            new PlateauCoordinate3D(center.Easting - halfSpanMetres, center.Northing - halfSpanMetres, 0.0))
                    }
                }
            }
        };

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(
            scanResult,
            BuildIdentityReferenceContext(),
            new[] { PlateauFeatureType.LandUse },
            new[] { tertiaryTileId });

        ContextShapePlan shape = Assert.Single(plan.Shapes);
        Assert.Equal(PlateauFeatureType.LandUse, shape.FeatureType);
        Assert.Equal("533945", shape.TileId);
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
    [Fact]
    public void BuildPlan_mass_on_relief_overrides_building_base_with_sampled_relief_elevation()
    {
        PlateauCityModel model = BuildBuildingAndReliefModel();
        PlateauImportReferenceContext referenceContext = BuildIdentityReferenceContext();

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(
            model,
            referenceContext,
            PlateauGeometryImportMode.LightweightMassOnRelief);

        ContextShapePlan buildingShape = plan.Shapes.Single(shape => shape.FeatureType == PlateauFeatureType.Building);

        // Sloped triangle (0,0,0)-(100,0,10)-(0,100,10) interpolated at building centroid (25, 25)
        // yields Z = 0.5*0 + 0.25*10 + 0.25*10 = 5 metres. Building height = 20 m (TopZ - BaseZ).
        Assert.Equal(5d / 0.3048d, buildingShape.BaseElevationFeet, 3);
        Assert.Equal(20d / 0.3048d, buildingShape.HeightFeet, 3);
        Assert.Equal(PlateauGeometryImportMode.LightweightMassOnRelief, plan.GeometryImportMode);
        Assert.DoesNotContain(plan.WarningMessages, w => w.Contains("outside the Relief hull", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(plan.WarningMessages, w => w.Contains("no Relief surfaces", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_mass_on_relief_warns_and_uses_nearest_triangle_when_building_centroid_is_outside_relief_hull()
    {
        PlateauCityModel model = BuildBuildingAndReliefModel(buildingCenterX: 500d, buildingCenterY: 500d);
        PlateauImportReferenceContext referenceContext = BuildIdentityReferenceContext();

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(
            model,
            referenceContext,
            PlateauGeometryImportMode.LightweightMassOnRelief);

        ContextShapePlan buildingShape = plan.Shapes.Single(shape => shape.FeatureType == PlateauFeatureType.Building);

        // Centroid of the only Relief triangle is (100/3, 100/3, 20/3) m.
        double expectedNearestZ = 20d / 3d;
        Assert.Equal(expectedNearestZ / 0.3048d, buildingShape.BaseElevationFeet, 3);
        Assert.Contains(plan.WarningMessages, w => w.Contains("outside the Relief hull", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPlan_mass_on_relief_warns_and_falls_back_when_no_relief_surfaces_are_present()
    {
        PlateauCityModel model = new PlateauCityModel
        {
            EpsgCode = 6677,
            Features = new PlateauContextFeature[] { BuildSquareBuilding(centerX: 25d, centerY: 25d) }
        };
        PlateauImportReferenceContext referenceContext = BuildIdentityReferenceContext();

        ContextImportPlan plan = new ContextGeometryBuilder().BuildPlan(
            model,
            referenceContext,
            PlateauGeometryImportMode.LightweightMassOnRelief);

        ContextShapePlan buildingShape = Assert.Single(plan.Shapes);

        // No Relief: falls back to the building's own BaseElevationMeters = 0 m.
        Assert.Equal(0d, buildingShape.BaseElevationFeet, 3);
        Assert.Equal(20d / 0.3048d, buildingShape.HeightFeet, 3);
        Assert.Contains(plan.WarningMessages, w => w.Contains("no Relief surfaces", StringComparison.OrdinalIgnoreCase));
    }

    private static PlateauCityModel BuildBuildingAndReliefModel(double buildingCenterX = 25d, double buildingCenterY = 25d)
    {
        return new PlateauCityModel
        {
            EpsgCode = 6677,
            Features = new PlateauContextFeature[]
            {
                BuildSquareBuilding(buildingCenterX, buildingCenterY),
                new PlateauContextFeature
                {
                    FeatureType = PlateauFeatureType.Relief,
                    Id = "relief-1",
                    Name = "Relief",
                    TileId = "tile-a",
                    ExteriorRing = new[]
                    {
                        new PlateauCoordinate3D(0d, 0d, 0d),
                        new PlateauCoordinate3D(100d, 0d, 10d),
                        new PlateauCoordinate3D(0d, 100d, 10d)
                    },
                    GeometrySurfaces = new[]
                    {
                        new PlateauGeometrySurface
                        {
                            SurfaceId = "relief-1-surface",
                            Lod = 1,
                            ExteriorRing = new[]
                            {
                                new PlateauCoordinate3D(0d, 0d, 0d),
                                new PlateauCoordinate3D(100d, 0d, 10d),
                                new PlateauCoordinate3D(0d, 100d, 10d)
                            }
                        }
                    }
                }
            }
        };
    }

    private static PlateauBuildingFeature BuildSquareBuilding(double centerX, double centerY)
    {
        PlateauCoordinate3D[] ring = new[]
        {
            new PlateauCoordinate3D(centerX - 5d, centerY - 5d, 0d),
            new PlateauCoordinate3D(centerX + 5d, centerY - 5d, 0d),
            new PlateauCoordinate3D(centerX + 5d, centerY + 5d, 0d),
            new PlateauCoordinate3D(centerX - 5d, centerY + 5d, 0d),
            new PlateauCoordinate3D(centerX - 5d, centerY - 5d, 0d)
        };
        return new PlateauBuildingFeature
        {
            Id = "bldg-1",
            Name = "Test Building",
            TileId = "tile-a",
            ExteriorRing = ring,
            GeometrySurfaces = new[] { CreateSurface("bldg-1-surface", 1, ring) },
            BaseElevationMeters = 0d,
            TopElevationMeters = 20d
        };
    }

    private static PlateauImportReferenceContext BuildIdentityReferenceContext()
    {
        return new PlateauImportReferenceContext
        {
            ProjectCrs = new CrsReference { EpsgCode = 6677, NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX" },
            AnchorProjectedCoordinate = new ProjectedCoordinate(0d, 0d),
            AnchorElevationMeters = 0d,
            AnchorXFeet = 0d,
            AnchorYFeet = 0d,
            AnchorZFeet = 0d
        };
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

    private static double ComputeFootprintAreaMeters(ContextShapePlan shape)
    {
        var points = shape.FootprintPointsFeet.Select(point => (X: point.XFeet * 0.3048d, Y: point.YFeet * 0.3048d)).ToArray();
        double areaTwice = 0d;
        for (int index = 0; index < points.Length; index++)
        {
            var current = points[index];
            var next = points[(index + 1) % points.Length];
            areaTwice += (current.X * next.Y) - (next.X * current.Y);
        }

        return Math.Abs(areaTwice) * 0.5d;
    }

    private static IEnumerable<ContextShapePoint3D> GetVertices(ContextShapeTriangle triangle)
    {
        yield return triangle.A;
        yield return triangle.B;
        yield return triangle.C;
    }
}
