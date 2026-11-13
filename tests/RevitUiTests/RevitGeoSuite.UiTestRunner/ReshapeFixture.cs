using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>One floor unit of a GIS export: its revit_key parts and its single polygon in the export CRS (metres).</summary>
internal sealed record FloorUnit(string RevitKey, string UniqueId, string Name, string LevelId, string Category, int Epsg, IReadOnlyList<IReadOnlyList<(double X, double Y)>> Rings)
{
    public string ModelKey => RevitKey.Split('|')[0].ToLowerInvariant();
}

/// <summary>
/// The C# form of c3/ui-tests/make_reshape_fixture.py, so the build PC needs no Python: reads the floor
/// units of a GIS export, moves one unit's easternmost points east, and writes a drift file whose only
/// content is that `reshaped` shape. Also compares outlines for the round trip.
/// </summary>
internal static class ReshapeFixture
{
    /// <summary>
    /// The floor units (revit_key "&lt;model&gt;|&lt;UniqueId&gt;|0", one polygon) of every GeoPackage under
    /// <paramref name="exportFolder"/>, in table and row order.
    /// </summary>
    public static List<FloorUnit> FloorUnits(string exportFolder)
    {
        var units = new List<FloorUnit>();
        foreach (string file in Directory.GetFiles(exportFolder, "*.gpkg", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            foreach ((string table, string column, int srs) in UnitTables(connection))
            {
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT revit_key, name, level_id, category, \"{column.Replace("\"", "\"\"")}\" FROM \"{table.Replace("\"", "\"\"")}\"";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    string key = Text(reader, 0);
                    string[] parts = key.Split('|');
                    if (parts.Length < 3 || parts[^1] != "0" || parts[0].Contains('@') || reader.IsDBNull(4))
                    {
                        continue;
                    }

                    List<List<List<(double, double)>>> polygons = GeoPackageGeometry.Polygons((byte[])reader[4]);
                    if (polygons.Count == 1)
                    {
                        units.Add(new FloorUnit(key, parts[1], Text(reader, 1), Text(reader, 2), Text(reader, 3), srs, polygons[0]));
                    }
                }
            }
        }

        return units;
    }

    /// <summary>The unit <paramref name="uniqueId"/> names, else the first of <paramref name="category"/>; null when none fits.</summary>
    public static FloorUnit? Pick(IEnumerable<FloorUnit> units, string category, string uniqueId = "") =>
        units.FirstOrDefault(unit => uniqueId.Length > 0 ? unit.UniqueId == uniqueId : unit.Category == category);

    /// <summary>The unit's exterior ring with its easternmost points moved <paramref name="growM"/> east (negative: west); holes stay.</summary>
    public static List<List<(double X, double Y)>> Grow(FloorUnit unit, double growM)
    {
        double east = unit.Rings[0].Max(point => point.X);
        var rings = new List<List<(double X, double Y)>>
        {
            unit.Rings[0].Select(point => Math.Abs(point.X - east) < 1e-6 ? (point.X + growM, point.Y) : point).ToList(),
        };
        rings.AddRange(unit.Rings.Skip(1).Select(ring => ring.ToList()));
        return rings;
    }

    public static string DriftJson(FloorUnit unit, IReadOnlyList<IReadOnlyList<(double X, double Y)>> outline, string model = "")
    {
        var coordinates = new JsonArray(outline.Select(ring => (JsonNode)new JsonArray(ring.Select(point => (JsonNode)new JsonArray(point.X, point.Y)).ToArray())).ToArray());
        var file = new JsonObject
        {
            ["schema"] = "kanizu.gis-drift",
            ["version"] = 1,
            ["model"] = model.Length > 0 ? model : unit.ModelKey,
            ["appliedVersion"] = "ui-test",
            ["rows"] = new JsonArray(),
            ["epsg"] = unit.Epsg,
            ["shapes"] = new JsonArray(new JsonObject
            {
                ["edit"] = "reshaped",
                ["revitKey"] = unit.RevitKey,
                ["uniqueId"] = unit.UniqueId,
                ["layer"] = "unit",
                ["label"] = unit.Name,
                ["levelId"] = unit.LevelId,
                ["outline"] = new JsonObject { ["type"] = "Polygon", ["coordinates"] = coordinates },
            }),
        };
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    /// <summary>
    /// The largest distance from a vertex of either outline to the other outline's edges, in the
    /// outlines' units: 0 for the same shape, however each one splits its edges.
    /// </summary>
    public static double Distance(IReadOnlyList<IReadOnlyList<(double X, double Y)>> a, IReadOnlyList<IReadOnlyList<(double X, double Y)>> b) =>
        Math.Max(OneWay(a, b), OneWay(b, a));

    private static double OneWay(IReadOnlyList<IReadOnlyList<(double X, double Y)>> from, IReadOnlyList<IReadOnlyList<(double X, double Y)>> to) =>
        from.SelectMany(ring => ring).Max(point => to.Min(ring => RingDistance(point, ring)));

    private static double RingDistance((double X, double Y) p, IReadOnlyList<(double X, double Y)> ring) =>
        Enumerable.Range(0, ring.Count).Min(index => SegmentDistance(p, ring[index], ring[(index + 1) % ring.Count]));

    private static double SegmentDistance((double X, double Y) p, (double X, double Y) a, (double X, double Y) b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double length = dx * dx + dy * dy;
        double t = length == 0 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length, 0, 1);
        return Math.Sqrt(Math.Pow(p.X - (a.X + t * dx), 2) + Math.Pow(p.Y - (a.Y + t * dy), 2));
    }

    private static IEnumerable<(string Table, string Column, int Srs)> UnitTables(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT g.table_name, g.column_name, g.srs_id FROM gpkg_geometry_columns g JOIN gpkg_contents c ON c.table_name = g.table_name WHERE c.data_type = 'features'";
        using SqliteDataReader reader = command.ExecuteReader();
        var tables = new List<(string, string, int)>();
        while (reader.Read())
        {
            string table = reader.GetString(0);
            if (table.Equals("unit", StringComparison.OrdinalIgnoreCase) || table.EndsWith("_unit", StringComparison.OrdinalIgnoreCase))
            {
                tables.Add((table, reader.GetString(1), reader.GetInt32(2)));
            }
        }

        return tables;
    }

    private static string Text(SqliteDataReader reader, int column) => reader.IsDBNull(column) ? string.Empty : Convert.ToString(reader.GetValue(column)) ?? string.Empty;
}

