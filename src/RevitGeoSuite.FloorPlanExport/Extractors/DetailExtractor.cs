using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NetTopologySuite.Precision;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Utilities;
using RevitGeoSuite.FloorPlanExport.Export;

namespace RevitGeoSuite.FloorPlanExport.Extractors;

public sealed class DetailExtractor
{
    private static readonly GeometryFactory GeometryFactory = new();

    private readonly Document _document;
    private readonly SharedCoordinateProjector _sharedCoordinateProjector;
    private readonly GeometryRepairOptions _geometryRepairOptions;
    private readonly ExportSourceDescriptor _sourceDescriptor;
    private readonly string _sourceDocumentKey;
    private readonly string _sourceDocumentName;
    private readonly string _modelKey;
    private readonly SchemaProfile _schemaProfile;
    private readonly StairVisibilityResolver _stairVisibilityResolver;

    internal void SetCurrentGeometryView(View3D? geometryView)
    {
        _stairVisibilityResolver.CurrentGeometryView = geometryView;
    }

    public DetailExtractor(
        Document document,
        GeometryRepairOptions? geometryRepairOptions = null,
        ExportSourceDescriptor? sourceDescriptor = null,
        SchemaProfile? schemaProfile = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _sourceDescriptor = sourceDescriptor ?? ExportSourceDescriptor.CreateHost(_document);
        _sharedCoordinateProjector = new SharedCoordinateProjector(_sourceDescriptor.ProjectionProjectLocation);
        _geometryRepairOptions = (geometryRepairOptions ?? new GeometryRepairOptions()).GetEffectiveOptions();
        _sourceDocumentKey = DocumentProjectKeyBuilder.Create(_document);
        _sourceDocumentName = DocumentProjectKeyBuilder.CreateDisplayName(_document);
        _modelKey = RevitModelKeyResolver.Resolve(_document, _sourceDescriptor);
        _schemaProfile = schemaProfile?.Clone() ?? SchemaProfile.CreateCoreProfile();
        _stairVisibilityResolver = new StairVisibilityResolver(
            _document,
            ProjectPoint,
            point => _sourceDescriptor.TransformToHost.OfPoint(point));
    }

    /// <summary>The stair drawing rule's numbers this extractor draws with.</summary>
    internal StairDrawingSettings StairDrawingSettings
    {
        set => _stairVisibilityResolver.DrawingSettings = value ?? StairDrawingSettings.Default;
    }

