using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Data.Sqlite;
using RevitGeoSuite.FloorPlanExport.Core.GeoPackage;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Export;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GeoPackage;

/// <summary>
/// The GIS export of the link contract's sample station as a real GeoPackage: the five layers of
/// <see cref="LayerDefinition"/>, written by <see cref="GpkgWriter"/> and read back through SQLite.
/// The station fixture's <c>gis/tokyo-station.gpkg</c> is a text stand-in that only gives the
/// manifest an artifact to name; this is the test of what the writer puts in such a file, down to
/// the table, column and key names Kanizu matches by.
/// </summary>
/// <remarks>
/// The writer stamps the clock into <c>gpkg_contents.last_change</c>, so its output is checked by
/// content, not byte for byte. Run with REVITGEOSUITE_WRITE_FIXTURES=1 to write the file to
/// Fixtures/Conformance/gis-geopackage, where Kanizu's tests can read a copy; once it is there,
/// the committed file is held to the same checks.
/// </remarks>
public sealed class GeoPackageConformanceTests
{
    private const int Epsg = 6677;
    private const string HostModel = "8d7f3c1e-0000-4000-8000-000000000001";
    private const string LinkedModel = "5a5a5a5a-0000-4000-8000-000000000002";
    private const string LinkInstance = "link-platform-1";
    private const double Easting = -5995.185;
    private const double Northing = -35367.23;
    private const string FixtureFileName = "tokyo-station.gpkg";

    private static readonly string HostUnitKey = $"{HostModel}|floor-concourse|0";
    private static readonly string LinkedUnitKey = $"{LinkedModel}@{LinkInstance}|floor-platform|0";
    private static readonly string StairKey = $"{HostModel}|stair-1|0";

