using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Algorithm.Distance;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

/// <summary>A floor a shape edit can change: its outline in model feet, and why Revit cannot edit its sketch, if it cannot.</summary>
public sealed class ShapeFloor
{
    public ShapeFloor(string uniqueId, long elementId, IEnumerable<Polygon2D> outline, string? cannotEdit = null, long levelId = 0)
    {
        UniqueId = uniqueId;
        ElementId = elementId;
        Outline = outline.ToList();
        CannotEdit = cannotEdit;
        LevelId = levelId;
    }

    public string UniqueId { get; }

    public long ElementId { get; }

    public IReadOnlyList<Polygon2D> Outline { get; }

    /// <summary>Why the floor's sketch cannot be redrawn (a slope arrow, shape-edited points), or null.</summary>
    public string? CannotEdit { get; }

    /// <summary>The floor's Revit level, so a split piece can be found among the floors beside its keeper.</summary>
    public long LevelId { get; }
}

public enum ShapeEditKind
{
    Reshape,
    Split,
    Merge,
}

/// <summary>
/// A floor a shape edit adds: its outline in model feet, the GDB values it takes over the kept
/// floor's (parameter name to value), and the standard floor type its GDB category and
/// restriction name, or null to take the kept floor's type.
/// </summary>
public sealed class NewFloor
{
    public NewFloor(GisDriftShape piece, IReadOnlyList<Polygon2D> outline, IReadOnlyDictionary<string, string> values, string? typeName)
    {
        Piece = piece;
        Outline = outline;
        Values = values;
        TypeName = typeName;
    }

    public GisDriftShape Piece { get; }

    public IReadOnlyList<Polygon2D> Outline { get; }

    public IReadOnlyDictionary<string, string> Values { get; }

    public string? TypeName { get; }
}

/// <summary>
/// One shape edit to apply: the floor it changes, its outline now and the GDB's, all in model feet.
/// A split also lists the pieces that become new floors beside it.
/// </summary>
public sealed class ShapeEdit
{
    public ShapeEdit(ShapeEditKind kind, GisDriftShape shape, ShapeFloor floor, IReadOnlyList<Polygon2D> outline)
        : this(kind, shape, floor, outline, Array.Empty<NewFloor>(), redrawFloor: true)
    {
    }

    public ShapeEdit(
        ShapeEditKind kind,
        GisDriftShape shape,
        ShapeFloor floor,
        IReadOnlyList<Polygon2D> outline,
        IReadOnlyList<NewFloor> newFloors,
        bool redrawFloor,
        IReadOnlyList<ShapeFloor>? absorbed = null)
    {
        Kind = kind;
        Shape = shape;
        Floor = floor;
        Outline = outline;
        NewFloors = newFloors;
        RedrawFloor = redrawFloor;
        Absorbed = absorbed ?? Array.Empty<ShapeFloor>();
    }

    public ShapeEditKind Kind { get; }

    public GisDriftShape Shape { get; }

    public ShapeFloor Floor { get; }

    /// <summary>The GDB's outline in model feet: the floor's new sketch.</summary>
    public IReadOnlyList<Polygon2D> Outline { get; }

    /// <summary>A split's pieces that no floor has yet, each a new floor on the kept floor's level.</summary>
    public IReadOnlyList<NewFloor> NewFloors { get; }

    /// <summary>False when the floor already has <see cref="Outline"/> and only new floors are missing, or only absorbed floors are left.</summary>
    public bool RedrawFloor { get; }

    /// <summary>A merge's floors that the GDB merged into this one and that are still in the model: they are deleted.</summary>
    public IReadOnlyList<ShapeFloor> Absorbed { get; }

    public string Label()
    {
        string name = Shape.Label.Length > 0 ? " (" + Shape.Label + ")" : string.Empty;
        return Kind switch
        {
            ShapeEditKind.Split => string.Format(CultureInfo.InvariantCulture, "Split floor {0}{1} into {2} floors by the GDB", Floor.ElementId, name, NewFloors.Count + 1),
            ShapeEditKind.Merge => string.Format(
                CultureInfo.InvariantCulture,
                "Merge floor(s) {0} into floor {1}{2} by the GDB",
                string.Join(", ", Absorbed.Select(floor => floor.ElementId.ToString(CultureInfo.InvariantCulture))),
                Floor.ElementId,
                name),
            _ => string.Format(CultureInfo.InvariantCulture, "Reshape floor {0}{1} to the GDB outline", Floor.ElementId, name),
        };
    }
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

    /// <summary>
    /// The standard's floor instance parameters a split's new floors take from the kept floor:
    /// name, category, level and the like. Not <c>id</c> or <c>gis_key</c>, which name one element.
    /// </summary>
    public static IReadOnlyList<string> CopiedToNewFloors { get; } = GisSharedParameters.All
        .Where(definition => definition.Binding == GisParameterBinding.Instance && definition.Categories.Contains("OST_Floors"))
        .Select(definition => definition.Name)
        .Where(name => name != "id" && name != "gis_key")
        .ToList();

