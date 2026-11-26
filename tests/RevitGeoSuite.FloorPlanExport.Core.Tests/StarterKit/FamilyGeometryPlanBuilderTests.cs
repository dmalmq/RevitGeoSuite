using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.StarterKit;

public sealed class FamilyGeometryPlanBuilderTests
{
    [Fact]
    public void EscalatorPlan_CompilesSlopedPrismsArrayAndPassesLinter()
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec spec = kit.FindFamily("KNZ_CIRC_Escalator")!;

        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kit);

        // 1 coarse footprint + (9 direct mediumFine ops + 16 expanded step treads = 25) = 26 primitives
        Assert.Single(plan.CoarsePrimitives);
        Assert.Equal(25, plan.MediumFinePrimitives.Count);
        Assert.Equal(26, plan.AllPrimitives.Count);

        // Coarse footprint check: Z=0, downward normal, Coarse-only
        CompiledExtrusionPrimitive footprint = plan.CoarsePrimitives[0];
        Assert.Equal("K_Footprint", footprint.Subcategory);
        Assert.True(footprint.VisibleInCoarse);
        Assert.False(footprint.VisibleInMedium);
        Assert.False(footprint.VisibleInFine);
        Assert.Equal(0.0, footprint.MinZMm, precision: 6);
        Assert.Equal(1200.0 * FamilyGeometryPlanBuilder.MillimetersToFeet, footprint.ProfileVertices[1].XFeet, precision: 9);
        Assert.Contains(footprint.PlanarFaces, f => f.NormalZ < -0.9 && Math.Abs(f.OriginZMm) <= 1.0);

        // Sloped prism truss check: sketched on YZ plane at X=8mm, extruded by width 1184mm
        CompiledExtrusionPrimitive truss = Assert.Single(plan.MediumFinePrimitives, p => p.Name == "truss_inclined_body");
        Assert.Equal(GeometryOpSpec.OpSlopedPrism, truss.OpKind);
        Assert.Equal(1.0, truss.SketchPlane.NormalX, precision: 9);
        Assert.Equal(1184.0, truss.ExtrusionDepthMm, precision: 6);
        Assert.Equal(1184.0 * FamilyGeometryPlanBuilder.MillimetersToFeet, truss.ExtrusionDepthFeet, precision: 9);
        Assert.Equal(4.0, truss.MinZMm, precision: 6);
        Assert.False(truss.VisibleInCoarse);
        Assert.True(truss.VisibleInMedium);
        Assert.True(truss.VisibleInFine);

        // Expanded array check: 16 step treads with deterministic step [0, 450, 262.5]
        List<CompiledExtrusionPrimitive> treads = plan.MediumFinePrimitives
            .Where(p => p.SourceOpName == "step_tread")
            .ToList();
        Assert.Equal(16, treads.Count);
        Assert.Equal(1200.0, treads[0].MinYMm, precision: 6);
        Assert.Equal(424.0, treads[0].MinZMm, precision: 6);
        Assert.Equal(1200.0 + (15 * 450.0), treads[15].MinYMm, precision: 6);
        Assert.Equal(424.0 + (15 * 262.5), treads[15].MinZMm, precision: 6);

        // Dimensional params include Width, Length, Height, Rise
        Assert.Contains(plan.DimensionParameters, d => d.Name == "Width" && Math.Abs(d.ValueMm - 1200.0) < 1e-6);
        Assert.Contains(plan.DimensionParameters, d => d.Name == "Length" && Math.Abs(d.ValueMm - 9600.0) < 1e-6);
        Assert.Contains(plan.DimensionParameters, d => d.Name == "Height" && Math.Abs(d.ValueMm - 4200.0) < 1e-6);
        Assert.Contains(plan.DimensionParameters, d => d.Name == "Rise" && Math.Abs(d.ValueMm - 4200.0) < 1e-6);

        Assert.True(plan.TriangleEstimate.WithinBudget);
        RfaLintReport report = RfaLinter.Lint(plan.BuildExpectedSnapshot(180 * 1024), spec, kit);
        Assert.True(report.Passed, string.Join("\n", report.Findings.Select(f => f.ToString())));
    }

    [Fact]
    public void MovingWalkwayPlan_ExpandsPalletRibsAndPassesLinter()
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec spec = kit.FindFamily("KNZ_CIRC_MovingWalkway")!;

        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kit);

        // 1 coarse footprint + (7 direct boxes + 8 pallet ribs = 15) = 16 primitives
        Assert.Single(plan.CoarsePrimitives);
        Assert.Equal(15, plan.MediumFinePrimitives.Count);
        Assert.Equal(8, plan.MediumFinePrimitives.Count(p => p.SourceOpName == "pallet_rib"));
        Assert.All(plan.MediumFinePrimitives, p => Assert.True(p.MinZMm >= 4.0));
        Assert.True(plan.TriangleEstimate.WithinBudget);

        RfaLintReport report = RfaLinter.Lint(plan.BuildExpectedSnapshot(140 * 1024), spec, kit);
        Assert.True(report.Passed, string.Join("\n", report.Findings.Select(f => f.ToString())));
    }

    [Fact]
    public void ElevatorAndElevatorDoorPlans_OrderSolidsBeforeVoidsAndPassLinter()
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();

        // Elevator: 1 coarse + 7 solids + 2 blind-pocket voids
        FamilyGeometrySpec evSpec = kit.FindFamily("KNZ_CIRC_Elevator")!;
        FamilyGeometryPlan evPlan = FamilyGeometryPlanBuilder.Build(evSpec, kit);
        Assert.Single(evPlan.CoarsePrimitives);
        Assert.Equal(9, evPlan.MediumFinePrimitives.Count);
        Assert.Equal(7, evPlan.MediumFinePrimitives.Count(p => p.IsSolid));
        Assert.Equal(2, evPlan.MediumFinePrimitives.Count(p => !p.IsSolid));

        // Verify voids come strictly after solids
        int lastSolidIdx = evPlan.MediumFinePrimitives.ToList().FindLastIndex(p => p.IsSolid);
        int firstVoidIdx = evPlan.MediumFinePrimitives.ToList().FindIndex(p => !p.IsSolid);
        Assert.True(lastSolidIdx < firstVoidIdx, "All solids must precede void cuts in deterministic execution order.");

        CompiledExtrusionPrimitive doorRecess = Assert.Single(evPlan.MediumFinePrimitives, p => p.Name == "door_recess_cut");
        Assert.False(doorRecess.IsSolid);
        Assert.False(doorRecess.ThroughCut);
        Assert.Equal("car_enclosure_body", doorRecess.TargetOpName);

        RfaLintReport evReport = RfaLinter.Lint(evPlan.BuildExpectedSnapshot(150 * 1024), evSpec, kit);
        Assert.True(evReport.Passed, string.Join("\n", evReport.Findings.Select(f => f.ToString())));

        // ElevatorDoor: 1 coarse + 8 solids + 2 through-cut voids
        FamilyGeometrySpec doorSpec = kit.FindFamily("KNZ_CIRC_ElevatorDoor")!;
        FamilyGeometryPlan doorPlan = FamilyGeometryPlanBuilder.Build(doorSpec, kit);
        Assert.Single(doorPlan.CoarsePrimitives);
        Assert.Equal(10, doorPlan.MediumFinePrimitives.Count);
        Assert.Equal(8, doorPlan.MediumFinePrimitives.Count(p => p.IsSolid));
        Assert.Equal(2, doorPlan.MediumFinePrimitives.Count(p => !p.IsSolid));
        Assert.All(doorPlan.MediumFinePrimitives.Where(p => !p.IsSolid), v => Assert.True(v.ThroughCut));

        RfaLintReport doorReport = RfaLinter.Lint(doorPlan.BuildExpectedSnapshot(160 * 1024), doorSpec, kit);
        Assert.True(doorReport.Passed, string.Join("\n", doorReport.Findings.Select(f => f.ToString())));
    }

    [Fact]
    public void CompiledCirculationManifestEntries_MatchExpectedManifestExact()
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        ExpectedManifest expected = ExpectedManifest.FromSpec(kit);

        string[] circulationIds =
        {
            "KNZ_CIRC_Escalator",
            "KNZ_CIRC_MovingWalkway",
            "KNZ_CIRC_Elevator",
            "KNZ_CIRC_ElevatorDoor",
        };

        foreach (string id in circulationIds)
        {
            FamilyGeometrySpec spec = kit.FindFamily(id)!;
            FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kit);
            ManifestFamilyEntry compiledEntry = plan.ToExpectedManifestEntry();
            ManifestFamilyEntry expectedEntry = Assert.Single(expected.Families, f => f.Id == id);

            Assert.Equal(expectedEntry.FamilyName, compiledEntry.FamilyName);
            Assert.Equal(expectedEntry.Category, compiledEntry.Category);
            Assert.Equal(expectedEntry.SeedKind, compiledEntry.SeedKind);
            Assert.Equal(expectedEntry.IsPolygonal, compiledEntry.IsPolygonal);
            Assert.Equal(expectedEntry.HasCoarseKFootprint, compiledEntry.HasCoarseKFootprint);
            Assert.Equal(expectedEntry.HasDownwardBottomFaceAtZero, compiledEntry.HasDownwardBottomFaceAtZero);
            Assert.Equal(expectedEntry.CoarseTriangles, compiledEntry.CoarseTriangles);
            Assert.Equal(expectedEntry.MediumFineTriangles, compiledEntry.MediumFineTriangles);
            Assert.Equal(expectedEntry.Subcategories, compiledEntry.Subcategories);
            Assert.Equal(expectedEntry.SharedParameterGuids, compiledEntry.SharedParameterGuids);
        }
    }

    [Fact]
    public void NPrismFamily_CompilesRegularPolygonVerticesAndUnitConversion()
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec roundCol = kit.FindFamily("KNZ_COL_Round")!;

        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(roundCol, kit);

        CompiledExtrusionPrimitive footprint = Assert.Single(plan.CoarsePrimitives);
        Assert.Equal(GeometryOpSpec.OpNPrism, footprint.OpKind);
        Assert.Equal(12, footprint.ProfileVertices.Count);
        Assert.Equal(0.0, footprint.MinZMm, precision: 6);
        Assert.Equal(44, footprint.EstimatedTriangles);
        Assert.Contains(footprint.PlanarFaces, f => f.NormalZ < -0.9 && Math.Abs(f.OriginZMm) <= 1.0);
    }

    [Fact]
    public void ScopedRestoreGuard_AlwaysRestoresOriginalPathEvenOnException()
    {
        string current = @"C:\Users\original\MySharedParams.txt";
        int setCount = 0;

        string Getter() => current;
        void Setter(string value)
        {
            current = value;
            setCount++;
        }

        // Normal dispose
        using (ScopedRestoreGuard guard = new(Getter, Setter, @"C:\Temp\KanizuGIS.SharedParameters.txt"))
        {
            Assert.Equal(@"C:\Temp\KanizuGIS.SharedParameters.txt", current);
            Assert.Equal(@"C:\Users\original\MySharedParams.txt", guard.OriginalValue);
            Assert.False(guard.IsRestored);
        }

        Assert.Equal(@"C:\Users\original\MySharedParams.txt", current);
        Assert.Equal(2, setCount);

        // Exception mid-build must STILL restore original path
        Action failingAction = () =>
        {
            using ScopedRestoreGuard guard = new(Getter, Setter, @"C:\Temp\KanizuGIS.SharedParameters.txt");
            Assert.Equal(@"C:\Temp\KanizuGIS.SharedParameters.txt", current);
            throw new InvalidOperationException("Simulated mid-build Revit failure.");
        };
        InvalidOperationException caught = Assert.Throws<InvalidOperationException>(failingAction);
        Assert.Contains("Simulated mid-build", caught.Message);
        Assert.Equal(@"C:\Users\original\MySharedParams.txt", current);

        // Double dispose is idempotent
        ScopedRestoreGuard doubleGuard = new(Getter, Setter, @"C:\Temp\Temp2.txt");
        int setsBeforeDispose = setCount;
        doubleGuard.Dispose();
        doubleGuard.Dispose();
        Assert.Equal(setsBeforeDispose + 1, setCount);
        Assert.Equal(@"C:\Users\original\MySharedParams.txt", current);
    }
}
