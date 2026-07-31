using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Triangulate.Polygon;

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

        if (points.Count == 4 && TryTriangulateQuad(points, planarPoints, isCounterClockwise, out triangles))
        {
            return true;
        }

        List<int> remaining = new List<int>(points.Count);
        for (int index = 0; index < points.Count; index++)
        {
            remaining.Add(index);
        }

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

                bool containsOtherVertex = false;
                for (int candidateIndex = 0; candidateIndex < remaining.Count; candidateIndex++)
                {
                    int candidate = remaining[candidateIndex];
                    if (candidate == previousIndex || candidate == currentIndex || candidate == nextIndex)
                    {
                        continue;
                    }

                    if (IsPointInsideTriangle(planarPoints[candidate], previous, current, next))
                    {
                        containsOtherVertex = true;
                        break;
                    }
                }

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

    public static bool TryTriangulate(
        IReadOnlyCollection<ContextShapePoint3D> exteriorRing,
        IReadOnlyCollection<IReadOnlyCollection<ContextShapePoint3D>> interiorRings,
        out IReadOnlyCollection<ContextShapeTriangle> triangles)
    {
        triangles = Array.Empty<ContextShapeTriangle>();
        if (interiorRings is null || interiorRings.Count == 0)
        {
            return TryTriangulate(exteriorRing, out triangles);
        }

        List<ContextShapePoint3D> exteriorPoints = Normalize(exteriorRing);
        List<List<ContextShapePoint3D>> holes = interiorRings
            .Select(Normalize)
            .Where(ring => ring.Count >= 3)
            .ToList();
        if (holes.Count == 0)
        {
            return TryTriangulate(exteriorPoints, out triangles);
        }

        if (exteriorPoints.Count < 3 || !TryCreateProjectionFrame(exteriorPoints, out ProjectionFrame frame))
        {
            return false;
        }

        List<PlanarPoint> exteriorPlanar = ProjectToPlane(exteriorPoints, frame);
        double exteriorArea = ComputeSignedArea(exteriorPlanar);
        if (Math.Abs(exteriorArea) <= Epsilon)
        {
            return false;
        }

        List<List<PlanarPoint>> holePlanars = new List<List<PlanarPoint>>(holes.Count);
        foreach (List<ContextShapePoint3D> hole in holes)
        {
            List<PlanarPoint> holePlanar = ProjectToPlane(hole, frame);
            if (Math.Abs(ComputeSignedArea(holePlanar)) > Epsilon)
            {
                holePlanars.Add(holePlanar);
            }
        }

        if (holePlanars.Count == 0)
        {
            return TryTriangulate(exteriorPoints, out triangles);
        }

        return TryTriangulateWithHoles(exteriorPlanar, holePlanars, frame, out triangles);
    }

    private static bool TryTriangulateQuad(
        IReadOnlyList<ContextShapePoint3D> points,
        IReadOnlyList<PlanarPoint> planarPoints,
        bool isCounterClockwise,
        out IReadOnlyCollection<ContextShapeTriangle> triangles)
    {
        triangles = Array.Empty<ContextShapeTriangle>();
        if (!IsConvexPolygon(planarPoints, isCounterClockwise))
        {
            return false;
        }

        triangles = new[]
        {
            CreateTriangle(points[0], points[1], points[2], isCounterClockwise),
            CreateTriangle(points[0], points[2], points[3], isCounterClockwise)
        };
        return true;
    }

    private static bool IsConvexPolygon(IReadOnlyList<PlanarPoint> points, bool isCounterClockwise)
    {
        for (int index = 0; index < points.Count; index++)
        {
            PlanarPoint previous = points[(index - 1 + points.Count) % points.Count];
            PlanarPoint current = points[index];
            PlanarPoint next = points[(index + 1) % points.Count];
            if (!IsConvex(previous, current, next, isCounterClockwise))
            {
                return false;
            }
        }

        return true;
    }

    private static List<ContextShapePoint3D> Normalize(IReadOnlyCollection<ContextShapePoint3D> inputPoints)
    {
        List<ContextShapePoint3D> points = inputPoints is null
            ? new List<ContextShapePoint3D>()
            : new List<ContextShapePoint3D>(inputPoints);
        if (points.Count > 1 && AreEqual(points[0], points[points.Count - 1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    private static bool TryProjectToPlane(IReadOnlyList<ContextShapePoint3D> points, out List<PlanarPoint> planarPoints)
    {
        planarPoints = new List<PlanarPoint>();
        if (!TryCreateProjectionFrame(points, out ProjectionFrame frame))
        {
            return false;
        }

        planarPoints = ProjectToPlane(points, frame);
        return true;
    }

    private static bool TryCreateProjectionFrame(IReadOnlyList<ContextShapePoint3D> points, out ProjectionFrame frame)
    {
        frame = default;
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
        frame = new ProjectionFrame(points[0], axisU, axisV);
        return true;
    }

    private static List<PlanarPoint> ProjectToPlane(IReadOnlyList<ContextShapePoint3D> points, ProjectionFrame frame)
    {
        List<PlanarPoint> planarPoints = new List<PlanarPoint>(points.Count);
        foreach (ContextShapePoint3D point in points)
        {
            Vector3 delta = Vector3.From(frame.Origin, point);
            planarPoints.Add(new PlanarPoint(delta.Dot(frame.AxisU), delta.Dot(frame.AxisV)));
        }

        return planarPoints;
    }

    private static bool TryTriangulateWithHoles(
        IReadOnlyList<PlanarPoint> exteriorRing,
        IReadOnlyList<IReadOnlyList<PlanarPoint>> interiorRings,
        ProjectionFrame frame,
        out IReadOnlyCollection<ContextShapeTriangle> triangles)
    {
        triangles = Array.Empty<ContextShapeTriangle>();
        try
        {
            GeometryFactory factory = GeometryFactory.Default;
            LinearRing shell = factory.CreateLinearRing(ToClosedCoordinates(EnsureOrientation(exteriorRing, counterClockwise: true)));
            LinearRing[] holes = interiorRings
                .Select(ring => factory.CreateLinearRing(ToClosedCoordinates(EnsureOrientation(ring, counterClockwise: false))))
                .ToArray();
            Polygon polygon = factory.CreatePolygon(shell, holes);
            if (!polygon.IsValid || polygon.Area <= Epsilon)
            {
                return false;
            }

            Geometry geometry = PolygonTriangulator.Triangulate(polygon);
            List<ContextShapeTriangle> result = new List<ContextShapeTriangle>();
            AddTriangles(geometry, frame, result);
            if (result.Count == 0)
            {
                return false;
            }

            triangles = result;
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (TopologyException)
        {
            return false;
        }
    }

    private static IReadOnlyList<PlanarPoint> EnsureOrientation(IReadOnlyList<PlanarPoint> ring, bool counterClockwise)
    {
        bool isCounterClockwise = ComputeSignedArea(ring) > 0d;
        if (isCounterClockwise == counterClockwise)
        {
            return ring;
        }

        PlanarPoint[] reversed = ring.Reverse().ToArray();
        return reversed;
    }

    private static Coordinate[] ToClosedCoordinates(IReadOnlyList<PlanarPoint> ring)
    {
        Coordinate[] coordinates = new Coordinate[ring.Count + 1];
        for (int index = 0; index < ring.Count; index++)
        {
            coordinates[index] = new Coordinate(ring[index].X, ring[index].Y);
        }

        coordinates[coordinates.Length - 1] = new Coordinate(ring[0].X, ring[0].Y);
        return coordinates;
    }

    private static void AddTriangles(Geometry geometry, ProjectionFrame frame, ICollection<ContextShapeTriangle> triangles)
    {
        if (geometry is Polygon polygon)
        {
            Coordinate[] coordinates = polygon.ExteriorRing.Coordinates;
            if (coordinates.Length >= 4)
            {
                PlanarPoint a = new PlanarPoint(coordinates[0].X, coordinates[0].Y);
                PlanarPoint b = new PlanarPoint(coordinates[1].X, coordinates[1].Y);
                PlanarPoint c = new PlanarPoint(coordinates[2].X, coordinates[2].Y);
                if (Math.Abs(ComputeSignedArea(new[] { a, b, c })) > Epsilon)
                {
                    bool isCounterClockwise = ComputeSignedArea(new[] { a, b, c }) > 0d;
                    triangles.Add(CreateTriangle(ToPoint3D(a, frame), ToPoint3D(b, frame), ToPoint3D(c, frame), isCounterClockwise));
                }
            }

            return;
        }

        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            AddTriangles(geometry.GetGeometryN(index), frame, triangles);
        }
    }

    private static ContextShapePoint3D ToPoint3D(PlanarPoint point, ProjectionFrame frame)
    {
        Vector3 offset = (frame.AxisU * point.X) + (frame.AxisV * point.Y);
        return new ContextShapePoint3D(
            frame.Origin.XFeet + offset.X,
            frame.Origin.YFeet + offset.Y,
            frame.Origin.ZFeet + offset.Z);
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

    private readonly struct ProjectionFrame
    {
        public ProjectionFrame(ContextShapePoint3D origin, Vector3 axisU, Vector3 axisV)
        {
            Origin = origin;
            AxisU = axisU;
            AxisV = axisV;
        }

        public ContextShapePoint3D Origin { get; }

        public Vector3 AxisU { get; }

        public Vector3 AxisV { get; }
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

        public static Vector3 operator +(Vector3 left, Vector3 right)
        {
            return new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }
    }
}
