using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;

namespace RevitGeoSuite.FloorPlanExport.Core.Standards;

/// <summary>One conflict answered "Keep Revit's": the field of one feature, and the two values it was answered for.</summary>
public sealed class GisKeepEntry : IEquatable<GisKeepEntry>
{
    public GisKeepEntry(string revitKey, string layer, string field, string revit, string gdb, string keptUtc)
    {
        RevitKey = revitKey ?? string.Empty;
        Layer = layer ?? string.Empty;
        Field = field ?? string.Empty;
        Revit = revit ?? string.Empty;
        Gdb = gdb ?? string.Empty;
        KeptUtc = keptUtc ?? string.Empty;
    }

    public string RevitKey { get; }

    /// <summary>unit, opening, fixture, level, ... as in the drift row.</summary>
    public string Layer { get; }

    /// <summary>The GDB field.</summary>
    public string Field { get; }

    /// <summary>Revit's value when it was kept. Kanizu takes the export's value only while it is exactly this.</summary>
    public string Revit { get; }

    /// <summary>The GDB value Revit's overrides. Kanizu takes Revit's only while the GDB holds exactly this.</summary>
    public string Gdb { get; }

    /// <summary>When the answer was recorded, ISO 8601 UTC to the second.</summary>
    public string KeptUtc { get; }

    /// <summary>Two entries with the same key, layer, field and GDB value answer the same conflict.</summary>
    public bool AnswersSameConflictAs(GisKeepEntry other) =>
        other != null &&
        RevitKey == other.RevitKey &&
        Layer == other.Layer &&
        Field == other.Field &&
        Gdb == other.Gdb;

    public bool Equals(GisKeepEntry? other) =>
        other is not null && AnswersSameConflictAs(other) && Revit == other.Revit && KeptUtc == other.KeptUtc;

    public override bool Equals(object? obj) => Equals(obj as GisKeepEntry);

    public override int GetHashCode() =>
        StringComparer.Ordinal.GetHashCode(string.Join("\u001f", RevitKey, Layer, Field, Revit, Gdb, KeptUtc));
}

/// <summary>
/// <c>gis-drift/&lt;model&gt;.gis-keep.json</c>: the conflicts Review GDB edits answered "Keep
/// Revit's". RevitGeoSuite writes it beside the model's drift file; Kanizu reads it, shows each
/// entry in its Revit models review, and takes Revit's value into the GDB at the next apply, only
/// while the export still holds exactly the entry's <c>revit</c> and the GDB exactly its
/// <c>gdb</c>. Kanizu then drops the entries it took in and the ones that no longer hold.
/// </summary>
/// <remarks>
/// The file has two writers, so each changes only its own part: a rewrite keeps every field and
/// entry this build does not know as it found them, and never replaces a file that changed since
/// it was read. The reader on the other side is Kanizu's <c>kanizu-core/src/revit/keep.rs</c>;
/// the schema, version, file name and field names here are that reader's.
/// </remarks>
public static class GisKeep
{
    public const string Schema = "kanizu.gis-keep";
    public const int Version = 1;

    /// <summary>The folder of the station's drift and keep files.</summary>
    public const string Folder = GisDrift.Folder;

    public const string FileSuffix = ".gis-keep.json";

    private const int WriteAttempts = 3;

    /// <summary>The keep file's name for a model key, as Kanizu names it.</summary>
    public static string FileName(string model) => GisDrift.SafeName(model) + FileSuffix;

    /// <summary>Where the keep file of the model keyed <paramref name="model"/> is in a station folder.</summary>
    public static string PathFor(string stationFolder, string model) =>
        Path.Combine(stationFolder, Folder, FileName(model));

    /// <summary>A time as <see cref="GisKeepEntry.KeptUtc"/> writes it: <c>2026-11-21T09:30:00Z</c>.</summary>
    public static string Timestamp(DateTime time) =>
        time.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    /// <summary>
    /// Why Kanizu would never act on keeping <paramref name="revit"/> over <paramref name="gdb"/>,
    /// or null when it can. Kanizu never writes an empty Revit value over a set GDB value, and
    /// equal values are no conflict.
    /// </summary>
    public static string? WhyNotKept(string revit, string gdb)
    {
        if (revit == gdb)
        {
            return "Revit and the GDB already hold the same value.";
        }

        return revit.Length == 0 && gdb.Length > 0
            ? "Revit's value is empty, and Kanizu never writes an empty value over one the GDB holds."
            : null;
    }

