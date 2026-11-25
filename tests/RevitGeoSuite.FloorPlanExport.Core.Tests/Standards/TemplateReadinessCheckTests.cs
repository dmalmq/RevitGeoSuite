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
                .Select((s, idx) => new QaScheduleInfo(400 + idx, s.Name, s.RequiredFields, s.CategoryName))
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
        Assert.Contains(501L, levels.ElementIds);
        Assert.Contains(502L, levels.ElementIds);
    }

    [Fact]
    public void ProjectModeFailsDuplicateLevelShortNameCaseInsensitively()
    {
        ReadinessCheckSnapshot snapshot = CleanProjectSnapshot();
        snapshot.Levels = new[]
        {
            new LevelInfo(501, "1F North", ordinal: 0, shortName: " 1f ", hasFloors: true),
            new LevelInfo(502, "1F South", ordinal: 1, shortName: "1F", hasFloors: true),
        };

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Project);

        ReadinessCheckItem levels = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "level-metadata");
        Assert.Equal(ReadinessCheckStatus.Fail, levels.Status);
        Assert.Contains("duplicate short_name", levels.Detail);
        Assert.Contains(501L, levels.ElementIds);
        Assert.Contains(502L, levels.ElementIds);
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
            new QaScheduleInfo(400, TemplateStandardDefinitions.FloorsScheduleName, new[] { GisSharedParameters.GisCategory }, "OST_Floors"),
        };
        snapshot.ViewTemplateNames = Array.Empty<string>();

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        Assert.False(report.IsReady);
        Assert.True(report.CanPrepareTemplate);
        Assert.False(report.CanSetupModel);
        Assert.Equal(3, report.TemplatePlan.Schedules.Count);
        Assert.True(report.TemplatePlan.CreateExportPlanTemplate);

        QaSchedulePlanStep floorStep = Assert.Single(report.TemplatePlan.Schedules, s => s.Definition.Name == TemplateStandardDefinitions.FloorsScheduleName);
        Assert.False(floorStep.CreateSchedule);
        Assert.False(floorStep.CategoryMismatch);
        Assert.Contains(GisSharedParameters.GisRestriction, floorStep.MissingFields);
        Assert.Contains(GisSharedParameters.Category, floorStep.MissingFields);

        IReadOnlyList<string> lines = TemplateStandardPlanner.Describe(report.TemplatePlan);
        Assert.Contains(lines, line => line.Contains(TemplateStandardDefinitions.ExportPlanViewTemplateName));
        Assert.Contains(lines, line => line.Contains(TemplateStandardDefinitions.DoorsScheduleName));
    }

    [Fact]
    public void ExistingScheduleWithWrongCategoryFailsReadinessAndIsFlagged()
    {
        ReadinessCheckSnapshot snapshot = CleanTemplateSnapshot();
        snapshot.Schedules = TemplateStandardDefinitions.QaSchedules
            .Select((s, idx) => new QaScheduleInfo(
                400 + idx,
                s.Name,
                s.RequiredFields,
                s.Name == TemplateStandardDefinitions.FloorsScheduleName ? string.Empty : s.CategoryName))
            .ToList();

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(snapshot, ReadinessMode.Template);

        Assert.False(report.IsReady);
        Assert.False(report.CanPrepareTemplate);
        ReadinessCheckItem floorSchedule = report.Groups.SelectMany(g => g.Items).Single(i => i.Id == "qa-schedule-floors");
        Assert.Equal(ReadinessCheckStatus.Fail, floorSchedule.Status);
        Assert.Equal(ReadinessActionKind.None, floorSchedule.Action);
        Assert.Contains("Multi-Category", floorSchedule.Detail);
        Assert.Contains("OST_Floors", floorSchedule.Detail);
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
    public void BadRfaGisKindWrongGuidAndInstanceBindingFailFamilyValidation()
    {
        GisToolStandard standard = GisToolStandard.LoadEmbedded();
        Guid gisKindGuid = standard.Parameter(GisSharedParameters.GisKind)!.Guid;
        Guid gisDoorGuid = standard.Parameter(GisSharedParameters.GisDoor)!.Guid;

        LoadedFamilyCheckInfo[] families =
        {
            new LoadedFamilyCheckInfo(
                310,
                "CustomLift",
                "TypeA",
                "OST_GenericModel",
                gisKind: "lift",
                hasGisKindParameter: true,
                instanceIds: new[] { 901L },
                sourcePath: @"C:\Families\CustomLift.rfa",
                parameterBindings: new Dictionary<string, FamilyParameterBindingInfo>
                {
                    [GisSharedParameters.GisKind] = new(GisSharedParameters.GisKind, isShared: true, guid: gisKindGuid, isInstance: false),
                }),
            new LoadedFamilyCheckInfo(
                311,
                "j EV",
                "WrongGuid",
                "OST_MechanicalEquipment",
                gisKind: "elevator",
                hasGisKindParameter: true,
                sourcePath: @"C:\Families\j EV.rfa",
                parameterBindings: new Dictionary<string, FamilyParameterBindingInfo>
                {
                    [GisSharedParameters.GisKind] = new(GisSharedParameters.GisKind, isShared: true, guid: Guid.NewGuid(), isInstance: false),
                }),
            new LoadedFamilyCheckInfo(
                312,
                "j 自動改札",
                "InstanceBound",
                "OST_SpecialityEquipment",
                gisKind: "ticket_gate",
                hasGisKindParameter: true,
                sourcePath: @"C:\Families\j Gate.rfa",
                parameterBindings: new Dictionary<string, FamilyParameterBindingInfo>
                {
                    [GisSharedParameters.GisKind] = new(GisSharedParameters.GisKind, isShared: true, guid: gisKindGuid, isInstance: true),
                }),
            new LoadedFamilyCheckInfo(
                313,
                "NonSharedDoor",
                "D1",
                "OST_Doors",
                hasGisDoorParameter: true,
                hasGisAccessConParameter: false,
                sourcePath: @"C:\Families\NonSharedDoor.rfa",
                parameterBindings: new Dictionary<string, FamilyParameterBindingInfo>
                {
                    [GisSharedParameters.GisDoor] = new(GisSharedParameters.GisDoor, isShared: false, guid: null, isInstance: false),
                }),
            new LoadedFamilyCheckInfo(
                314,
                "InstanceDoor",
                "D2",
                "OST_Doors",
                hasGisDoorParameter: true,
                hasGisAccessConParameter: false,
                sourcePath: @"C:\Families\InstanceDoor.rfa",
                parameterBindings: new Dictionary<string, FamilyParameterBindingInfo>
                {
                    [GisSharedParameters.GisDoor] = new(GisSharedParameters.GisDoor, isShared: true, guid: gisDoorGuid, isInstance: true),
                }),
        };

        IReadOnlyList<FamilyValidationItem> items = FamilyStandardValidator.ValidateFamilies(families, standard);
        Assert.Equal(5, items.Count);
        Assert.All(items, item => Assert.Equal(ReadinessCheckStatus.Fail, item.Status));
        Assert.Contains("GUID", items[1].Detail);
        Assert.Contains("instance", items[2].Detail);
        Assert.Contains("non-shared", items[3].Detail);
        Assert.Contains("instance", items[4].Detail);
    }

    [Fact]
    public void ExternalDoorRfaWithoutEmbeddedDoorParametersPassesBecauseProjectSuppliesBindings()
    {
        LoadedFamilyCheckInfo externalDoor = new(
            0,
            "PlainStationDoor",
            "Sliding_1800",
            "OST_Doors",
            hasGisDoorParameter: false,
            hasGisAccessConParameter: false,
            sourcePath: @"C:\Families\PlainStationDoor.rfa");

        FamilyValidationItem result = Assert.Single(FamilyStandardValidator.ValidateFamilies(new[] { externalDoor }));
        Assert.Equal(ReadinessCheckStatus.Pass, result.Status);
        Assert.Contains("OST_Doors", result.Detail);
    }

    [Fact]
    public void RvtPathSuggestsProjectModeEvenWhenEmptyWhileRteSuggestsTemplateMode()
    {
        ReadinessCheckSnapshot rvt = CleanProjectSnapshot();
        rvt.DocumentPath = @"D:\Projects\LinkedHost.rvt";
        rvt.IsEmptyModel = true;
        Assert.Equal(ReadinessMode.Project, ProjectReadinessEvaluator.Evaluate(rvt).SuggestedMode);

        ReadinessCheckSnapshot rte = CleanProjectSnapshot();
        rte.DocumentPath = @"C:\Templates\Master2024.rte";
        rte.IsEmptyModel = false;
        Assert.Equal(ReadinessMode.Template, ProjectReadinessEvaluator.Evaluate(rte).SuggestedMode);
    }
}
