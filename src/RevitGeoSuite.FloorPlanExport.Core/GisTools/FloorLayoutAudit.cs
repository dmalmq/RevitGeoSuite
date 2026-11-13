using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>A floor as the export takes it: its level, its resolved unit category, its top outline in feet, and how far its top rises.</summary>
public sealed class LayoutFloor
{
    public LayoutFloor(long elementId, long levelId, string category, IEnumerable<Polygon2D> outline, double riseFeet = 0)
    {
        ElementId = elementId;
        LevelId = levelId;
        Category = GisToolStandard.Normalize(category).ToLowerInvariant();
        Outline = outline.ToList();
        RiseFeet = riseFeet;
    }

    public long ElementId { get; }

    public long LevelId { get; }

    public string Category { get; }

    public IReadOnlyList<Polygon2D> Outline { get; }

    /// <summary>The top face's highest point above its lowest, in feet; 0 for a level floor.</summary>
    public double RiseFeet { get; }
}

/// <summary>A way onto a floor on one level (a door, window, accepted opening, or a stair or escalator end): the corners of its plan footprint, in feet.</summary>
public sealed class LayoutOpening
{
    public LayoutOpening(long elementId, long levelId, IEnumerable<Point2D> footprint)
    {
        ElementId = elementId;
        LevelId = levelId;
        Footprint = footprint.ToList();
    }

    public long ElementId { get; }

    public long LevelId { get; }

    public IReadOnlyList<Point2D> Footprint { get; }
}

/// <summary>
/// The geometry checks of the export audit, per level: floors that overlap, floors with a narrow
/// gap between them, walkable or shop floors nothing leads into, and sloped floors that are not a
/// slope type. Each finding gives the measured value. A level with more findings of one kind than
/// <see cref="GroupAbove"/> gets one finding for them all, so a model built another way does not
/// flood the list.
/// </summary>
public static class FloorLayoutAudit
{
    /// <summary>Two floors overlapping by more than this, in m², give one area to two units.</summary>
    public const double OverlapSquareMetres = 0.5;

    /// <summary>Floor edges closer than this, in metres, count as shared (A2's 5 cm).</summary>
    public const double TouchMetres = 0.05;

    /// <summary>A shared edge must run at least this far, in metres, to connect two floors (A2's 20 cm).</summary>
    public const double SharedEdgeMetres = 0.2;

    /// <summary>Floors further apart than <see cref="TouchMetres"/> but closer than this, in metres, leave a gap the network cannot cross.</summary>
    public const double NarrowGapMetres = 0.3;

    /// <summary>A door, opening, stair or escalator this close to a floor's outline, in metres, leads into it.</summary>
    public const double OpeningReachMetres = 0.5;

    /// <summary>A top face rising more than this, in metres, is sloped.</summary>
    public const double SlopeRiseMetres = 0.05;

    /// <summary>More findings of one kind on one level than this become one finding for the level.</summary>
    public const int GroupAbove = 10;

    public const string SlopeCategory = "ramp";

    private const double MetresPerFoot = 0.3048;

    private static readonly HashSet<string> Walkable = new(StringComparer.Ordinal) { "walkway", "platform", "ramp" };

    private static readonly HashSet<string> NeedsAWayIn = new(StringComparer.Ordinal) { "walkway", "platform", "ramp", "retail" };

    private static readonly GeometryFactory Factory = new();

