using Xunit;

namespace RevitGeoSuite.CityGmlExport.Tests;

public sealed class CodelistMapperTests
{
    [Fact]
    public void Known_override_code_resolves_to_assignment()
    {
        CodelistMapper mapper = new CodelistMapper();

        CityGmlCodeAssignment? assignment = mapper.Resolve(
            CityGmlSemanticType.Building,
            "Walls",
            new System.Collections.Generic.Dictionary<string, string>
            {
                [nameof(CityGmlSemanticType.Building)] = "402"
            });

        Assert.NotNull(assignment);
        Assert.Equal("402", assignment!.Code);
        Assert.Equal("Office Building", assignment.Name);
    }

    [Fact]
    public void Unknown_override_code_is_rejected_safely()
    {
        CodelistMapper mapper = new CodelistMapper();

        CityGmlCodeAssignment? assignment = mapper.Resolve(
            CityGmlSemanticType.Building,
            "Walls",
            new System.Collections.Generic.Dictionary<string, string>
            {
                [nameof(CityGmlSemanticType.Building)] = "9999"
            });

        Assert.Null(assignment);
    }
}
