using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.StarterKit;

public sealed class TriangleEstimatorTests
{
    [Fact]
    public void Box_Returns12Triangles()
    {
        GeometryOpSpec op = new(
            GeometryOpSpec.OpBox,
            "unit_box",
            sizeMm: new[] { 1000.0, 500.0, 200.0 });

        Assert.Equal(12, TriangleEstimator.EstimateOp(op));
    }

    [Theory]
    [InlineData(3, 8)]
    [InlineData(4, 12)]
    [InlineData(6, 20)]
    [InlineData(8, 28)]
    [InlineData(12, 44)]
    [InlineData(16, 60)]
    public void NPrism_ReturnsFourNMinusFour(int sides, int expectedTriangles)
    {
        GeometryOpSpec op = new(
            GeometryOpSpec.OpNPrism,
            "prism",
            radiusMm: 300.0,
            heightMm: 2000.0,
            sides: sides);

        Assert.Equal(expectedTriangles, TriangleEstimator.EstimateOp(op));
    }

    [Theory]
    [InlineData(null, 12)]
    [InlineData(4, 12)]
    [InlineData(6, 20)]
    public void SlopedPrism_Returns12ForDefaultAndFourNMinusFourForNSides(int? sides, int expectedTriangles)
    {
        GeometryOpSpec op = new(
            GeometryOpSpec.OpSlopedPrism,
            "truss",
            widthMm: 1200.0,
            runMm: 7200.0,
            riseMm: 4200.0,
            thicknessMm: 400.0,
            sides: sides);

        Assert.Equal(expectedTriangles, TriangleEstimator.EstimateOp(op));
    }

    [Fact]
    public void Array_MultipliesSingleAndMultipleChildrenDeterministically()
    {
        GeometryOpSpec box = new(GeometryOpSpec.OpBox, "slat", sizeMm: new[] { 100.0, 20.0, 10.0 });
        GeometryOpSpec singleChildArray = new(GeometryOpSpec.OpArray, "slats", count: 5, child: box);
        Assert.Equal(60, TriangleEstimator.EstimateOp(singleChildArray));

        GeometryOpSpec prism = new(GeometryOpSpec.OpNPrism, "post", radiusMm: 30.0, heightMm: 1000.0, sides: 6);
        GeometryOpSpec multiChildArray = new(
            GeometryOpSpec.OpArray,
            "bay",
            count: 3,
            children: new[] { box, prism });
        Assert.Equal(3 * (12 + 20), TriangleEstimator.EstimateOp(multiChildArray));

        GeometryOpSpec nestedArray = new(
            GeometryOpSpec.OpArray,
            "grid",
            count: 2,
            child: singleChildArray);
        Assert.Equal(120, TriangleEstimator.EstimateOp(nestedArray));
    }

    [Theory]
    [InlineData("box", null, true, 20)]
    [InlineData("box", null, false, 16)]
    [InlineData("nprism", 6, true, 28)]
    [InlineData("nprism", 6, false, 24)]
    public void Void_ReturnsDocumentedExactCutDelta(string shape, int? sides, bool throughCut, int expectedTriangles)
    {
        GeometryOpSpec op = new(
            GeometryOpSpec.OpVoid,
            "cut",
            sizeMm: shape == "box" ? new[] { 500.0, 200.0, 1000.0 } : null,
            radiusMm: shape == "nprism" ? 200.0 : (double?)null,
            heightMm: shape == "nprism" ? 500.0 : (double?)null,
            sides: sides,
            shape: shape,
            throughCut: throughCut);

        Assert.Equal(expectedTriangles, TriangleEstimator.EstimateOp(op));
    }

