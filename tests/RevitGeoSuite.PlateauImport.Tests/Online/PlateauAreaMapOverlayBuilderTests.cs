using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class PlateauAreaMapOverlayBuilderTests
{
    [Fact]
    public void Build_emits_polygon_features_with_selection_properties()
    {
        PlateauAreaOption chiyoda = new PlateauAreaOption("13101", Array.Empty<string>(), "東京都 \"千代田区\"", "東京都", "千代田区", string.Empty);
        PlateauAreaOption chuo = new PlateauAreaOption("13102", Array.Empty<string>(), "東京都 中央区", "東京都", "中央区", string.Empty);

        string geoJson = PlateauAreaMapOverlayBuilder.Build(
            new[]
            {
                (chiyoda, new PlateauAreaBounds(139.74, 35.67, 139.77, 35.70)),
                (chuo, new PlateauAreaBounds(139.76, 35.65, 139.79, 35.68))
            },
            selectedCode: "13102");

        JObject featureCollection = JObject.Parse(geoJson);
        JArray features = (JArray)featureCollection["features"]!;

        Assert.Equal("FeatureCollection", (string?)featureCollection["type"]);
        Assert.Equal(2, features.Count);
        Assert.All(features, feature => Assert.Equal("Polygon", (string?)feature["geometry"]?["type"]));

        JObject chiyodaFeature = (JObject)features.Single(feature => (string?)feature["properties"]?["featureId"] == "13101");
        Assert.Equal("13101", (string?)chiyodaFeature["properties"]?["code"]);
        Assert.Equal("東京都 \"千代田区\"", (string?)chiyodaFeature["properties"]?["label"]);
        Assert.False((bool?)chiyodaFeature["properties"]?["isSelected"]);

        JObject chuoFeature = (JObject)features.Single(feature => (string?)feature["properties"]?["featureId"] == "13102");
        Assert.Equal("東京都 中央区", (string?)chuoFeature["properties"]?["tileId"]);
        Assert.True((bool?)chuoFeature["properties"]?["isSelected"]);
    }
}
