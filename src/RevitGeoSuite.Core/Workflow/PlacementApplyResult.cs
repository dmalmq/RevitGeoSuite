using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.Core.Workflow;

public sealed class PlacementApplyResult
{
    public GeoProjectInfo SavedGeoProjectInfo { get; set; } = new GeoProjectInfo();

    public PlacementAuditRecord AuditRecord { get; set; } = new PlacementAuditRecord();

    public string AuditSummary { get; set; } = string.Empty;
}
