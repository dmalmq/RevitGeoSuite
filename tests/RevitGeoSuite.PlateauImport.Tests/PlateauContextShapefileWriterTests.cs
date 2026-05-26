using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO.Esri;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauContextShapefileWriterTests
{
    [Fact]
    public void Write_creates_arcmap_shapefile_sidecars_with_polygon_features_and_attributes()
    {
        string directory = CreateTempDirectory();
        try
        {
            string shapefilePath = Path.Combine(directory, "plateau_context.shp");
            IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> features = new[]
            {
                BuildSquare("PLATEAU_BUILDINGS", 100d, 150d, 20d, "building-001"),
                BuildSquare("PLATEAU_VEGETATION", 200d, 250d, 15d, "vegetation-001"),
            };
            IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> roads = new[]
            {
                new PlateauContextOutlinesDxfWriter.AreaFeature(
                    "PLATEAU_ROADS",
                    new (double X, double Y)[]
                    {
                        (10d, 20d),
                        (50d, 20d),
                        (50d, 35d),
                        (10d, 35d),
                    },
                    sourceId: "roads-dissolved-1"),
            };

            PlateauContextShapefileWriter.WriteResult result = PlateauContextShapefileWriter.Write(
                shapefilePath,
                features,
                roads,
                CreateCrs());

            Assert.Equal(3, result.FeatureCount);
            Assert.Equal(1, result.RoadFeatureCount);
            Assert.Equal(2, result.FootprintFeatureCount);
            Assert.Empty(result.Warnings);
            AssertSidecarsExist(shapefilePath);

            var readFeatures = Shapefile.ReadAllFeatures(shapefilePath).ToArray();
            Assert.Equal(3, readFeatures.Length);
            Assert.All(readFeatures, feature => Assert.True(feature.Geometry is Polygon || feature.Geometry is MultiPolygon));

            var road = Assert.Single(readFeatures, feature => string.Equals((string)feature.Attributes["TYPE"], "ROAD", StringComparison.Ordinal));
            Assert.Equal(true, road.Attributes["DISSOLVED"]);
            Assert.Equal("205,205,205", road.Attributes["FILL_RGB"]);
            Assert.Equal(6677, Convert.ToInt32(road.Attributes["EPSG"], System.Globalization.CultureInfo.InvariantCulture));

            var building = Assert.Single(readFeatures, feature => string.Equals((string)feature.Attributes["TYPE"], "BUILDING", StringComparison.Ordinal));
            Assert.Equal(false, building.Attributes["DISSOLVED"]);
            Assert.Equal("building-001", building.Attributes["SOURCE_ID"]);
            Assert.Equal("PLATEAU_BUILDINGS", building.Attributes["LAYER"]);
            Assert.Equal(100d, building.Geometry.EnvelopeInternal.MinX, 6);
            Assert.Equal(150d, building.Geometry.EnvelopeInternal.MinY, 6);

            string prj = File.ReadAllText(Path.ChangeExtension(shapefilePath, ".prj"));
            Assert.Contains("JGD_2011_Japan_Zone_9", prj);
            Assert.Contains("AUTHORITY[\"EPSG\",\"6677\"]", prj);
            Assert.Equal("UTF-8", File.ReadAllText(Path.ChangeExtension(shapefilePath, ".cpg")));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_package_splits_plateau_categories_into_companion_shapefiles()
    {
        string directory = CreateTempDirectory();
        try
        {
            string shapefilePath = Path.Combine(directory, "context.shp");
            WritePlaceholderSidecars(shapefilePath);
            IReadOnlyCollection<PlateauContextOutlinesDxfWriter.OutlineFeature> features = new[]
            {
                BuildSquare("PLATEAU_BUILDINGS", 100d, 150d, 20d, "building-001"),
                BuildSquare("PLATEAU_BRIDGES", 130d, 150d, 12d, "bridge-001"),
                BuildSquare("PLATEAU_VEGETATION", 200d, 250d, 15d, "vegetation-001"),
                BuildSquare("PLATEAU_RELIEF", 230d, 250d, 18d, "relief-001"),
                BuildSquare(PlateauContextOutlinesDxfWriter.PlateauLandUseLayer, 260d, 250d, 25d, "landuse-001", "101", "住宅"),
            };
            IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> roads = new[]
            {
                new PlateauContextOutlinesDxfWriter.AreaFeature(
                    "PLATEAU_ROADS",
                    new (double X, double Y)[]
                    {
                        (10d, 20d),
                        (50d, 20d),
                        (50d, 35d),
                        (10d, 35d),
                    },
                    sourceId: "roads-dissolved-1"),
            };
            PlateauOutlineDxfExportPackage package = new PlateauOutlineDxfExportPackage(
                features.ToArray(),
                roads.ToArray(),
                CreateCrs(),
                Vector3d.Zero,
                Vector3d.Zero);
            List<string> stages = new List<string>();

            PlateauContextShapefileWriter.WriteResult result = PlateauContextShapefileWriter.Write(shapefilePath, package, stages.Add);

            Assert.Equal(6, result.FeatureCount);
            Assert.Equal(1, result.RoadFeatureCount);
            Assert.Equal(5, result.FootprintFeatureCount);
            Assert.Empty(result.Warnings);
            AssertSidecarsDoNotExist(shapefilePath);

            string roadsPath = Path.Combine(directory, "context_plateau_roads.shp");
            string buildingsPath = Path.Combine(directory, "context_plateau_buildings.shp");
            string bridgesPath = Path.Combine(directory, "context_plateau_bridges.shp");
            string vegetationPath = Path.Combine(directory, "context_plateau_vegetation.shp");
            string reliefPath = Path.Combine(directory, "context_plateau_relief.shp");
            string landUsePath = Path.Combine(directory, "context_plateau_landuse.shp");

            AssertSidecarsExist(roadsPath);
            AssertSidecarsExist(buildingsPath);
            AssertSidecarsExist(bridgesPath);
            AssertSidecarsExist(vegetationPath);
            AssertSidecarsExist(reliefPath);
            AssertSidecarsExist(landUsePath);
            Assert.Contains(Path.ChangeExtension(roadsPath, ".shp"), result.Files);
            Assert.Contains(Path.ChangeExtension(buildingsPath, ".shp"), result.Files);
            Assert.Contains(Path.ChangeExtension(landUsePath, ".shp"), result.Files);

            AssertShapefileLayerAndType(roadsPath, "PLATEAU_ROADS", "ROAD");
            AssertShapefileLayerAndType(buildingsPath, "PLATEAU_BUILDINGS", "BUILDING");
            AssertShapefileLayerAndType(bridgesPath, "PLATEAU_BRIDGES", "BRIDGE");
            AssertShapefileLayerAndType(vegetationPath, "PLATEAU_VEGETATION", "VEGETATION");
            AssertShapefileLayerAndType(reliefPath, "PLATEAU_RELIEF", "RELIEF");
            AssertShapefileLayerAndType(landUsePath, PlateauContextOutlinesDxfWriter.PlateauLandUseLayer, "LANDUSE");

            Assert.Contains("Writing PLATEAU road polygons", stages);
            Assert.Contains("Writing PLATEAU building polygons", stages);
            Assert.Contains("Writing PLATEAU bridge polygons", stages);
            Assert.Contains("Writing PLATEAU vegetation polygons", stages);
            Assert.Contains("Writing PLATEAU relief polygons", stages);
            Assert.Contains("Writing PLATEAU land-use polygons", stages);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_streaming_appends_batches_to_existing_category_shapefiles()
    {
        string directory = CreateTempDirectory();
        try
        {
            string shapefilePath = Path.Combine(directory, "context.shp");
            string buildingsPath = Path.Combine(directory, "context_plateau_buildings.shp");
            WritePlaceholderSidecars(buildingsPath);
            List<string> warnings = new List<string>();

            PlateauContextShapefileWriter.WriteResult result;
            using (PlateauContextShapefileWriter.StreamingWriteSession session = PlateauContextShapefileWriter.OpenStreaming(shapefilePath, CreateCrs(), warnings))
            {
                session.WritePlateauOutlines(new[]
                {
                    BuildSquare("PLATEAU_BUILDINGS", 100d, 150d, 20d, "building-001"),
                });
                session.WritePlateauOutlines(new[]
                {
                    BuildSquare("PLATEAU_BUILDINGS", 130d, 150d, 15d, "building-002"),
                });
                session.WritePlateauRoadAreas(new[]
                {
                    new PlateauContextOutlinesDxfWriter.AreaFeature(
                        "PLATEAU_ROADS",
                        new (double X, double Y)[]
                        {
                            (10d, 20d),
                            (50d, 20d),
                            (50d, 35d),
                            (10d, 35d),
                        },
                        sourceId: "roads-dissolved-1"),
                });
                result = session.Complete();
            }

            Assert.Equal(3, result.FeatureCount);
            Assert.Equal(1, result.RoadFeatureCount);
            Assert.Equal(2, result.FootprintFeatureCount);
            Assert.Empty(result.Warnings);
            AssertSidecarsDoNotExist(shapefilePath);
            AssertSidecarsExist(buildingsPath);
            AssertSidecarsExist(Path.Combine(directory, "context_plateau_roads.shp"));

            var buildingFeatures = Shapefile.ReadAllFeatures(buildingsPath).ToArray();
            Assert.Equal(2, buildingFeatures.Length);
            Assert.Equal(1, Convert.ToInt32(buildingFeatures[0].Attributes["ROW_ID"], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(2, Convert.ToInt32(buildingFeatures[1].Attributes["ROW_ID"], System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal("building-001", buildingFeatures[0].Attributes["SOURCE_ID"]);
            Assert.Equal("building-002", buildingFeatures[1].Attributes["SOURCE_ID"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_package_skips_empty_plateau_category_shapefiles()
    {
        string directory = CreateTempDirectory();
        try
        {
            string shapefilePath = Path.Combine(directory, "context.shp");
            PlateauContextOutlinesDxfWriter.OutlineFeature[] features =
            {
                BuildSquare("PLATEAU_BUILDINGS", 100d, 150d, 20d, "building-001"),
            };
            PlateauOutlineDxfExportPackage package = new PlateauOutlineDxfExportPackage(
                features,
                Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
                CreateCrs(),
                Vector3d.Zero,
                Vector3d.Zero);

            PlateauContextShapefileWriter.WriteResult result = PlateauContextShapefileWriter.Write(shapefilePath, package);

            Assert.Equal(1, result.FeatureCount);
            Assert.Equal(0, result.RoadFeatureCount);
            Assert.Equal(1, result.FootprintFeatureCount);
            Assert.False(File.Exists(shapefilePath));
            AssertSidecarsExist(Path.Combine(directory, "context_plateau_buildings.shp"));
            AssertShapefileDoesNotExist(Path.Combine(directory, "context_plateau_roads.shp"));
            AssertShapefileDoesNotExist(Path.Combine(directory, "context_plateau_bridges.shp"));
            AssertShapefileDoesNotExist(Path.Combine(directory, "context_plateau_vegetation.shp"));
            AssertShapefileDoesNotExist(Path.Combine(directory, "context_plateau_relief.shp"));
            AssertShapefileDoesNotExist(Path.Combine(directory, "context_plateau_landuse.shp"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_preserves_road_polygon_holes()
    {
        string directory = CreateTempDirectory();
        try
        {
            string shapefilePath = Path.Combine(directory, "roads.shp");
            IReadOnlyCollection<PlateauContextOutlinesDxfWriter.AreaFeature> roads = new[]
            {
                new PlateauContextOutlinesDxfWriter.AreaFeature(
                    "PLATEAU_ROADS",
                    new (double X, double Y)[]
                    {
                        (0d, 0d),
                        (20d, 0d),
                        (20d, 20d),
                        (0d, 20d),
                    },
                    new[]
                    {
                        new (double X, double Y)[]
                        {
                            (5d, 5d),
                            (15d, 5d),
                            (15d, 15d),
                            (5d, 15d),
                        },
                    },
                    "road-with-hole"),
            };

            PlateauContextShapefileWriter.WriteResult result = PlateauContextShapefileWriter.Write(
                shapefilePath,
                Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
                roads,
                CreateCrs());

            Assert.Equal(1, result.FeatureCount);
            Assert.Empty(result.Warnings);

            var feature = Assert.Single(Shapefile.ReadAllFeatures(shapefilePath));
            MultiPolygon multiPolygon = Assert.IsType<MultiPolygon>(feature.Geometry);
            Polygon polygon = Assert.IsType<Polygon>(multiPolygon.GetGeometryN(0));
            Assert.Equal(1, polygon.NumInteriorRings);
            Assert.Equal(300d, polygon.Area, 6);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_package_emits_railway_line_shapefile_and_ignores_sidewalk_lines()
    {
        string directory = CreateTempDirectory();
        try
        {
            string shapefilePath = Path.Combine(directory, "context.shp");
            // Sidewalks no longer flow through the line channel — they're polygonized into
            // the polygon channel separately. If anything still passes a sidewalk
            // KibanLineExportFeature, the writer must silently ignore it.
            KibanLineExportFeature[] kibanLines =
            {
                new KibanLineExportFeature(
                    PlateauContextOutlinesDxfWriter.GsiSidewalksLayer,
                    new (double X, double Y)[] { (100d, 200d), (110d, 205d) },
                    "stale-sidewalk-line",
                    "533945",
                    @"C:\source\FG-GML-533945-RdCompt.xml",
                    "歩道",
                    "表示"),
                new KibanLineExportFeature(
                    PlateauContextOutlinesDxfWriter.GsiRailwaysLayer,
                    new (double X, double Y)[] { (120d, 210d), (130d, 220d) },
                    "rail-source",
                    "533945",
                    @"C:\source\FG-GML-533945-RailCL.xml",
                    "トンネル内の鉄道",
                    "非表示"),
            };
            PlateauOutlineDxfExportPackage package = new PlateauOutlineDxfExportPackage(
                Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
                Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
                Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
                kibanLines,
                CreateCrs(),
                Vector3d.Zero,
                Vector3d.Zero);

            PlateauContextShapefileWriter.WriteResult result = PlateauContextShapefileWriter.Write(shapefilePath, package);

            Assert.Equal(1, result.FeatureCount);
            Assert.Equal(0, result.SidewalkFeatureCount);
            Assert.Equal(1, result.RailwayFeatureCount);
            Assert.False(File.Exists(shapefilePath));
            string sidewalkPath = Path.Combine(directory, "context_gsi_sidewalks.shp");
            string railwayPath = Path.Combine(directory, "context_gsi_railways.shp");
            Assert.False(File.Exists(sidewalkPath));
            AssertSidecarsExist(railwayPath);

            var railwayFeature = Assert.Single(Shapefile.ReadAllFeatures(railwayPath));
            Assert.True(railwayFeature.Geometry is LineString || railwayFeature.Geometry is MultiLineString);
            Assert.Equal("RAILWAY", railwayFeature.Attributes["TYPE"]);
            Assert.Equal("非表示", railwayFeature.Attributes["VIS"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_package_emits_sidewalk_polygon_shapefile_for_gsi_sidewalk_strips()
    {
        string directory = CreateTempDirectory();
        try
        {
            string shapefilePath = Path.Combine(directory, "context.shp");
            KibanPolygonExportFeature[] kibanPolygons =
            {
                new KibanPolygonExportFeature(
                    PlateauContextOutlinesDxfWriter.GsiSidewalksLayer,
                    new (double X, double Y)[]
                    {
                        (100d, 200d),
                        (120d, 200d),
                        (120d, 220d),
                        (100d, 220d),
                    },
                    Array.Empty<IReadOnlyList<(double X, double Y)>>(),
                    "sidewalk-strip:1:53394536",
                    meshCode: string.Empty,
                    sourcePath: string.Empty,
                    featureType: "sidewalk-strip",
                    visibility: string.Empty),
            };
            PlateauOutlineDxfExportPackage package = new PlateauOutlineDxfExportPackage(
                Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
                Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
                Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
                Array.Empty<KibanLineExportFeature>(),
                kibanPolygons,
                CreateCrs(),
                Vector3d.Zero,
                Vector3d.Zero);

            PlateauContextShapefileWriter.WriteResult result = PlateauContextShapefileWriter.Write(shapefilePath, package);

            Assert.Equal(1, result.FeatureCount);
            Assert.Equal(1, result.SidewalkFeatureCount);
            Assert.False(File.Exists(shapefilePath));
            string sidewalkPath = Path.Combine(directory, "context_gsi_sidewalks.shp");
            AssertSidecarsExist(sidewalkPath);

            var sidewalkFeature = Assert.Single(Shapefile.ReadAllFeatures(sidewalkPath));
            Assert.True(sidewalkFeature.Geometry is Polygon || sidewalkFeature.Geometry is MultiPolygon);
            Assert.Equal("SIDEWALK", sidewalkFeature.Attributes["TYPE"]);
            Assert.Equal(PlateauContextOutlinesDxfWriter.GsiSidewalksLayer, sidewalkFeature.Attributes["LAYER"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Write_package_creates_companion_polygon_shapefile_for_gsi_land_use_features()
    {
        string directory = CreateTempDirectory();
        try
        {
            string shapefilePath = Path.Combine(directory, "context.shp");
            KibanPolygonExportFeature[] kibanPolygons =
            {
                new KibanPolygonExportFeature(
                    KibanGmlParser.LandUseLayer,
                    new (double X, double Y)[]
                    {
                        (100d, 200d),
                        (120d, 200d),
                        (120d, 215d),
                        (100d, 215d),
                    },
                    Array.Empty<IReadOnlyList<(double X, double Y)>>(),
                    "landuse-source",
                    "533945",
                    @"C:\source\FG-GML-533945-GreenArea.xml",
                    "緑地",
                    string.Empty),
            };
            PlateauOutlineDxfExportPackage package = new PlateauOutlineDxfExportPackage(
                Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
                Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
                Array.Empty<PlateauContextOutlinesDxfWriter.OutlineFeature>(),
                Array.Empty<KibanLineExportFeature>(),
                kibanPolygons,
                CreateCrs(),
                Vector3d.Zero,
                Vector3d.Zero);

            PlateauContextShapefileWriter.WriteResult result = PlateauContextShapefileWriter.Write(shapefilePath, package);

            Assert.Equal(1, result.FeatureCount);
            Assert.Equal(1, result.KibanLandUseFeatureCount);
            Assert.Equal(0, result.KibanWaterFeatureCount);
            Assert.False(File.Exists(shapefilePath));
            string landUsePath = Path.Combine(directory, "context_gsi_landuse.shp");
            AssertSidecarsExist(landUsePath);

            var landUseFeature = Assert.Single(Shapefile.ReadAllFeatures(landUsePath));
            Assert.True(landUseFeature.Geometry is Polygon || landUseFeature.Geometry is MultiPolygon);
            Assert.Equal("LANDUSE", landUseFeature.Attributes["TYPE"]);
            Assert.Equal(KibanGmlParser.LandUseLayer, landUseFeature.Attributes["LAYER"]);
            Assert.Equal("緑地", landUseFeature.Attributes["FGD_TYPE"]);
            Assert.Equal("緑地", landUseFeature.Attributes["LU_NAME"]);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static PlateauContextOutlinesDxfWriter.OutlineFeature BuildSquare(
        string layer,
        double originX,
        double originY,
        double size,
        string sourceId,
        string? classCode = null,
        string? className = null)
    {
        (double X, double Y)[] vertices = new[]
        {
            (originX, originY),
            (originX + size, originY),
            (originX + size, originY + size),
            (originX, originY + size),
        };
        return new PlateauContextOutlinesDxfWriter.OutlineFeature(layer, vertices, sourceId, classCode, className);
    }

    private static CrsReference CreateCrs()
    {
        return new CrsReference
        {
            EpsgCode = 6677,
            NameSnapshot = "JGD2011 / Japan Plane Rectangular CS IX",
        };
    }

    private static void AssertSidecarsExist(string shapefilePath)
    {
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".shp")));
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".shx")));
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".dbf")));
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".prj")));
        Assert.True(File.Exists(Path.ChangeExtension(shapefilePath, ".cpg")));
    }

    private static void AssertSidecarsDoNotExist(string shapefilePath)
    {
        Assert.False(File.Exists(Path.ChangeExtension(shapefilePath, ".shp")));
        Assert.False(File.Exists(Path.ChangeExtension(shapefilePath, ".shx")));
        Assert.False(File.Exists(Path.ChangeExtension(shapefilePath, ".dbf")));
        Assert.False(File.Exists(Path.ChangeExtension(shapefilePath, ".prj")));
        Assert.False(File.Exists(Path.ChangeExtension(shapefilePath, ".cpg")));
    }

    private static void AssertShapefileDoesNotExist(string shapefilePath)
    {
        Assert.False(File.Exists(Path.ChangeExtension(shapefilePath, ".shp")));
    }

    private static void AssertShapefileLayerAndType(string shapefilePath, string expectedLayer, string expectedType)
    {
        var feature = Assert.Single(Shapefile.ReadAllFeatures(shapefilePath));
        Assert.True(feature.Geometry is Polygon || feature.Geometry is MultiPolygon);
        Assert.Equal(expectedLayer, feature.Attributes["LAYER"]);
        Assert.Equal(expectedType, feature.Attributes["TYPE"]);
    }

    private static void WritePlaceholderSidecars(string shapefilePath)
    {
        File.WriteAllText(Path.ChangeExtension(shapefilePath, ".shp"), "stale");
        File.WriteAllText(Path.ChangeExtension(shapefilePath, ".shx"), "stale");
        File.WriteAllText(Path.ChangeExtension(shapefilePath, ".dbf"), "stale");
        File.WriteAllText(Path.ChangeExtension(shapefilePath, ".prj"), "stale");
        File.WriteAllText(Path.ChangeExtension(shapefilePath, ".cpg"), "stale");
    }

    private static string CreateTempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "RevitGeoSuiteShpTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }
}
