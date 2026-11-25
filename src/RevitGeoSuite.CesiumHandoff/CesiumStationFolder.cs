using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RevitGeoSuite.CesiumHandoff;

/// <summary>
/// Writes packages into a station folder that Kanizu reads, one new version folder per export:
/// <c>&lt;station&gt;/revit/&lt;model&gt;/&lt;yyyyMMddTHHmmssZ&gt;-&lt;hash8&gt;/</c>.
/// </summary>
/// <remarks>
/// RevitGeoSuite is the only writer under <c>revit/</c>, and Kanizu the only writer of
/// <c>kanizu-link.json</c>, so nothing on the share has two writers. A version is built in a
/// hidden <c>revit/.tmp-&lt;id&gt;</c> folder with the manifest written last, then renamed into
/// place: on an SMB share a directory rename is the only atomic step, so a reader never sees a
/// half-written version, and a folder without <c>cesium-package.json</c> is to be ignored.
/// Old versions are never touched here; Kanizu keeps the last few and the one it applied.
/// </remarks>
public static class CesiumStationFolder
{
    public const string RevitDirectoryName = "revit";

    public const string StagingPrefix = ".tmp-";

    private static readonly Regex VersionFolderNamePattern = new(
        @"^\d{8}T\d{6}Z(-[0-9a-f]+)?(-\d+)?$",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    /// <summary>Starts a staging package on the same volume as the versions it will join.</summary>
    public static CesiumPackageLayout CreateStagingLayout(string stationFolder)
    {
        string revitRoot = RevitRoot(stationFolder);
        Directory.CreateDirectory(revitRoot);
        string stagingRoot = Path.Combine(revitRoot, StagingPrefix + Guid.NewGuid().ToString("N"));
        return new CesiumPackageLayoutBuilder().CreateLayout(stagingRoot);
    }

    /// <summary>
    /// Renames a staged package (manifest already written) into its version folder and returns the
    /// published layout. A second export in the same second with the same content gets a suffix.
    /// </summary>
    public static CesiumPackageLayout Publish(CesiumPackageLayout stagingLayout, string stationFolder, CesiumPackageManifest manifest)
    {
        if (stagingLayout is null)
        {
            throw new ArgumentNullException(nameof(stagingLayout));
        }

        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        if (!File.Exists(stagingLayout.ManifestPath))
        {
            throw new InvalidOperationException("The staged package has no manifest.");
        }

        string modelDirectory = Path.Combine(RevitRoot(stationFolder), ModelFolderName(manifest));
        Directory.CreateDirectory(modelDirectory);
        string versionName = VersionFolderName(manifest.CreatedUtc, manifest.ContentHash);
        string destination = Path.Combine(modelDirectory, versionName);
        for (int attempt = 2; Directory.Exists(destination) || File.Exists(destination); attempt++)
        {
            destination = Path.Combine(modelDirectory, $"{versionName}-{attempt.ToString(CultureInfo.InvariantCulture)}");
        }

        Directory.Move(stagingLayout.RootDirectory, destination);
        return new CesiumPackageLayout(destination);
    }

    /// <summary>
    /// The per-model folder: the building name's slug plus the start of the stable model id, so
    /// it reads well on the share and stays the same across users and Save As. Without a model id
    /// it falls back to the building id, which hashes the file path.
    /// </summary>
    public static string ModelFolderName(CesiumPackageManifest manifest)
    {
        if (manifest is null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        string? modelId = manifest.Source?.ModelId;
        string buildingId = manifest.Building?.Id ?? string.Empty;
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return string.IsNullOrWhiteSpace(buildingId) ? "model" : buildingId;
        }

        string idPart = ModelIdPart(modelId!);
        string slug = CesiumBuildingIdentity.Slugify(manifest.Building?.Name);
        return slug.Length > 0 ? $"{slug}-{idPart}" : idPart;
    }

    /// <summary>The model id's part of a model folder name: its first 8 characters without dashes, lower case.</summary>
    private static string ModelIdPart(string modelId)
    {
        string idPart = modelId.Trim().Replace("-", string.Empty);
        return (idPart.Length > 8 ? idPart.Substring(0, 8) : idPart).ToLowerInvariant();
    }

    /// <summary>
    /// The newest other version of the same model that has a <c>gis/</c> folder, by the manifest's
    /// <c>createdUtc</c>, or null. Versions older than the given one only, so a send compares with
    /// what was there before it. The same model is the version's own model folder, plus any other
    /// model folder under <c>revit/</c> whose versions carry the same <c>source.modelId</c>: a Save
    /// As or a renamed building changes the folder's slug but not the model id. Only the other model
    /// folders whose name ends in the model id's part are opened, and of their versions only the
    /// manifest is read. A folder that is hidden, has no manifest or whose manifest cannot be read is
    /// skipped, as Kanizu skips it.
    /// </summary>
    public static string? PreviousVersion(string versionDirectory)
    {
        if (string.IsNullOrWhiteSpace(versionDirectory))
        {
            throw new ArgumentException("A version folder is required.", nameof(versionDirectory));
        }

        string self = Path.GetFullPath(versionDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string? modelDirectory = Path.GetDirectoryName(self);
        CesiumPackageManifest? selfManifest = TryReadManifest(self);
        if (modelDirectory is null || !Directory.Exists(modelDirectory) || selfManifest is null)
        {
            return null;
        }

        DateTime selfCreated = selfManifest.CreatedUtc.ToUniversalTime();
        string? modelId = selfManifest.Source?.ModelId?.Trim();
        var candidates = new List<(string Folder, bool SameFolder)>();
        candidates.AddRange(Directory.GetDirectories(modelDirectory).Select(folder => (folder, true)));
        string? revitDirectory = Path.GetDirectoryName(modelDirectory);
        if (!string.IsNullOrEmpty(modelId) && revitDirectory is not null && Directory.Exists(revitDirectory))
        {
            string idPart = ModelIdPart(modelId!);
            foreach (string otherModel in Directory.GetDirectories(revitDirectory))
            {
                string otherName = Path.GetFileName(otherModel);
                if (!otherName.StartsWith(".", StringComparison.Ordinal)
                    && (string.Equals(otherName, idPart, StringComparison.OrdinalIgnoreCase) || otherName.EndsWith("-" + idPart, StringComparison.OrdinalIgnoreCase))
                    && !string.Equals(Path.GetFullPath(otherModel).TrimEnd(Path.DirectorySeparatorChar), modelDirectory, StringComparison.OrdinalIgnoreCase))
                {
                    candidates.AddRange(Directory.GetDirectories(otherModel).Select(folder => (folder, false)));
                }
            }
        }

        string? best = null;
        DateTime bestCreated = DateTime.MinValue;
        foreach ((string candidate, bool sameFolder) in candidates)
        {
            string name = Path.GetFileName(candidate);
            if (name.StartsWith(".", StringComparison.Ordinal)
                || string.Equals(Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar), self, StringComparison.OrdinalIgnoreCase)
                || !Directory.Exists(Path.Combine(candidate, "gis")))
            {
                continue;
            }

            CesiumPackageManifest? manifest = TryReadManifest(candidate);
            if (manifest is null
                || (!sameFolder && !string.Equals(manifest.Source?.ModelId?.Trim(), modelId, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            DateTime created = manifest.CreatedUtc.ToUniversalTime();
            if (created > selfCreated)
            {
                continue;
            }

            if (best is null || created > bestCreated
                || (created == bestCreated && string.CompareOrdinal(name, Path.GetFileName(best)) > 0))
            {
                best = candidate;
                bestCreated = created;
            }
        }

        return best;
    }

    /// <summary>What a folder is to Send to Kanizu before the first send into it.</summary>
    public enum FolderKind
    {
        /// <summary>No such folder: a mistyped or unplugged share.</summary>
        Missing,

        /// <summary>The folder exists but holds no <c>revit/</c> versions yet; a send makes it a station folder.</summary>
        New,

        /// <summary>It already holds <c>revit/</c> versions.</summary>
        Station,
    }

    /// <summary>
    /// A folder is a station folder when <c>revit/</c> holds at least one version with a manifest;
    /// a project folder that merely has a <c>revit</c> directory is new. Hidden folders (staging,
    /// set aside) do not count. IO errors and a path that is not valid read as missing.
    /// </summary>
    public static FolderKind Classify(string? stationFolder)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(stationFolder) || !Directory.Exists(stationFolder))
            {
                return FolderKind.Missing;
            }

            string revit = Path.Combine(stationFolder, RevitDirectoryName);
            bool hasVersion = Directory.Exists(revit) && Directory.EnumerateDirectories(revit)
                .Where(model => !Path.GetFileName(model).StartsWith(".", StringComparison.Ordinal))
                .SelectMany(Directory.EnumerateDirectories)
                .Where(version => !Path.GetFileName(version).StartsWith(".", StringComparison.Ordinal))
                .Any(version => File.Exists(Path.Combine(version, "cesium-package.json")));
            return hasVersion ? FolderKind.Station : FolderKind.New;
        }
        catch (Exception ex) when (IsPathError(ex))
        {
            return FolderKind.Missing;
        }
    }

    /// <summary>
    /// The source of the newest version of the model <paramref name="modelId"/> in the station
    /// folder, by <c>createdUtc</c>, or null. Only that model's folders are read (their names end
    /// with the start of the model id, see <see cref="ModelFolderName"/>). Its <c>documentKey</c>
    /// hashes the file path the version was written from, so a Save As copy of the model, which
    /// keeps the model id, can tell that the last version came from another file.
    /// </summary>
    public static CesiumPackageSource? LatestSource(string stationFolder, string modelId)
    {
        if (string.IsNullOrWhiteSpace(stationFolder) || string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        string idPart = modelId.Replace("-", string.Empty);
        idPart = (idPart.Length > 8 ? idPart.Substring(0, 8) : idPart).ToLowerInvariant();
        try
        {
            string revit = Path.Combine(stationFolder, RevitDirectoryName);
            if (!Directory.Exists(revit))
            {
                return null;
            }

            CesiumPackageSource? best = null;
            DateTime bestCreated = DateTime.MinValue;
            IEnumerable<string> versions = Directory.EnumerateDirectories(revit)
                .Where(model =>
                {
                    string name = Path.GetFileName(model).ToLowerInvariant();
                    return name == idPart || name.EndsWith("-" + idPart, StringComparison.Ordinal);
                })
                .SelectMany(Directory.EnumerateDirectories)
                .Where(version => !Path.GetFileName(version).StartsWith(".", StringComparison.Ordinal));
            foreach (string version in versions)
            {
                CesiumPackageManifest? manifest = TryReadManifest(version);
                if (manifest?.Source is null || !string.Equals(manifest.Source.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                DateTime created = manifest.CreatedUtc.ToUniversalTime();
                if (best is null || created > bestCreated)
                {
                    best = manifest.Source;
                    bestCreated = created;
                }
            }

            return best;
        }
        catch (Exception ex) when (IsPathError(ex))
        {
            return null;
        }
    }

    // A typed folder can hold characters .NET Framework rejects (ArgumentException, NotSupportedException
    // for a stray colon) besides the usual IO and access errors.
    private static bool IsPathError(Exception ex) =>
        ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException;

    /// <summary>A version's manifest, or null when it is missing or cannot be read.</summary>
    public static CesiumPackageManifest? TryReadManifest(string versionDirectory)
    {
        string manifestPath = Path.Combine(versionDirectory, "cesium-package.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        try
        {
            return CesiumPackageManifestSerializer.Deserialize(File.ReadAllText(manifestPath));
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException || ex is InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>UTC time first so names sort by age, then the content hash prefix.</summary>
    public static string VersionFolderName(DateTime createdUtc, string? contentHash)
    {
        string time = createdUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string hash = (contentHash ?? string.Empty).Trim();
        hash = hash.Length > 8 ? hash.Substring(0, 8) : hash;
        return hash.Length > 0 ? $"{time}-{hash.ToLowerInvariant()}" : time;
    }

    /// <summary>
    /// Whether <paramref name="folder"/> is a published version:
    /// <c>revit/&lt;model&gt;/&lt;yyyyMMddTHHmmssZ&gt;-&lt;hash8&gt;</c>, with the suffix a same-second export gets.
    /// </summary>
    public static bool IsVersionFolder(string folder)
    {
        return !string.IsNullOrWhiteSpace(folder) && IsVersionDirectory(ToDirectory(folder));
    }

    /// <summary>The published version that strictly contains <paramref name="folder"/>, or null.</summary>
    public static string? FindEnclosingVersionFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return null;
        }

        for (DirectoryInfo? current = ToDirectory(folder).Parent; current != null; current = current.Parent)
        {
            if (IsVersionDirectory(current))
            {
                return current.FullName;
            }
        }

        return null;
    }

    private static bool IsVersionDirectory(DirectoryInfo directory)
    {
        return VersionFolderNamePattern.IsMatch(directory.Name) &&
               string.Equals(directory.Parent?.Parent?.Name, RevitDirectoryName, StringComparison.OrdinalIgnoreCase);
    }

    private static DirectoryInfo ToDirectory(string folder)
    {
        string full = Path.GetFullPath(folder.Trim());
        // Trimming a root would turn C:\ into the drive-relative C: and / into an empty path.
        return string.Equals(full, Path.GetPathRoot(full), StringComparison.OrdinalIgnoreCase)
            ? new DirectoryInfo(full)
            : new DirectoryInfo(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string RevitRoot(string stationFolder)
    {
        if (string.IsNullOrWhiteSpace(stationFolder))
        {
            throw new ArgumentException("A station folder is required.", nameof(stationFolder));
        }

        return Path.Combine(Path.GetFullPath(stationFolder), RevitDirectoryName);
    }
}
