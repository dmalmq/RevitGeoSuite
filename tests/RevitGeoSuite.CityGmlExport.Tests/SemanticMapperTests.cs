using System.Collections.Generic;
using Xunit;

namespace RevitGeoSuite.CityGmlExport.Tests;

public sealed class SemanticMapperTests
{
    [Theory]
    [InlineData("Roads", CityGmlSemanticType.Road)]
    [InlineData("Planting", CityGmlSemanticType.Vegetation)]
    [InlineData("Topography", CityGmlSemanticType.Relief)]
    [InlineData("Walls", CityGmlSemanticType.Building)]
    public void Category_names_map_to_expected_semantic_types(string categoryName, CityGmlSemanticType expected)
    {
        SemanticMapper mapper = new SemanticMapper();

        CityGmlSemanticType result = mapper.MapCategoryName(categoryName);

        Assert.Equal(expected, result);
    }

    [Fact]
    public void Explicit_override_wins_over_inferred_mapping()
    {
        SemanticMapper mapper = new SemanticMapper();
        Dictionary<string, string> overrides = new Dictionary<string, string>
        {
            ["Walls"] = nameof(CityGmlSemanticType.Road)
        };

        CityGmlSemanticType result = mapper.MapCategoryName("Walls", overrides);

        Assert.Equal(CityGmlSemanticType.Road, result);
    }
}