    public static IEnumerable<GisAuditFinding> Run(IEnumerable<LayoutFloor> floors, IEnumerable<LayoutOpening> openings, Func<long, string> levelName)
    {
        Dictionary<long, List<LayoutOpening>> openingsByLevel = openings.GroupBy(opening => opening.LevelId).ToDictionary(group => group.Key, group => group.ToList());
        foreach (IGrouping<long, LayoutFloor> level in floors.GroupBy(floor => floor.LevelId).OrderBy(level => levelName(level.Key), StringComparer.Ordinal))
        {
            List<(LayoutFloor Floor, NtsGeometry Shape)> shapes = level
                .Select(floor => (floor, Shape(floor.Outline)))
                .Where(entry => entry.Item2 != null && !entry.Item2.IsEmpty)
                .Select(entry => (entry.floor, entry.Item2!))
                .ToList();
            string name = levelName(level.Key);
            List<Measured> overlaps = new();
            List<Measured> gaps = new();
            Dictionary<long, bool> connected = shapes.ToDictionary(entry => entry.Floor.ElementId, _ => false);
            for (int i = 0; i < shapes.Count; i++)
            {
                for (int j = i + 1; j < shapes.Count; j++)
                {
                    Pair(shapes[i], shapes[j], overlaps, gaps, connected);
                }
            }

            List<NtsGeometry> doors = (openingsByLevel.TryGetValue(level.Key, out List<LayoutOpening>? found) ? found : new List<LayoutOpening>())
                .Select(opening => Footprint(opening.Footprint))
                .Where(shape => shape != null)
                .Select(shape => shape!)
                .ToList();
            List<Measured> unreached = shapes
                .Where(entry => NeedsAWayIn.Contains(entry.Floor.Category) && !connected[entry.Floor.ElementId])
                .Where(entry => !doors.Any(door => entry.Shape.Boundary.Distance(door) <= OpeningReachMetres))
                .Select(entry => new Measured(new[] { entry.Floor.ElementId }, 0, entry.Floor.Category))
                .ToList();
            List<Measured> slopes = level
                .Where(floor => floor.RiseFeet * MetresPerFoot > SlopeRiseMetres && floor.Category != SlopeCategory)
                .Select(floor => new Measured(new[] { floor.ElementId }, floor.RiseFeet * MetresPerFoot, floor.Category))
                .ToList();

            foreach (GisAuditFinding finding in Report(
                overlaps,
                GisAuditSeverity.Warning,
                pair => string.Format(CultureInfo.InvariantCulture, "Floors {0} and {1} on level {2} overlap by {3:0.0} m², so the export gives that area to two units.", pair.Ids[0], pair.Ids[1], name, pair.Value),
                all => string.Format(CultureInfo.InvariantCulture, "Level {0} has {1} pairs of overlapping floors (up to {2:0.0} m²), so the export gives those areas to two units.", name, all.Count, all.Max(pair => pair.Value))))
            {
                yield return finding;
            }

            foreach (GisAuditFinding finding in Report(
                gaps,
                GisAuditSeverity.Warning,
                pair => string.Format(CultureInfo.InvariantCulture, "Floors {0} and {1} on level {2} are {3:0.00} m apart, a gap the network cannot cross. Close it if people walk across.", pair.Ids[0], pair.Ids[1], name, pair.Value),
                all => string.Format(CultureInfo.InvariantCulture, "Level {0} has {1} pairs of floors {2:0.00} to {3:0.00} m apart, gaps the network cannot cross. Close them where people walk across.", name, all.Count, all.Min(pair => pair.Value), all.Max(pair => pair.Value))))
            {
                yield return finding;
            }

            foreach (GisAuditFinding finding in Report(
                unreached,
                GisAuditSeverity.Info,
                floor => string.Format(CultureInfo.InvariantCulture, "Floor {0} ({1}) on level {2} has no door, opening, stair or escalator within {3:0.0} m and shares no edge with a walkable floor, so the network cannot reach it.", floor.Ids[0], floor.Category, name, OpeningReachMetres),
                all => string.Format(CultureInfo.InvariantCulture, "Level {0} has {1} walkable or shop floors with no door, opening, stair or escalator within {2:0.0} m and no edge shared with a walkable floor, so the network cannot reach them.", name, all.Count, OpeningReachMetres)))
            {
                yield return finding;
            }

            foreach (GisAuditFinding finding in Report(
                slopes,
                GisAuditSeverity.Warning,
                floor => string.Format(CultureInfo.InvariantCulture, "Floor {0} ({1}) on level {2} rises {3:0.00} m but is not a slope type, so it exports as a flat {1} unit. Give it the type j ラチ外スロープ_床 or j ラチ内スロープ_床.", floor.Ids[0], floor.Category.Length > 0 ? floor.Category : "unit", name, floor.Value),
                all => string.Format(CultureInfo.InvariantCulture, "Level {0} has {1} floors that rise up to {2:0.00} m but are not a slope type, so they export as flat units. Give them the type j ラチ外スロープ_床 or j ラチ内スロープ_床.", name, all.Count, all.Max(floor => floor.Value))))
            {
                yield return finding;
            }
        }
    }

