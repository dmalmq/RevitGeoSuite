using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Preview;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.FloorPlanExport.UI;

internal static class WebPreviewPayloadMapper
{
    private const int MaxRingPoints = 240;
    private const int MaxLinePoints = 320;
    private const string UnspecifiedCategory = "unspecified";

    public static PreviewInitialStateResponse BuildInitialState(
        ExportPreviewController controller,
        IReadOnlyList<ViewPlan> views,
        PreviewViewPayload? currentView = null,
        int readinessWarningCount = 0,
        int readinessUnassignedFloorTypeCount = 0,
        PreviewAssignmentSummaryPayload? assignmentSummary = null)
    {
        if (controller is null)
        {
            throw new ArgumentNullException(nameof(controller));
        }

        return new PreviewInitialStateResponse
        {
            Language = controller.Language == UiLanguage.Japanese ? "japanese" : "english",
            CoordinateSummary = controller.BuildCoordinateSummaryText(),
            Views = (views ?? Array.Empty<ViewPlan>()).Select(view => new PreviewViewOption
            {
                Id = view.Id.Value,
                Name = view.Name,
                DisplayName = controller.BuildViewDisplayText(view),
            }).ToList(),
            SupportedCategories = controller.SupportedFloorCategories.ToList(),
            AssignmentSummary = assignmentSummary ?? BuildAssignmentSummary(controller, Array.Empty<PreviewViewData>()),
            CurrentView = currentView,
            ReadinessWarningCount = readinessWarningCount,
            ReadinessUnassignedFloorTypeCount = readinessUnassignedFloorTypeCount,
            ReadinessIssueCount = readinessWarningCount + readinessUnassignedFloorTypeCount,
        };
    }

    public static PreviewViewPayload ToPayload(ExportPreviewController controller, PreviewDisplayViewState displayState)
    {
        if (controller is null)
        {
            throw new ArgumentNullException(nameof(controller));
        }

        if (displayState is null)
        {
            throw new ArgumentNullException(nameof(displayState));
        }

        PreviewViewData viewData = displayState.SourceViewData;
        PreviewAssignmentState assignmentState = controller.GetAssignmentState();
        return new PreviewViewPayload
        {
            ViewId = viewData.ViewId,
            ViewName = viewData.ViewName,
            LevelName = viewData.LevelName,
            QuickSummary = controller.BuildQuickSummaryText(),
            Instruction = controller.BuildViewInstructionText(),
            Bounds = ToBounds(displayState.DisplayBounds),
            Features = displayState.DisplayFeatures.Select(ToFeaturePayload).ToList(),
            Legend = controller.GetLegendEntries().Select(entry => new PreviewLegendPayload
            {
                Label = entry.Label,
                Count = entry.Count,
                FillColor = NormalizeColor(entry.FillColorHex),
            }).ToList(),
            Warnings = controller.GetWarnings().ToList(),
            UnassignedFloors = controller.GetUnassignedFloors().Select(group => new PreviewUnassignedFloorPayload
            {
                FloorTypeName = group.FloorTypeName,
                ParsedCandidate = group.ParsedZoneCandidate ?? string.Empty,
                UnitCount = group.UnitCount,
            }).ToList(),
            AssignmentSourceLabel = controller.AssignmentSourceLabel,
            AssignmentPendingMessage = assignmentState.PendingMessage,
            HasPendingAssignments = assignmentState.HasPendingChanges,
        };
    }

