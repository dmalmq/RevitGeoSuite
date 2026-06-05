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
    public void ParseFile_exports_parent_bridge_when_construction_element_is_available()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:brid=""{PlateauConstants.BridgeNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6677"">
  <core:cityObjectMember>
    <brid:Bridge gml:id=""brid_5ee5c656-0cd8-4913-b2aa-dd68097d2f3b"">
      <gml:name>Parent Bridge With Construction</gml:name>
      <brid:outerBridgeConstruction>
        <brid:BridgeConstructionElement gml:id=""brid_5ee5c656-0cd8-4913-b2aa-dd68097d2f3b_cons"">
          <gml:name>Construction Element</gml:name>
          <brid:lod2Geometry>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""construction-poly"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">1 1 0 4 1 0 4 4 0 1 4 0 1 1 0</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </brid:lod2Geometry>
        </brid:BridgeConstructionElement>
      </brid:outerBridgeConstruction>
      <brid:boundedBy>
        <brid:GroundSurface>
          <brid:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""parent-ground"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 10 20 0 10 20 8 10 0 8 10 0 0 10</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""parent-ground-island"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">30 0 10 35 0 10 35 4 10 30 4 10 30 0 10</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </brid:lod2MultiSurface>
        </brid:GroundSurface>
      </brid:boundedBy>
    </brid:Bridge>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);

            PlateauContextFeature[] parentParts = model.Features
                .Where(feature => feature.Id == "brid_5ee5c656-0cd8-4913-b2aa-dd68097d2f3b"
                    || feature.Id.StartsWith("brid_5ee5c656-0cd8-4913-b2aa-dd68097d2f3b::", StringComparison.Ordinal))
                .ToArray();
            PlateauContextFeature parent = Assert.Single(model.Features, feature => feature.Id == "brid_5ee5c656-0cd8-4913-b2aa-dd68097d2f3b");
            Assert.Equal(2, parentParts.Length);
            Assert.Contains(parentParts, feature => feature.Id == "brid_5ee5c656-0cd8-4913-b2aa-dd68097d2f3b::2");
            PlateauContextFeature construction = Assert.Single(model.Features, feature => feature.Id == "brid_5ee5c656-0cd8-4913-b2aa-dd68097d2f3b_cons");

            Assert.Equal(PlateauFeatureType.Bridge, parent.FeatureType);
            Assert.Equal(PlateauFeatureType.Bridge, construction.FeatureType);
            Assert.Contains(parentParts, feature => feature.ExteriorRing.Any(point => point.X == 20d && point.Y == 8d && point.Z == 10d));
            Assert.All(parentParts.SelectMany(feature => feature.ExteriorRing), point => Assert.Equal(10d, point.Z, 6));
            Assert.Contains(construction.ExteriorRing, point => point.X == 4d && point.Y == 4d && point.Z == 0d);
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
    public void ParseFile_ignores_nested_bridge_construction_when_selecting_parent_footprint()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:brid=""{PlateauConstants.BridgeNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6697"">
  <core:cityObjectMember>
    <brid:Bridge gml:id=""brid_6aaccebb-3bae-4723-94fb-062f4e956d7b"">
      <gml:name>Geographic Bridge With Construction</gml:name>
      <brid:outerBridgeConstruction>
        <brid:BridgeConstructionElement gml:id=""brid_6aaccebb-3bae-4723-94fb-062f4e956d7b_cons"">
          <brid:boundedBy>
            <brid:GroundSurface>
              <brid:lod2MultiSurface>
                <gml:MultiSurface>
                  <gml:surfaceMember>
                    <gml:Polygon gml:id=""construction-ground"">
                      <gml:exterior>
                        <gml:LinearRing>
                          <gml:posList srsDimension=""3"">35.695830 139.699394 34 35.695835 139.699394 34 35.695835 139.699399 34 35.695830 139.699399 34 35.695830 139.699394 34</gml:posList>
                        </gml:LinearRing>
                      </gml:exterior>
                    </gml:Polygon>
                  </gml:surfaceMember>
                </gml:MultiSurface>
              </brid:lod2MultiSurface>
            </brid:GroundSurface>
          </brid:boundedBy>
        </brid:BridgeConstructionElement>
      </brid:outerBridgeConstruction>
      <brid:boundedBy>
        <brid:GroundSurface>
          <brid:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""parent-ground-a"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">35.695000 139.699000 40 35.695000 139.699100 40 35.695100 139.699100 40 35.695000 139.699000 40</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""parent-ground-b"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">35.695000 139.699000 40 35.695100 139.699100 40 35.695100 139.699000 40 35.695000 139.699000 40</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </brid:lod2MultiSurface>
        </brid:GroundSurface>
      </brid:boundedBy>
    </brid:Bridge>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);

            PlateauContextFeature parent = Assert.Single(model.Features, feature => feature.Id == "brid_6aaccebb-3bae-4723-94fb-062f4e956d7b");
            Assert.Single(model.Features, feature => feature.Id == "brid_6aaccebb-3bae-4723-94fb-062f4e956d7b_cons");

            (double X, double Y)[] xy = parent.ExteriorRing
                .Select(point => (point.X, point.Y))
                .ToArray();
            Assert.Contains((35.695000d, 139.699000d), xy);
            Assert.Contains((35.695000d, 139.699100d), xy);
            Assert.Contains((35.695100d, 139.699100d), xy);
            Assert.Contains((35.695100d, 139.699000d), xy);
            Assert.All(parent.ExteriorRing, point => Assert.Equal(40d, point.Z, 6));
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
    public void ParseFile_unions_triangulated_ground_surface_into_single_building_footprint()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        // GroundSurface tiled by four right triangles that together cover a 10x10 square.
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:bldg=""{PlateauConstants.BuildingNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6677"">
  <core:cityObjectMember>
    <bldg:Building gml:id=""bldg-triangulated"">
      <gml:name>Triangulated Ground Building</gml:name>
      <bldg:boundedBy>
        <bldg:GroundSurface>
          <bldg:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""g-1"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 5 10 0 5 10 5 5 0 0 5</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""g-2"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">10 5 5 10 10 5 0 10 5 10 5 5</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""g-3"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 5 10 5 5 0 10 5 0 0 5</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </bldg:lod2MultiSurface>
        </bldg:GroundSurface>
      </bldg:boundedBy>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);
            PlateauContextFeature building = Assert.Single(model.Features);

            Assert.Equal(PlateauFeatureType.Building, building.FeatureType);

            (double X, double Y)[] xy = building.ExteriorRing
                .Select(point => (point.X, point.Y))
                .ToArray();
            // Without the union the parser would have returned a single triangle (3 vertices); the union
            // restores the full square outline. NTS may keep colinear boundary vertices, so we assert that
            // the four square corners are present rather than the exact vertex count.
            Assert.True(building.ExteriorRing.Count >= 4, $"expected at least 4 vertices, got {building.ExteriorRing.Count}");
            Assert.Contains((0d, 0d), xy);
            Assert.Contains((10d, 0d), xy);
            Assert.Contains((10d, 10d), xy);
            Assert.Contains((0d, 10d), xy);
            Assert.All(building.ExteriorRing, point => Assert.Equal(5d, point.Z, 6));
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
    public void ParseFile_emits_multipart_features_when_ground_surface_has_disjoint_polygons()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:bldg=""{PlateauConstants.BuildingNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6677"">
  <core:cityObjectMember>
    <bldg:Building gml:id=""bldg-disjoint"">
      <gml:name>Disjoint Ground Building</gml:name>
      <bldg:boundedBy>
        <bldg:GroundSurface>
          <bldg:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""g-left"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 5 10 0 5 10 10 5 0 10 5 0 0 5</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""g-right"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">50 0 5 60 0 5 60 10 5 50 10 5 50 0 5</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </bldg:lod2MultiSurface>
        </bldg:GroundSurface>
      </bldg:boundedBy>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);

            Assert.Equal(2, model.Features.Count);
            Assert.All(model.Features, feature => Assert.Equal(PlateauFeatureType.Building, feature.FeatureType));
            Assert.Equal(new[] { "bldg-disjoint::1", "bldg-disjoint::2" }, model.Features.Select(feature => feature.Id).ToArray());
            Assert.Equal(new[] { "Disjoint Ground Building [1]", "Disjoint Ground Building [2]" }, model.Features.Select(feature => feature.Name).ToArray());
            Assert.All(model.Features, feature => Assert.Equal(4, feature.ExteriorRing.Count));
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
    public void ParseFile_unions_across_priority_buckets_when_groundsurface_is_a_corner_triangle()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        // GroundSurface covers only the bottom-left corner triangle of a 10x10 square.
        // lod1Solid bottom polygon covers the full square. Expected: union picks the full
        // square outline because the priority-0 GroundSurface alone is too small.
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:bldg=""{PlateauConstants.BuildingNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6677"">
  <core:cityObjectMember>
    <bldg:Building gml:id=""bldg-corner-ground"">
      <gml:name>Corner Ground Building</gml:name>
      <bldg:boundedBy>
        <bldg:GroundSurface>
          <bldg:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 5 4 0 5 0 4 5 0 0 5</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </bldg:lod2MultiSurface>
        </bldg:GroundSurface>
      </bldg:boundedBy>
      <bldg:lod1Solid>
        <gml:Solid>
          <gml:exterior>
            <gml:CompositeSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 5 10 0 5 10 10 5 0 10 5 0 0 5</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:CompositeSurface>
          </gml:exterior>
        </gml:Solid>
      </bldg:lod1Solid>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);
            PlateauContextFeature building = Assert.Single(model.Features);

            (double X, double Y)[] xy = building.ExteriorRing
                .Select(point => (point.X, point.Y))
                .ToArray();
            Assert.True(building.ExteriorRing.Count >= 4, $"expected >= 4 vertices, got {building.ExteriorRing.Count}");
            // All four square corners must appear; if the priority filter blocked the lod1Solid bottom
            // polygon, the only candidate left would be the 3-vertex GroundSurface corner triangle.
            Assert.Contains((0d, 0d), xy);
            Assert.Contains((10d, 0d), xy);
            Assert.Contains((10d, 10d), xy);
            Assert.Contains((0d, 10d), xy);
            // Z stays at the GroundSurface elevation (5), not at the lod1 elevation, because
            // selectedCandidate (semantic priority winner) still provides the elevation.
            Assert.All(building.ExteriorRing, point => Assert.Equal(5d, point.Z, 6));
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
    public void ParseFile_unions_triangulated_hilton_style_ground_surface_with_lod1_bottom()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        // Mirrors the Hilton Tokyo pattern: a building whose ground footprint is a 4-vertex
        // square represented in two ways simultaneously:
        //   (1) <bldg:GroundSurface> blocks each containing one triangle (the ground is
        //       triangulated into 2 triangles), and
        //   (2) a single <bldg:lod1Solid> whose bottom polygon is the full square.
        // The parser must produce the full square outline at the GroundSurface elevation (Z=10),
        // never just a triangle from one of the GroundSurface blocks.
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:bldg=""{PlateauConstants.BuildingNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6677"">
  <core:cityObjectMember>
    <bldg:Building gml:id=""bldg-hilton-style"">
      <gml:name>Hilton Style Building</gml:name>
      <bldg:lod0RoofEdge>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon>
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension=""3"">0 0 0 10 0 0 10 10 0 0 10 0 0 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </bldg:lod0RoofEdge>
      <bldg:lod1Solid>
        <gml:Solid>
          <gml:exterior>
            <gml:CompositeSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 10 10 0 10 10 10 10 0 10 10 0 0 10</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:CompositeSurface>
          </gml:exterior>
        </gml:Solid>
      </bldg:lod1Solid>
      <bldg:boundedBy>
        <bldg:GroundSurface>
          <bldg:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 10 10 0 10 10 10 10 0 0 10</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </bldg:lod2MultiSurface>
        </bldg:GroundSurface>
      </bldg:boundedBy>
      <bldg:boundedBy>
        <bldg:GroundSurface>
          <bldg:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 10 10 10 10 0 10 10 0 0 10</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </bldg:lod2MultiSurface>
        </bldg:GroundSurface>
      </bldg:boundedBy>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);
            PlateauContextFeature building = Assert.Single(model.Features);

            (double X, double Y)[] xy = building.ExteriorRing
                .Select(point => (point.X, point.Y))
                .ToArray();
            Assert.True(building.ExteriorRing.Count >= 4, $"expected >= 4 vertices, got {building.ExteriorRing.Count}");
            Assert.Contains((0d, 0d), xy);
            Assert.Contains((10d, 0d), xy);
            Assert.Contains((10d, 10d), xy);
            Assert.Contains((0d, 10d), xy);
            Assert.All(building.ExteriorRing, point => Assert.Equal(10d, point.Z, 6));
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
    public void ParseFile_unions_geographic_ground_surface_triangles_into_full_building_footprint()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:bldg=""{PlateauConstants.BuildingNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6697"">
  <core:cityObjectMember>
    <bldg:Building gml:id=""bldg-geographic-triangles"">
      <gml:name>Geographic Triangulated Ground</gml:name>
      <bldg:boundedBy>
        <bldg:GroundSurface>
          <bldg:lod2MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""geo-ground-a"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">35.690000 139.695000 12 35.690000 139.695100 12 35.690100 139.695100 12 35.690000 139.695000 12</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""geo-ground-b"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">35.690000 139.695000 12 35.690100 139.695100 12 35.690100 139.695000 12 35.690000 139.695000 12</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </bldg:lod2MultiSurface>
        </bldg:GroundSurface>
      </bldg:boundedBy>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);
            PlateauContextFeature building = Assert.Single(model.Features);

            (double X, double Y)[] xy = building.ExteriorRing
                .Select(point => (point.X, point.Y))
                .ToArray();
            Assert.True(building.ExteriorRing.Count >= 4, $"expected >= 4 vertices, got {building.ExteriorRing.Count}");
            Assert.Contains((35.690000d, 139.695000d), xy);
            Assert.Contains((35.690000d, 139.695100d), xy);
            Assert.Contains((35.690100d, 139.695100d), xy);
            Assert.Contains((35.690100d, 139.695000d), xy);
            Assert.All(building.ExteriorRing, point => Assert.Equal(12d, point.Z, 6));
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
    public void ParseFile_ignores_nested_building_installation_when_selecting_parent_footprint()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:bldg=""{PlateauConstants.BuildingNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6677"">
  <core:cityObjectMember>
    <bldg:Building gml:id=""bldg-parent-with-installation"">
      <gml:name>Parent With Installation</gml:name>
      <bldg:lod1Solid>
        <gml:Solid>
          <gml:exterior>
            <gml:CompositeSurface>
              <gml:surfaceMember>
                <gml:Polygon gml:id=""parent-bottom"">
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 10 20 0 10 20 12 10 0 12 10 0 0 10</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:CompositeSurface>
          </gml:exterior>
        </gml:Solid>
      </bldg:lod1Solid>
      <bldg:outerBuildingInstallation>
        <bldg:BuildingInstallation gml:id=""installation-1"">
          <bldg:boundedBy>
            <bldg:GroundSurface>
              <bldg:lod2MultiSurface>
                <gml:MultiSurface>
                  <gml:surfaceMember>
                    <gml:Polygon gml:id=""installation-ground"">
                      <gml:exterior>
                        <gml:LinearRing>
                          <gml:posList srsDimension=""3"">1 1 2 4 1 2 4 3 2 1 3 2 1 1 2</gml:posList>
                        </gml:LinearRing>
                      </gml:exterior>
                    </gml:Polygon>
                  </gml:surfaceMember>
                </gml:MultiSurface>
              </bldg:lod2MultiSurface>
            </bldg:GroundSurface>
          </bldg:boundedBy>
        </bldg:BuildingInstallation>
      </bldg:outerBuildingInstallation>
    </bldg:Building>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);
            PlateauContextFeature building = Assert.Single(model.Features);

            (double X, double Y)[] xy = building.ExteriorRing
                .Select(point => (point.X, point.Y))
                .ToArray();
            Assert.Contains((0d, 0d), xy);
            Assert.Contains((20d, 0d), xy);
            Assert.Contains((20d, 12d), xy);
            Assert.Contains((0d, 12d), xy);
            Assert.All(building.ExteriorRing, point => Assert.Equal(10d, point.Z, 6));
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
    public void ParseFile_classifies_traffic_area_with_function_2000_as_sidewalk()
    {
        string root = Path.Combine(Path.GetTempPath(), "RevitGeoSuiteSidewalkParserTests", Guid.NewGuid().ToString("N"));
        string tranDirectory = Path.Combine(root, "udx", "tran");
        string codelistDirectory = Path.Combine(root, "codelists");
        string filePath = Path.Combine(tranDirectory, "533945_tran_6697_op.gml");
        try
        {
            Directory.CreateDirectory(tranDirectory);
            Directory.CreateDirectory(codelistDirectory);
            File.WriteAllText(
                Path.Combine(codelistDirectory, "TrafficArea_function.xml"),
                @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gml:Dictionary xmlns:gml=""http://www.opengis.net/gml"">
  <gml:dictionaryEntry>
    <gml:Definition gml:id=""TrafficArea_function_2000"">
      <gml:description>歩道部</gml:description>
      <gml:name>2000</gml:name>
    </gml:Definition>
  </gml:dictionaryEntry>
  <gml:dictionaryEntry>
    <gml:Definition gml:id=""TrafficArea_function_3000"">
      <gml:description>車道部</gml:description>
      <gml:name>3000</gml:name>
    </gml:Definition>
  </gml:dictionaryEntry>
</gml:Dictionary>");
            string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:tran=""{PlateauConstants.TransportationNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6697"">
  <core:cityObjectMember>
    <tran:Road gml:id=""road-1"">
      <gml:name>Sidewalk Road</gml:name>
      <tran:trafficArea>
        <tran:TrafficArea gml:id=""traf-sidewalk-1"">
          <tran:function codeSpace=""../../codelists/TrafficArea_function.xml"">2000</tran:function>
          <tran:lod3MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 1 5 0 1 5 2 1 0 2 1 0 0 1</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </tran:lod3MultiSurface>
        </tran:TrafficArea>
      </tran:trafficArea>
    </tran:Road>
  </core:cityObjectMember>
</core:CityModel>";
            File.WriteAllText(filePath, xml);

            PlateauCityModel model = new CityGmlParser().ParseFile(filePath);
            PlateauContextFeature feature = Assert.Single(model.Features);

            Assert.Equal(PlateauFeatureType.Sidewalk, feature.FeatureType);
            Assert.Equal("2000", feature.ClassCode);
            Assert.Equal("歩道部", feature.ClassName);
            Assert.Equal("road-1:sidewalk", feature.Id);
            Assert.Contains("歩道", feature.Name);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ParseFile_groups_multiple_sidewalk_traffic_areas_per_road_into_one_feature()
    {
        // One Road with two adjacent sidewalk TrafficAreas (sharing an edge) plus one roadway
        // TrafficArea. The sidewalks merge into one PlateauFeatureType.Sidewalk feature; the
        // roadway stays as a PlateauFeatureType.Road feature.
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:tran=""{PlateauConstants.TransportationNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6697"">
  <core:cityObjectMember>
    <tran:Road gml:id=""road-merge"">
      <gml:name>Merge Road</gml:name>
      <tran:trafficArea>
        <tran:TrafficArea gml:id=""traf-sw-left"">
          <tran:function>2000</tran:function>
          <tran:lod3MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 1 5 0 1 5 5 1 0 5 1 0 0 1</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </tran:lod3MultiSurface>
        </tran:TrafficArea>
      </tran:trafficArea>
      <tran:trafficArea>
        <tran:TrafficArea gml:id=""traf-sw-right"">
          <tran:function>2000</tran:function>
          <tran:lod3MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">5 0 1 10 0 1 10 5 1 5 5 1 5 0 1</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </tran:lod3MultiSurface>
        </tran:TrafficArea>
      </tran:trafficArea>
      <tran:trafficArea>
        <tran:TrafficArea gml:id=""traf-road"">
          <tran:function>3000</tran:function>
          <tran:lod3MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 5 1 10 5 1 10 10 1 0 10 1 0 5 1</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </tran:lod3MultiSurface>
        </tran:TrafficArea>
      </tran:trafficArea>
    </tran:Road>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);

            PlateauContextFeature sidewalk = Assert.Single(model.Features, f => f.FeatureType == PlateauFeatureType.Sidewalk);
            PlateauContextFeature road = Assert.Single(model.Features, f => f.FeatureType == PlateauFeatureType.Road);

            Assert.Equal("road-merge:sidewalk", sidewalk.Id);
            Assert.Equal("traf-road", road.Id);

            (double X, double Y)[] sidewalkXy = sidewalk.ExteriorRing
                .Select(point => (point.X, point.Y))
                .ToArray();
            // Both sidewalk quads share the edge x=5 and merge into a 10x5 rectangle.
            Assert.Contains((0d, 0d), sidewalkXy);
            Assert.Contains((10d, 0d), sidewalkXy);
            Assert.Contains((10d, 5d), sidewalkXy);
            Assert.Contains((0d, 5d), sidewalkXy);
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
    public void ParseFile_emits_multipart_features_when_road_sidewalks_are_disjoint()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:tran=""{PlateauConstants.TransportationNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6697"">
  <core:cityObjectMember>
    <tran:Road gml:id=""road-disjoint"">
      <gml:name>Disjoint Sidewalk Road</gml:name>
      <tran:trafficArea>
        <tran:TrafficArea gml:id=""traf-sw-island-a"">
          <tran:function>2000</tran:function>
          <tran:lod3MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">0 0 1 5 0 1 5 5 1 0 5 1 0 0 1</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </tran:lod3MultiSurface>
        </tran:TrafficArea>
      </tran:trafficArea>
      <tran:trafficArea>
        <tran:TrafficArea gml:id=""traf-sw-island-b"">
          <tran:function>2000</tran:function>
          <tran:lod3MultiSurface>
            <gml:MultiSurface>
              <gml:surfaceMember>
                <gml:Polygon>
                  <gml:exterior>
                    <gml:LinearRing>
                      <gml:posList srsDimension=""3"">50 0 1 55 0 1 55 5 1 50 5 1 50 0 1</gml:posList>
                    </gml:LinearRing>
                  </gml:exterior>
                </gml:Polygon>
              </gml:surfaceMember>
            </gml:MultiSurface>
          </tran:lod3MultiSurface>
        </tran:TrafficArea>
      </tran:trafficArea>
    </tran:Road>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);

            PlateauContextFeature[] sidewalks = model.Features
                .Where(f => f.FeatureType == PlateauFeatureType.Sidewalk)
                .OrderBy(f => f.Id, StringComparer.Ordinal)
                .ToArray();
            Assert.Equal(2, sidewalks.Length);
            Assert.Equal(new[] { "road-disjoint:sidewalk::1", "road-disjoint:sidewalk::2" }, sidewalks.Select(f => f.Id).ToArray());
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

    [Fact]
    public void ParseFile_drops_near_vertical_road_polygons()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:tran=""{PlateauConstants.TransportationNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6677"">
  <core:cityObjectMember>
    <tran:Road gml:id=""road-vertical-test"">
      <tran:lod1MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id=""horizontal-triangle"">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList>0 0 0 10 0 0 0 10 0 0 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
          <gml:surfaceMember>
            <gml:Polygon gml:id=""vertical-curb-face"">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList>0 0 0 1 0 0 1 0 0.5 0 0 0.5 0 0 0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </tran:lod1MultiSurface>
    </tran:Road>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);

            PlateauContextFeature road = Assert.Single(model.Features);
            Assert.Equal(PlateauFeatureType.Road, road.FeatureType);
            Assert.Equal("road-vertical-test", road.Id);
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
    public void ParseFile_keeps_geographic_road_polygons_with_realistic_z_slope()
    {
        string tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".gml");
        string xml = $@"<?xml version=""1.0"" encoding=""utf-8""?>
<core:CityModel xmlns:core=""{PlateauConstants.CoreNamespace.NamespaceName}""
                xmlns:gml=""{PlateauConstants.GmlNamespace.NamespaceName}""
                xmlns:tran=""{PlateauConstants.TransportationNamespace.NamespaceName}""
                srsName=""urn:ogc:def:crs:EPSG::6697"">
  <core:cityObjectMember>
    <tran:Road gml:id=""road-geographic-slope-test"">
      <tran:lod1MultiSurface>
        <gml:MultiSurface>
          <gml:surfaceMember>
            <gml:Polygon gml:id=""sloped-road-surface"">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension=""3"">35.680000 139.770000 10.0 35.680000 139.770100 10.2 35.680100 139.770100 10.3 35.680100 139.770000 10.1 35.680000 139.770000 10.0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
          <gml:surfaceMember>
            <gml:Polygon gml:id=""geographic-curb-face"">
              <gml:exterior>
                <gml:LinearRing>
                  <gml:posList srsDimension=""3"">35.680000 139.770000 0.0 35.680000 139.770010 0.0 35.680000 139.770010 0.5 35.680000 139.770000 0.5 35.680000 139.770000 0.0</gml:posList>
                </gml:LinearRing>
              </gml:exterior>
            </gml:Polygon>
          </gml:surfaceMember>
        </gml:MultiSurface>
      </tran:lod1MultiSurface>
    </tran:Road>
  </core:cityObjectMember>
</core:CityModel>";

        try
        {
            File.WriteAllText(tempPath, xml);
            PlateauCityModel model = new CityGmlParser().ParseFile(tempPath);

            PlateauContextFeature road = Assert.Single(model.Features);
            Assert.Equal(PlateauFeatureType.Road, road.FeatureType);
            Assert.Equal("road-geographic-slope-test", road.Id);
            Assert.Contains(road.ExteriorRing, point => point.X == 35.680100d && point.Y == 139.770100d && point.Z == 10.3d);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }
}
