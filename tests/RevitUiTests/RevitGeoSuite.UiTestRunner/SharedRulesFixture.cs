using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// The per-PC floor plan store of a test run. The scenario only runs inside a Revit that
/// Run-UiTests.ps1 -FloorPlanDataSandbox started with REVITGEOSUITE_FLOORPLAN_DATA set to the run's own
/// appdata folder, so the roaming %APPDATA% folder is never read or written by the export.
/// </summary>
internal static class Sandbox
{
    public const string Variable = "REVITGEOSUITE_FLOORPLAN_DATA";
    public const string BeforeFile = "real-folder-before.txt";

    public static string Root(string runFolder) => Path.Combine(runFolder, "appdata");

    public static string Store(string runFolder) => Path.Combine(Root(runFolder), "RevitGeoSuite", "FloorPlanExport");

    public static string RealStore() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RevitGeoSuite", "FloorPlanExport");

    /// <summary>
    /// Why this Revit may not run the scenario, or null when its per-PC store is this run's sandbox.
    /// <paramref name="inRevit"/> is the variable's value in the Revit process as the test host reports it,
    /// null when the installed test host does not report it.
    /// </summary>
    public static string? Problem(string? inRevit, string runFolder)
    {
        if (inRevit == null)
        {
            return "The installed test host does not report REVITGEOSUITE_FLOORPLAN_DATA. Close Revit and run Install-TestHost.ps1, or start with Run-UiTests.ps1 -FloorPlanDataSandbox, which installs it.";
        }

        if (inRevit.Trim().Length == 0)
        {
            return "REVITGEOSUITE_FLOORPLAN_DATA is not set in this Revit, so the export would use the roaming %APPDATA% folder. Start the run with Run-UiTests.ps1 -StartRevit -FloorPlanDataSandbox.";
        }

        return TestPaths.Same(inRevit, Root(runFolder))
            ? null
            : $"REVITGEOSUITE_FLOORPLAN_DATA in Revit is '{inRevit}', not this run's {Root(runFolder)}.";
    }
}

/// <summary>A list of the files under a folder with the SHA-256 of each, to see whether anything in it changed.</summary>
internal sealed class FolderSnapshot
{
    private readonly Dictionary<string, string> files;

    private FolderSnapshot(Dictionary<string, string> files) => this.files = files;

    public IReadOnlyDictionary<string, string> Files => files;

    public static FolderSnapshot Take(string folder)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(folder))
        {
            string root = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                files[Path.GetFullPath(file)[root.Length..].Replace('/', '\\')] = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            }
        }

        return new FolderSnapshot(files);
    }

    /// <summary>Reads the lines "&lt;hash&gt;  &lt;relative path&gt;" that Run-UiTests.ps1 writes.</summary>
    public static FolderSnapshot Parse(string text)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0))
        {
            if (line.Length < 67 || line[64] != ' ' || line[65] != ' ')
            {
                throw new FormatException($"Not a snapshot line: '{line}'.");
            }

            files[line[66..].Replace('/', '\\')] = line[..64].ToLowerInvariant();
        }

        return new FolderSnapshot(files);
    }

    public string ToText() =>
        string.Concat(files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase).Select(pair => $"{pair.Value}  {pair.Key}\n"));

    /// <summary>What differs from <paramref name="before"/>: files added, removed or with other content.</summary>
    public IReadOnlyList<string> DifferencesFrom(FolderSnapshot before)
    {
        var differences = new List<string>();
        foreach ((string path, string hash) in files.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (!before.files.TryGetValue(path, out string? was))
            {
                differences.Add($"added {path}");
            }
            else if (was != hash)
            {
                differences.Add($"changed {path}");
            }
        }

        differences.AddRange(before.files.Keys.Where(path => !files.ContainsKey(path)).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).Select(path => $"removed {path}"));
        return differences;
    }
}

/// <summary>The <c>gis.rules</c> block of a Cesium package's manifest, or its absence.</summary>
internal sealed record RulesStamp(string Source, string Profile, string Hash)
{
    public static RulesStamp? Read(string packageRoot)
    {
        string manifest = Path.Combine(packageRoot, "cesium-package.json");
        if (!File.Exists(manifest))
        {
            return null;
        }

        JsonNode? rules = JsonNode.Parse(File.ReadAllText(manifest, Encoding.UTF8))?["gis"]?["rules"];
        return rules == null
            ? null
            : new RulesStamp(rules["source"]?.GetValue<string>() ?? string.Empty, rules["profile"]?.GetValue<string>() ?? string.Empty, rules["hash"]?.GetValue<string>() ?? string.Empty);
    }

    public override string ToString() => $"source '{Source}', profile '{Profile}', hash {Hash}";
}

/// <summary>Reads a station's gis-rules.json.</summary>
internal static class StationRules
{
    public static JsonNode Read(string stationFolder) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(stationFolder, "gis-rules.json"), Encoding.UTF8)) ?? throw new InvalidDataException("gis-rules.json is empty.");

    public static JsonNode? Profile(JsonNode rules, string name) =>
        rules["profiles"]?.AsArray().FirstOrDefault(profile => string.Equals(profile?["name"]?.GetValue<string>(), name, StringComparison.OrdinalIgnoreCase));

    public static JsonNode? FloorRule(JsonNode rules, string floorType) =>
        rules["categoryRules"]?.AsArray().FirstOrDefault(rule =>
            rule?["type"]?.GetValue<string>() == "floorCategory" && string.Equals(rule["match"]?.GetValue<string>(), floorType, StringComparison.Ordinal));

    /// <summary>A profile's settings without its name, as compact JSON, so two profiles can be compared.</summary>
    public static string Settings(JsonNode profile)
    {
        JsonObject copy = profile.DeepClone().AsObject();
        copy.Remove("name");
        return copy.ToJsonString();
    }
}

