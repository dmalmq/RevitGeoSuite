using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Models;

public sealed class StairDrawingRuleTests
{
    private const double Tolerance = 1e-6d;

    [Fact]
    public void StraightStair_FloorsOverlapByTheSettingAtTheMiddle()
    {
        StairDrawingRule rule = new(new[] { StraightRun(length: 10d) });

        Assert.Equal(10d, rule.TotalLengthMeters, 6);
        Assert.Equal(5.9d * 2d, Area(rule.BuildFloorPolygons(StairFloorSide.Lower, StairDrawingSettings.Default)), 6);
        Assert.Equal(5.9d * 2d, Area(rule.BuildFloorPolygons(StairFloorSide.Upper, StairDrawingSettings.Default)), 6);
        Assert.Equal((0d, 5.9d), Round(rule.GetPolygonRange(StairFloorSide.Lower, StairDrawingSettings.Default)));
        Assert.Equal((4.1d, 10d), Round(rule.GetPolygonRange(StairFloorSide.Upper, StairDrawingSettings.Default)));
    }

    [Fact]
    public void ZeroOverlap_TilesTheFloorsExactly()
    {
        StairDrawingRule rule = new(new[] { StraightRun(length: 10d) });
        StairDrawingSettings settings = new() { FloorOverlapMeters = 0d };

        Assert.Equal(10d, Area(rule.BuildFloorPolygons(StairFloorSide.Lower, settings)), 6);
        Assert.Equal(10d, Area(rule.BuildFloorPolygons(StairFloorSide.Upper, settings)), 6);
    }

    [Fact]
    public void StepLines_SitOnOneGridAndStopShortOfTheMiddle()
    {
        StairDrawingRule rule = new(new[] { StraightRun(length: 10d) });

        double[] lower = rule.GetStepStations(StairFloorSide.Lower, StairDrawingSettings.Default).ToArray();
        double[] upper = rule.GetStepStations(StairFloorSide.Upper, StairDrawingSettings.Default).ToArray();

        Assert.Equal(new[] { 0.6, 1.2, 1.8, 2.4, 3.0, 3.6 }, lower.Select(value => Math.Round(value, 6)));
        Assert.Equal(new[] { 6.6, 7.2, 7.8, 8.4, 9.0, 9.6 }, upper.Select(value => Math.Round(value, 6)));
    }

    [Fact]
    public void StepLines_RunWallToWallAcrossTheRun()
    {
        StairDrawingRule rule = new(new[] { StraightRun(length: 10d, width: 2.9d) });

        IReadOnlyList<StairStepLine> lines = rule.BuildStepLines(StairFloorSide.Lower, StairDrawingSettings.Default);

        Assert.Equal(6, lines.Count);
        foreach (StairStepLine line in lines)
        {
            Assert.Equal(2, line.Line.Points.Count);
            Assert.Equal(line.StationMeters, line.Line.Points[0].X, 6);
            Assert.Equal(line.StationMeters, line.Line.Points[1].X, 6);
            Assert.Equal(2.9d, Math.Abs(line.Line.Points[1].Y - line.Line.Points[0].Y), 6);
        }
    }

    [Fact]
    public void ThirtyCentimetreSpacing_IsAllowed()
    {
        StairDrawingRule rule = new(new[] { StraightRun(length: 6d) });

        IReadOnlyList<double> lower = rule.GetStepStations(StairFloorSide.Lower, new StairDrawingSettings { StepSpacingMeters = 0.30d });

        Assert.Equal(new[] { 0.3, 0.6, 0.9, 1.2, 1.5, 1.8 }, lower.Select(value => Math.Round(value, 6)));
    }

