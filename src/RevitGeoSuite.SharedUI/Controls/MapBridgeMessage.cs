namespace RevitGeoSuite.SharedUI.Controls;

public sealed class MapBridgeMessage
{
    public string Type { get; set; } = string.Empty;

    public double? Latitude { get; set; }

    public double? Longitude { get; set; }
}