/// <summary>
/// Compares the feature tables of two exports row for row. Row order and the fid values are ignored,
/// and so is gpkg_contents.last_change, which holds the time of the export.
/// </summary>
internal static class GeoPackageCompare
{
    private const int ExamplesPerTable = 3;

    /// <summary>The differences between the GeoPackages under two folders, empty when they hold the same rows.</summary>
    public static List<string> Differences(string folderA, string folderB)
    {
        var differences = new List<string>();
        Dictionary<string, string> filesA = Packages(folderA);
        Dictionary<string, string> filesB = Packages(folderB);
        foreach (string name in filesA.Keys.Except(filesB.Keys, StringComparer.OrdinalIgnoreCase))
        {
            differences.Add($"{name}: only in A");
        }

        foreach (string name in filesB.Keys.Except(filesA.Keys, StringComparer.OrdinalIgnoreCase))
        {
            differences.Add($"{name}: only in B");
        }

        foreach (string name in filesA.Keys.Intersect(filesB.Keys, StringComparer.OrdinalIgnoreCase).OrderBy(name => name, StringComparer.Ordinal))
        {
            using SqliteConnection a = Open(filesA[name]);
            using SqliteConnection b = Open(filesB[name]);
            CompareTable(differences, name, "gpkg_contents", a, b, excluded: new[] { "last_change" });
            List<string> tablesA = FeatureTables(a);
            List<string> tablesB = FeatureTables(b);
            foreach (string table in tablesA.Except(tablesB, StringComparer.Ordinal))
            {
                differences.Add($"{name}:{table}: only in A");
            }

            foreach (string table in tablesB.Except(tablesA, StringComparer.Ordinal))
            {
                differences.Add($"{name}:{table}: only in B");
            }

            foreach (string table in tablesA.Intersect(tablesB, StringComparer.Ordinal))
            {
                CompareTable(differences, name, table, a, b, excluded: new[] { "fid" });
            }
        }

        return differences;
    }

    /// <summary>The feature tables and their row counts that the comparison covered, for the report.</summary>
    public static List<string> Covered(string folder)
    {
        var covered = new List<string>();
        foreach ((string name, string path) in Packages(folder).OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            using SqliteConnection connection = Open(path);
            covered.AddRange(FeatureTables(connection).Select(table => $"{name}:{table} ({Rows(connection, table, Columns(connection, table)).Count})"));
        }

        return covered;
    }

    private static Dictionary<string, string> Packages(string folder) =>
        Directory.GetFiles(folder, "*.gpkg", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(folder, file).Replace('/', '\\'), file => file, StringComparer.OrdinalIgnoreCase);

    private static void CompareTable(List<string> differences, string file, string table, SqliteConnection a, SqliteConnection b, string[] excluded)
    {
        string label = $"{file}:{table}";
        List<string> columnsA = Columns(a, table).Where(column => !excluded.Contains(column, StringComparer.OrdinalIgnoreCase)).ToList();
        List<string> columnsB = Columns(b, table).Where(column => !excluded.Contains(column, StringComparer.OrdinalIgnoreCase)).ToList();
        if (!columnsA.SequenceEqual(columnsB, StringComparer.Ordinal))
        {
            differences.Add($"{label}: columns differ (A: {string.Join(", ", columnsA)}; B: {string.Join(", ", columnsB)})");
            return;
        }

        List<string> rowsA = Rows(a, table, columnsA);
        List<string> rowsB = Rows(b, table, columnsB);
        var onlyA = new List<string>();
        var onlyB = new List<string>();
        int indexA = 0, indexB = 0;
        while (indexA < rowsA.Count || indexB < rowsB.Count)
        {
            int order = indexA == rowsA.Count ? 1 : indexB == rowsB.Count ? -1 : string.CompareOrdinal(rowsA[indexA], rowsB[indexB]);
            if (order == 0)
            {
                indexA++;
                indexB++;
            }
            else if (order < 0)
            {
                onlyA.Add(rowsA[indexA++]);
            }
            else
            {
                onlyB.Add(rowsB[indexB++]);
            }
        }

        if (onlyA.Count > 0 || onlyB.Count > 0)
        {
            string Examples(List<string> rows) => string.Join(" | ", rows.Take(ExamplesPerTable).Select(row => row.Length > 240 ? row[..240] + "..." : row));
            differences.Add($"{label}: {onlyA.Count} row(s) only in A, {onlyB.Count} only in B. A: {Examples(onlyA)}. B: {Examples(onlyB)}.");
        }
    }

    private static List<string> FeatureTables(SqliteConnection connection)
    {
        var tables = new List<string>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM gpkg_contents WHERE data_type = 'features' ORDER BY table_name";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }

    private static List<string> Columns(SqliteConnection connection, string table)
    {
        var columns = new List<string>();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info({Quote(table)})";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static List<string> Rows(SqliteConnection connection, string table, List<string> columns)
    {
        var rows = new List<string>();
        if (columns.Count == 0)
        {
            return rows;
        }

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {string.Join(", ", columns.Select(Quote))} FROM {Quote(table)}";
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(string.Join("␟", Enumerable.Range(0, reader.FieldCount).Select(index => Cell(reader, index))));
        }

        rows.Sort(StringComparer.Ordinal);
        return rows;
    }

    private static string Cell(SqliteDataReader reader, int index)
    {
        if (reader.IsDBNull(index))
        {
            return "␀";
        }

        object value = reader.GetValue(index);
        return value switch
        {
            byte[] bytes => "x'" + Convert.ToHexString(bytes) + "'",
            double number => number.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
    }

    private static SqliteConnection Open(string file)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
}
