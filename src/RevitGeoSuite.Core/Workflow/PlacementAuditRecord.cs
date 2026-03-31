using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;

namespace RevitGeoSuite.Core.Workflow;

public sealed class PlacementAuditRecord
{
    public DateTime AppliedAtUtc { get; set; }

    public string DocumentTitle { get; set; } = string.Empty;

    [JsonConverter(typeof(StringEnumConverter))]
    public PlacementApplyMode ApplyMode { get; set; } = PlacementApplyMode.MetadataOnly;

    [JsonConverter(typeof(StringEnumConverter))]
    public PlacementAnchorTarget AnchorTarget { get; set; } = PlacementAnchorTarget.SurveyPoint;

    public CrsReference? ProjectCrs { get; set; }

    public ProjectOrigin? Origin { get; set; }

    public ProjectedCoordinate? ProjectedCoordinate { get; set; }

    public WorkingProjectBasePointReference? WorkingProjectBasePoint { get; set; }

    public double TrueNorthAngle { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public GeoConfidenceLevel Confidence { get; set; } = GeoConfidenceLevel.Unknown;

    public string SetupSource { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;
}
