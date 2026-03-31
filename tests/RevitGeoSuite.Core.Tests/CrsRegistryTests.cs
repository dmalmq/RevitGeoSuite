using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using RevitGeoSuite.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.Core.Tests;

public sealed class CrsRegistryTests
{
    [Fact]
    public void Registry_contains_all_japanese_presets_from_fixture()
    {
        CrsRegistry registry = new CrsRegistry();
        List<CrsFixture> fixtures = LoadFixtures();

        Assert.Equal(19, registry.GetAvailableDefinitions().Count);

        foreach (CrsFixture fixture in fixtures)
        {
            Assert.True(registry.TryGetByEpsgCode(fixture.EpsgCode, out CrsDefinition? definition));
            Assert.NotNull(definition);
            Assert.Equal(fixture.NameSnapshot, definition!.Name);
            Assert.Equal(fixture.ZoneNumber, definition.JapanZoneNumber);
            Assert.Equal(fixture.ZoneLabel, definition.ZoneLabel);
        }
    }

    [Fact]
    public void Search_matches_epsg_zone_and_area_terms()
    {
        CrsRegistry registry = new CrsRegistry();

        Assert.Contains(registry.Search("EPSG:6677"), definition => definition.EpsgCode == 6677);
        Assert.Contains(registry.Search("zone 9"), definition => definition.EpsgCode == 6677);
        Assert.Contains(registry.Search("Tokyo"), definition => definition.EpsgCode == 6677);
    }

    [Fact]
    public void Invalid_epsg_code_is_rejected_safely()
    {
        CrsRegistry registry = new CrsRegistry();

        Assert.False(registry.TryGetByEpsgCode(999999, out CrsDefinition? definition));
        Assert.Null(definition);
    }

    private static List<CrsFixture> LoadFixtures()
    {
        string path = TestPaths.GetRepoPath("tests/Fixtures/Crs/japan-presets.json");
        return JsonConvert.DeserializeObject<List<CrsFixture>>(File.ReadAllText(path))!;
    }

    private sealed class CrsFixture
    {
        public int EpsgCode { get; set; }

        public int ZoneNumber { get; set; }

        public string ZoneLabel { get; set; } = string.Empty;

        public string NameSnapshot { get; set; } = string.Empty;
    }
}