    /// <summary>The reason <see cref="ShapeFloor.CannotEdit"/> gives for an element a merge would delete that is not a floor.</summary>
    public const string NotAFloor = "is not a floor";

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
    /// <param name="standard">The GIS standard, to turn a new floor's GDB category into a parameter value and a floor type; null takes the kept floor's.</param>
    public static ShapePlan Plan(
        GisDriftFile file,
        IReadOnlyDictionary<string, ShapeFloor> floors,
        AffineTransform2D modelToProject,
        int modelEpsg,
        GisToolStandard? standard = null)
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
        foreach (IGrouping<string, GisDriftShape> group in file.Shapes.Where(shape => shape.Edit == GisDrift.Split).GroupBy(shape => shape.Group.Length > 0 ? shape.Group : shape.RevitKey))
        {
            settled += PlanSplit(group.ToList(), floors, projectToModel, standard, edits, problems);
        }

        foreach (IGrouping<string, GisDriftShape> group in file.Shapes.Where(shape => shape.Edit == GisDrift.Merged).GroupBy(shape => shape.Group.Length > 0 ? shape.Group : shape.RevitKey))
        {
            settled += PlanMerge(group.ToList(), floors, projectToModel, edits, problems);
        }

        foreach (GisDriftShape shape in file.Shapes.Where(shape => shape.Edit != GisDrift.Split && shape.Edit != GisDrift.Merged))
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

    /// <summary>
    /// Plans one split: the kept floor takes its piece's outline, and each other piece becomes a new
    /// floor unless a floor on the same level already has its outline. Returns how many of the
    /// group's pieces the model already matches.
    /// </summary>
    private static int PlanSplit(
        IReadOnlyList<GisDriftShape> pieces,
        IReadOnlyDictionary<string, ShapeFloor> floors,
        AffineTransform2D projectToModel,
        GisToolStandard? standard,
        List<ShapeEdit> edits,
        List<string> problems)
    {
        List<GisDriftShape> keepers = pieces.Where(piece => piece.Keeps).ToList();
        if (keepers.Count != 1)
        {
            problems.Add($"{pieces[0].Describe()}: the drift file has {keepers.Count} pieces of this split that keep the floor, not 1. Split it in Revit by hand.");
            return 0;
        }

        GisDriftShape keeper = keepers[0];
        if (!floors.TryGetValue(keeper.UniqueId, out ShapeFloor? floor))
        {
            problems.Add($"{keeper.Describe()}: no floor in this model has unique id {keeper.UniqueId}. Units from rooms and families are split in Revit by hand.");
            return 0;
        }

        List<Polygon2D> outline = ToModel(keeper.Outline, projectToModel);
        List<(GisDriftShape Piece, List<Polygon2D> Outline)> others = pieces
            .Where(piece => !piece.Keeps)
            .Select(piece => (piece, ToModel(piece.Outline, projectToModel)))
            .ToList();
        if (outline.Count == 0 || others.Any(other => other.Outline.Count == 0))
        {
            problems.Add($"{keeper.Describe()}: a piece of this split has no area in the GDB outline.");
            return 0;
        }

        // A new floor is one polygon, so a piece in several parts could never be read back as drawn.
        if (others.Any(other => other.Outline.Count > 1))
        {
            problems.Add($"{keeper.Describe()}: a piece of this split is in several separate parts in the GDB. Split it in Revit by hand.");
            return 0;
        }

        bool redraw = !SameOutline(floor.Outline, outline);
        if (redraw && floor.Outline.Count > 1)
        {
            problems.Add($"{keeper.Describe()}: floor {floor.ElementId} {InParts(floor)}");
            return 0;
        }

        List<NewFloor> missing = others
            .Where(other => !floors.Values.Any(existing => existing.UniqueId != floor.UniqueId && existing.LevelId == floor.LevelId && SameOutline(existing.Outline, other.Outline)))
            .Select(other => NewFloorOf(other.Piece, other.Outline, standard, problems))
            .ToList();
        int settled = (redraw ? 0 : 1) + (others.Count - missing.Count);
        if (!redraw && missing.Count == 0)
        {
            return settled;
        }

        if (redraw && floor.CannotEdit != null)
        {
            problems.Add($"{keeper.Describe()}: floor {floor.ElementId} {floor.CannotEdit}, so its sketch is not redrawn. Split it in Revit by hand.");
            return settled;
        }

        edits.Add(new ShapeEdit(ShapeEditKind.Split, keeper, floor, outline, missing, redraw));
        return settled;
    }

