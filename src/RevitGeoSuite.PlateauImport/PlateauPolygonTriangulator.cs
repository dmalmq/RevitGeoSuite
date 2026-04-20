using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.PlateauImport;

internal static class PlateauPolygonTriangulator
{
    private const double Epsilon = 1e-9d;

    public static bool TryTriangulate(IReadOnlyCollection<ContextShapePoint3D> inputPoints, out IReadOnlyCollection<ContextShapeTriangle> triangles)
    {
        List<ContextShapePoint3D> points = Normalize(inputPoints);
        triangles = Array.Empty<ContextShapeTriangle>();
        if (points.Count < 3)
        {
            return false;
        }

        if (!TryProjectToPlane(points, out List<PlanarPoint> planarPoints))
        {
            return false;
        }

        double signedArea = ComputeSignedArea(planarPoints);
        if (Math.Abs(signedArea) <= Epsilon)
        {
            return false;
        }

        bool isCounterClockwise = signedArea > 0d;
        if (points.Count == 3)
        {
            triangles = new[] { CreateTriangle(points[0], points[1], points[2], isCounterClockwise) };
            return true;
        }

        List<int> remaining = Enumerable.Range(0, points.Count).ToList();
        List<ContextShapeTriangle> result = new List<ContextShapeTriangle>(points.Count - 2);
        int guard = points.Count * points.Count;

        while (remaining.Count > 3 && guard-- > 0)
        {
            bool earFound = false;
            for (int index = 0; index < remaining.Count; index++)
            {
                int previousIndex = remaining[(index - 1 + remaining.Count) % remaining.Count];
                int currentIndex = remaining[index];
                int nextIndex = remaining[(index + 1) % remaining.Count];

                PlanarPoint previous = planarPoints[previousIndex];
                PlanarPoint current = planarPoints[currentIndex];
                PlanarPoint next = planarPoints[nextIndex];

                if (!IsConvex(previous, current, next, isCounterClockwise))
                {
                    continue;
                }

                bool containsOtherVertex = remaining
                    .Where(candidate => candidate != previousIndex && candidate != currentIndex && candidate != nextIndex)
                    .Any(candidate => IsPointInsideTriangle(planarPoints[candidate], previous, current, next));
                if (containsOtherVertex)
                {
                    continue;
                }

                result.Add(CreateTriangle(points[previousIndex], points[currentIndex], points[nextIndex], isCounterClockwise));
                remaining.RemoveAt(index);
                earFound = true;
                break;
            }

            if (!earFound)
            {
                triangles = Array.Empty<ContextShapeTriangle>();
                return false;
            }
        }

        if (remaining.Count != 3)
        {
            triangles = Array.Empty<ContextShapeTriangle>();
            return false;
        }

        result.Add(CreateTriangle(points[remaining[0]], points[remaining[1]], points[remaining[2]], isCounterClockwise));
        triangles = result;
        return true;
    }

