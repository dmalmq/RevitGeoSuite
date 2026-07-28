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
    private readonly string? _legacySettingsFilePath;

    public ExportDialogSettingsStore(string? settingsFilePath = null, string? legacySettingsFilePath = null)
    {
        if (string.IsNullOrWhiteSpace(settingsFilePath))
        {
            _settingsFilePath = FloorPlanExportAppDataPaths.CurrentSettingsFilePath;
            _legacySettingsFilePath = string.IsNullOrWhiteSpace(legacySettingsFilePath)
                ? FloorPlanExportAppDataPaths.LegacySettingsFilePath
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

        if (!string.IsNullOrWhiteSpace(_legacySettingsFilePath) && File.Exists(_legacySettingsFilePath))
        {
            return _legacySettingsFilePath!;
        }

        return _settingsFilePath;
    }
}
