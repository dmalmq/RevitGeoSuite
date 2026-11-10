using System;
using System.Collections.Generic;
using NetTopologySuite.Geometries;
using Polygon = NetTopologySuite.Geometries.Polygon;

namespace RevitGeoSuite.FloorPlanExport.Core.UnitFloors;

/// <summary>
/// Turns a polygon into the closed rings a Revit floor sketch takes. Revit refuses edges shorter
/// than its short-curve tolerance (about 0.8 mm), so vertices closer than <c>minSegmentMetres</c>
/// to the last kept one are dropped, and a ring left with fewer than three corners is dropped too.
/// </summary>
public static class UnitFloorRings
{
    public const double DefaultMinSegmentMetres = 0.002;

    /// <summary>The shell first, then each hole, as open point lists (the closing point is not repeated).</summary>
    public static IReadOnlyList<IReadOnlyList<(double X, double Y)>> FromPolygon(Polygon polygon, double minSegmentMetres = DefaultMinSegmentMetres)
    {
        if (polygon is null)
        {
            throw new ArgumentNullException(nameof(polygon));
        }

        List<IReadOnlyList<(double X, double Y)>> rings = new();
        IReadOnlyList<(double X, double Y)>? shell = Ring(polygon.Shell, minSegmentMetres);
        if (shell is null)
        {
            return rings;
        }

        rings.Add(shell);
        foreach (LinearRing hole in polygon.Holes)
        {
            IReadOnlyList<(double X, double Y)>? ring = Ring(hole, minSegmentMetres);
            if (ring is not null)
            {
                rings.Add(ring);
            }
        }

        return rings;
    }

    private static IReadOnlyList<(double X, double Y)>? Ring(LineString ring, double minSegmentMetres)
    {
        Coordinate[] coordinates = ring.Coordinates;
        int count = coordinates.Length;
        if (count > 1 && coordinates[0].Equals2D(coordinates[count - 1]))
        {
            count--;
        }

        double min2 = minSegmentMetres * minSegmentMetres;
        List<(double X, double Y)> kept = new(count);
        for (int i = 0; i < count; i++)
        {
            Coordinate c = coordinates[i];
            if (kept.Count > 0 && Distance2(kept[kept.Count - 1], c) < min2)
            {
                continue;
            }

            kept.Add((c.X, c.Y));
        }

        while (kept.Count > 1 && Distance2(kept[kept.Count - 1], new Coordinate(kept[0].X, kept[0].Y)) < min2)
        {
            kept.RemoveAt(kept.Count - 1);
        }

        return kept.Count >= 3 ? kept : null;
    }

    private static double Distance2((double X, double Y) a, Coordinate b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return (dx * dx) + (dy * dy);
    }
}
