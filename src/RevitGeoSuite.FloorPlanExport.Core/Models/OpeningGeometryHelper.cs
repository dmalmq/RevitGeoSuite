using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.Core.Models;

/// <summary>
/// Pure-C# description of a Revit door/opening placement including flips, mirroring, and wall angle.
/// Models Revit's <c>FamilyInstance</c> <c>HandOrientation</c>, <c>FacingOrientation</c>, and
/// <c>GetTransform()</c> behavior so Starter Kit opening width, orientation, and <c>K_Footprint</c>
/// invariance can be rigorously verified without a live Revit process.
/// </summary>
public readonly struct OpeningPlacement2D
{
    public OpeningPlacement2D(
        Point2D originMm,
        double wallAngleDegrees,
        bool handFlipped = false,
        bool facingFlipped = false,
        bool mirrored = false,
        double mirrorAxisAngleDegrees = 90.0)
    {
        OriginMm = originMm;
        WallAngleDegrees = wallAngleDegrees;
        HandFlipped = handFlipped;
        FacingFlipped = facingFlipped;
        Mirrored = mirrored;
        MirrorAxisAngleDegrees = mirrorAxisAngleDegrees;
    }

    public Point2D OriginMm { get; }

    public double WallAngleDegrees { get; }

    public bool HandFlipped { get; }

    public bool FacingFlipped { get; }

    public bool Mirrored { get; }

    /// <summary>Angle of the reflection line in degrees when <see cref="Mirrored"/> is true (default 90 deg = Y axis).</summary>
    public double MirrorAxisAngleDegrees { get; }

    /// <summary>
    /// Computes the instance's 2D <c>HandOrientation</c>, <c>FacingOrientation</c>, and local-to-model
    /// <see cref="AffineTransform2D"/> in millimetres.
    /// </summary>
    public void Evaluate(out Point2D handOrientation, out Point2D facingOrientation, out AffineTransform2D transform)
    {
        double rad = WallAngleDegrees * (Math.PI / 180.0);
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);

        double handSign = HandFlipped ? -1.0 : 1.0;
        double facingSign = FacingFlipped ? -1.0 : 1.0;

        Point2D hx = new(cos * handSign, sin * handSign);
        Point2D fy = new(-sin * facingSign, cos * facingSign);

        if (Mirrored)
        {
            AffineTransform2D reflection = OpeningGeometryHelper.CreateReflectionTransform(MirrorAxisAngleDegrees, new Point2D(0.0, 0.0));
            hx = reflection.TransformVector(hx);
            fy = reflection.TransformVector(fy);
        }

        handOrientation = hx;
        facingOrientation = fy;
        transform = new AffineTransform2D(OriginMm, hx, fy);
    }
}

/// <summary>
/// Result of projecting a Starter Kit opening's centerline, Coarse <c>K_Footprint</c> polygon,
/// and v3 semantic classification through instance flip/mirror + optional linked-model transforms.
/// </summary>
public sealed class ProjectedOpeningGeometry
{
    public ProjectedOpeningGeometry(
        string familyId,
        LineString2D centerlineMm,
        double effectiveWidthMm,
        Point2D midpointMm,
        Polygon2D footprintPolygonMm,
        double footprintSignedAreaSqMm,
        double combinedTransformDeterminant,
        string doorType,
        string openingCategory,
        bool isElevatorDoor)
    {
        FamilyId = familyId ?? string.Empty;
        CenterlineMm = centerlineMm ?? throw new ArgumentNullException(nameof(centerlineMm));
        EffectiveWidthMm = effectiveWidthMm;
        MidpointMm = midpointMm;
        FootprintPolygonMm = footprintPolygonMm ?? throw new ArgumentNullException(nameof(footprintPolygonMm));
        FootprintSignedAreaSqMm = footprintSignedAreaSqMm;
        CombinedTransformDeterminant = combinedTransformDeterminant;
        DoorType = doorType ?? string.Empty;
        OpeningCategory = openingCategory ?? string.Empty;
        IsElevatorDoor = isElevatorDoor;
    }

    public string FamilyId { get; }

    public LineString2D CenterlineMm { get; }

    public double EffectiveWidthMm { get; }

    public Point2D MidpointMm { get; }

    public Polygon2D FootprintPolygonMm { get; }

