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
}
