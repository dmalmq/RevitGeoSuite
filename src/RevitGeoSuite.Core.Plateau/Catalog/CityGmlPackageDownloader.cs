using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RevitGeoSuite.Core.Plateau.Catalog;

/// <summary>Progress for a multi-file CityGML package download (overall file count + current-file bytes).</summary>
public sealed class CityGmlPackageDownloadProgress
{
    public CityGmlPackageDownloadProgress(int completed, int total, string currentItem, double currentFileFraction)
    {
        Completed = completed;
        Total = total;
        CurrentItem = currentItem ?? string.Empty;
        CurrentFileFraction = currentFileFraction;
    }

    public int Completed { get; }
    public int Total { get; }
    public string CurrentItem { get; }
    public double CurrentFileFraction { get; }
}

/// <summary>Outcome of a CityGML package download: the extracted package root and any soft warnings.</summary>
public sealed class CityGmlPackageDownloadResult
{
    public CityGmlPackageDownloadResult(string folderPath, int filesExtracted, IReadOnlyList<string> warnings)
    {
        FolderPath = folderPath ?? string.Empty;
        FilesExtracted = filesExtracted;
        Warnings = warnings ?? Array.Empty<string>();
    }

    public string FolderPath { get; }
    public int FilesExtracted { get; }
    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Downloads selected CityGML resource ZIPs from geospatial.jp and extracts them into a single
/// per-municipality package root so the existing <c>PlateauFolderScanService</c> (which looks for a
/// top-level <c>udx</c> folder) can scan them. Each resource's leading wrapper folder is stripped so
/// the feature subfolders (and shared <c>codelists</c>/<c>schemas</c>) merge correctly.
/// </summary>
public sealed class CityGmlPackageDownloader
{
    private readonly IPlateauHttpClient http;
    private readonly string downloadsRoot;

    public CityGmlPackageDownloader(IPlateauHttpClient http, string? downloadsRoot = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.downloadsRoot = string.IsNullOrWhiteSpace(downloadsRoot)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RevitGeoSuite",
                "CityGmlDownloads")
            : downloadsRoot!;
    }