    private static List<ContextShapePoint3D> Normalize(IReadOnlyCollection<ContextShapePoint3D> inputPoints)
    {
        List<ContextShapePoint3D> points = inputPoints?.ToList() ?? new List<ContextShapePoint3D>();
        if (points.Count > 1 && AreEqual(points[0], points[points.Count - 1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    private static bool TryProjectToPlane(IReadOnlyList<ContextShapePoint3D> points, out List<PlanarPoint> planarPoints)
    {
        planarPoints = new List<PlanarPoint>(points.Count);
        Vector3 normal = ComputeNormal(points);
        if (normal.LengthSquared <= Epsilon)
        {
            return false;
        }

        normal = normal.Normalize();
        Vector3 axisU = default;
        for (int index = 1; index < points.Count; index++)
        {
            Vector3 candidate = Vector3.From(points[0], points[index]);
            Vector3 perpendicular = candidate - (normal * candidate.Dot(normal));
            if (perpendicular.LengthSquared > Epsilon)
            {
                axisU = perpendicular.Normalize();
                break;
            }
        }

        if (axisU.LengthSquared <= Epsilon)
        {
            return false;
        }

        Vector3 axisV = normal.Cross(axisU).Normalize();
        ContextShapePoint3D origin = points[0];
        foreach (ContextShapePoint3D point in points)
        {
            Vector3 delta = Vector3.From(origin, point);
            planarPoints.Add(new PlanarPoint(delta.Dot(axisU), delta.Dot(axisV)));
        }

        return true;
    }

    private static Vector3 ComputeNormal(IReadOnlyList<ContextShapePoint3D> points)
    {
        double x = 0d;
        double y = 0d;
        double z = 0d;
        for (int index = 0; index < points.Count; index++)
        {
            ContextShapePoint3D current = points[index];
            ContextShapePoint3D next = points[(index + 1) % points.Count];
            x += (current.YFeet - next.YFeet) * (current.ZFeet + next.ZFeet);
            y += (current.ZFeet - next.ZFeet) * (current.XFeet + next.XFeet);
            z += (current.XFeet - next.XFeet) * (current.YFeet + next.YFeet);
        }

        return new Vector3(x, y, z);
    }

    private static double ComputeSignedArea(IReadOnlyList<PlanarPoint> points)
    {
        double areaTwice = 0d;
        for (int index = 0; index < points.Count; index++)
        {
            PlanarPoint current = points[index];
            PlanarPoint next = points[(index + 1) % points.Count];
            areaTwice += (current.X * next.Y) - (next.X * current.Y);
        }

        return areaTwice * 0.5d;
    }

    private static bool IsConvex(PlanarPoint previous, PlanarPoint current, PlanarPoint next, bool isCounterClockwise)
    {
        double cross = Cross(previous, current, next);
        return isCounterClockwise
            ? cross > Epsilon
            : cross < -Epsilon;
    }

    private static bool IsPointInsideTriangle(PlanarPoint point, PlanarPoint a, PlanarPoint b, PlanarPoint c)
    {
        double c1 = Cross(point, a, b);
        double c2 = Cross(point, b, c);
        double c3 = Cross(point, c, a);

        bool hasNegative = c1 < -Epsilon || c2 < -Epsilon || c3 < -Epsilon;
        bool hasPositive = c1 > Epsilon || c2 > Epsilon || c3 > Epsilon;
        return !(hasNegative && hasPositive);
    }

    private static double Cross(PlanarPoint a, PlanarPoint b, PlanarPoint c)
    {
        return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
    }

    private static bool AreEqual(ContextShapePoint3D a, ContextShapePoint3D b)
    {
        return Math.Abs(a.XFeet - b.XFeet) <= Epsilon
            && Math.Abs(a.YFeet - b.YFeet) <= Epsilon
            && Math.Abs(a.ZFeet - b.ZFeet) <= Epsilon;
    }

    private static ContextShapeTriangle CreateTriangle(ContextShapePoint3D a, ContextShapePoint3D b, ContextShapePoint3D c, bool isCounterClockwise)
    {
        return isCounterClockwise
            ? new ContextShapeTriangle(a, b, c)
            : new ContextShapeTriangle(c, b, a);
    }

    private readonly struct PlanarPoint
    {
        public PlanarPoint(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }

        public double Y { get; }
    }

    private readonly struct Vector3
    {
        public Vector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public double X { get; }

        public double Y { get; }

        public double Z { get; }

        public double LengthSquared => (X * X) + (Y * Y) + (Z * Z);

        public static Vector3 From(ContextShapePoint3D start, ContextShapePoint3D end)
        {
            return new Vector3(end.XFeet - start.XFeet, end.YFeet - start.YFeet, end.ZFeet - start.ZFeet);
        }

        public Vector3 Normalize()
        {
            double length = Math.Sqrt(LengthSquared);
            return length <= Epsilon
                ? default
                : new Vector3(X / length, Y / length, Z / length);
        }

        public double Dot(Vector3 other)
        {
            return (X * other.X) + (Y * other.Y) + (Z * other.Z);
        }

        public Vector3 Cross(Vector3 other)
        {
            return new Vector3(
                (Y * other.Z) - (Z * other.Y),
                (Z * other.X) - (X * other.Z),
                (X * other.Y) - (Y * other.X));
        }

        public static Vector3 operator *(Vector3 vector, double scalar)
        {
            return new Vector3(vector.X * scalar, vector.Y * scalar, vector.Z * scalar);
        }

        public static Vector3 operator -(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }
    }
}