    public double FootprintSignedAreaSqMm { get; }

    public double CombinedTransformDeterminant { get; }

    public string DoorType { get; }

    public string OpeningCategory { get; }

    public bool IsElevatorDoor { get; }
}

/// <summary>
/// Pure-C# opening centerline, orientation, and Coarse <c>K_Footprint</c> projection helpers shared by
/// Revit's <c>OpeningExtractor</c> and Linux unit tests covering flipped, mirrored, and linked placements.
/// </summary>
public static class OpeningGeometryHelper
{
    private const double AxisLengthEpsilon = 1e-6;
    private const double CanonicalOrderEpsilon = 1e-9;

    /// <summary>
    /// Resolves the unit 2D opening span axis from Revit's <c>HandOrientation</c> (projected onto XY),
    /// falling back to the 90-degree perpendicular of <c>FacingOrientation</c>, and finally <c>(1, 0)</c>.
    /// </summary>
    public static Point2D ResolveOpeningAxis2D(double handX, double handY, double facingX, double facingY)
    {
        if (IsFinite(handX) && IsFinite(handY))
        {
            double handLen = Math.Sqrt((handX * handX) + (handY * handY));
            if (handLen > AxisLengthEpsilon)
            {
                return new Point2D(handX / handLen, handY / handLen);
            }
        }

        if (IsFinite(facingX) && IsFinite(facingY))
        {
            double perpX = -facingY;
            double perpY = facingX;
            double perpLen = Math.Sqrt((perpX * perpX) + (perpY * perpY));
            if (perpLen > AxisLengthEpsilon)
            {
                return new Point2D(perpX / perpLen, perpY / perpLen);
            }
        }

        return new Point2D(1.0, 0.0);
    }

    /// <summary>
    /// Computes the two endpoints of an opening centerline of length <paramref name="width"/> centered at
    /// <paramref name="center"/> along <paramref name="axis"/>. When <paramref name="canonicalizeOrder"/> is true,
    /// orders <c>(Start, End)</c> deterministically by <c>(X, Y)</c> so hand-flips and mirrors never swap endpoints.
    /// </summary>
    public static (Point2D Start, Point2D End) ComputeCenterlineEndpoints(
        Point2D center,
        double width,
        Point2D axis,
        bool canonicalizeOrder = true)
    {
        if (!IsFinite(width) || width <= 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(width), width, "Opening width must be finite and positive.");
        }

        Point2D unitAxis = ResolveOpeningAxis2D(axis.X, axis.Y, 0.0, 0.0);
        double half = width * 0.5;
        Point2D start = new(center.X - (unitAxis.X * half), center.Y - (unitAxis.Y * half));
        Point2D end = new(center.X + (unitAxis.X * half), center.Y + (unitAxis.Y * half));

