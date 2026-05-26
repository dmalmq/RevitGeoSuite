using System.Collections.Generic;

namespace RevitGeoSuite.PlateauImport.Online;

/// <summary>
/// 2D convex hull via Andrew's monotone chain (Graham-scan variant). Used to extract a
/// building footprint from a tessellated mesh's projected XY vertices.
/// </summary>
internal static class ConvexHull
{
    public static List<(double X, double Y)> Compute(List<(double X, double Y)> points)
    {
        if (points.Count < 3) return new List<(double X, double Y)>(points);

        List<(double X, double Y)> sorted = new List<(double X, double Y)>(points);
        sorted.Sort((a, b) =>
        {
            int xc = a.X.CompareTo(b.X);
            return xc != 0 ? xc : a.Y.CompareTo(b.Y);
        });

        List<(double X, double Y)> lower = new List<(double X, double Y)>();
        foreach (var p in sorted)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(p);
        }

        List<(double X, double Y)> upper = new List<(double X, double Y)>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            var p = sorted[i];
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(p);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        lower.AddRange(upper);
        return lower;
    }

    private static double Cross((double X, double Y) o, (double X, double Y) a, (double X, double Y) b) =>
        (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
}
