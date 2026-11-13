using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Extractors;

/// <summary>A floor's top faces, read once for the audit's layout checks and a slope's ends.</summary>
internal static class FloorTopFaces
{
    /// <summary>
    /// The floor's top faces as polygons (the largest loop of each face is its outline) and the
    /// height of their lowest and highest point, in the floor document's model coordinates, the
    /// coordinates of Level.ProjectElevation. Both heights are 0 when there is no top face.
    /// </summary>
    public static (List<Polygon2D> Outline, double LowFeet, double HighFeet) Read(Floor floor)
    {
        List<Polygon2D> polygons = new();
        double low = double.MaxValue;
        double high = double.MinValue;
        foreach (Reference reference in HostObjectUtils.GetTopFaces(floor))
        {
            if (floor.GetGeometryObjectFromReference(reference) is not Face face)
            {
                continue;
            }

            List<List<Point2D>> loops = new();
            foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
            {
                List<Point2D> ring = new();
                foreach (Curve curve in loop)
                {
                    IList<XYZ> points = curve.Tessellate();
                    for (int index = 0; index < points.Count - 1; index++)
                    {
                        ring.Add(new Point2D(points[index].X, points[index].Y));
                        low = Math.Min(low, points[index].Z);
                        high = Math.Max(high, points[index].Z);
                    }
                }

                if (ring.Count >= 3)
                {
                    loops.Add(ring);
                }
            }

            if (loops.Count > 0)
            {
                List<List<Point2D>> ordered = loops.OrderByDescending(ring => Math.Abs(Area(ring))).ToList();
                polygons.Add(new Polygon2D(ordered[0], ordered.Skip(1).Cast<IReadOnlyList<Point2D>>().ToList()));
            }
        }

        return polygons.Count == 0 ? (polygons, 0, 0) : (polygons, low, high);
    }

    private static double Area(IReadOnlyList<Point2D> ring)
    {
        double sum = 0;
        for (int index = 0; index < ring.Count; index++)
        {
            Point2D a = ring[index];
            Point2D b = ring[(index + 1) % ring.Count];
            sum += (a.X * b.Y) - (b.X * a.Y);
        }

        return sum / 2;
    }
}
