using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// Reads and judges an A3 export (RevitGeoSuite #45): the level, unit and opening rows of every
/// GeoPackage under a folder, and the export's gis-fields.json. Each pass rule is a pure function of
/// those rows, so the rules are tested without Revit.
/// </summary>
internal static class A3Fixture
{
    public const string TestDoorName = "A3 test door";
    public const string TestDoor = "automatic";
    public const string TestAccess = "test";
    public const string UnknownLevelName = "ZZ";

    /// <summary>The 7 field list rows the A3 check names, with what each must say.</summary>
    public static readonly IReadOnlyList<FieldExpectation> ExpectedFields = new FieldExpectation[]
    {
        new("unit", "levels_served", Owner: "revit", Kind: "core"),
        new("opening", "door", Owner: "revit", Kind: "parameter", Parameter: "gis_door", Binding: "type"),
        new("opening", "access_con", Owner: "revit", Kind: "parameter", Parameter: "gis_access_con", Binding: "type"),
        new("opening", "name", Owner: "revit", Kind: "parameter", Parameter: "name", Binding: "instance"),
        new("level", "address_id", Owner: "gdb", Kind: "none"),
        new("level", "build_id", Owner: "gdb", Kind: "none"),
        new("fixture", "anchor_id", Owner: "gdb", Kind: "none"),
    };

    /// <summary>Every row of the tables named <paramref name="layer"/> (or ending in _layer) in every GeoPackage, text columns only.</summary>
    public static List<Dictionary<string, string?>> Rows(string folder, string layer)
    {
        var rows = new List<Dictionary<string, string?>>();
        foreach (string file in Directory.GetFiles(folder, "*.gpkg", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            foreach (string table in Tables(connection).Where(table => table.Equals(layer, StringComparison.OrdinalIgnoreCase) || table.EndsWith("_" + layer, StringComparison.OrdinalIgnoreCase)))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT * FROM \"{table.Replace("\"", "\"\"")}\"";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    var row = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase) { ["_file"] = Path.GetFileName(file), ["_table"] = table };
                    for (int index = 0; index < reader.FieldCount; index++)
                    {
                        object value = reader.GetValue(index);
                        if (value is not byte[])
                        {
                            row[reader.GetName(index)] = value is DBNull ? null : Convert.ToString(value, CultureInfo.InvariantCulture);
                        }
                    }

