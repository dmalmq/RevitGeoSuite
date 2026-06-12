using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.FloorPlanExport.Commands;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.UI;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

internal sealed class FloorPlanExportSessionManager
{
    private readonly WebRpcBridge bridge;
    private FloorPlanExportSession? session;
    private Document? sessionDocument;
    private string? sessionProjectKey;

    public FloorPlanExportSessionManager(WebRpcBridge bridge)
    {
        this.bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
    }

    public void RegisterHandlers(WebRpcBridge target)
    {
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.getInitialState", this, s => s.BuildInitialState()));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.preparePreview", this, (s, p) => s.PreparePreview(Read<ExporterSettingsPayload>(p))));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.run", this, (s, p) => s.RunExport(Read<ExporterSettingsPayload>(p))));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.saveProfile", this, (s, p) => s.SaveProfile(Read<ExporterSaveProfileRequest>(p))));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.deleteProfile", this, (s, p) => s.DeleteProfile(Read<ExporterDeleteProfileRequest>(p))));
        target.RegisterHandler(new FloorPlanCancelHandler(this));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.preview.getInitialState", this, s => s.GetPreviewInitialState()));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.preview.loadView", this, (s, p) => s.LoadPreviewView(Read<PreviewLoadViewRequest>(p).ViewId)));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.preview.assignCategory", this, (s, p) => s.AssignPreviewCategory(Read<PreviewAssignmentRequest>(p))));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.preview.clearAssignment", this, (s, p) => s.ClearPreviewAssignment(Read<PreviewClearAssignmentRequest>(p))));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.preview.saveAssignments", this, s => s.SavePreviewAssignments()));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.preview.discardAssignments", this, s => s.DiscardPreviewAssignments()));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.execution.progress.getInitialState", this, s => s.BuildProgressInitialState()));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.execution.result.getInitialState", this, s => s.BuildResultInitialState()));
        target.RegisterHandler(new FloorPlanRpcHandler("floorplan.execution.result.openOutputFolder", this, s => s.OpenLastOutputDirectory()));
    }

    private Task<object?> InvokeAsync(Func<FloorPlanExportSession, object?> action)
    {
        return RevitContext.Instance.InvokeAsync(app =>
        {
            FloorPlanExportSession currentSession = GetOrCreateSession(app);
            return action(currentSession);
        });
    }

    private Task<object?> CancelAsync()
    {
        session?.Cancel();
        return Task.FromResult<object?>(null);
    }

    private FloorPlanExportSession GetOrCreateSession(UIApplication app)
    {
        UIDocument? uiDocument = app.ActiveUIDocument;
        Document? document = uiDocument?.Document;
        if (document == null)
        {
            throw new InvalidOperationException("No active Revit document.");
        }

        string projectKey = DocumentProjectKeyBuilder.Create(document);
        if (session != null &&
            string.Equals(sessionProjectKey, projectKey, StringComparison.Ordinal))
        {
            return session;
        }

        session?.Dispose();

        ViewCollector collector = new();
        IReadOnlyList<ViewPlan> views = collector.GetExportablePlanViews(document);
        if (views.Count == 0)
        {
            throw new InvalidOperationException("No exportable plan views were found.");
        }

        SettingsBundle bundle = new(projectKey);
        SettingsBundleSnapshot snapshot = bundle.Load();
        ExportProfileStore profileStore = new();
        IReadOnlyList<string> availableFloorTypeNames = GetAvailableFloorTypeNames(document);
        IReadOnlyList<LinkSelectionItem> availableLinks = GetAvailableLinks(document);
        ModelCoordinateInfo coordinateInfo = new ModelCoordinateInfoReader().Read(document);

        ExportWorkflowCoordinator workflow = new(
            document,
            uiDocument,
            projectKey,
            availableFloorTypeNames,
            profileStore);

        session = new FloorPlanExportSession(
            bridge,
            views,
            snapshot.GlobalSettings,
            availableLinks,
            snapshot.Profiles,
            bundle.SaveGlobalSettings,
            workflow.SaveProfile,
            workflow.DeleteProfile,
            (request, progress) => workflow.RunExportCore(request, coordinateInfo, progress),
            coordinateInfo);
        sessionDocument = document;
        sessionProjectKey = projectKey;
        return session;
    }

    private static IReadOnlyList<string> GetAvailableFloorTypeNames(Document document)
    {
        return new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Select(type => type.Name?.Trim() ?? string.Empty)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static IReadOnlyList<LinkSelectionItem> GetAvailableLinks(Document document)
    {
        ViewExportContextProvider contextProvider = new(document);
        return contextProvider.GetLoadedLinkInstances()
            .Select(linkInstance =>
            {
                Document? linkedDocument = linkInstance.GetLinkDocument();
                string linkName = string.IsNullOrWhiteSpace(linkInstance.Name)
                    ? $"Link {linkInstance.Id.Value}"
                    : linkInstance.Name.Trim();
                string sourceDocumentName = linkedDocument == null
                    ? string.Empty
                    : DocumentProjectKeyBuilder.CreateDisplayName(linkedDocument);
                string displayName = string.IsNullOrWhiteSpace(sourceDocumentName) ||
                                     string.Equals(linkName, sourceDocumentName, StringComparison.OrdinalIgnoreCase)
                    ? linkName
                    : $"{linkName} [{sourceDocumentName}]";
                return new LinkSelectionItem(linkInstance.Id.Value, displayName, sourceDocumentName);
            })
            .ToList();
    }

    private static T Read<T>(object? payload) where T : new()
    {
        if (payload is JObject jObject)
        {
            return jObject.ToObject<T>() ?? new T();
        }

        if (payload == null)
        {
            return new T();
        }

        string json = JsonConvert.SerializeObject(payload);
        return JsonConvert.DeserializeObject<T>(json) ?? new T();
    }

    private sealed class FloorPlanRpcHandler : IRpcHandler
    {
        private readonly FloorPlanExportSessionManager manager;
        private readonly Func<FloorPlanExportSession, object?, object?> handle;

        public FloorPlanRpcHandler(
            string method,
            FloorPlanExportSessionManager manager,
            Func<FloorPlanExportSession, object?> handle)
            : this(method, manager, (session, _) => handle(session))
        {
        }

        public FloorPlanRpcHandler(
            string method,
            FloorPlanExportSessionManager manager,
            Func<FloorPlanExportSession, object?, object?> handle)
        {
            Method = method ?? throw new ArgumentNullException(nameof(method));
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
            this.handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        public string Method { get; }

        public Task<object?> HandleAsync(object? payload)
        {
            return manager.InvokeAsync(session => handle(session, payload));
        }
    }

    private sealed class FloorPlanCancelHandler : IRpcHandler
    {
        private readonly FloorPlanExportSessionManager manager;

        public FloorPlanCancelHandler(FloorPlanExportSessionManager manager)
        {
            this.manager = manager ?? throw new ArgumentNullException(nameof(manager));
        }

        public string Method => "floorplan.cancel";

        public Task<object?> HandleAsync(object? payload)
        {
            return manager.CancelAsync();
        }
    }
}