    [Fact]
    public void InvalidOpParameters_AreRejected()
    {
        Assert.Throws<ArgumentException>(() => TriangleEstimator.EstimateOp(
            new GeometryOpSpec("cylinder", "bad_kind")));

        Assert.Throws<ArgumentException>(() => TriangleEstimator.EstimateOp(
            new GeometryOpSpec(GeometryOpSpec.OpBox, "zero_box", sizeMm: new[] { 100.0, 0.0, 100.0 })));

        Assert.Throws<ArgumentException>(() => TriangleEstimator.EstimateOp(
            new GeometryOpSpec(GeometryOpSpec.OpNPrism, "two_sided", radiusMm: 100.0, heightMm: 100.0, sides: 2)));

        Assert.Throws<ArgumentException>(() => TriangleEstimator.EstimateOp(
            new GeometryOpSpec(GeometryOpSpec.OpSlopedPrism, "neg_width", widthMm: -10.0, runMm: 100.0, thicknessMm: 50.0)));

        Assert.Throws<ArgumentException>(() => TriangleEstimator.EstimateOp(
            new GeometryOpSpec(GeometryOpSpec.OpArray, "zero_count", count: 0, child: new GeometryOpSpec(GeometryOpSpec.OpBox, "b", sizeMm: new[] { 1.0, 1.0, 1.0 }))));

        Assert.Throws<ArgumentException>(() => TriangleEstimator.EstimateOp(
            new GeometryOpSpec(GeometryOpSpec.OpArray, "empty_array", count: 3)));

        Assert.Throws<ArgumentException>(() => TriangleEstimator.EstimateOp(
            new GeometryOpSpec(GeometryOpSpec.OpVoid, "bad_void_shape", shape: "sphere")));

        Assert.Throws<ArgumentOutOfRangeException>(() => TriangleEstimator.PrismTriangles(2));
        Assert.Throws<ArgumentOutOfRangeException>(() => TriangleEstimator.VoidCutTriangles(2, throughCut: true));
    }

    [Fact]
    public void EveryShippedStarterKitFamily_PassesItsTriangleBudget()
    {
        StarterKitSpec spec = StarterKitSpecLoaderTests.LoadShippedSpec();
        IReadOnlyList<FamilyTriangleEstimate> estimates = TriangleEstimator.EstimateKit(spec);

        Assert.Equal(spec.Families.Count, estimates.Count);
        foreach (FamilyTriangleEstimate est in estimates)
        {
            Assert.True(est.CoarseTriangles > 0, $"{est.FamilyId} should have positive coarse triangle count.");
            Assert.True(est.MediumFineTriangles > 0, $"{est.FamilyId} should have positive mediumFine triangle count.");
            Assert.True(
                est.CoarseWithinBudget,
                $"{est.FamilyId} coarse triangles {est.CoarseTriangles} exceeded budget {est.CoarseBudget}.");
            Assert.True(
                est.MediumFineWithinBudget,
                $"{est.FamilyId} mediumFine triangles {est.MediumFineTriangles} exceeded budget {est.MediumFineBudget}.");
            Assert.True(est.WithinBudget);
            Assert.True(est.CoarseTriangles <= spec.Header.Budgets.MaxCoarseTrianglesPerFamily);
            Assert.True(est.MediumFineTriangles <= spec.Header.Budgets.MaxMediumTrianglesPerFamily);
        }
    }

    [Fact]
    public void InflatedArrayCount_ExceedsBudgetAndFailsValidation()
    {
        StarterKitSpec shipped = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec esc = shipped.FindFamily("KNZ_CIRC_Escalator")!;

        List<GeometryOpSpec> inflatedOps = esc.MediumFine.Ops
            .Select(op => op.Op == GeometryOpSpec.OpArray
                ? new GeometryOpSpec(
                    GeometryOpSpec.OpArray,
                    op.Name,
                    count: 100, // 100 * 12 = 1200 tris, blowing past both family (360) and kit (600) budgets
                    stepMm: op.StepMm,
                    child: op.Child)
                : op)
            .ToList();

        FamilyGeometrySpec inflatedEsc = new(
            esc.Schema,
            esc.SchemaVersion,
            esc.Id,
            esc.FamilyName,
            esc.DisplayNameEn,
            esc.DisplayNameJa,
            esc.Group,
            esc.Category,
            esc.SeedKind,
            esc.IsPolygonal,
            esc.Subcategories,
            esc.ClearancesMm,
            esc.DefaultDimensionsMm,
            esc.SharedParameters,
            esc.TriangleBudget,
            esc.Coarse,
            new MediumFineGeometrySpec(inflatedOps));

        FamilyTriangleEstimate est = TriangleEstimator.EstimateFamily(inflatedEsc);
        Assert.False(est.MediumFineWithinBudget);
        Assert.False(est.WithinBudget);
        Assert.True(est.MediumFineTriangles > esc.TriangleBudget.MediumFineMax);

        List<FamilyGeometrySpec> mutatedFamilies = shipped.Families
            .Select(f => f.Id == esc.Id ? inflatedEsc : f)
            .ToList();
        StarterKitValidationResult validation = StarterKitSpecLoader.Validate(
            new StarterKitSpec(shipped.Header, shipped.Rte, shipped.Materials3D, mutatedFamilies));

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, e => e.Code == "SK-TRI-002");
        Assert.Contains(validation.Errors, e => e.Code == "SK-TRI-004");
    }
}
