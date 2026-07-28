using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauFootprintSanitizer
{
    public IReadOnlyCollection<(double XFeet, double YFeet)> Sanitize(
        IReadOnlyCollection<(double XFeet, double YFeet)> footprintPointsFeet,
        double minSegmentLengthFeet)
    {
        if (footprintPointsFeet is null)
        {
            throw new ArgumentNullException(nameof(footprintPointsFeet));
        }

        if (minSegmentLengthFeet < 0d)
        {
            throw new ArgumentOutOfRangeException(nameof(minSegmentLengthFeet), minSegmentLengthFeet, "The minimum segment length cannot be negative.");
        }

        List<(double XFeet, double YFeet)> sanitized = new List<(double XFeet, double YFeet)>();
        foreach ((double XFeet, double YFeet) point in footprintPointsFeet)
        {
            if (!IsFinite(point.XFeet) || !IsFinite(point.YFeet))
            {
                continue;
            }

            if (sanitized.Count == 0 || Distance(sanitized[sanitized.Count - 1], point) > minSegmentLengthFeet)
            {
                sanitized.Add(point);
            }
        }

        TrimClosingPoint(sanitized, minSegmentLengthFeet);
        RemoveShortSegments(sanitized, minSegmentLengthFeet);
        TrimClosingPoint(sanitized, minSegmentLengthFeet);

        if (sanitized.Count < 3 || Math.Abs(ComputeSignedArea(sanitized)) <= (minSegmentLengthFeet * minSegmentLengthFeet))
        {
            return Array.Empty<(double XFeet, double YFeet)>();
        }

        return sanitized.ToArray();
    }

    private static void RemoveShortSegments(List<(double XFeet, double YFeet)> points, double minSegmentLengthFeet)
    {
        if (points.Count < 3)
        {
            return;
        }

        bool removedPoint;
        do
        {
            removedPoint = false;
            for (int index = 0; index < points.Count; index++)
            {
                int nextIndex = (index + 1) % points.Count;
                if (Distance(points[index], points[nextIndex]) > minSegmentLengthFeet)
                {
                    continue;
                }

                points.RemoveAt(nextIndex == 0 ? points.Count - 1 : nextIndex);
                removedPoint = true;
                break;
            }
        }
        while (removedPoint && points.Count >= 3);
    }

    private static void TrimClosingPoint(List<(double XFeet, double YFeet)> points, double minSegmentLengthFeet)
    {
        while (points.Count > 2 && Distance(points[0], points[points.Count - 1]) <= minSegmentLengthFeet)
        {
            points.RemoveAt(points.Count - 1);
        }
    }

    private static double ComputeSignedArea(IReadOnlyList<(double XFeet, double YFeet)> points)
    {
        double area = 0d;
        for (int index = 0; index < points.Count; index++)
        {
            (double XFeet, double YFeet) current = points[index];
            (double XFeet, double YFeet) next = points[(index + 1) % points.Count];
            area += (current.XFeet * next.YFeet) - (next.XFeet * current.YFeet);
        }

        return area / 2d;
    }

    private static double Distance((double XFeet, double YFeet) start, (double XFeet, double YFeet) end)
    {
        double deltaX = start.XFeet - end.XFeet;
        double deltaY = start.YFeet - end.YFeet;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
