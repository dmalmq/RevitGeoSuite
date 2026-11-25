using System;
using System.IO;
using Newtonsoft.Json;

namespace RevitGeoSuite.CesiumHandoff;

public sealed class CesiumViewerSettings
{
    public const string DefaultViewerUrl = "http://localhost:3001";

    public string ViewerUrl { get; set; } = DefaultViewerUrl;

    public string? Token { get; set; }
}

/// <summary>
/// Persists the Cesium viewer connection settings as JSON under AppData, following the
/// FloorPlanExport settings-store pattern (best-effort load, defaults on any failure).
/// </summary>
public sealed class CesiumViewerSettingsStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _settingsFilePath;
    private readonly string? _legacySettingsFilePath;

    public CesiumViewerSettingsStore(string? settingsFilePath = null, string? legacySettingsFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            _settingsFilePath = DefaultSettingsFilePath;
            _legacySettingsFilePath = string.IsNullOrWhiteSpace(legacySettingsFilePath)
                ? LegacySettingsFilePath
                : legacySettingsFilePath!.Trim();
        }
        else
        {
            _settingsFilePath = settingsFilePath!.Trim();
            _legacySettingsFilePath = string.IsNullOrWhiteSpace(legacySettingsFilePath)
                ? null
                : legacySettingsFilePath!.Trim();
        }
    }

    public static string DefaultSettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KanizuRevit",
            "CesiumHandoff",
            "viewer-settings.json");

    public static string LegacySettingsFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RevitGeoSuite",
            "CesiumHandoff",
            "viewer-settings.json");

    public CesiumViewerSettings Load()
    {
        try
        {
            string? loadPath = ResolveLoadPath();
            if (loadPath is null)
            {
                return new CesiumViewerSettings();
            }

            CesiumViewerSettings? settings = JsonConvert.DeserializeObject<CesiumViewerSettings>(
                File.ReadAllText(loadPath));
            if (settings is null || string.IsNullOrWhiteSpace(settings.ViewerUrl))
            {
                return new CesiumViewerSettings();
            }

            return settings;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new CesiumViewerSettings();
        }
    }

    public void Save(CesiumViewerSettings settings)
    {
        if (settings is null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        string? directory = Path.GetDirectoryName(_settingsFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_settingsFilePath, JsonConvert.SerializeObject(settings, JsonSettings));
    }

    private string? ResolveLoadPath()
    {
        if (File.Exists(_settingsFilePath))
        {
            return _settingsFilePath;
        }

        if (!string.IsNullOrWhiteSpace(_legacySettingsFilePath) && File.Exists(_legacySettingsFilePath))
        {
            return _legacySettingsFilePath;
        }

        return null;
    }
}
