using System;
using System.Collections.Generic;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class UnitCategoryFilterTests
{
    [Fact]
    public void ShouldInclude_WithNullOrEmptyFilter_IncludesEverything()
    {
        Assert.True(UnitCategoryFilter.ShouldInclude("room", null));
        Assert.True(UnitCategoryFilter.ShouldInclude("room", Array.Empty<string>()));
        Assert.True(UnitCategoryFilter.ShouldInclude(null, null));
        Assert.True(UnitCategoryFilter.ShouldInclude("room", new[] { " " }));
    }

    [Fact]
    public void ShouldInclude_WithFilter_KeepsOnlyMatches()
    {
        List<string> filter = new() { "column" };

        Assert.True(UnitCategoryFilter.ShouldInclude("column", filter));
        Assert.False(UnitCategoryFilter.ShouldInclude("room", filter));
        Assert.False(UnitCategoryFilter.ShouldInclude("stairs", filter));
    }

    [Fact]
    public void ShouldInclude_MatchesCaseAndWhitespaceInsensitively()
    {
        List<string> filter = new() { " Column " };

        Assert.True(UnitCategoryFilter.ShouldInclude("COLUMN", filter));
        Assert.True(UnitCategoryFilter.ShouldInclude(" column", filter));
    }

    [Fact]
    public void ShouldInclude_WithFilter_ExcludesMissingCategory()
    {
        List<string> filter = new() { "column" };

        Assert.False(UnitCategoryFilter.ShouldInclude(null, filter));
        Assert.False(UnitCategoryFilter.ShouldInclude("  ", filter));
    }
}
