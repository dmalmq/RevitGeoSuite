using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>The numbers of the stair drawing rule, measured from the Tokyo and Shinjuku reference GDBs.</summary>
public sealed class StairDrawingSettings
{
    /// <summary>Distance between step lines along the walking path. 0.30 is the other allowed value.</summary>
    public double StepSpacingMeters { get; set; } = 0.60d;

    /// <summary>How far before the middle of the stair each floor's step lines stop.</summary>
    public double LineGapBeforeMiddleMeters { get; set; } = 1.10d;

    /// <summary>How much the lower and upper floor polygons overlap at the middle. 0 tiles them exactly.</summary>
    public double FloorOverlapMeters { get; set; } = 1.80d;

    public static StairDrawingSettings Default => new();

    public StairDrawingSettings GetEffective()
    {
        return new StairDrawingSettings
        {
            StepSpacingMeters = StepSpacingMeters > 0.05d ? StepSpacingMeters : 0.60d,
            LineGapBeforeMiddleMeters = Math.Max(0d, LineGapBeforeMiddleMeters),
            FloorOverlapMeters = Math.Max(0d, FloorOverlapMeters),
        };
    }
}

/// <summary>Which part of a stair a floor shows.</summary>
public enum StairFloorSide
{
    /// <summary>The floor the stair starts on: from the bottom end to just past the middle.</summary>
    Lower,

    /// <summary>The floor the stair arrives at: from just before the middle to the top end.</summary>
    Upper,
}

public enum StairPieceKind
{
    Run,
    Landing,
}

/// <summary>
/// One run or landing of a stair in plan. <see cref="Path"/> follows the walking line upward;
/// for a landing it joins the end of the run below to the start of the run above.
/// </summary>
public sealed class StairPathPiece
{
    public StairPathPiece(StairPieceKind kind, IReadOnlyList<Point2D> path, Polygon2D footprint, string? key = null)
    {
        Kind = kind;
        Path = path ?? throw new ArgumentNullException(nameof(path));
        Footprint = footprint ?? throw new ArgumentNullException(nameof(footprint));
        Key = key;
    }

    public StairPieceKind Kind { get; }

    public IReadOnlyList<Point2D> Path { get; }

    public Polygon2D Footprint { get; }

    /// <summary>The run's own id, so a step line can be keyed to the run it lies on.</summary>
    public string? Key { get; }
}

/// <summary>A step line and the run it lies on.</summary>
public sealed class StairStepLine
{
    public StairStepLine(LineString2D line, StairPathPiece run, double stationMeters)
    {
        Line = line;
        Run = run;
        StationMeters = stationMeters;
    }

    public LineString2D Line { get; }

    public StairPathPiece Run { get; }

    /// <summary>Distance from the bottom of the stair along the walking path.</summary>
    public double StationMeters { get; }
}

/// <summary>
/// The fixed drawing rule for stairs. Each floor shows one polygon with a straight cut across the
/// walking path: the lower floor up to <see cref="StairDrawingSettings.FloorOverlapMeters"/>/2 past
/// the middle, the upper floor from the same distance before it. Step lines sit every
/// <see cref="StairDrawingSettings.StepSpacingMeters"/> on one grid that starts one spacing above
/// the bottom end, run wall to wall across the run, and stop
/// <see cref="StairDrawingSettings.LineGapBeforeMiddleMeters"/> short of the middle on each floor.
/// Landings get no lines. There are no arrows, closing lines or break lines.
/// </summary>
public sealed class StairDrawingRule
{
    private const double FarDistanceMeters = 10_000d;
    private const double MinimumPieceAreaSquareMeters = 1e-4d;
    private const double MinimumLineLengthMeters = 0.05d;
    private const double RunEndToleranceMeters = 1e-6d;

    private static readonly GeometryFactory Factory = new();

    private readonly List<(StairPathPiece Piece, double Start, double End)> _pieces = new();

