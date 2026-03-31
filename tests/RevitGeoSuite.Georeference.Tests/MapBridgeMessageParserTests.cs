using RevitGeoSuite.SharedUI.Controls;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class MapBridgeMessageParserTests
{
    [Fact]
    public void Parse_reads_message_type_and_coordinates()
    {
        MapBridgeMessage message = MapBridgeMessageParser.Parse("{\"type\":\"mapClick\",\"latitude\":35.681236,\"longitude\":139.767125}");

        Assert.Equal("mapClick", message.Type);
        Assert.Equal(35.681236, message.Latitude);
        Assert.Equal(139.767125, message.Longitude);
    }
}