    [Fact]
    public void The_sample_export_is_a_real_GeoPackage_that_reads_back()
    {
        string path = Path.Combine(Path.GetTempPath(), $"rgs-conformance-{Guid.NewGuid():N}.gpkg");
        try
        {
            ExportLayer[] layers = SampleLayers();

            new GpkgWriter().Write(path, Epsg, layers);

            AssertIsTheSample(path, layers);
            if (Environment.GetEnvironmentVariable("REVITGEOSUITE_WRITE_FIXTURES") == "1")
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FixturePath())!);
                File.Copy(path, FixturePath(), overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void The_committed_sample_GeoPackage_holds_what_the_writer_writes()
    {
        if (!File.Exists(FixturePath()))
        {
            // Not generated yet: the test above still checks the writer's own output.
            return;
        }

        AssertIsTheSample(FixturePath(), SampleLayers());
    }

    [Fact]
    public void The_sample_keys_follow_the_revit_key_format()
    {
        Assert.Equal(HostUnitKey, RevitKey.Create(HostModel, "floor-concourse"));
        Assert.Equal(
            LinkedUnitKey,
            RevitKey.Create(RevitKey.QualifyForLinkInstance(LinkedModel, LinkInstance), "floor-platform"));
        Assert.Equal("revit_key", RevitKey.AttributeName);
        Assert.Equal("parent_key", RevitKey.ParentAttributeName);
    }

    private static ExportLayer[] SampleLayers()
    {
        ExportLayer unit = LayerDefinition.CreateUnitLayer();
        unit.AddFeature(new ExportPolygon(
            Rectangle(Easting - 10, Northing - 5, 20, 10),
            new Dictionary<string, object?>
            {
                ["id"] = "unit-concourse",
                ["revit_key"] = HostUnitKey,
                ["category"] = "walkway",
                ["restrict"] = null,
                ["gis_restriction"] = "rachi_nai",
                ["name"] = "コンコース",
                ["alt_name"] = "Concourse",
                ["level_id"] = "gis-1f",
                ["direction"] = null,
                ["to_level"] = null,
                ["levels_served"] = null,
                ["source"] = "Tokyo",
                ["display_point"] = "POINT (-5995.185 -35367.23)",
                ["preview_fill_color"] = null,
            }));
        unit.AddFeature(new ExportPolygon(
            Rectangle(Easting, Northing - 20, 30, 8),
            new Dictionary<string, object?>
            {
                ["id"] = "unit-platform",
                ["revit_key"] = LinkedUnitKey,
                ["category"] = "platform",
                ["restrict"] = null,
                ["gis_restriction"] = "rachi_nai",
                ["name"] = "ホーム",
                ["alt_name"] = null,
                ["level_id"] = "gis-b1f",
                ["direction"] = null,
                ["to_level"] = null,
                ["levels_served"] = null,
                ["source"] = "Platform",
                ["display_point"] = "POINT (-5980.185 -35383.23)",
                ["preview_fill_color"] = null,
            }));

        ExportLayer detail = LayerDefinition.CreateDetailLayer();
        detail.AddFeature(new ExportLineString(
            new LineString2D(new[] { new Point2D(Easting - 2, Northing), new Point2D(Easting + 2, Northing) }),
            new Dictionary<string, object?>
            {
                ["id"] = "detail-step-1",
                ["revit_key"] = $"{HostModel}|stair-1|1",
                ["category"] = "stair",
                ["parent_key"] = StairKey,
                ["level_id"] = "gis-1f",
                ["element_id"] = 5001L,
            }));

        ExportLayer opening = LayerDefinition.CreateOpeningLayer();
        opening.AddFeature(new ExportLineString(
            new LineString2D(new[] { new Point2D(Easting - 10, Northing - 1), new Point2D(Easting - 10, Northing + 1) }),
            new Dictionary<string, object?>
            {
                ["id"] = "opening-gate",
                ["revit_key"] = $"{HostModel}|door-1|0",
                ["category"] = "pedestrian",
                ["level_id"] = "gis-1f",
                ["element_id"] = 2001L,
            }));

        ExportLayer fixture = LayerDefinition.CreateFixtureLayer();
        fixture.AddFeature(new ExportPolygon(
            Rectangle(Easting - 1, Northing - 1, 2, 0.5),
            new Dictionary<string, object?>
            {
                ["id"] = "fixture-gate",
                ["revit_key"] = $"{HostModel}|gate-1|0",
                ["type"] = "ticketgate",
                ["name"] = "中央改札",
                ["alt_name"] = null,
                ["level_id"] = "gis-1f",
                ["source"] = "Tokyo",
                ["display_point"] = "POINT (-5995.185 -35367.98)",
            }));

        ExportLayer level = LayerDefinition.CreateLevelLayer();
        level.AddFeature(new ExportPolygon(
            Rectangle(Easting - 10, Northing - 5, 20, 10),
            new Dictionary<string, object?>
            {
                ["id"] = "gis-1f",
                ["revit_key"] = $"{HostModel}|lv-host-1fl|0",
                ["level_name"] = "1FL",
                ["short_name"] = "1F",
                ["ordinal"] = 1L,
                ["elevation_m"] = 3.3,
            }));

        return new[] { unit, detail, opening, fixture, level };
    }

    private static void AssertIsTheSample(string path, IReadOnlyList<ExportLayer> layers)
    {
        byte[] head = new byte[16];
        using (FileStream stream = File.OpenRead(path))
        {
            Assert.Equal(head.Length, stream.Read(head, 0, head.Length));
            // A database of whole pages, not a line of text standing in for one.
            Assert.True(stream.Length >= 4096, $"{stream.Length} bytes is no SQLite database");
        }

        Assert.Equal(Encoding.ASCII.GetBytes("SQLite format 3\0"), head);

        using SqliteConnection connection = new($"Data Source={path};Mode=ReadOnly;Pooling=False");
        connection.Open();
        Assert.Equal("ok", Scalar(connection, "PRAGMA integrity_check;"));

        // The three reference systems every GeoPackage carries, and the export's own.
        Assert.Equal(new object[] { -1L, 0L, 4326L, (long)Epsg }, Column(connection, "SELECT srs_id FROM gpkg_spatial_ref_sys ORDER BY srs_id;"));
        Assert.Equal("EPSG", Scalar(connection, $"SELECT organization FROM gpkg_spatial_ref_sys WHERE srs_id = {Epsg};"));
        Assert.Equal((long)Epsg, Scalar(connection, $"SELECT organization_coordsys_id FROM gpkg_spatial_ref_sys WHERE srs_id = {Epsg};"));
        string definition = (string)Scalar(connection, $"SELECT definition FROM gpkg_spatial_ref_sys WHERE srs_id = {Epsg};");
        Assert.StartsWith("PROJCS[", definition);

        Assert.Equal(
            new object[] { "detail", "fixture", "level", "opening", "unit" },
            Column(connection, "SELECT table_name FROM gpkg_contents ORDER BY table_name;"));
        Assert.Equal(
            new object[] { "detail", "fixture", "level", "opening", "unit" },
            Column(connection, "SELECT table_name FROM gpkg_geometry_columns ORDER BY table_name;"));

        foreach (ExportLayer layer in layers)
        {
            AssertContents(connection, layer);
            AssertColumns(connection, layer);
            AssertRows(connection, layer);
        }

        // The names Kanizu matches by, spelled out: a rename must fail here, not in a delivered GDB.
        AssertHasColumns(connection, "unit", "revit_key", "category", "restrict", "gis_restriction", "name", "alt_name", "level_id", "direction", "to_level", "levels_served", "display_point");
        AssertHasColumns(connection, "detail", "revit_key", "parent_key", "category", "level_id");
        AssertHasColumns(connection, "opening", "revit_key", "category", "level_id");
        AssertHasColumns(connection, "fixture", "revit_key", "type", "name", "alt_name", "level_id", "display_point");
        AssertHasColumns(connection, "level", "revit_key", "level_name", "short_name", "ordinal", "elevation_m");

        Assert.Equal(
            new object[] { HostUnitKey, LinkedUnitKey },
            Column(connection, "SELECT revit_key FROM unit ORDER BY fid;"));
        Assert.Equal(StairKey, Scalar(connection, "SELECT parent_key FROM detail;"));
        Assert.Equal("コンコース", Scalar(connection, $"SELECT name FROM unit WHERE revit_key = '{HostUnitKey}';"));
        Assert.Equal(3.3, (double)Scalar(connection, "SELECT elevation_m FROM level;"), 9);
        Assert.Equal(1L, Scalar(connection, "SELECT ordinal FROM level;"));
    }

    private static void AssertContents(SqliteConnection connection, ExportLayer layer)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT c.data_type, c.identifier, c.srs_id, c.min_x, c.min_y, c.max_x, c.max_y, " +
            "g.column_name, g.geometry_type_name, g.srs_id, g.z, g.m " +
            "FROM gpkg_contents c JOIN gpkg_geometry_columns g ON g.table_name = c.table_name " +
            "WHERE c.table_name = $table;";
        command.Parameters.AddWithValue("$table", layer.Name);
        using SqliteDataReader reader = command.ExecuteReader();
        Assert.True(reader.Read(), $"{layer.Name} is in gpkg_contents and gpkg_geometry_columns");

        List<Point2D> points = layer.Features.SelectMany(feature => feature.GetAllPoints()).ToList();
        Assert.Equal("features", reader.GetString(0));
        Assert.Equal(layer.Name, reader.GetString(1));
        Assert.Equal(Epsg, reader.GetInt32(2));
        Assert.Equal(points.Min(point => point.X), reader.GetDouble(3), 9);
        Assert.Equal(points.Min(point => point.Y), reader.GetDouble(4), 9);
        Assert.Equal(points.Max(point => point.X), reader.GetDouble(5), 9);
        Assert.Equal(points.Max(point => point.Y), reader.GetDouble(6), 9);
        Assert.Equal(GpkgWriter.DefaultGeometryColumn, reader.GetString(7));
        Assert.Equal(layer.GeometryType == GpkgGeometryType.LineString ? "LINESTRING" : "MULTIPOLYGON", reader.GetString(8));
        Assert.Equal(Epsg, reader.GetInt32(9));
        Assert.Equal(0, reader.GetInt32(10));
        Assert.Equal(0, reader.GetInt32(11));
        Assert.False(reader.Read());
    }

