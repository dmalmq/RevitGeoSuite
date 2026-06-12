using System;
using System.Collections.Generic;
using RevitGeoSuite.Core.Plateau.Tiles3D;

namespace RevitGeoSuite.Core.Plateau.Dem;

/// <summary>
/// Uniform XY-bucket spatial index over a PLATEAU DEM tileset's triangles (all in project CRS).
/// Provides bilinear-equivalent ground elevation lookups for building-footprint flattening.
/// </summary>
public sealed class DemSampler
{
    private readonly Dictionary<long, List<DemTriangle>> buckets = new();
    private readonly double cellSize;
    private readonly double minX;
    private readonly double minY;
    private readonly double maxX;
    private readonly double maxY;
    private readonly List<DemTriangle> triangles;

    public DemSampler(PlateauTilesetModel demModel, double cellSizeMeters = 0)
        : this(EnumerateTriangles(demModel), cellSizeMeters)
    {
    }

    private const int MaxGridCells = 10_000_000;
    private const int MaxCellsPerTriangle = 10_000;

    public DemSampler(IEnumerable<(Vector3d A, Vector3d B, Vector3d C)> sourceTriangles, double cellSizeMeters = 0)
    {
        if (sourceTriangles is null) throw new ArgumentNullException(nameof(sourceTriangles));

        triangles = new List<DemTriangle>();
        double sumArea = 0;
        double minX = double.PositiveInfinity, minY = double.PositiveInfinity;
        double maxX = double.NegativeInfinity, maxY = double.NegativeInfinity;

        foreach ((Vector3d a, Vector3d b, Vector3d c) in sourceTriangles)
        {
            DemTriangle dt = new DemTriangle(a, b, c);
            triangles.Add(dt);
            sumArea += dt.Area2d;
            if (dt.MinX < minX) minX = dt.MinX;
            if (dt.MinY < minY) minY = dt.MinY;
            if (dt.MaxX > maxX) maxX = dt.MaxX;
            if (dt.MaxY > maxY) maxY = dt.MaxY;
        }

        if (triangles.Count == 0)
        {
            this.cellSize = 1.0;
            this.minX = this.minY = 0;
            this.maxX = this.maxY = 0;
            return;
        }

        if (cellSizeMeters > 0)
        {
            this.cellSize = cellSizeMeters;
        }
        else
        {
            double mean = sumArea / triangles.Count;
            this.cellSize = Math.Max(1.0, Math.Min(50.0, Math.Sqrt(mean)));
        }
        this.minX = minX;
        this.minY = minY;
        this.maxX = maxX;
        this.maxY = maxY;

        double extentX = maxX - minX;
        double extentY = maxY - minY;
        if (extentX > 0 && extentY > 0)
        {
            double gridCells = (extentX / this.cellSize) * (extentY / this.cellSize);
            if (gridCells > MaxGridCells)
            {
                this.cellSize = Math.Sqrt((extentX * extentY) / MaxGridCells);
            }
        }

        foreach (DemTriangle t in triangles)
        {
            int x0 = CellIndex(t.MinX, true);
            int x1 = CellIndex(t.MaxX, false);
            int y0 = CellIndex(t.MinY, true);
            int y1 = CellIndex(t.MaxY, false);

            int spanX = x1 - x0 + 1;
            int spanY = y1 - y0 + 1;
            if ((long)spanX * spanY > MaxCellsPerTriangle)
            {
                double shrink = Math.Sqrt(MaxCellsPerTriangle / ((double)spanX * spanY));
                int shrunkSpanX = Math.Max(1, (int)(spanX * shrink));
                int shrunkSpanY = Math.Max(1, (int)(spanY * shrink));
                int midX = (x0 + x1) / 2;
                int midY = (y0 + y1) / 2;
                x0 = midX - shrunkSpanX / 2;
                x1 = x0 + shrunkSpanX - 1;
                y0 = midY - shrunkSpanY / 2;
                y1 = y0 + shrunkSpanY - 1;
            }

            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    long key = MakeKey(x, y);
                    if (!buckets.TryGetValue(key, out List<DemTriangle>? list))
                    {
                        list = new List<DemTriangle>();
                        buckets[key] = list;
                    }
                    list.Add(t);
                }
            }
        }
    }

    public int TriangleCount => triangles.Count;

    /// <summary>True when the sampler holds no triangles, so the bounds are meaningless.</summary>
    public bool IsEmpty => triangles.Count == 0;

    /// <summary>Min/max XY extent of the sampled triangles, in the sampler's coordinate frame.</summary>
    public double MinX => minX;

    public double MinY => minY;

    public double MaxX => maxX;

    public double MaxY => maxY;

    public bool TrySampleElevation(double x, double y, out double z)
    {
        z = 0;
        if (triangles.Count == 0) return false;
        if (x < minX || x > maxX || y < minY || y > maxY) return false;

        long key = MakeKey(CellIndex(x, true), CellIndex(y, true));
        if (!buckets.TryGetValue(key, out List<DemTriangle>? cell)) return false;

        foreach (DemTriangle t in cell)
        {
            if (t.TryInterpolateZ(x, y, out double z0))
            {
                z = z0;
                return true;
            }
        }
        return false;
    }

    /// <summary>Falls back to the nearest triangle centroid Z if no triangle contains (x,y).</summary>
    public double SampleElevationOrNearest(double x, double y, out bool exact)
    {
        if (TrySampleElevation(x, y, out double z))
        {
            exact = true;
            return z;
        }
        exact = false;
        double bestSq = double.PositiveInfinity;
        double bestZ = 0;
        foreach (DemTriangle t in triangles)
        {
            double dx = t.CentroidX - x;
            double dy = t.CentroidY - y;
            double d2 = dx * dx + dy * dy;
            if (d2 < bestSq)
            {
                bestSq = d2;
                bestZ = t.CentroidZ;
            }
        }
        return bestZ;
    }

    private int CellIndex(double coord, bool min)
    {
        if (min) return (int)Math.Floor(coord / cellSize);
        return (int)Math.Floor(coord / cellSize);
    }

    private static long MakeKey(int cellX, int cellY) =>
        ((long)cellX & 0xFFFFFFFFL) << 32 | ((long)cellY & 0xFFFFFFFFL);

    private static IEnumerable<(Vector3d A, Vector3d B, Vector3d C)> EnumerateTriangles(PlateauTilesetModel demModel)
    {
        if (demModel is null) throw new ArgumentNullException(nameof(demModel));
        foreach (PlateauTilesetFeature feature in demModel.Features)
        {
            foreach (PlateauTilesetTriangle t in feature.Triangles)
            {
                yield return (t.A, t.B, t.C);
            }
        }
    }
}

