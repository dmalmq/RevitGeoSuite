using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.Georeference;

public sealed class SelectedMapPoint
{
    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public ProjectedCoordinate ProjectedCoordinate { get; set; }

    public string SourceLabel { get; set; } = string.Empty;

    public string ConfidenceLabel { get; set; } = string.Empty;

    public GeoConfidenceLevel ConfidenceLevel { get; set; } = GeoConfidenceLevel.Approximate;

    public PlacementAnchorTarget AnchorTarget { get; set; } = PlacementAnchorTarget.Unspecified;

    public bool IsKnownCoordinateInput { get; set; }
}
