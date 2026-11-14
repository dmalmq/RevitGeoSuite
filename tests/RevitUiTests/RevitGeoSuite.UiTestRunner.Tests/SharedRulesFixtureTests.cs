using Microsoft.Data.Sqlite;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

public class SharedRulesFixtureTests
{
    private const string Run = @"C:\dev\revit-test\auto\20261114-100000";

    [Fact]
    public void The_scenario_runs_only_when_revit_reports_this_runs_sandbox()
    {
        Assert.Null(Sandbox.Problem(Sandbox.Root(Run), Run));
        Assert.Null(Sandbox.Problem(Sandbox.Root(Run) + @"\", Run));
        Assert.Contains("Install-TestHost", Sandbox.Problem(null, Run));
        Assert.Contains("is not set", Sandbox.Problem("", Run));
        Assert.Contains("not this run's", Sandbox.Problem(@"C:\Users\daniel\AppData\Roaming", Run));
        Assert.Contains("not this run's", Sandbox.Problem(@"C:\dev\revit-test\auto\20261113-090000\appdata", Run));
    }

    [Fact]
    public void A_snapshot_names_added_changed_and_removed_files()
    {
        string folder = Directory.CreateTempSubdirectory("uitest-snapshot").FullName;
        File.WriteAllText(Path.Combine(folder, "settings.json"), "{}");
        Directory.CreateDirectory(Path.Combine(folder, "mapping-rules"));
        File.WriteAllText(Path.Combine(folder, "mapping-rules", "a.json"), "a");
        File.WriteAllText(Path.Combine(folder, "profiles.json"), "p");
        FolderSnapshot before = FolderSnapshot.Take(folder);

        File.WriteAllText(Path.Combine(folder, "settings.json"), "{ }");
        File.Delete(Path.Combine(folder, "profiles.json"));
        File.WriteAllText(Path.Combine(folder, "mapping-rules", "b.json"), "b");

        Assert.Empty(before.DifferencesFrom(before));
        Assert.Equal(
            new[] { @"added mapping-rules\b.json", @"changed settings.json", "removed profiles.json" },
            FolderSnapshot.Take(folder).DifferencesFrom(before));
    }

    [Fact]
    public void A_snapshot_survives_its_text_form_and_an_absent_folder_is_empty()
    {
        string folder = Directory.CreateTempSubdirectory("uitest-snapshot").FullName;
        File.WriteAllText(Path.Combine(folder, "profiles.json"), "p");
        FolderSnapshot taken = FolderSnapshot.Take(folder);

        Assert.Empty(FolderSnapshot.Parse(taken.ToText()).DifferencesFrom(taken));
        Assert.Matches("^[0-9a-f]{64}  profiles.json\n$", taken.ToText());
        Assert.Empty(FolderSnapshot.Take(Path.Combine(folder, "none")).Files);
        Assert.Throws<FormatException>(() => FolderSnapshot.Parse("not a snapshot"));
    }

    [Fact]
    public void The_rules_block_is_read_from_the_manifest()
    {
        string root = Directory.CreateTempSubdirectory("uitest-manifest").FullName;
        Assert.Null(RulesStamp.Read(root));
        File.WriteAllText(Path.Combine(root, "cesium-package.json"), "{\"gis\":{}}");
        Assert.Null(RulesStamp.Read(root));
        File.WriteAllText(Path.Combine(root, "cesium-package.json"), "{\"gis\":{\"rules\":{\"source\":\"station\",\"profile\":\"Station\",\"hash\":\"ab\"}}}");

        Assert.Equal(new RulesStamp("station", "Station", "ab"), RulesStamp.Read(root));
    }

    [Fact]
    public void Station_rules_are_found_by_type_and_exact_match()
    {
        JsonNodeFile file = JsonNodeFile.Parse("""
            {"categoryRules":[{"type":"floorCategory","match":"床 A","value":"retail"},{"type":"roomCategory","match":"Hall","value":"x"}],
             "profiles":[{"name":"Station","targetEpsg":6677,"coordinateMode":"x"}]}
            """);

        Assert.Equal("retail", StationRules.FloorRule(file.Node, "床 A")!["value"]!.GetValue<string>());
        Assert.Null(StationRules.FloorRule(file.Node, "Hall"));
        Assert.Equal("{\"targetEpsg\":6677,\"coordinateMode\":\"x\"}", StationRules.Settings(StationRules.Profile(file.Node, "station")!));
    }

    [Fact]
    public void A_per_pc_rules_file_is_named_and_written_as_mapping_rule_store_does()
    {
        // The name and the text are what RevitGeoSuite's MappingRuleStore.Save wrote for this key and rule.
        Assert.Equal("21d310b3708ac8eeb5269a5d1e38b6dbd5976b2ed5c4f152136e147873d5be6c.json", PerPcRules.FileName(@"path:C:\RUNS\H4\LUMINE-PRISTINE-UITEST.RVT"));
        string store = Directory.CreateTempSubdirectory("uitest-per-pc").FullName;
        string model = Path.Combine(store, "lumine-pristine-uitest.rvt");

        string written = PerPcRules.AddFloorRule(store, model, "H4 seed", "walkway");

        string key = "path:" + Path.GetFullPath(model).ToUpperInvariant();
        Assert.Equal(Path.Combine(store, "mapping-rules", PerPcRules.FileName(key)), written);
        string expected = $$"""
            {
              "ProjectKey": {{System.Text.Json.JsonSerializer.Serialize(key)}},
              "Rules": [
                {
                  "RuleType": 0,
                  "MatchValue": "H4 seed",
                  "ResolvedValue": "walkway"
                }
              ]
            }
            """;
        Assert.True(System.Text.Json.Nodes.JsonNode.DeepEquals(System.Text.Json.Nodes.JsonNode.Parse(expected), System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(written))));
    }

    [Fact]
    public void Adding_a_per_pc_rule_keeps_the_rules_the_file_holds()
    {
        string store = Directory.CreateTempSubdirectory("uitest-per-pc").FullName;
        string model = Path.Combine(store, "model.rvt");
        PerPcRules.AddFloorRule(store, model, "A", "walkway");

        string written = PerPcRules.AddFloorRule(store, model, "B", "retail");

        System.Text.Json.Nodes.JsonArray rules = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(written))!["Rules"]!.AsArray();
        Assert.Equal(new[] { "A", "B" }, rules.Select(rule => rule!["MatchValue"]!.GetValue<string>()));
    }

