using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.Georeference;

public sealed class ApplyModeOption
{
    public PlacementApplyMode Mode { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;
}
