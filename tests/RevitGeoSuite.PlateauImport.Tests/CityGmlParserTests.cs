using System.Linq;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests;

public sealed class CityGmlParserTests
{
    [Fact]
    public void ParseFile_reads_fixture_city_model_and_building_footprints()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Samples", "sample-origin-context.gml");
        CityGmlParser parser = new CityGmlParser();

        PlateauCityModel model = parser.ParseFile(fixturePath);

        Assert.Equal(fixturePath, model.SourcePath);
        Assert.Equal(6677, model.EpsgCode);
        Assert.Equal(2, model.Buildings.Count);
        Assert.Equal(new[] { "Sample Building A", "Sample Building B" }, model.Buildings.Select(building => building.Name).ToArray());
        Assert.All(model.Buildings, building => Assert.True(building.ExteriorRing.Count >= 4));
    }
}
