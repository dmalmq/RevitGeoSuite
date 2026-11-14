using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.Shell.Overlay;
using RevitGeoSuite.Shell.Standards;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.GisTools;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>Review GDB edits: reads the drift file Kanizu wrote for this model and previews what accepting it changes. Reads only.</summary>
public sealed class GisDriftInspectHandler : IRpcHandler
{
    public string Method => "gisDrift.inspect";

    public Task<object?> HandleAsync(object? payload)
    {
        GisDriftInspectRequest request = (payload as JObject)?.ToObject<GisDriftInspectRequest>() ?? new GisDriftInspectRequest();
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireProject(doc);
            (string modelKey, string station, string expected) = GisDriftSource.Locate(doc);
            string path = request.Path;
            if (request.Pick)
            {
                using OpenFileDialog dialog = new() { Filter = "Kanizu drift file (*.gis-drift.json)|*.gis-drift.json|JSON (*.json)|*.json", CheckFileExists = true };
                if (expected.Length > 0 && Directory.Exists(Path.GetDirectoryName(expected)))
                {
                    dialog.InitialDirectory = Path.GetDirectoryName(expected);
                }

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return (object?)new GisDriftInspectResponse { Cancelled = true };
                }

                path = dialog.FileName;
            }

            GisDriftInspectResponse response = new()
            {
                DocumentTitle = doc.Title ?? string.Empty,
                ModelKey = modelKey,
                StationFolder = station,
                ExpectedPath = expected,
            };
            if (path.Length == 0)
            {
                if (expected.Length == 0 || !File.Exists(expected))
                {
                    response.Source = "none";
                    return response;
                }

                path = expected;
            }

            GisDriftFile file = GisDriftWork.Read(path);
            (IReadOnlyList<GisDriftChange> changes, IReadOnlyList<string> problems) = GisDriftWork.Plan(doc, file);
            response.Source = string.Equals(path, expected, StringComparison.OrdinalIgnoreCase) ? "station" : "picked";
            response.Path = path;
            response.FileModel = file.Model;
            response.AppliedVersion = file.AppliedVersion;
            response.Changes = changes.Select(GisDriftWork.Item).ToArray();
            (IReadOnlyList<ShapeEdit> shapes, IReadOnlyList<string> shapeProblems) = ShapeEditWork.Plan(doc, file);
            response.Shapes = shapes.Select(GisDriftWork.Item).ToArray();
            response.Problems = problems.Concat(shapeProblems).ToArray();
            return response;
        });
    }
}

/// <summary>The count of GDB edits that wait in the model's own drift file, for the export page's notice. Reads only.</summary>
public sealed class GisDriftWaitingHandler : IRpcHandler
{
    public string Method => "gisDrift.waiting";

    public Task<object?> HandleAsync(object? payload) =>
        RevitContext.Instance.InvokeAsync(app =>
        {
            Document? doc = app.ActiveUIDocument?.Document;
            (int count, string path) = doc == null || doc.IsFamilyDocument ? (0, string.Empty) : GisDriftWork.Waiting(doc);
            return (object?)new GisDriftWaitingResponse { Count = count, Path = path };
        });
}

/// <summary>Review GDB edits: writes the ticked GDB values that the file still plans, in one transaction.</summary>
public sealed class GisDriftApplyHandler : IRpcHandler
{
    public string Method => "gisDrift.apply";

    public Task<object?> HandleAsync(object? payload)
    {
        GisDriftApplyRequest request = (payload as JObject)?.ToObject<GisDriftApplyRequest>() ?? new GisDriftApplyRequest();
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireEditable(doc);
            (IReadOnlyList<GisDriftChange> planned, _) = GisDriftWork.Plan(doc, GisDriftWork.Read(request.Path));
            (IReadOnlyList<GisDriftChange> changes, IReadOnlyList<string> skipped) = GisDrift.Accept(
                planned,
                request.Changes.Select(item => (item.UniqueId, item.Parameter, item.Binding, item.NewValue, item.Label)));
            if (changes.Count == 0)
            {
                return (object?)new GisStandardApplyResponse { Skipped = skipped.ToArray() };
            }

            (IReadOnlyList<string> log, IReadOnlyList<string> notWritten) = GisStandardWriter.ApplyDrift(doc, changes);
            GisStandardNotifier.Instance.Refresh(doc, force: true);
            return new GisStandardApplyResponse { Log = log.ToArray(), Skipped = skipped.Concat(notWritten).ToArray() };
        });
    }
}

