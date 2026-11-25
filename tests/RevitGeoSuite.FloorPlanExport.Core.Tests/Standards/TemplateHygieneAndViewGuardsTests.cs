using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class TemplateHygieneAndViewGuardsTests
{
    private static ReadinessCheckSnapshot CleanTemplateSnapshot()
    {
        GisToolStandard standard = GisToolStandard.LoadEmbedded();
        return new ReadinessCheckSnapshot
        {
            DocumentTitle = "KanizuStationMaster2024.rte",
            DocumentPath = @"C:\Templates\KanizuStationMaster2024.rte",
            IsEmptyModel = true,
            StandardVersionText = standard.Version.ToString(),
            BoundParameters = standard.Parameters
                .Select(p => new BoundParameterInfo(p.Name, p.Guid, p.Binding, p.Categories))
                .ToList(),
            Materials = standard.Materials
                .Select((m, idx) => new MaterialInfo(100 + idx, m.Name, m.FillHex))
                .ToList(),
            FloorTypes = standard.FloorTypes
                .Select((ft, idx) => new FloorTypeInfo(200 + idx, ft.Name, ft.GisCategory, ft.GisRestriction, ft.Material))
                .ToList(),
            LoadedFamilies = new[]
            {
                new LoadedFamilyCheckInfo(301, "j EV", "EV_1500", "OST_MechanicalEquipment", gisKind: "elevator", hasGisKindParameter: true),
                new LoadedFamilyCheckInfo(304, "StationDoor", "Sliding_1800", "OST_Doors", gisDoor: "sliding", gisAccessCon: "none", hasGisDoorParameter: true, hasGisAccessConParameter: true),
            },
            Schedules = TemplateStandardDefinitions.QaSchedules
                .Select((s, idx) => new QaScheduleInfo(
                    400 + idx,
                    s.Name,
                    s.RequiredFields,
                    s.CategoryName,
                    s.RequiredFields.Select(name => new QaScheduleFieldInfo(
                        name,
                        GisSharedParameters.TryGet(name, out GisParameterDefinition def) ? def.Guid : (Guid?)null))))
                .ToList(),
            ViewTemplateNames = new[] { TemplateStandardDefinitions.ExportPlanViewTemplateName },
            Levels = new[] { new LevelInfo(501, "Level 1", ordinal: null, shortName: "", hasFloors: false) },
        };
    }

    [Fact]
    public void CanonicalExportTemplateNameIsGisExport()
    {
        Assert.Equal("GIS Export", TemplateStandardDefinitions.ExportPlanViewTemplateName);
        Assert.Equal("Kanizu GIS Export Plan", TemplateStandardDefinitions.LegacyExportPlanViewTemplateName);
    }

    [Fact]
    public void LegacyKanizuGisExportPlanTemplateCountsAsPresent()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.ViewTemplateNames = new[] { "Kanizu GIS Export Plan" };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        ReadinessCheckItem exportTemplate = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "view-template-export-plan");
        Assert.Equal(ReadinessCheckStatus.Pass, exportTemplate.Status);
        Assert.False(report.TemplatePlan.CreateExportPlanTemplate);
    }

    [Fact]
    public void BakedInModelIdFailsKzTpl001InTemplateMode()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.HostModelId = "tokyo-concourse-01";

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        ReadinessCheckItem identity = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "template-identity");
        Assert.Equal(ReadinessCheckStatus.Fail, identity.Status);
        Assert.Equal("KZ-TPL-001", identity.RuleId);
        Assert.Contains("ModelId", identity.Detail);
        Assert.False(report.IsReady);
    }

    [Fact]
    public void BakedInStationFolderAlsoFailsKzTpl001()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.HostStationFolder = @"\\share\stations\tokyo";

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        ReadinessCheckItem identity = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "template-identity");
        Assert.Equal(ReadinessCheckStatus.Fail, identity.Status);
        Assert.Contains("GisStationFolder", identity.Detail);
    }

    [Fact]
    public void BakedInLevelOrElementIdsFailKzTpl002()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.LevelsWithBakedInIdCount = 3;
        snapshot.LevelsWithBakedInIdSample = new[] { 501L, 502L, 503L };
        snapshot.ElementsWithBakedInIdCount = 5;
        snapshot.ElementsWithBakedInIdSample = new[] { 701L, 702L };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        ReadinessCheckItem item = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "template-element-ids");
        Assert.Equal(ReadinessCheckStatus.Fail, item.Status);
        Assert.Equal("KZ-TPL-002", item.RuleId);
        Assert.Contains("level_id", item.Detail);
        Assert.Contains("gis_key", item.Detail);
        Assert.Contains(501L, item.ElementIds);
        Assert.Contains(701L, item.ElementIds);
    }

    [Fact]
    public void TemplateHygieneIsNotApplicableInProjectMode()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.HostModelId = "tokyo-concourse-01";
        snapshot.LevelsWithBakedInIdCount = 10;

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);

        ReadinessCheckGroup group = Assert.Single(report.Groups, g => g.Id == "template-hygiene");
        Assert.Equal(ReadinessCheckStatus.NotApplicable, group.Status);
        Assert.All(group.Items, item => Assert.Equal(ReadinessCheckStatus.NotApplicable, item.Status));
    }

    [Fact]
    public void HideFilterOnExportViewFailsKzView004()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.ExportViewHideFilterNames = new[] { "Hide: Owner Furniture" };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        ReadinessCheckItem item = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "view-template-hide-filter");
        Assert.Equal(ReadinessCheckStatus.Fail, item.Status);
        Assert.Equal("KZ-VIEW-004", item.RuleId);
        Assert.Contains("Hide: Owner Furniture", item.Detail);
        Assert.False(report.IsReady);
    }

    [Fact]
    public void ReservedShowcaseFilterPrefixIsIgnoredByKzView004()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.ExportViewHideFilterNames = new[] { "[KANIZU_FORMA_RO] Showcase hide" };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        ReadinessCheckItem item = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "view-template-hide-filter");
        Assert.Equal(ReadinessCheckStatus.Pass, item.Status);
    }

    [Fact]
    public void ProjectModeWithNoApplicableChecksIsNotReadyAndDoesNotReport100Percent()
    {
        // Simulate a snapshot whose checks are all NotApplicable (empty project, template mode items only).
        ReadinessCheckSnapshot snapshot = new()
        {
            DocumentTitle = "Empty.rvt",
            DocumentPath = @"D:\empty.rvt",
            IsEmptyModel = true,
            StandardVersionText = string.Empty,
        };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);

        // Even in an empty-snapshot Project run, the mandatory parameter and family/floor checks will
        // surface failures because standard/materials/floor-types/schedules are missing. But if they
        // somehow all drop out, scoring must not report 100 and IsReady must be false.
        Assert.False(report.IsReady);
        Assert.NotEqual(100, report.ScorePercent);
    }

    [Fact]
    public void ProjectReportWithZeroApplicableChecksOrUnrunMandatoryChecksNeverReportsReadyOr100Percent()
    {
        TemplatePreparationPlan emptyPlan = new(Array.Empty<QaSchedulePlanStep>(), createExportPlanTemplate: false, repairExportPlanTemplate: false);

        ReadinessCheckGroup allNaGroup = new(
            "template-hygiene",
            "Template zero-identity hygiene",
            new[]
            {
                new ReadinessCheckItem("template-identity", "No baked-in project identity", ReadinessCheckStatus.NotApplicable, "N/A"),
            });
        ProjectReadinessReport zeroApplicable = new(
            ReadinessMode.Project,
            ReadinessMode.Project,
            new[] { allNaGroup },
            emptyPlan,
            canSetupModel: false,
            canFixFloorTypes: false);

        Assert.Equal(0, zeroApplicable.ApplicableCount);
        Assert.False(zeroApplicable.HasRunMandatoryChecks);
        Assert.False(zeroApplicable.IsReady);
        Assert.Equal(0, zeroApplicable.ScorePercent);

        ReadinessCheckGroup partialPassingGroup = new(
            "parameters",
            "Shared parameters & standard",
            new[]
            {
                new ReadinessCheckItem("standard-version", "Kanizu GIS standard version", ReadinessCheckStatus.Pass, "Stamped."),
            });
        ProjectReadinessReport unrunMandatory = new(
            ReadinessMode.Project,
            ReadinessMode.Project,
            new[] { partialPassingGroup },
            emptyPlan,
            canSetupModel: false,
            canFixFloorTypes: false);

        Assert.Equal(1, unrunMandatory.ApplicableCount);
        Assert.False(unrunMandatory.HasRunMandatoryChecks);
        Assert.False(unrunMandatory.IsReady);
        Assert.True(unrunMandatory.ScorePercent < 100);
    }

    [Fact]
    public void ExportViewPlannerSkipsLevelIdAssignmentWhenDisabledForTemplateMode()
    {
        ExportViewSnapshot snapshot = new(
            template: null,
            levels: new[] { new ExportViewLevel(501, "Level 1", hasFloors: false, hasLevelId: false) },
            planViews: Array.Empty<ExportPlanView>(),
            viewNames: Array.Empty<string>(),
            floorPoints: Array.Empty<RevitGeoSuite.FloorPlanExport.Core.Models.Point2D>());

        ExportViewPlan templatePlan = ExportViewPlanner.Plan(snapshot, assignLevelIds: false);
        Assert.Empty(templatePlan.LevelIds);

        ExportViewPlan projectPlan = ExportViewPlanner.Plan(snapshot, assignLevelIds: true);
        Assert.Single(projectPlan.LevelIds);
    }

    [Fact]
    public void DoorValuesFailWhenGisDoorContainsControlCharacters()
    {
        ReadinessCheckSnapshot snapshot = TemplateReadinessProjectSnapshot();
        snapshot.LoadedFamilies = new[]
        {
            new LoadedFamilyCheckInfo(304, "StationDoor", "Sliding_1800", "OST_Doors", gisDoor: "sliding\nmultiline", gisAccessCon: "none", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 900L }),
        };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);
        ReadinessCheckItem item = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "door-values");
        Assert.Equal(ReadinessCheckStatus.Fail, item.Status);
        Assert.Equal("KZ-ELEM-001", item.RuleId);
        Assert.Contains(900L, item.ElementIds);
    }

    [Fact]
    public void DoorValuesPassWhenValueDataIsBeingEnteredAndEveryDoorHasValidClosedDomainValue()
    {
        ReadinessCheckSnapshot snapshot = TemplateReadinessProjectSnapshot();
        snapshot.LoadedFamilies = new[]
        {
            new LoadedFamilyCheckInfo(304, "StationDoor", "Sliding_1800", "OST_Doors", gisDoor: "sliding", gisAccessCon: "none", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 900L }, gisOpeningCategory: "pedestrian.transit", hasGisOpeningCategoryParameter: true),
            new LoadedFamilyCheckInfo(305, "StaffDoor", "Swinging_900", "OST_Doors", gisDoor: "swinging", gisAccessCon: "employeesonly", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 901L }),
        };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);
        ReadinessCheckItem item = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "door-values");
        Assert.Equal(ReadinessCheckStatus.Pass, item.Status);
        Assert.Equal("KZ-ELEM-001", item.RuleId);
    }

    [Fact]
    public void DoorValuesFailWhenPlacedDoorHasOutOfDomainGisDoorGisAccessConOrGisOpeningCategory()
    {
        ReadinessCheckSnapshot snapshot = TemplateReadinessProjectSnapshot();
        snapshot.LoadedFamilies = new[]
        {
            new LoadedFamilyCheckInfo(304, "BadDoorType", "Hinged_900", "OST_Doors", gisDoor: "hinged", gisAccessCon: "none", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 900L }),
            new LoadedFamilyCheckInfo(305, "BadAccessDoor", "Sliding_1800", "OST_Doors", gisDoor: "sliding", gisAccessCon: "public", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 901L }),
            new LoadedFamilyCheckInfo(306, "BadOpeningCatDoor", "Auto_2000", "OST_Doors", gisDoor: "automatic", gisAccessCon: "none", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 902L }, gisOpeningCategory: "main_gate", hasGisOpeningCategoryParameter: true),
        };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);
        ReadinessCheckItem item = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "door-values");
        Assert.Equal(ReadinessCheckStatus.Fail, item.Status);
        Assert.Equal("KZ-ELEM-001", item.RuleId);
        Assert.Contains(900L, item.ElementIds);
        Assert.Contains(901L, item.ElementIds);
        Assert.Contains(902L, item.ElementIds);
    }

    [Fact]
    public void DoorValuesFailWhenAnyPlacedDoorHasEmptyGisDoorAfterDataStarted()
    {
        ReadinessCheckSnapshot snapshot = TemplateReadinessProjectSnapshot();
        snapshot.LoadedFamilies = new[]
        {
            new LoadedFamilyCheckInfo(304, "StationDoor", "Sliding_1800", "OST_Doors", gisDoor: "sliding", gisAccessCon: "none", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 900L }),
            new LoadedFamilyCheckInfo(305, "StaffDoor", "Swinging_900", "OST_Doors", gisDoor: "", gisAccessCon: "employeesonly", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 901L, 902L }),
        };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);
        ReadinessCheckItem item = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "door-values");
        Assert.Equal(ReadinessCheckStatus.Fail, item.Status);
        Assert.Equal("KZ-ELEM-001", item.RuleId);
        Assert.Contains("StaffDoor", item.Detail);
        Assert.Contains(901L, item.ElementIds);
    }

    [Fact]
    public void DoorValuesCheckIsSilentWhenNoDoorHasValuesYet()
    {
        ReadinessCheckSnapshot snapshot = TemplateReadinessProjectSnapshot();
        snapshot.LoadedFamilies = new[]
        {
            new LoadedFamilyCheckInfo(304, "StationDoor", "Sliding_1800", "OST_Doors", gisDoor: "", gisAccessCon: "", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 900L }),
            new LoadedFamilyCheckInfo(305, "StaffDoor", "Hinged_900", "OST_Doors", gisDoor: "", gisAccessCon: "", hasGisDoorParameter: true, hasGisAccessConParameter: true, instanceIds: new[] { 901L }),
        };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);
        Assert.DoesNotContain(report.Groups.SelectMany(g => g.Items), i => i.Id == "door-values");
    }

    private static ReadinessCheckSnapshot TemplateReadinessProjectSnapshot()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.DocumentTitle = "Tokyo.rvt";
        snapshot.DocumentPath = @"D:\Tokyo.rvt";
        snapshot.IsEmptyModel = false;
        snapshot.ProjectCrsEpsgCode = 6677;
        snapshot.ProjectCrsName = "JGD2011 / Japan Plane Rectangular CS IX";
        snapshot.IsGeoreferenceVerified = true;
        snapshot.HostModelId = "tokyo-01";
        snapshot.Levels = new[] { new LevelInfo(501, "1F", ordinal: 0, shortName: "1F", hasFloors: true) };
        return snapshot;
    }
}
