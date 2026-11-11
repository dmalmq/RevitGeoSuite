using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class DetailCategoryTests
{
    [Fact]
    public void StepLines_UseTheReferenceGdbValue()
    {
        Assert.Equal("steps", DetailCategory.Steps);
    }

    [Theory]
    [InlineData("GIS_Wall", "GIS_Wall")]
    [InlineData("  <Thin Lines> ", "<Thin Lines>")]
    [InlineData("", "line")]
    [InlineData(null, "line")]
    public void ModelLines_CarryTheirLineStyle(string? lineStyle, string expected)
    {
        Assert.Equal(expected, DetailCategory.ForLineStyle(lineStyle));
    }
}
