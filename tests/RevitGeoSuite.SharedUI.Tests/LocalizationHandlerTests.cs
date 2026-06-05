using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.SharedUI.Localization;
using RevitGeoSuite.SharedUI.Shell.Handlers;
using RevitGeoSuite.SharedUI.Web.Contracts;
using Xunit;

namespace RevitGeoSuite.SharedUI.Tests;

public class LocalizationHandlerTests
{
    [Fact]
    public async Task SetLanguage_ReturnsJapaneseStringDictionary()
    {
        UiLanguage originalLanguage = UiLocalizer.Instance.CurrentLanguage;
        var handler = new LocalizationSetLanguageHandler();

        try
        {
            object? response = await handler.HandleAsync(JObject.FromObject(new { language = "japanese" }));
            var result = Assert.IsType<LocalizationSetLanguageResponse>(response);

            Assert.True(result.Success);
            Assert.Equal("japanese", result.Language);
            Assert.Equal("現在の設定", result.Strings["Georef.Wizard.CurrentSetup"]);
        }
        finally
        {
            UiLocalizer.Instance.SetLanguage(originalLanguage);
        }
    }
}
