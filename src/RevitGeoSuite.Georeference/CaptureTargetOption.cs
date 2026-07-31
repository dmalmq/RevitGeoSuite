namespace RevitGeoSuite.Georeference;

public sealed class CaptureTargetOption
{
    public ReferenceCaptureTarget Target { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
