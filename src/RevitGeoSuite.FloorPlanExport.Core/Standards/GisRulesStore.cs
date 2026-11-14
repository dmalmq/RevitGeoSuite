using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Newtonsoft.Json.Linq;

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

    /// <summary>Called after the file is read for a write, before the operations are applied. Lets a test play another PC's save.</summary>
    public Action? AfterRead { get; set; }

    public Func<GisRulesChanged> Stamp { get; set; } = DefaultStamp;

    /// <summary>A temporary file older than this, measured against the share's clock, is left over from a crashed write and is removed.</summary>
    public TimeSpan StaleTempAge { get; set; } = TimeSpan.FromHours(1);

    /// <summary>A lock file older than this, measured against the share's clock, is left over from a crashed write and may be taken over.</summary>
    public TimeSpan StaleLockAge { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>This PC's clock. Only written into the lock for display; ages are measured on the share's clock.</summary>
    public Func<DateTime> UtcNow { get; set; } = () => DateTime.UtcNow;

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
/// Reads and writes one <c>gis-rules.json</c>. A write holds <c>gis-rules.lock</c> beside it while it
/// applies operations to a fresh read, swaps the file in through a temporary file and reads it
/// again, so no other PC's save can fall between its read and its write.
/// </summary>
public sealed class GisRulesStore
{
    public const string LockFileName = "gis-rules.lock";

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

        GisRulesFile current = TryLoad() ?? new GisRulesFile();
        if (operations.All(operation => operation.IsReflectedIn(current)))
        {
            return current;
        }

        string token = Guid.NewGuid().ToString("N");
        AcquireLock(token);
        try
        {
            // A RevitGeoSuite from before the lock does not take it, so its write can still replace ours.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                GisRulesFile file = TryLoad() ?? new GisRulesFile();
                if (operations.All(operation => operation.IsReflectedIn(file)))
                {
                    return file;
                }

                _options.AfterRead?.Invoke();
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
        }
        finally
        {
            ReleaseLock(token);
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

        string probe = NewTempPath(directory);
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

    private string LockPath => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!, LockFileName);

    private void AcquireLock(string token)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(LockPath)!);
        string body = new JObject
        {
            ["machine"] = Environment.MachineName,
            ["user"] = Environment.UserName,
            ["utc"] = _options.UtcNow().ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture),
            ["token"] = token,
        }.ToString(Newtonsoft.Json.Formatting.None);

        for (int failedTries = 1; ; failedTries++)
        {
            if (TryCreateLock(body))
            {
                return;
            }

            DateTime? shareNow = ShareUtcNow(System.IO.Path.GetDirectoryName(LockPath)!);
            if (shareNow.HasValue && shareNow.Value - File.GetLastWriteTimeUtc(LockPath) > _options.StaleLockAge)
            {
                TryDelete(LockPath);
                if (TryCreateLock(body))
                {
                    return;
                }
            }

            if (failedTries >= _options.WriteAttempts)
            {
                throw new IOException($"{Path} is being saved by {DescribeLockHolder()}. The rules were not saved; try again in a moment.");
            }

            _options.Sleep(_options.BackoffMilliseconds(failedTries));
        }
    }

    /// <summary>Creates the lock file; false when another writer holds it. CreateNew is atomic on an SMB share.</summary>
    private bool TryCreateLock(string body)
    {
        try
        {
            using FileStream stream = new(LockPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            byte[] bytes = new UTF8Encoding(false).GetBytes(body);
            stream.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (IOException) when (File.Exists(LockPath))
        {
            return false;
        }
    }

    /// <summary>Deletes the lock only while it is still ours, so a lock another PC took over as stale stays.</summary>
    private void ReleaseLock(string token)
    {
        try
        {
            if (File.Exists(LockPath) && File.ReadAllText(LockPath, Encoding.UTF8).Contains(token))
            {
                File.Delete(LockPath);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
        }
    }

    private string DescribeLockHolder()
    {
        try
        {
            JObject holder = JObject.Parse(File.ReadAllText(LockPath, Encoding.UTF8));
            string user = holder.Value<string>("user") ?? string.Empty;
            string machine = holder.Value<string>("machine") ?? string.Empty;
            string utc = holder.Value<string>("utc") ?? string.Empty;
            if (user.Length > 0 || machine.Length > 0)
            {
                return $"{(user.Length > 0 ? user : "someone")} on {(machine.Length > 0 ? machine : "another PC")}" + (utc.Length > 0 ? $" (since {utc})" : string.Empty);
            }
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is Newtonsoft.Json.JsonException)
        {
        }

        return $"another PC ({LockPath} exists)";
    }

    private void Write(GisRulesFile file)
    {
        string directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Path))!;
        Directory.CreateDirectory(directory);
        RemoveStaleTemps(directory);

        string temp = NewTempPath(directory);
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

    /// <summary>
    /// The share's clock, read as the write time of a probe file created in <paramref name="directory"/>,
    /// because PCs' clocks drift from the share and from each other. Null when no probe can be written.
    /// </summary>
    private static DateTime? ShareUtcNow(string directory)
    {
        string probe = NewTempPath(directory);
        try
        {
            File.WriteAllText(probe, string.Empty);
            return File.GetLastWriteTimeUtc(probe);
        }
        catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
        {
            return null;
        }
        finally
        {
            TryDelete(probe);
        }
    }

    private static string NewTempPath(string directory) =>
        System.IO.Path.Combine(directory, TempPrefix + Guid.NewGuid().ToString("N").Substring(0, 8) + TempSuffix);

    private void RemoveStaleTemps(string directory)
    {
        DateTime? shareNow = ShareUtcNow(directory);
        if (!shareNow.HasValue)
        {
            return;
        }

        try
        {
            foreach (string stale in Directory.EnumerateFiles(directory, TempPrefix + "*" + TempSuffix))
            {
                if (shareNow.Value - File.GetLastWriteTimeUtc(stale) > _options.StaleTempAge)
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
