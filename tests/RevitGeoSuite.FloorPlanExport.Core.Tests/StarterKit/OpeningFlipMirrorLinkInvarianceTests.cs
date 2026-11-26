using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using Xunit;

namespace RevitGeoSuite.FloorPlanExport.Core.Tests.StarterKit;

/// <summary>
/// Pure-C# geometric and semantic invariance tests verifying that all Starter Kit wall-hosted doors,
/// openings, and shutters preserve exact opening span width, canonical non-swapped centerline endpoints,
/// valid CCW Coarse <c>K_Footprint</c> projection, and v3 opening classification across every combination
/// of hand-flip, facing-flip, mirroring (negative determinant), and rotated/translated/mirrored linked-model transforms.
/// </summary>
public sealed class OpeningFlipMirrorLinkInvarianceTests
{
    public static IEnumerable<object[]> AllStarterKitWallHostedOpenings()
    {
        yield return new object[] { "KNZ_OPEN_DoorSwing", "swinging", "pedestrian", false, 900.0, 180.0 };
        yield return new object[] { "KNZ_OPEN_DoorSlide", "sliding", "pedestrian", false, 1800.0, 200.0 };
        yield return new object[] { "KNZ_OPEN_DoorAuto", "automatic", "pedestrian.transit", false, 2400.0, 220.0 };
        yield return new object[] { "KNZ_OPEN_Shutter", "shutter", "pedestrian", false, 3000.0, 240.0 };
        yield return new object[] { "KNZ_OPEN_Opening", "open", "pedestrian", false, 2000.0, 200.0 };
        yield return new object[] { "KNZ_CIRC_ElevatorDoor", "elevator", "pedestrian", true, 1100.0, 200.0 };
    }

