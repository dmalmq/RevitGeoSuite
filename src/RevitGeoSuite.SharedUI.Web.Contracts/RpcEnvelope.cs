namespace RevitGeoSuite.SharedUI.Web.Contracts;

// Wire/transport types are owned by the TS side (rpc.ts) and intentionally NOT [TsExport], so there
// is a single source of truth for them and they can't drift from the generated DTO definitions.
public sealed class RpcEnvelope
{
    public string Kind { get; set; } = string.Empty;
    public string? Id { get; set; }
    public string Method { get; set; } = string.Empty;
    public object? Payload { get; set; }
    public RpcError? Error { get; set; }
}

public sealed class RpcError
{
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}
