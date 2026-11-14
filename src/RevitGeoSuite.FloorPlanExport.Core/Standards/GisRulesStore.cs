using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>How a <see cref="GisRulesStore"/> writes. The defaults suit a share; tests shorten the waits.</summary>
public sealed class GisRulesStoreOptions
{
    /// <summary>How many times a write is tried when the share refuses the swap (another PC has the file open).</summary>
    public int WriteAttempts { get; set; } = 5;

    /// <summary>The wait before try <c>n + 1</c>, in milliseconds; 100, 200, 400, 800 by default.</summary>
    public Func<int, int> BackoffMilliseconds { get; set; } = failedTries => Math.Min(100 << (failedTries - 1), 800);

    /// <summary>Swaps the finished temporary file in for the rules file. Replaced by tests to make the share refuse.</summary>
    public Action<string, string> Swap { get; set; } = SwapIn;

    public Action<int> Sleep { get; set; } = milliseconds => Thread.Sleep(milliseconds);

    /// <summary>Called after each write, before the file is read back. Lets a test play another PC's write.</summary>
    public Action? AfterWrite { get; set; }

    public Func<GisRulesChanged> Stamp { get; set; } = DefaultStamp;

    /// <summary>A temporary file older than this is left over from a crashed write and is removed.</summary>
    public TimeSpan StaleTempAge { get; set; } = TimeSpan.FromHours(1);

    private static void SwapIn(string temp, string target)
    {
        if (File.Exists(target))
        {
            File.Replace(temp, target, null);
        }
        else
        {
            File.Move(temp, target);
        }
    }

    private static GisRulesChanged DefaultStamp()
    {
        return new GisRulesChanged
        {
            Utc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            User = Environment.UserName,
            RevitGeoSuite = typeof(GisRulesStore).Assembly.GetName().Version?.ToString() ?? string.Empty,
        };
    }
}

/// <summary>
/// Reads and writes one <c>gis-rules.json</c>. A write applies operations to a fresh read, swaps
/// the file in through a temporary file beside it, then reads it again; if another PC's write
/// replaced ours, the operations are applied once more.
/// </summary>
public sealed class GisRulesStore
{
    private const string TempPrefix = ".gis-rules.";
    private const string TempSuffix = ".tmp";

    private readonly GisRulesStoreOptions _options;

    public GisRulesStore(string path, GisRulesStoreOptions? options = null)
    {
        Path = string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("A path is required.", nameof(path)) : path;
        _options = options ?? new GisRulesStoreOptions();
    }

    public string Path { get; }

    /// <summary>The file, or null when there is none. Throws when it cannot be read or is not a version this build reads.</summary>
    public GisRulesFile? TryLoad()
    {
        return GisRulesFile.TryLoad(Path);
    }

    /// <summary>
    /// Applies <paramref name="operations"/> and returns the file as it was read back. Writes
    /// nothing when the file already holds them. Throws <see cref="IOException"/> when the share
    /// keeps refusing the write or another PC keeps replacing it.
    /// </summary>
    public GisRulesFile Apply(IReadOnlyList<GisRulesOperation> operations)
    {
        if (operations is null)
        {
            throw new ArgumentNullException(nameof(operations));
        }

        for (int attempt = 0; attempt < 2; attempt++)
        {
            GisRulesFile file = TryLoad() ?? new GisRulesFile();
            if (operations.All(operation => operation.IsReflectedIn(file)))
            {
                return file;
            }

            foreach (GisRulesOperation operation in operations)
            {
                operation.ApplyTo(file);
            }

            file.Changed = _options.Stamp();
            Write(file);
            _options.AfterWrite?.Invoke();

            GisRulesFile? readBack = TryLoad();
            if (readBack != null && operations.All(operation => operation.IsReflectedIn(readBack)))
            {
                return readBack;
            }
        }

        throw new IOException($"{Path} was changed by another PC while it was being written. Try again.");
    }

    /// <summary>True when a file can be created beside the rules file. Leaves nothing behind.</summary>
    public bool CanWrite()
    {
        string? directory = System.IO.Path.GetDirectoryName(Path);
        if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
        {
            return false;
        }

        string probe = System.IO.Path.Combine(directory, TempPrefix + Guid.NewGuid().ToString("N").Substring(0, 8) + TempSuffix);
        try
        {
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            TryDelete(probe);
            return false;
        }
    }

    private void Write(GisRulesFile file)
    {
        string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        Directory.CreateDirectory(directory);
        RemoveStaleTemps(directory);

        string temp = System.IO.Path.Combine(directory, TempPrefix + Guid.NewGuid().ToString("N").Substring(0, 8) + TempSuffix);
        try
        {
            File.WriteAllText(temp, file.ToJson(), new UTF8Encoding(false));
            for (int failedTries = 1; ; failedTries++)
            {
                try
                {
                    _options.Swap(temp, Path);
                    return;
                }
                catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && failedTries < _options.WriteAttempts)
                {
                    _options.Sleep(_options.BackoffMilliseconds(failedTries));
                }
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private void RemoveStaleTemps(string directory)
    {
        try
        {
            foreach (string stale in Directory.EnumerateFiles(directory, TempPrefix + "*" + TempSuffix))
            {
                if (DateTime.UtcNow - File.GetLastWriteTimeUtc(stale) > _options.StaleTempAge)
                {
                    TryDelete(stale);
                }
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }
}
