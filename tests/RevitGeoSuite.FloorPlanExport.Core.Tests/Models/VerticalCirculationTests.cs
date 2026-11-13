using System.Collections.Generic;
using System.Linq;
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

    private static ElevatorCar Car(long id, double x, double y, double bottom, double top, double width = 1.6, double depth = 1.5)
    {
        return new ElevatorCar(id, x, y, x + width, y + depth, bottom, top);
    }

    private static IReadOnlyList<string> Served(IEnumerable<ElevatorCar> shaft, string? levelsServed = null, List<string>? unknown = null)
    {
        return VerticalCirculation.ResolveLevelsServed(Lumine, shaft, levelsServed, unknown ?? new List<string>());
    }

    [Fact]
    public void LevelsServed_AreTheLevelsOneTallCarSpans_LowestFirst()
    {
        // A shaft from a pit under B1 to an overrun 3 m above 2F: B1, 1F, the ceiling level and 2F.
        Assert.Equal(new[] { "lv-b1", "lv-1", "lv-1m", "lv-2" }, Served(new[] { Car(1, 0, 0, -6.2, 9.0) }));
    }

    [Fact]
    public void LevelsServed_IsEmpty_ForALoneCarOnOneLevel()
    {
        Assert.Empty(Served(new[] { Car(1, 0, 0, 0.0, 2.6) }));
        Assert.Empty(Served(new[] { Car(1, 0, 0, 0.0, 1.2) }));
    }

    [Fact]
    public void LevelsServed_AreTheLevelsOfTheCarsDrawnOnePerFloor()
    {
        // LUMINE draws one object per floor; a short one (a plan symbol 1.2 m high) still counts.
        ElevatorCar[] shaft = { Car(3, 0.1, 0, 12.0, 14.6), Car(1, 0, 0, -5.0, -2.4), Car(2, 0, 0.2, 6.0, 7.2) };

        Assert.Equal(new[] { "lv-b1", "lv-2", "lv-3" }, Served(shaft));
    }

    [Fact]
    public void LevelsServed_FromTheTypeParameter_ByNameOrShortName()
    {
        List<string> unknown = new();

        Assert.Equal(
            new[] { "lv-b1", "lv-2", "lv-3" },
            Served(new[] { Car(1, 0, 0, 0.0, 2.6) }, "3F, b1fl、2F, 9F", unknown));
        Assert.Equal(new[] { "9F" }, unknown);
    }

    [Fact]
    public void Shafts_JoinObjectsInTheSamePlanSpot_AndKeepABankApart()
    {
        // A bank of two cars 1.8 m apart, each drawn on B1, 1F and 2F with up to 0.3 m of drawing offset,
        // and a lone car elsewhere.
        ElevatorCar[] cars =
        {
            Car(1, 0.0, 0, -5.0, -2.4), Car(2, 0.3, 0, 0.0, 2.6), Car(3, 0.1, 0.2, 6.0, 8.6),
            Car(4, 1.8, 0, -5.0, -2.4), Car(5, 1.9, 0, 0.0, 2.6), Car(6, 1.8, 0.1, 6.0, 8.6),
            Car(7, 20.0, 5.0, 0.0, 2.6),
        };

        IReadOnlyList<IReadOnlyList<ElevatorCar>> shafts = VerticalCirculation.GroupShafts(cars);

        Assert.Equal(
            new[] { "1,2,3", "4,5,6", "7" },
            shafts.Select(shaft => string.Join(",", shaft.Select(car => car.Id).OrderBy(id => id))).OrderBy(ids => ids));
    }

    [Fact]
    public void Shafts_NeedHalfTheSmallerFootprintToOverlap()
    {
        Assert.True(Car(1, 0, 0, 0, 2.6).SharesShaftWith(Car(2, 0.7, 0, 6, 8.6)));
        Assert.False(Car(1, 0, 0, 0, 2.6).SharesShaftWith(Car(2, 0.9, 0, 6, 8.6)));
    }
}
