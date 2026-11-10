using System;
using System.Collections.Generic;
using System.Linq;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using Polygon = NetTopologySuite.Geometries.Polygon;

namespace RevitGeoSuite.FloorPlanExport.Core.UnitFloors;

/// <summary>A unit polygon from GIS, already in the project CRS (metres).</summary>
public sealed class UnitFloorSource
{
    public UnitFloorSource(string key, NtsGeometry? geometry, string? label = null)
    {
        Key = key ?? string.Empty;
        Geometry = geometry;
        Label = label;
    }

    /// <summary>The row's key in its file: the chosen key column, or the row's fid.</summary>
    public string Key { get; }

    public NtsGeometry? Geometry { get; }

    public string? Label { get; }
}

/// <summary>An existing Revit floor on the target level, its top outline in the project CRS (metres).</summary>
public sealed class HostFloorOutline
{
    public HostFloorOutline(string uniqueId, long elementId, long typeId, string typeName, NtsGeometry outline)
    {
        UniqueId = uniqueId ?? throw new ArgumentNullException(nameof(uniqueId));
        ElementId = elementId;
        TypeId = typeId;
        TypeName = typeName ?? string.Empty;
        Outline = outline ?? throw new ArgumentNullException(nameof(outline));
    }

    public string UniqueId { get; }

    public long ElementId { get; }

    public long TypeId { get; }

    public string TypeName { get; }

    public NtsGeometry Outline { get; }
}

public enum UnitFloorStatus
{
    /// <summary>A floor will be made with the type of the floor under it.</summary>
    Matched = 0,

    /// <summary>No existing floor lies under enough of the unit; it takes the fallback type.</summary>
    NoHost = 1,

    /// <summary>The row has no polygon (a point, a line or nothing): no floor.</summary>
    NotPolygon = 2,

    /// <summary>Another row has the same key: neither gets a floor, so the link stays one to one.</summary>
    DuplicateKey = 3,
}

public sealed class UnitFloorPlanOptions
{
    /// <summary>The share of a unit's area a floor must cover to give the unit its type.</summary>
    public double MinHostShare { get; set; } = 0.5;

    /// <summary>The share of a floor's area the planned units must cover before the floor counts as replaced.</summary>
    public double ReplaceCoverage { get; set; } = 0.95;

    /// <summary>Polygon parts smaller than this many square metres are dropped as slivers.</summary>
    public double MinPartArea { get; set; } = 0.05;
}

public sealed class PlannedUnitFloor
{
    public PlannedUnitFloor(UnitFloorSource unit, UnitFloorStatus status, IReadOnlyList<Polygon> polygons, HostFloorOutline? host, double hostShare)
    {
        Unit = unit;
        Status = status;
        Polygons = polygons;
        Host = host;
        HostShare = hostShare;
    }

    public UnitFloorSource Unit { get; }

    public UnitFloorStatus Status { get; }

    /// <summary>The unit's cleaned polygon parts. Empty when no floor will be made.</summary>
    public IReadOnlyList<Polygon> Polygons { get; }

    /// <summary>The floor whose type the unit takes, or null.</summary>
    public HostFloorOutline? Host { get; }

    /// <summary>The share of the unit's area that <see cref="Host"/> covers, 0 to 1.</summary>
    public double HostShare { get; }

    public bool WillCreate => Status == UnitFloorStatus.Matched || Status == UnitFloorStatus.NoHost;

    public double Area => Polygons.Sum(polygon => polygon.Area);
}

public sealed class HostFloorCoverage
{
    public HostFloorCoverage(HostFloorOutline host, int unitCount, double coveredShare, bool replaceable)
    {
        Host = host;
        UnitCount = unitCount;
        CoveredShare = coveredShare;
        Replaceable = replaceable;
    }

    public HostFloorOutline Host { get; }

    /// <summary>How many planned units take this floor's type.</summary>
    public int UnitCount { get; }

    /// <summary>The share of this floor's area covered by all units that will get a floor, 0 to 1.</summary>
    public double CoveredShare { get; }

    /// <summary>True when the new floors cover it well enough that deleting it loses nothing.</summary>
    public bool Replaceable { get; }
}

