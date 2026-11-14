using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Extractors;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>Reads the model for <see cref="GisShapeEdits"/> and applies its edits, each as one undoable step.</summary>
public static class ShapeEditWork
{
    public static ShapePlan Plan(Document document, GisDriftFile file)
    {
        if (file.Shapes.Count == 0)
        {
            return ShapePlan.Empty;
        }

        HashSet<string> wanted = new(file.Shapes.Select(shape => shape.UniqueId).Where(id => id.Length > 0), StringComparer.Ordinal);
        Dictionary<string, ShapeFloor> floors = new(StringComparer.Ordinal);
        foreach (string uniqueId in wanted)
        {
            if (document.GetElement(uniqueId) is Floor floor)
            {
                floors[uniqueId] = new ShapeFloor(uniqueId, floor.Id.Value, FloorTopFaces.Read(floor).Outline, WhyNotEditable(document, floor), floor.LevelId.Value, SketchOf(document, floor));
            }
        }

        // A merge deletes only floors: anything else the GDB merged away is named, not deleted.
        foreach (GisDriftShape gone in file.Shapes.Where(shape => shape.Edit == GisDrift.Merged && !shape.Keeps && !floors.ContainsKey(shape.UniqueId)))
        {
            if (gone.UniqueId.Length > 0 && document.GetElement(gone.UniqueId) is Element element)
            {
                floors[gone.UniqueId] = new ShapeFloor(gone.UniqueId, element.Id.Value, Array.Empty<Polygon2D>(), GisShapeEdits.NotAFloor);
            }
        }

        // A floor a piece may already be is on the split floor's level. Not only around it: after a
        // first apply the split floor has shrunk to its own piece, and pieces further off fall outside it.
        foreach (Floor splitFloor in file.Shapes
            .Where(shape => shape.Edit == GisDrift.Split && shape.Keeps)
            .Select(shape => document.GetElement(shape.UniqueId))
            .OfType<Floor>())
        {
            foreach (Floor beside in OtherFloorsOnLevel(document, splitFloor))
            {
                if (!floors.ContainsKey(beside.UniqueId))
                {
                    floors[beside.UniqueId] = new ShapeFloor(beside.UniqueId, beside.Id.Value, FloorTopFaces.Read(beside).Outline, levelId: beside.LevelId.Value);
                }
            }
        }

        return GisShapeEdits.Plan(file, floors, new SharedCoordinateProjector(document.ActiveProjectLocation).Transform, ModelEpsg(document), GisToolStandard.LoadEmbedded());
    }

    /// <summary>
    /// Applies one shape edit as one undo step named after it. A reshape redraws the floor's
    /// sketch by the GDB's change, so cuts by other elements stay out of it and the floor keeps its
    /// UniqueId, revit_key, id and parameters. A split redraws the kept floor to its piece and adds a floor for each other piece, with the kept floor's type,
    /// level, offset and GIS values; the next Kanizu apply pairs each piece with its new floor. A
    /// merge deletes the floors the GDB merged into the kept one, then redraws it to the merged outline.
    /// </summary>
    /// <returns>What Revit warned about, the elements gone with the old outline or a deleted floor, and the new floors, one line each.</returns>
    public static IReadOnlyList<string> Apply(Document document, ShapeEdit edit)
    {
        // Every kind in a group, so an outline Revit takes but draws differently is rolled back and nothing changes.
        using TransactionGroup group = new(document, edit.Label());
        group.Start();
        try
        {
            List<string> notes = new();
            if (edit.Kind == ShapeEditKind.Merge)
            {
                // The merged floors go first, so the kept floor's new outline never overlaps them.
                notes.AddRange(DeleteAbsorbed(document, edit));
            }

            if (edit.RedrawFloor)
            {
                notes.AddRange(Redraw(document, edit));
            }

            if (edit.NewFloors.Count > 0)
            {
                notes.AddRange(AddFloors(document, edit));
            }
            group.Assimilate();
            return notes;
        }
        finally
        {
            if (group.HasStarted() && !group.HasEnded())
            {
                group.RollBack();
            }
        }
    }

