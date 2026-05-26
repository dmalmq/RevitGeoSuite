using System;
using System.IO;
using System.Linq;
using RevitGeoSuite.Core.Plateau.Schema;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class CityGmlParserTests
{
    [Fact]
    public void ParseFile_reads_fixture_city_model_and_building_footprints()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-origin-context.gml");
        CityGmlParser parser = new CityGmlParser();

        PlateauCityModel model = parser.ParseFile(fixturePath);

        Assert.Equal(fixturePath, model.SourcePath);
        Assert.Equal(6677, model.EpsgCode);
        Assert.Equal(2, model.Features.Count);
        Assert.All(model.Features, feature => Assert.Equal(PlateauFeatureType.Building, feature.FeatureType));
        Assert.Equal(new[] { "Sample Building A", "Sample Building B" }, model.Features.Select(feature => feature.Name).ToArray());
        Assert.All(model.Features, feature => Assert.True(feature.ExteriorRing.Count >= 4));
        Assert.All(model.Features, feature => Assert.Single(feature.GeometrySurfaces));
        Assert.All(model.Features, feature => Assert.Equal(0, feature.HighestLod));
    }

    [Fact]
    public void ParseFile_preserves_highest_lod_surfaces_for_detailed_building_geometry()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-detailed-building.gml");
        CityGmlParser parser = new CityGmlParser();

        PlateauCityModel model = parser.ParseFile(fixturePath);
        PlateauBuildingFeature building = Assert.IsType<PlateauBuildingFeature>(Assert.Single(model.Features));

        Assert.Equal("Detailed Building", building.Name);
        Assert.Equal(2, building.HighestLod);
        Assert.True(building.GeometrySurfaces.Count >= 7);
        Assert.All(building.GeometrySurfaces, surface => Assert.Equal(2, surface.Lod));
        Assert.Contains(building.GeometrySurfaces, surface => surface.SemanticSurfaceType == "RoofSurface");
        Assert.Contains(building.GeometrySurfaces, surface => surface.SemanticSurfaceType == "WallSurface");
        Assert.DoesNotContain(building.GeometrySurfaces, surface => surface.SurfaceId == "lod0-poly-001");
    }

    [Fact]
    public void ParseFile_prefers_elevated_ground_surface_ring_over_lod0_footprint_when_available()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-elevated-ground.gml");
        CityGmlParser parser = new CityGmlParser();

        PlateauCityModel model = parser.ParseFile(fixturePath);
        PlateauBuildingFeature building = Assert.IsType<PlateauBuildingFeature>(Assert.Single(model.Features));
        PlateauCoordinate3D firstPoint = building.ExteriorRing.First();

        Assert.Equal(PlateauFeatureType.Building, building.FeatureType);
        Assert.Equal("Elevated Building", building.Name);
        Assert.Equal(2, building.HighestLod);
        Assert.Equal(42d, firstPoint.Z, 6);
        Assert.All(building.ExteriorRing, point => Assert.Equal(42d, point.Z, 6));
        Assert.True(building.BaseElevationMeters.HasValue);
        Assert.True(building.TopElevationMeters.HasValue);
        Assert.Equal(42d, building.BaseElevationMeters.Value, 6);
        Assert.Equal(57d, building.TopElevationMeters.Value, 6);
        Assert.Equal(2, building.GeometrySurfaces.Count);
    }

    [Fact]
    public void ParseFile_skips_parent_road_container_and_preserves_all_child_traffic_area_polygons()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-road-trafficarea-elevated.gml");
        CityGmlParser parser = new CityGmlParser();

        PlateauCityModel model = parser.ParseFile(fixturePath);

        Assert.Equal(2, model.Features.Count);
        Assert.All(model.Features, road => Assert.Equal(PlateauFeatureType.Road, road.FeatureType));
        Assert.Equal(new[] { "traffic-area-high-001::1", "traffic-area-high-001::2" }, model.Features.Select(feature => feature.Id).ToArray());
        Assert.Equal(new[] { "Elevated Traffic Area [1]", "Elevated Traffic Area [2]" }, model.Features.Select(feature => feature.Name).ToArray());
        Assert.Equal(new[] { 40.82d, 41.15d }, model.Features.Select(feature => feature.ExteriorRing.First().Z).ToArray());
        Assert.All(model.Features, feature => Assert.Equal(3, feature.HighestLod));
        Assert.All(model.Features, feature => Assert.Single(feature.GeometrySurfaces));
    }

    [Fact]
    public void ParseFile_enumerates_mixed_supported_features_in_descriptor_order()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:bldg=""{PlateauConstants.BuildingNamespace.NamespaceName}""
                xmlns:brid=""{PlateauConstants.BridgeNamespace.NamespaceName}""
                xmlns:tran=""{PlateauConstants.TransportationNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6677"">
  <core:cityObjectMember>
    <tran:Road gml:id=""road-1"">
      <gml:name>Road First In Xml</gml:name>
      <tran:lod0Network>
        <gml:GeometricComplex>
          <gml:element>
            <gml:Polygon gml:id=""road-poly"">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList>0 0 0 10 0 0 10 10 0 0 10 0 0 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:element>
        </gml:GeometricComplex>
      </tran:lod0Network>
    </tran:Road>
  </core:cityObjectMember>
  <core:cityObjectMember>
    <bldg:Building gml:id=""building-1"">
      <gml:name>Building Second In Xml</gml:name>
      <bldg:lod0FootPrint>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id=""building-poly"">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList>20 0 0 30 0 0 30 10 0 20 10 0 20 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod0FootPrint>
    </bldg:Building>
  </core:cityObjectMember>
  <core:cityObjectMember>
    <brid:Bridge gml:id=""bridge-1"">
      <gml:name>Bridge Third In Xml</gml:name>
      <brid:lod0Geometry>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id=""bridge-poly"">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList>40 0 0 50 0 0 50 10 0 40 10 0 40 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </brid:lod0Geometry>
    </brid:Bridge>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);

            Assert.Equal(
                new[]
                {
                    PlateauFeatureType.Building,
                    PlateauFeatureType.Bridge,
                    PlateauFeatureType.Road
                },
                model.Features.Select(feature => feature.FeatureType).ToArray());
            Assert.Equal(
                new[]
                {
                    "Building Second In Xml",
                    "Bridge Third In Xml",
                    "Road First In Xml"
                },
                model.Features.Select(feature => feature.Name).ToArray());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void ParseFile_reads_transportation_features_and_tile_id_from_path()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport", "53394536_tran_sample.gml");
        CityGmlParser parser = new CityGmlParser();

        PlateauCityModel model = parser.ParseFile(fixturePath);
        PlateauContextFeature road = Assert.Single(model.Features);

        Assert.Equal("53394536", model.FileTileId);
        Assert.Equal(PlateauFeatureType.Road, road.FeatureType);
        Assert.Equal("53394536", road.TileId);
        Assert.Equal("Folder Road A", road.Name);
        Assert.True(road.ExteriorRing.Count >= 4);
        Assert.Single(road.GeometrySurfaces);
    }

    [Fact]
    public void ParseFile_skips_parent_bridge_when_child_bridge_parts_are_available()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:brid=""{PlateauConstants.BridgeNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6677"">
  <core:cityObjectMember>
    <brid:Bridge gml:id=""bridge-parent"">
      <gml:name>Parent Bridge</gml:name>
      <brid:lod1MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id=""parent-poly"">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList>0 0 0 40 0 0 40 10 0 0 10 0 0 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </brid:lod1MultiSurface>
      <brid:consistsOfBridgePart>
        <brid:BridgePart gml:id=""bridge-part-1"">
          <gml:name>Deck Part</gml:name>
          <brid:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""part-poly"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList>0 0 10 20 0 10 20 8 10 0 8 10 0 0 10</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </brid:lod2MultiSurface>
        </brid:BridgePart>
      </brid:consistsOfBridgePart>
      <brid:outerBridgeConstruction>
        <brid:BridgeConstructionElement gml:id=""bridge-element-1"">
          <gml:name>Pier Element</gml:name>
          <brid:lod2Geometry>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""element-poly"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList>24 2 0 28 2 0 28 6 0 24 6 0 24 2 0</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </brid:lod2Geometry>
        </brid:BridgeConstructionElement>
      </brid:outerBridgeConstruction>
    </brid:Bridge>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);

            Assert.Equal(2, model.Features.Count);
            Assert.All(model.Features, feature => Assert.Equal(PlateauFeatureType.Bridge, feature.FeatureType));
            Assert.Equal(new[] { "bridge-part-1", "bridge-element-1" }, model.Features.Select(feature => feature.Id).ToArray());
            Assert.Equal(new[] { "Deck Part", "Pier Element" }, model.Features.Select(feature => feature.Name).ToArray());
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    [Fact]
    public void ParseFile_reads_bridge_features_and_tile_id_from_path()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "FolderImport", "54394536_brid_sample.gml");
        CityGmlParser parser = new CityGmlParser();

        PlateauCityModel model = parser.ParseFile(fixturePath);
        PlateauContextFeature bridge = Assert.Single(model.Features);

        Assert.Equal("54394536", model.FileTileId);
        Assert.Equal(PlateauFeatureType.Bridge, bridge.FeatureType);
        Assert.Equal("54394536", bridge.TileId);
        Assert.Equal("Folder Bridge A", bridge.Name);
        Assert.True(bridge.ExteriorRing.Count >= 4);
        Assert.Single(bridge.GeometrySurfaces);
    }

    [Fact]
    public void ParseFile_reads_land_use_secondary_mesh_and_common_land_use_type_codelist()
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitGeoSuiteLandUseParserTests", Guid.NewGuid().ToString("N"));
        string luseDirectory = Path.Combine(root, "udx", "luse");
        string codelistDirectory = Path.Combine(root, "codelists");
        string filePath = Path.Combine(luseDirectory, "533945_luse_6697_op.gml");
        try
        {
            Directory.CreateDirectory(luseDirectory);
            Directory.CreateDirectory(codelistDirectory);
            File.WriteAllText(
                Path.Combine(codelistDirectory, "Common_landUseType.xml"),
                @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gml:Dictionary xmlns:gml=""http://www.opengis.net/gml"">
  <gml:dictionaryEntry>
    <gml:Definition gml:id=""Common_landUseType_6"">
      <gml:description>住宅用地</gml:description>
      <gml:name>211</gml:name>
    </gml:Definition>
  </gml:dictionaryEntry>
</gml:Dictionary>");
            string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:luse=""{PlateauConstants.LandUseNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6697"">
  <core:cityObjectMember>
    <luse:LandUse gml:id=""luse-1"">
      <luse:class codeSpace=""../../codelists/Common_landUseType.xml"">211</luse:class>
      <luse:lod1MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon>
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList>35.0 139.0 0 35.0 139.1 0 35.1 139.1 0 35.1 139.0 0 35.0 139.0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </luse:lod1MultiSurface>
    </luse:LandUse>
  </core:cityObjectMember>
</core:CityModel>";
            File.WriteAllText(filePath, xml);

            PlateauCityModel model = new CityGmlParser().ParseFile(filePath);
            PlateauContextFeature landUse = Assert.Single(model.Features);

            Assert.Equal("533945", model.FileTileId);
            Assert.Equal(PlateauFeatureType.LandUse, landUse.FeatureType);
            Assert.Equal("533945", landUse.TileId);
            Assert.Equal("211", landUse.ClassCode);
            Assert.Equal("住宅用地", landUse.ClassName);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
