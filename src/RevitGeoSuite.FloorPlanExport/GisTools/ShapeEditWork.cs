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
                floors[uniqueId] = new ShapeFloor(uniqueId, floor.Id.Value, FloorTopFaces.Read(floor).Outline, WhyNotEditable(document, floor), SketchOf(document, floor));
            }
        }

        return GisShapeEdits.Plan(file, floors, new SharedCoordinateProjector(document.ActiveProjectLocation).Transform, ModelEpsg(document));
    }

    /// <summary>
    /// Redraws the floor's sketch as the edit's sketch, which gives the GDB outline. The floor keeps its UniqueId, so its
    /// revit_key, id and parameters stay. One undo step named after the edit.
    /// </summary>
    /// <returns>What Revit warned about, and the elements that hung on the floor and are gone after it, one line each.</returns>
    public static IReadOnlyList<string> Apply(Document document, ShapeEdit edit)
    {
        // In a group, so an outline Revit takes but draws differently is rolled back and the floor is truly unchanged.
        using TransactionGroup group = new(document, edit.Label());
        group.Start();
        try
        {
            IReadOnlyList<string> notes = Redraw(document, edit);
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
            throw new InvalidOperationException($"Revit refused the new outline of floor {edit.Floor.ElementId}; the floor is unchanged{why}");
        }

        List<string> notes = failures.Warnings.Distinct().Select(text => "Revit warned: " + text).ToList();
        double gap = GisShapeEdits.GapMetres(FloorTopFaces.Read(after).Outline, edit.Outline);
        if (gap > GisShapeEdits.SameOutlineMetres)
        {
            notes.Add($"The floor's top face is up to {Centimetres(gap)} off the GDB outline, most likely where another element joins or cuts it; the next export keeps that difference.");
        }

        notes.AddRange(dependents
            .Where(entry => document.GetElement(entry.Id) == null)
            .Select(entry => $"Revit deleted {entry.Name} {entry.Id.Value}, which hung on the floor outside its new outline."));
        return notes;
    }

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
