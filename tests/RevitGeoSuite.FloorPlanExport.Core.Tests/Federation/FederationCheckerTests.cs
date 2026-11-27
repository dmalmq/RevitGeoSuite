using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.Core.Text;
using RevitGeoSuite.FloorPlanExport.Core.Federation;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.Federation;

public sealed class FederationCheckerTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "kanizu-fed-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void KzFed001_ThreeControlPointPose_PassesEvenWhenSurveyPointsDifferIntentionally()
    {
        // Simulate a host and a linked building whose Survey Points / Internal Origins sit at completely
        // different offsets and rotations, yet both map the same 3 physical points (0,0,0), (100m,0,0),
        // (0,100m,0) to identical real-world shared CRS coordinates (EPSG:6677 Easting/Northing/T.P. Z).
        double hostE0 = -12450.250;
        double hostN0 = -35120.750;
        double hostZ0 = 37.710;
        double bearingRad = 23.5 * Math.PI / 180.0; // Link rotated 23.5 deg relative to True North
        double cosB = Math.Cos(bearingRad);
        double sinB = Math.Sin(bearingRad);

        // Even with sub-millimeter rounding noise (< 5 mm tolerance), pose check MUST pass.
        ControlPointPair[] controlPoints =
        {
            new("Origin (0,0,0)",
                new FederationPoint3D(hostE0, hostN0, hostZ0),
                new FederationPoint3D(hostE0 + 0.0004, hostN0 - 0.0003, hostZ0 + 0.0002)),
            new("+X Baseline (100m,0,0)",
                new FederationPoint3D(hostE0 + (100.0 * cosB), hostN0 + (100.0 * sinB), hostZ0),
                new FederationPoint3D(hostE0 + (100.0 * cosB) + 0.0003, hostN0 + (100.0 * sinB) - 0.0002, hostZ0)),
            new("+Y Baseline (0,100m,0)",
                new FederationPoint3D(hostE0 - (100.0 * sinB), hostN0 + (100.0 * cosB), hostZ0),
                new FederationPoint3D(hostE0 - (100.0 * sinB), hostN0 + (100.0 * cosB) + 0.0004, hostZ0 - 0.0001)),
        };

        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot(controlPoints);
        FederationCheckReport report = FederationChecker.Evaluate(snapshot);

        FederationRuleResult fed001 = report.GetRule(FederationChecker.RuleCrsAndPose);
        Assert.Equal(ReadinessCheckStatus.Pass, fed001.Status);
        Assert.Empty(fed001.Issues);
        LinkPoseCheckDetail poseDetail = Assert.Single(report.PoseResults);
        Assert.True(poseDetail.Pose.IsEvaluated);
        Assert.True(poseDetail.Pose.IsAligned);
        Assert.True(poseDetail.Pose.MaxHorizontalTranslationMm < 1.0);
        Assert.True(poseDetail.Pose.MaxVerticalDriftMm < 1.0);
        Assert.True(poseDetail.Pose.RotationErrorArcSeconds < 2.0);
        Assert.True(report.IsPassed);
    }

    [Fact]
    public void KzFed001_ThreeControlPointPose_FailsOnTranslationDriftAndVerticalDriftWithExactMetrics()
    {
        // Shift host shared coordinates by +30 mm East, +40 mm North (50 mm horizontal), and +22 mm vertical Z.
        ControlPointPair[] drifted =
        {
            new("Origin", new FederationPoint3D(1000.030, 2000.040, 10.022), new FederationPoint3D(1000.000, 2000.000, 10.000)),
            new("+X", new FederationPoint3D(1100.030, 2000.040, 10.022), new FederationPoint3D(1100.000, 2000.000, 10.000)),
            new("+Y", new FederationPoint3D(1000.030, 2100.040, 10.022), new FederationPoint3D(1000.000, 2100.000, 10.000)),
        };

        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot(drifted);
        FederationCheckReport report = FederationChecker.Evaluate(snapshot);

        FederationRuleResult fed001 = report.GetRule(FederationChecker.RuleCrsAndPose);
        Assert.Equal(ReadinessCheckStatus.Fail, fed001.Status);
        Assert.Contains("50", fed001.SummaryEn);
        Assert.Contains("22", fed001.SummaryEn);
        Assert.False(string.IsNullOrWhiteSpace(fed001.SummaryJa));

        LinkPoseCheckDetail poseDetail = Assert.Single(report.PoseResults);
        Assert.False(poseDetail.Pose.IsAligned);
        Assert.InRange(poseDetail.Pose.MaxHorizontalTranslationMm, 49.9, 50.1);
        Assert.InRange(poseDetail.Pose.MaxVerticalDriftMm, 21.9, 22.1);
        Assert.InRange(poseDetail.Pose.RotationErrorArcSeconds, 0.0, 0.01);
    }

    [Fact]
    public void KzFed001_ThreeControlPointPose_FailsOnTrueNorthRotationErrorEvenWhenOriginMatches()
    {
        // Origin matches identically (0 mm), but True North is rotated by 0.05 degrees (180 arcsec)
        // -> 87.3 mm displacement at the 100 m baseline ends!
        double errorDeg = 0.05;
        double rad = errorDeg * Math.PI / 180.0;
        double cosE = Math.Cos(rad);
        double sinE = Math.Sin(rad);

        ControlPointPair[] rotated =
        {
            new("Origin", new FederationPoint3D(500.0, 500.0, 0.0), new FederationPoint3D(500.0, 500.0, 0.0)),
            new("+X", new FederationPoint3D(500.0 + (100.0 * cosE), 500.0 + (100.0 * sinE), 0.0), new FederationPoint3D(600.0, 500.0, 0.0)),
            new("+Y", new FederationPoint3D(500.0 - (100.0 * sinE), 500.0 + (100.0 * cosE), 0.0), new FederationPoint3D(500.0, 600.0, 0.0)),
        };

        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot(rotated);
        FederationCheckReport report = FederationChecker.Evaluate(snapshot);

        FederationRuleResult fed001 = report.GetRule(FederationChecker.RuleCrsAndPose);
        Assert.Equal(ReadinessCheckStatus.Fail, fed001.Status);
        Assert.Contains("arcsec", fed001.SummaryEn);

        LinkPoseCheckDetail poseDetail = Assert.Single(report.PoseResults);
        Assert.False(poseDetail.Pose.IsAligned);
        Assert.InRange(poseDetail.Pose.OriginTranslationMm, 0.0, 0.001);
        Assert.InRange(poseDetail.Pose.RotationErrorDegrees, 0.0499, 0.0501);
        Assert.InRange(poseDetail.Pose.RotationErrorArcSeconds, 179.9, 180.1);
        Assert.InRange(poseDetail.Pose.MaxHorizontalTranslationMm, 87.0, 87.5);
    }

    [Fact]
    public void KzFed001_ThreeControlPointPose_FailsOnScaleSkewAndCollinearPoints()
    {
        ControlPointPair[] scaled =
        {
            new("Origin", new FederationPoint3D(0.0, 0.0, 0.0), new FederationPoint3D(0.0, 0.0, 0.0)),
            new("+X", new FederationPoint3D(100.02, 0.0, 0.0), new FederationPoint3D(100.0, 0.0, 0.0)),
            new("+Y", new FederationPoint3D(0.0, 100.02, 0.0), new FederationPoint3D(0.0, 100.0, 0.0)),
        };

        PoseConsistencyResult scaleResult = PoseConsistencyEvaluator.Evaluate(scaled);
        Assert.False(scaleResult.IsAligned);
        Assert.InRange(scaleResult.MaxScaleRatioErrorPpm, 199.5, 200.5);

        ControlPointPair[] collinear =
        {
            new("P0", new FederationPoint3D(0.0, 0.0, 0.0), new FederationPoint3D(0.0, 0.0, 0.0)),
            new("P1", new FederationPoint3D(50.0, 0.0, 0.0), new FederationPoint3D(50.0, 0.0, 0.0)),
            new("P2", new FederationPoint3D(100.0, 0.0, 0.0), new FederationPoint3D(100.0, 0.0, 0.0)),
        };

        PoseConsistencyResult collinearResult = PoseConsistencyEvaluator.Evaluate(collinear);
        Assert.False(collinearResult.IsAligned);
        Assert.True(collinearResult.IsDegenerateBaseline);
    }

    [Fact]
    public void KzFed001_FailsOnCrsEpsgHeightDatumOrGeoidModelMismatch()
    {
        FederationCheckSnapshot crsMismatch = CreateCleanHubSnapshot();
        crsMismatch.LinkedModels = new[]
        {
            BuildLink("EastWing.rvt", "east-002", crsEpsg: 6678, heightDatum: "TP", geoidModel: "EGM2008"),
        };
        FederationRuleResult crsRule = FederationChecker.Evaluate(crsMismatch).GetRule(FederationChecker.RuleCrsAndPose);
        Assert.Equal(ReadinessCheckStatus.Fail, crsRule.Status);
        Assert.Contains("LINK-CRS-MISMATCH", crsRule.Issues.Select(i => i.Code));

        FederationCheckSnapshot datumMismatch = CreateCleanHubSnapshot();
        datumMismatch.LinkedModels = new[]
        {
            BuildLink("EastWing.rvt", "east-002", crsEpsg: 6677, heightDatum: "unknown", geoidModel: "EGM2008"),
        };
        FederationRuleResult datumRule = FederationChecker.Evaluate(datumMismatch).GetRule(FederationChecker.RuleCrsAndPose);
        Assert.Equal(ReadinessCheckStatus.Fail, datumRule.Status);
        Assert.Contains("LINK-HEIGHT-DATUM-MISMATCH", datumRule.Issues.Select(i => i.Code));

        FederationCheckSnapshot geoidMismatch = CreateCleanHubSnapshot();
        geoidMismatch.LinkedModels = new[]
        {
            BuildLink("EastWing.rvt", "east-002", crsEpsg: 6677, heightDatum: "TP", geoidModel: "typed"),
        };
        FederationRuleResult geoidRule = FederationChecker.Evaluate(geoidMismatch).GetRule(FederationChecker.RuleCrsAndPose);
        Assert.Equal(ReadinessCheckStatus.Fail, geoidRule.Status);
        Assert.Contains("LINK-GEOID-MODEL-MISMATCH", geoidRule.Issues.Select(i => i.Code));
    }

    [Fact]
    public void KzFed002_FailsOnMissingOrDuplicateModelIdAcrossHostAndLinks()
    {
        // Duplicate ModelId between host and link (Save-As clone)
        FederationCheckSnapshot cloned = CreateCleanHubSnapshot();
        cloned.LinkedModels = new[]
        {
            BuildLink("EastWing_SaveAs.rvt", modelId: cloned.HostModelId, sourcePath: @"D:\Station\EastWing_SaveAs.rvt"),
        };

        FederationRuleResult dupRule = FederationChecker.Evaluate(cloned).GetRule(FederationChecker.RuleUniqueModelId);
        Assert.Equal(ReadinessCheckStatus.Fail, dupRule.Status);
        Assert.Contains("DUPLICATE-MODEL-ID", dupRule.Issues.Select(i => i.Code));

        // Missing link ModelId
        FederationCheckSnapshot missingLinkId = CreateCleanHubSnapshot();
        missingLinkId.LinkedModels = new[]
        {
            BuildLink("NoIdWing.rvt", modelId: ""),
        };
        FederationRuleResult missingRule = FederationChecker.Evaluate(missingLinkId).GetRule(FederationChecker.RuleUniqueModelId);
        Assert.Equal(ReadinessCheckStatus.Fail, missingRule.Status);
        Assert.Contains("LINK-MODEL-ID-MISSING", missingRule.Issues.Select(i => i.Code));

        // Same .rvt file placed twice as two link instances does NOT false-fail as Save-As clone
        FederationCheckSnapshot sameFileTwice = CreateCleanHubSnapshot();
        sameFileTwice.LinkedModels = new[]
        {
            BuildLink("WingA.rvt : 1", modelId: "wing-a-id", linkElementId: 801, linkUniqueId: "uid-1", sourcePath: @"D:\Station\WingA.rvt"),
            BuildLink("WingA.rvt : 2", modelId: "wing-a-id", linkElementId: 802, linkUniqueId: "uid-2", sourcePath: @"D:\Station\WingA.rvt"),
        };
        FederationRuleResult sameFileRule = FederationChecker.Evaluate(sameFileTwice).GetRule(FederationChecker.RuleUniqueModelId);
        Assert.Equal(ReadinessCheckStatus.Pass, sameFileRule.Status);
    }

    [Fact]
    public void KzFed003_FailsOnDuplicateLevelIdWithinModelOrAcrossDistinctModels()
    {
        // Cross-model duplicate level_id
        FederationCheckSnapshot crossDup = CreateCleanHubSnapshot();
        crossDup.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(601, "l-e-1", "1F", levelId: "host-level-1f", ordinal: 0, shortName: "1F", projectElevationMeters: 0.0, isExportLevel: true),
                }),
        };
        FederationRuleResult crossRule = FederationChecker.Evaluate(crossDup).GetRule(FederationChecker.RuleUniqueLevelId);
        Assert.Equal(ReadinessCheckStatus.Fail, crossRule.Status);
        Assert.Contains("DUPLICATE-LEVEL-ID-CROSS", crossRule.Issues.Select(i => i.Code));

        // Opt-in AllowSharedCanonicalLevelIdsAcrossModels allows aligned canonical level_id
        crossDup.Options = new FederationCheckOptions { AllowSharedCanonicalLevelIdsAcrossModels = true };
        Assert.Equal(ReadinessCheckStatus.Pass, FederationChecker.Evaluate(crossDup).GetRule(FederationChecker.RuleUniqueLevelId).Status);

        // Intra-model duplicate level_id always fails
        FederationCheckSnapshot intraDup = CreateCleanHubSnapshot();
        intraDup.HostLevels = new[]
        {
            new FederationLevelSnapshot(501, "h-1", "1F", levelId: "dup-id", ordinal: 0, shortName: "1F", projectElevationMeters: 0.0, isExportLevel: true),
            new FederationLevelSnapshot(502, "h-2", "2F", levelId: "dup-id", ordinal: 1, shortName: "2F", projectElevationMeters: 4.5, isExportLevel: true),
        };
        FederationRuleResult intraRule = FederationChecker.Evaluate(intraDup).GetRule(FederationChecker.RuleUniqueLevelId);
        Assert.Equal(ReadinessCheckStatus.Fail, intraRule.Status);
        Assert.Contains("DUPLICATE-LEVEL-ID-INTRA", intraRule.Issues.Select(i => i.Code));
    }

    [Theory]
    [InlineData(0.0, true)]     // 0 mm -> aligned PASS
    [InlineData(10.0, true)]    // 10 mm -> aligned PASS
    [InlineData(14.9, true)]    // 14.9 mm -> aligned PASS
    [InlineData(15.0, false)]   // 15 mm exact boundary -> FAIL near-miss
    [InlineData(35.0, false)]   // 35 mm (> UnitExtractor 30.5 mm tolerance!) -> FAIL near-miss
    [InlineData(150.0, false)]  // 150 mm exact boundary -> FAIL near-miss
    [InlineData(200.0, true)]   // 200 mm -> distinct floor/mezzanine PASS
    public void KzFed004_EnforcesExact15To150MmNearMissBandAtRealWorldTpElevations(double deltaMm, bool expectedPass)
    {
        // Use a realistic Tokyo T.P. elevation (37.710 m) to also test IEEE-754 floating-point boundary precision!
        const double hostTpElevationMeters = 37.710;
        double linkTpElevationMeters = hostTpElevationMeters + (deltaMm / 1000.0);

        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.HostLevels = new[]
        {
            new FederationLevelSnapshot(501, "h-1", "1F", levelId: "host-1f", ordinal: 0, shortName: "1F", projectElevationMeters: hostTpElevationMeters, isExportLevel: true),
        };
        string linkLevelName = deltaMm > 150.0 ? "M1F" : "1F";
        int linkOrdinal = deltaMm > 150.0 ? 1 : 0;
        string linkShortName = deltaMm > 150.0 ? "M1F" : "1F";
        snapshot.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(601, "l-1", linkLevelName, levelId: "east-1f", ordinal: linkOrdinal, shortName: linkShortName, projectElevationMeters: linkTpElevationMeters, isExportLevel: true),
                }),
        };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);
        if (expectedPass)
        {
            Assert.Equal(ReadinessCheckStatus.Pass, rule.Status);
        }
        else
        {
            Assert.Equal(ReadinessCheckStatus.Fail, rule.Status);
            FederationIssue issue = Assert.Single(rule.Issues, i => i.Code == "NEAR-MISS-LEVEL-DRIFT");
            Assert.InRange(issue.Metrics["deltaZMm"], deltaMm - 0.01, deltaMm + 0.01);
        }
    }

    [Fact]
    public void KzFed004_ComparesHostAgainstIndependentStationBuildingsOnce()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.HostLevels = new[]
        {
            new FederationLevelSnapshot(501, "h-1", "1F", levelId: "host-1f", ordinal: 0, shortName: "1F", projectElevationMeters: 37.710, isExportLevel: true),
        };
        snapshot.LinkedModels = Array.Empty<FederationLinkSnapshot>();
        FederationLevelSnapshot[] hostMirror =
        {
            new FederationLevelSnapshot(0, "h-1", "1F", levelId: "host-1f", ordinal: 0, shortName: "1F", projectElevationMeters: 37.710, isExportLevel: true),
        };
        snapshot.StationFolder = new StationFolderSnapshot(
            @"D:\Station",
            new[]
            {
                new StationFolderModelEntry("hub-001", "TokyoHub", crsEpsg: 6677, heightDatum: "TP", levels: hostMirror),
                new StationFolderModelEntry(
                    "west-003",
                    "WestBuilding",
                    crsEpsg: 6677,
                    heightDatum: "TP",
                    levels: new[]
                    {
                        new FederationLevelSnapshot(0, "w-1", "1F", levelId: "west-1f", ordinal: 0, shortName: "1F", projectElevationMeters: 37.745, isExportLevel: true),
                    }),
            });

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);

        Assert.Equal(ReadinessCheckStatus.Fail, rule.Status);
        FederationIssue issue = Assert.Single(rule.Issues);
        Assert.Equal("NEAR-MISS-LEVEL-DRIFT", issue.Code);
        Assert.Contains("TokyoHub.rvt", new[] { issue.ModelA, issue.ModelB });
        Assert.Contains("WestBuilding", new[] { issue.ModelA, issue.ModelB });
    }

    [Fact]
    public void KzFed004_IgnoresStructuralSlabLevelWithLevelIdThatNoExportViewUses()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.HostLevels = new[]
        {
            new FederationLevelSnapshot(501, "h-1", "1FL", levelId: "arch-1fl", ordinal: 0, shortName: "1F", projectElevationMeters: 0.000, isExportLevel: true),
            new FederationLevelSnapshot(502, "h-2", "2FL", levelId: "arch-2fl", ordinal: 1, shortName: "2F", projectElevationMeters: 4.000, isExportLevel: true),
        };
        snapshot.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(601, "e-1", "1FL", levelId: "east-1fl", ordinal: 0, shortName: "1F", projectElevationMeters: 0.000, isExportLevel: true),
                    new FederationLevelSnapshot(602, "e-2s", "2SL", levelId: "east-2sl", ordinal: null, shortName: "2S", projectElevationMeters: 3.900, hasFloors: true),
                    new FederationLevelSnapshot(603, "e-2", "2FL", levelId: "east-2fl", ordinal: 1, shortName: "2F", projectElevationMeters: 4.000, isExportLevel: true),
                }),
        };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);

        Assert.Equal(ReadinessCheckStatus.Pass, rule.Status);
    }

    [Fact]
    public void KzFed004_WarnsInsteadOfPassingForALinkWithoutExportLevels()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.HostLevels = new[]
        {
            new FederationLevelSnapshot(502, "h-2", "2FL", levelId: "arch-2fl", ordinal: 1, shortName: "2F", projectElevationMeters: 4.000, isExportLevel: true),
        };
        snapshot.LinkedModels = new[]
        {
            BuildLink(
                "Structure.rvt",
                "struct-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(602, "s-2", "2SL", levelId: "struct-2sl", ordinal: 1, shortName: "2S", projectElevationMeters: 3.900, hasFloors: true),
                }),
        };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);

        Assert.Equal(ReadinessCheckStatus.Warn, rule.Status);
        FederationIssue issue = Assert.Single(rule.Issues);
        Assert.Equal("LEVEL-DRIFT-NOT-CHECKED", issue.Code);
        Assert.Contains("Structure.rvt", issue.MessageEn);
    }

    [Fact]
    public void KzFed004_PassesAnExportLevelWithoutACounterpartBelowASharedFloor()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.HostLevels = new[]
        {
            new FederationLevelSnapshot(500, "h-0", "GL", levelId: "arch-gl", ordinal: 0, shortName: "GL", projectElevationMeters: 0.000, isExportLevel: true),
            new FederationLevelSnapshot(501, "h-1", "1FL", levelId: "arch-1fl", ordinal: 1, shortName: "1F", projectElevationMeters: 0.150, isExportLevel: true),
        };
        snapshot.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(601, "e-1", "1FL", levelId: "east-1fl", ordinal: 1, shortName: "1F", projectElevationMeters: 0.150, isExportLevel: true),
                }),
        };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);

        Assert.Equal(ReadinessCheckStatus.Pass, rule.Status);
    }

    [Fact]
    public void KzFed004_FailsWhenBothNearLevelsMissTheirPartner()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.HostLevels = new[]
        {
            new FederationLevelSnapshot(501, "h-1", "1FL", levelId: "arch-1fl", ordinal: 0, shortName: "1F", projectElevationMeters: 0.000, isExportLevel: true),
            new FederationLevelSnapshot(502, "h-2", "2FL", levelId: "arch-2fl", ordinal: 1, shortName: "2F", projectElevationMeters: 4.000, isExportLevel: true),
        };
        snapshot.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(601, "e-1", "1FL", levelId: "east-1fl", ordinal: 0, shortName: "1F", projectElevationMeters: 0.000, isExportLevel: true),
                    new FederationLevelSnapshot(602, "e-2", "2FL", levelId: "east-2fl", ordinal: 1, shortName: "2F", projectElevationMeters: 3.950, isExportLevel: true),
                }),
        };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);

        Assert.Equal(ReadinessCheckStatus.Fail, rule.Status);
        FederationIssue issue = Assert.Single(rule.Issues);
        Assert.Equal("NEAR-MISS-LEVEL-DRIFT", issue.Code);
        Assert.Equal(50d, issue.Metrics["deltaZMm"], 6);
    }

    [Fact]
    public void KzFed004_IgnoresGroundLevelWithLevelIdThatNoExportViewUses()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.HostLevels = new[]
        {
            new FederationLevelSnapshot(500, "h-0", "GL", levelId: "arch-gl", ordinal: null, shortName: "GL", projectElevationMeters: 0.000, hasFloors: true),
            new FederationLevelSnapshot(501, "h-1", "1FL", levelId: "arch-1fl", ordinal: 0, shortName: "1F", projectElevationMeters: 0.150, isExportLevel: true),
        };
        snapshot.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(600, "l-0", "GL", levelId: "east-gl", ordinal: null, shortName: "GL", projectElevationMeters: 0.000, hasFloors: true),
                    new FederationLevelSnapshot(601, "l-1", "1FL", levelId: "east-1fl", ordinal: 0, shortName: "1F", projectElevationMeters: 0.150, isExportLevel: true),
                }),
        };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);

        Assert.Equal(ReadinessCheckStatus.Pass, rule.Status);
    }

    [Fact]
    public void KzFed004_SkipsLinksTheHubExportLeavesOut()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.HostLevels = new[]
        {
            new FederationLevelSnapshot(502, "h-2", "2FL", levelId: "arch-2fl", ordinal: 1, shortName: "2F", projectElevationMeters: 4.000, isExportLevel: true),
        };
        snapshot.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(602, "l-2", "2F", levelId: "east-2f", ordinal: 1, shortName: "2F", projectElevationMeters: 3.900, isExportLevel: true),
                },
                isIncludedInHubExport: false),
        };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);

        Assert.Equal(ReadinessCheckStatus.Pass, rule.Status);
    }

    [Fact]
    public void KzFed004_StillFailsFloorLevelsOneHundredMmApart()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.HostLevels = new[]
        {
            new FederationLevelSnapshot(502, "h-2", "2FL", levelId: "host-2fl", ordinal: 1, shortName: "2F", projectElevationMeters: 4.000, isExportLevel: true),
        };
        snapshot.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(602, "l-2", "2F", levelId: "east-2f", ordinal: 1, shortName: "2F", projectElevationMeters: 3.900, isExportLevel: true),
                }),
        };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);

        Assert.Equal(ReadinessCheckStatus.Fail, rule.Status);
    }

    [Fact]
    public void KzFed004_FailsWhenAlignedLevelsHaveMismatchedOrdinalOrShortName()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        snapshot.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(601, "l-1", "1F", levelId: "east-1f", ordinal: 1, shortName: "G1", projectElevationMeters: 0.005, isExportLevel: true),
                }),
        };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleNearMissLevelDrift);
        Assert.Equal(ReadinessCheckStatus.Fail, rule.Status);
        Assert.Contains("ALIGNED-LEVEL-METADATA-MISMATCH", rule.Issues.Select(i => i.Code));
    }

    [Theory]
    [InlineData("1F", "1f")]
    [InlineData("1F-North", "1f_north")]
    [InlineData("1F North", "1f_north")]
    [InlineData("  B1F / Track-2  ", "b1f_track_2")]
    [InlineData("Unassigned", "unassigned")]
    [InlineData("---", "unassigned")]
    [InlineData("", "unassigned")]
    public void LevelKeyNormalizer_MatchesTiles3DLevelGrouperRules(string input, string expectedKey)
    {
        Assert.Equal(expectedKey, LevelKey.Build(input));
    }

    [Fact]
    public void KzFed005_FailsOnCrossLinkAndIntraModelNormalizedLevelKeyCollisions()
    {
        FederationCheckSnapshot punctuationCollision = CreateCleanHubSnapshot();
        punctuationCollision.HostLevels = new[]
        {
            new FederationLevelSnapshot(501, "h-1", "1F-North", levelId: "host-1f-n", ordinal: 0, shortName: "1F", projectElevationMeters: 0.0, isExportLevel: true),
        };
        punctuationCollision.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(601, "l-1", "1F North", levelId: "east-1f-n", ordinal: 0, shortName: "1F", projectElevationMeters: 0.0, isExportLevel: true),
                }),
        };
        FederationRuleResult punctRule = FederationChecker.Evaluate(punctuationCollision).GetRule(FederationChecker.RuleTilesLevelKeyCollision);
        Assert.Equal(ReadinessCheckStatus.Fail, punctRule.Status);
        Assert.Contains("1f_north", punctRule.SummaryEn);

        FederationCheckSnapshot elevCollision = CreateCleanHubSnapshot();
        elevCollision.HostLevels = new[]
        {
            new FederationLevelSnapshot(501, "h-1", "1F", levelId: "host-1f", ordinal: 0, shortName: "1F", projectElevationMeters: 0.0, isExportLevel: true),
        };
        elevCollision.LinkedModels = new[]
        {
            BuildLink(
                "EastWing.rvt",
                "east-002",
                levels: new[]
                {
                    new FederationLevelSnapshot(601, "l-1", "1F", levelId: "east-1f", ordinal: 0, shortName: "1F", projectElevationMeters: 3.5, isExportLevel: true),
                }),
        };
        FederationRuleResult elevRule = FederationChecker.Evaluate(elevCollision).GetRule(FederationChecker.RuleTilesLevelKeyCollision);
        Assert.Equal(ReadinessCheckStatus.Fail, elevRule.Status);
        Assert.Contains("TILES-KEY-COLLISION-CROSS", elevRule.Issues.Select(i => i.Code));

        FederationCheckSnapshot intraCollision = CreateCleanHubSnapshot();
        intraCollision.HostLevels = new[]
        {
            new FederationLevelSnapshot(501, "h-1", "1F-A", levelId: "host-1fa", ordinal: 0, shortName: "1FA", projectElevationMeters: 0.0, isExportLevel: true),
            new FederationLevelSnapshot(502, "h-2", "1F A", levelId: "host-1fb", ordinal: 1, shortName: "1FB", projectElevationMeters: 4.0, isExportLevel: true),
        };
        FederationRuleResult intraRule = FederationChecker.Evaluate(intraCollision).GetRule(FederationChecker.RuleTilesLevelKeyCollision);
        Assert.Equal(ReadinessCheckStatus.Fail, intraRule.Status);
        Assert.Contains("TILES-KEY-COLLISION-INTRA", intraRule.Issues.Select(i => i.Code));
    }

    [Fact]
    public void KzFed006_StationFolderCheck_PassesCleanMultiBuildingIndependentStationFolder()
    {
        string station = Path.Combine(_tempDir, "clean-station");
        WriteStationBuilding(
            station,
            modelId: "11111111-0000-4000-8000-000000000001",
            buildingName: "NorthBuilding",
            sourceModelFile: "NorthBuilding.rvt",
            documentKey: "doc-north",
            crsEpsg: 6677,
            heightDatum: "TP",
            geoidModel: "EGM2008",
            geoidOffset: 36.70,
            federationMode: KanizuFederationMode.Independent,
            levels: new[]
            {
                ("u-n-1", "1F", "lvl-north-1f", 0, "1F", 10.000),
                ("u-n-2", "2F", "lvl-north-2f", 1, "2F", 14.500),
            });
        WriteStationBuilding(
            station,
            modelId: "22222222-0000-4000-8000-000000000002",
            buildingName: "SouthBuilding",
            sourceModelFile: "SouthBuilding.rvt",
            documentKey: "doc-south",
            crsEpsg: 6677,
            heightDatum: "TP",
            geoidModel: "EGM2008",
            geoidOffset: 36.71,
            federationMode: KanizuFederationMode.Independent,
            levels: new[]
            {
                ("u-s-1", "1F", "lvl-south-1f", 0, "1F", 10.005), // 5 mm -> aligned PASS
                ("u-s-2", "2F", "lvl-south-2f", 1, "2F", 14.500),
            });

        FederationCheckReport report = StationFolderFederationCheck.EvaluateFolder(station);

        Assert.True(report.IsPassed, report.ToJson());
        Assert.Equal(ReadinessCheckStatus.Pass, report.GetRule(FederationChecker.RuleStationFolderFederation).Status);
        Assert.Equal(ReadinessCheckStatus.Pass, report.GetRule(FederationChecker.RuleNoDoublePublishing).Status);
    }

    [Fact]
    public void KzFed004_StationFolderCheck_WarnsForABuildingWhoseGisModelPredatesViewLevels()
    {
        string station = Path.Combine(_tempDir, "old-gis-model-station");
        Directory.CreateDirectory(station);
        WriteStationBuilding(
            station,
            "arch-01",
            "Arch",
            "Arch.rvt",
            "doc-arch",
            6677,
            "TP",
            "EGM2008",
            36.70,
            KanizuFederationMode.Independent,
            levels: new[] { ("u-a-2", "2FL", "arch-2fl", 2, "2F", 4.000) });
        WriteStationBuilding(
            station,
            "annex-02",
            "Annex",
            "Annex.rvt",
            "doc-annex",
            6677,
            "TP",
            "EGM2008",
            36.70,
            KanizuFederationMode.Independent,
            levels: new[] { ("u-n-2", "2FL", "annex-2fl", 2, "2F", 3.900) },
            legacyViewsWithoutLevels: true);

        FederationRuleResult rule = StationFolderFederationCheck.EvaluateFolder(station).GetRule(FederationChecker.RuleNearMissLevelDrift);

        Assert.Equal(ReadinessCheckStatus.Warn, rule.Status);
        FederationIssue issue = Assert.Single(rule.Issues);
        Assert.Equal("LEVEL-DRIFT-NOT-CHECKED", issue.Code);
        Assert.Contains("Annex", issue.MessageEn);
    }

    [Fact]
    public void KzFed006_StationFolderCheck_IgnoresLevelsNoExportViewUses()
    {
        string station = Path.Combine(_tempDir, "datum-levels-station");
        Directory.CreateDirectory(station);
        WriteStationBuilding(
            station,
            "arch-01",
            "Arch",
            "Arch.rvt",
            "doc-arch",
            6677,
            "TP",
            "EGM2008",
            36.70,
            KanizuFederationMode.Independent,
            levels: new[] { ("u-a-2", "2FL", "arch-2fl", 2, "2F", 4.000) });
        WriteStationBuilding(
            station,
            "annex-02",
            "Annex",
            "Annex.rvt",
            "doc-annex",
            6677,
            "TP",
            "EGM2008",
            36.70,
            KanizuFederationMode.Independent,
            levels: new[]
            {
                ("u-n-2s", "2SL", "annex-2sl", 1, "2S", 3.900),
                ("u-n-2", "2FL", "annex-2fl", 2, "2F", 4.000),
            },
            levelsWithoutExportView: new[] { "u-n-2s" });

        FederationCheckReport report = StationFolderFederationCheck.EvaluateFolder(station);

        Assert.True(report.GetRule(FederationChecker.RuleStationFolderFederation).Status == ReadinessCheckStatus.Pass, report.ToJson());
        Assert.Equal(ReadinessCheckStatus.Pass, report.GetRule(FederationChecker.RuleNearMissLevelDrift).Status);
    }

    [Theory]
    [InlineData(3.000, true)]
    [InlineData(0.035, false)]
    public void KzFed006_StationFolderCheck_ReadsLevelsJsonElevationsRelativeToEachPackageAnchor(double southAnchorMeters, bool expectedPass)
    {
        string station = Path.Combine(_tempDir, "legacy-station-" + southAnchorMeters.ToString(System.Globalization.CultureInfo.InvariantCulture));
        WriteStationBuilding(
            station,
            modelId: "11111111-0000-4000-8000-000000000001",
            buildingName: "NorthBuilding",
            sourceModelFile: "NorthBuilding.rvt",
            documentKey: "doc-north",
            crsEpsg: 6677,
            heightDatum: "TP",
            geoidModel: "EGM2008",
            geoidOffset: 36.70,
            federationMode: KanizuFederationMode.Independent,
            levels: new[] { ("u-n-1", "1F", "lvl-north-1f", 0, "1F", 0.0) },
            anchorOrthometricMeters: 0.0,
            legacyGisModelWithoutElevations: true);
        WriteStationBuilding(
            station,
            modelId: "22222222-0000-4000-8000-000000000002",
            buildingName: "SouthBuilding",
            sourceModelFile: "SouthBuilding.rvt",
            documentKey: "doc-south",
            crsEpsg: 6677,
            heightDatum: "TP",
            geoidModel: "EGM2008",
            geoidOffset: 36.70,
            federationMode: KanizuFederationMode.Independent,
            levels: new[] { ("u-s-1", "Ground", "lvl-south-g", 0, "G", southAnchorMeters) },
            anchorOrthometricMeters: southAnchorMeters,
            legacyGisModelWithoutElevations: true);

        FederationCheckReport report = StationFolderFederationCheck.EvaluateFolder(station);

        Assert.Equal(
            expectedPass ? ReadinessCheckStatus.Pass : ReadinessCheckStatus.Fail,
            report.GetRule(FederationChecker.RuleNearMissLevelDrift).Status);
    }

    [Fact]
    public void KzFed006_StationFolderCheck_CatchesCrsDatumDriftDuplicateLevelIdAndKeyCollisions()
    {
        string station = Path.Combine(_tempDir, "broken-station");
        WriteStationBuilding(
            station,
            modelId: "11111111-0000-4000-8000-000000000001",
            buildingName: "NorthBuilding",
            sourceModelFile: "NorthBuilding.rvt",
            documentKey: "doc-north",
            crsEpsg: 6677,
            heightDatum: "TP",
            geoidModel: "EGM2008",
            geoidOffset: 36.70,
            federationMode: KanizuFederationMode.Independent,
            levels: new[]
            {
                ("u-n-1", "1F-Main", "shared-bad-lvl-id", 0, "1F", 10.000),
            });
        WriteStationBuilding(
            station,
            modelId: "22222222-0000-4000-8000-000000000002",
            buildingName: "SouthBuilding",
            sourceModelFile: "SouthBuilding.rvt",
            documentKey: "doc-south",
            crsEpsg: 6678,          // CRS mismatch!
            heightDatum: "unknown", // Datum mismatch!
            geoidModel: "typed",    // Geoid mismatch!
            geoidOffset: 37.50,     // Geoid offset mismatch!
            federationMode: KanizuFederationMode.Independent,
            levels: new[]
            {
                ("u-s-1", "1F Main", "shared-bad-lvl-id", 0, "1F", 10.040), // 40 mm near-miss + dup level_id + key collision!
            });

        FederationCheckReport report = StationFolderFederationCheck.EvaluateFolder(station);

        Assert.True(report.HasBlockingFailures);
        Assert.Equal(ReadinessCheckStatus.Fail, report.GetRule(FederationChecker.RuleCrsAndPose).Status);
        Assert.Equal(ReadinessCheckStatus.Fail, report.GetRule(FederationChecker.RuleUniqueLevelId).Status);
        Assert.Equal(ReadinessCheckStatus.Fail, report.GetRule(FederationChecker.RuleNearMissLevelDrift).Status);
        Assert.Equal(ReadinessCheckStatus.Fail, report.GetRule(FederationChecker.RuleTilesLevelKeyCollision).Status);

        FederationRuleResult fed006 = report.GetRule(FederationChecker.RuleStationFolderFederation);
        Assert.Equal(ReadinessCheckStatus.Fail, fed006.Status);
        HashSet<string> codes = new(fed006.Issues.Select(i => i.Code));
        Assert.Contains("STATION-CRS-MISMATCH", codes);
        Assert.Contains("STATION-HEIGHT-DATUM-MISMATCH", codes);
        Assert.Contains("STATION-GEOID-MODEL-MISMATCH", codes);
        Assert.Contains("DUPLICATE-LEVEL-ID-CROSS", codes);
        Assert.Contains("NEAR-MISS-LEVEL-DRIFT", codes);
        Assert.Contains("TILES-KEY-COLLISION-CROSS", codes);
    }

    [Fact]
    public void KzFed007_FailsWhenBuildingPublishedBothInHubAndStandaloneOrInTwoHubs()
    {
        string stationOverlap = Path.Combine(_tempDir, "double-publish-station");
        WriteStationBuilding(
            stationOverlap,
            modelId: "hub-0001",
            buildingName: "MainHub",
            sourceModelFile: "MainHub.rvt",
            documentKey: "doc-hub",
            crsEpsg: 6677,
            heightDatum: "TP",
            geoidModel: "EGM2008",
            geoidOffset: 36.70,
            federationMode: KanizuFederationMode.Hub,
            includedLinks: new[] { ("link-uid-east", "EastWing.rvt : 1 : location Shared", "east-0002") },
            levels: new[] { ("u-h-1", "1F", "lvl-hub-1f", 0, "1F", 0.0) });
        WriteStationBuilding(
            stationOverlap,
            modelId: "east-0002",
            buildingName: "EastWing",
            sourceModelFile: "EastWing.rvt",
            documentKey: "doc-east",
            crsEpsg: 6677,
            heightDatum: "TP",
            geoidModel: "EGM2008",
            geoidOffset: 36.70,
            federationMode: KanizuFederationMode.Spoke,
            hubModelId: "hub-0001",
            levels: new[] { ("u-e-1", "1F", "lvl-east-1f", 0, "1F", 0.0) });

        FederationCheckReport overlapReport = StationFolderFederationCheck.EvaluateFolder(stationOverlap);
        FederationRuleResult fed007Overlap = overlapReport.GetRule(FederationChecker.RuleNoDoublePublishing);
        Assert.Equal(ReadinessCheckStatus.Fail, fed007Overlap.Status);
        Assert.Contains("DOUBLE-PUBLISH-HUB-AND-STANDALONE", fed007Overlap.Issues.Select(i => i.Code));

        StationFolderSnapshot twoHubs = new(
            "station",
            new[]
            {
                new StationFolderModelEntry(
                    modelId: "hub-a",
                    modelName: "HubA",
                    sourceModelFile: "HubA.rvt",
                    federationMode: KanizuFederationMode.Hub,
                    crsEpsg: 6677,
                    hasPublishedPackage: true,
                    includedLinks: new[] { new StationFolderPublishedLink("uid-1", "SharedPlatform.rvt : 1", "plat-99") }),
                new StationFolderModelEntry(
                    modelId: "hub-b",
                    modelName: "HubB",
                    sourceModelFile: "HubB.rvt",
                    federationMode: KanizuFederationMode.Hub,
                    crsEpsg: 6677,
                    hasPublishedPackage: true,
                    includedLinks: new[] { new StationFolderPublishedLink("uid-2", "SharedPlatform.rvt : 2", "plat-99") }),
            });
        FederationRuleResult fed007TwoHubs = StationFolderFederationCheck.Evaluate(twoHubs).GetRule(FederationChecker.RuleNoDoublePublishing);
        Assert.Equal(ReadinessCheckStatus.Fail, fed007TwoHubs.Status);
        Assert.Contains("DOUBLE-PUBLISH-OVERLAPPING-HUBS", fed007TwoHubs.Issues.Select(i => i.Code));

        StationFolderSnapshot cleanHubOnly = new(
            "station",
            new[]
            {
                new StationFolderModelEntry(
                    modelId: "hub-a",
                    modelName: "HubA",
                    sourceModelFile: "HubA.rvt",
                    federationMode: KanizuFederationMode.Hub,
                    crsEpsg: 6677,
                    hasPublishedPackage: true,
                    includedLinks: new[] { new StationFolderPublishedLink("uid-1", "EastWing.rvt : 1", "east-002") }),
                new StationFolderModelEntry(
                    modelId: "east-002",
                    modelName: "EastWing",
                    sourceModelFile: "EastWing.rvt",
                    federationMode: KanizuFederationMode.Spoke,
                    hubModelId: "hub-a",
                    crsEpsg: 6677,
                    hasPublishedPackage: false,
                    hasGisModelSettings: true),
            });
        Assert.Equal(
            ReadinessCheckStatus.Pass,
            StationFolderFederationCheck.Evaluate(cleanHubOnly).GetRule(FederationChecker.RuleNoDoublePublishing).Status);
    }

    [Fact]
    public void KzFed008_WarnsUnloadedAuthoringLinksExcludesShowcaseAndFailsRoleContradictions()
    {
        FederationCheckSnapshot unloaded = CreateCleanHubSnapshot();
        unloaded.LinkedModels = new[]
        {
            new FederationLinkSnapshot(701, "uid-unloaded", "WestWing.rvt", isLoaded: false),
        };
        FederationRuleResult warnRule = FederationChecker.Evaluate(unloaded).GetRule(FederationChecker.RuleLinkAndRoleHygiene);
        Assert.Equal(ReadinessCheckStatus.Warn, warnRule.Status);
        Assert.Contains("UNLOADED-LINK", warnRule.Issues.Select(i => i.Code));

        FederationCheckSnapshot unloadedShowcase = CreateCleanHubSnapshot();
        unloadedShowcase.LinkedModels = new[]
        {
            new FederationLinkSnapshot(702, "uid-sc", "KNZ_FRM_ShowcaseCompanion.rvt", isLoaded: false, modelRole: "showcase"),
        };
        FederationRuleResult showcaseUnloadedRule = FederationChecker.Evaluate(unloadedShowcase).GetRule(FederationChecker.RuleLinkAndRoleHygiene);
        Assert.Equal(ReadinessCheckStatus.Pass, showcaseUnloadedRule.Status);

        FederationCheckSnapshot selfSpoke = CreateCleanHubSnapshot();
        selfSpoke.HostFederationMode = KanizuFederationMode.Spoke;
        selfSpoke.HostHubModelId = selfSpoke.HostModelId;
        selfSpoke.LinkedModels = Array.Empty<FederationLinkSnapshot>();
        FederationRuleResult selfSpokeRule = FederationChecker.Evaluate(selfSpoke).GetRule(FederationChecker.RuleLinkAndRoleHygiene);
        Assert.Equal(ReadinessCheckStatus.Fail, selfSpokeRule.Status);
        Assert.Contains("SPOKE-SELF-HUB-ID", selfSpokeRule.Issues.Select(i => i.Code));

        FederationCheckSnapshot missingHub = CreateCleanHubSnapshot();
        missingHub.HostFederationMode = KanizuFederationMode.Spoke;
        missingHub.HostHubModelId = null;
        missingHub.LinkedModels = Array.Empty<FederationLinkSnapshot>();
        Assert.Contains(
            "SPOKE-MISSING-HUB-ID",
            FederationChecker.Evaluate(missingHub).GetRule(FederationChecker.RuleLinkAndRoleHygiene).Issues.Select(i => i.Code));

        StationFolderSnapshot standaloneTarget = new(
            "station",
            new[]
            {
                new StationFolderModelEntry("main-01", "Main", federationMode: KanizuFederationMode.Standalone, crsEpsg: 6677),
                new StationFolderModelEntry("spoke-02", "Wing", federationMode: KanizuFederationMode.Spoke, hubModelId: "main-01", crsEpsg: 6677),
            });
        Assert.Contains(
            "SPOKE-TARGET-NOT-HUB",
            StationFolderFederationCheck.Evaluate(standaloneTarget).GetRule(FederationChecker.RuleLinkAndRoleHygiene).Issues.Select(i => i.Code));
    }

    [Fact]
    public void KzFed009_FailsOnShowcaseLinkAndExcludesShowcaseFromAuthoringChecks()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        // Attach a showcase companion link with deliberately mismatched CRS, duplicate ModelId, and drifted level:
        // KZ-FED-009 must FAIL, while KZ-FED-001..005 must NOT pollute authoring results with the excluded showcase link!
        snapshot.LinkedModels = new[]
        {
            BuildLink("EastWing.rvt", "east-002"),
            new FederationLinkSnapshot(
                linkInstanceElementId: 999,
                linkInstanceUniqueId: "uid-showcase",
                name: "Tokyo_Showcase_Companion.rvt",
                isLoaded: true,
                modelId: snapshot.HostModelId, // would fail KZ-FED-002 if not excluded
                modelRole: ShowcaseExportGuard.ShowcaseModelRole,
                crsEpsg: 4326, // would fail KZ-FED-001 if not excluded
                levels: new[]
                {
                    new FederationLevelSnapshot(998, "sc-1", "1F", levelId: "host-level-1f", ordinal: 0, shortName: "1F", projectElevationMeters: 0.050, isExportLevel: true),
                }),
        };

        FederationCheckReport report = FederationChecker.Evaluate(snapshot);

        Assert.Equal(ReadinessCheckStatus.Fail, report.GetRule(FederationChecker.RuleShowcaseExclusion).Status);
        Assert.Equal(ReadinessCheckStatus.Pass, report.GetRule(FederationChecker.RuleCrsAndPose).Status);
        Assert.Equal(ReadinessCheckStatus.Pass, report.GetRule(FederationChecker.RuleUniqueModelId).Status);
        Assert.Equal(ReadinessCheckStatus.Pass, report.GetRule(FederationChecker.RuleUniqueLevelId).Status);
        Assert.Equal(ReadinessCheckStatus.Pass, report.GetRule(FederationChecker.RuleNearMissLevelDrift).Status);
        Assert.Equal(ReadinessCheckStatus.Pass, report.GetRule(FederationChecker.RuleTilesLevelKeyCollision).Status);
    }

    // Optional additive `federation: { mode, hubModelId }` in gis-model.json

    [Fact]
    public void GisModelFile_OptionalFederationBlockRoundTripsAndOmitsWhenUnset()
    {
        // Backward compatibility: default / legacy v1 gis-model.json omits "federation" completely
        GisModelFile defaultFile = GisModelFile.CreateDefault();
        Assert.Null(defaultFile.Federation);
        string defaultJson = defaultFile.ToJson();
        Assert.DoesNotContain("\"federation\"", defaultJson);
        Assert.Equal(GisModelFile.CreateFromTemplate().ToJson(), defaultJson);

        // Additive federation metadata round-trips cleanly
        GisModelFile spoke = GisModelFile.CreateDefault();
        spoke.Identity.ModelId = "spoke-east-01";
        spoke.Federation = GisModelFederation.Create(KanizuFederationMode.Spoke, "hub-main-00");
        spoke.Links = new List<GisModelLink>
        {
            new() { UniqueId = "uid-1", Name = "Platform.rvt", ModelId = "plat-02" },
        };
        spoke.Levels.Add(new GisModelLevel
        {
            UniqueId = "lvl-u-1",
            Name = "1F",
            LevelId = "lvl-id-1",
            Ordinal = 0,
            ShortName = "1F",
            ElevationMeters = 12.345,
        });

        string json = spoke.ToJson();
        Assert.Contains("\"federation\"", json);
        Assert.Contains("\"mode\": \"spoke\"", json);
        Assert.Contains("\"hubModelId\": \"hub-main-00\"", json);
        Assert.Contains("\"modelId\": \"plat-02\"", json);
        Assert.Contains("\"elevationMeters\": 12.345", json);

        GisModelFile parsed = GisModelFile.Parse(json, "test");
        Assert.NotNull(parsed.Federation);
        Assert.Equal("spoke", parsed.Federation!.Mode);
        Assert.Equal("hub-main-00", parsed.Federation.HubModelId);
        Assert.True(GisModelFederation.TryParseMode(parsed.Federation.Mode, out KanizuFederationMode mode));
        Assert.Equal(KanizuFederationMode.Spoke, mode);
        Assert.Equal("plat-02", Assert.Single(parsed.Links!).ModelId);
        Assert.Equal(12.345, Assert.Single(parsed.Levels).ElevationMeters);
    }

    // Readiness + HandoverReceipt Integration (Zero Duplicate RuleIds)

    [Fact]
    public void KzFed008_WarnsOncePerFileAboutAttachedNestedLinksItCannotCheck()
    {
        FederationCheckSnapshot snapshot = CreateCleanHubSnapshot();
        FederationLinkSnapshot clean = BuildLink("EastWing.rvt", "east-002");
        FederationLinkSnapshot Instance(long elementId, string uniqueId) =>
            new FederationLinkSnapshot(
                elementId,
                uniqueId,
                clean.Name,
                isLoaded: true,
                sourcePath: @"D:\Station\EastWing.rvt",
                modelId: clean.ModelId,
                modelRole: clean.ModelRole,
                federationMode: clean.FederationMode,
                hubModelId: clean.HubModelId,
                crsEpsg: clean.CrsEpsg,
                isGeoreferenceVerified: true,
                heightDatum: clean.HeightDatum,
                geoidModel: clean.GeoidModel,
                geoidOffsetMeters: clean.GeoidOffsetMeters,
                controlPoints: clean.ControlPoints,
                levels: clean.Levels,
                nestedLinkNames: new[] { "EastAnnex.rvt" });
        snapshot.LinkedModels = new[] { Instance(701, "link-uid-1"), Instance(702, "link-uid-2") };

        FederationRuleResult rule = FederationChecker.Evaluate(snapshot).GetRule(FederationChecker.RuleLinkAndRoleHygiene);

        Assert.Equal(ReadinessCheckStatus.Warn, rule.Status);
        FederationIssue issue = Assert.Single(rule.Issues);
        Assert.Equal("NESTED-LINK-NOT-CHECKED", issue.Code);
        Assert.Contains("EastAnnex.rvt", issue.MessageEn);
    }

    [Fact]
    public void ProjectReadinessEvaluator_WarnsWhenTheFederationCollectorFailed()
    {
        GisToolStandard standard = GisToolStandard.LoadEmbedded();
        ReadinessCheckSnapshot readinessSnapshot = CreateReadyProjectSnapshot(standard);
        readinessSnapshot.Federation = null;
        readinessSnapshot.FederationUnavailableReason = "InvalidOperationException: link document is closing";

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(readinessSnapshot, ReadinessMode.Project, standard);

        ReadinessCheckGroup group = Assert.Single(report.Groups, g => g.Id == "federation");
        ReadinessCheckItem item = Assert.Single(group.Items);
        Assert.Equal(ReadinessCheckStatus.Warn, item.Status);
        Assert.Contains("link document is closing", item.Detail);
        Assert.Null(report.FederationReport);
    }

    [Fact]
    public void ProjectReadinessEvaluator_IntegratesFullFederationCheckerWithoutDuplicateRuleIdsAndProducesValidReceipt()
    {
        GisToolStandard standard = GisToolStandard.LoadEmbedded();
        ReadinessCheckSnapshot readinessSnapshot = CreateReadyProjectSnapshot(standard);

        ProjectReadinessReport report = ProjectReadinessEvaluator.Evaluate(readinessSnapshot, ReadinessMode.Project, standard);

        Assert.True(report.IsReady);
        Assert.Equal(100, report.ScorePercent);
        Assert.NotNull(report.FederationReport);
        Assert.True(report.FederationReport!.IsPassed);

        List<ReadinessCheckItem> fedItems = report.Groups
            .SelectMany(g => g.Items)
            .Where(item => item.RuleId.StartsWith("KZ-FED-", StringComparison.Ordinal))
            .ToList();
        Assert.Equal(9, fedItems.Count);
        Assert.Equal(9, fedItems.Select(i => i.RuleId).Distinct(StringComparer.Ordinal).Count());

        Guid docGuid = new("11111111-2222-3333-4444-555555555555");
        HandoverReceipt receipt = HandoverReceipt.FromReadinessReport(
            report,
            "1.0.0",
            "2024.0.0",
            "contractor",
            new HandoverReceiptModel("model-1", docGuid, 7, "host"),
            new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc));

        Assert.Equal(HandoverReceiptVerdict.Pass, receipt.Verdict);
        for (int i = 1; i <= 9; i++)
        {
            string ruleId = $"KZ-FED-{i:000}";
            Assert.Single(receipt.Rules, r => r.RuleId == ruleId);
        }

        string receiptJson = receipt.ToJson();
        HandoverVerificationResult verified = HandoverReceiptVerifier.Verify(receiptJson, docGuid, 7);
        Assert.True(verified.IsValid);
    }

    // Test Helpers

    private static FederationCheckSnapshot CreateCleanHubSnapshot(IReadOnlyList<ControlPointPair>? controlPoints = null)
    {
        return new FederationCheckSnapshot
        {
            HostDocumentTitle = "TokyoHub.rvt",
            HostDocumentPath = @"D:\Station\TokyoHub.rvt",
            HostDocumentKey = "doc-hub-001",
            HostModelId = "hub-001",
            HostModelRole = ShowcaseExportGuard.AuthoringModelRole,
            HostFederationMode = KanizuFederationMode.Hub,
            HostCrsEpsg = 6677,
            HostCrsName = "JGD2011 / Japan Plane Rectangular CS IX",
            IsHostGeoreferenceVerified = true,
            HostHeightDatum = "TP",
            HostGeoidModel = "EGM2008",
            HostGeoidOffsetMeters = 36.70,
            HostLevels = new[]
            {
                new FederationLevelSnapshot(501, "h-1", "1F", levelId: "host-level-1f", ordinal: 0, shortName: "1F", projectElevationMeters: 0.0, isExportLevel: true),
                new FederationLevelSnapshot(502, "h-2", "2F", levelId: "host-level-2f", ordinal: 1, shortName: "2F", projectElevationMeters: 4.5, isExportLevel: true),
            },
            LinkedModels = new[]
            {
                BuildLink("EastWing.rvt", "east-002", controlPoints: controlPoints),
            },
        };
    }

    private static ReadinessCheckSnapshot CreateReadyProjectSnapshot(GisToolStandard standard) =>
        new()
        {
            DocumentTitle = "TokyoHub.rvt",
            DocumentPath = @"D:\Station\TokyoHub.rvt",
            IsEmptyModel = false,
            StandardVersionText = standard.Version.ToString(),
            BoundParameters = standard.Parameters.Select(p => new BoundParameterInfo(p.Name, p.Guid, p.Binding, p.Categories)).ToList(),
            Materials = standard.Materials.Select((m, idx) => new MaterialInfo(100 + idx, m.Name, m.FillHex)).ToList(),
            FloorTypes = standard.FloorTypes.Select((ft, idx) => new FloorTypeInfo(200 + idx, ft.Name, ft.GisCategory, ft.GisRestriction, ft.Material)).ToList(),
            LoadedFamilies = new[]
            {
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
            ProjectCrsEpsgCode = 6677,
            ProjectCrsName = "JGD2011 / Japan Plane Rectangular CS IX",
            IsGeoreferenceVerified = true,
            HostModelId = "hub-001",
            HostModelRole = ShowcaseExportGuard.AuthoringModelRole,
            Levels = new[]
            {
                new LevelInfo(501, "1F", ordinal: 0, shortName: "1F", hasFloors: true),
                new LevelInfo(502, "2F", ordinal: 1, shortName: "2F", hasFloors: true),
            },
            Federation = CreateCleanHubSnapshot(),
        };

    private static FederationLinkSnapshot BuildLink(
        string name,
        string modelId,
        int? crsEpsg = 6677,
        string heightDatum = "TP",
        string geoidModel = "EGM2008",
        double? geoidOffset = 36.70,
        long linkElementId = 701,
        string linkUniqueId = "link-uid-1",
        string? sourcePath = null,
        IReadOnlyList<ControlPointPair>? controlPoints = null,
        IReadOnlyList<FederationLevelSnapshot>? levels = null,
        bool isIncludedInHubExport = true)
    {
        IReadOnlyList<ControlPointPair> defaultPoints = controlPoints ?? new[]
        {
            new ControlPointPair("Origin", new FederationPoint3D(1000.0, 2000.0, 0.0), new FederationPoint3D(1000.0, 2000.0, 0.0)),
            new ControlPointPair("+X", new FederationPoint3D(1100.0, 2000.0, 0.0), new FederationPoint3D(1100.0, 2000.0, 0.0)),
            new ControlPointPair("+Y", new FederationPoint3D(1000.0, 2100.0, 0.0), new FederationPoint3D(1000.0, 2100.0, 0.0)),
        };
        IReadOnlyList<FederationLevelSnapshot> defaultLevels = levels ?? new[]
        {
            new FederationLevelSnapshot(601, "l-1", "1F", levelId: $"{modelId}-level-1f", ordinal: 0, shortName: "1F", projectElevationMeters: 0.0, isExportLevel: true),
            new FederationLevelSnapshot(602, "l-2", "2F", levelId: $"{modelId}-level-2f", ordinal: 1, shortName: "2F", projectElevationMeters: 4.5, isExportLevel: true),
        };

        return new FederationLinkSnapshot(
            linkInstanceElementId: linkElementId,
            linkInstanceUniqueId: linkUniqueId,
            name: name,
            isLoaded: true,
            sourcePath: sourcePath ?? $@"D:\Station\{name}",
            modelId: modelId,
            modelRole: ShowcaseExportGuard.AuthoringModelRole,
            federationMode: KanizuFederationMode.Spoke,
            hubModelId: "hub-001",
            crsEpsg: crsEpsg,
            isGeoreferenceVerified: true,
            heightDatum: heightDatum,
            geoidModel: geoidModel,
            geoidOffsetMeters: geoidOffset,
            controlPoints: defaultPoints,
            levels: defaultLevels,
            isIncludedInHubExport: isIncludedInHubExport);
    }

    private static void WriteStationBuilding(
        string stationRoot,
        string modelId,
        string buildingName,
        string sourceModelFile,
        string documentKey,
        int crsEpsg,
        string heightDatum,
        string geoidModel,
        double geoidOffset,
        KanizuFederationMode federationMode,
        string? hubModelId = null,
        IEnumerable<(string UniqueId, string Name, string ModelId)>? includedLinks = null,
        IEnumerable<(string UniqueId, string Name, string LevelId, int Ordinal, string ShortName, double ElevationMeters)>? levels = null,
        double anchorOrthometricMeters = 0.0,
        bool legacyGisModelWithoutElevations = false,
        IEnumerable<string>? levelsWithoutExportView = null,
        bool legacyViewsWithoutLevels = false)
    {
        GisModelFile gisModel = GisModelFile.CreateDefault();
        gisModel.Identity.ModelId = modelId;
        gisModel.Identity.Station = Path.GetFileName(stationRoot);
        gisModel.Identity.Building = buildingName;
        gisModel.Identity.CrsEpsg = crsEpsg;
        gisModel.Federation = GisModelFederation.Create(federationMode, hubModelId);

        if (includedLinks != null)
        {
            gisModel.Links = includedLinks
                .Select(l => new GisModelLink { UniqueId = l.UniqueId, Name = l.Name, ModelId = l.ModelId })
                .ToList();
        }

        List<(string UniqueId, string Name, string LevelId, int Ordinal, string ShortName, double ElevationMeters)> levelList =
            (levels ?? Array.Empty<(string, string, string, int, string, double)>()).ToList();

        foreach (var lvl in levelList)
        {
            gisModel.Levels.Add(new GisModelLevel
            {
                UniqueId = lvl.UniqueId,
                Name = lvl.Name,
                LevelId = lvl.LevelId,
                Ordinal = lvl.Ordinal,
                ShortName = lvl.ShortName,
                ElevationMeters = legacyGisModelWithoutElevations ? null : lvl.ElevationMeters,
            });
        }

        HashSet<string> noView = new(levelsWithoutExportView ?? Array.Empty<string>(), StringComparer.Ordinal);
        gisModel.Views = levelList
            .Where(l => !noView.Contains(l.UniqueId))
            .Select(l => new GisModelView { UniqueId = "view-" + l.UniqueId, Name = "GIS_" + l.Name, LevelUniqueId = legacyViewsWithoutLevels ? null : l.UniqueId })
            .ToList();

        gisModel.Save(GisModelFile.PathFor(stationRoot, modelId));

        CesiumPackageManifest manifest = new()
        {
            PackageId = Guid.NewGuid().ToString("D"),
            CreatedUtc = new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc),
            Source = new CesiumPackageSource
            {
                Model = sourceModelFile,
                DocumentKey = documentKey,
                ModelId = modelId,
                ModelRole = CesiumModelRoles.Authoring,
            },
            Building = new CesiumPackageBuilding
            {
                Id = buildingName.ToLowerInvariant(),
                Name = buildingName,
            },
            Crs = new CesiumPackageCrs
            {
                ProjectEpsg = crsEpsg,
                CoordinateMode = "projected",
                GisEpsg = crsEpsg,
            },
            Anchor = new CesiumPackageAnchor
            {
                Lat = 35.6812,
                Lon = 139.7671,
                ElevationMeters = anchorOrthometricMeters + geoidOffset,
                GeoidOffsetMeters = geoidOffset,
                GeoidModel = geoidModel,
                OrthometricHeightMeters = anchorOrthometricMeters,
                HeightDatum = heightDatum,
            },
            Tiles = new CesiumPackageTiles
            {
                Levels = "tiles/levels.json",
            },
            Export = new CesiumPackageExport
            {
                Scope = CesiumExportScopes.Whole,
                Links = (includedLinks ?? Array.Empty<(string, string, string)>())
                    .Select(l => new CesiumPackageExportItem { UniqueId = l.UniqueId, Name = l.Name })
                    .ToList(),
            },
            LevelMap = levelList
                .Select(l => new CesiumPackageLevelMapEntry
                {
                    GisLevelId = l.LevelId,
                    TilesLevelKey = LevelKey.Build(l.Name),
                    Name = l.Name,
                })
                .ToList(),
        };

        string modelFolder = CesiumStationFolder.ModelFolderName(manifest);
        string versionDir = Path.Combine(stationRoot, CesiumStationFolder.RevitDirectoryName, modelFolder, "20261127T000000Z-a1b2c3d4");
        Directory.CreateDirectory(Path.Combine(versionDir, "tiles"));
        File.WriteAllText(
            Path.Combine(versionDir, "cesium-package.json"),
            CesiumPackageManifestSerializer.Serialize(manifest),
            new UTF8Encoding(false));

        JObject levelsJson = new()
        {
            ["version"] = 2,
            ["levels"] = new JArray(levelList.Select(l => new JObject
            {
                ["levelKey"] = LevelKey.Build(l.Name),
                ["levelName"] = l.Name,
                ["levelUniqueIds"] = new JArray(l.UniqueId),
                ["levelElevationMeters"] = l.ElevationMeters - anchorOrthometricMeters,
            })),
        };
        File.WriteAllText(
            Path.Combine(versionDir, "tiles", "levels.json"),
            levelsJson.ToString(),
            new UTF8Encoding(false));
    }
}
