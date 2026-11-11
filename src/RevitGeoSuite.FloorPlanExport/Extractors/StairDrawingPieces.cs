using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Extractors;

/// <summary>
/// Reads a Revit stair into the runs and landings the <see cref="StairDrawingRule"/> works on,
/// and decides which floor of the stair a plan view shows.
/// </summary>
internal static class StairDrawingPieces
{
    private const double LevelToleranceFeet = 0.05d;
    private const double ElevationGridFeet = 0.01d;

    /// <summary>
    /// <see cref="StairFloorSide.Lower"/> when the view is on the stair's base level,
    /// <see cref="StairFloorSide.Upper"/> on its top level, and null on any other level, where the
    /// export keeps drawing what the view shows.
    /// </summary>
    public static StairFloorSide? ResolveSide(Stairs stairs, ViewPlan? view, Func<XYZ, XYZ> toHost)
    {
        Level? viewLevel = view?.GenLevel;
        if (stairs == null || viewLevel == null)
        {
            return null;
        }

        double viewElevation = viewLevel.ProjectElevation;
        if (TryGetHostLevelElevation(stairs, BuiltInParameter.STAIRS_BASE_LEVEL_PARAM, toHost, out double baseElevation) &&
            Math.Abs(baseElevation - viewElevation) <= LevelToleranceFeet)
        {
            return StairFloorSide.Lower;
        }

        if (TryGetHostLevelElevation(stairs, BuiltInParameter.STAIRS_TOP_LEVEL_PARAM, toHost, out double topElevation) &&
            Math.Abs(topElevation - viewElevation) <= LevelToleranceFeet)
        {
            return StairFloorSide.Upper;
        }

        return null;
    }

