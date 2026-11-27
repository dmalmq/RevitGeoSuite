using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.StarterKit;

public sealed class T9NewKanizuProjectTests
{
    [Fact]
    public void Planner_ConsecutiveCallsAlwaysMintDistinctModelIdsAndLevelIds()
    {
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        NewKanizuProjectOptions options = new()
        {
            StationName = "Tokyo Station",
            StationCode = "TYO",
            BuildingSlug = "Main Concourse",
            EnableWorksharing = true,
            TemplatePath = "KanizuStationStarter.rte",
        };

        NewKanizuProjectPlan first = NewKanizuProjectPlanner.Plan(options, existingDocument: null, kitSpec: spec);
        NewKanizuProjectPlan second = NewKanizuProjectPlanner.Plan(options, existingDocument: null, kitSpec: spec);

        Assert.True(first.IsValid, string.Join("; ", first.Errors.Select(e => e.ToString())));
        Assert.True(second.IsValid, string.Join("; ", second.Errors.Select(e => e.ToString())));

        // Fresh RFC-4122 GUIDs, strictly distinct across runs (zero baked/shared identity).
        Assert.True(Guid.TryParse(first.ModelId, out Guid firstGuid));
        Assert.True(Guid.TryParse(second.ModelId, out Guid secondGuid));
        Assert.NotEqual(Guid.Empty, firstGuid);
        Assert.NotEqual(Guid.Empty, secondGuid);
        Assert.NotEqual(first.ModelId, second.ModelId, StringComparer.OrdinalIgnoreCase);

        // Default levels: B1F (-1, -4.5m), 1F (0, 0.0m), 2F (1, 4.5m)
        Assert.Equal(3, first.Levels.Count);
        Assert.Equal(new[] { "B1F", "1F", "2F" }, first.Levels.Select(l => l.Name).ToArray());
        Assert.Equal(new[] { -1, 0, 1 }, first.Levels.Select(l => l.Ordinal).ToArray());

        // Every level_id is unique within a plan and strictly distinct between consecutive calls.
        HashSet<string> allIds = new(StringComparer.OrdinalIgnoreCase) { first.ModelId, second.ModelId };
        foreach (PlannedKanizuLevel level in first.Levels.Concat(second.Levels))
        {
            Assert.True(Guid.TryParse(level.LevelId, out Guid lvlGuid));
            Assert.NotEqual(Guid.Empty, lvlGuid);
            Assert.True(allIds.Add(level.LevelId), $"Duplicate identity minted: {level.LevelId}");
        }

        // Canonical stamps on ProjectInformation
        Assert.Equal(ShowcaseExportGuard.AuthoringModelRole, first.ModelRole);
        Assert.Equal("authoring", first.ProjectInfoStamps[GisSharedParameters.GisModelRole]);
        Assert.Equal(spec.Header.KitVersion, first.ProjectInfoStamps[GisSharedParameters.GisKitVersion]);
        Assert.Equal("3", first.ProjectInfoStamps[GisSharedParameters.GisStandardVersion]);
        Assert.Equal("main-concourse", first.BuildingSlug);
        Assert.Equal("tyo-main-concourse", first.ProjectKey);

        // Per-level views planned cleanly (3 views x 3 levels = 9 views)
        Assert.Equal(9, first.PlannedViews.Count);
        Assert.Contains(first.PlannedViews, v => v.ViewName == "GIS_1F" && v.ViewTemplateName == "GIS Export");
        Assert.Contains(first.PlannedViews, v => v.ViewName == "WORK_1F" && v.ViewTemplateName == "GIS Working Plan");
        Assert.Contains(first.PlannedViews, v => v.ViewName == "GIS Area - 1F" && v.AreaSchemeName == "GIS Area");
    }

