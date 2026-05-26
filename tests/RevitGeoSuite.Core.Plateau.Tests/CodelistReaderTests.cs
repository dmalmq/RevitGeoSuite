using RevitGeoSuite.Core.Plateau.Codelists;
using Xunit;

namespace RevitGeoSuite.Core.Plateau.Tests;

public sealed class CodelistReaderTests
{
    [Fact]
    public void Reader_parses_fixture_and_registry_resolves_entries_by_code()
    {
        string fixturePath = TestPathHelper.GetFixturePath("tests", "Fixtures", "Plateau", "Codelists", "sample-building-usage.xml");
        CodelistReader reader = new CodelistReader();

        var entries = reader.ReadFromFile(fixturePath);
        CodelistRegistry registry = new CodelistRegistry(entries);

        Assert.Equal(2, entries.Count);
        Assert.True(registry.TryGetByCode("401", out CodelistEntry? residential));
        Assert.NotNull(residential);
        Assert.Equal("Residential Building", residential!.Name);
    }

    [Fact]
    public void Reader_registers_numeric_gml_name_as_code_alias_when_description_contains_label()
    {
        string xml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gml:Dictionary xmlns:gml=""http://www.opengis.net/gml"">
  <gml:dictionaryEntry>
    <gml:Definition gml:id=""Common_landUseType_6"">
      <gml:description>住宅用地</gml:description>
      <gml:name>211</gml:name>
    </gml:Definition>
  </gml:dictionaryEntry>
</gml:Dictionary>";
        CodelistReader reader = new CodelistReader();

        var entries = reader.Read(xml);
        CodelistRegistry registry = new CodelistRegistry(entries);

        Assert.True(registry.TryGetByCode("211", out CodelistEntry? landUse));
        Assert.NotNull(landUse);
        Assert.Equal("住宅用地", landUse!.Name);
    }
}
