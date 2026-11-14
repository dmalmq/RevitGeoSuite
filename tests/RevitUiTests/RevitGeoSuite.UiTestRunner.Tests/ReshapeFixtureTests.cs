using System.Buffers.Binary;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Xunit;

namespace RevitGeoSuite.UiTestRunner.Tests;

public class ReshapeFixtureTests
{
    private static readonly (double, double)[] Square = { (0, 0), (10, 0), (10, 5), (0, 5), (0, 0) };

    [Fact]
    public void Grows_the_east_edge_of_the_first_unit_of_the_category_and_writes_one_reshaped_shape()
    {
        string folder = Directory.CreateTempSubdirectory("uitest-reshape").FullName;
        WriteUnits(Path.Combine(folder, "station.gpkg"), 6677,
            ("ModelA|room-uid|0", "walkway", Square, false),
            ("ModelA@link|from-link|0", "retail", Square, false),
            ("ModelA|shop-uid|1", "retail", Square, false),
            ("ModelA|shop-uid|0", "retail", Square, true));

        List<FloorUnit> units = ReshapeFixture.FloorUnits(folder);
        FloorUnit shop = ReshapeFixture.Pick(units, "retail")!;
        string path = Path.Combine(folder, "reshape.gis-drift.json");
        File.WriteAllText(path, ReshapeFixture.DriftJson(shop, ReshapeFixture.Reshaped(shop, ReshapeFixture.Grow(shop, 1.0))));
        ReshapeShape shape = ReshapeShape.Read(path);

        Assert.Equal(new[] { "room-uid", "shop-uid" }, units.Select(unit => unit.UniqueId));
        Assert.Equal("shop-uid", shape.UniqueId);
        Assert.Equal(new (double, double)[] { (0, 0), (11, 0), (11, 5), (0, 5), (0, 0) }, shape.Outline[0]);
        Assert.Contains("\"epsg\": 6677", File.ReadAllText(path));
        Assert.Contains("\"model\": \"modela\"", File.ReadAllText(path));
        Assert.Equal("room-uid", ReshapeFixture.Pick(units, "retail", "room-uid")!.UniqueId);

        // shop-uid exports as two units, so neither split nor merge may pick it, as K or as G.
        Dictionary<string, int> parts = ReshapeFixture.FloorParts(folder);
        Assert.Equal(1, parts["room-uid"]);
        Assert.Equal(2, parts["shop-uid"]);
        Assert.False(parts.ContainsKey("from-link"));
        Assert.Equal(new[] { "room-uid" }, ShapeEditPicks.SplitCandidates(units, parts, _ => true).Select(pair => pair.Unit.UniqueId));
        Assert.Empty(ShapeEditPicks.MergePairs(units, parts, _ => true, _ => true));
    }

    [Fact]
    public void Split_halves_a_unit_east_to_west_and_the_east_piece_carries_its_own_values()
    {
        var square = new FloorUnit("m|s|0", "s", "Shop", "L1", "retail", 6677, new[] { Square });
        var holed = square with { Rings = new[] { Square, new (double, double)[] { (1, 1), (2, 1), (2, 2), (1, 1) } } };

        var pieces = ReshapeFixture.Split(square)!;
        JsonObject[] shapes = ReshapeFixture.SplitShapes(square, pieces.West, pieces.East);

        Assert.Equal(25, ReshapeFixture.Area(pieces.West), 9);
        Assert.Equal(25, ReshapeFixture.Area(pieces.East), 9);
        Assert.Equal(new[] { true, false }, shapes.Select(shape => shape["keeps"]!.GetValue<bool>()));
        Assert.Equal(("Shop east", "E", "retail"), (shapes[1]["gdbName"]!.GetValue<string>(), shapes[1]["gdbAltName"]!.GetValue<string>(), shapes[1]["gdbCategory"]!.GetValue<string>()));
        Assert.Null(ReshapeFixture.Split(holed));
    }

