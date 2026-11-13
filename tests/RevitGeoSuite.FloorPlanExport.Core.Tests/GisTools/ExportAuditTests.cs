using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.GisTools;

public sealed class ExportAuditTests
{
    private static readonly ViewFrame North = new(new Point2D(0, 0), 1, 0);

    private static readonly ExportTemplate Template = new(900, new[]
    {
        new TemplateCategory(1, "Floors", TemplateCategoryKind.ExportReads, hidden: false),
        new TemplateCategory(2, "Thin Lines", TemplateCategoryKind.LineStyle, hidden: true),
    });

    // B1FL, 1FL and 2FL hold floors and have GIS_ plans; TP±0 is the level above with neither.
    private static ExportViewSnapshot Views(ExportTemplate? template = null, bool withTemplate = true, ExportPlanView[]? plans = null) => new(
        withTemplate ? template ?? Template : null,
        new[]
        {
            new ExportViewLevel(1, "B1FL", true, true),
            new ExportViewLevel(2, "1FL", true, true),
            new ExportViewLevel(3, "2FL", true, true),
            new ExportViewLevel(4, "TP±0", false, true),
        },
        plans ?? new ExportPlanView[]
        {
            new(11, "GIS_B1FL", 1, North, null, 900),
            new(12, "GIS_1FL", 2, North, null, 900),
            new(13, "GIS_2FL", 3, North, null, 900),
        },
        Array.Empty<string>(),
        Array.Empty<Point2D>());

    private static IReadOnlyList<GisAuditFinding> Audit(
        ExportViewSnapshot? views = null,
        AuditStair[]? stairs = null,
        AuditFamily[]? families = null,
        AuditIdValue[]? ids = null,
        AuditIdValue[]? levelIds = null) =>
        ExportAudit.Run(new ExportAuditSnapshot(views ?? Views(), stairs, families, ids, levelIds));

    private static AuditFamily Escalator(long id, string? direction = "up", long bottom = 1, long? top = 2, string kind = "escalator") =>
        new(id, 70, "ESC_1200", "S800", kind, null, direction, bottom, top);

    [Fact]
    public void AModelBuiltForTheExportHasNoFindings()
    {
        Assert.Empty(Audit(
            stairs: new[] { new AuditStair(201, 1, 2) },
            families: new[] { Escalator(301) },
            ids: new[] { new AuditIdValue(201, "a"), new AuditIdValue(301, "b") },
            levelIds: new[] { new AuditIdValue(1, "l1"), new AuditIdValue(2, "l2") }));
    }

    [Fact]
    public void ElementsSharingAnIdAreAnErrorThatListsEveryOne()
    {
        IReadOnlyList<GisAuditFinding> findings = Audit(
            ids: new[] { new AuditIdValue(201, "abc"), new AuditIdValue(202, "ABC "), new AuditIdValue(203, "abc"), new AuditIdValue(204, "other") },
            levelIds: new[] { new AuditIdValue(1, "l1"), new AuditIdValue(2, "l1") });

        Assert.Equal(2, findings.Count);
        Assert.All(findings, finding => Assert.Equal(GisAuditSeverity.Error, finding.Severity));
        Assert.Equal(new long[] { 201, 202, 203 }, findings[0].ElementIds);
        Assert.StartsWith("3 elements share id abc", findings[0].Message);
        Assert.Contains("201, 202, 203", findings[0].Message);
        Assert.Equal(new long[] { 1, 2 }, findings[1].ElementIds);
        Assert.StartsWith("Levels B1FL, 1FL share level_id l1", findings[1].Message);
    }

    [Fact]
    public void AStairFamilyIsReportedOncePerType()
    {
        IReadOnlyList<GisAuditFinding> findings = Audit(families: new[]
        {
            new AuditFamily(401, 80, "階段_A", "W1200"),
            new AuditFamily(402, 80, "階段_A", "W1200"),
            new AuditFamily(403, 81, "Stair Railing Post", "P1", gisKind: "elevator"),
            new AuditFamily(404, 82, "Bench", "B1"),
        });

        GisAuditFinding finding = Assert.Single(findings);
        Assert.Equal("stairs", finding.Topic);
        Assert.Equal(new long[] { 401, 402 }, finding.ElementIds);
        Assert.StartsWith("階段_A : W1200 (2 placed) looks like a stair but is a family", finding.Message);
    }

