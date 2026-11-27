using System;
using Newtonsoft.Json;
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

    /// <summary>
    /// The federation role chosen in New Kanizu Project: <c>standalone</c>, <c>hub</c>, <c>spoke</c>
    /// or <c>independent</c>. Kept in the model so the export can write it into
    /// <c>gis-model.json</c> once a station folder exists. Absent on models that never chose one.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? FederationMode { get; set; }

    /// <summary>The hub's ModelId when <see cref="FederationMode"/> is <c>spoke</c>.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? FederationHubModelId { get; set; }
}