    public StairDrawingRule(IReadOnlyList<StairPathPiece> piecesBottomToTop)
    {
        if (piecesBottomToTop is null)
        {
            throw new ArgumentNullException(nameof(piecesBottomToTop));
        }

        double station = 0d;
        foreach (StairPathPiece piece in piecesBottomToTop)
        {
            double length = PathLength(piece.Path);
            _pieces.Add((piece, station, station + length));
            station += length;
        }

        TotalLengthMeters = station;
    }

    /// <summary>Length of the walking path, landings included.</summary>
    public double TotalLengthMeters { get; }

    public double MiddleMeters => TotalLengthMeters * 0.5d;

    public bool HasRuns => _pieces.Any(entry => entry.Piece.Kind == StairPieceKind.Run && entry.End > entry.Start);

    /// <summary>The station range this floor's polygon covers.</summary>
    public (double From, double To) GetPolygonRange(StairFloorSide side, StairDrawingSettings settings)
    {
        StairDrawingSettings effective = (settings ?? StairDrawingSettings.Default).GetEffective();
        double half = Math.Min(effective.FloorOverlapMeters * 0.5d, MiddleMeters);
        return side == StairFloorSide.Lower
            ? (0d, MiddleMeters + half)
            : (MiddleMeters - half, TotalLengthMeters);
    }

    /// <summary>The stations of this floor's step lines, on the grid shared by both floors.</summary>
    public IReadOnlyList<double> GetStepStations(StairFloorSide side, StairDrawingSettings settings)
    {
        StairDrawingSettings effective = (settings ?? StairDrawingSettings.Default).GetEffective();
        double spacing = effective.StepSpacingMeters;
        double from = side == StairFloorSide.Lower ? 0d : MiddleMeters + effective.LineGapBeforeMiddleMeters;
        double to = side == StairFloorSide.Lower
            ? MiddleMeters - effective.LineGapBeforeMiddleMeters
            : TotalLengthMeters - (spacing * 0.5d);
        to = Math.Min(to, TotalLengthMeters - (spacing * 0.5d));

        List<double> stations = new();
        for (int k = 1; ; k++)
        {
            double station = k * spacing;
            if (station > to + 1e-9d)
            {
                break;
            }

            if (station >= from - 1e-9d && TryFindRun(station, out _))
            {
                stations.Add(station);
            }
        }

        return stations;
    }

    /// <summary>The floor's polygons: every piece cut to the floor's range, then joined.</summary>
    public IReadOnlyList<Polygon2D> BuildFloorPolygons(StairFloorSide side, StairDrawingSettings settings)
    {
        (double from, double to) = GetPolygonRange(side, settings);
        List<NetTopologySuite.Geometries.Geometry> parts = new();
        foreach ((StairPathPiece piece, double start, double end) in _pieces)
        {
            NetTopologySuite.Geometries.Geometry? part = ClipPiece(piece, start, end, from, to);
            if (part != null && !part.IsEmpty && part.Area > MinimumPieceAreaSquareMeters)
            {
                parts.Add(part);
            }
        }

        if (parts.Count == 0)
        {
            return Array.Empty<Polygon2D>();
        }

        NetTopologySuite.Geometries.Geometry joined = UnaryUnionOp.Union(parts);
        return ToPolygons(joined);
    }

    /// <summary>The floor's step lines, each across its run from wall to wall.</summary>
    public IReadOnlyList<StairStepLine> BuildStepLines(StairFloorSide side, StairDrawingSettings settings)
    {
        List<StairStepLine> lines = new();
        foreach (double station in GetStepStations(side, settings))
        {
            if (!TryFindRun(station, out (StairPathPiece Piece, double Start, double End) run))
            {
                continue;
            }

            if (!TryPointOnPath(run.Piece.Path, station - run.Start, out Point2D point, out Point2D direction))
            {
                continue;
            }

            LineString2D? line = CrossLine(run.Piece.Footprint, point, direction);
            if (line != null)
            {
                lines.Add(new StairStepLine(line, run.Piece, station));
            }
        }

        return lines;
    }

