using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Algorithm.Distance;
using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>A floor a shape edit can change: its outline in model feet, and why Revit cannot edit its sketch, if it cannot.</summary>
public sealed class ShapeFloor
{
    public ShapeFloor(string uniqueId, long elementId, IEnumerable<Polygon2D> outline, string? cannotEdit = null, IEnumerable<Polygon2D>? sketch = null)
    {
        UniqueId = uniqueId;
        ElementId = elementId;
        Outline = outline.ToList();
        CannotEdit = cannotEdit;
        Sketch = sketch?.ToList() ?? Outline;
    }

    public string UniqueId { get; }

    public long ElementId { get; }

    public IReadOnlyList<Polygon2D> Outline { get; }

    /// <summary>
    /// The region the floor's sketch encloses, in model feet. It differs from <see cref="Outline"/>,
    /// the top face the export reads, where an opening, a column or a joined element cuts the floor.
    /// </summary>
    public IReadOnlyList<Polygon2D> Sketch { get; }

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
    public ShapeEdit(ShapeEditKind kind, GisDriftShape shape, ShapeFloor floor, IReadOnlyList<Polygon2D> outline, IReadOnlyList<Polygon2D>? sketch = null)
    {
        Kind = kind;
        Shape = shape;
        Floor = floor;
        Outline = outline;
        Sketch = sketch ?? outline;
    }

    public ShapeEditKind Kind { get; }

    public GisDriftShape Shape { get; }

    public ShapeFloor Floor { get; }

    /// <summary>The GDB's outline in model feet: the floor's top face after the edit.</summary>
    public IReadOnlyList<Polygon2D> Outline { get; }

    /// <summary>The floor's new sketch in model feet: its sketch now, with what the GDB added and without what it took away.</summary>
    public IReadOnlyList<Polygon2D> Sketch { get; }

    public string Label() => string.Format(
        CultureInfo.InvariantCulture,
        "Reshape floor {0}{1} to the GDB outline",
        Floor.ElementId,
        Shape.Label.Length > 0 ? " (" + Shape.Label + ")" : string.Empty);
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
    public static (IReadOnlyList<ShapeEdit> Edits, IReadOnlyList<string> Problems) Plan(
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
            return (edits, problems);
        }

        if (file.Epsg == 0)
        {
            problems.Add($"The drift file names no CRS, so its {file.Shapes.Count} shape edit(s) have no outline. Export to the station with a project CRS set, then let Kanizu apply again.");
            return (edits, problems);
        }

        if (modelEpsg > 0 && file.Epsg != modelEpsg)
        {
            problems.Add($"The drift file's outlines are in EPSG:{file.Epsg}, but this model's project CRS is EPSG:{modelEpsg}. Its {file.Shapes.Count} shape edit(s) are not applied.");
            return (edits, problems);
        }

