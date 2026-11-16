using System;
using System.Collections.Generic;
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
            ExportViewPlan views = ExportViewPlanner.Plan(ExportViewSetup.Read(doc));
            IReadOnlyList<string> log = plan.IsEmpty && views.IsEmpty ? Array.Empty<string>() : GisStandardWriter.ApplySetup(doc, standard, plan, views);
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
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireEditable(doc);
            GisToolStandard standard = GisToolStandard.LoadEmbedded();
            ModelSnapshot snapshot = ModelSnapshotReader.Read(doc);
            string blocker = FloorTypeMatcher.WriteBlocker(snapshot);
            if (blocker.Length > 0)
            {
                throw new InvalidOperationException(blocker);
            }

            FloorTypeFix fix = FloorTypeMatcher.Accept(
                snapshot,
                standard,
                (request.Accepted ?? Array.Empty<GisAcceptedProposal>()).Select(entry => (entry.FloorTypeId, entry.StandardTypeName)),
                (request.Picked ?? Array.Empty<GisAcceptedProposal>()).Select(entry => (entry.FloorTypeId, entry.StandardTypeName)));
            IReadOnlyList<string> log = fix.Proposals.Count == 0
                ? Array.Empty<string>()
                : GisStandardWriter.ApplyProposals(doc, fix.Proposals, fix.EchoFloorIds);
            GisStandardNotifier.Instance.Refresh(doc, force: true);
            return (object?)new GisStandardApplyResponse { Log = log.ToArray(), Skipped = fix.Skipped.ToArray() };
        });
    }
}

/// <summary>Selects the given elements in Revit and zooms the active view to them.</summary>
/// <summary>
/// A finding's Fix: reads the model again, finds the fix of that id in a fresh audit (refused when
/// its finding has gone), and applies it in one transaction, with the values the user edited.
/// </summary>
public sealed class GisApplyFixHandler : IRpcHandler
{
    public string Method => "gisStandard.applyFix";

    public Task<object?> HandleAsync(object? payload)
    {
        GisApplyFixRequest request = (payload as JObject)?.ToObject<GisApplyFixRequest>() ?? new GisApplyFixRequest();
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireEditable(doc);
            GisToolStandard standard = GisToolStandard.LoadEmbedded();
            ExportViewSnapshot views = ExportViewSetup.Read(doc);
            GisFix? fix = GisStandardWork.Findings(doc, ModelSnapshotReader.Read(doc), standard, views)
                .Select(finding => finding.Fix)
                .FirstOrDefault(candidate => candidate != null && candidate.Id == request.FixId);
            if (fix == null)
            {
                return (object?)new GisStandardApplyResponse { Skipped = new[] { "Nothing was changed: the model no longer has this finding. Check again." } };
            }

            switch (fix.Kind)
            {
                case GisFixKind.Write:
                    Dictionary<(long, string), string> edits = new();
                    foreach (GisFixWriteItem value in request.Values ?? Array.Empty<GisFixWriteItem>())
                    {
                        edits[(value.ElementId, value.Parameter)] = value.NewValue ?? string.Empty;
                    }

                    (IReadOnlyList<GisFixWrite> writes, string? refused) = fix.Accept(edits);
                    if (refused != null)
                    {
                        return new GisStandardApplyResponse { Skipped = new[] { refused } };
                    }

                    (IReadOnlyList<string> log, IReadOnlyList<string> skipped) = GisStandardWriter.ApplyFix(doc, writes);
                    return new GisStandardApplyResponse { Log = log.ToArray(), Skipped = skipped.ToArray() };
                case GisFixKind.AddPlan:
                    ExportViewPlan plan = ExportViewPlanner.PlanFor(views, fix.LevelId!.Value);
                    return plan.IsEmpty
                        ? new GisStandardApplyResponse { Skipped = new[] { "Nothing was changed: the level already has a GIS_ plan." } }
                        : new GisStandardApplyResponse { Log = GisStandardWriter.AddPlan(doc, plan).ToArray() };
                default:
                    throw new InvalidOperationException("This finding is fixed by Set up model.");
            }
        });
    }
}

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
        RequireProject(document);
        GisToolStandard standard = GisToolStandard.LoadEmbedded();
        ModelSnapshot snapshot = ModelSnapshotReader.Read(document);
        GisStandardStatus status = GisStandardStateReader.Check(document);
        SetupPlan plan = SetupPlanner.Plan(snapshot, standard);
        ExportViewSnapshot viewSnapshot = ExportViewSetup.Read(document);
        ExportViewPlan views = ExportViewPlanner.Plan(viewSnapshot);
        return new GisStandardInspectResponse
        {
            DocumentTitle = document.Title ?? string.Empty,
            StandardVersion = standard.Version,
            ModelVersion = snapshot.StandardVersion,
            ExportSummary = status.Summary,
            ExportBlockers = status.Issues.Where(issue => issue.BlocksExport).Select(issue => issue.Message).ToArray(),
            Findings = Findings(document, snapshot, standard, viewSnapshot)
                .Select(finding => new GisAuditFindingItem
                {
                    Severity = finding.Severity.ToString().ToLowerInvariant(),
                    Topic = finding.Topic,
                    Message = finding.Message,
                    ElementIds = finding.ElementIds.ToArray(),
                    Fix = finding.Fix is GisFix fix ? FixItem(document, fix) : null,
                })
                .ToArray(),
            SetupLines = (plan.IsEmpty ? Array.Empty<string>() : SetupPlanner.Describe(plan)).Concat(ExportViewPlanner.Describe(views)).ToArray(),
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
                    NewName = proposal.NewName ?? string.Empty,
                    Reason = proposal.Reason switch
                    {
                        "name" => "name",
                        "old name" => "oldName",
                        _ => "words",
                    },
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
            FixBlocker = FloorTypeMatcher.WriteBlocker(snapshot),
            StandardFloorTypes = standard.FloorTypes
                .Select(floorType => new GisStandardFloorTypeItem { Name = floorType.Name, GisCategory = floorType.GisCategory, GisRestriction = floorType.GisRestriction ?? string.Empty })
                .ToArray(),
        };
    }

    /// <summary>The audit the page shows: the export's audit and the walking network's findings.</summary>
    public static IEnumerable<GisAuditFinding> Findings(Document document, ModelSnapshot snapshot, GisToolStandard standard, ExportViewSnapshot viewSnapshot) =>
        ExportAuditReader.Audit(document, snapshot, standard, viewSnapshot).Concat(GisNetworkSource.Audit(document));

    private static GisFixItem FixItem(Document document, GisFix fix) => new()
    {
        Id = fix.Id,
        Kind = fix.Kind switch
        {
            GisFixKind.SetUp => "setup",
            GisFixKind.AddPlan => "addPlan",
            _ => "write",
        },
        Writes = fix.Writes
            .Select(write => new GisFixWriteItem
            {
                ElementId = write.ElementId,
                ElementName = write.ElementName,
                Parameter = write.Parameter,
                OldValue = write.OldValue,
                NewValue = write.NewValue,
                Editable = write.Editable,
            })
            .ToArray(),
        LevelId = fix.LevelId ?? 0,
        LevelName = fix.LevelId is long levelId ? document.GetElement(new ElementId(levelId))?.Name ?? string.Empty : string.Empty,
    };

    public static void RequireProject(Document document)
    {
        if (document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Open a project model to check it against the Kanizu GIS standard.");
        }
    }

    public static void RequireEditable(Document document)
    {
        if (document.IsReadOnly || document.IsFamilyDocument)
        {
            throw new InvalidOperationException("Open an editable project model to change it.");
        }
    }
}