    private bool TryFindRun(double station, out (StairPathPiece Piece, double Start, double End) run)
    {
        foreach ((StairPathPiece Piece, double Start, double End) entry in _pieces)
        {
            // Strictly inside the run: a station on a run's end is the edge of the landing or
            // the stair's end, and a line there would be the closing line the rule leaves out.
            if (entry.Piece.Kind == StairPieceKind.Run &&
                entry.End > entry.Start &&
                station > entry.Start + RunEndToleranceMeters &&
                station < entry.End - RunEndToleranceMeters)
            {
                run = entry;
                return true;
            }
        }

        run = default;
        return false;
    }

    private static NetTopologySuite.Geometries.Geometry? ClipPiece(
        StairPathPiece piece,
        double start,
        double end,
        double from,
        double to)
    {
        NetTopologySuite.Geometries.Geometry footprint = ToGeometry(piece.Footprint);
        if (end <= start)
        {
            // A piece without a walking path, such as a landing at the very top, belongs wholly
            // to the floor whose range holds its station.
            return start >= from && start <= to ? footprint : null;
        }

        if (end <= from || start >= to)
        {
            return null;
        }

        NetTopologySuite.Geometries.Geometry clipped = footprint;
        if (from > start && TryPointOnPath(piece.Path, from - start, out Point2D lowCut, out Point2D lowDirection))
        {
            clipped = clipped.Intersection(HalfPlane(lowCut, lowDirection));
        }

        if (to < end && TryPointOnPath(piece.Path, to - start, out Point2D highCut, out Point2D highDirection))
        {
            clipped = clipped.Intersection(HalfPlane(highCut, Negate(highDirection)));
        }

        return clipped;
    }

    /// <summary>The half plane on the side <paramref name="direction"/> points to, bounded by the line across it at <paramref name="origin"/>.</summary>
    private static Polygon HalfPlane(Point2D origin, Point2D direction)
    {
        Point2D across = new(-direction.Y, direction.X);
        Point2D a = Offset(origin, across, FarDistanceMeters);
        Point2D b = Offset(origin, across, -FarDistanceMeters);
        Point2D c = Offset(b, direction, FarDistanceMeters);
        Point2D d = Offset(a, direction, FarDistanceMeters);
        return Factory.CreatePolygon(new[]
        {
            new Coordinate(a.X, a.Y),
            new Coordinate(b.X, b.Y),
            new Coordinate(c.X, c.Y),
            new Coordinate(d.X, d.Y),
            new Coordinate(a.X, a.Y),
        });
    }

    private static LineString2D? CrossLine(Polygon2D footprint, Point2D point, Point2D direction)
    {
        Point2D across = new(-direction.Y, direction.X);
        Point2D a = Offset(point, across, FarDistanceMeters);
        Point2D b = Offset(point, across, -FarDistanceMeters);
        LineString probe = Factory.CreateLineString(new[] { new Coordinate(a.X, a.Y), new Coordinate(b.X, b.Y) });
        NetTopologySuite.Geometries.Geometry inside = ToGeometry(footprint).Intersection(probe);

        // The footprint can be L-shaped; keep the piece of the probe that holds the path point.
        NetTopologySuite.Geometries.Point pathPoint = Factory.CreatePoint(new Coordinate(point.X, point.Y));
        LineString? best = null;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < inside.NumGeometries; i++)
        {
            if (inside.GetGeometryN(i) is not LineString segment || segment.Length < MinimumLineLengthMeters)
            {
                continue;
            }

            double distance = segment.Distance(pathPoint);
            if (distance < bestDistance)
            {
                best = segment;
                bestDistance = distance;
            }
        }

        if (best == null)
        {
            return null;
        }