    internal IReadOnlyList<ExportLineString> ExtractForLevel(
        Level level,
        string levelId,
        IReadOnlyList<CurveElement> detailCurves,
        IReadOnlyList<Stairs> stairs,
        GeometryRepairResult geometryRepair,
        ICollection<string> warnings,
        ViewPlan? view = null,
        string? viewName = null,
        IReadOnlyDictionary<long, VerticalCirculationVisibilityResult>? stairVisibilityResults = null,
        bool skipLevelFilter = false)
    {
        if (level is null)
        {
            throw new ArgumentNullException(nameof(level));
        }

        if (string.IsNullOrWhiteSpace(levelId))
        {
            throw new ArgumentException("Level id is required.", nameof(levelId));
        }

        if (detailCurves is null)
        {
            throw new ArgumentNullException(nameof(detailCurves));
        }

        if (stairs is null)
        {
            throw new ArgumentNullException(nameof(stairs));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        if (geometryRepair is null)
        {
            throw new ArgumentNullException(nameof(geometryRepair));
        }

        HashSet<ElementId> invisibleStairCurveIds = CollectInvisibleStairCurveIds(stairs, stairVisibilityResults);

        List<ExportLineString> features = new();
        foreach (CurveElement curveElement in detailCurves)
        {
            if (curveElement is not ModelCurve || RevitShowcaseElementFilter.IsShowcaseElement(curveElement))
            {
                continue;
            }

            if (invisibleStairCurveIds.Contains(curveElement.Id))
            {
                continue;
            }

            if (!skipLevelFilter && !IsOnLevel(curveElement, level))
            {
                continue;
            }

            if (!TryExtractLineString(curveElement, out LineString2D lineString))
            {
                warnings.Add($"Detail line {curveElement.Id.Value} geometry could not be extracted.");
                continue;
            }

            features.Add(
                CreateFeature(
                    curveElement,
                    lineString,
                    BuildSyntheticId("detail", curveElement.Id.Value, levelId),
                    levelId,
                    "detail-curve",
                    RevitKey.Create(_modelKey, curveElement.UniqueId),
                    DetailCategory.ForLineStyle(curveElement.LineStyle?.Name),
                    parentKey: null,
                    viewName,
                    warnings));
        }

        features.AddRange(ExtractStairStepLines(stairs, levelId, geometryRepair, warnings, view, viewName, stairVisibilityResults));
        return features;
    }

    private HashSet<ElementId> CollectInvisibleStairCurveIds(
        IReadOnlyList<Stairs> stairs,
        IReadOnlyDictionary<long, VerticalCirculationVisibilityResult>? stairVisibilityResults)
    {
        HashSet<ElementId> invisibleStairCurveIds = new();
        if (stairVisibilityResults == null || stairs.Count == 0)
        {
            return invisibleStairCurveIds;
        }

        foreach (Stairs stair in stairs)
        {
            bool isInvisible = !stairVisibilityResults.TryGetValue(stair.Id.Value, out VerticalCirculationVisibilityResult? result) ||
                               result == null ||
                               result.VisiblePolygons.Count == 0;
            if (!isInvisible)
            {
                continue;
            }

            ICollection<ElementId> dependentIds;
            try
            {
                dependentIds = stair.GetDependentElements(null);
            }
            catch (Exception)
            {
                continue;
            }

            foreach (ElementId dependentId in dependentIds)
            {
                if (_document.GetElement(dependentId) is CurveElement)
                {
                    invisibleStairCurveIds.Add(dependentId);
                }
            }
        }

        return invisibleStairCurveIds;
    }

    private bool TryExtractLineString(CurveElement curveElement, out LineString2D lineString)
    {
        lineString = null!;
        Curve? curve = curveElement.GeometryCurve;
        if (curve == null)
        {
            return false;
        }

        IList<XYZ> tessellated = curve.Tessellate();
        if (tessellated.Count < 2)
        {
            tessellated = new[]
            {
                curve.GetEndPoint(0),
                curve.GetEndPoint(1),
            };
        }

        List<Point2D> points = new(tessellated.Count);
        for (int i = 0; i < tessellated.Count; i++)
        {
            Point2D point = ProjectPoint(tessellated[i]);

            if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], point))
            {
                points.Add(point);
            }
        }

        if (points.Count < 2)
        {
            return false;
        }

        lineString = new LineString2D(points);
        return true;
    }

    private bool IsOnLevel(CurveElement curveElement, Level level)
    {
        if (curveElement.OwnerViewId != ElementId.InvalidElementId)
        {
            if (_document.GetElement(curveElement.OwnerViewId) is ViewPlan plan && plan.GenLevel != null)
            {
                return plan.GenLevel.Id == level.Id;
            }

            return false;
        }

        BoundingBoxXYZ? box = curveElement.get_BoundingBox(null);
        if (box != null)
        {
            const double toleranceFeet = 1.0d;
            return level.ProjectElevation >= box.Min.Z - toleranceFeet &&
                   level.ProjectElevation <= box.Max.Z + toleranceFeet;
        }

        Curve? curve = curveElement.GeometryCurve;
        if (curve == null)
        {
            return false;
        }

        XYZ p0 = curve.GetEndPoint(0);
        XYZ p1 = curve.GetEndPoint(1);
        double averageZ = (p0.Z + p1.Z) * 0.5d;
        return Math.Abs(averageZ - level.ProjectElevation) <= 1.0d;
    }

    private IReadOnlyList<ExportLineString> ExtractStairStepLines(
        IReadOnlyList<Stairs> stairs,
        string levelId,
        GeometryRepairResult geometryRepair,
        ICollection<string> warnings,
        ViewPlan? view,
        string? viewName,
        IReadOnlyDictionary<long, VerticalCirculationVisibilityResult>? stairVisibilityResults)
    {
        List<ExportLineString> features = new();
        foreach (Stairs stair in stairs)
        {
            VerticalCirculationVisibilityResult? stairVisibility = null;
            Geometry? visibleFootprintMask = null;
            if (stairVisibilityResults != null)
            {
                if (!stairVisibilityResults.TryGetValue(stair.Id.Value, out stairVisibility))
                {
                    continue;
                }

                visibleFootprintMask = CreatePolygonMask(stairVisibility.VisiblePolygons);
                if (visibleFootprintMask == null || visibleFootprintMask.IsEmpty)
                {
                    continue;
                }
            }
            else if (view != null &&
                     DocumentComparer.IsSameDocument(view.Document, _document) &&
                     _stairVisibilityResolver.TryResolveVisibleStair(
                         stair,
                         view,
                         warnings,
                         out stairVisibility) &&
                     stairVisibility.VisiblePolygons.Count > 0)
            {
                visibleFootprintMask = CreatePolygonMask(stairVisibility.VisiblePolygons);
            }

            IReadOnlyDictionary<string, object?>? stairVisibilityAttributes = stairVisibility?.BuildDebugAttributes();
            string? stairKey = RevitKey.Create(_modelKey, stair.UniqueId);

            // A linked stair has no visibility result here, but its unit follows the drawing rule
            // on the host view's level, so its step lines must too.
            bool linkedUnderHostView = stairVisibility == null &&
                                       view != null &&
                                       !DocumentComparer.IsSameDocument(view.Document, _document);
            bool ruleDrawsFloor = stairVisibility?.SourceKind == VerticalCirculationVisibilitySourceKind.DrawingRule ||
                                  linkedUnderHostView;
            foreach (IGrouping<StairPathPiece, StairStepLine> runLines in BuildStepLines(stair, view, ruleDrawsFloor)
                         .GroupBy(line => line.Run))
            {
                if (runLines.Key.Key == null || _document.GetElement(runLines.Key.Key) is not StairsRun run)
                {
                    continue;
                }

                if (!TryClipRunStepLines(
                        runLines.Select(line => line.Line).ToList(),
                        visibleFootprintMask,
                        geometryRepair,
                        out List<LineString2D> clipped))
                {
                    continue;
                }

                for (int i = 0; i < clipped.Count; i++)
                {
                    long syntheticElementId = (run.Id.Value * 1000L) + (i + 1);
                    features.Add(
                        CreateFeature(
                            run,
                            clipped[i],
                            BuildSyntheticId("detail.stair.step", syntheticElementId, levelId),
                            levelId,
                            "stair-step",
                            RevitKey.Create(_modelKey, run.UniqueId, i),
                            DetailCategory.Steps,
                            stairKey,
                            viewName,
                            warnings,
                            stairVisibilityAttributes));
                }
            }
        }

        return features;
    }

    /// <summary>
    /// Step lines by the stair drawing rule: every 0.60 m (the model's step spacing) on one grid
    /// for the whole stair, wall to wall, none on landings. On a floor the rule draws, they stop
    /// short of the middle; anywhere else the stair gets every line of the grid and the view's
    /// visible part of the stair clips them. A stair the rule cannot read as one walking path,
    /// such as one with runs side by side, gets a grid on each run, starting at that run's own
    /// start, so its runs do not share one grid. Revit's own treads are never
    /// drawn: the reference GDBs draw steps at the standard spacing, not tread by tread.
    /// </summary>
    private IReadOnlyList<StairStepLine> BuildStepLines(Stairs stair, ViewPlan? view, bool ruleDrawsFloor)
    {
        StairDrawingSettings settings = _stairVisibilityResolver.DrawingSettings ?? StairDrawingSettings.Default;
        if (StairDrawingPieces.TryRead(stair, ProjectPoint, out IReadOnlyList<StairPathPiece> pieces))
        {
            StairDrawingRule rule = new(pieces);
            StairFloorSide? side = ruleDrawsFloor ? _stairVisibilityResolver.ResolveDrawingRuleSide(stair, view) : null;
            return side == null ? rule.BuildAllStepLines(settings) : rule.BuildStepLines(side.Value, settings);
        }

        List<StairStepLine> lines = new();
        foreach (ElementId runId in stair.GetStairsRuns())
        {
            if (_document.GetElement(runId) is StairsRun run &&
                StairDrawingPieces.TryReadRun(run, ProjectPoint, out StairPathPiece piece))
            {
                lines.AddRange(new StairDrawingRule(new[] { piece }).BuildAllStepLines(settings));
            }
        }

        return lines;
    }

    private bool TryClipRunStepLines(
        IReadOnlyList<LineString2D> sourceLines,
        Geometry? visibleFootprintMask,
        GeometryRepairResult geometryRepair,
        out List<LineString2D> clippedLines)
    {
        clippedLines = new List<LineString2D>(sourceLines);
        if (visibleFootprintMask == null || sourceLines.Count == 0)
        {
            return clippedLines.Count > 0;
        }

        List<LineString2D> visibleLines = new();
        HashSet<string> emittedLineKeys = new(StringComparer.Ordinal);
        for (int i = 0; i < sourceLines.Count; i++)
        {
            if (!TryClipLineToPolygonMask(sourceLines[i], visibleFootprintMask, out List<LineString2D> segments))
            {
                continue;
            }

            for (int j = 0; j < segments.Count; j++)
            {
                LineString2D segment = segments[j];
                if (GetLength(segment.Points) < _geometryRepairOptions.MinimumOpeningLengthMeters)
                {
                    geometryRepair.SimplifiedDetails++;
                    continue;
                }

                if (emittedLineKeys.Add(BuildLineKey(segment)))
                {
                    visibleLines.Add(segment);
                }
            }
        }

        clippedLines = visibleLines;
        return clippedLines.Count > 0;
    }

    private Point2D ProjectPoint(XYZ point)
    {
        XYZ hostPoint = _sourceDescriptor.TransformToHost.OfPoint(point);
        return _sharedCoordinateProjector.ProjectPoint(hostPoint);
    }

    private ExportLineString CreateFeature(
        Element sourceElement,
        LineString2D lineString,
        string id,
        string levelId,
        string sourceLabel,
        string? revitKey,
        string category,
        string? parentKey,
        string? viewName,
        ICollection<string> warnings,
        IReadOnlyDictionary<string, object?>? additionalAttributes = null)
    {
        Dictionary<string, object?> attributes = new()
        {
            ["id"] = id,
            [RevitKey.AttributeName] = revitKey,
            [DetailCategory.AttributeName] = category,
            [RevitKey.ParentAttributeName] = parentKey,
            ["level_id"] = levelId,
            ["element_id"] = sourceElement.Id.Value,
            ["source_label"] = sourceLabel,
            ["source_document_key"] = _sourceDocumentKey,
            ["source_document_name"] = _sourceDocumentName,
            ["has_persisted_export_id"] = false,
            ["is_linked_source"] = _sourceDescriptor.IsLinkedSource,
            ["source_link_instance_id"] = _sourceDescriptor.LinkInstanceId,
            ["source_link_instance_name"] = _sourceDescriptor.LinkInstanceName,
        };
        SchemaAttributeMapper.ApplyMappings(
            _schemaProfile,
            SchemaLayerType.Detail,
            attributes,
            sourceElement,
            viewName,
            warnings);

        if (additionalAttributes != null)
        {
            foreach (KeyValuePair<string, object?> kvp in additionalAttributes)
            {
                attributes[kvp.Key] = kvp.Value;
            }
        }

        return new ExportLineString(lineString, attributes);
    }

    private string BuildSyntheticId(string scope, long elementId, string levelId)
    {
        if (!_sourceDescriptor.IsLinkedSource || !_sourceDescriptor.LinkInstanceId.HasValue)
        {
            return StableIdGenerator.Create(scope, elementId, levelId);
        }

        return DeterministicIdGenerator.CreateGuid(
            scope,
            _sourceDocumentKey,
            _sourceDescriptor.LinkInstanceId.Value.ToString(),
            elementId.ToString(),
            levelId);
    }

    private static Geometry? CreatePolygonMask(IReadOnlyList<Polygon2D> polygons)
    {
        List<Geometry> geometries = new();
        for (int i = 0; i < polygons.Count; i++)
        {
            Geometry? geometry = ToNtsPolygon(polygons[i]);
            if (geometry != null && !geometry.IsEmpty)
            {
                geometries.Add(geometry);
            }
        }

        if (geometries.Count == 0)
        {
            return null;
        }

        Geometry unioned = UnaryUnionOp.Union(geometries);
        return unioned.IsEmpty ? null : unioned.Buffer(0d);
    }

    private static Geometry SafeDifference(Geometry source, Geometry mask)
    {
        try
        {
            return source.Difference(mask).Buffer(0d);
        }
        catch (TopologyException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                Geometry reducedSource = reducer.Reduce(source);
                Geometry reducedMask = reducer.Reduce(mask);
                return reducedSource.Difference(reducedMask).Buffer(0d);
            }
            catch (TopologyException)
            {
                return source;
            }
        }
    }

    private static Geometry? ToNtsPolygon(Polygon2D polygon)
    {
        if (!TryCreateLinearRing(polygon.ExteriorRing, out LinearRing? shell))
        {
            return null;
        }

        List<LinearRing> holes = new();
        for (int i = 0; i < polygon.InteriorRings.Count; i++)
        {
            if (TryCreateLinearRing(polygon.InteriorRings[i], out LinearRing? hole) && hole != null)
            {
                holes.Add(hole);
            }
        }

        Polygon created = GeometryFactory.CreatePolygon(shell, holes.ToArray());
        return created.IsValid ? created : created.Buffer(0d);
    }

    private static bool TryCreateLinearRing(IReadOnlyList<Point2D> ringPoints, out LinearRing? ring)
    {
        ring = null;
        if (ringPoints == null || ringPoints.Count < 4)
        {
            return false;
        }

        List<Coordinate> coordinates = new(ringPoints.Count + 1);
        for (int i = 0; i < ringPoints.Count; i++)
        {
            coordinates.Add(new Coordinate(ringPoints[i].X, ringPoints[i].Y));
        }

        Coordinate first = coordinates[0];
        Coordinate last = coordinates[coordinates.Count - 1];
        if (!first.Equals2D(last))
        {
            coordinates.Add(new Coordinate(first.X, first.Y));
        }

        if (coordinates.Count < 4)
        {
            return false;
        }

        ring = GeometryFactory.CreateLinearRing(coordinates.ToArray());
        return !ring.IsEmpty;
    }

    private static bool TryClipLineToPolygonMask(
        LineString2D line,
        Geometry visibleFootprintMask,
        out List<LineString2D> clippedSegments)
    {
        clippedSegments = new List<LineString2D>();
        if (line == null || line.Points.Count < 2)
        {
            return false;
        }

        LineString lineString = GeometryFactory.CreateLineString(
            line.Points.Select(point => new Coordinate(point.X, point.Y)).ToArray());

        Geometry intersection;
        try
        {
            intersection = visibleFootprintMask.Intersection(lineString);
        }
        catch (TopologyException)
        {
            try
            {
                GeometryPrecisionReducer reducer = new(new PrecisionModel(100_000d));
                Geometry reducedMask = reducer.Reduce(visibleFootprintMask);
                Geometry reducedLine = reducer.Reduce(lineString);
                intersection = reducedMask.Intersection(reducedLine);
            }
            catch (TopologyException)
            {
                return false;
            }
        }

        if (intersection.IsEmpty)
        {
            return false;
        }

        List<LineString> lineStrings = new();
        CollectLineStrings(intersection, lineStrings);
        for (int i = 0; i < lineStrings.Count; i++)
        {
            List<Point2D> points = lineStrings[i].Coordinates
                .Select(coord => new Point2D(coord.X, coord.Y))
                .ToList();
            if (points.Count >= 2)
            {
                clippedSegments.Add(new LineString2D(points));
            }
        }

        return clippedSegments.Count > 0;
    }

    private static void CollectLineStrings(Geometry geometry, ICollection<LineString> target)
    {
        switch (geometry)
        {
            case LineString lineString:
                target.Add(lineString);
                break;
            case MultiLineString multiLineString:
                for (int i = 0; i < multiLineString.NumGeometries; i++)
                {
                    if (multiLineString.GetGeometryN(i) is LineString childLine)
                    {
                        target.Add(childLine);
                    }
                }

                break;
            case GeometryCollection collection:
                for (int i = 0; i < collection.NumGeometries; i++)
                {
                    CollectLineStrings(collection.GetGeometryN(i), target);
                }

                break;
        }
    }

    private static string BuildLineKey(LineString2D line)
    {
        Point2D start = line.Points[0];
        Point2D end = line.Points[line.Points.Count - 1];
        return ComparePoints(start, end) <= 0
            ? BuildLineKey(start, end)
            : BuildLineKey(end, start);
    }

    private static string BuildLineKey(Point2D start, Point2D end)
    {
        return $"{Math.Round(start.X, 4)}:{Math.Round(start.Y, 4)}:{Math.Round(end.X, 4)}:{Math.Round(end.Y, 4)}";
    }

    private static int ComparePoints(Point2D left, Point2D right)
    {
        int xComparison = left.X.CompareTo(right.X);
        return xComparison != 0 ? xComparison : left.Y.CompareTo(right.Y);
    }

    private static double GetLength(IReadOnlyList<Point2D> points)
    {
        double total = 0d;
        for (int i = 0; i < points.Count - 1; i++)
        {
            double dx = points[i + 1].X - points[i].X;
            double dy = points[i + 1].Y - points[i].Y;
            total += Math.Sqrt((dx * dx) + (dy * dy));
        }

        return total;
    }

    private static bool IsSamePoint(Point2D left, Point2D right)
    {
        return Math.Abs(left.X - right.X) <= 1e-8d &&
               Math.Abs(left.Y - right.Y) <= 1e-8d;
    }
}

