using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.Core.Workflow;

public sealed class PlacementCurrentState
{
    public CrsReference? CurrentCrs { get; set; }

    public ProjectOrigin? CurrentOrigin { get; set; }

    public string CurrentOriginSource { get; set; } = string.Empty;

    public double CurrentTrueNorthAngleDegrees { get; set; }

    public GeoConfidenceLevel? CurrentConfidence { get; set; }

    public string CurrentSetupSource { get; set; } = string.Empty;

    public bool HasStoredGeoMetadata { get; set; }

    public bool ExistingSetupDetected { get; set; }

    public WorkingProjectBasePointReference? CurrentWorkingProjectBasePoint { get; set; }
}