                    rows.Add(row);
                }
            }
        }

        return rows;
    }

    /// <summary>The UniqueId in a revit_key ("model|uniqueId|part"), or empty.</summary>
    public static string UniqueId(IReadOnlyDictionary<string, string?> row) =>
        row.TryGetValue("revit_key", out string? key) && key?.Split('|') is { Length: >= 3 } parts ? parts[1] : string.Empty;

    public static string? Value(IReadOnlyDictionary<string, string?> row, string column) =>
        row.TryGetValue(column, out string? value) && !string.IsNullOrEmpty(value) ? value : null;

    /// <summary>
    /// Rule B for one elevator unit row: "list" when levels_served holds two or more known level ids
    /// in rising elevation, "empty" when it is empty, and otherwise why the list is wrong.
    /// </summary>
    public static string SpanCase(string? levelsServed, IReadOnlyDictionary<string, double> elevationById)
    {
        if (string.IsNullOrWhiteSpace(levelsServed))
        {
            return "empty";
        }

        string[] ids = levelsServed.Split(',').Select(id => id.Trim()).ToArray();
        string[] unknown = ids.Where(id => !elevationById.ContainsKey(id)).ToArray();
        if (unknown.Length > 0)
        {
            return $"names unknown level id(s) {string.Join(", ", unknown)}";
        }

        if (ids.Length < 2)
        {
            return "has fewer than two levels";
        }

        return ids.Zip(ids.Skip(1)).All(pair => elevationById[pair.First] < elevationById[pair.Second]) ? "list" : "is not in rising elevation";
    }

    /// <summary>The fields of gis-fields.json, found anywhere under <paramref name="folder"/>; empty when there is none.</summary>
    public static (string? Path, JsonArray Fields) FieldList(string folder)
    {
        string? path = Directory.GetFiles(folder, "gis-fields.json", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault();
        return path == null ? (null, new JsonArray()) : (path, JsonNode.Parse(File.ReadAllText(path))?["fields"]?.AsArray() ?? new JsonArray());
    }

    /// <summary>Rule F: one line per expected row, null when the row is as expected.</summary>
    public static IEnumerable<(FieldExpectation Expected, string? Problem, string Seen)> CheckFields(JsonArray fields)
    {
        foreach (FieldExpectation expected in ExpectedFields)
        {
            JsonNode? row = fields.FirstOrDefault(field => (string?)field?["layer"] == expected.Layer && (string?)field?["name"] == expected.Name);
            if (row == null)
            {
                yield return (expected, "missing", "none");
                continue;
            }

            var seen = new FieldSeen((string?)row["owner"], (string?)row["source"]?["kind"], (string?)row["source"]?["parameter"], (string?)row["source"]?["binding"]);
            var problems = new List<string>();
            Compare(problems, "owner", expected.Owner, seen.Owner);
            Compare(problems, "source.kind", expected.Kind, seen.Kind);
            if (expected.Parameter != null)
            {
                Compare(problems, "source.parameter", expected.Parameter, seen.Parameter);
            }

            if (expected.Binding != null)
            {
                Compare(problems, "source.binding", expected.Binding, seen.Binding);
            }

            yield return (expected, problems.Count == 0 ? null : string.Join("; ", problems), seen.ToString());
        }
    }

    /// <summary>
    /// Rule E: the export's warning for a door field it could not fill,
    /// "Schema field 'door' on opening element &lt;id&gt; could not resolve its mapped source." (also access_con, name).
    /// </summary>
    public static string[] MissingDoorFieldWarnings(IEnumerable<string> warnings) =>
        warnings.Where(warning => System.Text.RegularExpressions.Regex.IsMatch(warning, @"Schema field '(door|access_con|name)'.*could not resolve its mapped source")).ToArray();

    /// <summary>
    /// The export's shafts: elevator objects whose plan boxes overlap by at least half of the smaller
    /// box, joined transitively. Each shaft lists its objects' indexes, in input order.
    /// </summary>
    public static List<List<int>> Shafts(IReadOnlyList<PlanBox> boxes)
    {
        int[] parent = Enumerable.Range(0, boxes.Count).ToArray();
        int Find(int index) => parent[index] == index ? index : parent[index] = Find(parent[index]);
        for (int a = 0; a < boxes.Count; a++)
        {
            for (int b = a + 1; b < boxes.Count; b++)
            {
                double smaller = Math.Min(boxes[a].Area, boxes[b].Area);
                if (smaller > 0 && boxes[a].Overlap(boxes[b]) >= 0.5 * smaller)
                {
                    parent[Find(b)] = Find(a);
                }
            }
        }

        return Enumerable.Range(0, boxes.Count).GroupBy(Find).Select(group => group.ToList()).OrderBy(group => group[0]).ToList();
    }

    /// <summary>Pairs of objects on the same level whose plan boxes overlap at all: two cars a shaft may have merged.</summary>
    public static List<(int A, int B)> SameLevelOverlaps(IReadOnlyList<PlanBox> boxes, IReadOnlyList<string?> levels)
    {
        var pairs = new List<(int, int)>();
        for (int a = 0; a < boxes.Count; a++)
        {
            for (int b = a + 1; b < boxes.Count; b++)
            {
                if (levels[a] != null && levels[a] == levels[b] && boxes[a].Overlap(boxes[b]) > 0)
                {
                    pairs.Add((a, b));
                }
            }
        }

        return pairs;
    }

    public static bool IsElevatorWarning(string warning) =>
        warning.Contains("Elevator", StringComparison.Ordinal) || warning.Contains("elevator", StringComparison.Ordinal) || warning.Contains("levels_served", StringComparison.Ordinal);

    private static void Compare(List<string> problems, string what, string expected, string? seen)
    {
        if (!string.Equals(expected, seen, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{what} is {seen ?? "(none)"}, expected {expected}");
        }
    }

    private static IEnumerable<string> Tables(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM gpkg_contents";
        using SqliteDataReader reader = command.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
        {
            tables.Add(reader.GetString(0));
        }

        return tables;
    }
}

/// <summary>An elevator object's plan bounding box, in metres.</summary>
internal sealed record PlanBox(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Area => Math.Max(0, MaxX - MinX) * Math.Max(0, MaxY - MinY);

    public double Overlap(PlanBox other) =>
        Math.Max(0, Math.Min(MaxX, other.MaxX) - Math.Max(MinX, other.MinX)) * Math.Max(0, Math.Min(MaxY, other.MaxY) - Math.Max(MinY, other.MinY));
}

internal sealed record FieldExpectation(string Layer, string Name, string Owner, string Kind, string? Parameter = null, string? Binding = null)
{
    public string Key => Layer + "." + Name;
}

internal sealed record FieldSeen(string? Owner, string? Kind, string? Parameter, string? Binding)
{
    public override string ToString() => $"owner {Owner ?? "-"}, kind {Kind ?? "-"}, parameter {Parameter ?? "-"}, binding {Binding ?? "-"}";
}
