using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace RevitGeoSuite.Core.Plateau.Catalog;

/// <summary>
/// Disk-backed response cache with TTL. Stores string payloads (typically JSON) keyed by a
/// logical name. Supports stale fallback: when the primary fetch fails and a stale cache entry
/// exists, the caller can serve it with a warning rather than failing outright.
/// </summary>
public sealed class ResponseCache
{
    private readonly string cacheRoot;
    private readonly string? legacyCacheRoot;

    public ResponseCache(string? rootOverride = null, string? legacyRootOverride = null)
    {
        if (string.IsNullOrWhiteSpace(rootOverride))
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            cacheRoot = Path.Combine(localAppData, "KanizuRevit", "Cache");
            legacyCacheRoot = string.IsNullOrWhiteSpace(legacyRootOverride)
                ? Path.Combine(localAppData, "RevitGeoSuite", "Cache")
                : legacyRootOverride!.Trim();
        }
        else
        {
            cacheRoot = rootOverride!.Trim();
            legacyCacheRoot = string.IsNullOrWhiteSpace(legacyRootOverride)
                ? null
                : legacyRootOverride!.Trim();
        }

        Directory.CreateDirectory(cacheRoot);
    }

    public Task<string?> TryGetAsync(string key, TimeSpan maxAge)
    {
        string? content = TryReadFresh(cacheRoot, key, maxAge);
        if (content is null && !string.IsNullOrWhiteSpace(legacyCacheRoot))
        {
            content = TryReadFresh(legacyCacheRoot!, key, maxAge);
        }

        return Task.FromResult(content);
    }

    public string? TryGetStale(string key)
    {
        string? content = TryReadStale(cacheRoot, key);
        if (content is null && !string.IsNullOrWhiteSpace(legacyCacheRoot))
        {
            content = TryReadStale(legacyCacheRoot!, key);
        }

        return content;
    }

    public Task StoreAsync(string key, string content)
    {
        string path = GetPath(cacheRoot, key);
        string metaPath = path + ".meta";

        try
        {
            Directory.CreateDirectory(cacheRoot);
            File.WriteAllText(path, content, Encoding.UTF8);
            File.WriteAllText(metaPath, DateTime.UtcNow.ToString("o"));
        }
        catch
        {
            // Best-effort — cache write failure is non-fatal.
        }

        return Task.CompletedTask;
    }

    private static string? TryReadFresh(string root, string key, TimeSpan maxAge)
    {
        string path = GetPath(root, key);
        string metaPath = path + ".meta";

        if (!File.Exists(path) || !File.Exists(metaPath))
            return null;

        try
        {
            string metaText = File.ReadAllText(metaPath).Trim();
            if (DateTime.TryParse(metaText, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime fetchedUtc))
            {
                if (DateTime.UtcNow - fetchedUtc > maxAge)
                    return null;
            }

            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryReadStale(string root, string key)
    {
        string path = GetPath(root, key);
        if (!File.Exists(path))
            return null;

        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch
        {
            return null;
        }
    }

    private static string GetPath(string root, string key)
    {
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        StringBuilder sb = new(hash.Length * 2);
        foreach (byte b in hash) sb.Append(b.ToString("x2"));
        return Path.Combine(root, sb.ToString() + ".json");
    }
}
