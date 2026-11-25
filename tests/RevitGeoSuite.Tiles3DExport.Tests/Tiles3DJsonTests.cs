using Newtonsoft.Json;
using Xunit;

namespace RevitGeoSuite.Tiles3DExport.Tests;

/// <summary>
/// The expected strings are what Newtonsoft writes on .NET Framework 4.8, where the conformance
/// fixtures were made. This test runs on both target frameworks and must pass unchanged on each.
/// </summary>
public sealed class Tiles3DJsonTests
{
    [Theory]
    [InlineData(200d / 255d, "0.78431372549019607")]
    [InlineData(1d / 3d, "0.33333333333333331")]
    [InlineData(0.1 + 0.2, "0.30000000000000004")]
    [InlineData(0.1, "0.1")]
    [InlineData(-5995.185, "-5995.185")]
    [InlineData(1d, "1.0")]
    [InlineData(-7.5, "-7.5")]
    [InlineData(0d, "0.0")]
    [InlineData(0.00001, "1E-05")]
    [InlineData(1e15, "1E+15")]
    [InlineData(123456789012345678d, "1.2345678901234568E+17")]
    [InlineData(double.MaxValue, "1.7976931348623157E+308")]
    [InlineData(double.MinValue, "-1.7976931348623157E+308")]
    public void FormatDouble_matches_net_framework_newtonsoft(double value, string expected)
    {
        Assert.Equal(expected, Tiles3DJson.FormatDouble(value));
    }

    [Fact]
    public void Serialize_formats_doubles_nested_in_anonymous_objects_and_arrays()
    {
        object document = new { factor = new[] { 200d / 255d, 1d }, height = -0d, count = 3 };

        Assert.Equal(
            "{\"factor\":[0.78431372549019607,1.0],\"height\":0.0,\"count\":3}",
            Tiles3DJson.Serialize(document, Formatting.None));
    }
}
