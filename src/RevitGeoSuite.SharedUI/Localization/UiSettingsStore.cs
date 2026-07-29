using System;
using System.IO;
using Newtonsoft.Json;

namespace RevitGeoSuite.SharedUI.Localization;

internal sealed class UiSettingsStore
{
    private readonly string settingsPath;

    public UiSettingsStore()
    {
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RevitGeoSuite");
        settingsPath = Path.Combine(root, "ui-settings.json");
    }

    public UiLanguage Load()
    {
        try
        {
            if (!File.Exists(settingsPath))
            {
                return UiLanguage.English;
            }

            UiSettingsDocument? document = JsonConvert.DeserializeObject<UiSettingsDocument>(File.ReadAllText(settingsPath));
            if (document is not null && Enum.TryParse(document.Language, ignoreCase: true, out UiLanguage parsed))
            {
                return parsed;
            }
        }
        catch
        {
        }

        return UiLanguage.English;
    }

    public void Save(UiLanguage language)
    {
        try
        {
            string? directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            UiSettingsDocument document = new UiSettingsDocument
            {
                Language = language.ToString()
            };

            File.WriteAllText(settingsPath, JsonConvert.SerializeObject(document, Formatting.Indented));
        }
        catch
        {
        }
    }

    private sealed class UiSettingsDocument
    {
        public string Language { get; set; } = UiLanguage.English.ToString();
    }
}