    private static void AssertColumns(SqliteConnection connection, ExportLayer layer)
    {
        Dictionary<string, string> declared = TableColumns(connection, layer.Name);

        Assert.Equal("INTEGER", declared["fid"]);
        Assert.Equal("BLOB", declared[GpkgWriter.DefaultGeometryColumn]);
        Assert.Equal(layer.Attributes.Count + 2, declared.Count);
        foreach (AttributeDefinition attribute in layer.Attributes)
        {
            string expected = attribute.Type switch
            {
                ExportAttributeType.Text => "TEXT",
                ExportAttributeType.Real => "REAL",
                _ => "INTEGER",
            };
            Assert.True(declared.ContainsKey(attribute.Name), $"{layer.Name}.{attribute.Name} is a column");
            Assert.Equal(expected, declared[attribute.Name]);
        }
    }

    private static void AssertRows(SqliteConnection connection, ExportLayer layer)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT * FROM \"{layer.Name}\" ORDER BY fid;";
        using SqliteDataReader reader = command.ExecuteReader();
        foreach (IExportFeature feature in layer.Features)
        {
            Assert.True(reader.Read(), $"{layer.Name} holds every feature");
            AssertGeometry(feature, (byte[])reader.GetValue(reader.GetOrdinal(GpkgWriter.DefaultGeometryColumn)));
            foreach (AttributeDefinition attribute in layer.Attributes)
            {
                int ordinal = reader.GetOrdinal(attribute.Name);
                feature.Attributes.TryGetValue(attribute.Name, out object? expected);
                string what = $"{layer.Name}.{attribute.Name}";
                switch (expected)
                {
                    case null:
                        Assert.True(reader.IsDBNull(ordinal), $"{what} is null");
                        break;
                    case string text:
                        Assert.Equal(text, reader.GetString(ordinal));
                        break;
                    case long whole:
                        Assert.Equal(whole, reader.GetInt64(ordinal));
                        break;
                    case double real:
                        Assert.Equal(real, reader.GetDouble(ordinal), 9);
                        break;
                    default:
                        Assert.Fail($"{what}: the sample writes text, whole numbers and reals only");
                        break;
                }
            }
        }