    /// <summary>
    /// The entries that answer <paramref name="rows"/> with Revit's value, and one line for each
    /// row that cannot be answered. A row is answered with the values the drift file lists, the
    /// ones Kanizu compares at its next apply.
    /// </summary>
    public static (IReadOnlyList<GisKeepEntry> Entries, IReadOnlyList<string> Skipped) EntriesFor(
        IEnumerable<GisDriftRow> rows,
        string keptUtc)
    {
        List<GisKeepEntry> entries = new();
        List<string> skipped = new();
        foreach (GisDriftRow row in rows ?? Enumerable.Empty<GisDriftRow>())
        {
            if (row.RevitKey.Length == 0 || row.Field.Length == 0)
            {
                skipped.Add($"{row.Describe()}: the drift file names no element key or field for it, so the answer cannot be recorded.");
                continue;
            }

            string? problem = WhyNotKept(row.Revit, row.Gdb);
            if (problem != null)
            {
                skipped.Add($"{row.Describe()}: {problem}");
                continue;
            }

            GisKeepEntry entry = new(row.RevitKey, row.Layer, row.Field, row.Revit, row.Gdb, keptUtc);
            if (!entries.Any(entry.AnswersSameConflictAs))
            {
                entries.Add(entry);
            }
        }

        return (entries, skipped);
    }

    /// <summary>
    /// The entries of the keep file at <paramref name="path"/>; none when there is no file. Throws
    /// <see cref="FormatException"/> for a file that is not a version 1 keep file of
    /// <paramref name="model"/>.
    /// </summary>
    public static IReadOnlyList<GisKeepEntry> Read(string path, string model)
    {
        if (!File.Exists(path))
        {
            return Array.Empty<GisKeepEntry>();
        }

        return Parse(Decode(File.ReadAllBytes(path)), model);
    }

    /// <summary>The entries of a keep file's JSON text. Entries that name no key or field, or whose values are not values, are left out.</summary>
    public static IReadOnlyList<GisKeepEntry> Parse(string text, string model)
    {
        JObject root = ParseRoot(text, model);
        return EntriesOf(root).Select(EntryOf).Where(entry => entry != null).Select(entry => entry!).ToList();
    }

    /// <summary>
    /// The keep file's JSON after <paramref name="entries"/> are added to
    /// <paramref name="existing"/> (the file's text now, or null without a file). An entry that
    /// answers a conflict the file already answers replaces that answer's Revit value and time;
    /// everything else in the file stays as written.
    /// </summary>
    public static string Merge(string? existing, string model, IEnumerable<GisKeepEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException("A model key is required.", nameof(model));
        }

        JObject root = existing == null
            ? new JObject { ["schema"] = Schema, ["version"] = Version, ["model"] = model, ["entries"] = new JArray() }
            : ParseRoot(existing, model);
        if (root["entries"] is not JArray written)
        {
            written = new JArray();
            root["entries"] = written;
        }

        foreach (GisKeepEntry entry in entries ?? Enumerable.Empty<GisKeepEntry>())
        {
            if (entry.RevitKey.Length == 0 || entry.Field.Length == 0)
            {
                throw new ArgumentException("A keep entry needs an element key and a field.", nameof(entries));
            }

            JObject? same = written.OfType<JObject>().FirstOrDefault(candidate => EntryOf(candidate)?.AnswersSameConflictAs(entry) == true);
            if (same != null)
            {
                same["revit"] = entry.Revit;
                same["keptUtc"] = entry.KeptUtc;
                continue;
            }

            written.Add(new JObject
            {
                ["revitKey"] = entry.RevitKey,
                ["layer"] = entry.Layer,
                ["field"] = entry.Field,
                ["revit"] = entry.Revit,
                ["gdb"] = entry.Gdb,
                ["keptUtc"] = entry.KeptUtc,
            });
        }

