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
    public void ElementsSharingAnIdAreAWarningTheExportFixesAndLevelsAnError()
    {
        IReadOnlyList<GisAuditFinding> findings = Audit(
            ids: new[] { new AuditIdValue(203, "abc"), new AuditIdValue(202, "ABC "), new AuditIdValue(201, "abc"), new AuditIdValue(204, "other") },
            levelIds: new[] { new AuditIdValue(1, "l1"), new AuditIdValue(2, "l1") });

        Assert.Equal(2, findings.Count);
        GisAuditFinding levels = findings.Single(finding => finding.Severity == GisAuditSeverity.Error);
        Assert.Equal(new long[] { 1, 2 }, levels.ElementIds);
        Assert.StartsWith("Levels B1FL, 1FL share level_id l1", levels.Message);
        GisAuditFinding copies = findings.Single(finding => finding.Severity == GisAuditSeverity.Warning);
        Assert.Equal(new long[] { 201, 202, 203 }, copies.ElementIds);
        Assert.Equal("3 elements share id abc (a copy keeps its original's id): 201, 202, 203. The next export keeps it on 201, the oldest, and gives each copy it exports a new id.", copies.Message);
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

    // G3: what the export decides by guess is an error on a level a GIS_ plan exports.
    private static ExportAuditSnapshot OnLevels(
        AuditDoor[]? doors = null,
        AuditUnread[]? unread = null,
        params (long Element, long Level)[] levels) =>
        new(Views(), doors: doors, unread: unread, elementLevels: levels.Select(entry => new KeyValuePair<long, long>(entry.Element, entry.Level)));

    private static GisAuditFinding Guess(params long[] elements) =>
        new(GisAuditSeverity.Warning, "floor types", "Floor type X (2 floors) has no gis_category, so its floors export by type name. Run Fix floor types.", elements, exportsByGuess: true);

    [Fact]
    public void AGuessOnAGisPlanBecomesAnError()
    {
        GisAuditFinding raised = Assert.Single(ExportAudit.RaiseGuessesOnGisPlans(new[] { Guess(501, 502) }, OnLevels(levels: new[] { (501L, 2L), (502L, 4L) })));

        Assert.Equal(GisAuditSeverity.Error, raised.Severity);
        Assert.Equal(new long[] { 501, 502 }, raised.ElementIds);
        Assert.EndsWith("Run Fix floor types. 1 of its 2 elements are on levels with a GIS_ plan, so the export asks to fix or accept this first.", raised.Message);
        Assert.Single(GisAudit.BlockingFindings(new[] { raised }));
    }

    [Fact]
    public void AGuessOffTheGisPlansAndAWarningThatIsNoGuessStayWarnings()
    {
        GisAuditFinding notAGuess = new(GisAuditSeverity.Warning, "levels", "Level 1FL has no ordinal.", new long[] { 2 });
        IReadOnlyList<GisAuditFinding> findings = ExportAudit.RaiseGuessesOnGisPlans(
            new[] { Guess(501), notAGuess, Guess(503) },
            OnLevels(levels: new[] { (501L, 4L), (2L, 2L) }));

        Assert.All(findings, finding => Assert.Equal(GisAuditSeverity.Warning, finding.Severity));
        Assert.Equal("Floor type X (2 floors) has no gis_category, so its floors export by type name. Run Fix floor types.", findings[0].Message);
    }

    [Fact]
    public void ErrorsComeFirstAfterRaising()
    {
        GisAuditFinding info = new(GisAuditSeverity.Info, "floors", "note");
        IReadOnlyList<GisAuditFinding> findings = ExportAudit.RaiseGuessesOnGisPlans(new[] { info, Guess(501) }, OnLevels(levels: new[] { (501L, 1L) }));

        Assert.Equal(new[] { GisAuditSeverity.Error, GisAuditSeverity.Info }, findings.Select(finding => finding.Severity));
        Assert.EndsWith(" Its element is on levels with a GIS_ plan, so the export asks to fix or accept this first.", findings[0].Message);
    }

    [Fact]
    public void AllElementsOnGisPlansAreCountedOnce()
    {
        GisAuditFinding raised = Assert.Single(ExportAudit.RaiseGuessesOnGisPlans(new[] { Guess(501, 502) }, OnLevels(levels: new[] { (501L, 1L), (502L, 2L) })));

        Assert.EndsWith(" All 2 of its elements are on levels with a GIS_ plan, so the export asks to fix or accept this first.", raised.Message);
    }

    [Fact]
    public void APlanWithoutALevelExportsNoLevel()
    {
        ExportViewSnapshot views = Views(plans: new ExportPlanView[] { new(11, "GIS_B1FL", 1, North, null, 900), new(19, "GIS_draft", -1, North, null, 900) });
        ExportAuditSnapshot snapshot = new(views, elementLevels: new[] { new KeyValuePair<long, long>(501, -1) });

        Assert.Equal(new long[] { 1 }, snapshot.GisPlanLevels);
        Assert.Equal(GisAuditSeverity.Warning, Assert.Single(ExportAudit.RaiseGuessesOnGisPlans(new[] { Guess(501) }, snapshot)).Severity);
    }

    [Fact]
    public void AStairMadeAsAFamilyIsAGuess()
    {
        GisAuditFinding finding = Assert.Single(Audit(families: new[] { new AuditFamily(401, 80, "階段_手摺付", "W1200") }));

        Assert.True(finding.ExportsByGuess);
    }

    [Fact]
    public void ADoorTypeWithoutGisDoorOnAGisPlanIsAWarning()
    {
        IReadOnlyList<GisAuditFinding> findings = ExportAudit.Run(OnLevels(doors: new[]
        {
            new AuditDoor(601, 90, "片開き : 900", 1, ""),
            new AuditDoor(602, 90, "片開き : 900", 2, ""),
            new AuditDoor(603, 90, "片開き : 900", 4, ""),
            new AuditDoor(604, 91, "両開き : 1800", 1, "double"),
            new AuditDoor(605, 92, "Store : 900", 4, ""),
        }));

        GisAuditFinding finding = Assert.Single(findings);
        Assert.Equal(GisAuditSeverity.Warning, finding.Severity);
        Assert.False(finding.ExportsByGuess);
        Assert.Equal("doors", finding.Topic);
        Assert.Equal(new long[] { 601, 602 }, finding.ElementIds);
        Assert.Equal("Door type 片開き : 900 (2 on levels with a GIS_ plan) has no gis_door, so its openings export with no door type; Kanizu keeps the GDB's door type only where the opening already has one. Set gis_door on the type.", finding.Message);
    }

    [Fact]
    public void RampsAndFloorFamiliesOnAGisPlanAreErrors()
    {
        IReadOnlyList<GisAuditFinding> findings = ExportAudit.Run(OnLevels(unread: new[]
        {
            new AuditUnread(701, UnreadKind.Ramp, 2),
            new AuditUnread(702, UnreadKind.Ramp, 2),
            new AuditUnread(703, UnreadKind.Ramp, 4),
            new AuditUnread(704, UnreadKind.FloorFamily, 1),
        }));

        Assert.Equal(new[] { new long[] { 701, 702 }, new long[] { 704 } }, findings.Select(finding => finding.ElementIds.ToArray()));
        Assert.All(findings, finding => Assert.Equal(GisAuditSeverity.Error, finding.Severity));
        Assert.Equal("Level 1FL has a GIS_ plan and 2 Revit ramp(s), which the export never reads: their area is missing from the export. Model each slope as a floor of a ramp floor type.", findings[0].Message);
        Assert.StartsWith("Level B1FL has a GIS_ plan and 1 floor(s) made as a family, which", findings[1].Message);
    }
}
