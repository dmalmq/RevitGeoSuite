using System.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.SharedUI.Controls;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class CrsFilterTests
{
    [Fact]
    public void Apply_returns_all_definitions_when_query_is_empty()
    {
        CrsRegistry registry = new CrsRegistry();

        var results = CrsFilter.Apply(registry.GetAvailableDefinitions(), string.Empty);

        Assert.Equal(19, results.Count);
    }

    [Fact]
    public void Apply_matches_unique_zone_and_place_name_terms()
    {
        CrsRegistry registry = new CrsRegistry();

        var results = CrsFilter.Apply(registry.GetAvailableDefinitions(), "zone 9 Chiba");

        Assert.Single(results);
        Assert.Equal(6677, results.Single().EpsgCode);
    }
}