public sealed class UnitFloorPlan
{
    public UnitFloorPlan(IReadOnlyList<PlannedUnitFloor> units, IReadOnlyList<HostFloorCoverage> hosts)
    {
        Units = units;
        Hosts = hosts;
    }

    public IReadOnlyList<PlannedUnitFloor> Units { get; }

    public IReadOnlyList<HostFloorCoverage> Hosts { get; }

    public int CreateCount => Units.Count(unit => unit.WillCreate);

    public int CountOf(UnitFloorStatus status) => Units.Count(unit => unit.Status == status);
}

/// <summary>
/// Plans one Revit floor per GIS unit polygon. Each unit takes the type of the existing floor that
/// covers most of it, so categories carried by floor type names (for example <c>j 店舗_床</c>) follow
/// the unit. Existing floors that the new ones cover almost completely are marked replaceable.
/// Pure geometry with no Revit dependency, so it is unit tested; coordinates are metres in the
/// project CRS on both sides.
/// </summary>
public static class UnitFloorPlanner
{
    public static UnitFloorPlan Plan(
        IReadOnlyList<UnitFloorSource> units,
        IReadOnlyList<HostFloorOutline> hosts,
        UnitFloorPlanOptions? options = null)
    {
        if (units is null)
        {
            throw new ArgumentNullException(nameof(units));
        }

        if (hosts is null)
        {
            throw new ArgumentNullException(nameof(hosts));
        }

        options ??= new UnitFloorPlanOptions();

        Dictionary<string, int> keyCounts = new(StringComparer.Ordinal);
        foreach (UnitFloorSource unit in units)
        {
            keyCounts[unit.Key] = keyCounts.TryGetValue(unit.Key, out int count) ? count + 1 : 1;
        }

        List<(HostFloorOutline Host, NtsGeometry Outline)> cleanHosts = hosts
            .Select(host => (host, Clean(host.Outline)))
            .ToList();

        List<PlannedUnitFloor> planned = new(units.Count);
        foreach (UnitFloorSource unit in units)
        {
            if (string.IsNullOrWhiteSpace(unit.Key) || keyCounts[unit.Key] > 1)
            {
                planned.Add(new PlannedUnitFloor(unit, UnitFloorStatus.DuplicateKey, Array.Empty<Polygon>(), null, 0));
                continue;
            }

            IReadOnlyList<Polygon> polygons = Polygons(unit.Geometry, options.MinPartArea);
            if (polygons.Count == 0)
            {
                planned.Add(new PlannedUnitFloor(unit, UnitFloorStatus.NotPolygon, polygons, null, 0));
                continue;
            }

            NtsGeometry shape = polygons.Count == 1
                ? polygons[0]
                : polygons[0].Factory.CreateMultiPolygon(polygons.ToArray());
            double area = shape.Area;
            HostFloorOutline? best = null;
            double bestShare = 0;
            foreach ((HostFloorOutline host, NtsGeometry outline) in cleanHosts)
            {
                if (!outline.EnvelopeInternal.Intersects(shape.EnvelopeInternal))
                {
                    continue;
                }

                double share = area > 0 ? SafeIntersectionArea(outline, shape) / area : 0;
                if (share > bestShare)
                {
                    bestShare = share;
                    best = host;
                }
            }

            planned.Add(bestShare >= options.MinHostShare
                ? new PlannedUnitFloor(unit, UnitFloorStatus.Matched, polygons, best, bestShare)
                : new PlannedUnitFloor(unit, UnitFloorStatus.NoHost, polygons, null, bestShare));
        }

        List<Polygon> created = planned.Where(unit => unit.WillCreate).SelectMany(unit => unit.Polygons).ToList();
        List<HostFloorCoverage> coverage = new(cleanHosts.Count);
        foreach ((HostFloorOutline host, NtsGeometry outline) in cleanHosts)
        {
            int unitCount = planned.Count(unit => ReferenceEquals(unit.Host, host));
            double covered = 0;
            foreach (Polygon polygon in created)
            {
                if (polygon.EnvelopeInternal.Intersects(outline.EnvelopeInternal))
                {
                    covered += SafeIntersectionArea(outline, polygon);
                }
            }

            double hostArea = outline.Area;
            double share = hostArea > 0 ? Math.Min(1, covered / hostArea) : 0;
            coverage.Add(new HostFloorCoverage(host, unitCount, share, share >= options.ReplaceCoverage));
        }

        return new UnitFloorPlan(planned, coverage);
    }

