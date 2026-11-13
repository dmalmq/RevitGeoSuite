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
    public static (IReadOnlyList<ShapeEdit> Edits, IReadOnlyList<string> Problems) Plan(Document document, GisDriftFile file)
    {
        if (file.Shapes.Count == 0)
        {
            return (Array.Empty<ShapeEdit>(), Array.Empty<string>());
        }

        HashSet<string> wanted = new(file.Shapes.Select(shape => shape.UniqueId).Where(id => id.Length > 0), StringComparer.Ordinal);
        Dictionary<string, ShapeFloor> floors = new(StringComparer.Ordinal);
        foreach (string uniqueId in wanted)
        {
            if (document.GetElement(uniqueId) is Floor floor)
            {
                floors[uniqueId] = new ShapeFloor(uniqueId, floor.Id.Value, FloorTopFaces.Read(floor).Outline, WhyNotEditable(document, floor));
            }
        }

        return GisShapeEdits.Plan(file, floors, new SharedCoordinateProjector(document.ActiveProjectLocation).Transform, ModelEpsg(document));
    }

    /// <summary>
    /// Redraws the floor's sketch as the edit's outline. The floor keeps its UniqueId, so its
    /// revit_key, id and parameters stay. One undo step named after the edit.
    /// </summary>
    public static void Apply(Document document, ShapeEdit edit)
    {
        if (document.GetElement(edit.Floor.UniqueId) is not Floor floor || document.GetElement(floor.SketchId) is not Sketch sketch)
        {
            throw new InvalidOperationException($"Floor {edit.Floor.ElementId} or its sketch is gone.");
        }

        HashSet<ElementId> boundary = ProfileCurveIds(sketch);
        double z = sketch.SketchPlane.GetPlane().Origin.Z;
        SketchEditScope scope = new(document, edit.Label());
        scope.Start(sketch.Id);
        try
        {
            using (Transaction transaction = new(document, edit.Label()))
            {
                transaction.Start();
                document.Delete(boundary);
                SketchPlane plane = sketch.SketchPlane;
                foreach (Polygon2D polygon in edit.Outline)
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

            scope.Commit(new FailOnError());
        }
        finally
        {
            if (scope.IsActive)
            {
                scope.Cancel();
            }
        }

        // An error in Revit's sketch check rolls the edit back without an exception: read the floor again.
        if (document.GetElement(edit.Floor.UniqueId) is not Floor after || !GisShapeEdits.SameOutline(FloorTopFaces.Read(after).Outline, edit.Outline))
        {
            throw new InvalidOperationException($"Revit refused the new outline of floor {edit.Floor.ElementId}; the floor is unchanged.");
        }
    }

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

        HashSet<ElementId> boundary = ProfileCurveIds(sketch);
        bool extraLines = sketch.GetAllElements().Any(id => !boundary.Contains(id) && document.GetElement(id) is CurveElement);
        return extraLines ? "has a slope arrow or other sketch line besides its boundary" : null;
    }

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

    /// <summary>Lets warnings through and rolls the edit back on an error, so a sketch Revit cannot close changes nothing.</summary>
    private sealed class FailOnError : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            foreach (FailureMessageAccessor failure in failuresAccessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    failuresAccessor.DeleteWarning(failure);
                }
                else
                {
                    return FailureProcessingResult.ProceedWithRollBack;
                }
            }

            return FailureProcessingResult.Continue;
        }
    }
}
