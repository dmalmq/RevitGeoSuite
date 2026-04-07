namespace RevitGeoSuite.SharedUI.Controls;

public sealed class MapReferenceMarker
{
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Kind { get; set; } = "context";
}
