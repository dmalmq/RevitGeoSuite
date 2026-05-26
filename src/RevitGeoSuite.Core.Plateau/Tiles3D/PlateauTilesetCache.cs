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

    public PlateauTilesetCache(string? rootOverride = null)
    {
        root = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitGeoSuite", "PlateauOnline");
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
        path = GetFilePath(datasetFolder, url);
        return File.Exists(path);
    }

    public void Store(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
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
