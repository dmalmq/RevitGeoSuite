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
                if (!columns.Contains("category") || !columns.Contains("restriction"))
                {
                    continue;
                }

                tables.Add($"{Path.GetFileName(file)}:{table}");
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT category, restriction FROM \"{table.Replace("\"", "\"\"")}\"";
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

internal sealed record UnitCounts(IReadOnlyDictionary<string, int> Categories, IReadOnlyDictionary<string, int> Restrictions, IReadOnlyList<string> Tables)
{
    public int Category(string name) => Categories.TryGetValue(name, out int count) ? count : 0;

    public int Restriction(string name) => Restrictions.TryGetValue(name, out int count) ? count : 0;
}
