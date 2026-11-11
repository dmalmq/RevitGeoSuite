using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class ZoneCatalogTests
{
    [Fact]
    public void KnownZone_ReturnsCategoryColorAndRestriction()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        bool found = catalog.TryGetZoneInfo("Circulation (inside fare gates)", out ZoneInfo info);

        Assert.True(found);
        Assert.Equal("walkway", info.Category);
        Assert.Equal("FEFEF2", info.FillColor);
        Assert.Equal("rachi_nai", info.Restriction);
    }

    [Theory]
    [InlineData("ラチ外コンコース", "walkway", "FFFFFF")]
    [InlineData("ラチ内コンコース", "walkway", "FEFEF2")]
    [InlineData("改札内コンコース", "walkway", "FEFEF2")]
    [InlineData("ラチ内コンコース(JR東新幹線)", "walkway", "EFFAED")]
    [InlineData("ラチ内コンコース(JR東日本新幹線)", "walkway", "EFFAED")]
    [InlineData("改札内コンコース(JR東海新幹線)", "walkway", "FFF4E0")]
    [InlineData("ラチ内店舗", "retail", "E1F3F9")]
    [InlineData("改札内店舗", "retail", "E1F3F9")]
    [InlineData("ラチ外店舗", "retail", "F3FCFF")]
    [InlineData("改札外店舗", "retail", "F3FCFF")]
    [InlineData("新幹線ホーム", "platform", "F9E8E1")]
    [InlineData("在来線ホーム", "platform", "FFF6F3")]
    [InlineData("案内所", "information", "EFEFF9")]
    [InlineData("案内", "information", "EFEFF9")]
    [InlineData("みどりの窓口", "ticketing", "A3CA7F")]
    [InlineData("緑の窓口", "ticketing", "A3CA7F")]
    [InlineData("多目的トイレ", "restroom.unisex", "D9FFD9")]
    [InlineData("待合室", "waitingroom", "BABABA")]
    public void JapaneseZoneNames_ResolveToTheirCategory(string zoneName, string category, string color)
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        bool found = catalog.TryGetZoneInfo(zoneName, out ZoneInfo info);

        Assert.True(found, zoneName);
        Assert.Equal(category, info.Category);
        Assert.Equal(color, info.FillColor);
    }

    [Fact]
    public void UnknownZone_ReturnsDefaultInfo()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        bool found = catalog.TryGetZoneInfo("Unknown Zone", out ZoneInfo info);

        Assert.False(found);
        Assert.Equal("unspecified", info.Category);
        Assert.Equal("CCCCCC", info.FillColor);
        Assert.Null(info.Restriction);
    }

    [Fact]
    public void OtherZone_UsesConfiguredFallbackCategory()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        ZoneInfo info = catalog.GetZoneInfoOrDefault("Other");

        Assert.Equal("nonpublic", info.Category);
    }

    [Fact]
    public void RestroomCategories_AreMapped()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        Assert.Equal("restroom.male", catalog.GetZoneInfoOrDefault("Men's restroom").Category);
        Assert.Equal("restroom.female", catalog.GetZoneInfoOrDefault("Women's restroom").Category);
        Assert.Equal(
            "restroom.unisex",
            catalog.GetZoneInfoOrDefault("Accessible / multipurpose restroom").Category);
    }

    [Fact]
    public void FamilyLookup_ContainsElevatorAndEscalatorMappings()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        bool foundElevator = catalog.TryGetFamilyInfo("j EV", out ZoneInfo elevator);
        bool foundEscalator = catalog.FamilyLookup.Values.Any(info => info.Category == "escalator");

        Assert.True(foundElevator);
        Assert.Equal("elevator", elevator.Category);
        Assert.True(foundEscalator);
    }

    [Fact]
    public void StairsDefault_IsConfigured()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        Assert.Equal("stairs", catalog.StairsDefault.Category);
        Assert.Equal("C0C0C0", catalog.StairsDefault.FillColor);
    }

    [Fact]
    public void CategoryLookup_ResolvesColumn()
    {
        ZoneCatalog catalog = ZoneCatalog.CreateDefault();

        bool found = catalog.TryGetCategoryInfo("column", out ZoneInfo info);

        Assert.True(found);
        Assert.Equal("column", info.Category);
        Assert.Equal("BDBDBD", info.FillColor);
    }

    [Fact]
    public void GetKnownCategories_IncludesImdfAndCatalogCustomCategories()
    {
        ZoneCatalog catalog = new(
            new Dictionary<string, ZoneInfo>
            {
                ["custom zone"] = new ZoneInfo("amenity.custom", "ABCDEF", null),
            });

        IReadOnlyList<string> categories = catalog.GetKnownCategories();

        Assert.Contains("column", categories);
        Assert.Contains("amenity.custom", categories);
        Assert.DoesNotContain("unspecified", categories);
    }
}
