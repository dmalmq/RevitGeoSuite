using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace RevitGeoSuite.PlateauImport;

internal static class PlateauScanSessionCache
{
    private const int MaxPlateauEntries = 2;
    private const int MaxKibanEntries = 2;
    private static readonly object Gate = new object();
    private static readonly Dictionary<string, CacheEntry<PlateauFolderScanResult>> PlateauEntries = new Dictionary<string, CacheEntry<PlateauFolderScanResult>>(StringComparer.Ordinal);
    private static readonly Dictionary<string, CacheEntry<KibanScanResult>> KibanEntries = new Dictionary<string, CacheEntry<KibanScanResult>>(StringComparer.Ordinal);
    private static long nextAccessOrder;
    private static string lastKibanFolderPath = string.Empty;

    public static string LastKibanFolderPath
    {
        get
        {
            lock (Gate)
            {
                return lastKibanFolderPath;
            }
        }
    }

    public static void RememberKibanFolderPath(string folderPath)
    {
        lock (Gate)
        {
            lastKibanFolderPath = folderPath ?? string.Empty;
        }
    }

    public static string BuildPlateauKey(
        string folderPath,
        string searchRootPath,
        bool isRecursivePackageScan,
        IReadOnlyCollection<string> supportedFiles)
    {
        List<string> parts = new List<string>
        {
            "plateau",
            NormalizePath(folderPath),
            NormalizePath(searchRootPath),
            isRecursivePackageScan ? "recursive" : "top-level"
        };
        AppendFileSignatures(parts, supportedFiles);
        return HashParts(parts);
    }

    public static string BuildKibanKey(
        string folderPath,
        IReadOnlyCollection<string> plateauSecondaryMeshCodes,
        IReadOnlyCollection<string>? additionalGreenLandUseTokens,
        IReadOnlyCollection<string> relevantFiles)
    {
        List<string> parts = new List<string>
        {
            "kiban",
            NormalizePath(folderPath),
            BuildNormalizedValueList(plateauSecondaryMeshCodes),
            BuildNormalizedValueList(additionalGreenLandUseTokens)
        };
        AppendFileSignatures(parts, relevantFiles);
        return HashParts(parts);
    }

    public static bool TryGetPlateau(string key, out PlateauFolderScanResult? result)
    {
        lock (Gate)
        {
            if (PlateauEntries.TryGetValue(key, out CacheEntry<PlateauFolderScanResult>? entry))
            {
                entry.AccessOrder = ++nextAccessOrder;
                result = entry.Value;
                return true;
            }
        }

        result = null;
        return false;
    }

    public static void StorePlateau(string key, PlateauFolderScanResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        lock (Gate)
        {
            PlateauEntries[key] = new CacheEntry<PlateauFolderScanResult>(result, ++nextAccessOrder);
            Trim(PlateauEntries, MaxPlateauEntries);
        }
    }

    public static bool TryGetKiban(string key, out KibanScanResult? result)
    {
        lock (Gate)
        {
            if (KibanEntries.TryGetValue(key, out CacheEntry<KibanScanResult>? entry))
            {
                entry.AccessOrder = ++nextAccessOrder;
                result = entry.Value;
                return true;
            }
        }

        result = null;
        return false;
    }

    public static void StoreKiban(string key, KibanScanResult result)
    {
        if (result is null) throw new ArgumentNullException(nameof(result));
        lock (Gate)
        {
            KibanEntries[key] = new CacheEntry<KibanScanResult>(result, ++nextAccessOrder);
            Trim(KibanEntries, MaxKibanEntries);
        }
    }

    internal static void ClearForTests()
    {
        lock (Gate)
        {
            PlateauEntries.Clear();
            KibanEntries.Clear();
            lastKibanFolderPath = string.Empty;
            nextAccessOrder = 0;
        }
    }

    private static void AppendFileSignatures(ICollection<string> parts, IEnumerable<string> files)
    {
        foreach (string path in files
            .Where(file => !string.IsNullOrWhiteSpace(file))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            FileInfo info = new FileInfo(path);
            parts.Add(NormalizePath(path));
            parts.Add(info.Exists ? info.Length.ToString(CultureInfo.InvariantCulture) : "-1");
            parts.Add(info.Exists ? info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "0");
        }
    }

    private static string BuildNormalizedValueList(IEnumerable<string>? values)
    {
        if (values is null)
        {
            return string.Empty;
        }

        return string.Join(
            "|",
            values
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value.Trim())
                .Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal));
    }

    private static string NormalizePath(string path)
    {
        string normalized = path ?? string.Empty;
        try
        {
            normalized = Path.GetFullPath(normalized);
        }
        catch
        {
            normalized = normalized.Trim();
        }

        string root = Path.GetPathRoot(normalized) ?? string.Empty;
        while (normalized.Length > root.Length
            && (normalized.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
                || normalized.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)))
        {
            normalized = normalized.Substring(0, normalized.Length - 1);
        }

        return normalized.ToUpperInvariant();
    }

    private static string HashParts(IEnumerable<string> parts)
    {
        string payload = string.Join("\n", parts);
        using (SHA256 sha256 = SHA256.Create())
        {
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return Convert.ToBase64String(hash);
        }
    }

    private static void Trim<T>(Dictionary<string, CacheEntry<T>> entries, int maxEntries)
    {
        while (entries.Count > maxEntries)
        {
            KeyValuePair<string, CacheEntry<T>> oldest = entries
                .OrderBy(pair => pair.Value.AccessOrder)
                .First();
            entries.Remove(oldest.Key);
        }
    }

    private sealed class CacheEntry<T>
    {
        public CacheEntry(T value, long accessOrder)
        {
            Value = value;
            AccessOrder = accessOrder;
        }

        public T Value { get; }

        public long AccessOrder { get; set; }
    }
}