    /// <summary>
    /// Polygons from the loops of one face. A Revit face can hold several separate regions, each
    /// with its own holes, so a loop is a shell when an even number of the other loops contain it
    /// and a hole of the smallest loop around it otherwise. Orientation is not trusted.
    /// </summary>
    public static IReadOnlyList<Polygon> PolygonsFromRings(IReadOnlyList<LinearRing> rings, GeometryFactory? factory = null)
    {
        if (rings is null)
        {
            throw new ArgumentNullException(nameof(rings));
        }

        factory ??= rings.Count > 0 ? rings[0].Factory : new GeometryFactory();
        List<(LinearRing Ring, Polygon Area)> sorted = rings
            .Where(ring => ring is not null && !ring.IsEmpty)
            .Select(ring => (Ring: ring, Area: factory.CreatePolygon(ring)))
            .OrderByDescending(item => item.Area.Area)
            .ToList();

        // For each loop, the larger loops that contain it, largest first.
        List<int> parent = new(sorted.Count);
        List<int> depth = new(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            Coordinate probe = sorted[i].Ring.GetCoordinateN(0);
            int found = -1;
            int count = 0;
            for (int j = 0; j < i; j++)
            {
                if (sorted[j].Area.EnvelopeInternal.Covers(sorted[i].Area.EnvelopeInternal) && Contains(sorted[j].Area, sorted[i].Area, probe))
                {
                    count++;
                    found = j;
                }
            }

            parent.Add(found);
            depth.Add(count);
        }

        List<Polygon> polygons = new();
        for (int i = 0; i < sorted.Count; i++)
        {
            if (depth[i] % 2 != 0)
            {
                continue;
            }

            LinearRing[] holes = Enumerable.Range(0, sorted.Count)
                .Where(k => depth[k] % 2 != 0 && parent[k] == i)
                .Select(k => sorted[k].Ring)
                .ToArray();
            polygons.Add(factory.CreatePolygon(sorted[i].Ring, holes));
        }

        return polygons;
    }

    private static bool Contains(Polygon outer, Polygon inner, Coordinate probe)
    {
        try
        {
            return outer.Covers(inner);
        }
        catch (TopologyException)
        {
            return outer.Covers(outer.Factory.CreatePoint(probe));
        }
    }

    /// <summary>The polygon parts of a geometry, made valid, with slivers dropped.</summary>
    public static IReadOnlyList<Polygon> Polygons(NtsGeometry? geometry, double minPartArea)
    {
        if (geometry is null || geometry.IsEmpty)
        {
            return Array.Empty<Polygon>();
        }

        NtsGeometry clean = Clean(geometry);
        List<Polygon> parts = new();
        Collect(clean, parts);
        return parts.Where(part => part.Area >= minPartArea).ToList();
    }

    private static void Collect(NtsGeometry geometry, List<Polygon> parts)
    {
        switch (geometry)
        {
            case Polygon polygon when !polygon.IsEmpty:
                parts.Add(polygon);
                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    Collect(collection.GetGeometryN(i), parts);
                }

                break;
        }
    }

    private static NtsGeometry Clean(NtsGeometry geometry)
    {
        NtsGeometry flat = geometry;
        if (geometry is Polygon || geometry is MultiPolygon || geometry is GeometryCollection)
        {
            return geometry.IsValid ? flat : flat.Buffer(0);
        }

        return geometry.Factory.CreateGeometryCollection();
    }

    private static double SafeIntersectionArea(NtsGeometry a, NtsGeometry b)
    {
        try
        {
            return a.Intersection(b).Area;
        }
        catch (TopologyException)
        {
            return a.Buffer(0).Intersection(b.Buffer(0)).Area;
        }
    }
}
