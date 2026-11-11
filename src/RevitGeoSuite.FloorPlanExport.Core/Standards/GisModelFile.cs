using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>
/// <c>gis-model.json</c>: one model's GIS export settings, kept in the station folder on the share
/// so every PC exports the model the same way. It sits at
/// <c>&lt;station&gt;/gis-models/&lt;model id&gt;.gis-model.json</c>, keyed by the model's stable id so
/// a building rename or Save As keeps the same file. RevitGeoSuite is its only writer.
/// </summary>
/// <remarks>
/// Reading is tolerant: a missing section falls back to the standard's defaults, and an unknown
/// field is kept as it is when the file is written again, so a newer RevitGeoSuite's fields
/// survive an export from an older one.
/// </remarks>
public sealed class GisModelFile
{
    public const string SchemaName = "revitgeosuite.gis-model";
    public const int CurrentVersion = 1;
    public const string FolderName = "gis-models";
    public const string Suffix = ".gis-model.json";
    public const string TemplateFileName = "gis-model.template.json";

    /// <summary>The name prefix of the plan views the standard exports.</summary>
    public const string GisViewPrefix = "GIS_";

    /// <summary>The <c>fields</c> value that means the field list shipped with the standard.</summary>
    public const string StandardFields = "standard";

    [JsonProperty("schema")]
    public string Schema { get; set; } = SchemaName;

    [JsonProperty("version")]
    public int Version { get; set; } = CurrentVersion;

    [JsonProperty("identity")]
    public GisModelIdentity Identity { get; set; } = new();

    [JsonProperty("levels")]
    public List<GisModelLevel> Levels { get; set; } = new();

    /// <summary>The plan views to export, normally the <c>GIS_</c> views. Empty means not chosen yet.</summary>
    [JsonProperty("views")]
    public List<GisModelView> Views { get; set; } = new();

    [JsonProperty("drawingRules")]
    public GisModelDrawingRules DrawingRules { get; set; } = new();

    /// <summary>
    /// Where the field list comes from. <see cref="StandardFields"/> is the list shipped with the
    /// standard; a later version may hold a path to a shared list instead.
    /// </summary>
    [JsonProperty("fields")]
    public string Fields { get; set; } = StandardFields;

    [JsonProperty("versions")]
    public GisModelVersions Versions { get; set; } = new();

    [JsonExtensionData]
    private IDictionary<string, JToken>? UnknownFields { get; set; }

    /// <summary>The file for a model in a station folder.</summary>
    public static string PathFor(string stationFolder, string modelId)
    {
        if (string.IsNullOrWhiteSpace(stationFolder))
        {
            throw new ArgumentException("A station folder is required.", nameof(stationFolder));
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            throw new ArgumentException("A model id is required.", nameof(modelId));
        }

        string name = modelId.Trim().ToLowerInvariant();
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.Contains(".."))
        {
            throw new ArgumentException($"'{modelId}' cannot be used in a file name.", nameof(modelId));
        }

