using RevitGeoSuite.UiTestRunner;
using Xunit;
using Xunit.Abstractions;

namespace RevitGeoSuite.UiTestRunner.Tests;

/// <summary>
/// Runs the c3b/c3c picks against a real GIS export folder named by REVITGEOSUITE_TEST_EXPORT (a run's
/// export folder, kept off the repo), so a pick is checked before a Revit round. Returns early without it.
/// </summary>
public sealed class ShapeEditPicksExportTests
{
    private readonly ITestOutputHelper output;

    public ShapeEditPicksExportTests(ITestOutputHelper output) => this.output = output;

    [Fact]
    public void A_real_export_has_a_clean_split_and_a_one_part_merge_pair()
    {
        string? folder = Environment.GetEnvironmentVariable("REVITGEOSUITE_TEST_EXPORT");
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        List<FloorUnit> units = ReshapeFixture.FloorUnits(folder);
        Dictionary<string, int> parts = ReshapeFixture.FloorParts(folder);
        var splits = ShapeEditPicks.SplitCandidates(units, parts, _ => true);
        var merges = ShapeEditPicks.MergePairs(units, parts, _ => true, _ => true);
        output.WriteLine($"{units.Count} single-polygon units, {parts.Count} floors, {parts.Count(pair => pair.Value == 1)} with one part.");
        foreach (var (unit, cut) in splits.Take(5))
        {
            output.WriteLine($"split {unit.UniqueId} {unit.Category} '{unit.Name}' {cut.Axis}={cut.At:0.00} {ReshapeFixture.Area(cut.West):0.0}+{ReshapeFixture.Area(cut.East):0.0} m², east side {cut.NewPieceOnEastSide(1.0)}");
        }

        foreach (var (kept, taken, gap) in merges.Take(5))
        {
            output.WriteLine($"merge K {kept.UniqueId} {kept.Category} G {taken.UniqueId} {taken.Category} level {kept.LevelId} gap {gap:0.00}");
        }

        Assert.NotEmpty(splits);
        Assert.Contains(splits.Skip(1), pair => pair.Cut.NewPieceOnEastSide(1.0));
        Assert.All(splits, pair =>
        {
            Assert.Equal(1, parts[pair.Unit.UniqueId]);
            Assert.True(Simple(pair.Cut.West) && Simple(pair.Cut.East), $"{pair.Unit.UniqueId} has a self-crossing piece.");
        });
        Assert.NotEmpty(merges);
        Assert.All(merges, pair => Assert.True(parts[pair.Kept.UniqueId] == 1 && parts[pair.Taken.UniqueId] == 1));
        Assert.Contains(parts, pair => pair.Value > 1);
    }

    // No two non-adjacent edges of the closed ring touch or cross.
    private static bool Simple(List<(double X, double Y)> ring)
    {
        int count = ring.Count - 1;
        for (int i = 0; i < count; i++)
        {
            for (int j = i + 1; j < count; j++)
            {
                if (j == i + 1 || (i == 0 && j == count - 1))
                {
                    continue;
                }

                if (Touch(ring[i], ring[i + 1], ring[j], ring[j + 1]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool Touch((double X, double Y) a, (double X, double Y) b, (double X, double Y) c, (double X, double Y) d)
    {
        static double Cross((double X, double Y) o, (double X, double Y) p, (double X, double Y) q) => (p.X - o.X) * (q.Y - o.Y) - (p.Y - o.Y) * (q.X - o.X);
        static bool On((double X, double Y) p, (double X, double Y) q, (double X, double Y) r) =>
            Math.Min(p.X, q.X) - 1e-9 <= r.X && r.X <= Math.Max(p.X, q.X) + 1e-9 && Math.Min(p.Y, q.Y) - 1e-9 <= r.Y && r.Y <= Math.Max(p.Y, q.Y) + 1e-9;
        double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
        if (((d1 > 1e-9 && d2 < -1e-9) || (d1 < -1e-9 && d2 > 1e-9)) && ((d3 > 1e-9 && d4 < -1e-9) || (d3 < -1e-9 && d4 > 1e-9)))
        {
            return true;
        }

        return (Math.Abs(d1) <= 1e-9 && On(c, d, a)) || (Math.Abs(d2) <= 1e-9 && On(c, d, b)) || (Math.Abs(d3) <= 1e-9 && On(a, b, c)) || (Math.Abs(d4) <= 1e-9 && On(a, b, d));
    }
}
