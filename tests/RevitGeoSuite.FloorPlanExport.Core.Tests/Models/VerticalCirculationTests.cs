using System.Collections.Generic;
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

    private static readonly ElevatorStop[] Lumine =
    {
        new("lv-b1", "B1FL", "B1", -5.0),
        new("lv-1", "1FL", "1F", 0.0),
        new("lv-1m", "1FL天井", null, 4.0),
        new("lv-2", "2FL", "2F", 6.0),
        new("lv-3", "3FL", "3F", 12.0),
    };

    [Fact]
    public void LevelsServed_AreTheLevelsTheCarSpans_LowestFirst()
    {
        List<string> unknown = new();

        // A shaft from a pit under B1 to an overrun 3 m above 2F: B1, 1F, the ceiling level and 2F.
        Assert.Equal(
            new[] { "lv-b1", "lv-1", "lv-1m", "lv-2" },
            VerticalCirculation.ResolveLevelsServed(Lumine, -6.2, 9.0, null, unknown));
        Assert.Empty(unknown);
    }

    [Fact]
    public void LevelsServed_IsEmpty_ForACarOnOneLevel()
    {
        Assert.Empty(VerticalCirculation.ResolveLevelsServed(Lumine, 0.0, 2.6, null, new List<string>()));
    }

    [Fact]
    public void LevelsServed_FromTheTypeParameter_ByNameOrShortName()
    {
        List<string> unknown = new();

        Assert.Equal(
            new[] { "lv-b1", "lv-2", "lv-3" },
            VerticalCirculation.ResolveLevelsServed(Lumine, 0.0, 2.6, "3F, b1fl、2F, 9F", unknown));
        Assert.Equal(new[] { "9F" }, unknown);
    }
}
