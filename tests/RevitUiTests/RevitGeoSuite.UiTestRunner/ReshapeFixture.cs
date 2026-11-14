using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>One floor unit of a GIS export: its revit_key parts and its single polygon in the export CRS (metres).</summary>
internal sealed record FloorUnit(string RevitKey, string UniqueId, string Name, string LevelId, string Category, int Epsg, IReadOnlyList<IReadOnlyList<(double X, double Y)>> Rings)
{
    public string ModelKey => RevitKey.Split('|')[0].ToLowerInvariant();

    /// <summary>The unit's restriction, empty when the layer has no restriction column.</summary>
    public string Restriction { get; init; } = string.Empty;
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
                string restriction = HasColumn(connection, table, "restrict") ? "\"restrict\"" : HasColumn(connection, table, "restriction") ? "restriction" : "''";
                command.CommandText = $"SELECT revit_key, name, level_id, category, \"{column.Replace("\"", "\"\"")}\", {restriction} FROM \"{table.Replace("\"", "\"\"")}\"";
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
                        units.Add(new FloorUnit(key, parts[1], Text(reader, 1), Text(reader, 2), Text(reader, 3), srs, polygons[0]) { Restriction = Text(reader, 5) });
                    }
                }
            }
        }

        return units;
    }

    /// <summary>The unit <paramref name="uniqueId"/> names, else the first of <paramref name="category"/>; null when none fits.</summary>
    public static FloorUnit? Pick(IEnumerable<FloorUnit> units, string category, string uniqueId = "") =>
        units.FirstOrDefault(unit => uniqueId.Length > 0 ? unit.UniqueId == uniqueId : unit.Category == category);

    /// <summary>Vertices this close to the largest project-east coordinate are on the east edge.</summary>
    public const double EastEdgeToleranceM = 0.3;

    /// <summary>
    /// The unit with only its east edge moved <paramref name="growM"/> east (negative: west): the
    /// exterior ring's vertices within <see cref="EastEdgeToleranceM"/> of its largest X. Everything
    /// else stays, so a notch another element cuts elsewhere in the floor stays where Revit has it.
    /// </summary>
    public static List<List<(double X, double Y)>> Grow(FloorUnit unit, double growM)
    {
        double east = unit.Rings[0].Max(point => point.X);
        var rings = new List<List<(double X, double Y)>>
        {
            unit.Rings[0].Select(point => east - point.X <= EastEdgeToleranceM ? (point.X + growM, point.Y) : point).ToList(),
        };
        rings.AddRange(unit.Rings.Skip(1).Select(ring => ring.ToList()));
        return rings;
    }

    /// <summary>
    /// C3a.9's shrink: the unit minus a <paramref name="bandM"/> strip along its east side. The exterior
    /// ring is clipped at the cut; a hole is kept only when it lies wholly west of the cut, since one the
    /// strip reaches is no longer inside the outline. Crossings counts the exterior ring's edges that
    /// cross the cut: more than two means the clip joined separate parts along the cut line.
    /// </summary>
    public static ShrunkOutline ShrinkEast(FloorUnit unit, double bandM)
    {
        double cut = unit.Rings[0].Max(point => point.X) - bandM;
        IReadOnlyList<(double X, double Y)> exterior = unit.Rings[0];
        int crossings = exterior.Zip(exterior.Skip(1)).Count(edge => (edge.First.X < cut) != (edge.Second.X < cut));
        var rings = new List<List<(double X, double Y)>> { Clip(exterior, cut, keepWest: true) };
        List<List<(double X, double Y)>> holes = unit.Rings.Skip(1).Select(ring => ring.ToList()).ToList();
        rings.AddRange(holes.Where(ring => ring.All(point => point.X < cut)));
        return new ShrunkOutline(rings, crossings, holes.Count - (rings.Count - 1));
    }

    /// <summary>
    /// The unit cut in two at the middle of its east-west extent, as make_reshape_fixture.py --split does;
    /// null for a unit with holes or a piece of 1 m² or less.
    /// </summary>
    public static (List<(double X, double Y)> West, List<(double X, double Y)> East)? Split(FloorUnit unit)
    {
        IReadOnlyList<(double X, double Y)> ring = unit.Rings[0];
        double middle = (ring.Min(point => point.X) + ring.Max(point => point.X)) / 2;
        List<(double X, double Y)> west = Clip(ring, middle, keepWest: true), east = Clip(ring, middle, keepWest: false);
        return unit.Rings.Count > 1 || west.Count < 4 || east.Count < 4 || Area(west) < 1 || Area(east) < 1 ? null : (west, east);
    }

    public static double Area(IReadOnlyList<(double X, double Y)> ring) =>
        Math.Abs(ring.Zip(ring.Skip(1)).Sum(pair => pair.First.X * pair.Second.Y - pair.Second.X * pair.First.Y)) / 2;

    // Sutherland-Hodgman against the line X = x, point for point as the Python fixture's clip(); closed again.
    private static List<(double X, double Y)> Clip(IReadOnlyList<(double X, double Y)> ring, double x, bool keepWest)
    {
        bool Inside((double X, double Y) point) => keepWest ? point.X <= x : point.X >= x;
        List<(double X, double Y)> points = ring.Count > 1 && ring[0] == ring[^1] ? ring.Take(ring.Count - 1).ToList() : ring.ToList();
        var clipped = new List<(double X, double Y)>();
        for (int index = 0; index < points.Count; index++)
        {
            (double X, double Y) current = points[index], previous = points[(index + points.Count - 1) % points.Count];
            if (Inside(current))
            {
                if (!Inside(previous))
                {
                    clipped.Add(Cross(previous, current, x));
                }

                clipped.Add(current);
            }
            else if (Inside(previous))
            {
                clipped.Add(Cross(previous, current, x));
            }
        }

        if (clipped.Count > 0)
        {
            clipped.Add(clipped[0]);
        }

        return clipped;
    }

    private static (double X, double Y) Cross((double X, double Y) a, (double X, double Y) b, double x) =>
        (x, a.Y + (x - a.X) / (b.X - a.X) * (b.Y - a.Y));

    /// <summary>A `reshaped` shape: the unit gets <paramref name="outline"/>.</summary>
    public static JsonObject[] Reshaped(FloorUnit unit, IReadOnlyList<IReadOnlyList<(double X, double Y)>> outline) =>
        new[] { Shape(unit, "reshaped", outline) };

    /// <summary>Two `split` shapes sharing the unit's key: the west piece keeps the floor, the east one carries its own GDB values.</summary>
    public static JsonObject[] SplitShapes(FloorUnit unit, List<(double X, double Y)> west, List<(double X, double Y)> east)
    {
        JsonObject kept = Shape(unit, "split", new[] { west }, group: unit.RevitKey, keeps: true);
        JsonObject piece = Shape(unit, "split", new[] { east }, group: unit.RevitKey, keeps: false);
        piece["gdbName"] = unit.Name + " east";
        piece["gdbAltName"] = "E";
        piece["gdbCategory"] = unit.Category;
        return new[] { kept, piece };
    }

    /// <summary>A `merged` group: the unit keeps its row and outline, and the floor <paramref name="takenIn"/> is taken in (no outline).</summary>
    public static JsonObject[] Merged(FloorUnit unit, string takenIn)
    {
        JsonObject kept = Shape(unit, "merged", unit.Rings, group: unit.RevitKey, keeps: true);
        var gone = new JsonObject
        {
            ["edit"] = "merged",
            ["revitKey"] = $"{unit.RevitKey.Split('|')[0]}|{takenIn}|0",
            ["uniqueId"] = takenIn,
            ["layer"] = "unit",
            ["label"] = string.Empty,
            ["levelId"] = unit.LevelId,
            ["group"] = unit.RevitKey,
            ["keeps"] = false,
            ["outline"] = null,
        };
        return new[] { kept, gone };
    }

    /// <summary>
    /// A `handDrawn` unit with the unit's outline and level_id, named "&lt;name&gt; (drawn)", with its category and
    /// restriction unless <paramref name="withType"/> is false, as make_reshape_fixture.py --hand-drawn [--no-type] writes it.
    /// </summary>
    public static JsonObject[] HandDrawn(FloorUnit unit, bool withType = true, string? levelId = null)
    {
        string name = unit.Name + " (drawn)";
        var coordinates = new JsonArray(unit.Rings.Select(ring => (JsonNode)new JsonArray(ring.Select(point => (JsonNode)new JsonArray(point.X, point.Y)).ToArray())).ToArray());
        var drawn = new JsonObject
        {
            ["edit"] = "handDrawn",
            ["revitKey"] = string.Empty,
            ["uniqueId"] = string.Empty,
            ["layer"] = "unit",
            ["label"] = name,
            ["levelId"] = levelId ?? unit.LevelId,
            ["outline"] = new JsonObject { ["type"] = "Polygon", ["coordinates"] = coordinates },
            ["gdbName"] = name,
        };
        if (withType)
        {
            drawn["gdbCategory"] = unit.Category;
            if (unit.Restriction.Length > 0)
            {
                drawn["gdbRestriction"] = unit.Restriction;
            }
        }

        return new[] { drawn };
    }

    public static string DriftJson(FloorUnit unit, IEnumerable<JsonObject> shapes, string model = "")
    {
        var file = new JsonObject
        {
            ["schema"] = "kanizu.gis-drift",
            ["version"] = 1,
            ["model"] = model.Length > 0 ? model : unit.ModelKey,
            ["appliedVersion"] = "ui-test",
            ["rows"] = new JsonArray(),
            ["epsg"] = unit.Epsg,
            ["shapes"] = new JsonArray(shapes.Cast<JsonNode>().ToArray()),
        };
        return file.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
    }

    private static JsonObject Shape(FloorUnit unit, string edit, IEnumerable<IReadOnlyList<(double X, double Y)>> outline, string? group = null, bool? keeps = null)
    {
        var shape = new JsonObject
        {
            ["revitKey"] = unit.RevitKey,
            ["uniqueId"] = unit.UniqueId,
            ["layer"] = "unit",
            ["label"] = unit.Name,
            ["levelId"] = unit.LevelId,
            ["edit"] = edit,
        };
        if (group != null)
        {
            shape["group"] = group;
            shape["keeps"] = keeps;
        }

        var coordinates = new JsonArray(outline.Select(ring => (JsonNode)new JsonArray(ring.Select(point => (JsonNode)new JsonArray(point.X, point.Y)).ToArray())).ToArray());
        shape["outline"] = new JsonObject { ["type"] = "Polygon", ["coordinates"] = coordinates };
        return shape;
    }

    /// <summary>The smallest distance between two outlines' edges (0 when they touch or cross), in their units.</summary>
    public static double Gap(IReadOnlyList<IReadOnlyList<(double X, double Y)>> a, IReadOnlyList<IReadOnlyList<(double X, double Y)>> b) =>
        Math.Min(
            a.SelectMany(ring => ring).Min(point => b.Min(ring => RingDistance(point, ring))),
            b.SelectMany(ring => ring).Min(point => a.Min(ring => RingDistance(point, ring))));

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

    private static bool HasColumn(SqliteConnection connection, string table, string column)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table.Replace("'", "''")}') WHERE name = '{column}'";
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
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

/// <summary>A shrunk outline (exterior ring first, then the holes kept), with what the cut did.</summary>
internal sealed record ShrunkOutline(List<List<(double X, double Y)>> Rings, int Crossings, int DroppedHoles);

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
