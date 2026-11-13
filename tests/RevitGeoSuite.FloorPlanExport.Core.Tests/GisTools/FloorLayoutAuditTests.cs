using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class FloorLayoutAuditTests
{
    private const double Feet = 1 / 0.3048;

    // A rectangle given in metres, stored in feet as Revit gives it.
    private static Polygon2D Rect(double minX, double minY, double maxX, double maxY) => new(new[]
    {
        new Point2D(minX * Feet, minY * Feet),
        new Point2D(maxX * Feet, minY * Feet),
        new Point2D(maxX * Feet, maxY * Feet),
        new Point2D(minX * Feet, maxY * Feet),
    });

    private static LayoutFloor Floor(long id, string category, Polygon2D outline, double riseMetres = 0, long level = 1) =>
        new(id, level, category, new[] { outline }, riseMetres * Feet);

    private static LayoutOpening Door(long id, double x, double y, long level = 1) =>
        new(id, level, new[] { new Point2D(x * Feet, y * Feet), new Point2D((x + 0.9) * Feet, y * Feet), new Point2D((x + 0.9) * Feet, (y + 0.1) * Feet) });

    private static List<GisAuditFinding> Audit(LayoutFloor[] floors, LayoutOpening[]? openings = null) =>
        FloorLayoutAudit.Run(floors, openings ?? Array.Empty<LayoutOpening>(), id => id == 1 ? "1FL" : "B1FL").ToList();

    [Fact]
    public void TwoWalkwaysSharingAnEdgeAreFine()
    {
        Assert.Empty(Audit(new[] { Floor(1, "walkway", Rect(0, 0, 10, 10)), Floor(2, "walkway", Rect(10, 0, 20, 10)) }));
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.04, false)]
    public void FloorsOverlappingByMoreThanHalfASquareMetreAreReportedWithTheArea(double overlapMetres, bool reported)
    {
        // 10 m tall strips overlapping by overlapMetres: 10 m² or 0.4 m².
        List<GisAuditFinding> findings = Audit(new[] { Floor(1, "walkway", Rect(0, 0, 10, 10)), Floor(2, "walkway", Rect(10 - overlapMetres, 0, 20, 10)) });

        if (reported)
        {
            GisAuditFinding finding = Assert.Single(findings);
            Assert.Equal(GisAuditSeverity.Warning, finding.Severity);
            Assert.Equal(new long[] { 1, 2 }, finding.ElementIds);
            Assert.Equal("Floors 1 and 2 on level 1FL overlap by 10.0 m², so the export gives that area to two units.", finding.Message);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Theory]
    [InlineData(0.12, true)]
    [InlineData(0.03, false)]
    [InlineData(0.5, false)]
    public void ANarrowGapIsReportedWithItsWidth(double gapMetres, bool reported)
    {
        List<GisAuditFinding> findings = Audit(
            new[] { Floor(1, "walkway", Rect(0, 0, 10, 10)), Floor(2, "walkway", Rect(10 + gapMetres, 0, 20, 10)) },
            new[] { Door(901, 2, 10), Door(902, 15, 10) });

        if (reported)
        {
            Assert.Equal("Floors 1 and 2 on level 1FL are 0.12 m apart, a gap the network cannot cross. Close it if people walk across.", Assert.Single(findings).Message);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Theory]
    [InlineData(10.3, false)]
    [InlineData(11.0, true)]
    public void AShopNeedsADoorWithinHalfAMetre(double doorY, bool reported)
    {
        List<GisAuditFinding> findings = Audit(new[] { Floor(1, "retail", Rect(0, 0, 10, 10)) }, new[] { Door(901, 2, doorY) });

        if (reported)
        {
            GisAuditFinding finding = Assert.Single(findings);
            Assert.Equal(GisAuditSeverity.Info, finding.Severity);
            Assert.Equal("Floor 1 (retail) on level 1FL has no door, opening, stair or escalator within 0.5 m and shares no edge with a walkable floor, so the network cannot reach it.", finding.Message);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void AShopOpenToAConcourseIsReachedButTheConcourseStillNeedsAWayIn()
    {
        GisAuditFinding finding = Assert.Single(Audit(new[] { Floor(1, "retail", Rect(0, 0, 10, 10)), Floor(2, "walkway", Rect(10, 0, 20, 10)) }));

        Assert.Equal(new long[] { 2 }, finding.ElementIds);
    }

    [Fact]
    public void ADoorOnAnotherLevelDoesNotCount()
    {
        Assert.Single(Audit(new[] { Floor(1, "retail", Rect(0, 0, 10, 10)) }, new[] { Door(901, 2, 10, level: 2) }));
    }

    [Theory]
    [InlineData("walkway", 0.6, true)]
    [InlineData("ramp", 0.6, false)]
    [InlineData("walkway", 0.03, false)]
    public void ASlopedFloorThatIsNotASlopeTypeIsReportedWithItsRise(string category, double rise, bool reported)
    {
        List<GisAuditFinding> findings = Audit(new[] { Floor(1, category, Rect(0, 0, 10, 10), rise) }, new[] { Door(901, 2, 10) });

        if (reported)
        {
            Assert.Equal("Floor 1 (walkway) on level 1FL rises 0.60 m but is not a slope type, so it exports as a flat walkway unit. Give it the type j ラチ外スロープ_床 or j ラチ内スロープ_床.", Assert.Single(findings).Message);
        }
        else
        {
            Assert.Empty(findings);
        }
    }

    [Fact]
    public void MoreThanTenOfOneKindOnALevelBecomeOneFinding()
    {
        // Twelve 10 m shops in a row, each overlapping the next by 0.1 m (1 m²), all with a door.
        LayoutFloor[] floors = Enumerable.Range(0, 12).Select(index => Floor(index + 1, "retail", Rect(index * 9.9, 0, index * 9.9 + 10, 10))).ToArray();
        LayoutOpening[] doors = Enumerable.Range(0, 12).Select(index => Door(900 + index, index * 9.9 + 4, 10)).ToArray();

        GisAuditFinding finding = Assert.Single(Audit(floors, doors));

        Assert.Equal("Level 1FL has 11 pairs of overlapping floors (up to 1.0 m²), so the export gives those areas to two units.", finding.Message);
        Assert.Equal(12, finding.ElementIds.Count);
    }
}