    public async Task<CityGmlPackageDownloadResult> DownloadAsync(
        string code,
        string year,
        IReadOnlyList<string> resourceUrls,
        IProgress<CityGmlPackageDownloadProgress>? progress,
        CancellationToken cancellationToken,
        string? areaName = null,
        bool force = false)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new ArgumentException("Municipality code is required.", nameof(code));
        }

        if (resourceUrls is null || resourceUrls.Count == 0)
        {
            throw new ArgumentException("At least one resource URL is required.", nameof(resourceUrls));
        }

        string folderLabel = SanitizeName(BuildFolderLabel(code, year, areaName));
        string root = Path.Combine(downloadsRoot, folderLabel);
        Directory.CreateDirectory(root);
        string markerDir = Path.Combine(root, ".rgs-cache");
        Directory.CreateDirectory(markerDir);

        List<string> warnings = new();
        int total = resourceUrls.Count;

        for (int i = 0; i < total; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string rawUrl = resourceUrls[i];
            Uri url;
            try
            {
                url = new Uri(rawUrl);
            }
            catch (UriFormatException)
            {
                warnings.Add($"Skipped malformed resource URL: {rawUrl}");
                progress?.Report(new CityGmlPackageDownloadProgress(i + 1, total, rawUrl, 1));
                continue;
            }

            string fileName = FileNameFromUrl(url);
            progress?.Report(new CityGmlPackageDownloadProgress(i, total, fileName, 0));

            string marker = Path.Combine(markerDir, SanitizeName(fileName) + ".done");
            if (!force && File.Exists(marker))
            {
                progress?.Report(new CityGmlPackageDownloadProgress(i + 1, total, fileName, 1));
                continue;
            }

            string tempZip = Path.Combine(Path.GetTempPath(), "rgs-" + Guid.NewGuid().ToString("N") + "-" + SanitizeName(fileName));
            try
            {
                int index = i;
                using (FileStream destination = File.Create(tempZip))
                {
                    await http.DownloadAsync(
                        url,
                        destination,
                        new Progress<double>(fraction => progress?.Report(
                            new CityGmlPackageDownloadProgress(index, total, fileName, fraction))),
                        cancellationToken).ConfigureAwait(false);
                }

                cancellationToken.ThrowIfCancellationRequested();
                ExtractPackage(tempZip, root, warnings, cancellationToken);
                File.WriteAllText(marker, DateTime.UtcNow.ToString("o"));
            }
            catch (OperationCanceledException)
            {
                SafeDelete(tempZip);
                throw;
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to download or extract {fileName}: {ex.Message}");
            }
            finally
            {
                SafeDelete(tempZip);
            }

            progress?.Report(new CityGmlPackageDownloadProgress(i + 1, total, fileName, 1));
        }

        int gmlCount = CountCityGmlFiles(root);
        return new CityGmlPackageDownloadResult(root, gmlCount, warnings);
    }

    /// <summary>Resolves the package folder a download would target (without creating it).</summary>
    public string ResolvePackageFolder(string code, string? year, string? areaName)
    {
        return Path.Combine(downloadsRoot, SanitizeName(BuildFolderLabel(code, year, areaName)));
    }

    /// <summary>
    /// Returns the subset of <paramref name="resourceUrls"/> already present on disk for the target
    /// folder (i.e. previously downloaded and extracted). Used to warn before re-downloading.
    /// </summary>
    public IReadOnlyList<string> GetAlreadyDownloaded(string code, string? year, string? areaName, IReadOnlyList<string> resourceUrls)
    {
        if (resourceUrls is null || resourceUrls.Count == 0)
        {
            return Array.Empty<string>();
        }

        string markerDir = Path.Combine(ResolvePackageFolder(code, year, areaName), ".rgs-cache");
        if (!Directory.Exists(markerDir))
        {
            return Array.Empty<string>();
        }

        List<string> existing = new();
        foreach (string rawUrl in resourceUrls)
        {
            if (string.IsNullOrWhiteSpace(rawUrl))
            {
                continue;
            }

            Uri url;
            try
            {
                url = new Uri(rawUrl);
            }
            catch (UriFormatException)
            {
                continue;
            }

            string marker = Path.Combine(markerDir, SanitizeName(FileNameFromUrl(url)) + ".done");
            if (File.Exists(marker))
            {
                existing.Add(rawUrl);
            }
        }

        return existing;
    }

    /// <summary>Extracts a PLATEAU ZIP into <paramref name="root"/>, stripping a single wrapper folder.</summary>
    internal static void ExtractPackage(string zipPath, string root, List<string> warnings, CancellationToken cancellationToken)
    {
        string rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using ZipArchive archive = ZipFile.OpenRead(zipPath);
        string? wrapper = DetectWrapper(archive);

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string full = entry.FullName.Replace('\\', '/');
            if (full.Length == 0)
            {
                continue;
            }

            string relative = full;
            if (wrapper != null && relative.StartsWith(wrapper + "/", StringComparison.OrdinalIgnoreCase))
            {
                relative = relative.Substring(wrapper.Length + 1);
            }

            if (relative.Length == 0)
            {
                continue;
            }

            string destPath = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!destPath.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add($"Skipped unsafe zip entry: {entry.FullName}");
                continue;
            }

            // Directory entry (trailing slash or empty name).
            if (full.EndsWith("/", StringComparison.Ordinal) || string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(destPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    /// <summary>
    /// Returns the single leading folder shared by every entry when it wraps the package (so it can be
    /// stripped), or null when entries sit at the zip root or there are several top-level folders.
    /// </summary>
    internal static string? DetectWrapper(ZipArchive archive)
    {
        string? only = null;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            string full = entry.FullName.Replace('\\', '/');
            if (full.Length == 0)
            {
                continue;
            }

            int slash = full.IndexOf('/');
            if (slash < 0)
            {
                // A file at the zip root — nothing to strip.
                return null;
            }

            string top = full.Substring(0, slash);
            if (only is null)
            {
                only = top;
            }
            else if (!string.Equals(only, top, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        if (only is null || IsPackageContentFolder(only))
        {
            return null;
        }

        return only;
    }

    private static bool IsPackageContentFolder(string name)
    {
        return name.Equals("udx", StringComparison.OrdinalIgnoreCase)
            || name.Equals("codelists", StringComparison.OrdinalIgnoreCase)
            || name.Equals("schemas", StringComparison.OrdinalIgnoreCase)
            || name.Equals("metadata", StringComparison.OrdinalIgnoreCase)
            || name.Equals("specification", StringComparison.OrdinalIgnoreCase);
    }

    private static int CountCityGmlFiles(string root)
    {
        try
        {
            return Directory
                .EnumerateFiles(root, "*.gml", SearchOption.AllDirectories)
                .Count();
        }
        catch
        {
            return 0;
        }
    }

    private static string FileNameFromUrl(Uri url)
    {
        string name = url.Segments.Length > 0 ? url.Segments[url.Segments.Length - 1] : url.AbsolutePath;
        name = Uri.UnescapeDataString(name.TrimEnd('/'));
        return string.IsNullOrEmpty(name) ? "package.zip" : name;
    }

    // e.g. "13107-2025 (Sumida-ku)" so previously downloaded folders are recognizable.
    internal static string BuildFolderLabel(string code, string? year, string? areaName)
    {
        string baseLabel = string.IsNullOrWhiteSpace(year) ? code : $"{code}-{year}";
        return string.IsNullOrWhiteSpace(areaName) ? baseLabel : $"{baseLabel} ({areaName!.Trim()})";
    }

    private static string SanitizeName(string name)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = name.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0)
            {
                chars[i] = '_';
            }
        }

        return new string(chars);
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort — leftover temp files are harmless.
        }
    }
}
