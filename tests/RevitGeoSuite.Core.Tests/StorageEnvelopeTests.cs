using RevitGeoSuite.Core.Storage;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class StorageEnvelopeTests
{
    [Fact]
    public void Envelope_can_hold_versioned_payload_metadata()
    {
        StorageEnvelope<string> envelope = new StorageEnvelope<string>
        {
            SchemaVersion = 1,
            PayloadType = "GeoProjectInfo",
            Payload = "fixture"
        };

        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal("GeoProjectInfo", envelope.PayloadType);
        Assert.Equal("fixture", envelope.Payload);
    }
}
