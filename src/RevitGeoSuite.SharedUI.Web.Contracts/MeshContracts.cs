namespace RevitGeoSuite.SharedUI.Web.Contracts;

/// <summary>Payload of the <c>mesh.getOverlay</c> request.</summary>
[TsExport]
public sealed class MeshOverlayRequest
{
}

/// <summary>Response of the <c>mesh.getOverlay</c> request.</summary>
[TsExport]
public sealed class MeshOverlayResponse
{
    public string? Error { get; set; }

    public string? PrimaryMeshCode { get; set; }

    public string[] NeighborMeshCodes { get; set; } = System.Array.Empty<string>();

    public string? OverlayGeoJson { get; set; }

    public double? CenterLatitude { get; set; }

    public double? CenterLongitude { get; set; }

    public string StatusMessage { get; set; } = string.Empty;
}
