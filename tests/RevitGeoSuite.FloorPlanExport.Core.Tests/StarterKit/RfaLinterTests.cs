using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.StarterKit;

public sealed class RfaLinterTests
{
    [Theory]
    [InlineData("KNZ_CIRC_Escalator")]
    [InlineData("KNZ_CIRC_MovingWalkway")]
    [InlineData("KNZ_CIRC_Elevator")]
    [InlineData("KNZ_CIRC_ElevatorDoor")]
    public void AllFourCirculationFamilies_CompiledPlansPassAll13Rules(string familyId)
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec spec = kit.FindFamily(familyId)!;
        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kit);
        RfaInspectionSnapshot snapshot = plan.BuildExpectedSnapshot(fileSizeBytes: 128 * 1024);

        RfaLintReport report = RfaLinter.Lint(snapshot, spec, kit);

        Assert.True(report.Passed, string.Join("\n", report.Findings.Select(f => f.ToString())));
        Assert.Equal(RfaLintStatus.Pass, report.Status);
        Assert.Empty(report.Findings);
        Assert.Equal(13, report.RuleResults.Count);
        Assert.All(report.RuleResults, r => Assert.Equal(RfaLintStatus.Pass, r.Status));
        Assert.Contains("PASS", report.SummaryEn);
        Assert.Contains("合格", report.SummaryJa);
    }

    [Fact]
    public void Rule001_FailsOnWrongFamilyNameCategoryOrUnregisteredFamiliesJson()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_Escalator");

        RfaInspectionSnapshot bad = CloneSnapshot(
            valid,
            familyName: "Custom_Escalator_Unregistered",
            category: "OST_Furniture");

        RfaLintReport report = RfaLinter.Lint(bad, spec, kit);

        Assert.False(report.Passed);
        Assert.True(report.HasFinding(RfaLinter.RuleFamilyIdentity));
        Assert.True(report.FindingsByRule[RfaLinter.RuleFamilyIdentity].Count >= 3);
        Assert.All(report.FindingsByRule[RfaLinter.RuleFamilyIdentity], f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.MessageEn));
            Assert.False(string.IsNullOrWhiteSpace(f.MessageJa));
        });
    }

    [Fact]
    public void Rule002_FailsOnMissingRequiredParamWrongGuidAndWrongBinding()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_ElevatorDoor");

        List<RfaParameterSnapshot> mutatedParams = valid.Parameters
            .Where(p => p.Name != GisSharedParameters.GisDoor) // missing required gis_door
            .Select(p => p.Name == GisSharedParameters.GisAccessCon
                ? new RfaParameterSnapshot(p.Name, isShared: true, guid: Guid.NewGuid(), isInstance: true, p.StorageKind, p.DefaultValue) // wrong GUID + wrong instance binding
                : p)
            .ToList();

        RfaLintReport report = RfaLinter.Lint(CloneSnapshot(valid, parameters: mutatedParams), spec, kit);

        Assert.False(report.Passed);
        Assert.True(report.HasFinding(RfaLinter.RuleSharedParameters));
        IReadOnlyList<RfaLintFinding> r002 = report.FindingsByRule[RfaLinter.RuleSharedParameters];
        Assert.Contains(r002, f => f.Subject == GisSharedParameters.GisDoor);
        Assert.Contains(r002, f => f.Subject == GisSharedParameters.GisAccessCon && f.MessageEn.Contains("GUID"));
        Assert.Contains(r002, f => f.Subject == GisSharedParameters.GisAccessCon && f.MessageEn.Contains("bound as instance"));
    }

    [Fact]
    public void Rule003_FailsOnMissingKFootprintCoarseLeakOrUnapprovedSubcategory()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_MovingWalkway");

        // Case A: Missing K_Footprint altogether
        RfaInspectionSnapshot noFootprint = CloneSnapshot(
            valid,
            subcategories: valid.Subcategories.Where(s => s != CoarseGeometrySpec.FootprintSubcategory),
            solids: valid.Solids.Where(s => s.Subcategory != CoarseGeometrySpec.FootprintSubcategory));

        RfaLintReport reportA = RfaLinter.Lint(noFootprint, spec, kit);
        Assert.False(reportA.Passed);
        Assert.True(reportA.HasFinding(RfaLinter.RuleSubcategoryVisibility));

        // Case B: Medium/Fine solid visible in Coarse + unapproved subcategory + K_Footprint visible in Medium
        List<RfaSolidSnapshot> badSolids = valid.Solids.Select(s =>
        {
            if (s.Subcategory == CoarseGeometrySpec.FootprintSubcategory)
            {
                return new RfaSolidSnapshot(
                    s.Name, s.Subcategory, s.MaterialName,
                    visibleInCoarse: true, visibleInMedium: true, visibleInFine: false,
                    s.MinXMm, s.MaxXMm, s.MinYMm, s.MaxYMm, s.MinZMm, s.MaxZMm, s.TriangleCount, s.PlanarFaces);
            }

            if (s.Name == "walkway_deck_body")
            {
                return new RfaSolidSnapshot(
                    s.Name, "K_BogusSubcat", s.MaterialName,
                    visibleInCoarse: true, visibleInMedium: true, visibleInFine: true,
                    s.MinXMm, s.MaxXMm, s.MinYMm, s.MaxYMm, s.MinZMm, s.MaxZMm, s.TriangleCount, s.PlanarFaces);
            }

            return s;
        }).ToList();

        RfaLintReport reportB = RfaLinter.Lint(CloneSnapshot(valid, solids: badSolids), spec, kit);
        Assert.False(reportB.Passed);
        Assert.True(reportB.FindingsByRule[RfaLinter.RuleSubcategoryVisibility].Count >= 3);
    }

    [Fact]
    public void Rule004_FailsOnTiltedNormalOrElevatedBottomPlanarFace()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_Elevator");

        // Case A: Tilted face normal.Z = -0.5 (>= -0.9)
        RfaInspectionSnapshot tilted = ReplaceCoarseFaces(
            valid,
            new[] { new RfaPlanarFaceSnapshot(0.0, 0.866, -0.5, originZMm: 0.0, minZMm: 0.0, maxZMm: 0.0, areaSqMm: 4_000_000) });
        RfaLintReport tiltedReport = RfaLinter.Lint(tilted, spec, kit);
        Assert.False(tiltedReport.Passed);
        Assert.True(tiltedReport.HasFinding(RfaLinter.RuleCoarseDownwardBottomFace));

        // Case B: Downward face at Z = 15 mm (outside 1 mm tolerance)
        RfaInspectionSnapshot elevated = ReplaceCoarseFaces(
            valid,
            new[] { new RfaPlanarFaceSnapshot(0.0, 0.0, -1.0, originZMm: 15.0, minZMm: 15.0, maxZMm: 15.0, areaSqMm: 4_000_000) });
        RfaLintReport elevatedReport = RfaLinter.Lint(elevated, spec, kit);
        Assert.False(elevatedReport.Passed);
        Assert.True(elevatedReport.HasFinding(RfaLinter.RuleCoarseDownwardBottomFace));

        // Case C: Within 1 mm tolerance (Z = 0.5 mm, NormalZ = -0.95) passes
        RfaInspectionSnapshot withinTol = ReplaceCoarseFaces(
            valid,
            new[] { new RfaPlanarFaceSnapshot(0.0, 0.0, -0.95, originZMm: 0.5, minZMm: 0.5, maxZMm: 0.5, areaSqMm: 4_000_000) });
        RfaLintReport withinTolReport = RfaLinter.Lint(withinTol, spec, kit);
        Assert.False(withinTolReport.HasFinding(RfaLinter.RuleCoarseDownwardBottomFace));
    }

    [Fact]
    public void Rule005_FailsOnSlabSideAndGlassTuckClearanceViolations()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_ElevatorDoor");

        List<RfaSolidSnapshot> badSolids = valid.Solids.Select(s =>
        {
            if (s.Name == "jamb_left")
            {
                // Slab Z = 0 mm (< 4 mm) and X = 2 mm (< 8 mm)
                return new RfaSolidSnapshot(
                    s.Name, s.Subcategory, s.MaterialName,
                    s.VisibleInCoarse, s.VisibleInMedium, s.VisibleInFine,
                    minXMm: 2.0, maxXMm: 62.0, s.MinYMm, s.MaxYMm, minZMm: 0.0, s.MaxZMm, s.TriangleCount, s.PlanarFaces);
            }

            if (s.Name == "vision_slit_left")
            {
                // Glass tucked only 10 mm from Y=0 (< 8 + 8 = 16 mm)
                return new RfaSolidSnapshot(
                    s.Name, s.Subcategory, s.MaterialName,
                    s.VisibleInCoarse, s.VisibleInMedium, s.VisibleInFine,
                    s.MinXMm, s.MaxXMm, minYMm: 10.0, maxYMm: 42.0, s.MinZMm, s.MaxZMm, s.TriangleCount, s.PlanarFaces);
            }

            return s;
        }).ToList();

        RfaLintReport report = RfaLinter.Lint(CloneSnapshot(valid, solids: badSolids), spec, kit);

        Assert.False(report.Passed);
        IReadOnlyList<RfaLintFinding> r005 = report.FindingsByRule[RfaLinter.RuleAntiZFightingClearances];
        Assert.True(r005.Count >= 3);
        Assert.Contains(r005, f => f.Subject == "jamb_left" && f.MessageEn.Contains("slab"));
        Assert.Contains(r005, f => f.Subject == "jamb_left" && f.MessageEn.Contains("side"));
        Assert.Contains(r005, f => f.Subject == "vision_slit_left" && f.MessageEn.Contains("glass-tuck"));
    }

    [Fact]
    public void Rule006_FailsOnZeroOrOverBudgetTriangles()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_Escalator");

        RfaLintReport overBudget = RfaLinter.Lint(
            CloneSnapshot(valid, coarseTriangles: 40, mediumFineTriangles: 999),
            spec,
            kit);
        Assert.False(overBudget.Passed);
        Assert.Equal(2, overBudget.FindingsByRule[RfaLinter.RuleTriangleBudgets].Count);

        RfaLintReport zeroTris = RfaLinter.Lint(
            CloneSnapshot(valid, coarseTriangles: 0, mediumFineTriangles: 0),
            spec,
            kit);
        Assert.False(zeroTris.Passed);
        Assert.Equal(2, zeroTris.FindingsByRule[RfaLinter.RuleTriangleBudgets].Count);
    }

    [Fact]
    public void Rule007_FailsOnImportedCadOrEmbeddedRasterImages()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_Escalator");

        RfaLintReport report = RfaLinter.Lint(
            CloneSnapshot(valid, importedCadCount: 2, embeddedImageCount: 1),
            spec,
            kit);

        Assert.False(report.Passed);
        Assert.Equal(2, report.FindingsByRule[RfaLinter.RuleNoImportedCadOrImages].Count);
    }

    [Fact]
    public void Rule008_FailsOnUnapprovedNestedFamilyOrExcessiveNestingDepth()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_Elevator");

        RfaInspectionSnapshot badNested = CloneSnapshot(
            valid,
            nestedFamilies: new[]
            {
                new RfaNestedFamilySnapshot("Vendor_Heavy_Escalator_2019", nestingDepth: 1, category: "OST_GenericModel"),
                new RfaNestedFamilySnapshot("KNZ_CIRC_ElevatorDoor", nestingDepth: 3, category: "OST_Doors"),
            },
            maxNestingDepth: 3);

        RfaLintReport report = RfaLinter.Lint(badNested, spec, kit);

        Assert.False(report.Passed);
        IReadOnlyList<RfaLintFinding> r008 = report.FindingsByRule[RfaLinter.RuleNoUnapprovedNestedFamilies];
        Assert.Contains(r008, f => f.Subject == "Vendor_Heavy_Escalator_2019");
        Assert.Contains(r008, f => f.Subject == "maxNestingDepth");
        Assert.Contains(r008, f => f.Subject == "KNZ_CIRC_ElevatorDoor");
    }

    [Fact]
    public void Rule009_FailsOnUnnamedMaterialUnknownMaterialOrSrgbMismatch()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_MovingWalkway");

        List<RfaSolidSnapshot> badSolids = valid.Solids.Select(s =>
            s.Name == "walkway_deck_body"
                ? new RfaSolidSnapshot(
                    s.Name, s.Subcategory, materialName: string.Empty,
                    s.VisibleInCoarse, s.VisibleInMedium, s.VisibleInFine,
                    s.MinXMm, s.MaxXMm, s.MinYMm, s.MaxYMm, s.MinZMm, s.MaxZMm, s.TriangleCount, s.PlanarFaces)
                : s.Name == "handrail_left"
                    ? new RfaSolidSnapshot(
                        s.Name, s.Subcategory, materialName: "Default Grey",
                        s.VisibleInCoarse, s.VisibleInMedium, s.VisibleInFine,
                        s.MinXMm, s.MaxXMm, s.MinYMm, s.MaxYMm, s.MinZMm, s.MaxZMm, s.TriangleCount, s.PlanarFaces)
                    : s).ToList();

        List<RfaMaterialSnapshot> badMaterials = valid.Materials.Select(m =>
            m.Name == "K3D_Glass"
                ? new RfaMaterialSnapshot(m.Name, red: 10, green: 20, blue: 30, transparency: 0)
                : m).ToList();

        RfaLintReport report = RfaLinter.Lint(CloneSnapshot(valid, solids: badSolids, materials: badMaterials), spec, kit);

        Assert.False(report.Passed);
        IReadOnlyList<RfaLintFinding> r009 = report.FindingsByRule[RfaLinter.RuleApprovedMaterials];
        Assert.Contains(r009, f => f.Subject == "walkway_deck_body");
        Assert.Contains(r009, f => f.Subject == "handrail_left");
        Assert.Contains(r009, f => f.Subject == "K3D_Glass" && f.MessageEn.Contains("sRGB"));
        Assert.Contains(r009, f => f.Subject == "K3D_Glass" && f.MessageEn.Contains("transparency"));
    }

    [Fact]
    public void Rule010_FailsOnBakedIdLevelIdOrGisKeyDefaults()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_Escalator");

        List<RfaParameterSnapshot> badParams = valid.Parameters.Select(p =>
            p.Name == GisSharedParameters.Id
                ? new RfaParameterSnapshot(p.Name, p.IsShared, p.Guid, p.IsInstance, p.StorageKind, defaultValue: "baked-uuid-1234")
                : p.Name == GisSharedParameters.GisKey
                    ? new RfaParameterSnapshot(p.Name, p.IsShared, p.Guid, p.IsInstance, p.StorageKind, defaultValue: "model:uid:0")
                    : p).ToList();

        Dictionary<string, IReadOnlyDictionary<string, string>> typeValues = new(StringComparer.Ordinal)
        {
            ["Default"] = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [GisSharedParameters.LevelId] = "lvl-baked-001",
            },
        };

        RfaLintReport report = RfaLinter.Lint(CloneSnapshot(valid, parameters: badParams, typeValues: typeValues), spec, kit);

        Assert.False(report.Passed);
        IReadOnlyList<RfaLintFinding> r010 = report.FindingsByRule[RfaLinter.RuleNoBakedIdentity];
        Assert.Equal(3, r010.Count);
        Assert.Contains(r010, f => f.Subject == GisSharedParameters.Id);
        Assert.Contains(r010, f => f.Subject == GisSharedParameters.GisKey);
        Assert.Contains(r010, f => f.Subject == "Default.level_id");
    }

    [Fact]
    public void Rule011_FailsWhenFileSizeExceedsKitBudgetOrIsNonPositive()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_Elevator");

        long oversizedBytes = (kit.Header.Budgets.MaxRfaFileSizeKb + 50) * 1024L;
        RfaLintReport oversized = RfaLinter.Lint(CloneSnapshot(valid, fileSizeBytes: oversizedBytes), spec, kit);
        Assert.False(oversized.Passed);
        Assert.True(oversized.HasFinding(RfaLinter.RuleFileSizeBudget));

        RfaLintReport zeroBytes = RfaLinter.Lint(CloneSnapshot(valid, fileSizeBytes: 0), spec, kit);
        Assert.False(zeroBytes.Passed);
        Assert.True(zeroBytes.HasFinding(RfaLinter.RuleFileSizeBudget));
    }

    [Fact]
    public void Rule012_FailsOnInvalidGisDomainDefaultValues()
    {
        (StarterKitSpec kit, FamilyGeometrySpec spec, RfaInspectionSnapshot valid) = LoadValid("KNZ_CIRC_ElevatorDoor");

        List<RfaParameterSnapshot> badParams = valid.Parameters.Select(p =>
            p.Name == GisSharedParameters.GisDoor
                ? new RfaParameterSnapshot(p.Name, p.IsShared, p.Guid, p.IsInstance, p.StorageKind, defaultValue: "trapdoor_invalid")
                : p.Name == GisSharedParameters.GisOpeningCategory
                    ? new RfaParameterSnapshot(p.Name, p.IsShared, p.Guid, p.IsInstance, p.StorageKind, defaultValue: "teleport_pad")
                    : p).ToList();

        RfaLintReport report = RfaLinter.Lint(CloneSnapshot(valid, parameters: badParams), spec, kit);

        Assert.False(report.Passed);
        IReadOnlyList<RfaLintFinding> r012 = report.FindingsByRule[RfaLinter.RuleValidDomainDefaults];
        Assert.Contains(r012, f => f.Subject == GisSharedParameters.GisDoor && f.MessageEn.Contains("opening-types.json"));
        Assert.Contains(r012, f => f.Subject == GisSharedParameters.GisOpeningCategory && f.MessageEn.Contains("opening-types.json"));
    }

    [Fact]
    public void Rule013_FailsOnBrokenCirculationDimensionsOrMetadata()
    {
        // Escalator missing Rise and invalid gis_direction
        (StarterKitSpec kit, FamilyGeometrySpec escSpec, RfaInspectionSnapshot escValid) = LoadValid("KNZ_CIRC_Escalator");
        List<RfaParameterSnapshot> badEscParams = escValid.Parameters
            .Where(p => p.Name != "Rise")
            .Select(p => p.Name == "Width"
                ? new RfaParameterSnapshot("Width", false, null, false, "Length", "0", numericValueMm: 0.0)
                : p.Name == GisSharedParameters.GisDirection
                    ? new RfaParameterSnapshot(p.Name, p.IsShared, p.Guid, p.IsInstance, p.StorageKind, defaultValue: "sideways")
                    : p)
            .ToList();

        RfaLintReport escReport = RfaLinter.Lint(CloneSnapshot(escValid, parameters: badEscParams), escSpec, kit);
        Assert.False(escReport.Passed);
        IReadOnlyList<RfaLintFinding> r013Esc = escReport.FindingsByRule[RfaLinter.RuleCirculationContract];
        Assert.Contains(r013Esc, f => f.Subject == "Rise");
        Assert.Contains(r013Esc, f => f.Subject == "Width");
        Assert.Contains(r013Esc, f => f.Subject == GisSharedParameters.GisDirection);

        // Elevator missing gis_levels_served
        (_, FamilyGeometrySpec evSpec, RfaInspectionSnapshot evValid) = LoadValid("KNZ_CIRC_Elevator");
        List<RfaParameterSnapshot> badEvParams = evValid.Parameters
            .Where(p => p.Name != GisSharedParameters.GisLevelsServed)
            .ToList();

        RfaLintReport evReport = RfaLinter.Lint(CloneSnapshot(evValid, parameters: badEvParams), evSpec, kit);
        Assert.False(evReport.Passed);
        Assert.Contains(
            evReport.FindingsByRule[RfaLinter.RuleCirculationContract],
            f => f.Subject == GisSharedParameters.GisLevelsServed);
    }

    private static (StarterKitSpec Kit, FamilyGeometrySpec Spec, RfaInspectionSnapshot Snapshot) LoadValid(string familyId)
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec spec = kit.FindFamily(familyId)!;
        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kit);
        return (kit, spec, plan.BuildExpectedSnapshot(fileSizeBytes: 96 * 1024));
    }

    private static RfaInspectionSnapshot ReplaceCoarseFaces(
        RfaInspectionSnapshot source,
        IEnumerable<RfaPlanarFaceSnapshot> replacementFaces)
    {
        List<RfaSolidSnapshot> solids = source.Solids.Select(s =>
            s.Subcategory == CoarseGeometrySpec.FootprintSubcategory
                ? new RfaSolidSnapshot(
                    s.Name, s.Subcategory, s.MaterialName,
                    s.VisibleInCoarse, s.VisibleInMedium, s.VisibleInFine,
                    s.MinXMm, s.MaxXMm, s.MinYMm, s.MaxYMm, s.MinZMm, s.MaxZMm, s.TriangleCount, replacementFaces)
                : s).ToList();
        return CloneSnapshot(source, solids: solids);
    }

    private static RfaInspectionSnapshot CloneSnapshot(
        RfaInspectionSnapshot source,
        string? familyName = null,
        string? category = null,
        long? fileSizeBytes = null,
        IEnumerable<string>? subcategories = null,
        IEnumerable<RfaSolidSnapshot>? solids = null,
        IEnumerable<RfaMaterialSnapshot>? materials = null,
        IEnumerable<RfaParameterSnapshot>? parameters = null,
        int? coarseTriangles = null,
        int? mediumFineTriangles = null,
        int? importedCadCount = null,
        int? embeddedImageCount = null,
        IEnumerable<RfaNestedFamilySnapshot>? nestedFamilies = null,
        int? maxNestingDepth = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? typeValues = null)
    {
        return new RfaInspectionSnapshot(
            familyName ?? source.FamilyName,
            category ?? source.Category,
            source.SeedKind,
            fileSizeBytes ?? source.FileSizeBytes,
            subcategories ?? source.Subcategories,
            solids ?? source.Solids,
            materials ?? source.Materials,
            parameters ?? source.Parameters,
            coarseTriangles ?? source.CoarseTriangles,
            mediumFineTriangles ?? source.MediumFineTriangles,
            importedCadCount ?? source.ImportedCadCount,
            embeddedImageCount ?? source.EmbeddedImageCount,
            nestedFamilies ?? source.NestedFamilies,
            maxNestingDepth ?? source.MaxNestingDepth,
            typeValues ?? source.TypeParameterValues);
    }
}
