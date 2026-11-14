using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class SlopeToLevelTests
{
    private const double Feet = 1 / 0.3048;
    private const long B1 = 1;
    private const long F1 = 2;
    private const long F2 = 3;

    // LUMINE EST's levels as Level.ProjectElevation gives them: the geometry's coordinates, with 1F
    // near Z 0. Their Level.Elevation reads 32.66, 37.71 and 41.86 m, which geometry never meets.
    private static readonly Dictionary<long, double> Levels = new() { [B1] = -5.05, [F1] = 0.0, [F2] = 4.15 };

    private static SlopeReach<long> Reach(long own, double lowMetres, double highMetres, Dictionary<long, double>? levels = null) =>
        VerticalCirculation.SlopeToLevel(
            own,
            lowMetres * Feet,
            highMetres * Feet,
            (levels ?? Levels).Select(level => new KeyValuePair<long, double>(level.Key, level.Value * Feet)));

    [Fact]
    public void ASlopeUpFromItsLevelLeadsToTheLevelAbove()
    {
        SlopeReach<long> reach = Reach(F1, 0.0, 4.1);

        Assert.Equal(SlopeOutcome.Reaches, reach.Outcome);
        Assert.Equal(F2, reach.ToLevel);
    }

    [Fact]
    public void ASlopeDownFromItsLevelLeadsToTheLevelBelow()
    {
        Assert.Equal(B1, Reach(F1, -4.7, 0.05).ToLevel);
    }

    [Fact]
    public void ASlopeDrawnOnTheUpperLevelLeadsDown()
    {
        Assert.Equal(F1, Reach(F2, 0.2, 4.15).ToLevel);
    }

    [Fact]
    public void ASmallRampInsideOneLevelIsFlat()
    {
        SlopeReach<long> reach = Reach(F1, 0.0, 0.2);

        Assert.Equal(SlopeOutcome.Flat, reach.Outcome);
        Assert.Null(reach.ToLevel);
    }

    [Fact]
    public void AHalfLevelRampGetsNoLevelAndNamesTheNearestWithItsDistance()
    {
        SlopeReach<long> reach = Reach(F1, 0.0, 2.95);

        Assert.Equal(SlopeOutcome.FarEndBetweenLevels, reach.Outcome);
        Assert.Null(reach.ToLevel);
        Assert.Equal(2.95, reach.FarEndFeet / Feet, 6);
        Assert.Equal(F2, reach.NearestLevel);
        Assert.Equal(1.2, reach.NearestDistanceFeet / Feet, 6);
    }

    [Theory]
    [InlineData(0.4, 4.15)]
    [InlineData(-1.0, 2.0)]
    public void ASlopeWithNeitherEndOnItsLevelGetsNoLevel(double low, double high)
    {
        SlopeReach<long> reach = Reach(F1, low, high);

        Assert.Equal(SlopeOutcome.OwnLevelAtNeitherEnd, reach.Outcome);
        Assert.Null(reach.ToLevel);
    }

    [Fact]
    public void TheOwnLevelIsNeverTheAnswerEvenWithASecondLevelAtItsHeight()
    {
        // A second level 0.2 m above 1F sits within the far-end tolerance of a 0.45 m ramp.
        Dictionary<long, double> levels = new() { [F1] = 0.0, [9] = 0.2, [F2] = 4.15 };

        SlopeReach<long> reach = Reach(F1, 0.0, 0.45, levels);

        Assert.Equal(SlopeOutcome.FarEndBetweenLevels, reach.Outcome);
        Assert.Null(reach.ToLevel);
    }

    [Fact]
    public void LevelElevationsFromTheSurveyPointMatchNothing()
    {
        // The same slope compared with Level.Elevation: no level is near geometry at Z 0 to 4.1 m,
        // so the slope would lose its to_level. The extractor passes ProjectElevation, and
        // LevelElevationUsageTests keeps it that way.
        Dictionary<long, double> elevations = new() { [B1] = 32.66, [F1] = 37.71, [F2] = 41.86 };

        Assert.Equal(SlopeOutcome.OwnLevelAtNeitherEnd, Reach(F1, 0.0, 4.1, elevations).Outcome);
    }

    [Fact]
    public void AFloorWhoseLevelIsUnknownIsFlat()
    {
        Assert.Equal(SlopeOutcome.Flat, Reach(42, 0.0, 4.1).Outcome);
    }
}
