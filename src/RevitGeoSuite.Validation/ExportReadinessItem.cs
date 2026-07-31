namespace RevitGeoSuite.Validation;

public sealed class ExportReadinessItem
{
    public string Title { get; set; } = string.Empty;

    public bool IsSatisfied { get; set; }

    public string Detail { get; set; } = string.Empty;

    public string StatusText => IsSatisfied ? "Ready" : "Needs Attention";
}
