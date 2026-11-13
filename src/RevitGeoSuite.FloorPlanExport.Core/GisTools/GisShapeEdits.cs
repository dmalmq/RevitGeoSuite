using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Algorithm.Distance;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>A floor a shape edit can change: its outline in model feet, and why Revit cannot edit its sketch, if it cannot.</summary>
public sealed class ShapeFloor
{
    public ShapeFloor(string uniqueId, long elementId, IEnumerable<Polygon2D> outline, string? cannotEdit = null)
    {
        UniqueId = uniqueId;
        ElementId = elementId;
        Outline = outline.ToList();
        CannotEdit = cannotEdit;
    }

    public string UniqueId { get; }

    public long ElementId { get; }

    public IReadOnlyList<Polygon2D> Outline { get; }

    /// <summary>Why the floor's sketch cannot be redrawn (a slope arrow, shape-edited points), or null.</summary>
    public string? CannotEdit { get; }
}

public enum ShapeEditKind
{
    Reshape,
}

/// <summary>One shape edit to apply: the floor it changes, its outline now and the GDB's, both in model feet.</summary>
public sealed class ShapeEdit
{
    public ShapeEdit(ShapeEditKind kind, GisDriftShape shape, ShapeFloor floor, IReadOnlyList<Polygon2D> outline)
    {
        Kind = kind;
        Shape = shape;
        Floor = floor;
        Outline = outline;
    }

    public ShapeEditKind Kind { get; }

    public GisDriftShape Shape { get; }

    public ShapeFloor Floor { get; }

    /// <summary>The GDB's outline in model feet: the floor's new sketch.</summary>
    public IReadOnlyList<Polygon2D> Outline { get; }

    public string Label() => string.Format(
        CultureInfo.InvariantCulture,
        "Reshape floor {0}{1} to the GDB outline",
        Floor.ElementId,
        Shape.Label.Length > 0 ? " (" + Shape.Label + ")" : string.Empty);
}

/// <summary>What a drift file's shapes need: the edits Review GDB edits can apply, what has to be done by hand, and how many shapes still wait.</summary>
public sealed class ShapePlan
{
    public ShapePlan(IReadOnlyList<ShapeEdit> edits, IReadOnlyList<string> problems, int waiting)
    {
        Edits = edits;
        Problems = problems;
        Waiting = waiting;
    }

    public static ShapePlan Empty { get; } = new(Array.Empty<ShapeEdit>(), Array.Empty<string>(), 0);

    public IReadOnlyList<ShapeEdit> Edits { get; }

    public IReadOnlyList<string> Problems { get; }

    /// <summary>The file's shapes the model does not match yet: every shape except a reshape whose floor already has the GDB outline.</summary>
    public int Waiting { get; }

    public void Deconstruct(out IReadOnlyList<ShapeEdit> edits, out IReadOnlyList<string> problems)
    {
        edits = Edits;
        problems = Problems;
    }
}

/// <summary>
/// The shape edits of a drift file that Review GDB edits can apply to floors. An outline comes
/// back to model feet through the inverse of the export's shared-coordinate transform, so the next
/// export writes the GDB's outline again. A floor already within <see cref="SameOutlineMetres"/> of
/// the GDB outline is left out, so reading the file again after applying offers nothing.
/// </summary>
public static class GisShapeEdits
{
    /// <summary>The reason <see cref="ShapeFloor.CannotEdit"/> gives for a floor with arcs or splines in its boundary.</summary>
    public const string CurvedEdges = "has curved edges, which the GDB's straight-segment outline would replace with short lines";

    /// <summary>Outlines closer than this everywhere are the same.</summary>
    public const double SameOutlineMetres = 0.01;

    /// <summary>Points closer than this are one point: above Revit's short-curve tolerance of about 0.8 mm.</summary>
    public const double ShortestEdgeFeet = 0.005;

    private const double MetresPerFoot = 0.3048;

    private static readonly GeometryFactory Factory = new();

    /// <param name="file">The drift file.</param>
    /// <param name="floors">The model's floors by UniqueId.</param>
    /// <param name="modelToProject">The export's transform from model feet to the project CRS in metres.</param>
    /// <param name="modelEpsg">The model's project CRS, or 0 when unknown.</param>
    public static ShapePlan Plan(
        GisDriftFile file,
        IReadOnlyDictionary<string, ShapeFloor> floors,
        AffineTransform2D modelToProject,
        int modelEpsg)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        List<ShapeEdit> edits = new();
        List<string> problems = new();
        if (file.Shapes.Count == 0)
        {
            return ShapePlan.Empty;
        }

        if (file.Epsg == 0)
        {
            problems.Add($"The drift file names no CRS, so its {file.Shapes.Count} shape edit(s) have no outline. Export to the station with a project CRS set, then let Kanizu apply again.");
            return new ShapePlan(edits, problems, file.Shapes.Count);
        }

        if (modelEpsg > 0 && file.Epsg != modelEpsg)
        {
            problems.Add($"The drift file's outlines are in EPSG:{file.Epsg}, but this model's project CRS is EPSG:{modelEpsg}. Its {file.Shapes.Count} shape edit(s) are not applied.");
            return new ShapePlan(edits, problems, file.Shapes.Count);
        }

