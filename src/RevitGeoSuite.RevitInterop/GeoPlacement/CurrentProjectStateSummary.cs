using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Workflow;

namespace RevitGeoSuite.RevitInterop.GeoPlacement;

public sealed class CurrentProjectStateSummary
{
    public string DocumentTitle { get; set; } = string.Empty;

    public bool IsSupportedDocument { get; set; } = true;

    public bool IsReadOnly { get; set; }

    public string StatusMessage { get; set; } = string.Empty;

    public bool ExistingSetupDetected { get; set; }

    public string ExistingSetupMessage { get; set; } = string.Empty;

    public bool HasStoredGeoInfo { get; set; }

    public CrsReference? StoredCrs { get; set; }

    public ProjectOrigin? StoredOrigin { get; set; }

    public double? StoredTrueNorthAngle { get; set; }

    public GeoConfidenceLevel? StoredConfidence { get; set; }

    public string SetupSource { get; set; } = string.Empty;

    public WorkingProjectBasePointReference? StoredWorkingProjectBasePoint { get; set; }

    public double? SiteLatitudeDegrees { get; set; }

    public double? SiteLongitudeDegrees { get; set; }

    public double? SiteTimeZoneHours { get; set; }

    public ProjectPositionSnapshot ProjectPosition { get; set; } = new ProjectPositionSnapshot();

    public BasePointSnapshot SurveyPoint { get; set; } = new BasePointSnapshot { Name = "Survey Point" };

    public BasePointSnapshot ProjectBasePoint { get; set; } = new BasePointSnapshot { Name = "Project Base Point" };
}
