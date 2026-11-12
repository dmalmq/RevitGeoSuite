using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class ImdfRestrictionNormalizerTests
{
    [Theory]
    [InlineData("rachi_gai")]
    [InlineData("rachi_nai")]
    [InlineData("rachigai")]
    [InlineData("rachinai")]
    [InlineData("RACHI_GAI")]
    [InlineData("rachi-nai")]
    public void NormalizeUnitRestriction_RemovesLegacyFareGateValues(string restriction)
    {
        Assert.Null(ImdfRestrictionNormalizer.NormalizeUnitRestriction(restriction));
    }

    [Fact]
    public void NormalizeUnitRestriction_PreservesOtherValues()
    {
        Assert.Equal("employeesonly", ImdfRestrictionNormalizer.NormalizeUnitRestriction(" employeesonly "));
    }

    [Fact]
    public void FloorTypeRestriction_KeepsTheFareGateValueThatRestrictLeavesOut()
    {
        Assert.Equal("rachi_nai", ImdfRestrictionNormalizer.FloorTypeRestriction(" rachi_nai "));
        Assert.Equal("rachi_gai", ImdfRestrictionNormalizer.FloorTypeRestriction("rachi_gai"));
        Assert.Null(ImdfRestrictionNormalizer.FloorTypeRestriction("  "));
        Assert.Null(ImdfRestrictionNormalizer.FloorTypeRestriction(null));
    }

    [Theory]
    [InlineData("RACHI_GAI", "rachi_gai")]
    [InlineData("rachinai", "rachi_nai")]
    [InlineData("rachi-nai", "rachi_nai")]
    [InlineData(" employeesonly ", "employeesonly")]
    public void FloorTypeRestriction_SpellsTheSideOfTheGatesOneWay(string restriction, string expected)
    {
        Assert.Equal(expected, ImdfRestrictionNormalizer.FloorTypeRestriction(restriction));
    }

    [Theory]
    [InlineData("ラチ内店舗", "rachi_nai")]
    [InlineData("改札内コンコース", "rachi_nai")]
    [InlineData("ラチ外店舗", "rachi_gai")]
    public void FloorTypeRestriction_CarriesTheSideAFloorTypeNameGives(string typeName, string side)
    {
        // A legacy type with no gis_restriction gets its side of the gates from its name; the
        // export exports that resolved side, so Kanizu can still pick store_sta or walkway_sta.
        Assert.True(ZoneCatalog.CreateDefault().TryGetZoneInfo(typeName, out ZoneInfo info));

        Assert.Equal(side, ImdfRestrictionNormalizer.FloorTypeRestriction(info.Restriction));
        Assert.Null(ImdfRestrictionNormalizer.NormalizeUnitRestriction(info.Restriction));
    }
}
