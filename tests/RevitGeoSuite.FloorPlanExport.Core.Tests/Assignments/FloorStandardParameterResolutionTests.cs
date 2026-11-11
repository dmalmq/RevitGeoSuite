using System.Collections.Generic;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Assignments;

public sealed class FloorStandardParameterResolutionTests
{
    [Fact]
    public void TypeParameters_DecideBeforeTheTypeName()
    {
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault());

        ResolvedFloorCategory resolved = resolver.Resolve(
            "j 何か_床",
            "何か",
            new FloorStandardParameters(null, "waitingroom", null));

        Assert.False(resolved.IsUnassigned);
        Assert.Equal(FloorCategoryResolutionSource.TypeParameter, resolved.ResolutionSource);
        Assert.Equal("waitingroom", resolved.ZoneInfo.Category);
        Assert.Null(resolved.ZoneInfo.Restriction);
    }

    [Fact]
    public void TypeRestriction_IsUsedWhenSet()
    {
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault());

        ResolvedFloorCategory resolved = resolver.Resolve(
            "Custom floor",
            "Custom floor",
            new FloorStandardParameters(null, "walkway", "rachi_nai"));

        Assert.Equal("walkway", resolved.ZoneInfo.Category);
        Assert.Equal("rachi_nai", resolved.ZoneInfo.Restriction);
    }

    [Fact]
    public void EmptyTypeRestriction_KeepsTheRestrictionTheNameGives_WhenTheyAgree()
    {
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault());

        ResolvedFloorCategory resolved = resolver.Resolve(
            "j ラチ外店舗_床",
            "ラチ外店舗",
            new FloorStandardParameters(null, "retail", null));

        Assert.Equal(FloorCategoryResolutionSource.TypeParameter, resolved.ResolutionSource);
        Assert.Equal("retail", resolved.ZoneInfo.Category);
        Assert.Equal("rachi_gai", resolved.ZoneInfo.Restriction);
    }

    [Fact]
    public void InstanceCategory_OverridesTheType()
    {
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault());

        ResolvedFloorCategory resolved = resolver.Resolve(
            "j ラチ内コンコース_床",
            "ラチ内コンコース",
            new FloorStandardParameters("nonpublic", "walkway", "rachi_nai"));

        Assert.Equal(FloorCategoryResolutionSource.InstanceParameter, resolved.ResolutionSource);
        Assert.Equal("nonpublic", resolved.ZoneInfo.Category);
        Assert.Equal("rachi_nai", resolved.ZoneInfo.Restriction);
    }

    [Fact]
    public void InstanceCategoryEchoingTheName_DoesNotHideTheTypeParameter()
    {
        // Older exports wrote the name's category back into every floor.
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault());

        ResolvedFloorCategory resolved = resolver.Resolve(
            "j ラチ内コンコース_床",
            "ラチ内コンコース",
            new FloorStandardParameters("walkway", "nonpublic", null));

        Assert.Equal(FloorCategoryResolutionSource.TypeParameter, resolved.ResolutionSource);
        Assert.Equal("nonpublic", resolved.ZoneInfo.Category);
    }

    [Fact]
    public void InstanceCategoryEchoingAnOverride_DoesNotHideTheTypeParameter()
    {
        FloorCategoryResolver resolver = new(
            ZoneCatalog.CreateDefault(),
            new Dictionary<string, string> { ["Custom floor"] = "retail" });

        ResolvedFloorCategory resolved = resolver.Resolve(
            "Custom floor",
            "Custom floor",
            new FloorStandardParameters("retail", "platform", "rachi_nai"));

        Assert.Equal(FloorCategoryResolutionSource.TypeParameter, resolved.ResolutionSource);
        Assert.Equal("platform", resolved.ZoneInfo.Category);
    }

    [Fact]
    public void InstanceCategoryEchoingTheName_StillCounts_WithoutATypeParameter()
    {
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault());

        ResolvedFloorCategory resolved = resolver.Resolve(
            "j ラチ内コンコース_床",
            "ラチ内コンコース",
            new FloorStandardParameters("walkway", null, null));

        Assert.Equal(FloorCategoryResolutionSource.InstanceParameter, resolved.ResolutionSource);
        Assert.Equal("walkway", resolved.ZoneInfo.Category);
        Assert.Equal("rachi_nai", resolved.ZoneInfo.Restriction);
    }

    [Fact]
    public void TypeParameters_WinOverPerPcOverrides()
    {
        FloorCategoryResolver resolver = new(
            ZoneCatalog.CreateDefault(),
            new Dictionary<string, string> { ["Custom floor"] = "retail" });

        ResolvedFloorCategory resolved = resolver.Resolve(
            "Custom floor",
            "Custom floor",
            new FloorStandardParameters(null, "platform", "rachi_nai"));

        Assert.Equal(FloorCategoryResolutionSource.TypeParameter, resolved.ResolutionSource);
        Assert.Equal("platform", resolved.ZoneInfo.Category);
    }

    [Fact]
    public void UnknownCategory_IsIgnoredWithAWarning_AndTheNameDecides()
    {
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault());
        List<string> warnings = new();

        ResolvedFloorCategory resolved = resolver.Resolve(
            "j ラチ内コンコース_床",
            "ラチ内コンコース",
            new FloorStandardParameters("not-a-category", "unspecified", null),
            warnings);

        Assert.Equal(FloorCategoryResolutionSource.Catalog, resolved.ResolutionSource);
        Assert.Equal("walkway", resolved.ZoneInfo.Category);
        Assert.Equal(2, warnings.Count);
        Assert.Contains("not-a-category", warnings[0]);
    }

    [Fact]
    public void NoParameters_ResolvesByName_AsBefore()
    {
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault());

        ResolvedFloorCategory withEmpty = resolver.Resolve(
            "j ラチ内コンコース_床",
            "ラチ内コンコース",
            new FloorStandardParameters(" ", null, null));
        ResolvedFloorCategory withNull = resolver.Resolve("j ラチ内コンコース_床", "ラチ内コンコース", null);

        Assert.Equal(FloorCategoryResolutionSource.Catalog, withEmpty.ResolutionSource);
        Assert.Equal(withNull.ZoneInfo.Category, withEmpty.ZoneInfo.Category);
        Assert.Equal(withNull.ZoneInfo.Restriction, withEmpty.ZoneInfo.Restriction);
    }

    [Fact]
    public void CategoryValue_IsNormalisedToTheCatalogSpelling()
    {
        FloorCategoryResolver resolver = new(ZoneCatalog.CreateDefault());

        ResolvedFloorCategory resolved = resolver.Resolve(
            "Custom floor",
            "Custom floor",
            new FloorStandardParameters(null, " Restroom.Male ", null));

        Assert.Equal("restroom.male", resolved.ZoneInfo.Category);
    }
}
