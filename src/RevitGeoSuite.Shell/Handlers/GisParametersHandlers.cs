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

/// <summary>Parameters: writes the model's GIS parameters as a CSV for Excel (UTF-8 with a byte order mark).</summary>
public sealed class GisParametersWriteHandler : IRpcHandler
{
    public string Method => "gisParameters.write";

    public Task<object?> HandleAsync(object? payload) =>
        RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireProject(doc);
            List<IReadOnlyDictionary<string, string>> rows = ModelSnapshotReader.ParameterRows(doc);
            using SaveFileDialog dialog = new()
            {
                Filter = "CSV (*.csv)|*.csv",
                DefaultExt = "csv",
                AddExtension = true,
                OverwritePrompt = true,
                FileName = $"{Path.GetFileNameWithoutExtension(doc.Title)}_gis-parameters.csv",
            };
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return (object?)new GisParametersWriteResponse();
            }

            ParameterTable.WriteCsv(dialog.FileName, rows);
            return new GisParametersWriteResponse { Path = dialog.FileName, RowCount = rows.Count };
        });
}

/// <summary>Parameters: reads an edited CSV back and previews the cells it would change. Reads only.</summary>
public sealed class GisParametersReadHandler : IRpcHandler
{
    public string Method => "gisParameters.read";

    public Task<object?> HandleAsync(object? payload) =>
        RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireProject(doc);
            using OpenFileDialog dialog = new() { Filter = "CSV (*.csv)|*.csv", CheckFileExists = true };
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return (object?)new GisParametersReadResponse();
            }

            IReadOnlyList<Dictionary<string, string>> edited = ParameterTable.ReadCsv(dialog.FileName);
            (IReadOnlyList<ParameterTableChange> changes, IReadOnlyList<string> problems) =
                ParameterTable.Diff(ModelSnapshotReader.ParameterRows(doc), edited, GisToolStandard.LoadEmbedded());
            return new GisParametersReadResponse
            {
                Path = dialog.FileName,
                RowCount = edited.Count,
                Changes = changes.Select(change => new GisParameterChangeItem
                {
                    ElementId = change.ElementId,
                    Kind = change.Kind,
                    Column = change.Column,
                    OldValue = change.OldValue,
                    NewValue = change.NewValue,
                }).ToArray(),
                Problems = problems.ToArray(),
            };
        });
}

/// <summary>Parameters: writes the ticked changes that still apply, in one transaction.</summary>
public sealed class GisParametersApplyHandler : IRpcHandler
{
    public string Method => "gisParameters.apply";

    public Task<object?> HandleAsync(object? payload)
    {
        GisParametersApplyRequest request = (payload as JObject)?.ToObject<GisParametersApplyRequest>() ?? new GisParametersApplyRequest();
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireEditable(doc);
            (IReadOnlyList<ParameterTableChange> changes, IReadOnlyList<string> skipped) = ParameterTable.Accept(
                ModelSnapshotReader.ParameterRows(doc),
                request.Changes.Select(item => new ParameterTableChange(item.ElementId, item.Kind, item.Column, item.OldValue, item.NewValue)),
                GisToolStandard.LoadEmbedded());
            if (changes.Count == 0)
            {
                return (object?)new GisStandardApplyResponse { Skipped = skipped.ToArray() };
            }

            (IReadOnlyList<string> log, IReadOnlyList<string> missing) = GisStandardWriter.ApplyParameters(doc, changes);
            return new GisStandardApplyResponse { Log = log.ToArray(), Skipped = skipped.Concat(missing).ToArray() };
        });
    }
}
