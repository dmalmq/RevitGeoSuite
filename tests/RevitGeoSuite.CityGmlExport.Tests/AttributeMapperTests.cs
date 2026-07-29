using System.Linq;
using Xunit;

namespace RevitGeoSuite.CityGmlExport.Tests;

public sealed class AttributeMapperTests
{
    [Fact]
    public void Basic_attribute_builder_includes_expected_revit_fields()
    {
        AttributeMapper mapper = new AttributeMapper();

        var attributes = mapper.BuildBasicAttributes("42", "Walls", "Basic Wall", "Generic - 200mm").ToArray();

        Assert.Contains(attributes, attribute => attribute.Name == "revitElementId" && attribute.Value == "42");
        Assert.Contains(attributes, attribute => attribute.Name == "revitCategory" && attribute.Value == "Walls");
        Assert.Contains(attributes, attribute => attribute.Name == "revitName" && attribute.Value == "Basic Wall");
        Assert.Contains(attributes, attribute => attribute.Name == "revitTypeName" && attribute.Value == "Generic - 200mm");
    }
}
