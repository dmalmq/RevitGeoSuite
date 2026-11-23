using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.Tiles3DExport;

/// <summary>
/// Reduces Medium and Coarse meshes by vertex clustering: vertices in one grid cell merge into a
/// single original vertex, so triangles that shared a vertex still share it and no holes open.
/// Each mesh uses the finest grid that meets its level's triangle budget.
/// </summary>
public sealed class Tiles3DGeometrySimplifier
{
    private const int CoarsestStride = 4;
    private const int MinGridResolution = 2;
    private const int CellBits = 20;
    private const int MaxGridResolution = 1 << CellBits;

    public IReadOnlyCollection<Tiles3DMeshPrimitive> Simplify(IReadOnlyCollection<Tiles3DMeshPrimitive> meshes, Tiles3DLevelOfDetail levelOfDetail)
    {
        if (meshes is null)
        {
            throw new ArgumentNullException(nameof(meshes));
        }

        int stride = GetTriangleStride(levelOfDetail);
        if (stride <= 1)
        {
            return meshes.Select(ClonePrimitive).ToArray();
        }

        return meshes
            .Select(mesh => Simplify(mesh, stride))
            .Where(mesh => mesh.Triangles.Count > 0)
            .ToArray();
    }

    public static int GetTriangleStride(Tiles3DLevelOfDetail levelOfDetail)
    {
        return levelOfDetail switch
        {
            Tiles3DLevelOfDetail.Coarse => CoarsestStride,
            Tiles3DLevelOfDetail.Medium => 2,
            _ => 1
        };
    }

    /// <summary>Geometric error in metres. Coarser levels carry a larger error, as 3D Tiles requires.</summary>
    public static double GetGeometricError(double maxExtentMeters, Tiles3DLevelOfDetail levelOfDetail)
    {
        return Math.Round(maxExtentMeters * GetTriangleStride(levelOfDetail) / CoarsestStride, 3);
    }

    private static Tiles3DMeshPrimitive Simplify(Tiles3DMeshPrimitive source, int stride)
    {
        if (source.Triangles.Count <= 16)
        {
            return ClonePrimitive(source);
        }

        int targetCount = source.Triangles.Count / stride;
        ClusterBounds bounds = ClusterBounds.From(source.Triangles);
        List<Tiles3DTriangle> best = Cluster(source.Triangles, bounds, MinGridResolution);

        int low = MinGridResolution + 1;
        int high = Math.Min(targetCount, MaxGridResolution);
        while (low <= high)
        {
            int resolution = low + ((high - low) / 2);
            List<Tiles3DTriangle> candidate = Cluster(source.Triangles, bounds, resolution);
            if (candidate.Count > targetCount)
            {
                high = resolution - 1;
                continue;
            }

            if (candidate.Count > 0)
            {
                best = candidate;
            }

            low = resolution + 1;
        }

        return new Tiles3DMeshPrimitive
        {
            Metadata = source.Metadata.Clone(),
            Triangles = best.Count > 0 ? best : source.Triangles.ToList()
        };
    }

    private static List<Tiles3DTriangle> Cluster(IReadOnlyList<Tiles3DTriangle> triangles, ClusterBounds bounds, int resolution)
    {
        // The vertex farthest from the mesh centre represents its cell, which keeps corners and
        // silhouettes in place and never grows the mesh's bounds.
        Dictionary<long, Tiles3DPoint> representatives = new Dictionary<long, Tiles3DPoint>();
        foreach (Tiles3DTriangle triangle in triangles)
        {
            KeepFarthest(representatives, bounds, resolution, triangle.A);
            KeepFarthest(representatives, bounds, resolution, triangle.B);
            KeepFarthest(representatives, bounds, resolution, triangle.C);
        }

        HashSet<(long, long, long, Tiles3DMaterialColor?)> seen = new HashSet<(long, long, long, Tiles3DMaterialColor?)>();
        List<Tiles3DTriangle> retained = new List<Tiles3DTriangle>();
        foreach (Tiles3DTriangle triangle in triangles)
        {
            long a = bounds.CellKey(triangle.A, resolution);
            long b = bounds.CellKey(triangle.B, resolution);
            long c = bounds.CellKey(triangle.C, resolution);
            if (a == b || b == c || a == c)
            {
                continue;
            }

            // Rotating keeps the winding, so a thin solid's front and back faces both survive.
            (long first, long second, long third) = a < b && a < c ? (a, b, c) : b < c ? (b, c, a) : (c, a, b);
            if (seen.Add((first, second, third, triangle.MaterialColor)))
            {
                retained.Add(new Tiles3DTriangle(representatives[a], representatives[b], representatives[c], triangle.MaterialColor));
            }
        }

        return retained;
    }

    private static void KeepFarthest(Dictionary<long, Tiles3DPoint> representatives, ClusterBounds bounds, int resolution, Tiles3DPoint point)
    {
        long key = bounds.CellKey(point, resolution);
        if (!representatives.TryGetValue(key, out Tiles3DPoint current)
            || bounds.DistanceSquaredFromCenter(point) > bounds.DistanceSquaredFromCenter(current))
        {
            representatives[key] = point;
        }
    }

    private static Tiles3DMeshPrimitive ClonePrimitive(Tiles3DMeshPrimitive source)
    {
        return new Tiles3DMeshPrimitive
        {
            Metadata = source.Metadata.Clone(),
            Triangles = source.Triangles.ToList()
        };
    }

    private readonly struct ClusterBounds
    {
        private readonly double minX;
        private readonly double minY;
        private readonly double minZ;
        private readonly double extentX;
        private readonly double extentY;
        private readonly double extentZ;

        private ClusterBounds(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            this.minX = minX;
            this.minY = minY;
            this.minZ = minZ;
            extentX = maxX - minX;
            extentY = maxY - minY;
            extentZ = maxZ - minZ;
        }

        public static ClusterBounds From(IReadOnlyList<Tiles3DTriangle> triangles)
        {
            IEnumerable<Tiles3DPoint> points = triangles.SelectMany(triangle => new[] { triangle.A, triangle.B, triangle.C });
            double minX = double.MaxValue, minY = double.MaxValue, minZ = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue, maxZ = double.MinValue;
            foreach (Tiles3DPoint point in points)
            {
                minX = Math.Min(minX, point.X);
                minY = Math.Min(minY, point.Y);
                minZ = Math.Min(minZ, point.Z);
                maxX = Math.Max(maxX, point.X);
                maxY = Math.Max(maxY, point.Y);
                maxZ = Math.Max(maxZ, point.Z);
            }

            return new ClusterBounds(minX, minY, minZ, maxX, maxY, maxZ);
        }

        public long CellKey(Tiles3DPoint point, int resolution)
        {
            return AxisCell(point.X, minX, extentX, resolution)
                | (AxisCell(point.Y, minY, extentY, resolution) << CellBits)
                | (AxisCell(point.Z, minZ, extentZ, resolution) << (CellBits * 2));
        }

        public double DistanceSquaredFromCenter(Tiles3DPoint point)
        {
            double dx = point.X - (minX + (extentX / 2d));
            double dy = point.Y - (minY + (extentY / 2d));
            double dz = point.Z - (minZ + (extentZ / 2d));
            return (dx * dx) + (dy * dy) + (dz * dz);
        }

        private static long AxisCell(double value, double min, double extent, int resolution)
        {
            if (!(extent > 0d))
            {
                return 0L;
            }

            double scaled = (value - min) / extent * resolution;
            return scaled > 0d ? Math.Min(resolution - 1L, (long)scaled) : 0L;
        }
    }
}