    /// <summary>The stair's runs and landings from bottom to top, projected to export coordinates.</summary>
    public static bool TryRead(
        Stairs stairs,
        Func<XYZ, Point2D> project,
        out IReadOnlyList<StairPathPiece> pieces)
    {
        pieces = Array.Empty<StairPathPiece>();
        Document document = stairs.Document;

        List<(double Elevation, int Order, Element Element)> ordered = new();
        foreach (ElementId runId in stairs.GetStairsRuns())
        {
            if (document.GetElement(runId) is StairsRun run)
            {
                ordered.Add((run.BaseElevation, 1, run));
            }
        }

        foreach (ElementId landingId in stairs.GetStairsLandings())
        {
            if (document.GetElement(landingId) is StairsLanding landing)
            {
                ordered.Add((landing.BaseElevation, 0, landing));
            }
        }

        // A landing sits at the top elevation of the run below it, which is the base of the run
        // above, so it sorts between the two. Elevations are compared on a 0.01 ft grid so a
        // landing a hair above the next run's base still sorts before that run.
        ordered.Sort((left, right) =>
        {
            int byElevation = Math.Round(left.Elevation / ElevationGridFeet)
                .CompareTo(Math.Round(right.Elevation / ElevationGridFeet));
            return byElevation != 0 ? byElevation : left.Order.CompareTo(right.Order);
        });

        // The rule needs one walking path from bottom to top. A stair that branches into runs
        // side by side at the same height has no single path, so it keeps the view-based result.
        bool hasParallelRuns = ordered
            .Where(entry => entry.Element is StairsRun)
            .GroupBy(entry => Math.Round(entry.Elevation / ElevationGridFeet))
            .Any(group => group.Count() > 1);
        if (hasParallelRuns)
        {
            return false;
        }

        Dictionary<ElementId, IReadOnlyList<Point2D>> runPaths = new();
        foreach ((_, _, Element element) in ordered)
        {
            if (element is not StairsRun run)
            {
                continue;
            }

            try
            {
                List<Point2D> path = ProjectLoop(run.GetStairsPath(), project);
                if (path.Count >= 2)
                {
                    // A spiral or S-shaped run can't be cut straight across; keep the view's result.
                    if (!StairDrawingRule.CanCutAcross(path))
                    {
                        return false;
                    }

                    runPaths[run.Id] = path;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        if (runPaths.Count == 0)
        {
            return false;
        }

        List<StairPathPiece> result = new(ordered.Count);
        for (int i = 0; i < ordered.Count; i++)
        {
            Element element = ordered[i].Element;
            Polygon2D? footprint = element switch
            {
                StairsRun run => TryFootprint(() => run.GetFootprintBoundary(), project),
                StairsLanding landing => TryFootprint(() => landing.GetFootprintBoundary(), project),
                _ => null,
            };
            if (footprint == null)
            {
                return false;
            }

            if (element is StairsRun stairsRun)
            {
                if (!runPaths.TryGetValue(stairsRun.Id, out IReadOnlyList<Point2D>? path))
                {
                    return false;
                }

                result.Add(new StairPathPiece(StairPieceKind.Run, path, footprint, stairsRun.UniqueId));
                continue;
            }

            // A landing's walking line joins the end of the run below to the start of the run above.
            IReadOnlyList<Point2D>? below = FindRunPath(ordered, runPaths, i, -1);
            IReadOnlyList<Point2D>? above = FindRunPath(ordered, runPaths, i, +1);
            IReadOnlyList<Point2D> landingPath = below != null && above != null
                ? new[] { below[below.Count - 1], above[0] }
                : new[] { below?[below.Count - 1] ?? above?[0] ?? default };
            result.Add(new StairPathPiece(StairPieceKind.Landing, landingPath, footprint, element.UniqueId));
        }

        pieces = result;
        return true;
    }

    private static IReadOnlyList<Point2D>? FindRunPath(
        List<(double Elevation, int Order, Element Element)> ordered,
        Dictionary<ElementId, IReadOnlyList<Point2D>> runPaths,
        int index,
        int step)
    {
        int next = index + step;
        if (next < 0 || next >= ordered.Count || ordered[next].Element is not StairsRun run)
        {
            return null;
        }

        return runPaths.TryGetValue(run.Id, out IReadOnlyList<Point2D>? path) ? path : null;
    }

    private static Polygon2D? TryFootprint(Func<CurveLoop> read, Func<XYZ, Point2D> project)
    {
        try
        {
            List<Point2D> ring = ProjectLoop(read(), project);
            if (ring.Count < 3)
            {
                return null;
            }

            ring.Add(ring[0]);
            return new Polygon2D(ring);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static List<Point2D> ProjectLoop(CurveLoop loop, Func<XYZ, Point2D> project)
    {
        List<Point2D> points = new();
        foreach (Curve curve in loop)
        {
            IList<XYZ> tessellated = curve.Tessellate();
            foreach (XYZ xyz in tessellated)
            {
                Point2D point = project(xyz);
                if (points.Count == 0 || !IsSamePoint(points[points.Count - 1], point))
                {
                    points.Add(point);
                }
            }
        }

        if (points.Count > 1 && IsSamePoint(points[0], points[points.Count - 1]))
        {
            points.RemoveAt(points.Count - 1);
        }

        return points;
    }

    private static bool TryGetHostLevelElevation(
        Stairs stairs,
        BuiltInParameter parameter,
        Func<XYZ, XYZ> toHost,
        out double elevation)
    {
        elevation = 0d;
        ElementId? levelId = stairs.get_Parameter(parameter)?.AsElementId();
        if (levelId == null ||
            levelId == ElementId.InvalidElementId ||
            stairs.Document.GetElement(levelId) is not Level level)
        {
            return false;
        }

        elevation = toHost(new XYZ(0d, 0d, level.ProjectElevation)).Z;
        return true;
    }

    private static bool IsSamePoint(Point2D a, Point2D b)
    {
        return Math.Abs(a.X - b.X) <= 1e-6d && Math.Abs(a.Y - b.Y) <= 1e-6d;
    }
}
