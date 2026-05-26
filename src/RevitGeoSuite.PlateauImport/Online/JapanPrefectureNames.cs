using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport.Online;

/// <summary>
/// Maps the 47 standard Japanese prefecture names (as PLATEAU surfaces them in the
/// <c>pref</c> field) to their conventional English names. Used to render dropdown
/// labels that are searchable by either script.
/// </summary>
public static class JapanPrefectureNames
{
    private static readonly IReadOnlyDictionary<string, string> EnglishByJapanese =
        new Dictionary<string, string>
        {
            ["北海道"] = "Hokkaido",
            ["青森県"] = "Aomori",
            ["岩手県"] = "Iwate",
            ["宮城県"] = "Miyagi",
            ["秋田県"] = "Akita",
            ["山形県"] = "Yamagata",
            ["福島県"] = "Fukushima",
            ["茨城県"] = "Ibaraki",
            ["栃木県"] = "Tochigi",
            ["群馬県"] = "Gunma",
            ["埼玉県"] = "Saitama",
            ["千葉県"] = "Chiba",
            ["東京都"] = "Tokyo",
            ["神奈川県"] = "Kanagawa",
            ["新潟県"] = "Niigata",
            ["富山県"] = "Toyama",
            ["石川県"] = "Ishikawa",
            ["福井県"] = "Fukui",
            ["山梨県"] = "Yamanashi",
            ["長野県"] = "Nagano",
            ["岐阜県"] = "Gifu",
            ["静岡県"] = "Shizuoka",
            ["愛知県"] = "Aichi",
            ["三重県"] = "Mie",
            ["滋賀県"] = "Shiga",
            ["京都府"] = "Kyoto",
            ["大阪府"] = "Osaka",
            ["兵庫県"] = "Hyogo",
            ["奈良県"] = "Nara",
            ["和歌山県"] = "Wakayama",
            ["鳥取県"] = "Tottori",
            ["島根県"] = "Shimane",
            ["岡山県"] = "Okayama",
            ["広島県"] = "Hiroshima",
            ["山口県"] = "Yamaguchi",
            ["徳島県"] = "Tokushima",
            ["香川県"] = "Kagawa",
            ["愛媛県"] = "Ehime",
            ["高知県"] = "Kochi",
            ["福岡県"] = "Fukuoka",
            ["佐賀県"] = "Saga",
            ["長崎県"] = "Nagasaki",
            ["熊本県"] = "Kumamoto",
            ["大分県"] = "Oita",
            ["宮崎県"] = "Miyazaki",
            ["鹿児島県"] = "Kagoshima",
            ["沖縄県"] = "Okinawa",
        };

    public static string? GetEnglishName(string japaneseName)
    {
        return EnglishByJapanese.TryGetValue(japaneseName, out string? english) ? english : null;
    }

    /// <summary>
    /// Returns <c>"&lt;English&gt; (&lt;Japanese&gt;)"</c> if the Japanese name is one
    /// of the 47 standardised prefectures, or the raw Japanese name otherwise.
    /// </summary>
    public static string GetDisplayLabel(string japaneseName)
    {
        string? english = GetEnglishName(japaneseName);
        return english is null ? japaneseName : $"{english} ({japaneseName})";
    }
}