/// <summary>Draws one shape edit's outlines (the floor now in grey, the GDB's in orange) over the floor's plan in Revit. Changes nothing in the model.</summary>
public sealed class GisShapeShowHandler : IRpcHandler
{
    public string Method => "gisDrift.showShape";

    public Task<object?> HandleAsync(object? payload)
    {
        GisShapeShowRequest request = (payload as JObject)?.ToObject<GisShapeShowRequest>() ?? new GisShapeShowRequest();
        return RevitContext.Instance.InvokeAsync(app =>
        {
            UIDocument uiDocument = app.ActiveUIDocument ?? throw new InvalidOperationException("No active Revit document.");
            Document doc = uiDocument.Document;
            ShapeEdit edit = GisDriftWork.Find(doc, request.Path, request.UniqueId);
            if (doc.GetElement(edit.Floor.UniqueId) is not Floor floor || ShapeEditWork.PlanViewOf(doc, floor.LevelId) is not ViewPlan view)
            {
                return (object?)new GisShapeShowResponse();
            }

            double z = FloorTopZ(floor) + 0.15;
            List<OverlayLine> lines = Lines(edit.Floor.Outline, z, new Color(128, 128, 128))
                .Concat(Lines(edit.Outline, z, new Color(234, 88, 12)))
                .ToList();
            OutlineOverlay.Show(view, lines);
            if (uiDocument.ActiveView.Id != view.Id)
            {
                uiDocument.ActiveView = view;
            }

            uiDocument.ShowElements(floor.Id);
            uiDocument.RefreshActiveView();
            return new GisShapeShowResponse { View = view.Name };
        });
    }

    private static double FloorTopZ(Floor floor) =>
        floor.get_BoundingBox(null)?.Max.Z ?? 0;

    private static IEnumerable<OverlayLine> Lines(IReadOnlyList<Polygon2D> outline, double z, Color color)
    {
        foreach (Polygon2D polygon in outline)
        {
            foreach (IReadOnlyList<Point2D> ring in new[] { polygon.ExteriorRing }.Concat(polygon.InteriorRings))
            {
                for (int index = 0; index + 1 < ring.Count; index++)
                {
                    yield return new OverlayLine(new XYZ(ring[index].X, ring[index].Y, z), new XYZ(ring[index + 1].X, ring[index + 1].Y, z), color);
                }
            }
        }
    }
}

/// <summary>Removes the outline preview.</summary>
public sealed class GisShapeClearHandler : IRpcHandler
{
    public string Method => "gisDrift.clearShape";

    public Task<object?> HandleAsync(object? payload) =>
        RevitContext.Instance.InvokeAsync(app =>
        {
            OutlineOverlay.Clear();
            app.ActiveUIDocument?.RefreshActiveView();
            return (object?)null;
        });
}

/// <summary>Review GDB edits: applies the ticked shape edits the file still plans, each as its own undo step.</summary>
public sealed class GisShapeApplyHandler : IRpcHandler
{
    public string Method => "gisDrift.applyShapes";

    public Task<object?> HandleAsync(object? payload)
    {
        GisShapeApplyRequest request = (payload as JObject)?.ToObject<GisShapeApplyRequest>() ?? new GisShapeApplyRequest();
        return RevitContext.Instance.InvokeAsync(app =>
        {
            Document doc = app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active Revit document.");
            GisStandardWork.RequireEditable(doc);
            OutlineOverlay.Clear();
            (IReadOnlyList<ShapeEdit> planned, _) = ShapeEditWork.Plan(doc, GisDriftWork.Read(request.Path));
            Dictionary<string, ShapeEdit> byFloor = planned.ToDictionary(edit => edit.Floor.UniqueId, StringComparer.Ordinal);
            List<string> log = new();
            List<string> skipped = new();
            foreach (string uniqueId in request.UniqueIds.Distinct(StringComparer.Ordinal))
            {
                if (!byFloor.TryGetValue(uniqueId, out ShapeEdit? edit))
                {
                    skipped.Add($"{uniqueId}: the file no longer plans a shape edit for it, or the floor already has the GDB outline.");
                    continue;
                }

                try
                {
                    IReadOnlyList<string> notes = ShapeEditWork.Apply(doc, edit);
                    log.Add(edit.Label());
                    log.AddRange(notes.Select(note => "  " + note));
                }
                catch (Exception error) when (error is InvalidOperationException or Autodesk.Revit.Exceptions.ApplicationException or ArgumentException)
                {
                    skipped.Add($"{edit.Label()}: {error.Message}");
                }
            }

            app.ActiveUIDocument?.RefreshActiveView();
            GisStandardNotifier.Instance.Refresh(doc, force: true);
            return (object?)new GisStandardApplyResponse { Log = log.ToArray(), Skipped = skipped.ToArray() };
        });
    }
}

