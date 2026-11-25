using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RevitGeoSuite.Core.Plateau.Tiles3D;

/// <summary>
/// Disk cache for downloaded PLATEAU 3D Tiles. Files are keyed by absolute URL hash so a
/// tileset.json and its .b3dm children land in the same per-dataset folder.
/// </summary>
public sealed class PlateauTilesetCache
{
    private readonly string root;
    private readonly string? legacyRoot;

    public PlateauTilesetCache(string? rootOverride = null, string? legacyRootOverride = null)
    {
        if (string.IsNullOrWhiteSpace(rootOverride))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            root = Path.Combine(localAppData, "KanizuRevit", "PlateauOnline");
            legacyRoot = string.IsNullOrWhiteSpace(legacyRootOverride)
                ? Path.Combine(localAppData, "RevitGeoSuite", "PlateauOnline")
                : legacyRootOverride!.Trim();
        }
        else
        {
            root = rootOverride!.Trim();
            legacyRoot = string.IsNullOrWhiteSpace(legacyRootOverride)
                ? null
                : legacyRootOverride!.Trim();
        }

        Directory.CreateDirectory(root);
    }

    public string GetDatasetFolder(string areaCode, string typeEn, string? lod, bool? texture)
    {
        string variant = $"{typeEn}-lod{lod ?? "0"}-{(texture == true ? "tex" : "notex")}";
        string folder = Path.Combine(root, areaCode, variant);
        Directory.CreateDirectory(folder);
        return folder;
    }

    public string GetFilePath(string datasetFolder, Uri url)
    {
        string hash = ComputeHash(url.AbsoluteUri);
        string extension = Path.GetExtension(url.AbsolutePath);
        if (string.IsNullOrEmpty(extension)) extension = ".bin";
        return Path.Combine(datasetFolder, hash + extension);
    }

    public bool TryGet(string datasetFolder, Uri url, out string path)
    {
        string currentPath = GetFilePath(datasetFolder, url);
        if (File.Exists(currentPath))
        {
            path = currentPath;
            return true;
        }

        if (TryResolveLegacyFilePath(datasetFolder, url, out string legacyPath) && File.Exists(legacyPath))
        {
            path = legacyPath;
            return true;
        }

        path = currentPath;
        return false;
    }

    public void Store(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }

    private bool TryResolveLegacyFilePath(string datasetFolder, Uri url, out string legacyPath)
    {
        legacyPath = string.Empty;
        if (string.IsNullOrWhiteSpace(legacyRoot))
        {
            return false;
        }

        string normalizedRoot = Path.GetFullPath(root)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        string normalizedFolder = Path.GetFullPath(datasetFolder);
        if (!normalizedFolder.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string relativeFolder = normalizedFolder.Substring(normalizedRoot.Length);
        string legacyDatasetFolder = Path.Combine(legacyRoot!, relativeFolder);
        legacyPath = GetFilePath(legacyDatasetFolder, url);
        return true;
    }

    private static string ComputeHash(string text)
    {
        using SHA1 sha = SHA1.Create();
        byte[] hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
        StringBuilder sb = new StringBuilder(hashBytes.Length * 2);
        foreach (byte b in hashBytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
