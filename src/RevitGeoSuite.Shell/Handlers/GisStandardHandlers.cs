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

/// <summary>Audits the active model and previews what Set up model, Prepare Template and Fix floor types would change. Reads only.</summary>
public sealed class GisStandardInspectHandler : IRpcHandler
{
    public string Method => "gisStandard.inspect";

    public Task<object?> HandleAsync(object? payload)
    {
        GisStandardInspectRequest request = (payload as JObject)?.ToObject<GisStandardInspectRequest>() ?? new GisStandardInspectRequest();
        string stationFolder = (request.StationFolder ?? string.Empty).Trim();
        string? mode = string.IsNullOrWhiteSpace(request.Mode) ? null : request.Mode!.Trim();
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            using IDisposable? scope = stationFolder.Length > 0 ? GisRulesSourceFactory.ReadFrom(stationFolder) : null;
            return (object?)GisStandardWork.Inspect(doc, mode);
        });
    }
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

/// <summary>Prepare Template: runs Set up model plus creates/updates the 4 QA schedules and Kanizu GIS Export Plan view template in one transaction.</summary>
public sealed class GisStandardPrepareTemplateHandler : IRpcHandler
{
    public string Method => "gisStandard.prepareTemplate";

    public Task<object?> HandleAsync(object? payload) =>
        RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireEditable(doc);
            GisToolStandard standard = GisToolStandard.LoadEmbedded();
            ModelSnapshot snapshot = ModelSnapshotReader.Read(doc);
            SetupPlan plan = SetupPlanner.Plan(snapshot, standard);
            ExportViewPlan views = ExportViewPlanner.Plan(ExportViewSetup.Read(doc));
            ReadinessCheckSnapshot readiness = GisStandardStateReader.ReadReadiness(doc, snapshot);
            TemplatePreparationPlan templatePlan = TemplateStandardPlanner.Plan(
                readiness.Schedules,
                readiness.ViewTemplateNames,
                readiness.ExportPlanTemplateIssues);
            IReadOnlyList<string> log = TemplateStandardWriter.PrepareTemplate(doc, standard, plan, views, templatePlan);
            GisStandardNotifier.Instance.Refresh(doc, force: true);
            return (object?)new GisStandardApplyResponse { Log = log.ToArray() };
        });
}

/// <summary>Validates a folder of .rfa family files against standards/families.json and door opening parameter rules.</summary>
public sealed class GisStandardValidateRfaFolderHandler : IRpcHandler
{
    public string Method => "gisStandard.validateRfaFolder";

    public Task<object?> HandleAsync(object? payload)
    {
        GisRfaValidationRequest request = (payload as JObject)?.ToObject<GisRfaValidationRequest>() ?? new GisRfaValidationRequest();
        string folderPath = (request.FolderPath ?? string.Empty).Trim();
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            RfaFolderValidationResult result = RfaFamilyValidator.ValidateFolder(doc, folderPath, GisToolStandard.LoadEmbedded());
            return (object?)new GisRfaValidationResponse
            {
                FolderPath = folderPath,
                TotalFilesFound = result.TotalFilesFound,
                InspectedFileCount = result.InspectedFileCount,
                Truncated = result.Truncated,
                PassCount = result.Items.Count(item => item.Status == ReadinessCheckStatus.Pass),
                WarnCount = result.Items.Count(item => item.Status == ReadinessCheckStatus.Warn),
                FailCount = result.Items.Count(item => item.Status == ReadinessCheckStatus.Fail),
                Items = result.Items.Select(item => new GisRfaValidationItem
                {
                    FamilyName = item.FamilyName,
                    Category = item.Category,
                    Status = GisStandardWork.StatusToken(item.Status),
                    Detail = item.Detail,
                    FilePath = item.FilePath,
                }).ToArray(),
            };
        });
    }
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
                request.Accepted.Select(entry => (entry.FloorTypeId, entry.StandardTypeName)));
            IReadOnlyList<string> log = fix.Proposals.Count == 0
                ? Array.Empty<string>()
                : GisStandardWriter.ApplyProposals(doc, fix.Proposals, fix.EchoFloorIds);
            GisStandardNotifier.Instance.Refresh(doc, force: true);
            return (object?)new GisStandardApplyResponse { Log = log.ToArray(), Skipped = fix.Skipped.ToArray() };
        });
    }
}

/// <summary>
/// Resets one floor type with a standard name to its standard type (the audit's one-click repair),
/// planned again from the model as it is now, in one transaction.
/// </summary>
public sealed class GisResetFloorTypeHandler : IRpcHandler
{
    public string Method => "gisStandard.resetFloorType";

