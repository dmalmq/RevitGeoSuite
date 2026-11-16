using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class LevelByHeightTests
{
    // Out of order on purpose: the model's levels come in no set order.
    private static readonly (long Id, double Elevation)[] Levels = { (3, 20.0), (1, 0.0), (2, 10.0) };

    [Theory]
    [InlineData(10.0, 2L)]
    [InlineData(9.995, 2L)]
    [InlineData(9.98, 1L)]
    [InlineData(15.0, 2L)]
    [InlineData(500.0, 3L)]
    [InlineData(0.0, 1L)]
    public void PicksTheHighestLevelAtOrBelow(double height, long expected)
    {
        Assert.Equal(expected, LevelByHeight.At(Levels, height));
    }

    [Fact]
    public void BelowTheLowestLevelIsNoLevel()
    {
        Assert.Null(LevelByHeight.At(Levels, -1.0));
    }

    [Fact]
    public void JustBelowTheLowestLevelWithinToleranceIsOnIt()
    {
        Assert.Equal(1L, LevelByHeight.At(Levels, -0.005));
    }

    [Fact]
    public void OfTwoLevelsAtOneHeightTheFirstListedWins()
    {
        Assert.Equal(7L, LevelByHeight.At(new (long, double)[] { (7, 10.0), (8, 10.0) }, 12.0));
    }

    [Fact]
    public void NoLevelsIsNoLevel()
    {
        Assert.Null(LevelByHeight.At(System.Array.Empty<(long, double)>(), 5.0));
    }

    [Fact]
    public void AFloorSlabStandsOnTheLevelAtItsTop()
    {
        // A 300 mm slab whose top is at level 2: by its bottom it would land on level 1.
        Assert.Equal(2L, LevelByHeight.At(Levels, LevelByHeight.Of(isFloor: true, boxBottom: 9.0, boxTop: 10.0)));
        Assert.Equal(1L, LevelByHeight.At(Levels, LevelByHeight.Of(isFloor: false, boxBottom: 9.0, boxTop: 10.0)));
    }
}
