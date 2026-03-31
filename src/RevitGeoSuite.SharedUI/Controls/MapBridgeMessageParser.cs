using Newtonsoft.Json.Linq;

namespace RevitGeoSuite.SharedUI.Controls;

public static class MapBridgeMessageParser
{
    public static MapBridgeMessage Parse(string json)
    {
        JObject payload = JObject.Parse(json);

        return new MapBridgeMessage
        {
            Type = (string?)payload["type"] ?? string.Empty,
            Latitude = (double?)payload["latitude"],
            Longitude = (double?)payload["longitude"]
        };
    }
}
