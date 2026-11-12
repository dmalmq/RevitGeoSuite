using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.GisTools;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using RevitGeoSuite.Shell.Standards;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>Audits the active model and previews what Set up model and Fix floor types would change. Reads only.</summary>
public sealed class GisStandardInspectHandler : IRpcHandler
{
    public string Method => "gisStandard.inspect";

    public Task<object?> HandleAsync(object? payload) =>
        RevitContext.Instance.InvokeWithDocumentAsync(doc => (object?)GisStandardWork.Inspect(doc));
}

/// <summary>Set up model: plans again from the model as it is now and applies the plan in one transaction.</summary>
public sealed class GisStandardSetupHandler : IRpcHandler
{
    public string Method => "gisStandard.setup";

    public Task<object?> HandleAsync(object? payload) =>
        RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireEditable(doc);
            GisToolStandard standard = GisToolStandard.LoadEmbedded();
            SetupPlan plan = SetupPlanner.Plan(ModelSnapshotReader.Read(doc), standard);
            IReadOnlyList<string> log = plan.IsEmpty ? Array.Empty<string>() : GisStandardWriter.ApplySetup(doc, standard, plan);
            GisStandardNotifier.Instance.Refresh(doc, force: true);
            return (object?)new GisStandardApplyResponse { Log = log.ToArray() };
        });
}

/// <summary>Fix floor types: applies the accepted proposals, matched again from the model as it is now.</summary>
public sealed class GisFixFloorTypesHandler : IRpcHandler
{
    public string Method => "gisStandard.fixFloorTypes";

    public Task<object?> HandleAsync(object? payload)
    {
        GisFixFloorTypesRequest request = (payload as JObject)?.ToObject<GisFixFloorTypesRequest>() ?? new GisFixFloorTypesRequest();
        HashSet<long> accepted = new(request.FloorTypeIds);
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireEditable(doc);
            GisToolStandard standard = GisToolStandard.LoadEmbedded();
            ModelSnapshot snapshot = ModelSnapshotReader.Read(doc);
            string blocker = GisStandardWork.FixBlocker(snapshot);
            if (blocker.Length > 0)
            {
                throw new InvalidOperationException(blocker);
            }

            List<FloorTypeProposal> proposals = FloorTypeMatcher.Propose(snapshot, standard)
                .Where(proposal => accepted.Contains(proposal.FloorType.ElementId))
                .ToList();
            IReadOnlyList<string> log = proposals.Count == 0
                ? Array.Empty<string>()
                : GisStandardWriter.ApplyProposals(doc, proposals, FloorTypeMatcher.EchoFloorIds(snapshot, proposals));
            GisStandardNotifier.Instance.Refresh(doc, force: true);
            return (object?)new GisStandardApplyResponse { Log = log.ToArray() };
        });
    }
}

/// <summary>Selects the given elements in Revit and zooms the active view to them.</summary>
public sealed class GisSelectElementsHandler : IRpcHandler
{
    public string Method => "gisStandard.select";

    public Task<object?> HandleAsync(object? payload)
    {
        GisSelectElementsRequest request = (payload as JObject)?.ToObject<GisSelectElementsRequest>() ?? new GisSelectElementsRequest();
        return RevitContext.Instance.InvokeAsync(app =>
        {
            UIDocument uiDocument = app.ActiveUIDocument ?? throw new InvalidOperationException("No active Revit document.");
            List<ElementId> ids = request.ElementIds
                .Select(id => new ElementId(id))
                .Where(id => uiDocument.Document.GetElement(id) != null)
                .ToList();
            uiDocument.Selection.SetElementIds(ids);
            if (ids.Count > 0)
            {
                try
                {
                    uiDocument.ShowElements(ids);
                }
                catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // No open view can show them; they stay selected.
                }
            }

            return (object?)new GisSelectElementsResponse { Selected = ids.Count };
        });
    }
}

internal static class GisStandardWork
{
    public static GisStandardInspectResponse Inspect(Document document)
    {
        GisToolStandard standard = GisToolStandard.LoadEmbedded();
        ModelSnapshot snapshot = ModelSnapshotReader.Read(document);
        GisStandardStatus status = GisStandardStateReader.Check(document);
        SetupPlan plan = SetupPlanner.Plan(snapshot, standard);
        return new GisStandardInspectResponse
        {
            DocumentTitle = document.Title ?? string.Empty,
            StandardVersion = standard.Version,
            ModelVersion = snapshot.StandardVersion,
            ExportSummary = status.Summary,
            ExportBlockers = status.Issues.Where(issue => issue.BlocksExport).Select(issue => issue.Message).ToArray(),
            Findings = GisAudit.Run(snapshot, standard)
                .Select(finding => new GisAuditFindingItem
                {
                    Severity = finding.Severity.ToString().ToLowerInvariant(),
                    Topic = finding.Topic,
                    Message = finding.Message,
                    ElementIds = finding.ElementIds.ToArray(),
                })
                .ToArray(),
            SetupLines = plan.IsEmpty ? Array.Empty<string>() : SetupPlanner.Describe(plan).ToArray(),
            Proposals = FloorTypeMatcher.Propose(snapshot, standard)
                .Select(proposal => new GisFloorTypeProposalItem
                {
                    FloorTypeId = proposal.FloorType.ElementId,
                    FloorTypeName = proposal.FloorType.Name,
                    FloorCount = proposal.FloorType.FloorIds.Count,
                    StandardTypeName = proposal.StandardType.Name,
                    GisCategory = proposal.GisCategory,
                    GisRestriction = proposal.GisRestriction,
                    Material = proposal.Material,
                    Reason = proposal.Reason == "name" ? "name" : "words",
                })
                .ToArray(),
            Unmatched = FloorTypeMatcher.Unmatched(snapshot, standard)
                .Select(floorType => new GisUnmatchedFloorTypeItem
                {
                    FloorTypeId = floorType.ElementId,
                    Name = floorType.Name,
                    FloorIds = floorType.FloorIds.ToArray(),
                })
                .ToArray(),
            FixBlocker = FixBlocker(snapshot),
        };
    }

    /// <summary>Fix floor types writes type parameters, so both must be bound, and bound to types.</summary>
    public static string FixBlocker(ModelSnapshot snapshot)
    {
        foreach (string name in new[] { GisSharedParameters.GisCategory, GisSharedParameters.GisRestriction })
        {
            if (!snapshot.BoundParameters.TryGetValue(name, out BoundParameterInfo? bound))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} is not bound yet. Run Set up model first.", name);
            }

            if (bound.Binding != GisParameterBinding.Type)
            {
                return string.Format(CultureInfo.InvariantCulture, "{0} is bound as an instance parameter, so it cannot be written onto floor types. Rebind it as a type parameter first.", name);
            }
        }

        return string.Empty;
    }

    public static void RequireEditable(Document document)
    {
        if (document.IsReadOnly || document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Open an editable project model to change it.");
        }
    }
}
