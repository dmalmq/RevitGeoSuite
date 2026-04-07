using System.Linq;
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
    }

    [Fact]
    public void ParseFile_prefers_elevated_ground_surface_ring_over_lod0_footprint_when_available()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-elevated-ground.gml");
        CityGmlParser parser = new CityGmlParser();

        PlateauCityModel model = parser.ParseFile(fixturePath);
        PlateauContextFeature building = Assert.Single(model.Features);
        PlateauCoordinate3D firstPoint = building.ExteriorRing.First();

        Assert.Equal(PlateauFeatureType.Building, building.FeatureType);
        Assert.Equal("Elevated Building", building.Name);
        Assert.Equal(42d, firstPoint.Z, 6);
        Assert.All(building.ExteriorRing, point => Assert.Equal(42d, point.Z, 6));
    }

    [Fact]
    public void ParseFile_skips_parent_road_container_and_preserves_all_child_traffic_area_polygons()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-road-trafficarea-elevated.gml");
        CityGmlParser parser = new CityGmlParser();

        PlateauCityModel model = parser.ParseFile(fixturePath);

        Assert.Equal(2, model.Features.Count);
        Assert.All(model.Features, road => Assert.Equal(PlateauFeatureType.Road, road.FeatureType));
        Assert.Equal(new[] { "traffic-area-001::1", "traffic-area-001::2" }, model.Features.Select(feature => feature.Id).ToArray());
        Assert.Equal(new[] { "Elevated Traffic Area [1]", "Elevated Traffic Area [2]" }, model.Features.Select(feature => feature.Name).ToArray());
        Assert.Equal(new[] { 40.82d, 41.15d }, model.Features.Select(feature => feature.ExteriorRing.First().Z).ToArray());
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
    }
}