    public static IReadOnlyList<string> CleanFloorTypeNames(IEnumerable<string>? names)
    {
        return (names ?? Enumerable.Empty<string>())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    public static PreviewAssignmentSummaryPayload BuildAssignmentSummary(
        ExportPreviewController controller,
        IEnumerable<PreviewViewData>? viewData)
    {
        if (controller is null)
        {
            throw new ArgumentNullException(nameof(controller));
        }

        Dictionary<string, AssignmentAccumulator> assignments = new(StringComparer.Ordinal);
        foreach (PreviewViewData view in viewData ?? Enumerable.Empty<PreviewViewData>())
        {
            string viewLabel = BuildViewLabel(view);
            foreach (PreviewFeatureData feature in view.Features)
            {
                string? rawFloorTypeName = feature.FloorTypeName;
                if (feature.FeatureType != ExportFeatureType.Unit ||
                    string.IsNullOrWhiteSpace(rawFloorTypeName))
                {
                    continue;
                }

                string floorTypeName = rawFloorTypeName!.Trim();
                if (!assignments.TryGetValue(floorTypeName, out AssignmentAccumulator? accumulator))
                {
                    accumulator = new AssignmentAccumulator(floorTypeName);
                    assignments[floorTypeName] = accumulator;
                }

                accumulator.Add(feature, viewLabel);
            }
        }

        List<PreviewCategoryAssignmentRowPayload> rows = assignments.Values
            .OrderBy(row => row.FloorTypeName, StringComparer.OrdinalIgnoreCase)
            .Select(row => row.ToPayload())
            .ToList();

        PreviewAssignmentState assignmentState = controller.GetAssignmentState();
        return new PreviewAssignmentSummaryPayload
        {
            SourceLabel = controller.AssignmentSourceLabel,
            PendingMessage = assignmentState.PendingMessage,
            HasPendingAssignments = assignmentState.HasPendingChanges,
            FloorTypeCount = rows.Count,
            AssignedFloorTypeCount = rows.Count(row => !row.IsUnassigned),
            UnassignedFloorTypeCount = rows.Count(row => row.IsUnassigned),
            Rows = rows,
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

    private static string NormalizeCategory(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return UnspecifiedCategory;
        }

        return value!.Trim();
    }

    private static string BuildViewLabel(PreviewViewData view)
    {
        string viewName = view.ViewName ?? string.Empty;
        string levelName = view.LevelName ?? string.Empty;
        return string.IsNullOrWhiteSpace(levelName)
            ? viewName
            : $"{viewName} [{levelName}]";
    }

    private static string BuildFeatureLabel(PreviewFeatureData feature)
    {
        string? name = feature.Name;
        if (!string.IsNullOrWhiteSpace(name))
        {
            return name!.Trim();
        }

        string? exportId = feature.ExportId;
        if (!string.IsNullOrWhiteSpace(exportId))
        {
            return exportId!.Trim();
        }

        string? sourceLabel = feature.SourceLabel;
        return string.IsNullOrWhiteSpace(sourceLabel) ? string.Empty : sourceLabel!.Trim();
    }

    private sealed class AssignmentAccumulator
    {
        private readonly HashSet<string> _viewNameSet = new(StringComparer.Ordinal);
        private readonly HashSet<string> _sampleUnitSet = new(StringComparer.Ordinal);
        private string _category = UnspecifiedCategory;
        private string _parsedCandidate = string.Empty;
        private bool _usesOverride;
        private bool _isUnassigned;

        public AssignmentAccumulator(string floorTypeName)
        {
            FloorTypeName = floorTypeName;
        }

        public string FloorTypeName { get; }

        public int UnitCount { get; private set; }

        public List<string> ViewNames { get; } = new();

        public List<string> SampleUnits { get; } = new();

        public void Add(PreviewFeatureData feature, string viewLabel)
        {
            UnitCount++;
            string category = NormalizeCategory(feature.Category);
            if (feature.UsesFloorCategoryOverride || _category == UnspecifiedCategory)
            {
                _category = category;
            }

            string? parsedCandidate = feature.ParsedZoneCandidate;
            if (string.IsNullOrWhiteSpace(_parsedCandidate) &&
                !string.IsNullOrWhiteSpace(parsedCandidate))
            {
                _parsedCandidate = parsedCandidate!.Trim();
            }

            _usesOverride |= feature.UsesFloorCategoryOverride;
            _isUnassigned |= feature.IsUnassignedFloor;

            if (!string.IsNullOrWhiteSpace(viewLabel) && _viewNameSet.Add(viewLabel) && ViewNames.Count < 6)
            {
                ViewNames.Add(viewLabel);
            }

            string sampleUnit = BuildFeatureLabel(feature);
            if (!string.IsNullOrWhiteSpace(sampleUnit) && _sampleUnitSet.Add(sampleUnit) && SampleUnits.Count < 5)
            {
                SampleUnits.Add(sampleUnit);
            }
        }

        public PreviewCategoryAssignmentRowPayload ToPayload()
        {
            return new PreviewCategoryAssignmentRowPayload
            {
                FloorTypeName = FloorTypeName,
                Category = _category,
                ParsedCandidate = _parsedCandidate,
                UnitCount = UnitCount,
                ViewCount = _viewNameSet.Count,
                ViewNames = ViewNames.ToList(),
                SampleUnits = SampleUnits.ToList(),
                UsesOverride = _usesOverride,
                IsUnassigned = _isUnassigned,
                Status = _usesOverride ? "Override" : _isUnassigned ? "Unassigned" : "Catalog",
            };
        }
    }
}