        Coordinate first = best.GetCoordinateN(0);
        Coordinate last = best.GetCoordinateN(best.NumPoints - 1);
        return new LineString2D(new[] { new Point2D(first.X, first.Y), new Point2D(last.X, last.Y) });
    }

    /// <summary>
    /// True when a straight cut across the walking path at any point splits the run into the part
    /// before and the part after it. That holds while the path turns less than half a circle in
    /// all; a spiral or S-shaped run that turns more can fold back across its own cut line.
    /// </summary>
    public static bool CanCutAcross(IReadOnlyList<Point2D> path, double maximumTurnDegrees = 170d)
    {
        if (path is null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        double totalTurn = 0d;
        double? previousHeading = null;
        for (int i = 1; i < path.Count; i++)
        {
            double dx = path[i].X - path[i - 1].X;
            double dy = path[i].Y - path[i - 1].Y;
            if ((dx * dx) + (dy * dy) <= 1e-18d)
            {
                continue;
            }

            double heading = Math.Atan2(dy, dx);
            if (previousHeading.HasValue)
            {
                double turn = heading - previousHeading.Value;
                while (turn > Math.PI)
                {
                    turn -= 2d * Math.PI;
                }

                while (turn < -Math.PI)
                {
                    turn += 2d * Math.PI;
                }

                totalTurn += Math.Abs(turn);
            }

            previousHeading = heading;
        }

        return totalTurn * 180d / Math.PI <= maximumTurnDegrees;
    }

    internal static bool TryPointOnPath(IReadOnlyList<Point2D> path, double distance, out Point2D point, out Point2D direction)
    {
        point = default;
        direction = default;
        if (path.Count < 2)
        {
            return false;
        }

        double remaining = Math.Max(0d, distance);
        for (int i = 1; i < path.Count; i++)
        {
            Point2D from = path[i - 1];
            Point2D to = path[i];
            double length = Distance(from, to);
            if (length <= 1e-9d)
            {
                continue;
            }

            bool last = i == path.Count - 1;
            if (remaining <= length || last)
            {
                double t = Math.Min(1d, remaining / length);
                point = new Point2D(from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
                direction = new Point2D((to.X - from.X) / length, (to.Y - from.Y) / length);
                return true;
            }

            remaining -= length;
        }

        return false;
    }

    internal static double PathLength(IReadOnlyList<Point2D> path)
    {
        double length = 0d;
        for (int i = 1; i < path.Count; i++)
        {
            length += Distance(path[i - 1], path[i]);
        }

        return length;
    }

    private static IReadOnlyList<Polygon2D> ToPolygons(NetTopologySuite.Geometries.Geometry geometry)
    {
        List<Polygon2D> polygons = new();
        for (int i = 0; i < geometry.NumGeometries; i++)
        {
            if (geometry.GetGeometryN(i) is not Polygon polygon || polygon.Area <= MinimumPieceAreaSquareMeters)
            {
                continue;
            }

            polygons.Add(new Polygon2D(
                ToPoints(polygon.ExteriorRing),
                polygon.InteriorRings.Select(ring => (IReadOnlyList<Point2D>)ToPoints(ring)).ToArray()));
        }

        return polygons;
    }

    private static Point2D[] ToPoints(LineString ring)
    {
        return ring.Coordinates.Select(coordinate => new Point2D(coordinate.X, coordinate.Y)).ToArray();
    }

    private static NetTopologySuite.Geometries.Geometry ToGeometry(Polygon2D polygon)
    {
        LinearRing shell = Factory.CreateLinearRing(ToRing(polygon.ExteriorRing));
        LinearRing[] holes = polygon.InteriorRings.Select(ring => Factory.CreateLinearRing(ToRing(ring))).ToArray();
        NetTopologySuite.Geometries.Geometry created = Factory.CreatePolygon(shell, holes);
        return created.IsValid ? created : created.Buffer(0d);
    }

    private static Coordinate[] ToRing(IReadOnlyList<Point2D> points)
    {
        List<Coordinate> coordinates = points.Select(point => new Coordinate(point.X, point.Y)).ToList();
        if (coordinates.Count > 0 && !coordinates[0].Equals2D(coordinates[coordinates.Count - 1]))
        {
            coordinates.Add(coordinates[0].Copy());
        }

        return coordinates.ToArray();
    }

    private static Point2D Offset(Point2D point, Point2D direction, double distance)
    {
        return new Point2D(point.X + (direction.X * distance), point.Y + (direction.Y * distance));
    }

    private static Point2D Negate(Point2D direction)
    {
        return new Point2D(-direction.X, -direction.Y);
    }

    private static double Distance(Point2D a, Point2D b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}
