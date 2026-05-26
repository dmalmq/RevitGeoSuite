using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.Core.Plateau.Catalog;
using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class AreaSearchFilterTests
{
    [Fact]
    public void Empty_query_returns_no_results()
    {
        var all = BuildSampleOptions();
        var result = PlateauOnlineImportViewModel.FilterSearchOptions(all, "");
        Assert.Empty(result);
    }

    [Fact]
    public void Whitespace_only_query_returns_no_results()
    {
        var all = BuildSampleOptions();
        var result = PlateauOnlineImportViewModel.FilterSearchOptions(all, "   ");
        Assert.Empty(result);
    }

    [Fact]
    public void Single_English_token_returns_all_areas_for_that_prefecture()
    {
        var all = BuildSampleOptions();
        var result = PlateauOnlineImportViewModel.FilterSearchOptions(all, "tokyo").ToList();
        // Tokyo entries in the sample: Chiyoda (13101), Shinjuku (13104), Shibuya (13113).
        Assert.Equal(3, result.Count);
        Assert.All(result, r => Assert.Equal("東京都", r.PrefectureJapaneseName));
    }

    [Fact]
    public void Single_Japanese_ward_token_isolates_one_area()
    {
        var all = BuildSampleOptions();
        var result = PlateauOnlineImportViewModel.FilterSearchOptions(all, "新宿").ToList();
        var single = Assert.Single(result);
        Assert.Equal("13104", single.CodeLabel);
    }

    [Fact]
    public void Area_code_token_isolates_one_area()
    {
        var all = BuildSampleOptions();
        var result = PlateauOnlineImportViewModel.FilterSearchOptions(all, "13104").ToList();
        var single = Assert.Single(result);
        Assert.Equal("13104", single.CodeLabel);
    }

    [Fact]
    public void Multi_token_query_intersects_matches()
    {
        var all = BuildSampleOptions();
        // 'tokyo' alone yields 3 Tokyo results; adding the Japanese ward kanji
        // disambiguates to a single area. We don't ship romaji for wards, so the
        // realistic multi-token pattern is "<English prefecture> <Japanese ward>".
        var result = PlateauOnlineImportViewModel.FilterSearchOptions(all, "tokyo 新宿").ToList();
        var single = Assert.Single(result);
        Assert.Equal("13104", single.CodeLabel);
    }

    [Fact]
    public void Matching_is_case_insensitive()
    {
        var all = BuildSampleOptions();
        var lower = PlateauOnlineImportViewModel.FilterSearchOptions(all, "tokyo").ToList();
        var upper = PlateauOnlineImportViewModel.FilterSearchOptions(all, "TOKYO").ToList();
        Assert.Equal(lower.Select(o => o.CodeLabel), upper.Select(o => o.CodeLabel));
        Assert.NotEmpty(lower);
    }

    [Fact]
    public void English_ward_name_finds_the_area()
    {
        // "Shinjuku" (no -ku suffix) should match the Shinjuku-ku entry via the
        // romaji token bundled into the search tokens by BuildSearchOption.
        var all = BuildSampleOptions();
        var result = PlateauOnlineImportViewModel.FilterSearchOptions(all, "shinjuku").ToList();
        var single = Assert.Single(result);
        Assert.Equal("13104", single.CodeLabel);
    }

    [Fact]
    public void Simplified_romaji_query_matches_long_vowel_entry()
    {
        // The kana for Osaka is オオサカ → literal romaji "oosaka". A user typing
        // the conventional "osaka" should still find the Osaka entry via the
        // simplified-romaji token. Use Kita-ku in Osaka-shi (27127) which contains
        // "oosaka..." in its literal romaji.
        var all = BuildSampleOptions();
        var result = PlateauOnlineImportViewModel.FilterSearchOptions(all, "osaka").ToList();
        Assert.NotEmpty(result);
        Assert.All(result, r => Assert.Equal("大阪府", r.PrefectureJapaneseName));
    }

    [Fact]
    public void Display_label_includes_romaji_when_available()
    {
        var all = BuildSampleOptions();
        AreaSearchOption shinjuku = all.Single(o => o.CodeLabel == "13104");
        Assert.Contains("Shinjuku", shinjuku.DisplayLabel);
        Assert.Contains("新宿区", shinjuku.DisplayLabel);
    }

    [Fact]
    public void Results_are_alphabetically_sorted_by_display_label()
    {
        var all = BuildSampleOptions();
        var result = PlateauOnlineImportViewModel.FilterSearchOptions(all, "tokyo").ToList();
        var labels = result.Select(o => o.DisplayLabel).ToList();
        Assert.Equal(labels, labels.OrderBy(s => s, StringComparer.Ordinal).ToList());
    }

    private static IReadOnlyList<AreaSearchOption> BuildSampleOptions()
    {
        // Hand-built sample mirroring how PlateauOnlineImportViewModel.BuildSearchOption
        // would shape these entries. Kept narrow so each assertion is unambiguous.
        return new List<AreaSearchOption>
        {
            MakeOption(pref: "東京都", city: "東京都", ward: "千代田区", code: "13101"),
            MakeOption(pref: "東京都", city: "東京都", ward: "新宿区",   code: "13104"),
            MakeOption(pref: "東京都", city: "東京都", ward: "渋谷区",   code: "13113"),
            MakeOption(pref: "北海道", city: "札幌市", ward: "中央区",   code: "01101"),
            MakeOption(pref: "北海道", city: "札幌市", ward: "北区",     code: "01102"),
            MakeOption(pref: "大阪府", city: "大阪市", ward: "北区",     code: "27127"),
        };
    }

    private static AreaSearchOption MakeOption(string pref, string city, string ward, string code)
    {
        // Build via the real BuildSearchOption so tests cover the actual tokenisation
        // logic (Japanese fields + English prefecture + romaji literal + romaji
        // simplified + area code).
        var area = new PlateauAreaOption(code, Array.Empty<string>(), $"{pref} {city} {ward}".Trim(), pref, city, ward);
        return PlateauOnlineImportViewModel.BuildSearchOption(area);
    }
}
