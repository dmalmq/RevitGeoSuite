using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>
/// The models whose standards notice a person chose to skip for a version, on this PC. A newer
/// standard version shows the notice again.
/// </summary>
public sealed class GisStandardNoticeStore
{
    private readonly string _path;

    public GisStandardNoticeStore(string? path = null)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? Path.Combine(FloorPlanExportAppDataPaths.CurrentBaseDirectory, "gis-standard-notices.json")
            : path!;
    }

    public bool IsSkipped(string modelKey, int standardVersion)
    {
        return !string.IsNullOrWhiteSpace(modelKey) &&
               Load().TryGetValue(modelKey.Trim().ToLowerInvariant(), out int skipped) &&
               skipped >= standardVersion;
    }

    public void Skip(string modelKey, int standardVersion)
    {
        if (string.IsNullOrWhiteSpace(modelKey))
        {
            return;
        }

        Dictionary<string, int> skipped = Load();
        skipped[modelKey.Trim().ToLowerInvariant()] = standardVersion;
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = _path + ".tmp";
        File.WriteAllText(temp, JsonConvert.SerializeObject(skipped, Formatting.Indented), new UTF8Encoding(false));
        if (File.Exists(_path))
        {
            File.Replace(temp, _path, null);
        }
        else
        {
            File.Move(temp, _path);
        }
    }

    private Dictionary<string, int> Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                return JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(_path, Encoding.UTF8))
                    ?? new Dictionary<string, int>();
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException)
        {
            // A damaged file only means the notice shows again.
        }

        return new Dictionary<string, int>();
    }
}
