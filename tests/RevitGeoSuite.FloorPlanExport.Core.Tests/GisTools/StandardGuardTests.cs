using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class StandardGuardTests
{
    [Theory]
    [InlineData("", true)]
    [InlineData("not-a-category", true)]
    [InlineData("retail", false)]
    [InlineData(" Retail ", false)]
    public void AFloorTypeIsOutsideWithoutAUnitCategory(string category, bool outside)
    {
        Assert.Equal(outside, StandardGuard.FloorTypeOutside(category, Standard));
    }

    [Theory]
    [InlineData("j EV", "A", "", "", null)]
    [InlineData("j EV", "A", "elevator", "", null)]
    [InlineData("Lift", "A", "elevator", "", "elevator")]
    [InlineData("Walkway", "A", "movingwalkway", "", "moving_walkway")]
    [InlineData("Moving Walkway", "Type 1", "", "", "moving_walkway")]
    [InlineData("ESC_1200", "S800", "", "escalator", null)]
    [InlineData("Escalator Other", "S800", "", "", "escalator")]
    [InlineData("Bench", "1800", "", "", null)]
    [InlineData("Lift", "A", "lift", "", null)]
    public void AFamilyIsOutsideWhenItIsAKindOffTheList(string family, string type, string kind, string category, string? outside)
    {
        Assert.Equal(outside, StandardGuard.FamilyOutside(family, type, kind, category, Standard));
    }

    [Fact]
    public void ALookAlikeTheExportDialogMapsByHandIsInside()
    {
        Assert.Equal("moving_walkway", StandardGuard.FamilyOutside("Moving Walkway", "Type 1", "", "", Standard, new[] { "Other" }));
        Assert.Null(StandardGuard.FamilyOutside(" Moving Walkway", "Type 1", "", "", Standard, new[] { "Moving Walkway" }));
    }

    [Fact]
    public void TheWarningNamesTheStandardFamilies()
    {
        Assert.Equal("j EV", StandardGuard.StandardFamilies(Standard, "elevator"));
        Assert.Equal(string.Empty, StandardGuard.StandardFamilies(Standard, "moving_walkway"));
    }
}