        return Path.Combine(stationFolder.Trim(), FolderName, name + Suffix);
    }

    /// <summary>A new file with the standard's defaults: what <c>gis-model.template.json</c> holds.</summary>
    public static GisModelFile CreateDefault()
    {
        return new GisModelFile();
    }

    /// <summary>The template shipped in <c>standards/</c>, read from this assembly.</summary>
    public static GisModelFile CreateFromTemplate()
    {
        return Parse(GisStandardFiles.ReadGisModelTemplate(), TemplateFileName);
    }

    public static GisModelFile Parse(string json, string sourceLabel)
    {
        JObject root;
        try
        {
            root = JObject.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{sourceLabel} is not valid JSON: {ex.Message}", ex);
        }

        string? schema = root.Value<string>("schema");
        if (!string.Equals(schema, SchemaName, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{sourceLabel} is not a GIS model file (schema '{schema}').");
        }

        int version = root.Value<int?>("version") ?? 0;
        if (version < 1 || version > CurrentVersion)
        {
            throw new InvalidDataException(
                $"{sourceLabel} has version {version}; this RevitGeoSuite reads version {CurrentVersion}. Update RevitGeoSuite.");
        }

        GisModelFile file = root.ToObject<GisModelFile>() ?? new GisModelFile();
        file.Identity ??= new GisModelIdentity();
        file.Levels = (file.Levels ?? new List<GisModelLevel>()).Where(level => level != null).ToList();
        file.Views = (file.Views ?? new List<GisModelView>()).Where(view => view != null).ToList();
        file.DrawingRules ??= new GisModelDrawingRules();
        file.DrawingRules.Stairs ??= new GisModelStairRules();
        file.Versions ??= new GisModelVersions();
        if (string.IsNullOrWhiteSpace(file.Fields))
        {
            file.Fields = StandardFields;
        }

        return file;
    }

    /// <summary>The file at <paramref name="path"/>, or null when there is none.</summary>
    public static GisModelFile? TryLoad(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        return Parse(File.ReadAllText(path, Encoding.UTF8), path);
    }

    public string ToJson()
    {
        return JsonConvert.SerializeObject(this, Formatting.Indented).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>Writes the file through a temporary file and a rename, so a reader never sees half of it.</summary>
    public void Save(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string temp = path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
        File.WriteAllText(temp, ToJson(), new UTF8Encoding(false));
        try
        {
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path);
            }
        }
        finally
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
    }

    /// <summary>The stair settings the export uses for this model.</summary>
    public StairDrawingSettings ToStairDrawingSettings()
    {
        GisModelStairRules stairs = DrawingRules?.Stairs ?? new GisModelStairRules();
        StairDrawingSettings defaults = StairDrawingSettings.Default;
        return new StairDrawingSettings
        {
            StepSpacingMeters = stairs.StepSpacingMeters ?? defaults.StepSpacingMeters,
            LineGapBeforeMiddleMeters = stairs.StopBeforeMiddleMeters ?? defaults.LineGapBeforeMiddleMeters,
            FloorOverlapMeters = stairs.OverlapMeters ?? defaults.FloorOverlapMeters,
        }.GetEffective();
    }

    /// <summary>
    /// The views an export takes when nobody picked any: those <paramref name="file"/> lists that
    /// still exist, else the <c>GIS_</c> views, else all of <paramref name="available"/>.
    /// </summary>
    public static IReadOnlyList<T> ChooseDefaultViews<T>(
        IEnumerable<T> available,
        Func<T, string> uniqueId,
        Func<T, string> name,
        GisModelFile? file)
    {
        List<T> candidates = available.ToList();
        if (file != null)
        {
            IReadOnlyList<T> listed = file.MatchViews(candidates, uniqueId, name);
            if (listed.Count > 0)
            {
                return listed;
            }
        }

        List<T> gisViews = candidates
            .Where(candidate => (name(candidate) ?? string.Empty).StartsWith(GisViewPrefix, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return gisViews.Count > 0 ? gisViews : candidates;
    }

    /// <summary>
    /// The views the file lists that <paramref name="available"/> still holds, by UniqueId and
    /// then by name, in the file's order. Empty when the file lists none or none is found.
    /// </summary>
    public IReadOnlyList<T> MatchViews<T>(IEnumerable<T> available, Func<T, string> uniqueId, Func<T, string> name)
    {
        List<T> candidates = available.ToList();
        List<int> matchedIndexes = new();
        foreach (GisModelView view in Views)
        {
            int index = string.IsNullOrEmpty(view.UniqueId)
                ? -1
                : candidates.FindIndex(candidate => string.Equals(uniqueId(candidate), view.UniqueId, StringComparison.OrdinalIgnoreCase));
            if (index < 0 && !string.IsNullOrEmpty(view.Name))
            {
                index = candidates.FindIndex(candidate => string.Equals(name(candidate), view.Name, StringComparison.Ordinal));
            }

            if (index >= 0 && !matchedIndexes.Contains(index))
            {
                matchedIndexes.Add(index);
            }
        }

        List<T> matched = matchedIndexes.Select(index => candidates[index]).ToList();
        return matched;
    }
}

public sealed class GisModelIdentity
{
    /// <summary>The model's stable id (<c>GeoProjectInfo.ModelId</c>), the same as the manifest's <c>source.modelId</c>.</summary>
    [JsonProperty("modelId")]
    public string ModelId { get; set; } = string.Empty;

    /// <summary>The station folder's name, for people reading the share.</summary>
    [JsonProperty("station")]
    public string Station { get; set; } = string.Empty;

    [JsonProperty("building")]
    public string Building { get; set; } = string.Empty;

    /// <summary>The CRS of the GIS export, such as 6677 for Tokyo. Null until the first export.</summary>
    [JsonProperty("crsEpsg")]
    public int? CrsEpsg { get; set; }
}

public sealed class GisModelLevel
{
    [JsonProperty("uniqueId")]
    public string UniqueId { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>The GIS <c>level_id</c> the level's features carry.</summary>
    [JsonProperty("levelId")]
    public string LevelId { get; set; } = string.Empty;

    [JsonProperty("ordinal")]
    public int? Ordinal { get; set; }

    [JsonProperty("shortName")]
    public string ShortName { get; set; } = string.Empty;
}

public sealed class GisModelView
{
    [JsonProperty("uniqueId")]
    public string UniqueId { get; set; } = string.Empty;

    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;
}

public sealed class GisModelDrawingRules
{
    [JsonProperty("stairs")]
    public GisModelStairRules Stairs { get; set; } = new();
}

/// <summary>The stair drawing rule's numbers. A missing number takes the standard's value.</summary>
public sealed class GisModelStairRules
{
    /// <summary>Distance between step lines along the walking path: 0.60, or 0.30.</summary>
    [JsonProperty("stepSpacingMeters")]
    public double? StepSpacingMeters { get; set; } = StairDrawingSettings.Default.StepSpacingMeters;

    /// <summary>How far before the middle of the stair each floor's step lines stop.</summary>
    [JsonProperty("stopBeforeMiddleMeters")]
    public double? StopBeforeMiddleMeters { get; set; } = StairDrawingSettings.Default.LineGapBeforeMiddleMeters;

    /// <summary>How much the lower and upper floor polygons overlap at the middle. 0 tiles them exactly.</summary>
    [JsonProperty("overlapMeters")]
    public double? OverlapMeters { get; set; } = StairDrawingSettings.Default.FloorOverlapMeters;
}

public sealed class GisModelVersions
{
    /// <summary>The Kanizu GIS standard version the model was on at the last export, or null.</summary>
    [JsonProperty("standard")]
    public int? Standard { get; set; }

    [JsonProperty("lastExportRevitGeoSuite")]
    public string LastExportRevitGeoSuite { get; set; } = string.Empty;

    [JsonProperty("lastExportUtc")]
    public string LastExportUtc { get; set; } = string.Empty;
}