        return root.ToString(Formatting.Indented).Replace("\r\n", "\n") + "\n";
    }

    /// <summary>
    /// Adds <paramref name="entries"/> to the keep file at <paramref name="path"/>, through a
    /// temporary file and a rename so a reader never sees half a file. When Kanizu changed the
    /// file meanwhile, the entries are added again to what it holds now. Throws
    /// <see cref="FormatException"/> for a file this build cannot read, which is then left alone.
    /// </summary>
    public static void Add(string path, string model, IReadOnlyCollection<GisKeepEntry> entries)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A keep file path is required.", nameof(path));
        }

        if (entries is null || entries.Count == 0)
        {
            return;
        }

        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        for (int attempt = 0; attempt < WriteAttempts; attempt++)
        {
            byte[]? before = ReadBytes(path);
            string text = Merge(before == null ? null : Decode(before), model, entries);
            string temp = path + ".tmp-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            File.WriteAllText(temp, text, new UTF8Encoding(false));
            try
            {
                byte[]? now = ReadBytes(path);
                if (!SameBytes(before, now))
                {
                    continue;
                }

                if (now != null)
                {
                    File.Replace(temp, path, null);
                }
                else
                {
                    File.Move(temp, path);
                }

                return;
            }
            finally
            {
                if (File.Exists(temp))
                {
                    File.Delete(temp);
                }
            }
        }

        throw new IOException($"The keep file {path} kept changing while it was written. Try again.");
    }

    private static JObject ParseRoot(string text, string model)
    {
        JToken data;
        try
        {
            // Dates stay text: keptUtc, and any value that looks like a date, must be written back as read.
            using JsonTextReader reader = new(new StringReader((text ?? string.Empty).TrimStart('\uFEFF'))) { DateParseHandling = DateParseHandling.None };
            data = JToken.ReadFrom(reader);
        }
        catch (JsonException error)
        {
            throw new FormatException($"The keep file is not JSON: {error.Message}", error);
        }

        if (data is not JObject root || root["schema"] is not JValue { Type: JTokenType.String } schema || (string?)schema != Schema)
        {
            throw new FormatException($"The file is not a Kanizu keep file (schema {Schema}).");
        }

        // Kanizu reads the version as an unsigned integer, so 1.0 is not version 1 here.
        if (root["version"] is not JValue { Type: JTokenType.Integer, Value: long version } || version != Version)
        {
            throw new FormatException($"The keep file is version {root["version"]}; this tool reads and writes version {Version}. Update RevitGeoSuite.");
        }

        string fileModel = root["model"] is JValue { Type: JTokenType.String } named ? (string)named! : string.Empty;
        if (fileModel != model)
        {
            throw new FormatException($"The keep file holds the answers of model '{fileModel}', not '{model}'.");
        }

        if (root["entries"] is { } entries && entries.Type != JTokenType.Array)
        {
            throw new FormatException("The keep file's entries are not a list.");
        }

        return root;
    }

    private static IEnumerable<JObject> EntriesOf(JObject root) =>
        (root["entries"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>();

    /// <summary>An entry as Kanizu reads it, or null for one it leaves out (and keeps as written).</summary>
    private static GisKeepEntry? EntryOf(JObject entry)
    {
        string? revitKey = Text(entry["revitKey"]);
        string? layer = Text(entry["layer"]);
        string? field = Text(entry["field"]);
        string? keptUtc = Text(entry["keptUtc"]);
        string? revit = Value(entry["revit"]);
        string? gdb = Value(entry["gdb"]);
        if (revitKey == null || layer == null || field == null || keptUtc == null || revit == null || gdb == null ||
            revitKey.Length == 0 || field.Length == 0)
        {
            return null;
        }

        return new GisKeepEntry(revitKey, layer, field, revit, gdb, keptUtc);
    }

    /// <summary>A text field: absent is empty; anything but text is not this field.</summary>
    private static string? Text(JToken? token)
    {
        if (token is null)
        {
            return string.Empty;
        }

        return token.Type == JTokenType.String ? (string)token! : null;
    }

    /// <summary>A value as the file may write it: text, null for empty, or a number or flag as its text. A list or an object is no value.</summary>
    private static string? Value(JToken? token)
    {
        if (token is null)
        {
            return string.Empty;
        }

        return token.Type switch
        {
            JTokenType.Null => string.Empty,
            JTokenType.String => (string)token!,
            JTokenType.Boolean => (bool)token ? "true" : "false",
            JTokenType.Integer or JTokenType.Float => Convert.ToString(((JValue)token).Value, CultureInfo.InvariantCulture) ?? string.Empty,
            _ => null,
        };
    }

    private static string Decode(byte[] bytes)
    {
        try
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true).GetString(bytes).TrimStart('\uFEFF');
        }
        catch (ArgumentException error)
        {
            throw new FormatException($"The keep file is not UTF-8: {error.Message}", error);
        }
    }

    private static byte[]? ReadBytes(string path)
    {
        try
        {
            return File.ReadAllBytes(path);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
    }

    private static bool SameBytes(byte[]? left, byte[]? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.Length == right.Length && left.SequenceEqual(right);
    }
}
