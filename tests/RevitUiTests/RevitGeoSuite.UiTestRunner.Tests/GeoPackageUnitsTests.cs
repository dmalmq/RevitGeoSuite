using Microsoft.Data.Sqlite;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

public class GeoPackageUnitsTests
{
    [Fact]
    public void Counts_categories_and_restrictions_of_every_unit_table()
    {
        string folder = Directory.CreateTempSubdirectory("uitest-gpkg").FullName;
        Write(Path.Combine(folder, "B1.gpkg"), ("unit", new[] { ("shop", "rachi_gai"), ("unenclosedarea", (string?)null) }));
        Write(Path.Combine(folder, "sub", "1F.gpkg"), ("unit", new[] { ("shop", (string?)"rachi_gai") }), ("opening", null));

        UnitCounts counts = GeoPackageUnits.Count(folder);

        Assert.Equal(2, counts.Category("shop"));
        Assert.Equal(1, counts.Category("unenclosedarea"));
        Assert.Equal(2, counts.Restriction("rachi_gai"));
        Assert.Equal(1, counts.Restriction(string.Empty));
        Assert.Equal(2, counts.Tables.Count);
    }

    [Fact]
    public void Counts_rows_per_table_and_vertical_units_with_and_without_to_level()
    {
        string folder = Directory.CreateTempSubdirectory("uitest-gpkg").FullName;
        string file = Path.Combine(folder, "1F.gpkg");
        Write(file, ("unit", new[] { ("stairs", (string?)null), ("escalator", null), ("shop", null) }), ("detail", null));
        using (var connection = new SqliteConnection($"Data Source={file};Pooling=False"))
        {
            connection.Open();
            Execute(connection, "ALTER TABLE unit ADD COLUMN to_level TEXT");
            Execute(connection, "UPDATE unit SET to_level = 'L2' WHERE category = 'stairs'");
            Execute(connection, "UPDATE unit SET to_level = '' WHERE category = 'escalator'");
        }

        Assert.Equal(new Dictionary<string, int> { ["1F.gpkg:unit"] = 3, ["1F.gpkg:detail"] = 0 }, GeoPackageTables.RowCounts(folder));
        Assert.Equal((1, 1), GeoPackageTables.VerticalUnitsToLevel(folder));
    }

    // A minimal GeoPackage: gpkg_contents plus feature tables. A table given null rows has no
    // category/restriction columns and must be skipped.
    private static void Write(string path, params (string Table, (string, string?)[]? Rows)[] tables)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        Execute(connection, "CREATE TABLE gpkg_contents (table_name TEXT PRIMARY KEY, data_type TEXT NOT NULL)");
        foreach ((string table, (string, string?)[]? rows) in tables)
        {
            Execute(connection, $"INSERT INTO gpkg_contents VALUES ('{table}', 'features')");
            if (rows == null)
            {
                Execute(connection, $"CREATE TABLE \"{table}\" (fid INTEGER PRIMARY KEY, name TEXT)");
                continue;
            }

            Execute(connection, $"CREATE TABLE \"{table}\" (fid INTEGER PRIMARY KEY, category TEXT, \"restrict\" TEXT)");
            foreach ((string category, string? restriction) in rows)
            {
                using SqliteCommand insert = connection.CreateCommand();
                insert.CommandText = $"INSERT INTO \"{table}\" (category, \"restrict\") VALUES ($c, $r)";
                insert.Parameters.AddWithValue("$c", category);
                insert.Parameters.AddWithValue("$r", (object?)restriction ?? DBNull.Value);
                insert.ExecuteNonQuery();
            }
        }
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Theory]
    [InlineData(new[] { "fid", "category", "restrict" }, "restrict")]
    [InlineData(new[] { "fid", "category", "restriction" }, "restriction")]
    [InlineData(new[] { "fid", "category" }, null)]
    public void Reads_the_restrict_column_the_export_writes(string[] columns, string? expected) =>
        Assert.Equal(expected, GeoPackageUnits.RestrictionColumn(columns));
}
