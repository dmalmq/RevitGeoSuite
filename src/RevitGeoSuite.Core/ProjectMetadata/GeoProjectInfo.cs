using System;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Mesh;
using RevitGeoSuite.Core.Versioning;

namespace RevitGeoSuite.Core.ProjectMetadata;

public sealed class GeoProjectInfo
{
    public int SchemaVersion { get; set; } = RevitGeoSuite.Core.Versioning.SchemaVersion.Current;

    public CrsReference? ProjectCrs { get; set; }

    public ProjectOrigin? Origin { get; set; }

    public double TrueNorthAngle { get; set; }

    public MeshCode? PrimaryMeshCode { get; set; }

    public GeoConfidenceLevel Confidence { get; set; } = GeoConfidenceLevel.Unknown;

    public string SetupSource { get; set; } = string.Empty;

    public DateTime? GeoSetupDate { get; set; }
}
