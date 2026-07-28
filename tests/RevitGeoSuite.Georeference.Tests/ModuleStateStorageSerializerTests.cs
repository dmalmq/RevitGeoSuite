using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.RevitInterop.Storage;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class ModuleStateStorageSerializerTests
{
    [Fact]
    public void TryDeserializeState_returns_state_for_supported_payload()
    {
        StorageEnvelope<string> entry = new StorageEnvelope<string>
        {
            SchemaVersion = ModuleStateStorage.CurrentSchemaVersion,
            PayloadType = typeof(TestState).FullName!,
            Payload = "{\"Name\":\"sample\"}"
        };

        bool result = ModuleStateStorageSerializer.TryDeserializeState(entry, out TestState? state, out string? reason);

        Assert.True(result);
        Assert.NotNull(state);
        Assert.Equal("sample", state!.Name);
        Assert.Null(reason);
    }

    [Fact]
    public void TryDeserializeState_rejects_newer_schema_versions()
    {
        StorageEnvelope<string> entry = new StorageEnvelope<string>
        {
            SchemaVersion = ModuleStateStorage.CurrentSchemaVersion + 1,
            PayloadType = typeof(TestState).FullName!,
            Payload = "{\"Name\":\"sample\"}"
        };

        bool result = ModuleStateStorageSerializer.TryDeserializeState(entry, out TestState? state, out string? reason);

        Assert.False(result);
        Assert.Null(state);
        Assert.Contains("schema version", reason);
    }

    [Fact]
    public void TryDeserializeState_rejects_mismatched_payload_types()
    {
        StorageEnvelope<string> entry = new StorageEnvelope<string>
        {
            SchemaVersion = ModuleStateStorage.CurrentSchemaVersion,
            PayloadType = "Another.Namespace.OtherState",
            Payload = "{\"Name\":\"sample\"}"
        };

        bool result = ModuleStateStorageSerializer.TryDeserializeState(entry, out TestState? state, out string? reason);

        Assert.False(result);
        Assert.Null(state);
        Assert.Contains("payload type", reason);
    }

    [Fact]
    public void TryDeserializeState_rejects_corrupt_payload_json()
    {
        StorageEnvelope<string> entry = new StorageEnvelope<string>
        {
            SchemaVersion = ModuleStateStorage.CurrentSchemaVersion,
            PayloadType = typeof(TestState).FullName!,
            Payload = "{ not valid json }"
        };

        bool result = ModuleStateStorageSerializer.TryDeserializeState(entry, out TestState? state, out string? reason);

        Assert.False(result);
        Assert.Null(state);
        Assert.Contains("could not be deserialized", reason);
    }

    [Fact]
    public void DeserializeEntries_returns_empty_dictionary_for_corrupt_outer_payload()
    {
        var entries = ModuleStateStorageSerializer.DeserializeEntries("{ not valid json }");

        Assert.Empty(entries);
    }

    private sealed class TestState
    {
        public string Name { get; set; } = string.Empty;
    }
}
