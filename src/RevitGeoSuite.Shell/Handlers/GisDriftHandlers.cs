using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using Autodesk.Revit.DB;
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
            response.Problems = problems.ToArray();
            return response;
        });
    }
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
            return new GisStandardApplyResponse { Log = log.ToArray(), Skipped = skipped.Concat(notWritten).ToArray() };
        });
    }
}

internal static class GisDriftWork
{
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