internal sealed class DemTriangle
{
    public DemTriangle(Vector3d a, Vector3d b, Vector3d c)
    {
        A = a; B = b; C = c;
        MinX = Math.Min(a.X, Math.Min(b.X, c.X));
        MaxX = Math.Max(a.X, Math.Max(b.X, c.X));
        MinY = Math.Min(a.Y, Math.Min(b.Y, c.Y));
        MaxY = Math.Max(a.Y, Math.Max(b.Y, c.Y));
        Area2d = 0.5 * Math.Abs((b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y));
        CentroidX = (a.X + b.X + c.X) / 3.0;
        CentroidY = (a.Y + b.Y + c.Y) / 3.0;
        CentroidZ = (a.Z + b.Z + c.Z) / 3.0;
    }

    public Vector3d A { get; }
    public Vector3d B { get; }
    public Vector3d C { get; }
    public double MinX { get; }
    public double MaxX { get; }
    public double MinY { get; }
    public double MaxY { get; }
    public double Area2d { get; }
    public double CentroidX { get; }
    public double CentroidY { get; }
    public double CentroidZ { get; }

    public bool TryInterpolateZ(double x, double y, out double z)
    {
        // Barycentric coordinates in 2D.
        double denom = (B.Y - C.Y) * (A.X - C.X) + (C.X - B.X) * (A.Y - C.Y);
        if (Math.Abs(denom) < 1e-12)
        {
            z = 0;
            return false;
        }
        double w1 = ((B.Y - C.Y) * (x - C.X) + (C.X - B.X) * (y - C.Y)) / denom;
        double w2 = ((C.Y - A.Y) * (x - C.X) + (A.X - C.X) * (y - C.Y)) / denom;
        double w3 = 1.0 - w1 - w2;

        const double eps = 1e-9;
        if (w1 < -eps || w2 < -eps || w3 < -eps)
        {
            z = 0;
            return false;
        }
        z = w1 * A.Z + w2 * B.Z + w3 * C.Z;
        return true;
    }
}
