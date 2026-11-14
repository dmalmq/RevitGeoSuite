using Microsoft.Data.Sqlite;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>
/// Counts the unit features of an export by category and restriction, across every GeoPackage in
/// the export folder: each feature table that has both columns counts.
/// </summary>
internal static class GeoPackageUnits
{
    public static UnitCounts Count(string folder)
    {
        var categories = new Dictionary<string, int>(StringComparer.Ordinal);
        var restrictions = new Dictionary<string, int>(StringComparer.Ordinal);
        var tables = new List<string>();
        foreach (string file in Directory.GetFiles(folder, "*.gpkg", SearchOption.AllDirectories))
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            foreach (string table in FeatureTables(connection))
            {
                List<string> columns = Columns(connection, table);
                string? restriction = RestrictionColumn(columns);
                if (!columns.Contains("category") || restriction == null)
                {
                    continue;
                }

                tables.Add($"{Path.GetFileName(file)}:{table}");
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT category, \"{restriction}\" FROM \"{table.Replace("\"", "\"\"")}\"";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    Add(categories, reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
                    Add(restrictions, reader.IsDBNull(1) ? string.Empty : reader.GetString(1));
                }
            }
        }

        return new UnitCounts(categories, restrictions, tables);
    }

    /// <summary>
    /// The unit table's column for the side of the ticket gates (rachi_nai, rachi_gai). The export
    /// writes it as gis_restriction and leaves it out of restrict, which keeps only other restrictions
    /// (ImdfRestrictionNormalizer); restrict and the older restriction are read when gis_restriction is missing.
    /// </summary>
    public static string? RestrictionColumn(IReadOnlyCollection<string> columns) =>
        new[] { "gis_restriction", "restrict", "restriction" }.FirstOrDefault(columns.Contains);

    private static IEnumerable<string> FeatureTables(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT table_name FROM gpkg_contents WHERE data_type = 'features'";
        using SqliteDataReader reader = command.ExecuteReader();
        var names = new List<string>();
        while (reader.Read())
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    private static List<string> Columns(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table.Replace("\"", "\"\"")}\")";
        using SqliteDataReader reader = command.ExecuteReader();
        var columns = new List<string>();
        while (reader.Read())
        {
            columns.Add(reader.GetString(1));
        }

        return columns;
    }

    private static void Add(Dictionary<string, int> counts, string key) => counts[key] = counts.TryGetValue(key, out int count) ? count + 1 : 1;
}

internal static class GeoPackageTables
{
    /// <summary>Rows per feature table, keyed "file.gpkg:table", across every GeoPackage under the folder.</summary>
    public static Dictionary<string, int> RowCounts(string folder)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (string file in Directory.GetFiles(folder, "*.gpkg", SearchOption.AllDirectories))
        {
            using var connection = Open(file);
            foreach (string table in Query(connection, "SELECT table_name FROM gpkg_contents WHERE data_type = 'features'"))
            {
                counts[$"{Path.GetFileName(file)}:{table}"] = int.Parse(Query(connection, $"SELECT COUNT(*) FROM {Quote(table)}").Single());
            }
        }

        return counts;
    }

    /// <summary>Stair and escalator units with and without a to_level, in every unit table that has the column.</summary>
    public static (int With, int Without) VerticalUnitsToLevel(string folder)
    {
        int with = 0, without = 0;
        foreach (string file in Directory.GetFiles(folder, "*.gpkg", SearchOption.AllDirectories))
        {
            using var connection = Open(file);
            foreach (string table in Query(connection, "SELECT table_name FROM gpkg_contents WHERE data_type = 'features'"))
            {
                List<string> columns = Query(connection, $"SELECT name FROM pragma_table_info('{table.Replace("'", "''")}')");
                if (!columns.Contains("category") || !columns.Contains("to_level"))
                {
                    continue;
                }

                string vertical = $"SELECT COUNT(*) FROM {Quote(table)} WHERE (category LIKE 'stair%' OR category LIKE 'escalator%')";
                with += int.Parse(Query(connection, vertical + " AND COALESCE(to_level, '') <> ''").Single());
                without += int.Parse(Query(connection, vertical + " AND COALESCE(to_level, '') = ''").Single());
            }
        }

        return (with, without);
    }

    /// <summary>Stair and escalator units with an empty to_level: "file:table name (category, level_id, revit_key)".</summary>
    public static List<string> VerticalUnitsWithoutToLevel(string folder)
    {
        var units = new List<string>();
        foreach (string file in Directory.GetFiles(folder, "*.gpkg", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            using var connection = Open(file);
            foreach (string table in Query(connection, "SELECT table_name FROM gpkg_contents WHERE data_type = 'features'"))
            {
                List<string> columns = Query(connection, $"SELECT name FROM pragma_table_info('{table.Replace("'", "''")}')");
                if (!columns.Contains("category") || !columns.Contains("to_level"))
                {
                    continue;
                }

                string Column(string name) => columns.Contains(name) ? $"COALESCE({name}, '')" : "''";
                units.AddRange(Query(connection,
                    $"SELECT {Column("name")} || ' (' || COALESCE(category, '') || ', level_id ' || {Column("level_id")} || ', ' || {Column("revit_key")} || ')' FROM {Quote(table)} " +
                    "WHERE (category LIKE 'stair%' OR category LIKE 'escalator%') AND COALESCE(to_level, '') = ''")
                    .Select(unit => $"{Path.GetFileName(file)}:{table} {unit}"));
            }
        }

        return units;
    }

    private static SqliteConnection Open(string file)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }

    private static string Quote(string table) => "\"" + table.Replace("\"", "\"\"") + "\"";

    private static List<string> Query(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        var values = new List<string>();
        while (reader.Read())
        {
            values.Add(Convert.ToString(reader.GetValue(0), System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
        }

        return values;
    }
}

internal sealed record UnitCounts(IReadOnlyDictionary<string, int> Categories, IReadOnlyDictionary<string, int> Restrictions, IReadOnlyList<string> Tables)
{
    public int Category(string name) => Categories.TryGetValue(name, out int count) ? count : 0;

    public int Restriction(string name) => Restrictions.TryGetValue(name, out int count) ? count : 0;
}