    [Fact]
    public void Merge_keeps_the_unit_outline_and_takes_in_the_other_floor_without_one()
    {
        var kept = new FloorUnit("Model|k|0", "k", "K", "L1", "retail", 6677, new[] { Square });

        JsonObject[] shapes = ReshapeFixture.Merged(kept, "g");

        Assert.Equal("Model|g|0", shapes[1]["revitKey"]!.GetValue<string>());
        Assert.Null(shapes[1]["outline"]);
        Assert.All(shapes, shape => Assert.Equal("Model|k|0", shape["group"]!.GetValue<string>()));
        Assert.Equal(0, ReshapeFixture.Gap(kept.Rings, new[] { new (double, double)[] { (10, 0), (12, 0), (12, 5), (10, 5), (10, 0) } }), 9);
        Assert.Equal(1, ReshapeFixture.Gap(kept.Rings, new[] { new (double, double)[] { (11, 0), (12, 0), (12, 5), (11, 5), (11, 0) } }), 9);
    }

    [Fact]
    public void Hand_drawn_unit_carries_the_type_fields_only_when_asked_and_can_name_another_level()
    {
        var unit = new FloorUnit("Model|u|0", "u", "Kiosk", "L1", "retail", 6677, new[] { Square }) { Restriction = "rachi_gai" };

        JsonObject typed = ReshapeFixture.HandDrawn(unit).Single();
        JsonObject untyped = ReshapeFixture.HandDrawn(unit, withType: false, levelId: "nowhere").Single();

        Assert.Equal(("handDrawn", "", "Kiosk (drawn)"), (typed["edit"]!.GetValue<string>(), typed["uniqueId"]!.GetValue<string>(), typed["gdbName"]!.GetValue<string>()));
        Assert.Equal(("retail", "rachi_gai"), (typed["gdbCategory"]!.GetValue<string>(), typed["gdbRestriction"]!.GetValue<string>()));
        Assert.False(untyped.ContainsKey("gdbCategory") || untyped.ContainsKey("gdbRestriction"));
        Assert.Equal("nowhere", untyped["levelId"]!.GetValue<string>());
    }

    [Fact]
    public void Distance_is_zero_for_the_same_shape_however_its_edges_are_split_and_catches_a_two_centimetre_shift()
    {
        var outline = new List<IReadOnlyList<(double X, double Y)>> { Square };
        var split = new List<IReadOnlyList<(double X, double Y)>> { new (double, double)[] { (0, 0), (5, 0), (10, 0), (10, 5), (0, 5), (0, 0) } };
        var shifted = new List<IReadOnlyList<(double X, double Y)>> { Square.Select(point => (point.Item1 + 0.02, point.Item2)).ToArray() };

        Assert.True(ReshapeFixture.Distance(outline, split) < 1e-9);
        Assert.Equal(0.02, ReshapeFixture.Distance(outline, shifted), 6);
    }