    [Fact]
    public void UShapedStair_CutsStraightAcrossTheLandingAndLeavesItBlank()
    {
        // Run up along +X, a landing at the far end, and a run back along -X beside the first.
        StairPathPiece first = new(
            StairPieceKind.Run,
            new[] { new Point2D(0, 0.5), new Point2D(4, 0.5) },
            Rect(0, 0, 4, 1),
            "run-1");
        StairPathPiece landing = new(
            StairPieceKind.Landing,
            new[] { new Point2D(4, 0.5), new Point2D(4, 2.5) },
            Rect(4, 0, 6, 3));
        StairPathPiece second = new(
            StairPieceKind.Run,
            new[] { new Point2D(4, 2.5), new Point2D(0, 2.5) },
            Rect(0, 2, 4, 3),
            "run-2");
        StairDrawingRule rule = new(new[] { first, landing, second });

        // 4 + 2 + 4 = 10 m, so the middle is on the landing.
        Assert.Equal(8.8d, Area(rule.BuildFloorPolygons(StairFloorSide.Lower, StairDrawingSettings.Default)), 6);
        Assert.Equal(8.8d, Area(rule.BuildFloorPolygons(StairFloorSide.Upper, StairDrawingSettings.Default)), 6);

        IReadOnlyList<StairStepLine> lower = rule.BuildStepLines(StairFloorSide.Lower, StairDrawingSettings.Default);
        IReadOnlyList<StairStepLine> upper = rule.BuildStepLines(StairFloorSide.Upper, StairDrawingSettings.Default);
        Assert.All(lower, line => Assert.Same(first, line.Run));
        Assert.All(upper, line => Assert.Same(second, line.Run));
        Assert.Equal(new[] { 6.6, 7.2, 7.8, 8.4, 9.0, 9.6 }, upper.Select(line => Math.Round(line.StationMeters, 6)));
        Assert.All(upper, line => Assert.Equal(1d, Math.Abs(line.Line.Points[1].Y - line.Line.Points[0].Y), 6));
    }

    [Fact]
    public void LandingStations_GetNoLines()
    {
        StairPathPiece first = new(StairPieceKind.Run, new[] { new Point2D(0, 0), new Point2D(3, 0) }, Rect(0, -1, 3, 1));
        StairPathPiece landing = new(StairPieceKind.Landing, new[] { new Point2D(3, 0), new Point2D(5, 0) }, Rect(3, -1, 5, 1));
        StairPathPiece second = new(StairPieceKind.Run, new[] { new Point2D(5, 0), new Point2D(8, 0) }, Rect(5, -1, 8, 1));
        StairDrawingRule rule = new(new[] { first, landing, second });
        StairDrawingSettings settings = new() { LineGapBeforeMiddleMeters = 0d };

        double[] stations = rule.GetStepStations(StairFloorSide.Lower, settings)
            .Concat(rule.GetStepStations(StairFloorSide.Upper, settings))
            .ToArray();

        Assert.DoesNotContain(stations, station => station > 3d + Tolerance && station < 5d - Tolerance);
    }

    [Fact]
    public void RunEndOnTheGrid_GetsNoClosingLine()
    {
        // The first run ends at 3.0 m, a multiple of 0.60 m, where the landing starts.
        StairPathPiece first = new(StairPieceKind.Run, new[] { new Point2D(0, 0), new Point2D(3, 0) }, Rect(0, -1, 3, 1));
        StairPathPiece landing = new(StairPieceKind.Landing, new[] { new Point2D(3, 0), new Point2D(5, 0) }, Rect(3, -1, 5, 1));
        StairPathPiece second = new(StairPieceKind.Run, new[] { new Point2D(5, 0), new Point2D(8, 0) }, Rect(5, -1, 8, 1));
        StairDrawingRule rule = new(new[] { first, landing, second });
        StairDrawingSettings settings = new() { LineGapBeforeMiddleMeters = 0d };

        double[] lower = rule.GetStepStations(StairFloorSide.Lower, settings).ToArray();

        Assert.Equal(new[] { 0.6, 1.2, 1.8, 2.4 }, lower.Select(value => Math.Round(value, 6)));
    }

    [Fact]
    public void AllStepLines_KeepTheGridThroughTheMiddleAndSkipTheLanding()
    {
        StairPathPiece first = new(StairPieceKind.Run, new[] { new Point2D(0, 0), new Point2D(3, 0) }, Rect(0, -1, 3, 1));
        StairPathPiece landing = new(StairPieceKind.Landing, new[] { new Point2D(3, 0), new Point2D(5, 0) }, Rect(3, -1, 5, 1));
        StairPathPiece second = new(StairPieceKind.Run, new[] { new Point2D(5, 0), new Point2D(8, 0) }, Rect(5, -1, 8, 1));
        StairDrawingRule rule = new(new[] { first, landing, second });

        IReadOnlyList<StairStepLine> lines = rule.BuildAllStepLines(StairDrawingSettings.Default);

        Assert.Equal(
            new[] { 0.6, 1.2, 1.8, 2.4, 5.4, 6.0, 6.6, 7.2 },
            lines.Select(line => Math.Round(line.StationMeters, 6)));
        Assert.All(lines, line => Assert.Equal(line.StationMeters, line.Line.Points[0].X, 6));
        Assert.All(lines, line => Assert.Equal(2d, Math.Abs(line.Line.Points[1].Y - line.Line.Points[0].Y), 6));
    }

