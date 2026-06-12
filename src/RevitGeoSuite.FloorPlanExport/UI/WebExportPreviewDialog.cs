using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Preview;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using SharedLanguage = RevitGeoSuite.SharedUI.Localization.UiLanguage;
using SharedLocalizer = RevitGeoSuite.SharedUI.Localization.UiLocalizer;
using WinForms = System.Windows.Forms;

namespace RevitGeoSuite.FloorPlanExport.UI;

internal sealed class WebExportPreviewDialog : IDisposable
{
    private const int MaxRingPoints = 240;
    private const int MaxLinePoints = 320;

    private readonly ExportPreviewRequest _request;
    private readonly ExportPreviewController _controller;
    private readonly WebShellWindow _window;
    private readonly IReadOnlyList<ViewPlan> _views;
    private ViewPlan? _currentView;

    public WebExportPreviewDialog(
        ExportPreviewRequest request,
        ExportPreviewService previewService,
        WinForms.IWin32Window? owner = null)
    {
        _request = request ?? throw new ArgumentNullException(nameof(request));
        _controller = new ExportPreviewController(request, previewService ?? throw new ArgumentNullException(nameof(previewService)));
        _views = request.SelectedViews.ToList();

        SharedLocalizer.Instance.SetLanguage(ToSharedLanguage(_controller.Language));

        _window = new WebShellWindow(new WebShellOptions
        {
            InitialRoute = "/preview",
            TitleKey = UiLanguageText.Select(_controller.Language, "Export Preview", "エクスポート プレビュー"),
            OwnerHandle = owner?.Handle ?? IntPtr.Zero,
            RegisterHandlers = RegisterHandlers,
        });
    }

    public WinForms.DialogResult ShowDialog()
    {
        _ = _window.ShowDialog();
        return WinForms.DialogResult.OK;
    }

    public void Dispose()
    {
        _controller.DiscardPendingChangesOnClose();
        if (!_window.IsClosed)
        {
            _window.Close();
        }
    }

    private void RegisterHandlers(WebRpcBridge bridge, JobManager jobs)
    {
        bridge.RegisterHandler(new GetInitialStateHandler(this));
        bridge.RegisterHandler(new LoadViewHandler(this));
        bridge.RegisterHandler(new AssignCategoryHandler(this));
        bridge.RegisterHandler(new ClearAssignmentHandler(this));
        bridge.RegisterHandler(new SaveAssignmentsHandler(this));
        bridge.RegisterHandler(new DiscardAssignmentsHandler(this));
        bridge.RegisterHandler(new CloseHandler(this));
    }

    private PreviewInitialStateResponse BuildInitialState()
    {
        return new PreviewInitialStateResponse
        {
            Language = _controller.Language == UiLanguage.Japanese ? "japanese" : "english",
            CoordinateSummary = _controller.BuildCoordinateSummaryText(),
            Views = _views.Select(view => new PreviewViewOption
            {
                Id = view.Id.Value,
                Name = view.Name,
                DisplayName = _controller.BuildViewDisplayText(view),
            }).ToList(),
            SupportedCategories = _controller.SupportedFloorCategories.ToList(),
        };
    }

    private PreviewViewPayload LoadView(long viewId)
    {
        ViewPlan view = _views.FirstOrDefault(candidate => candidate.Id.Value == viewId) ??
                        _views.FirstOrDefault() ??
                        throw new InvalidOperationException("No preview views are available.");
        _currentView = view;
        PreviewDisplayViewState displayState = _controller.LoadView(view);
        return ToPayload(displayState);
    }

    private PreviewViewPayload AssignCategory(PreviewAssignmentRequest request)
    {
        IReadOnlyList<string> floorTypeNames = CleanFloorTypeNames(request.FloorTypeNames);
        if (floorTypeNames.Count > 0 && !string.IsNullOrWhiteSpace(request.Category))
        {
            _controller.StageCategoryOverride(floorTypeNames, request.Category.Trim());
        }

        return CurrentPayload();
    }

    private PreviewViewPayload ClearAssignment(PreviewClearAssignmentRequest request)
    {
        IReadOnlyList<string> floorTypeNames = CleanFloorTypeNames(request.FloorTypeNames);
        if (floorTypeNames.Count > 0)
        {
            _controller.ClearCategoryOverride(floorTypeNames);
        }

        return CurrentPayload();
    }

    private PreviewViewPayload SaveAssignments()
    {
        _controller.SavePendingAssignments();
        return CurrentPayload();
    }

    private PreviewViewPayload DiscardAssignments()
    {
        _controller.DiscardPendingAssignments();
        return CurrentPayload();
    }

    private void Close()
    {
        _window.Dispatcher.BeginInvoke(new Action(_window.Close));
    }

