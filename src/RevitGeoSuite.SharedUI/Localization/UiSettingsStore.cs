using System;
using System.IO;
using Newtonsoft.Json;

namespace RevitGeoSuite.SharedUI.Localization;

internal sealed class UiSettingsStore
{
    private readonly string settingsPath;
    private readonly string? legacySettingsPath;

    public UiSettingsStore()
        : this(DefaultSettingsFilePath, LegacySettingsFilePath)
    {
    }

    internal UiSettingsStore(string settingsPath, string? legacySettingsPath = null)
    {
        this.settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
        this.legacySettingsPath = string.IsNullOrWhiteSpace(legacySettingsPath) ? null : legacySettingsPath!.Trim();
    }

    internal static string DefaultSettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "KanizuRevit",
            "ui-settings.json");

    internal static string LegacySettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitGeoSuite",
            "ui-settings.json");

    public UiLanguage Load()
    {
        try
        {
            string? loadPath = ResolveLoadPath();
            if (loadPath is null)
            {
                return UiLanguage.English;
            }

            UiSettingsDocument? document = JsonConvert.DeserializeObject<UiSettingsDocument>(File.ReadAllText(loadPath));
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

    private string? ResolveLoadPath()
    {
        if (File.Exists(settingsPath))
        {
            return settingsPath;
        }

        if (!string.IsNullOrWhiteSpace(legacySettingsPath) && File.Exists(legacySettingsPath))
        {
            return legacySettingsPath;
        }

        return null;
    }

    private sealed class UiSettingsDocument
    {
        public string Language { get; set; } = UiLanguage.English.ToString();
    }
}
