using System;
using System.Collections.Generic;

namespace RevitGeoSuite.SharedUI.Controls;

public sealed class MapBridgeMessage
{
    public string Type { get; set; } = string.Empty;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }

    public string FeatureId { get; set; } = string.Empty;

    public IReadOnlyList<string> FeatureIds { get; set; } = Array.Empty<string>();
}