internal static class GisDriftWork
{
    private const double MetresPerFoot = 0.3048;

    /// <summary>The planned shape edit of one floor, or an error the page shows.</summary>
    public static ShapeEdit Find(Document document, string path, string uniqueId)
    {
        (IReadOnlyList<ShapeEdit> edits, _) = ShapeEditWork.Plan(document, Read(path));
        return edits.FirstOrDefault(edit => edit.Floor.UniqueId == uniqueId)
            ?? throw new InvalidOperationException("The drift file no longer plans this shape edit. Check again.");
    }

    public static GisShapeEditItem Item(ShapeEdit edit) => new()
    {
        UniqueId = edit.Floor.UniqueId,
        Kind = "reshape",
        ElementId = edit.Floor.ElementId,
        Feature = edit.Shape.Label.Length > 0 ? edit.Shape.Label : edit.Floor.UniqueId,
        Label = edit.Label(),
        Before = Rings(edit.Floor.Outline),
        After = Rings(edit.Outline),
    };

    private static double[][][] Rings(IReadOnlyList<Polygon2D> outline) =>
        outline
            .SelectMany(polygon => new[] { polygon.ExteriorRing }.Concat(polygon.InteriorRings))
            .Select(ring => ring.Select(point => new[] { Math.Round(point.X * MetresPerFoot, 3), Math.Round(point.Y * MetresPerFoot, 3) }).ToArray())
            .ToArray();

    public static GisDriftFile Read(string path)
    {
        try
        {
            return GisDrift.Read(path);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
        {
            throw new InvalidOperationException($"Could not read the drift file {path}: {error.Message}", error);
        }
    }

    /// <summary>
    /// The GDB edits in the model's own drift file (the station's, not a hand-picked one) that the
    /// model does not match yet, and that file's path. 0 when there is no file; an unreadable file
    /// counts as 0 here and shows its error on the GDB edits tab.
    /// </summary>
    public static (int Count, string Path) Waiting(Document document)
    {
        string expected = GisDriftSource.Locate(document).ExpectedPath;
        if (expected.Length == 0 || !File.Exists(expected))
        {
            return (0, string.Empty);
        }

        GisDriftFile file;
        try
        {
            file = Read(expected);
        }
        catch (InvalidOperationException)
        {
            return (0, expected);
        }

        (IReadOnlyList<GisDriftChange> changes, IReadOnlyList<string> problems) = Plan(document, file);
        return (GisDrift.Waiting(changes, problems, ShapeEditWork.Plan(document, file)), expected);
    }

    public static (IReadOnlyList<GisDriftChange> Changes, IReadOnlyList<string> Problems) Plan(Document document, GisDriftFile file) =>
        GisDrift.Plan(file, (uniqueId, name, binding) => GisDriftSource.CurrentValue(document, uniqueId, name, binding), GisToolStandard.LoadEmbedded());

    public static GisDriftChangeItem Item(GisDriftChange change) => new()
    {
        UniqueId = change.UniqueId,
        Layer = change.Row.Layer,
        Field = change.Row.Field,
        Feature = change.Row.Label.Length > 0 ? change.Row.Label : change.UniqueId,
        Parameter = change.Parameter,
        Binding = change.Binding,
        OldValue = change.OldValue,
        NewValue = change.NewValue,
        Conflict = change.Conflict,
        Label = change.Label(),
    };
}