        AffineTransform2D projectToModel = modelToProject.Inverse();
        int settled = 0;
        foreach (GisDriftShape shape in file.Shapes)
        {
            if (shape.Edit != GisDrift.Reshaped)
            {
                problems.Add($"{shape.Describe()}: this edit is not applied by Review GDB edits yet; make it in Revit by hand.");
                continue;
            }

            if (!floors.TryGetValue(shape.UniqueId, out ShapeFloor? floor))
            {
                problems.Add($"{shape.Describe()}: no floor in this model has unique id {shape.UniqueId}. Units from rooms and families are reshaped in Revit by hand.");
                continue;
            }

            List<Polygon2D> outline = ToModel(shape.Outline, projectToModel);
            if (outline.Count == 0)
            {
                problems.Add($"{shape.Describe()}: the GDB outline has no area.");
                continue;
            }

            if (SameOutline(floor.Outline, outline))
            {
                settled++;
                continue;
            }

            if (floor.CannotEdit != null)
            {
                problems.Add($"{shape.Describe()}: floor {floor.ElementId} {floor.CannotEdit}, so its sketch is not redrawn. Reshape it in Revit by hand.");
                continue;
            }

            if (floor.Outline.Count > 1)
            {
                problems.Add($"{shape.Describe()}: floor {floor.ElementId} {InParts(floor)}");
                continue;
            }

            edits.Add(new ShapeEdit(ShapeEditKind.Reshape, shape, floor, outline));
        }

        return new ShapePlan(OnePerFloor(edits, problems), problems, file.Shapes.Count - settled);
    }

    /// <summary>Why a floor in several parts is not redrawn: the export writes each part as its own unit, and one part's outline would replace them all.</summary>
    public static string InParts(ShapeFloor floor) =>
        $"is in {floor.Outline.Count} separate parts, which the GDB holds as separate units; redrawing it to one of them would drop the others. Change it in Revit by hand.";

    // Two edits for one floor cannot both apply: neither is offered.
    private static List<ShapeEdit> OnePerFloor(List<ShapeEdit> edits, List<string> problems)
    {
        HashSet<string> twice = new(edits.GroupBy(edit => edit.Floor.UniqueId, StringComparer.Ordinal).Where(group => group.Count() > 1).Select(group => group.Key), StringComparer.Ordinal);
        foreach (ShapeEdit edit in edits.Where(edit => twice.Contains(edit.Floor.UniqueId)).GroupBy(edit => edit.Floor.UniqueId).Select(group => group.First()))
        {
            problems.Add($"Floor {edit.Floor.ElementId}: the GDB changed it in more than one way, so none of its edits is applied. Change it in Revit by hand.");
        }

        return edits.Where(edit => !twice.Contains(edit.Floor.UniqueId)).ToList();
    }

    /// <summary>The GDB outline in model feet, each ring cleaned of points closer than <see cref="ShortestEdgeFeet"/>; rings left with fewer than 3 points are dropped.</summary>
    public static List<Polygon2D> ToModel(IReadOnlyList<IReadOnlyList<IReadOnlyList<(double X, double Y)>>> outline, AffineTransform2D projectToModel)
    {
        List<Polygon2D> polygons = new();
        foreach (IReadOnlyList<IReadOnlyList<(double X, double Y)>> rings in outline)
        {
            List<List<Point2D>> cleaned = rings
                .Select(ring => Clean(ring.Select(point => projectToModel.Transform(point.X, point.Y))))
                .ToList();
            if (cleaned.Count == 0 || cleaned[0].Count < 3)
            {
                continue;
            }

            polygons.Add(new Polygon2D(cleaned[0], cleaned.Skip(1).Where(ring => ring.Count >= 3).Cast<IReadOnlyList<Point2D>>().ToList()));
        }

        return polygons;
    }

    /// <summary>Whether two outlines in model feet are within <see cref="SameOutlineMetres"/> of each other everywhere.</summary>
    public static bool SameOutline(IReadOnlyList<Polygon2D> a, IReadOnlyList<Polygon2D> b)
    {
        NtsGeometry? left = Shape(a);
        NtsGeometry? right = Shape(b);
        if (left == null || right == null)
        {
            return left == null && right == null;
        }

        return DiscreteHausdorffDistance.Distance(left, right, 0.25) <= SameOutlineMetres;
    }

    private static List<Point2D> Clean(IEnumerable<Point2D> points)
    {
        List<Point2D> ring = new();
        foreach (Point2D point in points)
        {
            if (ring.Count == 0 || Distance(ring[ring.Count - 1], point) >= ShortestEdgeFeet)
            {
                ring.Add(point);
            }
        }

        while (ring.Count > 1 && Distance(ring[0], ring[ring.Count - 1]) < ShortestEdgeFeet)
        {
            ring.RemoveAt(ring.Count - 1);
        }

        return ring;
    }

    private static double Distance(Point2D a, Point2D b) => Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));

    /// <summary>The outline as one geometry in metres.</summary>
    private static NtsGeometry? Shape(IReadOnlyList<Polygon2D> outline)
    {
        List<NtsGeometry> parts = new();
        foreach (Polygon2D polygon in outline)
        {
            LinearRing? shell = Ring(polygon.ExteriorRing);
            if (shell != null)
            {
                parts.Add(Factory.CreatePolygon(shell, polygon.InteriorRings.Select(Ring).Where(ring => ring != null).Select(ring => ring!).ToArray()).Buffer(0));
            }
        }

        return parts.Count == 0 ? null : parts.Count == 1 ? parts[0] : Factory.BuildGeometry(parts).Union();
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
}