    [Theory]
    [InlineData("showcase", "T9-ROLE-001")]
    [InlineData("SHOWCASE", "T9-ROLE-001")]
    [InlineData("  Showcase  ", "T9-ROLE-001")]
    [InlineData("invalid_role", "T9-ROLE-002")]
    public void Planner_LocksModelRoleToAuthoringAndRejectsShowcaseOrInvalidRoles(string requestedRole, string expectedCode)
    {
        NewKanizuProjectOptions options = new()
        {
            StationName = "Shinjuku",
            ModelRole = requestedRole,
        };

        NewKanizuProjectPlan plan = NewKanizuProjectPlanner.Plan(options);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Code == expectedCode);
        Assert.False(plan.PreFlightReadiness.IsReady);
        Assert.Equal(ShowcaseExportGuard.AuthoringModelRole, plan.ModelRole);
    }

    [Fact]
    public void Planner_WorksharingEnabledCreatesFiveStandardWorksetsAndNonHidingQaFilters()
    {
        NewKanizuProjectOptions options = new()
        {
            StationName = "Shibuya",
            EnableWorksharing = true,
        };

        NewKanizuProjectPlan plan = NewKanizuProjectPlanner.Plan(options);

        Assert.True(plan.IsValid, string.Join("; ", plan.Errors.Select(e => e.ToString())));
        Assert.True(plan.WorksetPlan.EnableWorksharing);
        Assert.Equal(NewKanizuProjectPlanner.DefaultSharedLevelsWorksetName, plan.WorksetPlan.DefaultSharedLevelsWorksetName);
        Assert.Equal(KanizuWorksetSpec.GisAreasName, plan.WorksetPlan.DefaultActiveWorksetName);

        Assert.Equal(
            new[]
            {
                "KNZ_GIS_Areas",
                "KNZ_Circulation",
                "KNZ_Structure",
                "KNZ_Fixtures",
                "KNZ_Links",
            },
            plan.WorksetPlan.Worksets.Select(w => w.Name).ToArray());

        // Every workset MUST stay visible in GIS Export views (KZ-VIEW-004).
        Assert.All(plan.WorksetPlan.Worksets, w => Assert.True(w.VisibleInGisExportViews));

        // Workset filters MUST be visible=true and NEVER target canonical "GIS Export".
        Assert.Equal(5, plan.WorksetPlan.WorksetViewFilters.Count);
        Assert.All(plan.WorksetPlan.WorksetViewFilters, filter =>
        {
            Assert.True(filter.Visible);
            Assert.StartsWith("KNZ_WS_", filter.FilterName, StringComparison.Ordinal);
            Assert.DoesNotContain(TemplateStandardDefinitions.ExportPlanViewTemplateName, filter.TargetViewTemplateNames);
            Assert.Contains("GIS Working Plan", filter.TargetViewTemplateNames);
            Assert.Contains("GIS QA Plan", filter.TargetViewTemplateNames);
            Assert.Contains("GIS QA 3D", filter.TargetViewTemplateNames);
        });
    }

    [Fact]
    public void Planner_NonWorksharedFallbackProducesZeroWorksetsAndBilingualGuidance()
    {
        NewKanizuProjectOptions options = new()
        {
            StationName = "Ueno",
            EnableWorksharing = false,
        };

        NewKanizuProjectPlan plan = NewKanizuProjectPlanner.Plan(options);

        Assert.True(plan.IsValid);
        Assert.False(plan.WorksetPlan.EnableWorksharing);
        Assert.Empty(plan.WorksetPlan.Worksets);
        Assert.Empty(plan.WorksetPlan.WorksetViewFilters);
        Assert.Contains("zero worksets", plan.WorksetPlan.FallbackNoteEn, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ワークセット", plan.WorksetPlan.FallbackNoteJa, StringComparison.Ordinal);
        Assert.True(plan.ExpectedPostInitReadiness.IsReady);
    }

    [Fact]
    public void Planner_RejectsHiddenGisWorksetToProtectKzView004()
    {
        NewKanizuProjectOptions options = new()
        {
            StationName = "Shinagawa",
            EnableWorksharing = true,
            Worksets = new[]
            {
                new KanizuWorksetSpec(
                    "KNZ_GIS_Areas",
                    "Areas",
                    "エリア",
                    new[] { "OST_Floors" },
                    visibleInGisExportViews: false),
            },
        };

        NewKanizuProjectPlan plan = NewKanizuProjectPlanner.Plan(options);

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Code == "T9-WS-003");
    }

    [Fact]
    public void Planner_ValidatesLevelsDuplicatesNonFiniteMissingOrdinalsAndMonotonicOrdering()
    {
        // Duplicate names
        NewKanizuProjectPlan dupName = NewKanizuProjectPlanner.Plan(new NewKanizuProjectOptions
        {
            StationName = "Ikebukuro",
            Levels = new[]
            {
                new NewKanizuLevelSpec("1F", 0, 0.0, "1F"),
                new NewKanizuLevelSpec("1f", 1, 4.5, "1F_alt"),
            },
        });
        Assert.False(dupName.IsValid);
        Assert.Contains(dupName.Errors, e => e.Code == "T9-LVL-003");

        // NaN and Infinity elevations (net48-safe IsFiniteDouble check)
        Assert.False(NewKanizuProjectPlanner.IsFiniteDouble(double.NaN));
        Assert.False(NewKanizuProjectPlanner.IsFiniteDouble(double.PositiveInfinity));
        Assert.False(NewKanizuProjectPlanner.IsFiniteDouble(double.NegativeInfinity));
        Assert.True(NewKanizuProjectPlanner.IsFiniteDouble(-12.5));

        NewKanizuProjectPlan nonFinite = NewKanizuProjectPlanner.Plan(new NewKanizuProjectOptions
        {
            StationName = "Ikebukuro",
            Levels = new[]
            {
                new NewKanizuLevelSpec("B1F", -1, double.NaN),
                new NewKanizuLevelSpec("1F", 0, double.PositiveInfinity),
                new NewKanizuLevelSpec("2F", 1, double.NegativeInfinity),
            },
        });
        Assert.False(nonFinite.IsValid);
        Assert.Equal(3, nonFinite.Errors.Count(e => e.Code == "T9-LVL-005"));

        // Missing ordinal
        NewKanizuProjectPlan missingOrdinal = NewKanizuProjectPlanner.Plan(new NewKanizuProjectOptions
        {
            StationName = "Ikebukuro",
            Levels = new[]
            {
                new NewKanizuLevelSpec("1F", ordinal: null, elevationMeters: 0.0),
            },
        });
        Assert.False(missingOrdinal.IsValid);
        Assert.Contains(missingOrdinal.Errors, e => e.Code == "T9-LVL-007");

        // Non-monotonic elevation (2F lower than 1F)
        NewKanizuProjectPlan invertedElev = NewKanizuProjectPlanner.Plan(new NewKanizuProjectOptions
        {
            StationName = "Ikebukuro",
            Levels = new[]
            {
                new NewKanizuLevelSpec("1F", 0, 4.0),
                new NewKanizuLevelSpec("2F", 1, 2.0),
            },
        });
        Assert.False(invertedElev.IsValid);
        Assert.Contains(invertedElev.Errors, e => e.Code == "T9-LVL-009");

        // Empty level list explicitly passed
        NewKanizuProjectPlan emptyLevels = NewKanizuProjectPlanner.Plan(new NewKanizuProjectOptions
        {
            StationName = "Ikebukuro",
            Levels = Array.Empty<NewKanizuLevelSpec>(),
        });
        Assert.False(emptyLevels.IsValid);
        Assert.Contains(emptyLevels.Errors, e => e.Code == "T9-LVL-001");

        // Deep negative basement ordinals (-3, -2, -1, 0, 1) sorted cleanly even when passed out of order!
        NewKanizuProjectPlan deepBasements = NewKanizuProjectPlanner.Plan(new NewKanizuProjectOptions
        {
            StationName = "Otemachi",
            Levels = new[]
            {
                new NewKanizuLevelSpec("2F", 1, 5.0, "2F"),
                new NewKanizuLevelSpec("B3F", -3, -15.5, "B3F"),
                new NewKanizuLevelSpec("1F", 0, 0.0, "1F"),
                new NewKanizuLevelSpec("B1F", -1, -5.0, "B1F"),
                new NewKanizuLevelSpec("B2F", -2, -10.2, "B2F"),
            },
        });
        Assert.True(deepBasements.IsValid, string.Join("; ", deepBasements.Errors.Select(e => e.ToString())));
        Assert.Equal(new[] { -3, -2, -1, 0, 1 }, deepBasements.Levels.Select(l => l.Ordinal).ToArray());
        Assert.Equal(new[] { "B3F", "B2F", "B1F", "1F", "2F" }, deepBasements.Levels.Select(l => l.Name).ToArray());
    }

    [Fact]
    public void StaleIdentityDetector_FlagsCopiedModelIdDuplicateLevelIdsAndRequiresExplicitClearOptIn()
    {
        string copiedModelId = "11111111-2222-4333-8444-555555555555";
        string dupLevelId = "aaaaaaaa-bbbb-4ccc-8ddd-eeeeeeeeeeee";

        StaleIdentitySnapshot copiedDoc = new()
        {
            DocumentTitle = "CopiedStation.rvt",
            ExistingModelId = copiedModelId,
            ExistingStationFolder = @"\\share\stations\old-station",
            ExistingModelRole = "authoring",
            KnownOtherProjectModelIds = new[] { copiedModelId },
            ExistingLevels = new[]
            {
                new ExistingLevelIdentityEntry(101, "1F", dupLevelId, 0, "1F", 0.0),
                new ExistingLevelIdentityEntry(102, "2F", dupLevelId, 1, "2F", 4.5),
            },
            ElementsWithIdentityCount = 42,
            ElementsWithIdentitySample = new long[] { 1001, 1002 },
        };

        StaleIdentityReport report = NewKanizuProjectPlanner.InspectStaleIdentity(copiedDoc);
        Assert.True(report.HasStaleIdentity);
        Assert.True(report.HasCopiedModelIdCollision);
        Assert.True(report.HasDuplicateLevelIds);
        Assert.False(report.IsShowcaseModel);
        Assert.Contains(report.Findings, f => f.Kind == StaleIdentityIssueKind.CopiedModelIdCollision);
        Assert.Contains(report.Findings, f => f.Kind == StaleIdentityIssueKind.DuplicateLevelId);
        Assert.Contains(report.Findings, f => f.Kind == StaleIdentityIssueKind.ExistingStationFolder);
        Assert.Contains(report.Findings, f => f.Kind == StaleIdentityIssueKind.ExistingElementIdentity);

        // Without ClearStaleIdentity = true, planner blocks initialization!
        NewKanizuProjectPlan blocked = NewKanizuProjectPlanner.Plan(
            new NewKanizuProjectOptions
            {
                StationName = "Akihabara",
                InitializationMode = NewKanizuProjectInitializationMode.InitializeOpenDocument,
                ClearStaleIdentity = false,
            },
            existingDocument: copiedDoc);
        Assert.False(blocked.IsValid);
        Assert.Contains(blocked.Errors, e => e.Code == "T9-STALE-001");
        Assert.False(blocked.ShouldClearStaleIdentity);

        // With explicit ClearStaleIdentity = true, planner succeeds, marks ShouldClearStaleIdentity = true,
        // and guarantees the newly minted ModelId and level_ids never match any stale ID!
        NewKanizuProjectPlan cleared = NewKanizuProjectPlanner.Plan(
            new NewKanizuProjectOptions
            {
                StationName = "Akihabara",
                InitializationMode = NewKanizuProjectInitializationMode.InitializeOpenDocument,
                ClearStaleIdentity = true,
            },
            existingDocument: copiedDoc);
        Assert.True(cleared.IsValid, string.Join("; ", cleared.Errors.Select(e => e.ToString())));
        Assert.True(cleared.ShouldClearStaleIdentity);
        Assert.NotEqual(copiedModelId, cleared.ModelId, StringComparer.OrdinalIgnoreCase);
        Assert.All(cleared.Levels, l => Assert.NotEqual(dupLevelId, l.LevelId, StringComparer.OrdinalIgnoreCase));

        // Showcase document MUST always be blocked even with ClearStaleIdentity = true!
        StaleIdentitySnapshot showcaseDoc = new()
        {
            DocumentTitle = "Tokyo_GIS_Showcase.rvt",
            ExistingModelRole = ShowcaseExportGuard.ShowcaseModelRole,
        };
        NewKanizuProjectPlan blockedShowcase = NewKanizuProjectPlanner.Plan(
            new NewKanizuProjectOptions
            {
                StationName = "Tokyo",
                InitializationMode = NewKanizuProjectInitializationMode.InitializeOpenDocument,
                ClearStaleIdentity = true,
            },
            existingDocument: showcaseDoc);
        Assert.False(blockedShowcase.IsValid);
        Assert.Contains(blockedShowcase.Errors, e => e.Code == "T9-STALE-SHOWCASE");
    }

    [Fact]
    public void Planner_ValidatesFederationModesAndRejectsSpokeWithoutHubModelId()
    {
        NewKanizuProjectPlan invalidSpoke = NewKanizuProjectPlanner.Plan(new NewKanizuProjectOptions
        {
            StationName = "Tokyo East Wing",
            FederationMode = KanizuFederationMode.Spoke,
            HubModelId = null,
        });
        Assert.False(invalidSpoke.IsValid);
        Assert.Contains(invalidSpoke.Errors, e => e.Code == "T9-FED-001");

        string hubId = "99999999-8888-4777-8666-555555555555";
        NewKanizuProjectPlan validSpoke = NewKanizuProjectPlanner.Plan(new NewKanizuProjectOptions
        {
            StationName = "Tokyo East Wing",
            FederationMode = KanizuFederationMode.Spoke,
            HubModelId = hubId,
        });
        Assert.True(validSpoke.IsValid, string.Join("; ", validSpoke.Errors.Select(e => e.ToString())));
        Assert.Equal(KanizuFederationMode.Spoke, validSpoke.FederationMode);
        Assert.Equal(hubId, validSpoke.HubModelId);
        Assert.NotEqual(hubId, validSpoke.ModelId, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluatePostInitReadiness_IntegratesWithTemplateReadinessCheckAndVerifiesAllInvariants()
    {
        GisToolStandard standard = GisToolStandard.LoadEmbedded();
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();

        NewKanizuProjectPlan plan = NewKanizuProjectPlanner.Plan(
            new NewKanizuProjectOptions
            {
                StationName = "Yokohama",
                StationCode = "YKH",
                EnableWorksharing = true,
            },
            kitSpec: spec);
        Assert.True(plan.IsValid);

        // Simulate a freshly initialized Starter Kit project snapshot (pre-georef or post-georef)
        ReadinessCheckSnapshot snapshot = new()
        {
            DocumentTitle = "Yokohama.rvt",
            DocumentPath = "Yokohama.rvt",
            IsEmptyModel = false,
            StandardVersionText = "3",
            BoundParameters = standard.Parameters
                .Select(p => new BoundParameterInfo(p.Name, p.Guid, p.Binding, p.Categories))
                .ToList(),
            Materials = standard.Materials
                .Select((m, idx) => new MaterialInfo(idx + 1, m.Name, m.FillHex))
                .ToList(),
            FloorTypes = standard.FloorTypes
                .Select((ft, idx) => new FloorTypeInfo(idx + 100, ft.Name, ft.GisCategory, ft.GisRestriction ?? string.Empty, ft.Material))
                .ToList(),
            Levels = plan.Levels
                .Select((l, idx) => new LevelInfo(idx + 1000, l.Name, l.Ordinal, l.ShortName, hasFloors: false))
                .ToList(),
            Schedules = TemplateStandardDefinitions.QaSchedules
                .Select((s, idx) => new QaScheduleInfo(idx + 2000, s.Name, s.RequiredFields, s.CategoryName))
                .ToList(),
            ViewTemplateNames = new[] { "GIS Export", "GIS Working Plan", "GIS QA Plan", "GIS QA 3D", "Kanizu 3D Export" },
            ExportPlanTemplateIssues = Array.Empty<string>(),
            ExportViewHideFilterNames = Array.Empty<string>(),
            ProjectCrsEpsgCode = 6677,
            ProjectCrsName = "JGD2011 / Japan Plane Rectangular CS IX",
            IsGeoreferenceVerified = true,
            HostModelId = plan.ModelId,
            HostModelRole = ShowcaseExportGuard.AuthoringModelRole,
        };

        List<ExistingLevelIdentityEntry> actualLevels = plan.Levels
            .Select((l, idx) => new ExistingLevelIdentityEntry(idx + 1000, l.Name, l.LevelId, l.Ordinal, l.ShortName, l.ElevationMeters))
            .ToList();

        ProjectInitializationReadinessSummary postInit = NewKanizuProjectPlanner.EvaluatePostInitReadiness(
            plan,
            snapshot,
            actualLevels,
            plan.KitVersion,
            standard);

        Assert.True(postInit.IsReady);
        Assert.Equal(0, postInit.FailCount);
        Assert.NotNull(postInit.TemplateReadinessReport);
        Assert.True(postInit.TemplateReadinessReport!.IsReady);
        Assert.Equal(100, postInit.TemplateReadinessReport.ScorePercent);

        // If a hide filter were somehow present on GIS Export, post-init readiness MUST fail!
        snapshot.ExportViewHideFilterNames = new[] { "BadHideFilter" };
        ProjectInitializationReadinessSummary failedGuard = NewKanizuProjectPlanner.EvaluatePostInitReadiness(
            plan,
            snapshot,
            actualLevels,
            plan.KitVersion,
            standard);
        Assert.False(failedGuard.IsReady);
        Assert.Contains(failedGuard.Items, i => i.Code == "T9-POST-VIEWGUARD" && i.Status == ProjectInitializationCheckStatus.Fail);
    }

    [Fact]
    public void BilingualStringsOptionsJsonAndDialogLevelParser_WorkCleanlyInEnglishAndJapanese()
    {
        NewKanizuProjectOptions options = new()
        {
            StationName = "東京駅",
            StationCode = "TYO",
            BuildingSlug = "marunouchi",
            FederationMode = KanizuFederationMode.Hub,
            EnableWorksharing = true,
            TemplatePath = "KanizuStationStarter.rte",
            Language = NewKanizuProjectLanguage.Japanese,
        };

        NewKanizuProjectOptions roundTripped = NewKanizuProjectOptions.FromJson(options.ToJson());
        Assert.Equal("東京駅", roundTripped.StationName);
        Assert.Equal("TYO", roundTripped.StationCode);
        Assert.Equal("marunouchi", roundTripped.BuildingSlug);
        Assert.Equal(KanizuFederationMode.Hub, roundTripped.FederationMode);
        Assert.True(roundTripped.EnableWorksharing);
        Assert.Equal(NewKanizuProjectLanguage.Japanese, roundTripped.Language);

        NewKanizuProjectPlan plan = NewKanizuProjectPlanner.Plan(roundTripped);
        Assert.True(plan.IsValid);
        Assert.Contains("New Kanizu Project Plan", plan.SummaryEn, StringComparison.Ordinal);
        Assert.Contains("新規 Kanizu プロジェクト計画", plan.SummaryJa, StringComparison.Ordinal);
        Assert.Contains(plan.ModelId, plan.SummaryEn, StringComparison.Ordinal);
        Assert.Contains(plan.ModelId, plan.SummaryJa, StringComparison.Ordinal);
        Assert.Contains("kanizu.new-project-plan", plan.ToCanonicalJson(), StringComparison.Ordinal);

        // Bilingual UI strings present in both EN and JA
        Assert.NotEqual(
            NewKanizuProjectStrings.CommandTitle(NewKanizuProjectLanguage.English),
            NewKanizuProjectStrings.CommandTitle(NewKanizuProjectLanguage.Japanese));
        Assert.Contains("ModelId", NewKanizuProjectStrings.DialogSubtitle(NewKanizuProjectLanguage.English), StringComparison.Ordinal);
        Assert.Contains("ModelId", NewKanizuProjectStrings.DialogSubtitle(NewKanizuProjectLanguage.Japanese), StringComparison.Ordinal);

        // Completion body formatting
        string completionEn = NewKanizuProjectStrings.FormatCompletionDialogBody(
            NewKanizuProjectLanguage.English,
            plan.ModelId,
            plan.KitVersion,
            plan.GisStandardVersion,
            plan.Levels.Select(l => (l.Name, l.Ordinal, l.ElevationMeters, l.LevelId)).ToList(),
            plan.WorksetPlan.Worksets.Select(w => w.Name).ToList(),
            plan.PlannedViews.Select(v => v.ViewName).ToList(),
            postInitReady: true,
            warnings: Array.Empty<string>());
        string completionJa = NewKanizuProjectStrings.FormatCompletionDialogBody(
            NewKanizuProjectLanguage.Japanese,
            plan.ModelId,
            plan.KitVersion,
            plan.GisStandardVersion,
            plan.Levels.Select(l => (l.Name, l.Ordinal, l.ElevationMeters, l.LevelId)).ToList(),
            plan.WorksetPlan.Worksets.Select(w => w.Name).ToList(),
            plan.PlannedViews.Select(v => v.ViewName).ToList(),
            postInitReady: true,
            warnings: Array.Empty<string>());
        Assert.Contains("READY", completionEn, StringComparison.Ordinal);
        Assert.Contains("合格 (READY)", completionJa, StringComparison.Ordinal);

        // Dialog level editor parser round-trip
        string formatted = NewKanizuProjectPlanner.FormatLevelsText(NewKanizuProjectOptions.DefaultLevels);
        Assert.True(NewKanizuProjectPlanner.TryParseLevelsText(formatted, NewKanizuProjectLanguage.English, out List<NewKanizuLevelSpec> parsed, out string? error));
        Assert.Null(error);
        Assert.Equal(3, parsed.Count);
        Assert.Equal(-1, parsed[0].Ordinal);
        Assert.Equal(-4.5, parsed[0].ElevationMeters, precision: 3);

        // Invalid editor input returns localized error
        Assert.False(NewKanizuProjectPlanner.TryParseLevelsText("1F, notAnInt, 0.0", NewKanizuProjectLanguage.Japanese, out _, out string? jaError));
        Assert.Contains("整数", jaError!, StringComparison.Ordinal);
    }
}
