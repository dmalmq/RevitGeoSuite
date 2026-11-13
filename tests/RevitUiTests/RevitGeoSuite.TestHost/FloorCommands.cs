using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// Reads floors and what hangs on them for the reshape checks (C3a), and places a floor opening as a
/// fixture. "East" is the project CRS east the export writes, taken from the active project location
/// the way RevitGeoSuite's SharedCoordinateProjector does, so it matches the GDB outline's east.
/// </summary>
internal static class FloorCommands
{
    private const double FeetToMetres = 0.3048;

    /// <summary>Each element by UniqueId or id: whether it exists, and its id, type and bounding box (feet).</summary>
    public static Dictionary<string, object?> Elements(UIApplication app, string[] uniqueIds, long[] ids)
    {
        Document document = Active(app);
        var found = new List<Dictionary<string, object?>>();
        foreach (string uniqueId in uniqueIds)
        {
            found.Add(Describe(uniqueId, document.GetElement(uniqueId)));
        }

        foreach (long id in ids)
        {
            found.Add(Describe(id.ToString(), document.GetElement(new ElementId(id))));
        }

        return new Dictionary<string, object?> { ["elements"] = found };
    }

    /// <summary>The first element of a category (by BuiltInCategory name, such as OST_Rooms), or nothing.</summary>
    public static Dictionary<string, object?> FirstOf(UIApplication app, string category)
    {
        Document document = Active(app);
        BuiltInCategory builtIn = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), category);
        Element? element = new FilteredElementCollector(document).OfCategory(builtIn).WhereElementIsNotElementType().FirstElement();
        return new Dictionary<string, object?> { ["id"] = element?.Id.Value, ["uniqueId"] = element?.UniqueId };
    }

    /// <summary>
    /// Every floor: why its sketch would not be redrawn (arcs, extra sketch lines), and its openings and
    /// hosted family instances with their distance west of the floor's east edge in metres.
    /// </summary>
    public static Dictionary<string, object?> FloorScan(UIApplication app)
    {
        Document document = Active(app);
        Projector east = new(document.ActiveProjectLocation);
        var floors = new List<Dictionary<string, object?>>();
        foreach (Floor floor in new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType().Cast<Floor>())
        {
            Sketch? sketch = document.GetElement(floor.SketchId) as Sketch;
            List<XYZ> vertices = sketch == null ? new List<XYZ>() : Vertices(sketch).ToList();
            double edge = vertices.Count == 0 ? double.NaN : vertices.Max(east.East);
            HashSet<ElementId> boundary = sketch == null ? new HashSet<ElementId>() : BoundaryIds(sketch);
            floors.Add(new Dictionary<string, object?>
            {
                ["id"] = floor.Id.Value,
                ["uniqueId"] = floor.UniqueId,
                ["level"] = document.GetElement(floor.LevelId)?.Name,
                ["hasArc"] = sketch != null && sketch.Profile.Cast<CurveArray>().Any(loop => loop.Cast<Curve>().Any(curve => curve is not Line)),
                ["shapeEdited"] = floor.GetSlabShapeEditor() is { IsEnabled: true },
                ["extraSketchLines"] = sketch != null && sketch.GetAllElements().Any(id => !boundary.Contains(id) && document.GetElement(id) is CurveElement),
                ["dependents"] = floor.GetDependentElements(null)
                    .Select(document.GetElement)
                    .Where(element => element is Opening || element is FamilyInstance)
                    .Select(element => new Dictionary<string, object?>
                    {
                        ["id"] = element.Id.Value,
                        ["category"] = element.Category?.Name,
                        ["name"] = element is FamilyInstance instance ? instance.Symbol?.FamilyName : "opening",
                        ["westOfEdgeM"] = Location(element) is XYZ point && !double.IsNaN(edge) ? (double?)(edge - east.East(point)) : null,
                    })
                    .ToList(),
            });
        }

        return new Dictionary<string, object?> { ["floors"] = floors };
    }

    /// <summary>
    /// Cuts a square opening of <paramref name="sizeM"/> into the floor, wholly inside the floor and
    /// wholly within the band <paramref name="bandM"/> wide west of its east edge, which a shrink by
    /// that much removes. Runs inside the fixture transaction.
    /// </summary>
    public static string OpeningInEastBand(Document document, long floorId, double bandM, double sizeM)
    {
        Floor floor = document.GetElement(new ElementId(floorId)) as Floor ?? throw new ArgumentException($"No floor {floorId}.");
        Sketch sketch = document.GetElement(floor.SketchId) as Sketch ?? throw new InvalidOperationException($"Floor {floorId} has no sketch.");
        Projector projector = new(document.ActiveProjectLocation);
        List<XYZ> vertices = Vertices(sketch).ToList();
        double edge = vertices.Max(projector.East);
        double z = sketch.SketchPlane.GetPlane().Origin.Z;
        XYZ east = projector.EastInModel;
        XYZ north = new(-east.Y, east.X, 0);
        double half = sizeM / 2 / FeetToMetres;
        double step = 0.1 / FeetToMetres;
        XYZ[]? square = null;
        for (double x = vertices.Min(point => point.X); square == null && x <= vertices.Max(point => point.X); x += step)
        {
            for (double y = vertices.Min(point => point.Y); square == null && y <= vertices.Max(point => point.Y); y += step)
            {
                XYZ centre = new(x, y, z);
                XYZ[] corners =
                {
                    centre - east * half - north * half,
                    centre + east * half - north * half,
                    centre + east * half + north * half,
                    centre - east * half + north * half,
                };
                if (corners.All(corner => edge - projector.East(corner) is > 0.05 && edge - projector.East(corner) < bandM - 0.05 && Inside(corner, sketch)))
                {
                    square = corners;
                }
            }
        }

        if (square == null)
        {
            throw new InvalidOperationException($"No {sizeM} m square fits inside floor {floorId} within {bandM} m of its east edge.");
        }

        var profile = new CurveArray();
        for (int index = 0; index < square.Length; index++)
        {
            profile.Append(Line.CreateBound(square[index], square[(index + 1) % square.Length]));
        }

        Opening opening = document.Create.NewOpening(floor, profile, true);
        return $"cut opening {opening.Id.Value} into floor {floorId}, within {bandM} m of its east edge";
    }

    private static Dictionary<string, object?> Describe(string key, Element? element)
    {
        BoundingBoxXYZ? box = element?.get_BoundingBox(null);
        return new Dictionary<string, object?>
        {
            ["key"] = key,
            ["exists"] = element != null,
            ["id"] = element?.Id.Value,
            ["uniqueId"] = element?.UniqueId,
            ["category"] = element?.Category?.Name,
            ["typeId"] = element?.GetTypeId().Value,
            ["typeName"] = element == null ? null : element.Document.GetElement(element.GetTypeId())?.Name,
            ["levelId"] = element == null || element.LevelId == ElementId.InvalidElementId ? null : element.LevelId.Value,
            ["heightOffsetFt"] = element?.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM)?.AsDouble(),
            ["areaM2"] = element?.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED)?.AsDouble() * FeetToMetres * FeetToMetres,
            ["min"] = box == null ? null : new[] { box.Min.X, box.Min.Y, box.Min.Z },
            ["max"] = box == null ? null : new[] { box.Max.X, box.Max.Y, box.Max.Z },
        };
    }

    private static XYZ? Location(Element element) =>
        element.Location is LocationPoint point ? point.Point
        : element.get_BoundingBox(null) is BoundingBoxXYZ box ? (box.Min + box.Max) / 2
        : null;

    private static IEnumerable<XYZ> Vertices(Sketch sketch) =>
        sketch.Profile.Cast<CurveArray>().SelectMany(loop => loop.Cast<Curve>()).Select(curve => curve.GetEndPoint(0));

    private static HashSet<ElementId> BoundaryIds(Sketch sketch) =>
        new(sketch.Profile.Cast<CurveArray>().SelectMany(loop => loop.Cast<Curve>())
            .Select(curve => curve.Reference?.ElementId)
            .Where(id => id != null && id != ElementId.InvalidElementId)
            .Cast<ElementId>());

    // Even-odd rule over every loop, so a point in a hole is outside.
    private static bool Inside(XYZ point, Sketch sketch)
    {
        bool inside = false;
        foreach (CurveArray loop in sketch.Profile)
        {
            foreach (Curve curve in loop)
            {
                XYZ a = curve.GetEndPoint(0);
                XYZ b = curve.GetEndPoint(1);
                if ((a.Y > point.Y) != (b.Y > point.Y) && point.X < (b.X - a.X) * (point.Y - a.Y) / (b.Y - a.Y) + a.X)
                {
                    inside = !inside;
                }
            }
        }

        return inside;
    }

    private static Document Active(UIApplication app) => app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");

    /// <summary>Model feet to project CRS east in metres, and project east as a unit vector in the model.</summary>
    private sealed class Projector
    {
        private readonly double originEast;
        private readonly double eastPerX;
        private readonly double eastPerY;

        public Projector(ProjectLocation location)
        {
            ProjectPosition origin = location.GetProjectPosition(XYZ.Zero);
            ProjectPosition x = location.GetProjectPosition(XYZ.BasisX);
            ProjectPosition y = location.GetProjectPosition(XYZ.BasisY);
            originEast = origin.EastWest * FeetToMetres;
            eastPerX = (x.EastWest - origin.EastWest) * FeetToMetres;
            eastPerY = (y.EastWest - origin.EastWest) * FeetToMetres;
            double northPerX = (x.NorthSouth - origin.NorthSouth) * FeetToMetres;
            double northPerY = (y.NorthSouth - origin.NorthSouth) * FeetToMetres;
            double determinant = eastPerX * northPerY - eastPerY * northPerX;
            EastInModel = new XYZ(northPerY, -northPerX, 0).Multiply(Math.Sign(determinant)).Normalize();
        }

        public XYZ EastInModel { get; }

        public double East(XYZ point) => originEast + eastPerX * point.X + eastPerY * point.Y;
    }
}
