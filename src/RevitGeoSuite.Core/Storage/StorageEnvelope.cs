namespace RevitGeoSuite.Core.Storage;

public sealed class StorageEnvelope<TPayload>
{
    public int SchemaVersion { get; set; }

    public string PayloadType { get; set; } = string.Empty;

    public TPayload? Payload { get; set; }
}
