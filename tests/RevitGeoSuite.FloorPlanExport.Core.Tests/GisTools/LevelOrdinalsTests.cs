using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class LevelOrdinalsTests
{
    private const double FeetPerMetre = 1 / 0.3048;

    // LUMINE EST's levels, measured from its base point: the model sits at its real height.
    private static LevelOrdinalInput[] Lumine(int? b2 = null, int? b1 = null, int? first = null, int? second = null) => new[]
    {
        new LevelOrdinalInput(1, "2FL", 41.86 * FeetPerMetre, second),
        new LevelOrdinalInput(2, "B2FL", 27.5 * FeetPerMetre, b2),
        new LevelOrdinalInput(3, "1FL", 37.71 * FeetPerMetre, first),
        new LevelOrdinalInput(4, "B1FL", 32.66 * FeetPerMetre, b1),
    };

    private static readonly long[] All = { 1, 2, 3, 4 };

    private static (int[] Ordinals, IReadOnlyList<string> Warnings) Resolve(IEnumerable<LevelOrdinalInput> levels, long[]? exported = null)
    {
        (IReadOnlyDictionary<long, int> ordinals, IReadOnlyList<string> warnings) = LevelOrdinals.Resolve(levels, new HashSet<long>(exported ?? All));
        return (new[] { ordinals[2], ordinals[4], ordinals[3], ordinals[1] }, warnings);
    }

    [Fact]
    public void EachLevelsOwnOrdinalWins()
    {
        var (ordinals, warnings) = Resolve(Lumine(-2, -1, 0, 1));

        Assert.Equal(new[] { -2, -1, 0, 1 }, ordinals);
        Assert.Empty(warnings);
    }

    [Fact]
    public void ALevelWithoutOneCountsFromTheNearestLevelThatHasOne()
    {
        var (ordinals, warnings) = Resolve(Lumine(first: 0));

        Assert.Equal(new[] { -2, -1, 0, 1 }, ordinals);
        Assert.Equal(
            new[]
            {
                "Level B2FL has no ordinal, so it is exported as -2, counted from level 1FL. Set its ordinal to be sure.",
                "Level B1FL has no ordinal, so it is exported as -1, counted from level 1FL. Set its ordinal to be sure.",
                "Level 2FL has no ordinal, so it is exported as 1, counted from level 1FL. Set its ordinal to be sure.",
            },
            warnings);
    }

    [Fact]
    public void WithNoOrdinalAtAllGroundIsTheLevelNearestZeroAndTheExportSaysWhich()
    {
        var (ordinals, warnings) = Resolve(Lumine());

        Assert.Equal(new[] { 0, 1, 2, 3 }, ordinals);
        Assert.Contains("level B2FL, the one nearest elevation 0, is exported as ground (0)", Assert.Single(warnings));
    }

    [Fact]
    public void WarnsOnlyAboutExportedLevelsAndNamesASharedOrdinal()
    {
        var (_, warnings) = Resolve(Lumine(b2: -1, b1: -1, first: 0), exported: new long[] { 2, 4, 3 });

        Assert.Equal(new[] { "Levels B2FL, B1FL are all exported with ordinal -1." }, warnings);
    }
}
