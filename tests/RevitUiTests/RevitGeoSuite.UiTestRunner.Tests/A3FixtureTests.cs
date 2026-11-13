using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

public class A3FixtureTests
{
    private static readonly Dictionary<string, double> Elevations = new() { ["L-B1"] = -5, ["L-1"] = 0, ["L-2"] = 5 };

    [Theory]
    [InlineData("L-B1,L-1,L-2", "list")]
    [InlineData("L-1, L-2", "list")]
    [InlineData(null, "empty")]
    [InlineData("", "empty")]
    [InlineData("L-2,L-1", "is not in rising elevation")]
    [InlineData("L-1", "has fewer than two levels")]
    [InlineData("L-1,L-9", "names unknown level id(s) L-9")]
    public void Judges_each_elevator_row(string? levelsServed, string expected)
    {
        Assert.Equal(expected, A3Fixture.SpanCase(levelsServed, Elevations));
    }

    [Fact]
    public void Checks_the_seven_field_rows()
    {
        JsonArray fields = JsonNode.Parse("""
            [
              {"layer":"unit","name":"levels_served","owner":"revit","source":{"kind":"core"}},
              {"layer":"opening","name":"door","owner":"revit","source":{"kind":"parameter","parameter":"gis_door","binding":"type"}},
              {"layer":"opening","name":"access_con","owner":"revit","source":{"kind":"parameter","parameter":"gis_access_con","binding":"instance"}},
              {"layer":"opening","name":"name","owner":"revit","source":{"kind":"parameter","parameter":"name","binding":"instance"}},
              {"layer":"level","name":"address_id","owner":"gdb","source":{"kind":"none"}},
              {"layer":"level","name":"build_id","owner":"gdb","source":{"kind":"none"}}
            ]
            """)!.AsArray();

        var results = A3Fixture.CheckFields(fields).ToDictionary(result => result.Expected.Key, result => result.Problem);

        Assert.Equal(7, results.Count);
        Assert.Null(results["unit.levels_served"]);
        Assert.Null(results["opening.door"]);
        Assert.Equal("source.binding is instance, expected type", results["opening.access_con"]);
        Assert.Equal("missing", results["fixture.anchor_id"]);
    }

    [Fact]
    public void Finds_warnings_about_missing_door_fields_only()
    {
        string[] warnings =
        {
            "Door 123 has no value for gis_door.",
            "Opening 5: name is empty.",
            "Elevator 9 (EV) spans fewer than two exported levels, so levels_served is empty. Set gis_levels_served on its type.",
            "The model has no GIS_ views.",
        };

        Assert.Equal(new[] { "Door 123 has no value for gis_door.", "Opening 5: name is empty." }, A3Fixture.MissingDoorFieldWarnings(warnings));
        Assert.True(A3Fixture.IsElevatorWarning(warnings[2]));
    }

    [Fact]
    public void Reads_rows_of_a_layer_across_geopackages()
    {
        string folder = Directory.CreateTempSubdirectory("uitest-a3").FullName;
        Write(Path.Combine(folder, "m_1F_level.gpkg"), "unit", ("id", "u1"), ("category", "elevator"), ("levels_served", "L-1,L-2"), ("revit_key", "model|ev-uid|0"));
        Write(Path.Combine(folder, "m_2F_level.gpkg"), "station_unit", ("id", "u2"), ("category", "elevator"), ("levels_served", null), ("revit_key", "model|ev2-uid|0"));

        var rows = A3Fixture.Rows(folder, "unit");

        Assert.Equal(new[] { "u1", "u2" }, rows.Select(row => A3Fixture.Value(row, "id")));
        Assert.Equal("ev-uid", A3Fixture.UniqueId(rows[0]));
        Assert.Null(A3Fixture.Value(rows[1], "levels_served"));
        Assert.Equal("m_2F_level.gpkg", rows[1]["_file"]);
    }

    private static void Write(string path, string table, params (string Column, string? Value)[] values)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"CREATE TABLE gpkg_contents (table_name TEXT, data_type TEXT); INSERT INTO gpkg_contents VALUES ('{table}', 'features');"
            + $"CREATE TABLE \"{table}\" (geom BLOB, {string.Join(", ", values.Select(value => value.Column + " TEXT"))});"
            + $"INSERT INTO \"{table}\" VALUES (x'00', {string.Join(", ", values.Select(value => value.Value == null ? "NULL" : $"'{value.Value}'"))});";
        command.ExecuteNonQuery();
    }
}
