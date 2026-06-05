using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RevitGeoSuite.SharedUI.Localization;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.SharedUI.Shell.Handlers;

public sealed class LocalizationGetAllHandler : IRpcHandler
{
    public string Method => "localization.getAll";

    public Task<object?> HandleAsync(object? payload)
    {
        // Return the full current-language dictionary so the Svelte i18n store can render every
        // string, not just the 16 keys the old hard-coded list exposed. The localizer already
        // falls back to English for any missing key, so this is always a complete view.
        return Task.FromResult<object?>(LocalizationPayloads.CreateStrings(UiLocalizer.Instance));
    }
}

public sealed class LocalizationSetLanguageHandler : IRpcHandler
{
    public string Method => "localization.setLanguage";

    public Task<object?> HandleAsync(object? payload)
    {
        if (payload is Newtonsoft.Json.Linq.JObject jobj)
        {
            var lang = jobj.Value<string>("language");
            if (lang != null)
            {
                var language = lang.ToLower() == "japanese" || lang.ToLower() == "ja"
                    ? UiLanguage.Japanese
                    : UiLanguage.English;
                
                UiLocalizer.Instance.SetLanguage(language);

                LocalizationStrings strings = LocalizationPayloads.CreateStrings(UiLocalizer.Instance);
                return Task.FromResult<object?>(new LocalizationSetLanguageResponse
                {
                    Success = true,
                    Language = strings.Language,
                    Strings = strings.Strings
                });
            }
        }

        return Task.FromResult<object?>(new LocalizationSetLanguageResponse
        {
            Success = false,
            Error = "Invalid language parameter"
        });
    }
}

internal static class LocalizationPayloads
{
    public static LocalizationStrings CreateStrings(UiLocalizer localizer)
    {
        if (localizer is null) throw new ArgumentNullException(nameof(localizer));

        return new LocalizationStrings
        {
            Language = localizer.CurrentLanguage.ToString().ToLowerInvariant(),
            Strings = Copy(localizer.GetAllStrings())
        };
    }

    private static Dictionary<string, string> Copy(IReadOnlyDictionary<string, string> source)
    {
        var copy = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> pair in source)
        {
            copy[pair.Key] = pair.Value;
        }

        return copy;
    }
}