        Assert.False(reader.Read(), $"{layer.Name} holds no other row");
    }

    /// <summary>A GeoPackage geometry: the GP header with the reference system, then little-endian WKB.</summary>
    private static void AssertGeometry(IExportFeature feature, byte[] blob)
    {
        using MemoryStream stream = new(blob);
        using BinaryReader reader = new(stream);
        Assert.Equal((byte)'G', reader.ReadByte());
        Assert.Equal((byte)'P', reader.ReadByte());
        Assert.Equal((byte)0, reader.ReadByte());
        // Little-endian, no envelope, not empty.
        Assert.Equal((byte)1, reader.ReadByte());
        Assert.Equal(Epsg, reader.ReadInt32());

        switch (feature)
        {
            case ExportPolygon polygons:
                Assert.Equal((byte)1, reader.ReadByte());
                Assert.Equal(6u, reader.ReadUInt32());
                Assert.Equal((uint)polygons.Polygons.Count, reader.ReadUInt32());
                foreach (Polygon2D polygon in polygons.Polygons)
                {
                    Assert.Equal((byte)1, reader.ReadByte());
                    Assert.Equal(3u, reader.ReadUInt32());
                    Assert.Equal((uint)(1 + polygon.InteriorRings.Count), reader.ReadUInt32());
                    AssertPoints(polygon.ExteriorRing, reader);
                    foreach (IReadOnlyList<Point2D> ring in polygon.InteriorRings)
                    {
                        AssertPoints(ring, reader);
                    }
                }

                break;
            case ExportLineString line:
                Assert.Equal((byte)1, reader.ReadByte());
                Assert.Equal(2u, reader.ReadUInt32());
                AssertPoints(line.LineString.Points, reader);
                break;
            default:
                Assert.Fail($"{feature.GetType().Name} is not a sample geometry");
                break;
        }

        Assert.Equal(stream.Length, stream.Position);
    }

    private static void AssertPoints(IReadOnlyList<Point2D> expected, BinaryReader reader)
    {
        Assert.Equal((uint)expected.Count, reader.ReadUInt32());
        foreach (Point2D point in expected)
        {
            Assert.Equal(point.X, reader.ReadDouble());
            Assert.Equal(point.Y, reader.ReadDouble());
        }
    }

    private static void AssertHasColumns(SqliteConnection connection, string table, params string[] columns)
    {
        Dictionary<string, string> declared = TableColumns(connection, table);
        foreach (string column in columns)
        {
            Assert.True(declared.ContainsKey(column), $"{table}.{column} is a column");
        }
    }

    /// <summary>The table's columns with their declared types, by exact name.</summary>
    private static Dictionary<string, string> TableColumns(SqliteConnection connection, string table)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{table}\");";
        using SqliteDataReader reader = command.ExecuteReader();
        Dictionary<string, string> columns = new(StringComparer.Ordinal);
        while (reader.Read())
        {
            columns.Add(reader.GetString(1), reader.GetString(2));
        }

        return columns;
    }

    private static object Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        Assert.NotNull(value);
        return value!;
    }

    private static object[] Column(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        using SqliteDataReader reader = command.ExecuteReader();
        List<object> values = new();
        while (reader.Read())
        {
            values.Add(reader.GetValue(0));
        }

        return values.ToArray();
    }

    private static Polygon2D Rectangle(double west, double south, double width, double height)
    {
        return new Polygon2D(new[]
        {
            new Point2D(west, south),
            new Point2D(west + width, south),
            new Point2D(west + width, south + height),
            new Point2D(west, south + height),
        });
    }

    private static string FixturePath([CallerFilePath] string sourceFile = "")
    {
        return Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(sourceFile)!,
            "..",
            "..",
            "Fixtures",
            "Conformance",
            "gis-geopackage",
            FixtureFileName));
    }
}
