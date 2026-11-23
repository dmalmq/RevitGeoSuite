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
using GisKeep = RevitGeoSuite.FloorPlanExport.Core.Standards.GisKeep;
using GisKeepEntry = RevitGeoSuite.FloorPlanExport.Core.Standards.GisKeepEntry;
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

            // Rows of linked models are listed, never looked up: their UniqueIds are not this model's.
            GisDriftHostScope scope = GisDriftHostScope.Of(GisDriftWork.Read(path), modelKey);
            GisDriftFile file = scope.File;
            (IReadOnlyList<GisDriftChange> changes, IReadOnlyList<string> problems) = GisDriftWork.Plan(doc, file);
            response.Source = string.Equals(path, expected, StringComparison.OrdinalIgnoreCase) ? "station" : "picked";
            response.Path = path;
            response.FileModel = file.Model;
            response.AppliedVersion = file.AppliedVersion;
            response.Changes = changes.Select(GisDriftWork.Item).ToArray();
            (IReadOnlyList<ShapeEdit> shapes, IReadOnlyList<string> shapeProblems) = ShapeEditWork.Plan(doc, file);
            response.Shapes = shapes.Select(GisDriftWork.Item).ToArray();
            response.ShapeFloorTypes = shapes.Any(edit => edit.Kind == ShapeEditKind.Create) ? ShapeEditWork.StandardFloorTypesIn(doc).ToArray() : Array.Empty<string>();
            response.Problems = scope.Problems.Concat(problems).Concat(shapeProblems).ToArray();
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
            (IReadOnlyList<GisDriftChange> planned, _) = GisDriftWork.Plan(doc, GisDriftWork.ReadFor(doc, request.Path));
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

/// <summary>
/// Review GDB edits: answers the ticked rows "Keep Revit's". Each answer goes into the station's
/// <c>gis-keep.json</c> beside the drift file, where Kanizu reads it and takes Revit's value into
/// the GDB at its next apply. Changes nothing in the model.
/// </summary>
public sealed class GisDriftKeepHandler : IRpcHandler
{
    public string Method => "gisDrift.keep";

