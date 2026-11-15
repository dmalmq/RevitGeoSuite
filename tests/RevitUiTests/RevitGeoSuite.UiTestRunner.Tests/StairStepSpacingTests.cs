using System.Globalization;
using Microsoft.Data.Sqlite;
using RevitGeoSuite.UiTestRunner;
using Xunit;
using Xunit.Abstractions;

namespace RevitGeoSuite.UiTestRunner.Tests;

public sealed class StairStepSpacingTests
{
    private readonly ITestOutputHelper output;

    public StairStepSpacingTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void Gaps_are_measured_per_run_across_the_lines_and_split_pieces_count_once()
    {
        string folder = Path.Combine(Path.GetTempPath(), "stair-steps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            WriteDetails(Path.Combine(folder, "m_GIS_1F_detail.gpkg"), new[]
            {
                ("m|run-a|0", "steps", ((0.0, 0.0), (0.0, 2.0))),
                ("m|run-a|1", "steps", ((0.6, 0.0), (0.6, 0.8))),
                ("m|run-a|2", "steps", ((0.6, 1.2), (0.6, 2.0))),
                ("m|run-a|3", "steps", ((1.2, 0.0), (1.2, 2.0))),
                ("m|run-b|0", "steps", ((10.0, 0.0), (10.0, 2.0))),
                ("m|run-b|1", "steps", ((10.3, 0.0), (10.3, 2.0))),
                ("m|wall|0", "wall", ((0.0, 5.0), (9.0, 5.0))),
            });

            List<StepGap> gaps = StairStepSpacing.Measure(folder);

            Assert.Equal(new[] { ("m|run-a", 0.6), ("m|run-a", 0.6), ("m|run-b", 0.3) }, gaps.Select(gap => (gap.RunKey, Math.Round(gap.Meters, 6))));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// Runs against a real GIS export folder named by REVITGEOSUITE_TEST_EXPORT (kept off the repo):
    /// nearly every step line sits one step spacing (REVITGEOSUITE_TEST_STEP_SPACING, else 0.60 m)
    /// from its neighbour, not a Revit tread apart. Returns early without the folder.
    /// </summary>
    [Fact]
    public void A_real_export_draws_steps_at_the_rule_spacing()
    {
        string? folder = Environment.GetEnvironmentVariable("REVITGEOSUITE_TEST_EXPORT");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        double spacing = double.TryParse(Environment.GetEnvironmentVariable("REVITGEOSUITE_TEST_STEP_SPACING"), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) ? value : 0.60d;
        List<StepGap> gaps = StairStepSpacing.Measure(folder);
        int onSpacing = gaps.Count(gap => Math.Abs(gap.Meters - spacing) <= 0.05d);
        output.WriteLine($"STEP GAPS: {gaps.Count} between neighbouring step lines in {gaps.Select(gap => gap.RunKey).Distinct().Count()} runs; {onSpacing} within 0.05 m of {spacing:0.00} m.");
        foreach (var bucket in gaps.GroupBy(gap => Math.Round(gap.Meters * 20d) / 20d).OrderBy(group => group.Key))
        {
            output.WriteLine($"  {bucket.Key:0.00} m: {bucket.Count()}");
        }

        foreach (var table in gaps.GroupBy(gap => gap.Table))
        {
            output.WriteLine($"  {table.Key}: {table.Count()} gaps, {table.Count(gap => Math.Abs(gap.Meters - spacing) <= 0.05d)} on spacing");
        }

        foreach (StepGap gap in gaps.Where(gap => Math.Abs(gap.Meters - spacing) > 0.05d).OrderBy(gap => gap.Meters).Take(15))
        {
            output.WriteLine($"  off: {gap.Meters:0.000} m {gap.Table} {gap.RunKey} line {gap.Index}");
        }

        Assert.NotEmpty(gaps);
        Assert.True(onSpacing >= gaps.Count * 0.95d, $"{gaps.Count - onSpacing} of {gaps.Count} step gaps are off the {spacing:0.00} m spacing.");
    }

    private static void WriteDetails(string path, IEnumerable<(string Key, string Category, ((double X, double Y) A, (double X, double Y) B) Line)> rows)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText =
                "CREATE TABLE gpkg_contents (table_name TEXT, data_type TEXT);" +
                "CREATE TABLE gpkg_geometry_columns (table_name TEXT, column_name TEXT);" +
                "INSERT INTO gpkg_contents VALUES ('detail', 'features');" +
                "INSERT INTO gpkg_geometry_columns VALUES ('detail', 'geom');" +
                "CREATE TABLE detail (fid INTEGER PRIMARY KEY, geom BLOB, category TEXT, revit_key TEXT);";
            create.ExecuteNonQuery();
        }

        foreach (var row in rows)
        {
            using SqliteCommand insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO detail (geom, category, revit_key) VALUES ($geom, $category, $key)";
            insert.Parameters.AddWithValue("$geom", LineBlob(row.Line.A, row.Line.B));
            insert.Parameters.AddWithValue("$category", row.Category);
            insert.Parameters.AddWithValue("$key", row.Key);
            insert.ExecuteNonQuery();
        }
    }

    private static byte[] LineBlob((double X, double Y) a, (double X, double Y) b)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[] { (byte)'G', (byte)'P', 0, 1 });
        writer.Write(6677);
        writer.Write((byte)1);
        writer.Write(2u);
        writer.Write(2u);
        writer.Write(a.X);
        writer.Write(a.Y);
        writer.Write(b.X);
        writer.Write(b.Y);
        return stream.ToArray();
    }
}
