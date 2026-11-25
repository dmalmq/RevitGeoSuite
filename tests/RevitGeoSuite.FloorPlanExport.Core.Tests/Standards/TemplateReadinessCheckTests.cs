using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Standards;

public sealed class TemplateReadinessCheckTests
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
                new LoadedFamilyCheckInfo(302, "j エスカレータ-lightweight", "W1200", "OST_GenericModel", gisKind: "escalator", hasGisKindParameter: true),
                new LoadedFamilyCheckInfo(303, "j 自動改札", "Standard", "OST_SpecialityEquipment", gisKind: "ticket_gate", hasGisKindParameter: true),
                new LoadedFamilyCheckInfo(304, "StationDoor", "Sliding_1800", "OST_Doors", gisDoor: "sliding", gisAccessCon: "public", hasGisDoorParameter: true, hasGisAccessConParameter: true),
            },
            Schedules = TemplateStandardDefinitions.QaSchedules
                .Select((s, idx) => new QaScheduleInfo(400 + idx, s.Name, s.RequiredFields))
                .ToList(),
            ViewTemplateNames = new[] { TemplateStandardDefinitions.ExportPlanViewTemplateName },
            Levels = new[] { new LevelInfo(501, "Level 1", ordinal: null, shortName: "", hasFloors: false) },
        };
    }

    private static ReadinessCheckSnapshot CleanProjectSnapshot()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.DocumentTitle = "TokyoStation_Concourse.rvt";
        snapshot.DocumentPath = @"D:\Projects\TokyoStation_Concourse.rvt";
        snapshot.IsEmptyModel = false;
        snapshot.ProjectCrsEpsgCode = 6677;
        snapshot.ProjectCrsName = "JGD2011 / Japan Plane Rectangular CS IX";
        snapshot.IsGeoreferenceVerified = true;
        snapshot.HostModelId = "tokyo-concourse-01";
        snapshot.LinkedModels = new[]
        {
            new LinkedModelReadinessInfo("TokyoStation_Platform.rvt", isLoaded: true, modelId: "tokyo-platform-02"),
        };
        snapshot.Levels = new[]
        {
            new LevelInfo(501, "B1FL", ordinal: -1, shortName: "B1", hasFloors: true),
            new LevelInfo(502, "1FL", ordinal: 0, shortName: "1F", hasFloors: true),
        };
        return snapshot;
    }

    [Fact]
    public void CleanTemplateInTemplateModeReaches100PercentReady()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        Assert.Equal(ReadinessMode.Template, report.Mode);
        Assert.Equal(ReadinessMode.Template, report.SuggestedMode);
        Assert.True(report.IsReady);
        Assert.Equal(100, report.ScorePercent);
        Assert.Equal(0, report.FailCount);
        Assert.Equal(0, report.WarnCount);
        Assert.Equal(3, report.NotApplicableCount);
        Assert.False(report.CanPrepareTemplate);
        Assert.False(report.CanSetupModel);

        ReadinessCheckGroup projectGroup = Assert.Single(report.Groups, g => g.Id == "project-setup");
        Assert.Equal(ReadinessCheckStatus.NotApplicable, projectGroup.Status);
        Assert.All(projectGroup.Items, item => Assert.Equal(ReadinessCheckStatus.NotApplicable, item.Status));
    }

    [Fact]
    public void CleanTemplateInProjectModeFailsProjectOnlyChecks()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);

        Assert.Equal(ReadinessMode.Project, report.Mode);
        Assert.False(report.IsReady);
        Assert.Equal(3, report.FailCount);
        Assert.Equal(0, report.NotApplicableCount);

        ReadinessCheckGroup projectGroup = Assert.Single(report.Groups, g => g.Id == "project-setup");
        Assert.Equal(ReadinessCheckStatus.Fail, projectGroup.Status);
        Assert.All(projectGroup.Items, item => Assert.Equal(ReadinessCheckStatus.Fail, item.Status));
    }

    [Fact]
    public void CleanProjectInProjectModeReaches100PercentReady()
    {
        ReadinessCheckSnapshot snapshot = CleanProjectSnapshot();

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);

        Assert.Equal(ReadinessMode.Project, report.Mode);
        Assert.Equal(ReadinessMode.Project, report.SuggestedMode);
        Assert.True(report.IsReady);
        Assert.Equal(100, report.ScorePercent);
        Assert.Equal(0, report.FailCount);
        Assert.Equal(0, report.WarnCount);
        Assert.Equal(0, report.NotApplicableCount);
    }

    [Fact]
    public void ProjectModeFailsMissingCrsOrOutOfRangeEpsg()
    {
        ReadinessCheckSnapshot snapshot = CleanProjectSnapshot();
        snapshot.ProjectCrsEpsgCode = 4326;
        snapshot.ProjectCrsName = "WGS 84";

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);

        ReadinessCheckItem crs = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "project-crs");
        Assert.Equal(ReadinessCheckStatus.Fail, crs.Status);
        Assert.Contains("6669", crs.Detail);
    }

    [Fact]
    public void ProjectModeWarnsUnverifiedJapanCrs()
    {
        ReadinessCheckSnapshot snapshot = CleanProjectSnapshot();
        snapshot.ProjectCrsEpsgCode = 6669;
        snapshot.IsGeoreferenceVerified = false;

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);

        ReadinessCheckItem crs = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "project-crs");
        Assert.Equal(ReadinessCheckStatus.Warn, crs.Status);
        Assert.False(report.IsReady);
    }

    [Fact]
    public void ProjectModeFailsDuplicateLevelOrdinalAndMissingShortName()
    {
        ReadinessCheckSnapshot snapshot = CleanProjectSnapshot();
        snapshot.Levels = new[]
        {
            new LevelInfo(501, "1F North", ordinal: 0, shortName: "1F", hasFloors: true),
            new LevelInfo(502, "1F South", ordinal: 0, shortName: "", hasFloors: true),
        };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);

        ReadinessCheckItem levels = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "level-metadata");
        Assert.Equal(ReadinessCheckStatus.Fail, levels.Status);
        Assert.Contains("duplicate ordinal", levels.Detail);
        Assert.Contains("501", string.Join(",", levels.ElementIds));
        Assert.Contains("502", string.Join(",", levels.ElementIds));
    }

    [Fact]
    public void ProjectModeFailsWhenLoadedLinkLacksModelId()
    {
        ReadinessCheckSnapshot snapshot = CleanProjectSnapshot();
        snapshot.LinkedModels = new[]
        {
            new LinkedModelReadinessInfo("SouthAnnex.rvt", isLoaded: true, modelId: ""),
        };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);

        ReadinessCheckItem modelIds = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "model-ids");
        Assert.Equal(ReadinessCheckStatus.Fail, modelIds.Status);
        Assert.Contains("SouthAnnex.rvt", modelIds.Detail);
    }

    [Fact]
    public void MissingQaSchedulesAndViewTemplateAreReportedAndRemediableByPrepareTemplate()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.Schedules = new[]
        {
            new QaScheduleInfo(400, TemplateStandardDefinitions.FloorsScheduleName, new[] { GisSharedParameters.GisCategory }),
        };
        snapshot.ViewTemplateNames = Array.Empty<string>();

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        Assert.False(report.IsReady);
        Assert.True(report.CanPrepareTemplate);
        Assert.False(report.CanSetupModel);
        Assert.Equal(4, report.TemplatePlan.Schedules.Count);
        Assert.True(report.TemplatePlan.CreateExportPlanTemplate);

        QaSchedulePlanStep floorStep = Assert.Single(report.TemplatePlan.Schedules, s => s.Definition.Name == TemplateStandardDefinitions.FloorsScheduleName);
        Assert.False(floorStep.CreateSchedule);
        Assert.Contains(GisSharedParameters.GisRestriction, floorStep.MissingFields);
        Assert.Contains(GisSharedParameters.Category, floorStep.MissingFields);

        IReadOnlyList<string> lines = TemplateStandardPlanner.Describe(report.TemplatePlan);
        Assert.Contains(lines, line => line.Contains(TemplateStandardDefinitions.ExportPlanViewTemplateName));
        Assert.Contains(lines, line => line.Contains(TemplateStandardDefinitions.DoorsScheduleName));
    }

    [Fact]
    public void TemplateStandardPlannerIsIdempotentWhenAllAssetsExist()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();

        TemplatePreparationPlan plan = TemplateStandardPlanner.Plan(snapshot.Schedules, snapshot.ViewTemplateNames);

        Assert.True(plan.IsEmpty);
        Assert.Empty(TemplateStandardPlanner.Describe(plan));
    }

    [Fact]
    public void BadRfaGisKindAndMissingDoorParametersFailFamilyValidation()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.LoadedFamilies = new[]
        {
            new LoadedFamilyCheckInfo(310, "CustomLift", "TypeA", "OST_GenericModel", gisKind: "lift", hasGisKindParameter: true, instanceIds: new[] { 901L }, sourcePath: @"C:\Families\CustomLift.rfa"),
            new LoadedFamilyCheckInfo(311, "j EV", "Mismatch", "OST_MechanicalEquipment", gisKind: "escalator", hasGisKindParameter: true, sourcePath: @"C:\Families\j EV.rfa"),
            new LoadedFamilyCheckInfo(312, "OldDoor", "D1", "OST_Doors", hasGisDoorParameter: false, hasGisAccessConParameter: false, sourcePath: @"C:\Families\OldDoor.rfa"),
        };

        IReadOnlyList<FamilyValidationItem> items = FamilyStandardValidator.ValidateFamilies(snapshot.LoadedFamilies);
        Assert.Equal(3, items.Count);
        Assert.All(items, item => Assert.Equal(ReadinessCheckStatus.Fail, item.Status));

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);
        ReadinessCheckGroup familiesGroup = Assert.Single(report.Groups, g => g.Id == "families");
        Assert.Equal(ReadinessCheckStatus.Fail, familiesGroup.Status);
        Assert.Equal(2, familiesGroup.FailCount);
    }

    [Fact]
    public void RtePathSuggestsTemplateModeEvenWhenNonEmpty()
    {
        ReadinessCheckSnapshot snapshot = CleanProjectSnapshot();
        snapshot.DocumentPath = @"C:\Templates\Master2024.rte";
        snapshot.IsEmptyModel = false;

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot);

        Assert.Equal(ReadinessMode.Template, report.SuggestedMode);
        Assert.Equal(ReadinessMode.Template, report.Mode);
    }
}
