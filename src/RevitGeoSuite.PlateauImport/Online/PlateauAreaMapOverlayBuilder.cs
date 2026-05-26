using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Plateau.Catalog;

namespace RevitGeoSuite.PlateauImport.Online;

public static class PlateauAreaMapOverlayBuilder
{
    public static string Build(
        IEnumerable<(PlateauAreaOption Area, PlateauAreaBounds Bounds)> areas,
        string? selectedCode)
    {
        IEnumerable<(PlateauAreaOption Area, PlateauAreaBounds Bounds)> source =
            areas ?? Enumerable.Empty<(PlateauAreaOption Area, PlateauAreaBounds Bounds)>();

        JArray features = new JArray();
        foreach ((PlateauAreaOption area, PlateauAreaBounds bounds) in source)
        {
            if (area is null || bounds is null)
            {
                continue;
            }

            features.Add(new JObject
            {
                ["type"] = "Feature",
                ["properties"] = new JObject
                {
                    ["featureId"] = area.Code,
                    ["code"] = area.Code,
                    ["label"] = area.Label,
                    ["tileId"] = string.IsNullOrWhiteSpace(area.Label) ? area.Code : area.Label,
                    ["isSelected"] = string.Equals(area.Code, selectedCode, StringComparison.Ordinal),
                    ["isSuggested"] = false
                },
                ["geometry"] = new JObject
                {
                    ["type"] = "Polygon",
                    ["coordinates"] = new JArray
                    {
                        new JArray
                        {
                            Coordinate(bounds.WestDeg, bounds.SouthDeg),
                            Coordinate(bounds.EastDeg, bounds.SouthDeg),
                            Coordinate(bounds.EastDeg, bounds.NorthDeg),
                            Coordinate(bounds.WestDeg, bounds.NorthDeg),
                            Coordinate(bounds.WestDeg, bounds.SouthDeg)
                        }
                    }
                }
            });
        }

        JObject featureCollection = new JObject
        {
            ["type"] = "FeatureCollection",
            ["features"] = features
        };
        return featureCollection.ToString(Formatting.None);
    }

    private static JArray Coordinate(double longitude, double latitude)
    {
        return new JArray(longitude, latitude);
    }
}