    // A minimal GeoPackage with a unit layer; big-endian geometry for the last row, so both byte orders are read.
    private static void WriteUnits(string path, int srs, params (string Key, string Category, (double, double)[] Ring, bool BigEndian)[] rows)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        Execute(connection, "CREATE TABLE gpkg_contents (table_name TEXT PRIMARY KEY, data_type TEXT NOT NULL)");
        Execute(connection, "CREATE TABLE gpkg_geometry_columns (table_name TEXT, column_name TEXT, srs_id INTEGER)");
        Execute(connection, "INSERT INTO gpkg_contents VALUES ('unit', 'features')");
        Execute(connection, $"INSERT INTO gpkg_geometry_columns VALUES ('unit', 'geom', {srs})");
        Execute(connection, "CREATE TABLE unit (fid INTEGER PRIMARY KEY, revit_key TEXT, name TEXT, level_id TEXT, category TEXT, geom BLOB)");
        foreach ((string key, string category, (double, double)[] ring, bool bigEndian) in rows)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO unit (revit_key, name, level_id, category, geom) VALUES ($k, $n, 'L1', $c, $g)";
            insert.Parameters.AddWithValue("$k", key);
            insert.Parameters.AddWithValue("$n", category + " name");
            insert.Parameters.AddWithValue("$c", category);
            insert.Parameters.AddWithValue("$g", Polygon(ring, bigEndian));
            insert.ExecuteNonQuery();
        }
    }

    private static byte[] Polygon((double X, double Y)[] ring, bool bigEndian)
    {
        var bytes = new List<byte> { (byte)'G', (byte)'P', 0, 0b0000_0011, 0, 0, 0, 0 };
        bytes.AddRange(new byte[32]);
        bytes.Add(bigEndian ? (byte)0 : (byte)1);
        bytes.AddRange(UInt(3, bigEndian));
        bytes.AddRange(UInt(1, bigEndian));
        bytes.AddRange(UInt((uint)ring.Length, bigEndian));
        foreach ((double x, double y) in ring)
        {
            bytes.AddRange(Double(x, bigEndian));
            bytes.AddRange(Double(y, bigEndian));
        }

        return bytes.ToArray();
    }

    private static byte[] UInt(uint value, bool bigEndian)
    {
        var data = new byte[4];
        if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(data, value); else BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        return data;
    }

    private static byte[] Double(double value, bool bigEndian)
    {
        var data = new byte[8];
        if (bigEndian) BinaryPrimitives.WriteDoubleBigEndian(data, value); else BinaryPrimitives.WriteDoubleLittleEndian(data, value);
        return data;
    }

    private static void Execute(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    [Fact]
    public void Moves_only_the_east_edge_and_leaves_a_notch_elsewhere_where_it_is()
    {
        // The east edge is slightly skewed (10.0 and 10.2); the notch at x 6 to 7 stays put.
        var ring = new List<(double X, double Y)> { (0, 0), (10, 0), (10.2, 10), (7, 10), (7, 9), (6, 9), (6, 10), (0, 10), (0, 0) };
        var unit = new FloorUnit("m|n|0", "n", "notched", "L1", "walkway", 6677, new[] { (IReadOnlyList<(double X, double Y)>)ring });

        Assert.Equal(new[] { (0.0, 0.0), (11.0, 0.0), (11.2, 10.0), (7.0, 10.0), (7.0, 9.0), (6.0, 9.0), (6.0, 10.0), (0.0, 10.0), (0.0, 0.0) }, ReshapeFixture.Grow(unit, 1)[0]);
    }

    [Fact]
    public void Shrink_cuts_a_strip_off_the_east_side_and_keeps_only_the_holes_west_of_it()
    {
        var ring = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10), (0, 10), (0, 0) };
        var west = new List<(double X, double Y)> { (2, 2), (3, 2), (3, 3), (2, 3), (2, 2) };
        var east = new List<(double X, double Y)> { (9.2, 4), (9.6, 4), (9.6, 4.4), (9.2, 4.4), (9.2, 4) };
        var unit = new FloorUnit("m|n|0", "n", "holed", "L1", "walkway", 6677, new[] { (IReadOnlyList<(double X, double Y)>)ring, west, east });

        ShrunkOutline shrunk = ReshapeFixture.ShrinkEast(unit, 1);

        Assert.Equal(2, shrunk.Crossings);
        Assert.Equal(1, shrunk.DroppedHoles);
        Assert.Equal(2, shrunk.Rings.Count);
        Assert.Equal(west, shrunk.Rings[1]);
        Assert.Equal(9.0, shrunk.Rings[0].Max(point => point.X));
        Assert.Equal(90.0, ReshapeFixture.Area(shrunk.Rings[0]), 6);
    }

    [Fact]
    public void Names_the_drift_file_as_Kanizu_does()
    {
        Assert.Equal("8d7f3c1e-0000.gis-drift.json", GisStandardRun.DriftFileName("8d7f3c1e-0000"));
        Assert.Equal("a_b_c.gis-drift.json", GisStandardRun.DriftFileName("a.b/c"));
        Assert.Equal("___.gis-drift.json", GisStandardRun.DriftFileName("駅é_"));
    }

    [Fact]
    public void Split_takes_a_line_the_outline_crosses_only_twice()
    {
        // A U open to the east: the middle line X = 5 cuts both arms, so the ring crosses it four times.
        var u = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 4), (4, 4), (4, 6), (10, 6), (10, 10), (0, 10), (0, 0) };
        var unit = new FloorUnit("m|u|0", "u", "U", "L1", "walkway", 6677, new[] { (IReadOnlyList<(double X, double Y)>)u });
        Assert.Equal(4, ReshapeFixture.CutCrossings(u, 5));

        var square = new FloorUnit("m|s|0", "s", "S", "L1", "walkway", 6677, new[] { Square });
        Assert.NotNull(ReshapeFixture.Split(square));

        // Other lines still find a clean cut: the U's solid west part is crossed twice.
        SplitCut cut = ReshapeFixture.Split(unit)!;
        Assert.Equal('X', cut.Axis);
        Assert.True(cut.At < 4);
        Assert.Equal(88, ReshapeFixture.Area(cut.West) + ReshapeFixture.Area(cut.East), 6);
    }
}
