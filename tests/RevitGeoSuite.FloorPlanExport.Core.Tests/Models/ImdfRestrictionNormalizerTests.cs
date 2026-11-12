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
}