    [Fact]
    public void AllStepLines_FollowTheModelsSpacingNotTheTreads()
    {
        // A 4.2 m run Revit would draw with 14 treads of 0.30 m.
        StairDrawingRule rule = new(new[] { StraightRun(length: 4.2d) });

        Assert.Equal(
            new[] { 0.6, 1.2, 1.8, 2.4, 3.0, 3.6 },
            rule.GetAllStepStations(StairDrawingSettings.Default).Select(value => Math.Round(value, 6)));
        Assert.Equal(13, rule.GetAllStepStations(new StairDrawingSettings { StepSpacingMeters = 0.30d }).Count);
    }

    [Theory]
    // Base level 0, top level 4: the plain case.
    [InlineData(0d, 4d, 0d, 4d, 0d, "Lower")]
    [InlineData(0d, 4d, 0d, 4d, 4d, "Upper")]
    [InlineData(0d, 4d, 0d, 4d, 8d, null)]
    // Hosted on level -4 with a +4 offset, no top level: it starts on 0 and arrives at 4; -4 shows nothing.
    [InlineData(0d, 4d, -4d, null, 0d, "Lower")]
    [InlineData(0d, 4d, -4d, null, 4d, "Upper")]
    [InlineData(0d, 4d, -4d, null, -4d, null)]
    // Named top level 8, but its height ends it on 4: only 4 shows the upper half.
    [InlineData(0d, 4d, 0d, 8d, 4d, "Upper")]
    [InlineData(0d, 4d, 0d, 8d, 8d, null)]
    // Starts 0.3 above level 0, on no level: the named base level stands in for it.
    [InlineData(0.3d, 4d, 0d, 4d, 0d, "Lower")]
    // Tops out 0.2 below level 4, on no level: the named top level stands in for it.
    [InlineData(0d, 3.8d, 0d, 4d, 4d, "Upper")]
    public void StairEnds_EachEndBelongsToOneFloor(double bottom, double top, double namedBase, double? namedTop, double level, string? expected)
    {
        double[] levels = { -4d, 0d, 4d, 8d };
        StairEnds ends = new(bottom, top, namedBase, namedTop);

        StairFloorSide? side = ends.SideOn(level, elevation => levels.Any(l => Math.Abs(l - elevation) <= 0.01d), 0.01d);

        Assert.Equal(expected, side?.ToString());
    }

    [Fact]
    public void CanCutAcross_AllowsQuarterTurnsButNotSpirals()
    {
        Assert.True(StairDrawingRule.CanCutAcross(new[] { new Point2D(0, 0), new Point2D(5, 0) }));
        Assert.True(StairDrawingRule.CanCutAcross(Arc(degrees: 90d)));
        Assert.False(StairDrawingRule.CanCutAcross(Arc(degrees: 270d)));
        Assert.False(StairDrawingRule.CanCutAcross(new[]
        {
            new Point2D(0, 0), new Point2D(2, 0), new Point2D(2, 2), new Point2D(0, 2), new Point2D(0, 4),
        }));
    }

    private static Point2D[] Arc(double degrees)
    {
        const int Steps = 36;
        return Enumerable.Range(0, Steps + 1)
            .Select(i => (degrees * i / Steps) * Math.PI / 180d)
            .Select(angle => new Point2D(3d * Math.Cos(angle), 3d * Math.Sin(angle)))
            .ToArray();
    }

    private static StairPathPiece StraightRun(double length, double width = 2d)
    {
        return new StairPathPiece(
            StairPieceKind.Run,
            new[] { new Point2D(0, 0), new Point2D(length, 0) },
            Rect(0, -width / 2d, length, width / 2d),
            "run");
    }

    private static Polygon2D Rect(double minX, double minY, double maxX, double maxY)
    {
        return new Polygon2D(new[]
        {
            new Point2D(minX, minY),
            new Point2D(maxX, minY),
            new Point2D(maxX, maxY),
            new Point2D(minX, maxY),
            new Point2D(minX, minY),
        });
    }

    private static double Area(IReadOnlyList<Polygon2D> polygons)
    {
        double total = 0d;
        foreach (Polygon2D polygon in polygons)
        {
            IReadOnlyList<Point2D> ring = polygon.ExteriorRing;
            double sum = 0d;
            for (int i = 0; i < ring.Count - 1; i++)
            {
                sum += (ring[i].X * ring[i + 1].Y) - (ring[i + 1].X * ring[i].Y);
            }

            total += Math.Abs(sum) * 0.5d;
        }

        return total;
    }

    private static (double, double) Round((double From, double To) range)
    {
        return (Math.Round(range.From, 6), Math.Round(range.To, 6));
    }
}
