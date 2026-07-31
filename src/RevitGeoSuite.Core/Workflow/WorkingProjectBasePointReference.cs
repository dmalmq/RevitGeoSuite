using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.Core.Workflow;

public sealed class WorkingProjectBasePointReference
{
    public CrsReference? ProjectCrs { get; set; }

    public ProjectOrigin? Origin { get; set; }

    public ProjectedCoordinate? ProjectedCoordinate { get; set; }

    public GeoConfidenceLevel Confidence { get; set; } = GeoConfidenceLevel.Unknown;

    public string SetupSource { get; set; } = string.Empty;

    public bool IsValid => ProjectCrs is not null
        && Origin is not null
        && ProjectedCoordinate.HasValue
        && ProjectedCoordinate.Value.IsFinite;
}
