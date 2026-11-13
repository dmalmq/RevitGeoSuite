using System.Buffers.Binary;
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
        File.WriteAllText(path, ReshapeFixture.DriftJson(shop, ReshapeFixture.Grow(shop, 1.0)));
        ReshapeShape shape = ReshapeShape.Read(path);

        Assert.Equal(new[] { "room-uid", "shop-uid" }, units.Select(unit => unit.UniqueId));
        Assert.Equal("shop-uid", shape.UniqueId);
        Assert.Equal(new (double, double)[] { (0, 0), (11, 0), (11, 5), (0, 5), (0, 0) }, shape.Outline[0]);
        Assert.Contains("\"epsg\": 6677", File.ReadAllText(path));
        Assert.Contains("\"model\": \"modela\"", File.ReadAllText(path));
        Assert.Equal("room-uid", ReshapeFixture.Pick(units, "retail", "room-uid")!.UniqueId);
    }

    [Fact]
    public void Shrink_cuts_a_band_off_the_east_side_also_when_the_east_end_is_a_single_corner()
    {
        var diamond = new FloorUnit("m|d|0", "d", "diamond", "L1", "retail", 6677, new[] { new (double, double)[] { (0, 0), (5, -5), (10, 0), (5, 5), (0, 0) } });
        var square = new FloorUnit("m|s|0", "s", "square", "L1", "retail", 6677, new[] { Square });

        List<List<(double X, double Y)>> cut = ReshapeFixture.ShrinkEast(diamond, 1.5);

        Assert.Equal(new (double, double)[] { (0, 0), (5, -5), (8.5, -1.5), (8.5, 1.5), (5, 5), (0, 0) }, cut[0]);
        Assert.True(ReshapeFixture.Distance(ReshapeFixture.ShrinkEast(square, 1.5), ReshapeFixture.Grow(square, -1.5)) < 1e-9);
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
}
