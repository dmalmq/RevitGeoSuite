using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.StarterKit;

public sealed class T7OpeningsFixturesColumnsPoiTests
{
    [Theory]
    [InlineData("KNZ_OPEN_DoorSwing", "swinging", "pedestrian", "none", 900, 180, 2100)]
    [InlineData("KNZ_OPEN_DoorSlide", "sliding", "pedestrian", "none", 1800, 200, 2200)]
    [InlineData("KNZ_OPEN_DoorAuto", "automatic", "pedestrian.transit", "none", 2400, 220, 2400)]
    [InlineData("KNZ_OPEN_Shutter", "shutter", "pedestrian", "scheduled", 3000, 240, 2800)]
    [InlineData("KNZ_OPEN_Opening", "open", "pedestrian", "none", 2000, 200, 2400)]
    public void AllFiveOpeningFamilies_CompilePassLinterAndExposeWallHostedContract(
        string familyId,
        string expectedDoorType,
        string expectedOpeningCategory,
        string expectedAccessCon,
        double expectedWidthMm,
        double expectedDepthMm,
        double expectedHeightMm)
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec spec = kit.FindFamily(familyId)!;

        Assert.Equal("OST_Doors", spec.Category);
        Assert.Equal(FamilyGeometrySpec.SeedWallHostedDoor, spec.SeedKind);
        Assert.True(spec.IsPolygonal);

        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kit);

        // Seed centering offset aligns wall-hosted doors symmetrically on Metric Door.rft wall cut
        PlanPoint3D seedOffset = FamilyGeometryPlanBuilder.ComputeSeedPlacementOffsetMm(spec);
        Assert.Equal(-expectedWidthMm * 0.5, seedOffset.XMm, precision: 6);
        Assert.Equal(-expectedDepthMm * 0.5, seedOffset.YMm, precision: 6);
        Assert.Equal(0.0, seedOffset.ZMm, precision: 6);

        // Synchronized Width / Depth / Height parameters
        Assert.Contains(plan.DimensionParameters, d => d.Name == "Width" && Math.Abs(d.ValueMm - expectedWidthMm) < 1e-6);
        Assert.Contains(plan.DimensionParameters, d => d.Name == "Depth" && Math.Abs(d.ValueMm - expectedDepthMm) < 1e-6);
        Assert.Contains(plan.DimensionParameters, d => d.Name == "Height" && Math.Abs(d.ValueMm - expectedHeightMm) < 1e-6);

        // Shared parameter defaults match spec
        Assert.Contains(plan.SharedParameters, p => p.Name == GisSharedParameters.GisDoor && p.DefaultValue == expectedDoorType && !p.IsInstance);
        Assert.Contains(plan.SharedParameters, p => p.Name == GisSharedParameters.GisOpeningCategory && p.DefaultValue == expectedOpeningCategory && !p.IsInstance);
        Assert.Contains(plan.SharedParameters, p => p.Name == GisSharedParameters.GisAccessCon && p.DefaultValue == expectedAccessCon && !p.IsInstance);

        // Coarse K_Footprint sits at Z=0 with downward PlanarFace and is emitted LAST in AllPrimitives
        CompiledExtrusionPrimitive footprint = Assert.Single(plan.CoarsePrimitives);
        Assert.Equal(CoarseGeometrySpec.FootprintSubcategory, footprint.Subcategory);
        Assert.True(footprint.VisibleInCoarse);
        Assert.False(footprint.VisibleInMedium);
        Assert.False(footprint.VisibleInFine);
        Assert.Equal(0.0, footprint.MinZMm, precision: 6);
        Assert.Equal(expectedWidthMm, footprint.MaxXMm - footprint.MinXMm, precision: 6);
        Assert.Equal(expectedDepthMm, footprint.MaxYMm - footprint.MinYMm, precision: 6);
        Assert.Equal("footprint_plan", plan.AllPrimitives[plan.AllPrimitives.Count - 1].Name);

        RfaInspectionSnapshot snapshot = plan.BuildExpectedSnapshot(fileSizeBytes: 128 * 1024);
        Assert.True(snapshot.HasHostWallCutOpening);

        RfaLintReport report = RfaLinter.Lint(snapshot, spec, kit);
        Assert.True(report.Passed, string.Join("\n", report.Findings.Select(f => f.ToString())));
        Assert.Empty(report.Findings);
        Assert.Equal(13, report.RuleResults.Count);
    }

    [Fact]
    public void AllFiveOpeningSpecsAndKiosk_EnforceSafeSolidVoidNonTargetCoarseCreationOrder()
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();

        // 1. KNZ_OPEN_DoorSwing: door_leaf -> vision_lite_cut -> non-target solids (incl. K_Glass vision_lite) -> K_Footprint LAST
        FamilyGeometryPlan swingPlan = FamilyGeometryPlanBuilder.Build(kit.FindFamily("KNZ_OPEN_DoorSwing")!, kit);
        List<string> swingNames = swingPlan.AllPrimitives.Select(p => p.Name).ToList();
        int doorLeafIdx = swingNames.IndexOf("door_leaf");
        int visionCutIdx = swingNames.IndexOf("vision_lite_cut");
        int visionGlassIdx = swingNames.IndexOf("vision_lite");
        int swingFootprintIdx = swingNames.IndexOf("footprint_plan");

        Assert.Equal(0, doorLeafIdx);
        Assert.Equal(doorLeafIdx + 1, visionCutIdx);
        Assert.True(visionCutIdx < visionGlassIdx, "vision_lite_cut must precede K_Glass vision_lite.");
        Assert.Equal(swingNames.Count - 1, swingFootprintIdx);
        Assert.Equal("door_leaf", swingPlan.AllPrimitives[visionCutIdx].TargetOpName);
        Assert.True(swingPlan.AllPrimitives[visionCutIdx].ThroughCut);

        // 2. KNZ_OPEN_DoorSlide: leaf_frame_left -> glazing_cut_left -> leaf_frame_right -> glazing_cut_right -> glazing_left/right -> K_Footprint LAST
        FamilyGeometryPlan slidePlan = FamilyGeometryPlanBuilder.Build(kit.FindFamily("KNZ_OPEN_DoorSlide")!, kit);
        List<string> slideNames = slidePlan.AllPrimitives.Select(p => p.Name).ToList();
        int leftLeafIdx = slideNames.IndexOf("leaf_frame_left");
        int leftCutIdx = slideNames.IndexOf("glazing_cut_left");
        int rightLeafIdx = slideNames.IndexOf("leaf_frame_right");
        int rightCutIdx = slideNames.IndexOf("glazing_cut_right");
        int leftGlassIdx = slideNames.IndexOf("glazing_left");
        int rightGlassIdx = slideNames.IndexOf("glazing_right");
        int slideFootprintIdx = slideNames.IndexOf("footprint_plan");

        Assert.Equal(leftLeafIdx + 1, leftCutIdx);
        Assert.Equal(rightLeafIdx + 1, rightCutIdx);
        Assert.True(leftCutIdx < leftGlassIdx && rightCutIdx < leftGlassIdx, "Both glazing cuts must precede K_Glass glazing_left.");
        Assert.True(leftCutIdx < rightGlassIdx && rightCutIdx < rightGlassIdx, "Both glazing cuts must precede K_Glass glazing_right.");
        Assert.Equal(slideNames.Count - 1, slideFootprintIdx);
        Assert.Equal("leaf_frame_left", slidePlan.AllPrimitives[leftCutIdx].TargetOpName);
        Assert.Equal("leaf_frame_right", slidePlan.AllPrimitives[rightCutIdx].TargetOpName);

        // 3. KNZ_OPEN_DoorAuto, KNZ_OPEN_Shutter, KNZ_OPEN_Opening: zero voids, all Medium/Fine before Coarse K_Footprint
        foreach (string noVoidOpeningId in new[] { "KNZ_OPEN_DoorAuto", "KNZ_OPEN_Shutter", "KNZ_OPEN_Opening" })
        {
            FamilyGeometryPlan p = FamilyGeometryPlanBuilder.Build(kit.FindFamily(noVoidOpeningId)!, kit);
            Assert.DoesNotContain(p.AllPrimitives, x => !x.IsSolid);
            Assert.Equal("footprint_plan", p.AllPrimitives[p.AllPrimitives.Count - 1].Name);
            Assert.All(p.MediumFinePrimitives, m => Assert.False(m.VisibleInCoarse));
        }

        // Shutter specifically expands its 4 upper_slat_curtain array boxes cleanly
        FamilyGeometryPlan shutterPlan = FamilyGeometryPlanBuilder.Build(kit.FindFamily("KNZ_OPEN_Shutter")!, kit);
        Assert.Equal(4, shutterPlan.MediumFinePrimitives.Count(p => p.SourceOpName == "shutter_slat"));
        Assert.Equal(8, shutterPlan.MediumFinePrimitives.Count);

        // 4. KNZ_FIX_Kiosk: kiosk_cabinet_body -> screen_bay_recess (blind pocket) -> touchscreen_display -> K_Footprint LAST
        FamilyGeometryPlan kioskPlan = FamilyGeometryPlanBuilder.Build(kit.FindFamily("KNZ_FIX_Kiosk")!, kit);
        List<string> kioskNames = kioskPlan.AllPrimitives.Select(p => p.Name).ToList();
        int cabinetIdx = kioskNames.IndexOf("kiosk_cabinet_body");
        int recessIdx = kioskNames.IndexOf("screen_bay_recess");
        int screenIdx = kioskNames.IndexOf("touchscreen_display");
        int kioskFootprintIdx = kioskNames.IndexOf("footprint_plan");

        Assert.Equal(0, cabinetIdx);
        Assert.Equal(cabinetIdx + 1, recessIdx);
        Assert.True(recessIdx < screenIdx, "Blind pocket void screen_bay_recess must precede touchscreen_display.");
        Assert.Equal(kioskNames.Count - 1, kioskFootprintIdx);
        Assert.False(kioskPlan.AllPrimitives[recessIdx].ThroughCut);
        Assert.Equal("kiosk_cabinet_body", kioskPlan.AllPrimitives[recessIdx].TargetOpName);
    }

    [Theory]
    [InlineData("KNZ_FIX_TicketGateBank", "OST_SpecialityEquipment", "turnstile", "ticket_gate", 16)]
    [InlineData("KNZ_FIX_BarrierFence", "OST_SpecialityEquipment", "wall", null, 6)]
    [InlineData("KNZ_FIX_Kiosk", "OST_SpecialityEquipment", "kiosk", null, 5)]
    [InlineData("KNZ_FIX_Equipment", "OST_MechanicalEquipment", "equipment", null, 4)]
    public void AllFourFixtureFamilies_CompilePassLinterAndMapCorrectlyThroughFixtureFeatureAttributes(
        string familyId,
        string expectedCategory,
        string expectedFixtureType,
        string? expectedGisKind,
        int expectedMediumFinePrimitiveCount)
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec spec = kit.FindFamily(familyId)!;

        Assert.Equal(expectedCategory, spec.Category);
        Assert.Equal(FamilyGeometrySpec.SeedProcedural, spec.SeedKind);
        Assert.True(spec.IsPolygonal);

        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kit);
        Assert.Single(plan.CoarsePrimitives);
        Assert.Equal(expectedMediumFinePrimitiveCount, plan.MediumFinePrimitives.Count);

        RfaInspectionSnapshot snapshot = plan.BuildExpectedSnapshot(fileSizeBytes: 128 * 1024);
        RfaLintReport report = RfaLinter.Lint(snapshot, spec, kit);
        Assert.True(report.Passed, string.Join("\n", report.Findings.Select(f => f.ToString())));

        string actualFixtureType = Assert.Single(
            plan.SharedParameters,
            p => p.Name == GisSharedParameters.GisFixtureType).DefaultValue;
        Assert.Equal(expectedFixtureType, actualFixtureType);

        if (expectedGisKind != null)
        {
            string actualKind = Assert.Single(
                plan.SharedParameters,
                p => p.Name == GisSharedParameters.GisKind).DefaultValue;
            Assert.Equal(expectedGisKind, actualKind);

            StarterKitReferenceStandards std = StarterKitReferenceStandards.LoadEmbedded();
            Assert.True(std.FamiliesByGisKind[expectedGisKind].Contains(spec.FamilyName));
        }

        // Verify v3 FixtureFeatureAttributes resolution vs v2 legacy fallback
        Assert.Equal(expectedFixtureType, FixtureFeatureAttributes.ResolveType(actualFixtureType, standardVersion: 3));
        Assert.Equal(FixtureFeatureAttributes.DefaultType, FixtureFeatureAttributes.ResolveType(actualFixtureType, standardVersion: 2));

        Dictionary<string, object?> unitRow = new()
        {
            ["id"] = "u-1",
            ["category"] = "fixture",
            ["level_id"] = "l-1",
        };
        Dictionary<string, object?> v3Fixture = FixtureFeatureAttributes.FromUnit(unitRow, actualFixtureType, standardVersion: 3);
        Dictionary<string, object?> v2Fixture = FixtureFeatureAttributes.FromUnit(unitRow, actualFixtureType, standardVersion: 2);
        Assert.Equal(expectedFixtureType, v3Fixture["type"]);
        Assert.Equal("fixture", v2Fixture["type"]);
    }

    [Theory]
    [InlineData(1, 1280.0, 2, 1)]
    [InlineData(2, 2240.0, 3, 2)]
    [InlineData(3, 3200.0, 4, 3)]
    [InlineData(4, 4160.0, 5, 4)]
    [InlineData(5, 5120.0, 6, 5)]
    public void TicketGateBankArrayExpansion_ScalesLanesPreservesClearancesAndPassesLinter(
        int laneCount,
        double expectedWidthMm,
        int expectedCabinets,
        int expectedFlappers)
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec baseSpec = kit.FindFamily(StarterKitCatalogSelector.TicketGateBankId)!;

        FamilyGeometrySpec expanded = StarterKitArrayExpander.ExpandTicketGateBank(baseSpec, laneCount);
        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(expanded, kit);

        Assert.Equal(expectedWidthMm, expanded.DefaultDimensionsMm.WidthMm, precision: 6);
        Assert.Equal(expectedCabinets, plan.MediumFinePrimitives.Count(p => p.SourceOpName == "gate_cabinet_body"));
        Assert.Equal(expectedCabinets, plan.MediumFinePrimitives.Count(p => p.SourceOpName == "ic_reader_pad"));
        Assert.Equal(expectedCabinets, plan.MediumFinePrimitives.Count(p => p.SourceOpName == "cabinet_top_trim"));
        Assert.Equal(expectedFlappers, plan.MediumFinePrimitives.Count(p => p.SourceOpName == "flapper_glass_leaf"));

        CompiledExtrusionPrimitive footprint = Assert.Single(plan.CoarsePrimitives);
        Assert.Equal(expectedWidthMm, footprint.MaxXMm - footprint.MinXMm, precision: 6);

        RfaLintReport report = RfaLinter.Lint(plan.BuildExpectedSnapshot(180 * 1024), expanded, kit);
        Assert.True(report.Passed, string.Join("\n", report.Findings.Select(f => f.ToString())));
    }

    [Theory]
    [InlineData(2, 1240.0)]
    [InlineData(3, 2400.0)]
    [InlineData(4, 3560.0)]
    [InlineData(5, 4720.0)]
    public void BarrierFenceArrayExpansion_ScalesPostsPreservesClearancesAndPassesLinter(
        int postCount,
        double expectedWidthMm)
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec baseSpec = kit.FindFamily(StarterKitCatalogSelector.BarrierFenceId)!;

        FamilyGeometrySpec expanded = StarterKitArrayExpander.ExpandBarrierFence(baseSpec, postCount);
        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(expanded, kit);

        Assert.Equal(expectedWidthMm, expanded.DefaultDimensionsMm.WidthMm, precision: 6);
        Assert.Equal(postCount, plan.MediumFinePrimitives.Count(p => p.SourceOpName == "stanchion_post"));

        CompiledExtrusionPrimitive footprint = Assert.Single(plan.CoarsePrimitives);
        Assert.Equal(expectedWidthMm, footprint.MaxXMm - footprint.MinXMm, precision: 6);

        RfaLintReport report = RfaLinter.Lint(plan.BuildExpectedSnapshot(120 * 1024), expanded, kit);
        Assert.True(report.Passed, string.Join("\n", report.Findings.Select(f => f.ToString())));
    }

    [Fact]
    public void ColumnsAndOptionalPoiMarker_CompileAndPassLinter()
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();

        // KNZ_COL_Rect
        FamilyGeometrySpec rectSpec = kit.FindFamily(StarterKitCatalogSelector.RectColumnId)!;
        FamilyGeometryPlan rectPlan = FamilyGeometryPlanBuilder.Build(rectSpec, kit);
        Assert.Single(rectPlan.CoarsePrimitives);
        Assert.Equal(2, rectPlan.MediumFinePrimitives.Count);
        Assert.True(RfaLinter.Lint(rectPlan.BuildExpectedSnapshot(64 * 1024), rectSpec, kit).Passed);

        // KNZ_COL_Round: 12-gon radial nprism + Diameter parameter
        FamilyGeometrySpec roundSpec = kit.FindFamily(StarterKitCatalogSelector.RoundColumnId)!;
        FamilyGeometryPlan roundPlan = FamilyGeometryPlanBuilder.Build(roundSpec, kit);
        CompiledExtrusionPrimitive roundFootprint = Assert.Single(roundPlan.CoarsePrimitives);
        Assert.Equal(GeometryOpSpec.OpNPrism, roundFootprint.OpKind);
        Assert.Equal(12, roundFootprint.ProfileVertices.Count);
        Assert.Equal(44, roundFootprint.EstimatedTriangles);
        Assert.Contains(roundPlan.DimensionParameters, d => d.Name == "Diameter" && Math.Abs(d.ValueMm - 700.0) < 1e-6);
        Assert.True(RfaLinter.Lint(roundPlan.BuildExpectedSnapshot(80 * 1024), roundSpec, kit).Passed);

        // Optional non-polygonal KNZ_POI_FacilityMarker: 6-gon Coarse, 8/6/8-gon Medium/Fine
        FamilyGeometrySpec poiSpec = kit.FindFamily(StarterKitCatalogSelector.PoiFacilityMarkerId)!;
        Assert.False(poiSpec.IsPolygonal);
        FamilyGeometryPlan poiPlan = FamilyGeometryPlanBuilder.Build(poiSpec, kit);
        CompiledExtrusionPrimitive poiCoarse = Assert.Single(poiPlan.CoarsePrimitives);
        Assert.Equal(6, poiCoarse.ProfileVertices.Count);
        Assert.Equal(3, poiPlan.MediumFinePrimitives.Count);
        Assert.All(poiPlan.MediumFinePrimitives, p => Assert.Equal(GeometryOpSpec.OpNPrism, p.OpKind));
        RfaLintReport poiReport = RfaLinter.Lint(poiPlan.BuildExpectedSnapshot(64 * 1024), poiSpec, kit);
        Assert.True(poiReport.Passed, string.Join("\n", poiReport.Findings.Select(f => f.ToString())));
    }

    [Fact]
    public void CatalogSelector_Full16AndRequired15Runs_OrderDeterministicallyAndMatchExpectedManifestExact()
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();

        // 1. Full 16-family catalog (including optional KNZ_POI_FacilityMarker)
        StarterKitCatalogPlanResult fullRun = StarterKitCatalogSelector.CompileAndLintCatalog(
            kit,
            includeOptionalFamilies: true);
        Assert.True(fullRun.IncludedOptionalFamilies);
        Assert.True(fullRun.AllPassed);
        Assert.Equal(16, fullRun.Plans.Count);
        Assert.Equal(16, fullRun.ManifestEntries.Count);
        Assert.Equal(StarterKitCatalogSelector.AllCatalogFamilyIds, fullRun.Plans.Select(p => p.Spec.Id).ToArray());

        ExpectedManifest expectedFull = StarterKitCatalogSelector.BuildExpectedManifestForSelection(kit, includeOptionalFamilies: true);
        ExpectedManifest actualFull = CloneManifestWithFamilies(expectedFull, fullRun.ManifestEntries);
        ManifestComparisonResult fullDiff = StarterKitManifestComparer.Compare(expectedFull, actualFull);
        Assert.True(fullDiff.IsMatch, string.Join("\n", fullDiff.Findings.Select(f => f.ToString())));

        // 2. Required 15-family catalog (omitting optional KNZ_POI_FacilityMarker)
        StarterKitCatalogPlanResult requiredRun = StarterKitCatalogSelector.CompileAndLintCatalog(
            kit,
            includeOptionalFamilies: false);
        Assert.False(requiredRun.IncludedOptionalFamilies);
        Assert.True(requiredRun.AllPassed);
        Assert.Equal(15, requiredRun.Plans.Count);
        Assert.Equal(15, requiredRun.ManifestEntries.Count);
        Assert.DoesNotContain(requiredRun.Plans, p => p.Spec.Id == StarterKitCatalogSelector.PoiFacilityMarkerId);
        Assert.Equal(StarterKitCatalogSelector.AllRequiredFamilyIds, requiredRun.Plans.Select(p => p.Spec.Id).ToArray());

        ExpectedManifest expectedRequired = StarterKitCatalogSelector.BuildExpectedManifestForSelection(kit, includeOptionalFamilies: false);
        ExpectedManifest actualRequired = CloneManifestWithFamilies(expectedRequired, requiredRun.ManifestEntries);
        ManifestComparisonResult reqDiff = StarterKitManifestComparer.Compare(expectedRequired, actualRequired);
        Assert.True(reqDiff.IsMatch, string.Join("\n", reqDiff.Findings.Select(f => f.ToString())));
    }

    [Fact]
    public void RfaLinter_RejectsT7OpeningFixtureAndColumnContractViolations()
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();

        // 1. Wall-hosted door missing host wall cut or using procedural seed + out-of-sync Width
        FamilyGeometrySpec swingSpec = kit.FindFamily(StarterKitCatalogSelector.DoorSwingId)!;
        RfaInspectionSnapshot swingValid = FamilyGeometryPlanBuilder.Build(swingSpec, kit).BuildExpectedSnapshot(96 * 1024);
        List<RfaParameterSnapshot> badDoorParams = swingValid.Parameters.Select(p =>
            p.Name == "Width"
                ? new RfaParameterSnapshot("Width", false, null, false, "Length", "1200", numericValueMm: 1200.0)
                : p).ToList();
        RfaInspectionSnapshot badDoor = new(
            swingValid.FamilyName,
            swingValid.Category,
            seedKind: FamilyGeometrySpec.SeedProcedural,
            swingValid.FileSizeBytes,
            swingValid.Subcategories,
            swingValid.Solids,
            swingValid.Materials,
            badDoorParams,
            swingValid.CoarseTriangles,
            swingValid.MediumFineTriangles,
            hasHostWallCutOpening: false);

        RfaLintReport doorReport = RfaLinter.Lint(badDoor, swingSpec, kit);
        Assert.False(doorReport.Passed);
        Assert.Contains(doorReport.FindingsByRule[RfaLinter.RuleFamilyIdentity], f => f.Subject == "seedKind");
        Assert.Contains(doorReport.FindingsByRule[RfaLinter.RuleFamilyIdentity], f => f.Subject == "wallCutOpening");
        Assert.Contains(doorReport.FindingsByRule[RfaLinter.RuleCirculationContract], f => f.Subject == "Width");

        // 2. Ticket gate bank with wrong gis_fixture_type and missing gis_kind = "ticket_gate"
        FamilyGeometrySpec gateSpec = kit.FindFamily(StarterKitCatalogSelector.TicketGateBankId)!;
        RfaInspectionSnapshot gateValid = FamilyGeometryPlanBuilder.Build(gateSpec, kit).BuildExpectedSnapshot(128 * 1024);
        List<RfaParameterSnapshot> badGateParams = gateValid.Parameters
            .Where(p => p.Name != GisSharedParameters.GisKind)
            .Select(p => p.Name == GisSharedParameters.GisFixtureType
                ? new RfaParameterSnapshot(p.Name, p.IsShared, p.Guid, p.IsInstance, p.StorageKind, defaultValue: "kiosk")
                : p)
            .ToList();
        RfaInspectionSnapshot badGate = new(
            gateValid.FamilyName,
            gateValid.Category,
            gateValid.SeedKind,
            gateValid.FileSizeBytes,
            gateValid.Subcategories,
            gateValid.Solids,
            gateValid.Materials,
            badGateParams,
            gateValid.CoarseTriangles,
            gateValid.MediumFineTriangles);

        RfaLintReport gateReport = RfaLinter.Lint(badGate, gateSpec, kit);
        Assert.False(gateReport.Passed);
        Assert.True(gateReport.HasFinding(RfaLinter.RuleSharedParameters));
        Assert.True(gateReport.HasFinding(RfaLinter.RuleValidDomainDefaults));
        Assert.Contains(gateReport.FindingsByRule[RfaLinter.RuleCirculationContract], f => f.Subject == GisSharedParameters.GisKind);
        Assert.Contains(gateReport.FindingsByRule[RfaLinter.RuleCirculationContract], f => f.Subject == GisSharedParameters.GisFixtureType);

        // 3. Round column missing Diameter and reduced to a 4-sided box in Coarse (12 tris < 28)
        FamilyGeometrySpec roundSpec = kit.FindFamily(StarterKitCatalogSelector.RoundColumnId)!;
        RfaInspectionSnapshot roundValid = FamilyGeometryPlanBuilder.Build(roundSpec, kit).BuildExpectedSnapshot(80 * 1024);
        RfaInspectionSnapshot badRound = new(
            roundValid.FamilyName,
            roundValid.Category,
            roundValid.SeedKind,
            roundValid.FileSizeBytes,
            roundValid.Subcategories,
            roundValid.Solids,
            roundValid.Materials,
            roundValid.Parameters.Where(p => p.Name != "Diameter"),
            coarseTriangles: 12,
            roundValid.MediumFineTriangles);

        RfaLintReport roundReport = RfaLinter.Lint(badRound, roundSpec, kit);
        Assert.False(roundReport.Passed);
        Assert.Contains(roundReport.FindingsByRule[RfaLinter.RuleCirculationContract], f => f.Subject == "Diameter");
        Assert.Contains(roundReport.FindingsByRule[RfaLinter.RuleCirculationContract], f => f.Subject == "coarseTriangles");
    }

    private static ExpectedManifest CloneManifestWithFamilies(
        ExpectedManifest template,
        IEnumerable<ManifestFamilyEntry> families)
    {
        return new ExpectedManifest(
            template.KitVersion,
            template.GisStandardVersion,
            template.SupportedRevitVersions,
            template.CanonicalExportViewTemplate,
            template.StartingViewName,
            template.StartingViewType,
            template.StartingViewBilingual,
            template.ZeroIdentityEnforced,
            template.ZeroWorksetsEnforced,
            template.HasProjectIdentity,
            template.WorksetCount,
            template.ViewTemplates,
            template.QaViewFilters,
            template.Schedules,
            template.LineStyles,
            template.Materials,
            template.SystemFamilies,
            template.SharedParameters,
            families);
    }
}
