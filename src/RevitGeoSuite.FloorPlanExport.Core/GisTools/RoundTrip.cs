using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.CesiumHandoff;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>Where a model stands in the round trip. The five words are in <c>docs/kanizu-link-contract.md</c>.</summary>
public enum RoundTripState
{
    /// <summary>The GDB holds the newest export and no edit waits for Revit.</summary>
    InSync,

    /// <summary>Revit exported since the GDB last took an export in, and the GDB has nothing waiting.</summary>
    RevitAhead,

    /// <summary>The GDB holds edits of Revit's values or shapes that Revit has not taken in.</summary>
    GdbAhead,

    /// <summary>A new export waits for the GDB, and GDB edits wait for Revit.</summary>
    BothChanged,

    /// <summary>The state cannot be read, so neither side can go on until the folder is put right.</summary>
    Blocked,
}

/// <summary>One model's round-trip state, keyed as its drift file is.</summary>
public sealed class RoundTripModel
{
    public RoundTripModel(string model, string folder, RoundTripState state)
    {
        Model = model ?? string.Empty;
        Folder = folder ?? string.Empty;
        State = state;
    }

    /// <summary>The model's id in lower case, or its document key without one.</summary>
    public string Model { get; }

    /// <summary>The model folder under <c>revit/</c> its newest export is in.</summary>
    public string Folder { get; }

    public RoundTripState State { get; }
}

/// <summary>
/// The round-trip state of a station folder, read from <c>revit/</c> and <c>gis-drift/</c> alone.
/// Kanizu reads the same folder the same way (<c>docs/kanizu-link-contract.md</c>).
/// </summary>
public static class RoundTrip
{
    private const string ManifestName = "cesium-package.json";

    /// <summary>
    /// Every model of <paramref name="stationFolder"/> with an export, and its state. A station
    /// whose <c>revit/</c> folder cannot be listed is one Blocked entry with an empty model. A
    /// station with no export has none.
    /// </summary>
    public static IReadOnlyList<RoundTripModel> Models(string stationFolder)
    {
        if (!TryListVersions(stationFolder, out List<ListedVersion> versions, out bool blocked))
        {
            return blocked
                ? new[] { new RoundTripModel(string.Empty, string.Empty, RoundTripState.Blocked) }
                : Array.Empty<RoundTripModel>();
        }

        Dictionary<string, string> folderIds = FolderIds(versions);
        var newest = new Dictionary<string, ListedVersion>(StringComparer.Ordinal);
        foreach (ListedVersion version in versions)
        {
            string group = Group(version, folderIds);
            if (!newest.TryGetValue(group, out ListedVersion kept) || IsNewer(version, kept))
            {
                newest[group] = version;
            }
        }

        var models = new List<RoundTripModel>(newest.Count);
        foreach (ListedVersion version in newest.Values)
        {
            string model = version.ModelId ?? version.DocumentKey;
            models.Add(new RoundTripModel(model, version.ModelFolder, OfModel(stationFolder, version, model)));
        }

        models.Sort((left, right) =>
        {
            int folder = string.CompareOrdinal(left.Folder, right.Folder);
            return folder != 0 ? folder : string.CompareOrdinal(left.Model, right.Model);
        });
        return models;
    }

    /// <summary>
    /// The station's state from its models' states. Null for a station with no Revit export.
    /// </summary>
    public static RoundTripState? Station(IReadOnlyList<RoundTripModel> models)
    {
        if (models is null)
        {
            throw new ArgumentNullException(nameof(models));
        }

        if (models.Count == 0)
        {
            return null;
        }

        bool Has(RoundTripState state)
        {
            foreach (RoundTripModel model in models)
            {
                if (model.State == state)
                {
                    return true;
                }
            }

            return false;
        }

        if (Has(RoundTripState.Blocked))
        {
            return RoundTripState.Blocked;
        }

        if (Has(RoundTripState.BothChanged) || (Has(RoundTripState.RevitAhead) && Has(RoundTripState.GdbAhead)))
        {
            return RoundTripState.BothChanged;
        }

        if (Has(RoundTripState.RevitAhead))
        {
            return RoundTripState.RevitAhead;
        }

        if (Has(RoundTripState.GdbAhead))
        {
            return RoundTripState.GdbAhead;
        }

        return RoundTripState.InSync;
    }