        AffineTransform2D projectToModel = modelToProject.Inverse();
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
                continue;
            }

            if (!CanSketch(outline))
            {
                problems.Add($"{shape.Describe()}: {CrossesItself}");
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

            List<Polygon2D> sketch = SketchFor(floor, outline);
            if (sketch.Count != 1 || !CanSketch(sketch))
            {
                problems.Add($"{shape.Describe()}: floor {floor.ElementId} {CutApart}");
                continue;
            }

            if (SameOutline(sketch, floor.Sketch))
            {
                problems.Add($"{shape.Describe()}: floor {floor.ElementId} {AlreadySketched(GapMetres(floor.Outline, outline))}");
                continue;
            }

            edits.Add(new ShapeEdit(ShapeEditKind.Reshape, shape, floor, outline, sketch));
        }

        return (OnePerFloor(edits, problems), problems);
    }

    /// <summary>Why a sketch is not redrawn when the GDB's change would leave it in pieces or with lines Revit cannot close.</summary>
    public const string CutApart = "would need a sketch in several parts, or one whose lines cross, to give the GDB outline around the elements that cut it. Change it in Revit by hand.";

    /// <summary>Why a floor whose sketch already gives the GDB outline is not redrawn: what still differs is cut by another element.</summary>
    public static string AlreadySketched(double gapMetres) => string.Format(
        CultureInfo.InvariantCulture,
        "already has the GDB outline in its sketch; its top face is up to {0:F0} cm off where another element (an opening, a column, a joined floor or wall) cuts it. Change that element, or the GDB.",
        gapMetres * 100);

    /// <summary>
    /// The sketch that gives the GDB outline: the floor's sketch with what the GDB added to its top face
    /// and without what the GDB took away. Cuts by other elements are in neither, so they stay out of
    /// the sketch and keep cutting it. A difference narrower than <see cref="SameOutlineMetres"/> is
    /// rounding, not an edit.
    /// </summary>
    public static List<Polygon2D> SketchFor(ShapeFloor floor, IReadOnlyList<Polygon2D> gdb)
    {
        NtsGeometry? sketch = Snapped(floor.Sketch);
        NtsGeometry? top = Snapped(floor.Outline);
        NtsGeometry? wanted = Snapped(gdb);
        if (sketch == null || top == null || wanted == null)
        {
            return gdb.ToList();
        }

        NtsGeometry added = Edit(wanted.Difference(top));
        NtsGeometry removed = Edit(top.Difference(wanted));
        return ToFeet(GeometryPrecisionReducer.Reduce(sketch.Union(added).Difference(removed), Millimetres));
    }

    /// <summary>The parts of a difference that are an edit: wider than <see cref="SameOutlineMetres"/>, not slivers along a shared edge.</summary>
    private static NtsGeometry Edit(NtsGeometry difference)
    {
        List<NtsGeometry> parts = Enumerable.Range(0, difference.NumGeometries)
            .Select(difference.GetGeometryN)
            .Where(part => part is Polygon && part.Area > 0 && 2 * part.Area / part.Length > SameOutlineMetres / 2)
            .ToList();
        return parts.Count == 0 ? Factory.CreatePolygon() : Factory.BuildGeometry(parts).Union();
    }

    private static readonly PrecisionModel Millimetres = new(1000);

    private static NtsGeometry? Snapped(IReadOnlyList<Polygon2D> outline) =>
        Shape(outline) is NtsGeometry shape ? GeometryPrecisionReducer.Reduce(shape, Millimetres) : null;

    /// <summary>Polygons in metres back to model feet, each ring cleaned of points closer than <see cref="ShortestEdgeFeet"/>.</summary>
    private static List<Polygon2D> ToFeet(NtsGeometry geometry)
    {
        List<Polygon2D> polygons = new();
        for (int index = 0; index < geometry.NumGeometries; index++)
        {
            if (geometry.GetGeometryN(index) is not Polygon polygon || polygon.IsEmpty)
            {
                continue;
            }

            List<Point2D> shell = Feet(polygon.ExteriorRing);
            if (shell.Count >= 3)
            {
                polygons.Add(new Polygon2D(shell, polygon.InteriorRings.Select(Feet).Where(ring => ring.Count >= 3).Cast<IReadOnlyList<Point2D>>().ToList()));
            }
        }

        return polygons;
    }

    private static List<Point2D> Feet(LineString ring) =>
        Clean(ring.Coordinates.Select(coordinate => new Point2D(coordinate.X / MetresPerFoot, coordinate.Y / MetresPerFoot)));

    /// <summary>
    /// A sketch's loops as polygons in model feet, by the even-odd rule Revit uses: a loop inside
    /// another is a hole, a loop inside a hole is an island.
    /// </summary>
    public static List<Polygon2D> FromLoops(IEnumerable<IReadOnlyList<Point2D>> loops)
    {
        NtsGeometry region = Factory.CreatePolygon();
        foreach (IReadOnlyList<Point2D> loop in loops)
        {
            if (Ring(loop) is LinearRing ring)
            {
                region = region.SymmetricDifference(Factory.CreatePolygon(ring).Buffer(0));
            }
        }

        return ToFeet(region);
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
    public static bool SameOutline(IReadOnlyList<Polygon2D> a, IReadOnlyList<Polygon2D> b) => GapMetres(a, b) <= SameOutlineMetres;

    /// <summary>How far apart two outlines in model feet are at their farthest, in metres; infinite when only one has area.</summary>
    public static double GapMetres(IReadOnlyList<Polygon2D> a, IReadOnlyList<Polygon2D> b)
    {
        NtsGeometry? left = Shape(a);
        NtsGeometry? right = Shape(b);
        if (left == null || right == null)
        {
            return left == null && right == null ? 0 : double.PositiveInfinity;
        }

        return DiscreteHausdorffDistance.Distance(left, right, 0.25);
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
    /// <summary>Why an outline is not drawn: Revit cannot close a sketch whose lines cross.</summary>
    public const string CrossesItself = "the GDB outline crosses itself or has a hole outside it, which Revit cannot sketch. Fix it in the GDB, or change the floor in Revit by hand.";

    /// <summary>Whether every polygon is valid as drawn: no ring crosses itself or another, and every hole lies inside its outline.</summary>
    public static bool CanSketch(IReadOnlyList<Polygon2D> outline) =>
        outline.All(polygon => Ring(polygon.ExteriorRing) is LinearRing shell &&
            Factory.CreatePolygon(shell, polygon.InteriorRings.Select(Ring).Where(ring => ring != null).Select(ring => ring!).ToArray()).IsValid);

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