    public Task<object?> HandleAsync(object? payload)
    {
        GisDriftApplyRequest request = (payload as JObject)?.ToObject<GisDriftApplyRequest>() ?? new GisDriftApplyRequest();
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireProject(doc);
            (string modelKey, string station, string expected) = GisDriftSource.Locate(doc);
            if (expected.Length == 0)
            {
                throw new InvalidOperationException("This model has no model id or station folder yet, so there is no station to record the answer in. Export it to the station folder first.");
            }

            // The answer is filed in the model's own station, so it must answer that station's drift file.
            if (!SamePath(request.Path, expected))
            {
                throw new InvalidOperationException($"Keep Revit's value answers the station's own drift file ({expected}), not a file picked by hand.");
            }

            GisDriftFile read = GisDriftWork.Read(request.Path);
            if (!string.Equals(read.Model, modelKey, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The drift file is for model {read.Model}, not the open model ({modelKey}).");
            }

            (IReadOnlyList<GisDriftChange> planned, _) = GisDriftWork.Plan(doc, GisDriftHostScope.Of(read, modelKey).File);
            (IReadOnlyList<GisDriftChange> changes, IReadOnlyList<string> skipped) = GisDrift.Accept(
                planned,
                request.Changes.Select(item => (item.UniqueId, item.Parameter, item.Binding, item.NewValue, item.Label)));
            (IReadOnlyList<GisKeepEntry> entries, IReadOnlyList<string> notKept) = GisKeep.EntriesFor(
                changes.Select(change => change.Row),
                GisKeep.Timestamp(DateTime.UtcNow));
            if (entries.Count > 0)
            {
                try
                {
                    // Under the model name the drift file carries: Kanizu refuses a keep file of another model.
                    GisKeep.Add(GisKeep.PathFor(station, read.Model), read.Model, entries);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or FormatException or ArgumentException)
                {
                    throw new InvalidOperationException($"Could not record the answer in the station's keep file: {error.Message}", error);
                }
            }

            return (object?)new GisStandardApplyResponse
            {
                Log = entries
                    .Select(entry => $"{entry.Layer} {entry.Field} of {entry.RevitKey}: Revit's {Shown(entry.Revit)} is kept over the GDB's {Shown(entry.Gdb)}; Kanizu takes it in at its next apply.")
                    .ToArray(),
                Skipped = skipped.Concat(notKept).ToArray(),
            };
        });
    }

    private static string Shown(string value) => value.Length > 0 ? value : "(empty)";

    private static bool SamePath(string left, string right)
    {
        try
        {
            return left.Length > 0 && string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
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
            // A hand-drawn unit has no floor yet: draw it at its level's elevation.
            Floor? floor = doc.GetElement(edit.Floor.UniqueId) as Floor;
            ElementId levelId = floor?.LevelId ?? new ElementId(edit.Floor.LevelId);
            if (ShapeEditWork.PlanViewOf(doc, levelId) is not ViewPlan view)
            {
                return (object?)new GisShapeShowResponse();
            }

            double z = (floor != null ? FloorTopZ(floor) : (doc.GetElement(levelId) as Level)?.ProjectElevation ?? 0) + 0.15;
            List<OverlayLine> lines = edit.Absorbed.Select(floor => floor.Outline).Prepend(edit.Floor.Outline).SelectMany(outline => Lines(outline, z, new Color(128, 128, 128)))
                .Concat(edit.NewFloors.Select(piece => piece.Outline).Prepend(edit.Outline).SelectMany(outline => Lines(outline, z, new Color(234, 88, 12))))
                .ToList();
            OutlineOverlay.Show(view, lines);
            if (uiDocument.ActiveView.Id != view.Id)
            {
                uiDocument.ActiveView = view;
            }

            if (floor != null)
            {
                uiDocument.ShowElements(floor.Id);
            }

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
            (IReadOnlyList<ShapeEdit> planned, _) = ShapeEditWork.Plan(doc, GisDriftWork.ReadFor(doc, request.Path));
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
                    string? typeName = request.Types.FirstOrDefault(choice => choice.UniqueId == uniqueId)?.TypeName;
                    IReadOnlyList<string> notes = ShapeEditWork.Apply(doc, edit, string.IsNullOrEmpty(typeName) ? null : typeName);
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
        (IReadOnlyList<ShapeEdit> edits, _) = ShapeEditWork.Plan(document, ReadFor(document, path));
        return edits.FirstOrDefault(edit => edit.Floor.UniqueId == uniqueId)
            ?? throw new InvalidOperationException("The drift file no longer plans this shape edit. Check again.");
    }

    public static GisShapeEditItem Item(ShapeEdit edit) => new()
    {
        UniqueId = edit.Floor.UniqueId,
        Kind = edit.Kind switch
        {
            ShapeEditKind.Split => "split",
            ShapeEditKind.Merge => "merge",
            ShapeEditKind.Create => "create",
            _ => "reshape",
        },
        Floors = edit.Kind switch
        {
            ShapeEditKind.Merge => edit.Absorbed.Count + 1,
            ShapeEditKind.Create => 1,
            _ => edit.NewFloors.Count + 1,
        },
        TypeName = edit.Kind == ShapeEditKind.Create ? edit.NewFloors[0].TypeName ?? string.Empty : string.Empty,
        ElementId = edit.Floor.ElementId,
        Feature = edit.Shape.Label.Length > 0 ? edit.Shape.Label : edit.Floor.UniqueId,
        Label = edit.Label(),
        Before = edit.Absorbed.Select(floor => floor.Outline).Prepend(edit.Floor.Outline).SelectMany(Rings).ToArray(),
        After = edit.NewFloors.Select(piece => piece.Outline).Prepend(edit.Outline).SelectMany(Rings).ToArray(),
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
    /// The drift file at <paramref name="path"/>, with only the rows and shapes of
    /// <paramref name="document"/> itself. Everything that finds an element by UniqueId plans from
    /// this, so a row of a linked model never reaches an element of the host that shares its UniqueId.
    /// </summary>
    public static GisDriftFile ReadFor(Document document, string path) =>
        GisDriftHostScope.Of(Read(path), GisDriftSource.Locate(document).ModelKey).File;

    /// <summary>
    /// The GDB edits in the model's own drift file (the station's, not a hand-picked one) that the
    /// model does not match yet, and that file's path. 0 when there is no file; an unreadable file
    /// counts as 0 here and shows its error on the GDB edits tab.
    /// </summary>
    public static (int Count, string Path) Waiting(Document document)
    {
        (string modelKey, _, string expected) = GisDriftSource.Locate(document);
        if (expected.Length == 0 || !File.Exists(expected))
        {
            return (0, string.Empty);
        }

        GisDriftHostScope scope;
        try
        {
            scope = GisDriftHostScope.Of(Read(expected), modelKey);
        }
        catch (InvalidOperationException)
        {
            return (0, expected);
        }

        (IReadOnlyList<GisDriftChange> changes, IReadOnlyList<string> problems) = Plan(document, scope.File);
        return (GisDrift.Waiting(changes, scope.Problems.Concat(problems).ToList(), ShapeEditWork.Plan(document, scope.File)), expected);
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