    private PreviewViewPayload CurrentPayload()
    {
        if (_controller.CurrentDisplayState != null)
        {
            return ToPayload(_controller.CurrentDisplayState);
        }

        if (_currentView != null)
        {
            return LoadView(_currentView.Id.Value);
        }

        ViewPlan? firstView = _views.FirstOrDefault();
        if (firstView == null)
        {
            throw new InvalidOperationException("No preview views are available.");
        }

        return LoadView(firstView.Id.Value);
    }

    private PreviewViewPayload ToPayload(PreviewDisplayViewState displayState)
    {
        PreviewViewData viewData = displayState.SourceViewData;
        PreviewAssignmentState assignmentState = _controller.GetAssignmentState();
        return new PreviewViewPayload
        {
            ViewId = viewData.ViewId,
            ViewName = viewData.ViewName,
            LevelName = viewData.LevelName,
            QuickSummary = _controller.BuildQuickSummaryText(),
            Instruction = _controller.BuildViewInstructionText(),
            Bounds = ToBounds(displayState.DisplayBounds),
            Features = displayState.DisplayFeatures.Select(ToFeaturePayload).ToList(),
            Legend = _controller.GetLegendEntries().Select(entry => new PreviewLegendPayload
            {
                Label = entry.Label,
                Count = entry.Count,
                FillColor = NormalizeColor(entry.FillColorHex),
            }).ToList(),
            Warnings = _controller.GetWarnings().ToList(),
            UnassignedFloors = _controller.GetUnassignedFloors().Select(group => new PreviewUnassignedFloorPayload
            {
                FloorTypeName = group.FloorTypeName,
                ParsedCandidate = group.ParsedZoneCandidate ?? string.Empty,
                UnitCount = group.UnitCount,
            }).ToList(),
            AssignmentSourceLabel = _controller.AssignmentSourceLabel,
            AssignmentPendingMessage = assignmentState.PendingMessage,
            HasPendingAssignments = assignmentState.HasPendingChanges,
        };
    }

    private static PreviewBoundsPayload ToBounds(Bounds2D bounds)
    {
        return new PreviewBoundsPayload
        {
            MinX = bounds.MinX,
            MinY = bounds.MinY,
            MaxX = bounds.MaxX,
            MaxY = bounds.MaxY,
            IsEmpty = bounds.IsEmpty,
        };
    }

    private static PreviewFeaturePayload ToFeaturePayload(PreviewFeatureData feature, int index)
    {
        PreviewFeaturePayload payload = new()
        {
            Index = index,
            FeatureType = feature.FeatureType.ToString(),
            Category = feature.Category ?? string.Empty,
            Name = feature.Name ?? string.Empty,
            Restriction = feature.Restriction ?? string.Empty,
            ExportId = feature.ExportId ?? string.Empty,
            SourceLabel = feature.SourceLabel ?? string.Empty,
            FillColor = NormalizeColor(feature.FillColorHex),
            StrokeColor = NormalizeColor(feature.StrokeColorHex),
            HasWarning = feature.HasWarning,
            IsUnassignedFloor = feature.IsUnassignedFloor,
            UsesFloorCategoryOverride = feature.UsesFloorCategoryOverride,
            SupportsFloorCategoryAssignment = feature.SupportsFloorCategoryAssignment,
            FloorTypeName = feature.FloorTypeName ?? string.Empty,
            ParsedZoneCandidate = feature.ParsedZoneCandidate ?? string.Empty,
            SearchText = feature.SearchText,
        };

        if (feature.Feature is ExportPolygon polygon)
        {
            payload.GeometryType = "polygon";
            foreach (Polygon2D part in polygon.Polygons)
            {
                payload.Rings.Add(ToSimplifiedRing(part.ExteriorRing));
                foreach (IReadOnlyList<Point2D> interiorRing in part.InteriorRings)
                {
                    payload.Rings.Add(ToSimplifiedRing(interiorRing));
                }
            }
        }
        else if (feature.Feature is ExportLineString line)
        {
            payload.GeometryType = "line";
            payload.Points = ToSimplifiedLine(line.LineString.Points);
        }
        else
        {
            payload.GeometryType = "unknown";
            payload.Points = ToSimplifiedLine(feature.Feature.GetAllPoints().ToList());
        }

        return payload;
    }

    private static List<PreviewPointPayload> ToPoints(IEnumerable<Point2D> points)
    {
        return points.Select(point => new PreviewPointPayload
        {
            X = point.X,
            Y = point.Y,
        }).ToList();
    }

    private static List<PreviewPointPayload> ToSimplifiedRing(IReadOnlyList<Point2D> points)
    {
        return ToPoints(DownsampleClosedRing(points, MaxRingPoints));
    }

    private static List<PreviewPointPayload> ToSimplifiedLine(IReadOnlyList<Point2D> points)
    {
        return ToPoints(DownsampleOpenLine(points, MaxLinePoints));
    }

