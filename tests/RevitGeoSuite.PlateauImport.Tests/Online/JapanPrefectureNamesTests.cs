using RevitGeoSuite.PlateauImport.Online;
using Xunit;

namespace RevitGeoSuite.PlateauImport.Tests.Online;

public sealed class JapanPrefectureNamesTests
{
    [Theory]
    [InlineData("北海道", "Hokkaido (北海道)")]
    [InlineData("東京都", "Tokyo (東京都)")]
    [InlineData("大阪府", "Osaka (大阪府)")]
    [InlineData("京都府", "Kyoto (京都府)")]
    [InlineData("沖縄県", "Okinawa (沖縄県)")]
    public void GetDisplayLabel_combines_English_and_Japanese_for_known_prefectures(string japanese, string expected)
    {
        Assert.Equal(expected, JapanPrefectureNames.GetDisplayLabel(japanese));
    }

    [Fact]
    public void GetDisplayLabel_falls_back_to_raw_Japanese_for_unknown_value()
    {
        // Defensive fallback: if PLATEAU ever surfaces an unexpected pref string we
        // still display *something*, just without the English annotation.
        Assert.Equal("架空県", JapanPrefectureNames.GetDisplayLabel("架空県"));
    }
}
