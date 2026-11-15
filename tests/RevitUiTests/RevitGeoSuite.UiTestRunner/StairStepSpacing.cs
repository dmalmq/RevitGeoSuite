using Microsoft.Data.Sqlite;

namespace RevitGeoSuite.UiTestRunner;

/// <summary>The distance between two neighbouring step lines of one stair run, in the export CRS (metres).</summary>
internal sealed record StepGap(string Table, string RunKey, int Index, double Meters);

/// <summary>
/// Measures the step lines (detail category "steps") of a GIS export: for each run, the distance
/// between each pair of neighbouring lines. The stair drawing rule puts them one step spacing apart
/// (0.60 m unless the model says otherwise); Revit's own treads sit about 0.30 m apart.
/// </summary>
internal static class StairStepSpacing
{
    /// <summary>Two pieces of one step line, split where the view hides part of the stair, are this close or closer.</summary>
    private const double SameLineMeters = 0.05d;

    public static List<StepGap> Measure(string exportFolder)
    {
        var gaps = new List<StepGap>();
        foreach (string file in Directory.GetFiles(exportFolder, "*.gpkg", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
        {
            using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = file, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            connection.Open();
            foreach ((string table, string geometryColumn) in FeatureTables(connection))
            {
                List<string> columns = Columns(connection, table);
                if (!columns.Contains("category") || !columns.Contains("revit_key"))
                {
                    continue;
                }

                var runs = new Dictionary<string, List<(int Index, (double X, double Y) A, (double X, double Y) B)>>(StringComparer.Ordinal);
                using SqliteCommand command = connection.CreateCommand();
                command.CommandText = $"SELECT revit_key, \"{geometryColumn.Replace("\"", "\"\"")}\" FROM \"{table.Replace("\"", "\"\"")}\" WHERE category = 'steps'";
                using SqliteDataReader reader = command.ExecuteReader();
                while (reader.Read())
                {
                    if (reader.IsDBNull(0) || reader.IsDBNull(1))
                    {
                        continue;
                    }

                    string key = reader.GetString(0);
                    int bar = key.LastIndexOf('|');
                    if (bar < 0 || !int.TryParse(key[(bar + 1)..], out int index))
                    {
                        continue;
                    }

                    List<(double X, double Y)> points = LinePoints((byte[])reader.GetValue(1));
                    if (points.Count < 2)
                    {
                        continue;
                    }

                    string run = key[..bar];
                    if (!runs.TryGetValue(run, out var lines))
                    {
                        runs[run] = lines = new();
                    }

                    lines.Add((index, points[0], points[^1]));
                }

                string name = $"{Path.GetFileName(file)}:{table}";
                foreach ((string run, var lines) in runs.OrderBy(pair => pair.Key, StringComparer.Ordinal))
                {
                    lines.Sort((left, right) => left.Index.CompareTo(right.Index));
                    for (int i = 1; i < lines.Count; i++)
                    {
                        double meters = DistanceBetweenLines(lines[i - 1].A, lines[i - 1].B, lines[i].A, lines[i].B);
                        if (meters > SameLineMeters)
                        {
                            gaps.Add(new StepGap(name, run, lines[i].Index, meters));
                        }
                    }
                }
            }
        }

        return gaps;
    }

    /// <summary>How far the middle of the second line lies from the first line, extended both ways.</summary>
    private static double DistanceBetweenLines((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        double mx = (c.X + d.X) / 2d;
        double my = (c.Y + d.Y) / 2d;
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        return length <= 1e-9d
            ? Math.Sqrt(((mx - a.X) * (mx - a.X)) + ((my - a.Y) * (my - a.Y)))
            : Math.Abs(((mx - a.X) * dy) - ((my - a.Y) * dx)) / length;
    }

    /// <summary>The points of a GeoPackage linestring, or of the first part of a multilinestring.</summary>
    private static List<(double X, double Y)> LinePoints(byte[] blob)
    {
        if (blob.Length < 8 || blob[0] != (byte)'G' || blob[1] != (byte)'P')
        {
            throw new FormatException("Not a GeoPackage geometry.");
        }

        int envelope = ((blob[3] >> 1) & 0x07) switch { 0 => 0, 1 => 32, 2 or 3 => 48, 4 => 64, var code => throw new FormatException($"Bad envelope code {code}.") };
        int offset = 8 + envelope;
        bool little = blob[offset] == 1;
        uint kind = UInt(blob, offset + 1, little) % 1000;
        offset += 5;
        if (kind == 5)
        {
            offset += 4;
            little = blob[offset] == 1;
            kind = UInt(blob, offset + 1, little) % 1000;
            offset += 5;
        }

        if (kind != 2)
        {
            throw new FormatException($"Step line geometry is not a line: WKB type {kind}.");
        }

        uint count = UInt(blob, offset, little);
        offset += 4;
        var points = new List<(double X, double Y)>((int)count);
        for (int i = 0; i < count; i++)
        {
            points.Add((Double(blob, offset, little), Double(blob, offset + 8, little)));
            offset += 16;
        }

        return points;
    }

    private static List<(string Table, string GeometryColumn)> FeatureTables(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT c.table_name, g.column_name FROM gpkg_contents c JOIN gpkg_geometry_columns g ON g.table_name = c.table_name WHERE c.data_type = 'features'";
        using SqliteDataReader reader = command.ExecuteReader();
        var tables = new List<(string, string)>();
        while (reader.Read())
        {
            tables.Add((reader.GetString(0), reader.GetString(1)));
        }

        return tables;
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

    private static uint UInt(byte[] data, int offset, bool little) =>
        little ? BitConverter.ToUInt32(data, offset) : System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));

    private static double Double(byte[] data, int offset, bool little) =>
        little ? BitConverter.ToDouble(data, offset) : System.Buffers.Binary.BinaryPrimitives.ReadDoubleBigEndian(data.AsSpan(offset));
}
