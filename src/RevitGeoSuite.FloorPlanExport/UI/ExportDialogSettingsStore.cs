using System;
using System.IO;
using Newtonsoft.Json;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Utilities;

namespace RevitGeoSuite.FloorPlanExport.UI;

public sealed class ExportDialogSettingsStore
{
    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        Formatting = Formatting.Indented,
    };

    private readonly string _settingsFilePath;
    private readonly string[] _legacySettingsFilePaths;

    public ExportDialogSettingsStore(string? settingsFilePath = null, string? legacySettingsFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            _settingsFilePath = FloorPlanExportAppDataPaths.CurrentSettingsFilePath;
            _legacySettingsFilePaths = string.IsNullOrWhiteSpace(legacySettingsFilePath)
                ? new[]
                {
                    FloorPlanExportAppDataPaths.LegacyRevitGeoSuiteSettingsFilePath,
                    FloorPlanExportAppDataPaths.LegacySettingsFilePath,
                }
                : new[] { legacySettingsFilePath!.Trim() };
        }
        else
        {
            _settingsFilePath = settingsFilePath!.Trim();
            _legacySettingsFilePaths = string.IsNullOrWhiteSpace(legacySettingsFilePath)
                ? Array.Empty<string>()
                : new[] { legacySettingsFilePath!.Trim() };
        }
    }

    public ExportDialogSettings Load()
    {
        return LoadWithDiagnostics().Value;
    }

    public LoadResult<ExportDialogSettings> LoadWithDiagnostics()
    {
        return JsonFileLoadHelper.Load(
            ResolveLoadPath(),
            createDefaultValue: () => new ExportDialogSettings(),
            deserialize: json => JsonConvert.DeserializeObject<ExportDialogSettings>(json),
            documentLabel: "Export dialog settings");
    }

    public void Save(ExportDialogSettings settings)
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

        string json = JsonConvert.SerializeObject(settings, JsonSettings);
        File.WriteAllText(_settingsFilePath, json);
    }

    private string ResolveLoadPath()
    {
        if (File.Exists(_settingsFilePath))
        {
            return _settingsFilePath;
        }

        foreach (string legacyPath in _legacySettingsFilePaths)
        {
            if (!string.IsNullOrWhiteSpace(legacyPath) && File.Exists(legacyPath))
            {
                return legacyPath;
            }
        }

        return _settingsFilePath;
    }
}
