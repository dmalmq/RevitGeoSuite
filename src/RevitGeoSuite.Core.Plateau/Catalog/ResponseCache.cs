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

    public ResponseCache(string? rootOverride = null)
    {
        cacheRoot = rootOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RevitGeoSuite", "Cache");
        Directory.CreateDirectory(cacheRoot);
    }

    public Task<string?> TryGetAsync(string key, TimeSpan maxAge)
    {
        string path = GetPath(key);
        string metaPath = path + ".meta";

        if (!File.Exists(path) || !File.Exists(metaPath))
            return Task.FromResult<string?>(null);

        try
        {
            string metaText = File.ReadAllText(metaPath).Trim();
            if (DateTime.TryParse(metaText, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime fetchedUtc))
            {
                if (DateTime.UtcNow - fetchedUtc > maxAge)
                    return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>(File.ReadAllText(path, Encoding.UTF8));
        }
        catch
        {
            return Task.FromResult<string?>(null);
        }
    }

    public string? TryGetStale(string key)
    {
        string path = GetPath(key);
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

    public Task StoreAsync(string key, string content)
    {
        string path = GetPath(key);
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

    private string GetPath(string key)
    {
        using SHA256 sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
        StringBuilder sb = new(hash.Length * 2);
        foreach (byte b in hash) sb.Append(b.ToString("x2"));
        return Path.Combine(cacheRoot, sb.ToString() + ".json");
    }
}