    private static void Pair(
        (LayoutFloor Floor, NtsGeometry Shape) a,
        (LayoutFloor Floor, NtsGeometry Shape) b,
        List<Measured> overlaps,
        List<Measured> gaps,
        Dictionary<long, bool> connected)
    {
        if (!a.Shape.EnvelopeInternal.Intersects(ExpandedEnvelope(b.Shape, NarrowGapMetres)))
        {
            return;
        }

        long[] ids = { a.Floor.ElementId, b.Floor.ElementId };
        double distance = a.Shape.Distance(b.Shape);
        if (distance <= 0)
        {
            double area = a.Shape.Intersection(b.Shape).Area;
            if (area > OverlapSquareMetres)
            {
                overlaps.Add(new Measured(ids, area, string.Empty));
            }
        }
        else if (distance > TouchMetres && distance < NarrowGapMetres)
        {
            gaps.Add(new Measured(ids, distance, string.Empty));
        }

        if (distance <= TouchMetres && SharedEdgeLength(a.Shape, b.Shape) >= SharedEdgeMetres)
        {
            if (Walkable.Contains(b.Floor.Category))
            {
                connected[a.Floor.ElementId] = true;
            }

            if (Walkable.Contains(a.Floor.Category))
            {
                connected[b.Floor.ElementId] = true;
            }
        }
    }

    /// <summary>How far <paramref name="a"/>'s outline runs within <see cref="TouchMetres"/> of <paramref name="b"/>.</summary>
    private static double SharedEdgeLength(NtsGeometry a, NtsGeometry b) =>
        a.Boundary.Intersection(b.Buffer(TouchMetres)).Length;

    private static Envelope ExpandedEnvelope(NtsGeometry shape, double by)
    {
        Envelope envelope = new(shape.EnvelopeInternal);
        envelope.ExpandBy(by);
        return envelope;
    }

    private static IEnumerable<GisAuditFinding> Report(List<Measured> items, GisAuditSeverity severity, Func<Measured, string> one, Func<List<Measured>, string> grouped)
    {
        if (items.Count > GroupAbove)
        {
            yield return new GisAuditFinding(severity, "floors", grouped(items), items.SelectMany(item => item.Ids).Distinct());
            yield break;
        }

        foreach (Measured item in items)
        {
            yield return new GisAuditFinding(severity, "floors", one(item), item.Ids);
        }
    }

    /// <summary>The outline as one geometry in metres, holes kept; null when it has no area.</summary>
    private static NtsGeometry? Shape(IReadOnlyList<Polygon2D> outline)
    {
        List<NtsGeometry> parts = new();
        foreach (Polygon2D polygon in outline)
        {
            LinearRing? shell = Ring(polygon.ExteriorRing);
            if (shell == null)
            {
                continue;
            }

            LinearRing[] holes = polygon.InteriorRings.Select(Ring).Where(ring => ring != null).Select(ring => ring!).ToArray();
            parts.Add(Factory.CreatePolygon(shell, holes).Buffer(0));
        }

        if (parts.Count == 0)
        {
            return null;
        }

        NtsGeometry union = parts.Count == 1 ? parts[0] : Factory.BuildGeometry(parts).Union();
        return union.Area > 0 ? union : null;
    }

    private static NtsGeometry? Footprint(IReadOnlyList<Point2D> points)
    {
        if (points.Count == 0)
        {
            return null;
        }

        Coordinate[] coordinates = points.Select(point => new Coordinate(point.X * MetresPerFoot, point.Y * MetresPerFoot)).ToArray();
        return Factory.CreateMultiPointFromCoords(coordinates).ConvexHull();
    }

    private static LinearRing? Ring(IReadOnlyList<Point2D> points)
    {
        List<Coordinate> coordinates = points.Select(point => new Coordinate(point.X * MetresPerFoot, point.Y * MetresPerFoot)).ToList();
        if (coordinates.Count < 3)
        {
            return null;
        }

        if (!coordinates[0].Equals2D(coordinates[coordinates.Count - 1]))
        {
            coordinates.Add(coordinates[0]);
        }

        return coordinates.Count < 4 ? null : Factory.CreateLinearRing(coordinates.ToArray());
    }

    private sealed class Measured
    {
        public Measured(IReadOnlyList<long> ids, double value, string category)
        {
            Ids = ids;
            Value = value;
            Category = category;
        }

        public IReadOnlyList<long> Ids { get; }

        public double Value { get; }

        public string Category { get; }
    }
}