    [Fact]
    public void StairsWithNoTopOrTheSameTopAndBaseShareOneFinding()
    {
        GisAuditFinding finding = Assert.Single(Audit(stairs: new[]
        {
            new AuditStair(201, 1, null),
            new AuditStair(202, 2, 2),
            new AuditStair(203, 1, 2),
        }));

        Assert.Equal(new long[] { 201, 202 }, finding.ElementIds);
        Assert.StartsWith("2 stair(s) have no top level other than their base", finding.Message);
    }

    [Fact]
    public void AMultistoreyStairGroupNamesItsStoreys()
    {
        GisAuditFinding finding = Assert.Single(Audit(stairs: new[] { new AuditStair(201, 1, 2, new long[] { 1, 2 }), new AuditStair(202, 2, 3, new long[] { 2 }) }));

        Assert.Equal(new long[] { 201 }, finding.ElementIds);
        Assert.StartsWith("Stair 201 is a multistorey group on levels B1FL, 1FL.", finding.Message);
    }

    [Fact]
    public void EscalatorsWithoutAUsableDirectionAreGroupedByType()
    {
        IReadOnlyList<GisAuditFinding> findings = Audit(families: new[]
        {
            Escalator(301, direction: null),
            Escalator(302, direction: "sideways"),
            Escalator(303, direction: "下り"),
            new AuditFamily(304, 71, "エスカレーター", "S1000", gisDirection: null, bottomLevelId: 1, topLevelId: 2),
            new AuditFamily(305, 72, "MW", "1", gisKind: "moving_walkway"),
        });

        Assert.Equal(2, findings.Count);
        Assert.Equal(new long[] { 301, 302 }, findings[0].ElementIds);
        Assert.Equal("ESC_1200 : S800: 2 escalator(s) have no gis_direction of up, down or both, so the network cannot tell which way they run.", findings[0].Message);
        Assert.Equal(new long[] { 304 }, findings[1].ElementIds);
    }

    [Fact]
    public void AConnectorEndingOnALevelWithNoGisPlanIsReportedPerLevel()
    {
        IReadOnlyList<GisAuditFinding> findings = Audit(
            stairs: new[] { new AuditStair(201, 3, 4), new AuditStair(202, 3, 4) },
            families: new[] { Escalator(301, bottom: 3, top: 4), Escalator(302, bottom: 1, top: null) });

        GisAuditFinding finding = Assert.Single(findings);
        Assert.Equal(new long[] { 201, 202, 301 }, finding.ElementIds);
        Assert.Equal(
            "Level TP±0 has no GIS_ plan, so the export does not write it, but 2 stair(s) and 1 escalator(s) end on it: their links have nowhere to land. Add a GIS_ plan for it, or end them on a level that is exported.",
            finding.Message);
    }

    [Fact]
    public void ALevelWithFloorsButNoGisPlanYetPointsToSetUp()
    {
        ExportViewSnapshot views = Views(plans: new ExportPlanView[] { new(11, "GIS_B1FL", 1, North, null, 900) });

        GisAuditFinding finding = Assert.Single(Audit(views, stairs: new[] { new AuditStair(201, 1, 2) }));

        Assert.EndsWith("Run Set up model to add its plan.", finding.Message);
    }

    [Fact]
    public void AMissingTemplateIsReported()
    {
        GisAuditFinding finding = Assert.Single(Audit(Views(withTemplate: false)));

        Assert.Equal("The view template GIS Export is missing, so the GIS_ plans may hide what the export reads. Run Set up model.", finding.Message);
    }

    [Fact]
    public void AChangedTemplateNamesWhatSetUpPutsBack()
    {
        ExportTemplate changed = new(900, new[]
        {
            new TemplateCategory(1, "Floors", TemplateCategoryKind.ExportReads, hidden: true),
            new TemplateCategory(2, "Thin Lines", TemplateCategoryKind.LineStyle, hidden: false),
        });

        GisAuditFinding finding = Assert.Single(Audit(Views(changed)));

        Assert.Equal("The view template GIS Export was changed in what the export reads. Run Set up model to show Floors; hide line style Thin Lines.", finding.Message);
    }

