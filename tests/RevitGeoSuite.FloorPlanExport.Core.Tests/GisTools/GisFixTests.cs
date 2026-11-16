using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class GisFixTests
{
    private static readonly GisFix Level = GisFix.Write("level:2", new[]
    {
        new GisFixWrite(2, "B1FL", "ordinal", "", "-1", editable: true, wholeNumber: true),
        new GisFixWrite(2, "B1FL", "short_name", "", "B1FL", editable: true),
        new GisFixWrite(2, "B1FL", "level_id", "l1", ""),
    });

    // The writes as the page shows them, with the user's values for the editable ones.
    private static List<GisFixWrite?> Shown(GisFix fix, params (string Parameter, string Value)[] edits) =>
        fix.Writes.Select(write => edits.Any(edit => edit.Parameter == write.Parameter) ? write.With(edits.First(edit => edit.Parameter == write.Parameter).Value) : write).ToList<GisFixWrite?>();

    [Fact]
    public void EditsReplaceOnlyEditableValuesAndAreTrimmed()
    {
        (IReadOnlyList<GisFixWrite> writes, string? refused) = Level.Accept(Shown(Level, ("short_name", " B1 ")), null);

        Assert.Null(refused);
        Assert.Equal(new[] { ("ordinal", "-1"), ("short_name", "B1"), ("level_id", "") }, writes.Select(write => (write.Parameter, write.NewValue)));
    }

    [Fact]
    public void AChangedFixedValueIsRefusedAsChanged()
    {
        Assert.Equal(GisFix.Changed, Level.Accept(Shown(Level, ("level_id", "mine")), null).Refused);
    }

    [Fact]
    public void AnOrdinalThatIsNotAWholeNumberIsRefused()
    {
        (IReadOnlyList<GisFixWrite> writes, string? refused) = Level.Accept(Shown(Level, ("ordinal", "B1")), null);

        Assert.Empty(writes);
        Assert.Equal("B1FL: ordinal takes a whole number, not 'B1'.", refused);
    }

    [Fact]
    public void AValueLeftAsItIsDropsOutAndNothingLeftIsRefused()
    {
        GisFix fix = GisFix.Write("level:2", new[] { new GisFixWrite(2, "B1FL", "short_name", "B1", "B1FL", editable: true) });

        (IReadOnlyList<GisFixWrite> writes, string? refused) = fix.Accept(Shown(fix, ("short_name", "B1")), null);

        Assert.Empty(writes);
        Assert.Equal("Nothing to change: every value is already what the fix would write.", refused);
    }

    [Fact]
    public void AnEditNeverEmptiesAValueThatIsSet()
    {
        GisFix fix = GisFix.Write("level:2", new[] { new GisFixWrite(2, "B1FL", "short_name", "B1", "B1FL", editable: true) });

        Assert.Equal("B1FL: short_name is 'B1'; the fix does not empty it. Type a value.", fix.Accept(Shown(fix, ("short_name", " ")), null).Refused);
    }

    [Fact]
    public void WritesShownBeforeTheModelChangedAreRefused()
    {
        // The page showed the level without short_name; since then someone typed one in Revit.
        List<GisFixWrite?> shown = Shown(Level);
        GisFix fresh = GisFix.Write("level:2", new[]
        {
            new GisFixWrite(2, "B1FL", "ordinal", "", "-1", editable: true, wholeNumber: true),
            new GisFixWrite(2, "B1FL", "short_name", "B", "B1FL", editable: true),
            new GisFixWrite(2, "B1FL", "level_id", "l1", ""),
        });
        GisFix fewer = GisFix.Write("level:2", Level.Writes.Take(2));
        GisFix other = GisFix.Write("level:2", Level.Writes.Select(write => new GisFixWrite(3, write.ElementName, write.Parameter, write.OldValue, write.NewValue, write.Editable, write.WholeNumber)));

        Assert.Equal(GisFix.Changed, fresh.Accept(shown, null).Refused);
        Assert.Equal(GisFix.Changed, fewer.Accept(shown, null).Refused);
        Assert.Equal(GisFix.Changed, other.Accept(shown, null).Refused);
        Assert.Null(Level.Accept(shown, null).Refused);
    }

    [Fact]
    public void NullsFromThePageAreRefusedOrEmptyNeverThrown()
    {
        // GisFixWrite turns null text into empty, as the handler builds it from the request.
        GisFixWrite nullValue = new(2, null!, "short_name", null!, null!);
        GisFix fix = GisFix.Write("level:2", new[] { new GisFixWrite(2, "B1FL", "short_name", "", "B1FL", editable: true) });

        Assert.Equal(GisFix.Changed, Level.Accept(new GisFixWrite?[] { null, null, null }, null).Refused);
        Assert.Equal("Nothing to change: every value is already what the fix would write.", fix.Accept(new GisFixWrite?[] { nullValue }, new string?[] { }).Refused);
        Assert.Equal(GisFix.Changed, fix.Accept(Shown(fix), new string?[] { null }).Refused);
    }

    [Fact]
    public void NotesShownMustBeTheFixsNotes()
    {
        GisFix plan = GisFix.AddPlan(12, new[] { "Create plan GIS_2F for level 2F" });

        Assert.True(plan.ShowsSameNotes(new[] { "Create plan GIS_2F for level 2F" }));
        Assert.False(plan.ShowsSameNotes(new[] { "Create plan GIS_2F (2) for level 2F" }));
        Assert.Equal(GisFix.Changed, GisFix.Write("levelid:A", Level.Writes, new[] { "1F keeps level_id A." }).Accept(Shown(Level), new[] { "2F keeps level_id A." }).Refused);
    }
}