    /// <summary>
    /// Plans one merge: the floor that keeps its row takes the merged outline, and the floors the
    /// GDB merged into it are deleted. A floor already gone counts as done, so a second read offers
    /// nothing. Returns how many of the group's shapes the model already matches.
    /// </summary>
    private static int PlanMerge(
        IReadOnlyList<GisDriftShape> shapes,
        IReadOnlyDictionary<string, ShapeFloor> floors,
        AffineTransform2D projectToModel,
        List<ShapeEdit> edits,
        List<string> problems)
    {
        List<GisDriftShape> keepers = shapes.Where(shape => shape.Keeps).ToList();
        if (keepers.Count != 1)
        {
            problems.Add($"{shapes[0].Describe()}: the drift file has {keepers.Count} units of this merge that keep their row, not 1. Merge them in Revit by hand.");
            return 0;
        }

        GisDriftShape keeper = keepers[0];
        if (!floors.TryGetValue(keeper.UniqueId, out ShapeFloor? floor) || floor.CannotEdit == NotAFloor)
        {
            problems.Add($"{keeper.Describe()}: no floor in this model has unique id {keeper.UniqueId}. Units from rooms and families are merged in Revit by hand.");
            return 0;
        }

        List<Polygon2D> outline = ToModel(keeper.Outline, projectToModel);
        if (outline.Count == 0)
        {
            problems.Add($"{keeper.Describe()}: the GDB outline has no area.");
            return 0;
        }

        List<ShapeFloor> absorbed = new();
        int settled = 0;
        foreach (GisDriftShape gone in shapes.Where(shape => !shape.Keeps))
        {
            if (!floors.TryGetValue(gone.UniqueId, out ShapeFloor? other))
            {
                settled++;
            }
            else if (other.CannotEdit == NotAFloor)
            {
                problems.Add($"{gone.Describe()}: element {other.ElementId} {NotAFloor}, so the merge does not delete it. Merge it in Revit by hand.");
                return settled;
            }
            else if (other.Outline.Count > 1)
            {
                problems.Add($"{gone.Describe()}: floor {other.ElementId} {InParts(other)}");
                return settled;
            }
            else
            {
                absorbed.Add(other);
            }
        }

        bool redraw = !SameOutline(floor.Outline, outline);
        if (redraw && floor.Outline.Count > 1)
        {
            problems.Add($"{keeper.Describe()}: floor {floor.ElementId} {InParts(floor)}");
            return settled;
        }

        settled += redraw ? 0 : 1;
        if (!redraw && absorbed.Count == 0)
        {
            return settled;
        }

        if (redraw && floor.CannotEdit != null)
        {
            problems.Add($"{keeper.Describe()}: floor {floor.ElementId} {floor.CannotEdit}, so its sketch is not redrawn. Merge it in Revit by hand.");
            return settled;
        }

        edits.Add(new ShapeEdit(ShapeEditKind.Merge, keeper, floor, outline, Array.Empty<NewFloor>(), redraw, absorbed));
        return settled;
    }

    /// <summary>
    /// A new floor for a piece: the GDB name, alt_name and category the piece carries, as the
    /// parameters name, short_name and category, and the standard floor type of its category and
    /// restriction. A field the piece does not carry, or a category the standard does not know,
    /// leaves the kept floor's value or type.
    /// </summary>
    public static NewFloor NewFloorOf(GisDriftShape piece, IReadOnlyList<Polygon2D> outline, GisToolStandard? standard, List<string> problems)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);
        if (piece.GdbName.Length > 0)
        {
            values["name"] = piece.GdbName;
        }

        if (piece.GdbAltName.Length > 0)
        {
            values["short_name"] = piece.GdbAltName;
        }

        string? typeName = null;
        if (piece.GdbCategory.Length > 0 && standard != null)
        {
            (string? category, string? problem) = ParameterTable.CheckValue("category", piece.GdbCategory, standard);
            if (category != null)
            {
                values["category"] = category;
            }
            else
            {
                problems.Add($"{piece.Describe()}: {problem} The new floor takes the kept floor's category.");
            }

            typeName = StandardTypeFor(piece.GdbCategory, piece.GdbRestriction, standard)?.Name;
        }

        return new NewFloor(piece, outline, values, typeName);
    }

    /// <summary>The standard floor type of a GDB category and restriction, spelled as on category rows; null when none matches.</summary>
    public static StandardFloorType? StandardTypeFor(string category, string restriction, GisToolStandard standard)
    {
        string wanted = ImdfRestrictionNormalizer.FloorTypeRestriction(restriction) ?? string.Empty;
        return standard.FloorTypes.FirstOrDefault(type =>
            string.Equals(type.GisCategory, category, StringComparison.Ordinal)
            && (ImdfRestrictionNormalizer.FloorTypeRestriction(type.GisRestriction) ?? string.Empty) == wanted);
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