    [Fact]
    public void AGisPlanOnAnotherTemplateIsReportedNotReassigned()
    {
        ExportViewSnapshot views = Views(plans: new ExportPlanView[]
        {
            new(11, "GIS_B1FL", 1, North, null, 900),
            new(12, "GIS_1FL", 2, North, null, 950),
            new(13, "GIS_2FL", 3, North, null, null),
        });

        GisAuditFinding finding = Assert.Single(Audit(views));

        Assert.StartsWith("GIS_ plans GIS_1FL, GIS_2FL do not use the view template GIS Export", finding.Message);
        Assert.True(ExportViewPlanner.Plan(views).IsEmpty);
    }

    [Theory]
    [InlineData("escalator", null, "Lift", "A", true)]
    [InlineData("elevator", null, "Escalator", "A", false)]
    [InlineData(null, "escalator", "Box", "A", true)]
    [InlineData(null, "walkway", "Escalator", "A", false)]
    [InlineData(null, null, "エスカレーター_S1000", "A", true)]
    [InlineData(null, null, "動く歩道", "A", false)]
    public void AnEscalatorIsFoundByKindThenCategoryThenName(string? kind, string? category, string family, string type, bool expected)
    {
        Assert.Equal(expected, ExportAudit.IsEscalator(kind, category, family, type));
    }

    [Fact]
    public void TheEscalatorTopLevelIsTheLevelAboveNearestItsTopWithin25Metres()
    {
        // LUMINE EST: an escalator from B1 whose geometry tops out under the 1F slab.
        Dictionary<string, double> metres = new() { ["B1"] = 32.66, ["1F"] = 37.71, ["2F"] = 41.86 };
        string? Top(double topMetres) => VerticalCirculation.EscalatorTopLevel(32.66 / 0.3048, topMetres / 0.3048, metres.Keys, name => metres[name] / 0.3048);

        Assert.Equal("1F", Top(37.2));
        Assert.Null(Top(33.0));
        Assert.Null(Top(50.0));
    }

    // Levels B1FL, 1FL and 2FL at their Level.ProjectElevation: -5.05, 0 and 4.15 m.
    private static IReadOnlyList<GisAuditFinding> Slope(double lowMetres, double highMetres)
    {
        const double Feet = 1 / 0.3048;
        Polygon2D square = new(new[] { new Point2D(0, 0), new Point2D(10 * Feet, 0), new Point2D(10 * Feet, 10 * Feet), new Point2D(0, 10 * Feet) });
        return ExportAudit.Run(new ExportAuditSnapshot(
            Views(),
            floors: new[] { new LayoutFloor(501, 2, "ramp", new[] { square }, lowMetres * Feet, highMetres * Feet) },
            openings: new[] { new LayoutOpening(601, 2, new[] { new Point2D(Feet, 0), new Point2D(2 * Feet, 0), new Point2D(2 * Feet, 0.1 * Feet) }) },
            levelElevations: new Dictionary<long, double> { [1] = -5.05 * Feet, [2] = 0, [3] = 4.15 * Feet }))
            .Where(finding => finding.Topic == "floors")
            .ToList();
    }

    [Theory]
    [InlineData(0.0, 4.1)]
    [InlineData(0.0, 0.2)]
    public void ASlopeThatReachesALevelOrStaysOnItsOwnHasNoFinding(double low, double high)
    {
        Assert.Empty(Slope(low, high));
    }

    [Fact]
    public void AHalfLevelSlopeIsReportedWithItsFarEndAndTheNearestLevel()
    {
        GisAuditFinding finding = Assert.Single(Slope(0.0, 2.95));

        Assert.Equal(GisAuditSeverity.Info, finding.Severity);
        Assert.Equal(new long[] { 501 }, finding.ElementIds);
        Assert.Equal(
            "Floor 501 (ramp) on level 1FL leads to +2.95 m against its level, which is on no level: the nearest level is 2FL, 1.20 m from it. Its unit gets no to_level, so the network does not link it to another level.",
            finding.Message);
    }

    [Fact]
    public void ASlopeWithNeitherEndOnItsLevelIsReportedWithBothEnds()
    {
        Assert.Equal(
            "Floor 501 (ramp) on level 1FL runs from +0.40 m to +4.15 m against its level, so neither end is on it and the export does not guess where it leads. Its unit gets no to_level. Set its level and offset so its lower or upper end sits on its level.",
            Assert.Single(Slope(0.4, 4.15)).Message);
    }
}