    /// <summary>Deletes the floors a merge took in, in one transaction, and names each with what Revit deleted with it.</summary>
    private static IEnumerable<string> DeleteAbsorbed(Document document, ShapeEdit edit)
    {
        List<string> notes = new();
        using Transaction transaction = new(document, edit.Label());
        FailOnError failures = new();
        transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(failures));
        transaction.Start();
        foreach (ShapeFloor gone in edit.Absorbed)
        {
            if (document.GetElement(gone.UniqueId) is not Floor floor)
            {
                continue;
            }

            HashSet<ElementId> parts = new() { floor.Id, floor.SketchId };
            if (document.GetElement(floor.SketchId) is Sketch sketch)
            {
                parts.UnionWith(sketch.GetAllElements());
            }

            List<string> hosted = floor.GetDependentElements(null)
                .Where(id => !parts.Contains(id))
                .Select(id => (Id: id, Name: Describe(document.GetElement(id))))
                .Where(entry => entry.Name.Length > 0)
                .Select(entry => $"{entry.Name} {entry.Id.Value}")
                .ToList();
            document.Delete(floor.Id);
            notes.Add(hosted.Count == 0
                ? $"Deleted floor {gone.ElementId}."
                : $"Deleted floor {gone.ElementId}, and with it {string.Join(", ", hosted)}.");
        }

        if (transaction.Commit() != TransactionStatus.Committed)
        {
            string why = failures.Errors.Count > 0 ? ": " + string.Join(" ", failures.Errors.Distinct()) : string.Empty;
            throw new InvalidOperationException($"Revit refused to delete the floors merged into floor {edit.Floor.ElementId}; nothing changed{why}");
        }