    /// <summary>
    /// The version folders under <c>revit/</c>, so a poll can see a new export without reading
    /// manifests. Null when <c>revit/</c> cannot be listed; empty when the station has no export.
    /// </summary>
    public static string? VersionStamp(string stationFolder)
    {
        if (!TryListVersionFolders(stationFolder, out List<string> folders, out bool blocked))
        {
            return blocked ? null : string.Empty;
        }

        folders.Sort(StringComparer.Ordinal);
        return string.Join("|", folders);
    }

    private static RoundTripState OfModel(string station, ListedVersion newest, string model)
    {
        string path = Path.Combine(station, GisDrift.Folder, GisDrift.FileName(model));
        GisDriftFile drift;
        try
        {
            drift = GisDrift.Read(path);
        }
        catch (FileNotFoundException)
        {
            return RoundTripState.RevitAhead;
        }
        catch (DirectoryNotFoundException)
        {
            return RoundTripState.RevitAhead;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            return RoundTripState.Blocked;
        }

        bool applied = drift.AppliedVersion.Length > 0;
        if (applied && !AppliedExists(station, drift.AppliedVersion))
        {
            return RoundTripState.Blocked;
        }

        bool revit = !applied || !string.Equals(drift.AppliedVersion, newest.RelativePath, StringComparison.Ordinal);
        bool gdb = drift.Rows.Count > 0 || drift.Shapes.Count > 0;
        return (revit, gdb) switch
        {
            (false, false) => RoundTripState.InSync,
            (true, false) => RoundTripState.RevitAhead,
            (false, true) => RoundTripState.GdbAhead,
            _ => RoundTripState.BothChanged,
        };
    }

    private static bool AppliedExists(string station, string applied)
    {
        if (Path.IsPathRooted(applied))
        {
            return Directory.Exists(applied);
        }

        string path = station;
        foreach (string part in applied.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries))
        {
            path = Path.Combine(path, part);
        }

