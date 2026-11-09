using System;
using System.Globalization;
using System.IO;

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

        string idPart = modelId!.Replace("-", string.Empty);
        idPart = idPart.Length > 8 ? idPart.Substring(0, 8) : idPart;
        string slug = CesiumBuildingIdentity.Slugify(manifest.Building?.Name);
        return slug.Length > 0 ? $"{slug}-{idPart.ToLowerInvariant()}" : idPart.ToLowerInvariant();
    }

    /// <summary>UTC time first so names sort by age, then the content hash prefix.</summary>
    public static string VersionFolderName(DateTime createdUtc, string? contentHash)
    {
        string time = createdUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        string hash = (contentHash ?? string.Empty).Trim();
        hash = hash.Length > 8 ? hash.Substring(0, 8) : hash;
        return hash.Length > 0 ? $"{time}-{hash.ToLowerInvariant()}" : time;
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