        return failures.Warnings.Distinct().Select(text => "Revit warned: " + text).Concat(notes);
    }

    /// <summary>A floor for each piece of a split that no floor has yet, in one transaction, read back to check Revit kept each outline.</summary>
    private static IEnumerable<string> AddFloors(Document document, ShapeEdit edit)
    {
        if (document.GetElement(edit.Floor.UniqueId) is not Floor keeper || document.GetElement(keeper.SketchId) is not Sketch sketch)
        {
            throw new InvalidOperationException($"Floor {edit.Floor.ElementId} or its sketch is gone.");
        }

        double z = sketch.SketchPlane.GetPlane().Origin.Z;
        FailOnError failures = new();
        List<ElementId> created = new();
        List<Polygon2D> drawn = new();
        List<string> typeNotes = new();
        using (Transaction transaction = new(document, edit.Label()))
        {
            transaction.SetFailureHandlingOptions(transaction.GetFailureHandlingOptions().SetFailuresPreprocessor(failures));
            transaction.Start();
            foreach (NewFloor piece in edit.NewFloors)
            {
                ElementId typeId = keeper.GetTypeId();
                if (piece.TypeName != null)
                {
                    if (FloorTypeNamed(document, piece.TypeName) is FloorType type)
                    {
                        typeId = type.Id;
                    }
                    else
                    {
                        typeNotes.Add($"No floor type {piece.TypeName} in this model, so a new floor has floor {edit.Floor.ElementId}'s type. Run Set up model, then change its type.");
                    }
                }

                foreach (Polygon2D polygon in piece.Outline)
                {
                    List<CurveLoop> loops = new[] { polygon.ExteriorRing }
                        .Concat(polygon.InteriorRings)
                        .Select(ring => CurveLoop.Create(Lines(ring, z).Cast<Curve>().ToList()))
                        .ToList();
                    Floor floor = Floor.Create(document, loops, typeId, keeper.LevelId);
                    CopyValues(keeper, floor);
                    foreach (KeyValuePair<string, string> value in piece.Values)
                    {
                        if (floor.LookupParameter(value.Key) is { IsReadOnly: false, StorageType: StorageType.String } parameter)
                        {
                            parameter.Set(value.Value);
                        }
                    }

                    created.Add(floor.Id);
                    drawn.Add(polygon);
                }
            }

            if (transaction.Commit() != TransactionStatus.Committed)
            {
                string why = failures.Errors.Count > 0 ? ": " + string.Join(" ", failures.Errors.Distinct()) : string.Empty;
                throw new InvalidOperationException($"Revit refused the new floors of the split of floor {edit.Floor.ElementId}; nothing changed{why}");
            }
        }

        List<Floor> made = created.Select(id => document.GetElement(id)).OfType<Floor>().ToList();
        if (made.Count != created.Count)
        {
            throw new InvalidOperationException($"Revit did not keep every new floor of the split of floor {edit.Floor.ElementId}; nothing changed.");
        }

        return failures.Warnings.Distinct().Select(text => "Revit warned: " + text)
            .Concat(typeNotes.Distinct())
            .Concat(made.Zip(drawn, (floor, polygon) => TopFaceGap(floor, new[] { polygon })).SelectMany(note => note))
            .Concat(made.Select(floor => $"New floor {floor.Id.Value} ({floor.Name}) on the level of floor {edit.Floor.ElementId}, with the GDB's values where the drift file gives them and floor {edit.Floor.ElementId}'s otherwise."));
    }

    private static FloorType? FloorTypeNamed(Document document, string name) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .FirstOrDefault(type => type.Name == name);

    /// <summary>The kept floor's height offset and the standard's GIS values that describe a floor, not one element.</summary>
    private static void CopyValues(Floor from, Floor to)
    {
        Copy(from.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM), to.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM));
        Copy(from.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL), to.get_Parameter(BuiltInParameter.FLOOR_PARAM_IS_STRUCTURAL));
        foreach (string name in GisShapeEdits.CopiedToNewFloors)
        {
            Copy(from.LookupParameter(name), to.LookupParameter(name));
        }
    }

    private static void Copy(Parameter? from, Parameter? to)
    {
        if (from == null || to == null || to.IsReadOnly || !from.HasValue || from.StorageType != to.StorageType)
        {
            return;
        }

        switch (from.StorageType)
        {
            case StorageType.String:
                to.Set(from.AsString() ?? string.Empty);
                break;
            case StorageType.Integer:
                to.Set(from.AsInteger());
                break;
            case StorageType.Double:
                to.Set(from.AsDouble());
                break;
            case StorageType.ElementId:
                to.Set(from.AsElementId());
                break;
        }
    }

    private static IEnumerable<Floor> OtherFloorsOnLevel(Document document, Floor floor) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(Floor))
            .WherePasses(new ElementLevelFilter(floor.LevelId))
            .Cast<Floor>()
            .Where(other => other.Id != floor.Id);

    /// <summary>Redraws the floor's sketch as the edit's outline, as one SketchEditScope.</summary>
    private static IReadOnlyList<string> Redraw(Document document, ShapeEdit edit)
    {
        if (document.GetElement(edit.Floor.UniqueId) is not Floor floor || document.GetElement(floor.SketchId) is not Sketch sketch)
        {
            throw new InvalidOperationException($"Floor {edit.Floor.ElementId} or its sketch is gone.");
        }

        HashSet<ElementId> boundary = ProfileCurveIds(sketch);
        double z = sketch.SketchPlane.GetPlane().Origin.Z;
        List<(ElementId Id, string Name)> dependents = floor.GetDependentElements(null)
            .Where(id => id != floor.Id && id != sketch.Id && !boundary.Contains(id))
            .Select(id => (id, Describe(document.GetElement(id))))
            .Where(entry => entry.Item2.Length > 0)
            .ToList();
        FailOnError failures = new();
        SketchEditScope scope = new(document, edit.Label());
        scope.Start(sketch.Id);
        try
        {
            using (Transaction transaction = new(document, edit.Label()))
            {
                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(failures);
                transaction.SetFailureHandlingOptions(options);
                transaction.Start();
                document.Delete(boundary);
                SketchPlane plane = sketch.SketchPlane;
                foreach (Polygon2D polygon in edit.Sketch)
                {
                    foreach (IReadOnlyList<Point2D> ring in new[] { polygon.ExteriorRing }.Concat(polygon.InteriorRings))
                    {
                        foreach (Line line in Lines(ring, z))
                        {
                            document.Create.NewModelCurve(line, plane);
                        }
                    }
                }

                if (transaction.Commit() != TransactionStatus.Committed)
                {
                    throw new InvalidOperationException($"Revit did not take the new sketch of floor {edit.Floor.ElementId}.");
                }
            }

            scope.Commit(failures);
        }
        finally
        {
            if (scope.IsActive)
            {
                scope.Cancel();
            }
        }

        // An error in Revit's sketch check rolls the edit back without an exception, and the rollback brings the old boundary lines back.
        if (failures.Errors.Count > 0 || boundary.Any(id => document.GetElement(id) != null) || document.GetElement(edit.Floor.UniqueId) is not Floor after)
        {
            string why = failures.Errors.Count > 0 ? ": " + string.Join(" ", failures.Errors.Distinct()) : string.Empty;
            throw new InvalidOperationException($"Revit refused the new outline of floor {edit.Floor.ElementId} ({Loops(edit.Sketch)}); the floor is unchanged{why}");
        }

        List<string> notes = failures.Warnings.Distinct().Select(text => "Revit warned: " + text).ToList();
        notes.AddRange(TopFaceGap(after, edit.Outline));

        notes.AddRange(dependents
            .Where(entry => document.GetElement(entry.Id) == null)
            .Select(entry => $"Revit deleted {entry.Name} {entry.Id.Value}, which hung on the floor outside its new outline."));
        return notes;
    }

    /// <summary>A note when Revit drew the floor's top face away from the GDB outline, most likely where another element joins or cuts it.</summary>
    private static IEnumerable<string> TopFaceGap(Floor floor, IReadOnlyList<Polygon2D> outline)
    {
        double gap = GisShapeEdits.GapMetres(FloorTopFaces.Read(floor).Outline, outline);
        if (gap > GisShapeEdits.SameOutlineMetres)
        {
            yield return $"Floor {floor.Id.Value}'s top face is up to {Centimetres(gap)} off the GDB outline, most likely where another element joins or cuts it; the next export keeps that difference.";
        }
    }

    // What the refused sketch was, so a report shows whether a hole or a second part made Revit refuse it.
    private static string Loops(IReadOnlyList<Polygon2D> sketch) =>
        $"sketch of {sketch.Count} part(s), {sketch.Sum(polygon => polygon.InteriorRings.Count)} hole(s), {sketch.Sum(polygon => polygon.ExteriorRing.Count + polygon.InteriorRings.Sum(ring => ring.Count))} line(s)";

    private static string Centimetres(double metres) => double.IsInfinity(metres) ? "the whole outline" : $"{metres * 100:F0} cm";

    /// <summary>A named element that depends on the floor (an opening, a hosted family), or empty for sketch parts and internals.</summary>
    private static string Describe(Element? element) =>
        element switch
        {
            Opening => "opening",
            FamilyInstance instance => $"{instance.Symbol?.FamilyName ?? "family"} ({instance.Category?.Name ?? "element"})",
            null => string.Empty,
            _ when element.Category?.CategoryType == CategoryType.Model => element.Category.Name,
            _ => string.Empty,
        };

    /// <summary>The level's GIS_ plan, else any plan of the level, to preview a shape edit in.</summary>
    public static ViewPlan? PlanViewOf(Document document, ElementId levelId)
    {
        List<ViewPlan> plans = new FilteredElementCollector(document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(view => !view.IsTemplate && view.ViewType == ViewType.FloorPlan && view.GenLevel?.Id == levelId)
            .ToList();
        return plans.FirstOrDefault(view => view.Name.StartsWith("GIS_", StringComparison.OrdinalIgnoreCase)) ?? plans.FirstOrDefault();
    }

    /// <summary>Why the sketch should not be redrawn: a slope arrow or other extra sketch line would end in the wrong place, and shape-edited points would be dropped.</summary>
    private static string? WhyNotEditable(Document document, Floor floor)
    {
        if (document.GetElement(floor.SketchId) is not Sketch sketch)
        {
            return "has no sketch Revit lets the API edit";
        }

        if (floor.GetSlabShapeEditor() is { IsEnabled: true })
        {
            return "has shape-edited points";
        }

        // The GDB holds polylines: redrawing would turn every arc into short segments, also where the GDB left it alone.
        if (sketch.Profile.Cast<CurveArray>().Any(loop => loop.Cast<Curve>().Any(curve => curve is not Line)))
        {
            return GisShapeEdits.CurvedEdges;
        }

        HashSet<ElementId> boundary = ProfileCurveIds(sketch);
        bool extraLines = sketch.GetAllElements().Any(id => !boundary.Contains(id) && document.GetElement(id) is CurveElement);
        return extraLines ? "has a slope arrow or other sketch line besides its boundary" : null;
    }

    /// <summary>The region the floor's sketch encloses, in model feet, or null when it has no sketch.</summary>
    private static List<Polygon2D>? SketchOf(Document document, Floor floor) =>
        document.GetElement(floor.SketchId) is Sketch sketch
            ? GisShapeEdits.FromLoops(sketch.Profile.Cast<CurveArray>().Select(Loop))
            : null;

    // Each curve's points but its last, which the next curve starts at.
    private static IReadOnlyList<Point2D> Loop(CurveArray loop) =>
        loop.Cast<Curve>()
            .SelectMany(curve =>
            {
                IList<XYZ> points = curve.Tessellate();
                return points.Take(points.Count - 1);
            })
            .Select(point => new Point2D(point.X, point.Y))
            .ToList();

    private static HashSet<ElementId> ProfileCurveIds(Sketch sketch)
    {
        HashSet<ElementId> ids = new();
        foreach (CurveArray loop in sketch.Profile)
        {
            foreach (Curve curve in loop)
            {
                if (curve.Reference?.ElementId is ElementId id && id != ElementId.InvalidElementId)
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static IEnumerable<Line> Lines(IReadOnlyList<Point2D> ring, double z)
    {
        // Polygon2D rings are closed: the last point repeats the first.
        int count = ring.Count > 1 && ring[0].X == ring[ring.Count - 1].X && ring[0].Y == ring[ring.Count - 1].Y ? ring.Count - 1 : ring.Count;
        for (int index = 0; index < count; index++)
        {
            Point2D a = ring[index];
            Point2D b = ring[(index + 1) % count];
            yield return Line.CreateBound(new XYZ(a.X, a.Y, z), new XYZ(b.X, b.Y, z));
        }
    }

    private static int ModelEpsg(Document document)
    {
        try
        {
            return new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document))?.ProjectCrs?.EpsgCode ?? 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// Records every warning's text and lets it through; rolls the edit back on an error, so a
    /// sketch Revit cannot close changes nothing. The texts go into the apply result.
    /// </summary>
    private sealed class FailOnError : IFailuresPreprocessor
    {
        public List<string> Warnings { get; } = new();

        public List<string> Errors { get; } = new();

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            bool error = false;
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                string text = failure.GetDescriptionText();
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    Warnings.Add(text);
                    failuresAccessor.DeleteWarning(failure);
                }
                else
                {
                    Errors.Add(text);
                    error = true;
                }
            }

            if (error)
            {
                // Without this Revit still shows the rolled-back errors in its own dialog, which waits for a click.
                FailureHandlingOptions options = failuresAccessor.GetFailureHandlingOptions();
                options.SetClearAfterRollback(true);
                failuresAccessor.SetFailureHandlingOptions(options);
            }

            return error ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
        }
    }
}
