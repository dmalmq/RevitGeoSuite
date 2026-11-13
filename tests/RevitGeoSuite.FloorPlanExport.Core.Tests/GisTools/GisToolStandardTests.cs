using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class GisToolStandardTests
{
    [Fact]
    public void LoadsTheShippedStandard()
    {
        Assert.Equal(2, Standard.Version);
        Assert.Equal(12, Standard.Parameters.Count);
        Assert.Equal(21, Standard.FloorTypes.Count);
    }

    [Fact]
    public void FloorTypesCarryThemeColours()
    {
        var floorType = Standard.FloorType("j ラチ内コンコース_床")!;

        Assert.Equal("walkway", floorType.GisCategory);
        Assert.Equal("rachi_nai", floorType.GisRestriction);
        Assert.Equal("#F2F7FB", floorType.FillHex);
        Assert.Equal(floorType.FillHex, Standard.Material(floorType.Material)!.FillHex);
    }

    [Fact]
    public void ShopsAreSplitByGateSide()
    {
        Assert.Null(Standard.FloorType("j 店舗_床"));
        Assert.Equal("rachi_gai", Standard.FloorType("j ラチ外店舗_床")!.GisRestriction);
        Assert.Equal("rachi_nai", Standard.FloorType("j ラチ内店舗_床")!.GisRestriction);
    }

    [Fact]
    public void FormerNamesPointAtCurrentStandardNames()
    {
        Assert.All(Standard.FormerNames.SelectMany(former => former.Now), name => Assert.NotNull(Standard.FloorType(name)));
        Assert.All(Standard.FormerNames, former => Assert.Null(Standard.FloorType(former.Name)));
        Assert.Equal(new[] { "j ラチ外店舗_床", "j ラチ内店舗_床" }, Standard.FormerTargets("j 店舗_床"));
        Assert.Equal(new[] { "j 屋外_床" }, Standard.FormerTargets(" roof "));
        Assert.Empty(Standard.FormerTargets("j 屋外_床"));
    }

    [Fact]
    public void KnownCategoriesAreCaseInsensitive()
    {
        Assert.True(Standard.IsKnownCategory(" Walkway "));
        Assert.False(Standard.IsKnownCategory("concourse"));
        Assert.False(Standard.IsKnownCategory(""));
        Assert.False(Standard.IsKnownCategory(null));
    }

    [Fact]
    public void HexRoundTrip()
    {
        Assert.Equal(((byte)0xF2, (byte)0xF7, (byte)0xFB), GisToolStandard.HexToRgb("#F2F7FB"));
        Assert.Equal("#F2F7FB", GisToolStandard.RgbToHex(0xF2, 0xF7, 0xFB));
    }
}