    public Task<object?> HandleAsync(object? payload)
    {
        GisResetFloorTypeRequest request = (payload as JObject)?.ToObject<GisResetFloorTypeRequest>() ?? new GisResetFloorTypeRequest();
        return RevitContext.Instance.InvokeWithDocumentAsync(doc =>
        {
            GisStandardWork.RequireEditable(doc);
            ModelSnapshot snapshot = ModelSnapshotReader.Read(doc);
            GisToolStandard standard = GisToolStandard.LoadEmbedded();
            FloorTypeSetupStep? step = SetupPlanner.ResetStep(snapshot, standard, request.FloorTypeId);
            if (step is null)
            {
                return (object?)new GisStandardApplyResponse { Skipped = new[] { SetupPlanner.NothingToReset(snapshot, standard, request.FloorTypeId) } };
            }

            IReadOnlyList<string> log = GisStandardWriter.ResetFloorType(doc, step);
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
    public static GisStandardInspectResponse Inspect(Document document, string? modeText = null)
    {
        RequireProject(document);
        GisToolStandard standard = GisToolStandard.LoadEmbedded();
        ModelSnapshot snapshot = ModelSnapshotReader.Read(document);
        GisStandardStatus status = GisStandardStateReader.Check(document);
        SetupPlan plan = SetupPlanner.Plan(snapshot, standard);
        ExportViewSnapshot viewSnapshot = ExportViewSetup.Read(document);
        ExportViewPlan views = ExportViewPlanner.Plan(viewSnapshot);
        GisReadinessSummary? readiness = null;
        try
        {
            ReadinessCheckSnapshot readinessSnapshot = GisStandardStateReader.ReadReadiness(document, snapshot);
            ProjectReadinessReport readinessReport = ProjectReadinessEvaluator.Evaluate(readinessSnapshot, ParseMode(modeText), standard);
            readiness = MapReadiness(readinessReport);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            System.Diagnostics.Trace.WriteLine($"Reading GIS readiness failed: {ex}");
        }
        return new GisStandardInspectResponse
        {
            DocumentTitle = document.Title ?? string.Empty,
            StandardVersion = standard.Version,
            ModelVersion = snapshot.StandardVersion,
            ExportSummary = status.Summary,
            ExportBlockers = status.Issues.Where(issue => issue.BlocksExport).Select(issue => issue.Message).ToArray(),
            Findings = ExportAuditReader.Audit(document, snapshot, standard, viewSnapshot)
                .Concat(GisNetworkSource.Audit(document))
                .Select(finding => new GisAuditFindingItem
                {
                    Severity = finding.Severity.ToString().ToLowerInvariant(),
                    Topic = finding.Topic,
                    Message = finding.Message,
                    ElementIds = finding.ElementIds.ToArray(),
                    Fix = finding.Fix?.Kind == GisAuditFixKind.ResetFloorType ? "resetFloorType" : string.Empty,
                    FixTargetId = finding.Fix?.TargetId ?? 0,
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
            Readiness = readiness,
        };
    }

    internal static string StatusToken(ReadinessCheckStatus status) => status switch
    {
        ReadinessCheckStatus.Pass => "pass",
        ReadinessCheckStatus.Warn => "warn",
        ReadinessCheckStatus.Fail => "fail",
        _ => "not_applicable",
    };

    private static string ActionToken(ReadinessActionKind action) => action switch
    {
        ReadinessActionKind.PrepareTemplate => "prepareTemplate",
        ReadinessActionKind.SetupModel => "setupModel",
        ReadinessActionKind.FixFloorTypes => "fixFloorTypes",
        _ => "none",
    };

    private static ReadinessMode? ParseMode(string? modeText)
    {
        if (string.Equals(modeText, "template", StringComparison.OrdinalIgnoreCase))
        {
            return ReadinessMode.Template;
        }

        if (string.Equals(modeText, "project", StringComparison.OrdinalIgnoreCase))
        {
            return ReadinessMode.Project;
        }

        return null;
    }

    private static GisReadinessSummary MapReadiness(ProjectReadinessReport report) => new()
    {
        Mode = report.Mode == ReadinessMode.Template ? "template" : "project",
        SuggestedMode = report.SuggestedMode == ReadinessMode.Template ? "template" : "project",
        ScorePercent = report.ScorePercent,
        IsReady = report.IsReady,
        ApplicableCount = report.ApplicableCount,
        PassCount = report.PassCount,
        WarnCount = report.WarnCount,
        FailCount = report.FailCount,
        NotApplicableCount = report.NotApplicableCount,
        CanPrepareTemplate = report.CanPrepareTemplate,
        CanSetupModel = report.CanSetupModel,
        CanFixFloorTypes = report.CanFixFloorTypes,
        PrepareTemplateLines = TemplateStandardPlanner.Describe(report.TemplatePlan).ToArray(),
        Groups = report.Groups.Select(group => new GisReadinessGroup
        {
            Id = group.Id,
            Title = group.Title,
            Status = StatusToken(group.Status),
            PassCount = group.PassCount,
            WarnCount = group.WarnCount,
            FailCount = group.FailCount,
            NotApplicableCount = group.NotApplicableCount,
            Items = group.Items.Select(item => new GisReadinessItem
            {
                Id = item.Id,
                Title = item.Title,
                Status = StatusToken(item.Status),
                Detail = item.Detail,
                Action = ActionToken(item.Action),
                ElementIds = item.ElementIds.ToArray(),
            }).ToArray(),
        }).ToArray(),
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