    [Fact]
    public void Packages_with_the_same_rows_are_equal_whatever_the_fid_order_and_last_change()
    {
        string a = Package("a", lastChange: "2026-11-14T01:00:00Z", rows: new[] { (1, "shop", "x"), (2, "walkway", "y") });
        string b = Package("b", lastChange: "2026-11-14T05:00:00Z", rows: new[] { (7, "walkway", "y"), (3, "shop", "x") });

        Assert.Empty(GeoPackageCompare.Differences(a, b));
        Assert.Equal(new[] { "1F.gpkg:unit (2)" }, GeoPackageCompare.Covered(a));
    }

    [Fact]
    public void A_changed_cell_a_missing_table_and_a_missing_file_are_reported()
    {
        string a = Package("a", rows: new[] { (1, "shop", "x"), (2, "walkway", "y") });
        string b = Package("b", rows: new[] { (1, "shop", "x"), (2, "walkway", "z") });
        string c = Package("c", rows: new[] { (1, "shop", "x"), (2, "walkway", "y") }, extraTable: "detail");
        File.Delete(Path.Combine(a, "2F.gpkg"));
        Package("a", rows: new[] { (1, "shop", "x") }, file: "2F.gpkg");

        List<string> changed = GeoPackageCompare.Differences(a, b);
        List<string> other = GeoPackageCompare.Differences(a, c);

        Assert.Contains(changed, line => line.StartsWith("1F.gpkg:unit: 1 row(s) only in A, 1 only in B.") && line.Contains("walkway\u241Fy") && line.Contains("walkway\u241Fz"));
        Assert.Contains("1F.gpkg:detail: only in B", other);
        Assert.Contains("2F.gpkg: only in A", other);
    }

    private static string Package(string name, string lastChange = "2026-11-14T00:00:00Z", (int Fid, string Category, string Name)[]? rows = null, string? extraTable = null, string file = "1F.gpkg")
    {
        string folder = Path.Combine(Path.GetTempPath(), "uitest-gpkg-compare-" + Environment.ProcessId, name);
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, file);
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        Execute(connection, "CREATE TABLE gpkg_contents (table_name TEXT PRIMARY KEY, data_type TEXT NOT NULL, last_change TEXT NOT NULL)");
        foreach (string table in new[] { "unit", extraTable }.OfType<string>())
        {
            Execute(connection, $"CREATE TABLE {table} (fid INTEGER PRIMARY KEY, category TEXT, name TEXT)");
            Execute(connection, $"INSERT INTO gpkg_contents VALUES ('{table}', 'features', '{lastChange}')");
        }

        foreach ((int fid, string category, string rowName) in rows ?? Array.Empty<(int, string, string)>())
        {
            Execute(connection, $"INSERT INTO unit VALUES ({fid}, '{category}', '{rowName}')");
        }

        return folder;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private sealed record JsonNodeFile(System.Text.Json.Nodes.JsonNode Node)
    {
        public static JsonNodeFile Parse(string json) => new(System.Text.Json.Nodes.JsonNode.Parse(json)!);
    }
}