    [Theory]
    [MemberData(nameof(AllStarterKitWallHostedOpenings))]
    public void FlipAndMirrorMatrix_PreservesExactWidthCanonicalCenterlineCcwFootprintAndClassification(
        string familyId,
        string expectedDoorType,
        string expectedCategory,
        bool expectedIsElevator,
        double expectedWidthMm,
        double expectedDepthMm)
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec spec = kit.FindFamily(familyId)!;
        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kit);

        Point2D origin = new(12500.0, -4300.0);
        double[] wallAngles = { 0.0, 45.0, 90.0, 180.0, 215.0 };
        (bool Hand, bool Facing, bool Mirrored, double MirrorAxis)[] flipStates =
        {
            (false, false, false, 90.0),
            (true, false, false, 90.0),
            (false, true, false, 90.0),
            (true, true, false, 90.0),
            (false, false, true, 90.0),
            (true, false, true, 90.0),
            (false, true, true, 90.0),
            (true, true, true, 90.0),
            (false, false, true, 37.0),
            (true, true, true, 0.0),
        };

        bool sawNegativeDeterminant = false;
        bool sawPositiveDeterminant = false;

        foreach (double wallAngle in wallAngles)
        {
            // Baseline unflipped, unmirrored placement at this wall angle
            ProjectedOpeningGeometry baseline = OpeningGeometryHelper.ProjectStarterKitOpening(
                plan,
                new OpeningPlacement2D(origin, wallAngle));

            foreach ((bool handFlipped, bool facingFlipped, bool mirrored, double mirrorAxis) in flipStates)
            {
                OpeningPlacement2D placement = new(
                    origin,
                    wallAngle,
                    handFlipped,
                    facingFlipped,
                    mirrored,
                    mirrorAxis);

                ProjectedOpeningGeometry projected = OpeningGeometryHelper.ProjectStarterKitOpening(plan, placement);

                if (projected.CombinedTransformDeterminant < 0.0)
                {
                    sawNegativeDeterminant = true;
                }
                else
                {
                    sawPositiveDeterminant = true;
                }

                // 1. Exact invariant span width (never zero-length, never distorted)
                Assert.Equal(expectedWidthMm, projected.EffectiveWidthMm, precision: 6);
                Assert.Equal(2, projected.CenterlineMm.Points.Count);

                // 2. Exact midpoint at placement origin
                Assert.Equal(origin.X, projected.MidpointMm.X, precision: 6);
                Assert.Equal(origin.Y, projected.MidpointMm.Y, precision: 6);

                // 3. Unmirrored flips (HandFlipped / FacingFlipped / BothFlipped) produce IDENTICAL
                //    canonicalized centerline endpoints — zero endpoint swapping!
                if (!mirrored)
                {
                    Assert.Equal(baseline.CenterlineMm.Points[0].X, projected.CenterlineMm.Points[0].X, precision: 6);
                    Assert.Equal(baseline.CenterlineMm.Points[0].Y, projected.CenterlineMm.Points[0].Y, precision: 6);
                    Assert.Equal(baseline.CenterlineMm.Points[1].X, projected.CenterlineMm.Points[1].X, precision: 6);
                    Assert.Equal(baseline.CenterlineMm.Points[1].Y, projected.CenterlineMm.Points[1].Y, precision: 6);
                }

                // 4. Coarse K_Footprint projection is ALWAYS a valid closed CCW polygon with exact area
                Assert.Equal(5, projected.FootprintPolygonMm.ExteriorRing.Count);
                Assert.True(
                    projected.FootprintSignedAreaSqMm > 0.0,
                    $"Expected CCW positive signed area for {familyId} (det={projected.CombinedTransformDeterminant}).");
                Assert.Equal(expectedWidthMm * expectedDepthMm, projected.FootprintSignedAreaSqMm, precision: 3);

                // Every edge of the footprint polygon must be non-degenerate (either widthMm or depthMm)
                for (int i = 0; i < 4; i++)
                {
                    Point2D a = projected.FootprintPolygonMm.ExteriorRing[i];
                    Point2D b = projected.FootprintPolygonMm.ExteriorRing[i + 1];
                    double edgeLen = Math.Sqrt(((b.X - a.X) * (b.X - a.X)) + ((b.Y - a.Y) * (b.Y - a.Y)));
                    bool matchesWidth = Math.Abs(edgeLen - expectedWidthMm) < 1e-5;
                    bool matchesDepth = Math.Abs(edgeLen - expectedDepthMm) < 1e-5;
                    Assert.True(matchesWidth || matchesDepth, $"Unexpected footprint edge length {edgeLen} mm.");
                }

                // 5. Semantic classification invariant under all flips and mirrors
                Assert.Equal(expectedDoorType, projected.DoorType);
                Assert.Equal(expectedCategory, projected.OpeningCategory);
                Assert.Equal(expectedIsElevator, projected.IsElevatorDoor);
            }
        }

        Assert.True(sawNegativeDeterminant);
        Assert.True(sawPositiveDeterminant);
    }

    [Theory]
    [MemberData(nameof(AllStarterKitWallHostedOpenings))]
    public void LinkedModelTransformMatrix_RotatedTranslatedAndMirroredLinks_PreserveExactWidthAndCcwFootprint(
        string familyId,
        string expectedDoorType,
        string expectedCategory,
        bool expectedIsElevator,
        double expectedWidthMm,
        double expectedDepthMm)
    {
        StarterKitSpec kit = StarterKitSpecLoaderTests.LoadShippedSpec();
        FamilyGeometrySpec spec = kit.FindFamily(familyId)!;
        FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kit);

        (string Name, AffineTransform2D Transform, double ExpectedDet)[] linkTransforms =
        {
            ("Identity", OpeningGeometryHelper.CreateRigidTransform(0.0, new Point2D(0.0, 0.0)), +1.0),
            ("Rotated90_Translated", OpeningGeometryHelper.CreateRigidTransform(90.0, new Point2D(50000.0, -18000.0)), +1.0),
            ("Rotated45_Translated", OpeningGeometryHelper.CreateRigidTransform(45.0, new Point2D(-25000.0, 31000.0)), +1.0),
            ("Rotated180_Translated", OpeningGeometryHelper.CreateRigidTransform(180.0, new Point2D(10000.0, 10000.0)), +1.0),
            ("MirroredY_Translated", OpeningGeometryHelper.CreateReflectionTransform(90.0, new Point2D(40000.0, 12000.0)), -1.0),
            ("Mirrored45_Translated", OpeningGeometryHelper.CreateReflectionTransform(45.0, new Point2D(-15000.0, -9000.0)), -1.0),
        };

        OpeningPlacement2D[] placements =
        {
            new(new Point2D(3200.0, 1400.0), wallAngleDegrees: 0.0),
            new(new Point2D(3200.0, 1400.0), wallAngleDegrees: 30.0, handFlipped: true),
            new(new Point2D(3200.0, 1400.0), wallAngleDegrees: 90.0, facingFlipped: true),
            new(new Point2D(3200.0, 1400.0), wallAngleDegrees: 135.0, handFlipped: true, facingFlipped: true),
            new(new Point2D(3200.0, 1400.0), wallAngleDegrees: 45.0, mirrored: true),
            new(new Point2D(3200.0, 1400.0), wallAngleDegrees: 60.0, handFlipped: true, mirrored: true),
        };

        foreach ((string linkName, AffineTransform2D linkTransform, double expectedLinkDet) in linkTransforms)
        {
            Assert.Equal(expectedLinkDet, OpeningGeometryHelper.Determinant(linkTransform), precision: 9);

            foreach (OpeningPlacement2D placement in placements)
            {
                ProjectedOpeningGeometry projected = OpeningGeometryHelper.ProjectStarterKitOpening(
                    plan,
                    placement,
                    linkTransform);

                // Exact invariant opening width through linked-model transform
                Assert.Equal(expectedWidthMm, projected.EffectiveWidthMm, precision: 6);

                // Midpoint must equal linkTransform(placement.OriginMm)
                Point2D expectedMidpoint = linkTransform.Transform(placement.OriginMm);
                Assert.Equal(expectedMidpoint.X, projected.MidpointMm.X, precision: 6);
                Assert.Equal(expectedMidpoint.Y, projected.MidpointMm.Y, precision: 6);

                // Endpoints must be in canonical lexicographical order (Start <= End)
                Point2D start = projected.CenterlineMm.Points[0];
                Point2D end = projected.CenterlineMm.Points[1];
                Assert.True(
                    start.X < end.X - 1e-9 || (Math.Abs(start.X - end.X) <= 1e-9 && start.Y <= end.Y + 1e-9),
                    $"Centerline endpoints not canonically ordered for {familyId} under link '{linkName}'.");

                // Coarse K_Footprint projection must stay CCW with exact area even under mirrored link + mirrored door
                Assert.True(projected.FootprintSignedAreaSqMm > 0.0);
                Assert.Equal(expectedWidthMm * expectedDepthMm, projected.FootprintSignedAreaSqMm, precision: 3);

                // Footprint centroid must equal midpoint (confirming centered wall-hosted symmetry!)
                double cx = 0.0;
                double cy = 0.0;
                for (int i = 0; i < 4; i++)
                {
                    cx += projected.FootprintPolygonMm.ExteriorRing[i].X;
                    cy += projected.FootprintPolygonMm.ExteriorRing[i].Y;
                }

                Assert.Equal(expectedMidpoint.X, cx / 4.0, precision: 6);
                Assert.Equal(expectedMidpoint.Y, cy / 4.0, precision: 6);

                // Classification stays invariant across linked transforms
                Assert.Equal(expectedDoorType, projected.DoorType);
                Assert.Equal(expectedCategory, projected.OpeningCategory);
                Assert.Equal(expectedIsElevator, projected.IsElevatorDoor);
            }
        }
    }

    [Fact]
    public void ResolveOpeningAxis2D_HandlesDegenerateZeroAndNonFiniteOrientationsSafely()
    {
        // Normal HandOrientation normalized
        Point2D normal = OpeningGeometryHelper.ResolveOpeningAxis2D(3.0, 4.0, 0.0, 1.0);
        Assert.Equal(0.6, normal.X, precision: 9);
        Assert.Equal(0.8, normal.Y, precision: 9);

        // Zero HandOrientation falls back to perpendicular of FacingOrientation (-fy, fx)
        Point2D fromFacing = OpeningGeometryHelper.ResolveOpeningAxis2D(0.0, 0.0, 0.0, 1.0);
        Assert.Equal(-1.0, fromFacing.X, precision: 9);
        Assert.Equal(0.0, fromFacing.Y, precision: 9);

        // Non-finite HandOrientation falls back to perpendicular of FacingOrientation
        Point2D fromFacingAfterNaN = OpeningGeometryHelper.ResolveOpeningAxis2D(double.NaN, 0.0, 1.0, 0.0);
        Assert.Equal(0.0, fromFacingAfterNaN.X, precision: 9);
        Assert.Equal(1.0, fromFacingAfterNaN.Y, precision: 9);

        // Both degenerate falls back to +X
        Point2D fallbackX = OpeningGeometryHelper.ResolveOpeningAxis2D(0.0, 0.0, 0.0, 0.0);
        Assert.Equal(1.0, fallbackX.X, precision: 9);
        Assert.Equal(0.0, fallbackX.Y, precision: 9);

        // Hand-flipped axis produces identical canonical endpoints
        Point2D center = new(1000.0, 2000.0);
        (Point2D s1, Point2D e1) = OpeningGeometryHelper.ComputeCenterlineEndpoints(center, 900.0, new Point2D(1.0, 0.0), canonicalizeOrder: true);
        (Point2D s2, Point2D e2) = OpeningGeometryHelper.ComputeCenterlineEndpoints(center, 900.0, new Point2D(-1.0, 0.0), canonicalizeOrder: true);
        Assert.Equal(s1.X, s2.X, precision: 9);
        Assert.Equal(s1.Y, s2.Y, precision: 9);
        Assert.Equal(e1.X, e2.X, precision: 9);
        Assert.Equal(e1.Y, e2.Y, precision: 9);
    }
}
