using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.Georeference;

public sealed class AnchorTargetOption
{
    public PlacementAnchorTarget Target { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