/// <summary>GeoPackage binary geometry (polygon or multipolygon) to polygons, each a list of rings.</summary>
internal static class GeoPackageGeometry
{
    public static List<List<List<(double, double)>>> Polygons(byte[] blob)
    {
        if (blob.Length < 8 || blob[0] != (byte)'G' || blob[1] != (byte)'P')
        {
            throw new FormatException("Not a GeoPackage geometry.");
        }

        int envelope = ((blob[3] >> 1) & 0x07) switch { 0 => 0, 1 => 32, 2 or 3 => 48, 4 => 64, var code => throw new FormatException($"Bad envelope code {code}.") };
        int offset = 8 + envelope;
        return Wkb(blob, ref offset);
    }

    private static List<List<List<(double, double)>>> Wkb(byte[] data, ref int offset)
    {
        bool little = data[offset] == 1;
        uint kind = UInt(data, offset + 1, little) % 1000;
        offset += 5;
        if (kind == 3)
        {
            return new List<List<List<(double, double)>>> { Rings(data, ref offset, little) };
        }

        if (kind == 6)
        {
            uint count = UInt(data, offset, little);
            offset += 4;
            var polygons = new List<List<List<(double, double)>>>();
            for (int index = 0; index < count; index++)
            {
                polygons.AddRange(Wkb(data, ref offset));
            }

            return polygons;
        }

        throw new FormatException($"Unit geometry is not a polygon: WKB type {kind}.");
    }

    private static List<List<(double, double)>> Rings(byte[] data, ref int offset, bool little)
    {
        uint count = UInt(data, offset, little);
        offset += 4;
        var rings = new List<List<(double, double)>>();
        for (int ring = 0; ring < count; ring++)
        {
            uint points = UInt(data, offset, little);
            offset += 4;
            var list = new List<(double, double)>();
            for (int point = 0; point < points; point++)
            {
                list.Add((Double(data, offset, little), Double(data, offset + 8, little)));
                offset += 16;
            }

            rings.Add(list);
        }

        return rings;
    }

    private static uint UInt(byte[] data, int offset, bool little) =>
        little ? BitConverter.ToUInt32(data, offset) : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    private static double Double(byte[] data, int offset, bool little) =>
        little ? BitConverter.ToDouble(data, offset) : System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(offset));
}