        return canonicalizeOrder ? CanonicalizeSegmentOrder(start, end) : (start, end);
    }

    /// <summary>Orders a 2-point segment deterministically so <c>Start &lt;= End</c> lexicographically by <c>(X, Y)</c>.</summary>
    public static (Point2D Start, Point2D End) CanonicalizeSegmentOrder(Point2D first, Point2D second)
    {
        if (first.X < second.X - CanonicalOrderEpsilon)
        {
            return (first, second);
        }

        if (second.X < first.X - CanonicalOrderEpsilon)
        {
            return (second, first);
        }

        return first.Y <= second.Y + CanonicalOrderEpsilon ? (first, second) : (second, first);
    }

    /// <summary>Transforms a 2D segment through a rigid or mirrored <see cref="AffineTransform2D"/>.</summary>
    public static (Point2D Start, Point2D End) TransformCenterline(
        Point2D start,
        Point2D end,
        AffineTransform2D transform,
        bool canonicalizeOrder = true)
    {
        Point2D tStart = transform.Transform(start);
        Point2D tEnd = transform.Transform(end);
        return canonicalizeOrder ? CanonicalizeSegmentOrder(tStart, tEnd) : (tStart, tEnd);
    }

    /// <summary>Creates a 2D rigid rotation + translation transform (determinant = +1).</summary>
    public static AffineTransform2D CreateRigidTransform(double angleDegrees, Point2D translation)
    {
        double rad = angleDegrees * (Math.PI / 180.0);
        double cos = Math.Cos(rad);
        double sin = Math.Sin(rad);
        return new AffineTransform2D(translation, new Point2D(cos, sin), new Point2D(-sin, cos));
    }

    /// <summary>Creates a 2D reflection across a line at <paramref name="mirrorAxisAngleDegrees"/> plus translation (determinant = -1).</summary>
    public static AffineTransform2D CreateReflectionTransform(double mirrorAxisAngleDegrees, Point2D translation)
    {
        double twoTheta = 2.0 * mirrorAxisAngleDegrees * (Math.PI / 180.0);
        double c = Math.Cos(twoTheta);
        double s = Math.Sin(twoTheta);
        // Reflection matrix across unit direction (cos θ, sin θ):
        //   [ cos 2θ   sin 2θ ]
        //   [ sin 2θ  -cos 2θ ]
        return new AffineTransform2D(translation, new Point2D(c, s), new Point2D(s, -c));
    }

    /// <summary>Composes two 2D affine transforms (<paramref name="outer"/> applied after <paramref name="inner"/>).</summary>
    public static AffineTransform2D Compose(AffineTransform2D outer, AffineTransform2D inner)
    {
        Point2D origin = outer.Transform(inner.Origin);
        Point2D xBasis = outer.TransformVector(inner.XBasis);
        Point2D yBasis = outer.TransformVector(inner.YBasis);
        return new AffineTransform2D(origin, xBasis, yBasis);
    }

    /// <summary>Returns the determinant of a 2D affine transform's linear basis (<c>&lt; 0</c> when mirrored/flipped).</summary>
    public static double Determinant(AffineTransform2D transform) =>
        (transform.XBasis.X * transform.YBasis.Y) - (transform.YBasis.X * transform.XBasis.Y);

    /// <summary>Computes the signed area of a closed or open 2D polygon ring (&gt; 0 for CCW, &lt; 0 for CW).</summary>
    public static double ComputeSignedArea(IReadOnlyList<Point2D> ring)
    {
        if (ring is null || ring.Count < 3)
        {
            return 0.0;
        }

        int count = ring.Count;
        bool isClosed = Math.Abs(ring[0].X - ring[count - 1].X) <= 1e-12 &&
                        Math.Abs(ring[0].Y - ring[count - 1].Y) <= 1e-12;
        int n = isClosed ? count - 1 : count;
        if (n < 3)
        {
            return 0.0;
        }

        double twiceArea = 0.0;
        for (int i = 0; i < n; i++)
        {
            Point2D a = ring[i];
            Point2D b = ring[(i + 1) % n];
            twiceArea += (a.X * b.Y) - (b.X * a.Y);
        }

        return twiceArea * 0.5;
    }

    /// <summary>
    /// Ensures a 2D polygon exterior ring is strictly counter-clockwise (positive signed area),
    /// reversing vertex order when a mirrored or flipped transform inverted its winding.
    /// </summary>
    public static Polygon2D NormalizeRingCounterClockwise(IReadOnlyList<Point2D> vertices)
    {
        if (vertices is null || vertices.Count < 3)
        {
            throw new ArgumentException("A polygon ring requires at least 3 vertices.", nameof(vertices));
        }

        List<Point2D> open = new(vertices);
        if (open.Count >= 4 &&
            Math.Abs(open[0].X - open[open.Count - 1].X) <= 1e-12 &&
            Math.Abs(open[0].Y - open[open.Count - 1].Y) <= 1e-12)
        {
            open.RemoveAt(open.Count - 1);
        }

        if (ComputeSignedArea(open) < 0.0)
        {
            open.Reverse();
        }

        return new Polygon2D(open);
    }

    /// <summary>
    /// Projects a compiled Starter Kit opening <see cref="FamilyGeometryPlan"/> through an instance
    /// <see cref="OpeningPlacement2D"/> (flips/mirrors/rotation) and an optional linked-model transform,
    /// returning the canonicalized centerline, CCW <c>K_Footprint</c> polygon, and v3 semantic classification.
    /// </summary>
    public static ProjectedOpeningGeometry ProjectStarterKitOpening(
        FamilyGeometryPlan plan,
        OpeningPlacement2D placement,
        AffineTransform2D? linkToHostTransformMm = null,
        int standardVersion = OpeningFamilyRules.V3StandardVersion)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        CompiledExtrusionPrimitive footprint = plan.CoarsePrimitives
            .FirstOrDefault(p => p.IsSolid && string.Equals(p.Subcategory, CoarseGeometrySpec.FootprintSubcategory, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"Opening family '{plan.Spec.Id}' has no Coarse K_Footprint solid.");

        placement.Evaluate(out Point2D handOrientation, out Point2D facingOrientation, out AffineTransform2D instanceTransform);
        AffineTransform2D linkTransform = linkToHostTransformMm
            ?? new AffineTransform2D(new Point2D(0.0, 0.0), new Point2D(1.0, 0.0), new Point2D(0.0, 1.0));

        // 1. Centerline extraction matching OpeningExtractor.TryExtractOpeningLine + ProjectPoint:
        //    resolve axis in the opening's own model, generate endpoints around LocationPoint, then apply linkToHost.
        Point2D axis = ResolveOpeningAxis2D(handOrientation.X, handOrientation.Y, facingOrientation.X, facingOrientation.Y);
        (Point2D localStart, Point2D localEnd) = ComputeCenterlineEndpoints(
            placement.OriginMm,
            plan.Spec.DefaultDimensionsMm.WidthMm,
            axis,
            canonicalizeOrder: false);
        (Point2D hostStart, Point2D hostEnd) = TransformCenterline(
            localStart,
            localEnd,
            linkTransform,
            canonicalizeOrder: true);

        LineString2D centerline = new(new[] { hostStart, hostEnd });
        double dx = hostEnd.X - hostStart.X;
        double dy = hostEnd.Y - hostStart.Y;
        double effectiveWidthMm = Math.Sqrt((dx * dx) + (dy * dy));
        Point2D midpointMm = new((hostStart.X + hostEnd.X) * 0.5, (hostStart.Y + hostEnd.Y) * 0.5);

        // 2. Coarse K_Footprint projection with seed centering + instance placement + link transform,
        //    normalized to strictly CCW exterior ring even when combined det < 0.
        PlanPoint3D seedOffset = FamilyGeometryPlanBuilder.ComputeSeedPlacementOffsetMm(plan.Spec);
        AffineTransform2D combined = Compose(linkTransform, instanceTransform);
        double combinedDet = Determinant(combined);

        List<Point2D> projectedVertices = new(footprint.ProfileVertices.Count);
        foreach (PlanPoint3D v in footprint.ProfileVertices)
        {
            projectedVertices.Add(combined.Transform(v.XMm + seedOffset.XMm, v.YMm + seedOffset.YMm));
        }

        Polygon2D footprintPolygon = NormalizeRingCounterClockwise(projectedVertices);
        double signedArea = ComputeSignedArea(footprintPolygon.ExteriorRing);

        // 3. Semantic classification from the family's bound v3 shared parameters (invariant to flips/mirrors/links).
        string? gisDoor = plan.SharedParameters
            .FirstOrDefault(p => string.Equals(p.Name, GisSharedParameters.GisDoor, StringComparison.Ordinal))
            ?.DefaultValue;
        string? gisOpeningCategory = plan.SharedParameters
            .FirstOrDefault(p => string.Equals(p.Name, GisSharedParameters.GisOpeningCategory, StringComparison.Ordinal))
            ?.DefaultValue;
        string? gisAccessCon = plan.SharedParameters
            .FirstOrDefault(p => string.Equals(p.Name, GisSharedParameters.GisAccessCon, StringComparison.Ordinal))
            ?.DefaultValue;

        string normalizedDoor = GisStandardDomains.TryNormalizeDoorType(gisDoor, out string canonicalDoor)
            ? canonicalDoor
            : string.Empty;
        string resolvedCategory = OpeningFamilyRules.ResolveOpeningCategory(
            plan.Spec.FamilyName,
            gisOpeningCategory,
            gisAccessCon,
            standardVersion);
        bool isElevatorDoor = OpeningFamilyRules.IsElevatorDoor(
            plan.Spec.FamilyName,
            gisDoor,
            standardVersion);

        return new ProjectedOpeningGeometry(
            plan.Spec.Id,
            centerline,
            effectiveWidthMm,
            midpointMm,
            footprintPolygon,
            signedArea,
            combinedDet,
            normalizedDoor,
            resolvedCategory,
            isElevatorDoor);
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
