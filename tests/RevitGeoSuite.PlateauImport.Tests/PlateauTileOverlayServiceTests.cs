using Newtonsoft.Json.Linq;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class PlateauTileOverlayServiceTests
{
    [Fact]
    public void Create_geojson_includes_detected_tile_properties_and_selection_state()
    {
        PlateauTileOverlayService service = new PlateauTileOverlayService();

        string geoJson = service.CreateGeoJson(new[]
        {
            new PlateauTileSelectionItem
            {
                TileId = "53394536",
                FeatureCount = 4,
                SourceFileCount = 2,
                IsSuggested = true,
                IsSelected = false
            },
            new PlateauTileSelectionItem
            {
                TileId = "53394537",
                FeatureCount = 3,
                SourceFileCount = 1,
                IsSuggested = false,
                IsSelected = true
            }
        });

        JObject featureCollection = JObject.Parse(geoJson);
        JArray features = (JArray)featureCollection["features"]!;

        Assert.Equal(2, features.Count);
        Assert.Contains(features, feature => (string?)feature["properties"]?["tileId"] == "53394536");
        Assert.Contains(features, feature => (bool?)feature["properties"]?["isSuggested"] == true);
        Assert.Contains(features, feature => (bool?)feature["properties"]?["isSelected"] == true);
        Assert.All(features, feature => Assert.Equal("Polygon", (string?)feature["geometry"]?["type"]));
    }
}