    private static IReadOnlyList<Point2D> DownsampleClosedRing(IReadOnlyList<Point2D> points, int maxPoints)
    {
        if (points.Count <= maxPoints || maxPoints < 8)
        {
            return points;
        }

        bool isClosed = AreSamePoint(points[0], points[points.Count - 1]);
        int sourceCount = isClosed ? points.Count - 1 : points.Count;
        int targetCount = Math.Max(4, maxPoints - 1);
        List<Point2D> result = new(targetCount + 1);
        for (int i = 0; i < targetCount; i++)
        {
            int sourceIndex = (int)Math.Floor(i * (sourceCount / (double)targetCount));
            if (sourceIndex >= sourceCount)
            {
                sourceIndex = sourceCount - 1;
            }

            result.Add(points[sourceIndex]);
        }

        result.Add(result[0]);
        return result;
    }

    private static IReadOnlyList<Point2D> DownsampleOpenLine(IReadOnlyList<Point2D> points, int maxPoints)
    {
        if (points.Count <= maxPoints || maxPoints < 2)
        {
            return points;
        }

        List<Point2D> result = new(maxPoints);
        for (int i = 0; i < maxPoints; i++)
        {
            int sourceIndex = (int)Math.Round(i * ((points.Count - 1) / (double)(maxPoints - 1)));
            result.Add(points[sourceIndex]);
        }

        return result;
    }

    private static bool AreSamePoint(Point2D a, Point2D b)
    {
        return Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-9;
    }

    private static string NormalizeColor(string? color)
    {
        string value = (color ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "#525252";
        }

        return value.StartsWith("#", StringComparison.Ordinal) ? value : $"#{value}";
    }

    private static IReadOnlyList<string> CleanFloorTypeNames(IEnumerable<string>? names)
    {
        return (names ?? Enumerable.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static SharedLanguage ToSharedLanguage(UiLanguage language)
    {
        return language == UiLanguage.Japanese ? SharedLanguage.Japanese : SharedLanguage.English;
    }

    private sealed class GetInitialStateHandler : IRpcHandler
    {
        private readonly WebExportPreviewDialog _dialog;

        public GetInitialStateHandler(WebExportPreviewDialog dialog) => _dialog = dialog;

        public string Method => "preview.getInitialState";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.BuildInitialState());
    }

    private sealed class LoadViewHandler : IRpcHandler
    {
        private readonly WebExportPreviewDialog _dialog;

        public LoadViewHandler(WebExportPreviewDialog dialog) => _dialog = dialog;

        public string Method => "preview.loadView";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.LoadView(PayloadReader.Read<PreviewLoadViewRequest>(payload).ViewId));
    }

    private sealed class AssignCategoryHandler : IRpcHandler
    {
        private readonly WebExportPreviewDialog _dialog;

        public AssignCategoryHandler(WebExportPreviewDialog dialog) => _dialog = dialog;

        public string Method => "preview.assignCategory";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.AssignCategory(PayloadReader.Read<PreviewAssignmentRequest>(payload)));
    }

    private sealed class ClearAssignmentHandler : IRpcHandler
    {
        private readonly WebExportPreviewDialog _dialog;

        public ClearAssignmentHandler(WebExportPreviewDialog dialog) => _dialog = dialog;

        public string Method => "preview.clearAssignment";

        public Task<object?> HandleAsync(object? payload) =>
            Task.FromResult<object?>(_dialog.ClearAssignment(PayloadReader.Read<PreviewClearAssignmentRequest>(payload)));
    }

    private sealed class SaveAssignmentsHandler : IRpcHandler
    {
        private readonly WebExportPreviewDialog _dialog;

        public SaveAssignmentsHandler(WebExportPreviewDialog dialog) => _dialog = dialog;

        public string Method => "preview.saveAssignments";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.SaveAssignments());
    }

    private sealed class DiscardAssignmentsHandler : IRpcHandler
    {
        private readonly WebExportPreviewDialog _dialog;

        public DiscardAssignmentsHandler(WebExportPreviewDialog dialog) => _dialog = dialog;

        public string Method => "preview.discardAssignments";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.DiscardAssignments());
    }

    private sealed class CloseHandler : IRpcHandler
    {
        private readonly WebExportPreviewDialog _dialog;

        public CloseHandler(WebExportPreviewDialog dialog) => _dialog = dialog;

        public string Method => "preview.close";

        public Task<object?> HandleAsync(object? payload)
        {
            _dialog.Close();
            return Task.FromResult<object?>(null);
        }
    }

    private static class PayloadReader
    {
        public static T Read<T>(object? payload) where T : new()
        {
            if (payload is Newtonsoft.Json.Linq.JObject jobj)
            {
                return jobj.ToObject<T>() ?? new T();
            }

            if (payload == null)
            {
                return new T();
            }

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
            return Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json) ?? new T();
        }
    }
}
