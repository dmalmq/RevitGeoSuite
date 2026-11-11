using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class VerticalCirculationTests
{
    [Theory]
    [InlineData("escalator", "escalator")]
    [InlineData(" Moving_Walkway ", "movingwalkway")]
    [InlineData("elevator", "elevator")]
    [InlineData("ticket_gate", "fixture")]
    public void GisKind_MapsToAUnitCategory(string gisKind, string category)
    {
        Assert.True(VerticalCirculation.TryMapGisKind(gisKind, out string mapped));
        Assert.Equal(category, mapped);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("stairs")]
    public void UnknownGisKind_IsNotMapped(string? gisKind)
    {
        Assert.False(VerticalCirculation.TryMapGisKind(gisKind, out _));
    }

    [Theory]
    [InlineData("up", "up")]
    [InlineData("上り", "up")]
    [InlineData("DOWN", "down")]
    [InlineData("下り", "down")]
    [InlineData("両方向", "both")]
    [InlineData("sideways", null)]
    [InlineData(null, null)]
    public void Direction_IsNormalized(string? value, string? expected)
    {
        Assert.Equal(expected, VerticalCirculation.NormalizeDirection(value));
    }

    [Fact]
    public void ToLevel_IsTheOtherEnd()
    {
        Assert.Equal("L2", VerticalCirculation.ResolveToLevel("L1", "L1", "L2"));
        Assert.Equal("L1", VerticalCirculation.ResolveToLevel("L2", "L1", "L2"));
        Assert.Null(VerticalCirculation.ResolveToLevel("L3", "L1", "L2"));
        Assert.Null(VerticalCirculation.ResolveToLevel("L1", "L1", null));
    }

    [Fact]
    public void EscalatorsAndWalkways_AreMovingCirculation()
    {
        Assert.True(VerticalCirculation.IsMovingCirculation("escalator"));
        Assert.True(VerticalCirculation.IsMovingCirculation("movingwalkway"));
        Assert.False(VerticalCirculation.IsMovingCirculation("elevator"));
        Assert.False(VerticalCirculation.IsMovingCirculation(null));
    }
}
