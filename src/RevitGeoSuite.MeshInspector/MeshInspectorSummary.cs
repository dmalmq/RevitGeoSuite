using System.Collections.Generic;

namespace RevitGeoSuite.MeshInspector;

public sealed class MeshInspectorSummary
{
    public string DocumentTitle { get; set; } = string.Empty;

    public string StatusMessage { get; set; } = string.Empty;

    public string PrimaryMeshCode { get; set; } = string.Empty;

    public bool CanSavePrimaryMeshCode { get; set; }

    public string OverlayGeoJson { get; set; } = string.Empty;

    public double? CenterLatitude { get; set; }

    public double? CenterLongitude { get; set; }

    public string ReferenceSourceTitle { get; set; } = string.Empty;

    public string ReferenceSourceDescription { get; set; } = string.Empty;

    public IReadOnlyCollection<DetailRow> DetailRows { get; set; } = new DetailRow[0];

    public IReadOnlyCollection<string> NeighborMeshCodes { get; set; } = new string[0];

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public bool HasOverlay => !string.IsNullOrWhiteSpace(OverlayGeoJson);

    public bool HasPrimaryMeshCode => !string.IsNullOrWhiteSpace(PrimaryMeshCode);

    public bool HasNeighborMeshCodes => NeighborMeshCodes.Count > 0;
}
