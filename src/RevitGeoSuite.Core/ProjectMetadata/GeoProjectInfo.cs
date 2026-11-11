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

    /// <summary>
    /// A random id written once into the model, so downstream tools such as Kanizu can
    /// recognise the same model across users, worksharing local copies, Save As and moves.
    /// Unlike a hash of the file path, it never changes. Empty until first needed.
    /// </summary>
    public string? ModelId { get; set; }

    /// <summary>
    /// The station folder on the share this model was last exported into, where its
    /// <c>gis-models/&lt;model id&gt;.gis-model.json</c> settings file lives. Recorded by the Cesium
    /// export into a station folder; empty until then.
    /// </summary>
    public string? GisStationFolder { get; set; }
}
