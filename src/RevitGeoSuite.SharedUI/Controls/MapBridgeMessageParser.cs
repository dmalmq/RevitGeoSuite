using System;
using System.Collections.Generic;
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
            Longitude = (double?)payload["longitude"],
            FeatureId = (string?)payload["featureId"] ?? string.Empty,
            FeatureIds = ParseFeatureIds(payload["featureIds"])
        };
    }

    private static IReadOnlyList<string> ParseFeatureIds(JToken? token)
    {
        if (token is not JArray array)
        {
            return Array.Empty<string>();
        }

        List<string> ids = new List<string>(array.Count);
        foreach (JToken element in array)
        {
            string? id = (string?)element;
            if (!string.IsNullOrWhiteSpace(id))
            {
                ids.Add(id!);
            }
        }

        return ids;
    }
}
