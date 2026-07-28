using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class SplitSurveyProjectBasePointIntent
{
    public CrsReference? SelectedCrs { get; set; }

    public ProjectOrigin? SharedSurveyOrigin { get; set; }

    public ProjectedCoordinate? SharedSurveyProjectedCoordinate { get; set; }

    public WorkingProjectBasePointReference? LocalProjectBasePoint { get; set; }

    public double? TrueNorthAngle { get; set; }

    public GeoConfidenceLevel Confidence { get; set; } = GeoConfidenceLevel.Approximate;

    public string SetupSource { get; set; } = string.Empty;

    public PlacementApplyMode ApplyMode { get; set; } = PlacementApplyMode.ProjectLocation;
}
