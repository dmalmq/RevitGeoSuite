using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using Xunit;
using static RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools.GisToolsFixtures;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class GisToolStandardTests
{
    [Fact]
    public void LoadsTheShippedStandard()
    {
        Assert.Equal(1, Standard.Version);
        Assert.Equal(12, Standard.Parameters.Count);
        Assert.Equal(12, Standard.FloorTypes.Count);
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
    public void ShopRestrictionIsByLocation()
    {
        Assert.True(string.IsNullOrEmpty(Standard.FloorType("j 店舗_床")!.GisRestriction));
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
