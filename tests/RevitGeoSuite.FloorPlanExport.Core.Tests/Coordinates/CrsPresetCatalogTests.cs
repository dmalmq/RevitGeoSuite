using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Coordinates;

public sealed class CrsPresetCatalogTests
{
    [Fact]
    public void GetAllGroups_contains_only_the_19_official_japan_zones()
    {
        IReadOnlyList<CrsPresetGroup> groups = CrsPresetCatalog.GetAllGroups();

        CrsPresetGroup japan = Assert.Single(groups);
        Assert.Equal("Japan", japan.Region);
        Assert.Equal(Enumerable.Range(6669, 19), japan.Entries.Select(entry => entry.Epsg));
        Assert.All(japan.Entries, entry => Assert.Equal("JGD2024", entry.SearchAliases));
    }

    [Theory]
    [InlineData(25832)]
    [InlineData(27700)]
    [InlineData(2154)]
    [InlineData(26917)]
    [InlineData(7855)]
    [InlineData(2193)]
    public void Removed_international_presets_are_not_in_catalog(int epsg)
    {
        Assert.Null(CrsPresetCatalog.TryGetDisplayName(epsg));
    }

    [Fact]
    public void Search_supports_jgd2024_alias_zone_and_epsg_queries()
    {
        Assert.Equal(19, CrsPresetCatalog.Search("JGD2024").Count);
        Assert.Contains(CrsPresetCatalog.Search("JGD2024 zone 9"), entry => entry.Epsg == 6677);
        Assert.Contains(CrsPresetCatalog.Search("JGD2024 IX"), entry => entry.Epsg == 6677);
        Assert.Contains(CrsPresetCatalog.Search("EPSG:6677"), entry => entry.Epsg == 6677);
    }

    [Fact]
    public void Legacy_jgd2024_codes_10267_to_10285_migrate_to_6669_to_6687()
    {
        for (int offset = 0; offset < 19; offset++)
        {
            int legacyCode = 10267 + offset;
            int officialCode = 6669 + offset;

            Assert.Equal(officialCode, JapanPlaneRectangular.NormalizeEpsg(legacyCode));
            Assert.True(JapanPlaneRectangular.TryGetZone(legacyCode, out JapanPlaneRectangularZoneDefinition? zone));
            Assert.NotNull(zone);
            Assert.Equal(officialCode, zone!.Epsg);

            Assert.True(JapanPlaneRectangular.TryResolveEpsg($"EPSG:{legacyCode}", out int resolvedEpsg));
            Assert.Equal(officialCode, resolvedEpsg);

            Assert.Equal(CrsPresetCatalog.TryGetDisplayName(officialCode), CrsPresetCatalog.TryGetDisplayName(legacyCode));
            Assert.Equal(CoordinateSystemCatalog.DescribeEpsg(officialCode), CoordinateSystemCatalog.DescribeEpsg(legacyCode));
            Assert.True(CoordinateSystemCatalog.TryGetDefinitionWkt(legacyCode, out string wkt));
            Assert.Contains($"AUTHORITY[\"EPSG\",\"{officialCode}\"]", wkt);
            Assert.True(CoordinateSystemCatalog.TryCreateFromEpsg(legacyCode, out var coordinateSystem));
            Assert.NotNull(coordinateSystem);
        }
    }

    [Theory]
    [InlineData("JGD2024 / Japan Plane Rectangular CS IX", 6677)]
    [InlineData("JGD2024-09", 6677)]
    [InlineData("JGD 2024 Zone 1", 6669)]
    [InlineData("JGD2024 Zone 10", 6678)]
    [InlineData("JGD2024 / Japan Plane Rectangular CS 12", 6680)]
    [InlineData("JGD2011-19", 6687)]
    [InlineData("JGD2011 / Japan Plane Rectangular CS XIX", 6687)]
    [InlineData("JGD2011 / Japan Plane Rectangular CS IX (Rev 2)", 6677)]
    [InlineData("JGD2011 / Japan Plane Rectangular CS IX 2D", 6677)]
    [InlineData("JGD2011 / Japan Plane Rectangular CS XII v3", 6680)]
    [InlineData("EPSG unknown, JGD2024 Zone 9", 6677)]
    [InlineData("JGD2011 平面直角座標系CS IX", 6677)]
    [InlineData("JGD2011 Zone 9: Tokyo", 6677)]
    [InlineData("JGD2011 CS IX-Tokyo", 6677)]
    [InlineData("JGD2011_CS9", 6677)]
    public void JapanPlaneRectangular_TryResolveEpsg_recognizes_jgd2024_and_jgd2011_names(string text, int expectedEpsg)
    {
        Assert.True(JapanPlaneRectangular.TryResolveEpsg(text, out int epsg));
        Assert.Equal(expectedEpsg, epsg);
    }

    [Fact]
    public void JapanPlaneRectangular_TryResolveEpsg_resolves_all_19_zones_for_jgd2011_and_jgd2024()
    {
        foreach (JapanPlaneRectangularZoneDefinition zone in JapanPlaneRectangular.AllZones)
        {
            foreach (string datum in new[] { "JGD2011", "JGD2024" })
            {
                string[] labels =
                {
                    $"{datum}-{zone.ZoneNumber:00}",
                    $"{datum} Zone {zone.ZoneNumber}",
                    $"{datum} / Japan Plane Rectangular CS {zone.ZoneNumber}",
                    $"{datum} / Japan Plane Rectangular CS {zone.RomanZone}",
                };

                foreach (string label in labels)
                {
                    Assert.True(JapanPlaneRectangular.TryResolveEpsg(label, out int epsg), $"Failed to resolve '{label}'.");
                    Assert.Equal(zone.Epsg, epsg);
                }
            }
        }

        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2024 Zone 0", out _));
        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2024 Zone 20", out _));
        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2024 Zone 100", out _));
        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2011 geographic 2D", out _));
        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2024 1.5", out _));
        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2011 graphics V", out _));
        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2011 ozone 5", out _));
        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2011 2-D", out _));
        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2011 3-D", out _));
        Assert.False(JapanPlaneRectangular.TryResolveEpsg("JGD2024 1:500", out _));
    }

    [Fact]
    public void CoordinateSystemCatalog_retains_wgs84_and_web_mercator_for_basemap_reprojection()
    {
        Assert.True(CoordinateSystemCatalog.TryCreateFromEpsg(4326, out var wgs84));
        Assert.NotNull(wgs84);
        Assert.True(CoordinateSystemCatalog.TryCreateWebMercator(out var webMercator));
        Assert.NotNull(webMercator);
        Assert.Contains("4326", CoordinateSystemCatalog.DescribeEpsg(4326));
        Assert.Contains("3857", CoordinateSystemCatalog.DescribeEpsg(3857));
    }
}