        return Directory.Exists(path);
    }

    private static bool TryListVersions(string stationFolder, out List<ListedVersion> versions, out bool blocked)
    {
        versions = new List<ListedVersion>();
        if (!TryListVersionFolders(stationFolder, out List<string> folders, out blocked))
        {
            return false;
        }

        foreach (string folder in folders)
        {
            string manifestPath = Path.Combine(folder, ManifestName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            ListedVersion? version = ReadVersion(folder, manifestPath);
            if (version != null)
            {
                versions.Add(version);
            }
        }

        return true;
    }

    /// <summary>False when there is nothing to list. <paramref name="blocked"/> is set when the listing itself failed.</summary>
    private static bool TryListVersionFolders(string stationFolder, out List<string> folders, out bool blocked)
    {
        folders = new List<string>();
        blocked = false;
        if (string.IsNullOrWhiteSpace(stationFolder))
        {
            blocked = true;
            return false;
        }

        string revit = Path.Combine(stationFolder, CesiumStationFolder.RevitDirectoryName);
        if (!TryDirectories(revit, out string[] modelDirs, out bool missing))
        {
            blocked = !missing;
            return false;
        }

        foreach (string modelDir in modelDirs)
        {
            if (Hidden(modelDir))
            {
                continue;
            }

            if (!TryDirectories(modelDir, out string[] versionDirs, out missing))
            {
                // A model folder that cannot be listed hides whatever export it holds.
                blocked = true;
                folders.Clear();
                return false;
            }

            foreach (string versionDir in versionDirs)
            {
                if (!Hidden(versionDir))
                {
                    folders.Add(versionDir);
                }
            }
        }

        return true;
    }

    private static bool TryDirectories(string path, out string[] directories, out bool missing)
    {
        directories = Array.Empty<string>();
        missing = false;
        try
        {
            directories = Directory.GetDirectories(path);
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            missing = true;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static ListedVersion? ReadVersion(string directory, string manifestPath)
    {
        string json;
        try
        {
            using StreamReader reader = new(manifestPath, new UTF8Encoding(false), detectEncodingFromByteOrderMarks: true);
            json = reader.ReadToEnd();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }

        CesiumPackageManifest manifest;
        try
        {
            manifest = CesiumPackageManifestSerializer.Deserialize(json);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            return null;
        }

        // The serializer accepts an older version; a reader that does not know it skips the folder.
        if (manifest.Version != CesiumPackageManifest.CurrentVersion)
        {
            return null;
        }

        string? modelId = manifest.Source?.ModelId;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            modelId = null;
        }
        else
        {
            modelId = modelId!.ToLowerInvariant();
        }

        string? parent = Path.GetDirectoryName(directory);
        return new ListedVersion(
            Path.GetFileName(parent ?? string.Empty),
            Path.GetFileName(directory),
            directory,
            SortableTime(CreatedUtcText(json)),
            modelId,
            manifest.Source?.DocumentKey ?? string.Empty);
    }

    private static string CreatedUtcText(string json)
    {
        try
        {
            using JsonTextReader reader = new(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            if (JToken.ReadFrom(reader) is not JObject root || root["createdUtc"] is not JValue { Type: JTokenType.String } created)
            {
                return string.Empty;
            }

            return (string)created! ?? string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// <c>2026-11-08T23:51:31.12Z</c> as <c>20261108235131.1200000</c>, so a time with a fraction
    /// sorts with one without. Anything else sorts after every parsed time.
    /// </summary>
    private static string SortableTime(string text)
    {
        string digits = AsciiDigits(text);
        int dot = text.IndexOf('.');
        string whole = dot < 0 ? text : text.Substring(0, dot);
        string fraction = dot < 0 ? string.Empty : TrimEndNonDigits(text.Substring(dot + 1));
        string wholeDigits = AsciiDigits(whole);
        if (wholeDigits.Length != 14 || digits.Length != wholeDigits.Length + fraction.Length)
        {
            return "~" + text;
        }

        return wholeDigits + "." + fraction.PadRight(7, '0');
    }

    private static string AsciiDigits(string text)
    {
        StringBuilder digits = new(text.Length);
        foreach (char character in text)
        {
            if (character >= '0' && character <= '9')
            {
                digits.Append(character);
            }
        }

        return digits.ToString();
    }

    private static string TrimEndNonDigits(string text)
    {
        int end = text.Length;
        while (end > 0 && (text[end - 1] < '0' || text[end - 1] > '9'))
        {
            end--;
        }

        return text.Substring(0, end);
    }

    private static Dictionary<string, string> FolderIds(List<ListedVersion> versions)
    {
        var ids = new Dictionary<string, (string Key, string Path, string Id)>(StringComparer.Ordinal);
        foreach (ListedVersion version in versions)
        {
            if (version.ModelId == null)
            {
                continue;
            }

            if (!ids.TryGetValue(version.ModelFolder, out (string Key, string Path, string Id) kept) ||
                string.CompareOrdinal(version.CreatedKey, kept.Key) > 0 ||
                (version.CreatedKey == kept.Key && string.CompareOrdinal(version.Directory, kept.Path) > 0))
            {
                ids[version.ModelFolder] = (version.CreatedKey, version.Directory, version.ModelId);
            }
        }

        var folderIds = new Dictionary<string, string>(ids.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, (string Key, string Path, string Id)> entry in ids)
        {
            folderIds[entry.Key] = entry.Value.Id;
        }

        return folderIds;
    }

    private static string Group(ListedVersion version, Dictionary<string, string> folderIds)
    {
        if (version.ModelId != null)
        {
            return "id\0" + version.ModelId;
        }

        return folderIds.TryGetValue(version.ModelFolder, out string? id)
            ? "id\0" + id
            : "folder\0" + version.ModelFolder;
    }

    private static bool IsNewer(ListedVersion candidate, ListedVersion current) =>
        string.CompareOrdinal(candidate.CreatedKey, current.CreatedKey) > 0 ||
        (candidate.CreatedKey == current.CreatedKey && string.CompareOrdinal(candidate.Directory, current.Directory) > 0);

    private static bool Hidden(string path) =>
        Path.GetFileName(path).StartsWith(".", StringComparison.Ordinal);

    private sealed class ListedVersion
    {
        public ListedVersion(string modelFolder, string versionFolder, string directory, string createdKey, string? modelId, string documentKey)
        {
            ModelFolder = modelFolder;
            Directory = directory;
            CreatedKey = createdKey;
            ModelId = modelId;
            DocumentKey = documentKey;
            RelativePath = CesiumStationFolder.RevitDirectoryName + "/" + modelFolder + "/" + versionFolder;
        }

        public string ModelFolder { get; }

        public string Directory { get; }

        public string CreatedKey { get; }

        public string? ModelId { get; }

        public string DocumentKey { get; }

        public string RelativePath { get; }
    }
}
