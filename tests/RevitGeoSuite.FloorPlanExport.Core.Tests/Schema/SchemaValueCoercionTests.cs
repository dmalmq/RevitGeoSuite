using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Schema;

public sealed class SchemaValueCoercionTests
{
    [Fact]
    public void TryCoerce_ParsesIntegerStrings()
    {
        bool success = SchemaValueCoercion.TryCoerce("42", ExportAttributeType.Integer, out object? value, out string? failureReason);

        Assert.True(success);
        Assert.Null(failureReason);
        Assert.Equal(42L, value);
    }

    [Fact]
    public void TryCoerce_ParsesBooleanAliases()
    {
        bool success = SchemaValueCoercion.TryCoerce("yes", ExportAttributeType.Boolean, out object? value, out string? failureReason);

        Assert.True(success);
        Assert.Null(failureReason);
        Assert.Equal(true, value);
    }

    [Fact]
    public void TryCoerce_ReturnsFailureForInvalidReal()
    {
        bool success = SchemaValueCoercion.TryCoerce("not-a-number", ExportAttributeType.Real, out object? value, out string? failureReason);

        Assert.False(success);
        Assert.Null(value);
        Assert.False(string.IsNullOrWhiteSpace(failureReason));
    }
}
