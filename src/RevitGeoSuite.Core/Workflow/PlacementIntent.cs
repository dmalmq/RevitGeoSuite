using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.Core.Workflow;

public sealed class PlacementIntent
{
    public CrsReference? SelectedCrs { get; set; }

    public ProjectOrigin? SelectedOrigin { get; set; }

    public ProjectedCoordinate? SelectedProjectedCoordinate { get; set; }

    public double? TrueNorthAngle { get; set; }

    public GeoConfidenceLevel Confidence { get; set; } = GeoConfidenceLevel.Approximate;

    public string SetupSource { get; set; } = string.Empty;

    public PlacementApplyMode ApplyMode { get; set; } = PlacementApplyMode.MetadataOnly;

    public PlacementAnchorTarget AnchorTarget { get; set; } = PlacementAnchorTarget.Unspecified;

    public WorkingProjectBasePointReference? WorkingProjectBasePoint { get; set; }
}
