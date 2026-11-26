using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Pure-C# parameterized array expansion helpers for repeatable Starter Kit fixture families
/// (<c>KNZ_FIX_TicketGateBank</c> lane count and <c>KNZ_FIX_BarrierFence</c> post array).
/// Every expanded spec preserves anti-Z-fighting clearances (<c>+4 mm</c> slab, <c>+8 mm</c> side,
/// <c>8 mm</c> glass tuck), downward Z=0 bottom <c>PlanarFace</c>, and deterministic triangle estimates.
/// </summary>
public static class StarterKitArrayExpander
{
    public const int DefaultTicketGateLaneCount = 3;
    public const int MinTicketGateLaneCount = 1;
    public const int MaxTicketGateLaneCount = 10;
    public const double TicketGatePitchMm = 960.0;
    public const double TicketGateEndMarginMm = 320.0;

    public const int DefaultBarrierFencePostCount = 3;
    public const int MinBarrierFencePostCount = 2;
    public const int MaxBarrierFencePostCount = 12;
    public const double BarrierFencePostStepMm = 1160.0;
    public const double BarrierFenceEndMarginMm = 80.0;

    /// <summary>
    /// Expands <c>KNZ_FIX_TicketGateBank</c> to <paramref name="laneCount"/> aisles
    /// (<c>laneCount + 1</c> cabinets / IC readers / top trim caps, <c>laneCount</c> flapper leaves),
    /// scaling the Coarse <c>K_Footprint</c> and <c>floor_guide_plinth</c> width deterministically.
    /// </summary>
    public static FamilyGeometrySpec ExpandTicketGateBank(
        FamilyGeometrySpec baseSpec,
        int laneCount,
        string? familyNameOverride = null)
    {
        if (baseSpec is null)
        {
            throw new ArgumentNullException(nameof(baseSpec));
        }

        if (!string.Equals(baseSpec.Id, "KNZ_FIX_TicketGateBank", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected 'KNZ_FIX_TicketGateBank' spec, received '{baseSpec.Id}'.",
                nameof(baseSpec));
        }

        if (laneCount < MinTicketGateLaneCount || laneCount > MaxTicketGateLaneCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(laneCount),
                laneCount,
                $"Ticket gate bank lane count must be between {MinTicketGateLaneCount} and {MaxTicketGateLaneCount}.");
        }

        double widthMm = TicketGateEndMarginMm + (laneCount * TicketGatePitchMm);
        int cabinetCount = laneCount + 1;
        double sideClearMm = Math.Max(AntiZFightingClearances.MinimumSideClearMm, baseSpec.ClearancesMm.SideClearMm);

        CoarseGeometrySpec? expandedCoarse = baseSpec.Coarse == null
            ? null
            : new CoarseGeometrySpec(
                baseSpec.Coarse.Subcategory,
                baseSpec.Coarse.MaterialRef,
                baseSpec.Coarse.RequiresDownwardPlanarBottomAtZero,
                Array.AsReadOnly(baseSpec.Coarse.Ops.Select(op =>
                    string.Equals(op.Name, "footprint_plan", StringComparison.Ordinal) && op.SizeMm != null && op.SizeMm.Count == 3
                        ? CloneWithSizeX(op, widthMm)
                        : op).ToArray()));

        List<GeometryOpSpec> expandedMediumOps = new(baseSpec.MediumFine.Ops.Count);
        foreach (GeometryOpSpec op in baseSpec.MediumFine.Ops)
        {
            if (string.Equals(op.Name, "floor_guide_plinth", StringComparison.Ordinal) &&
                op.SizeMm != null && op.SizeMm.Count == 3)
            {
                expandedMediumOps.Add(CloneWithSizeX(op, widthMm - (2.0 * sideClearMm)));
            }
            else if (string.Equals(op.Op, GeometryOpSpec.OpArray, StringComparison.Ordinal))
            {
                int count = string.Equals(op.Name, "aisle_flapper_doors", StringComparison.Ordinal)
                    ? laneCount
                    : cabinetCount;
                expandedMediumOps.Add(CloneWithArrayCount(op, count));
            }
            else
            {
                expandedMediumOps.Add(op);
            }
        }

        MediumFineGeometrySpec expandedMedium = new(Array.AsReadOnly(expandedMediumOps.ToArray()));
        int estimatedMediumTris = TriangleEstimator.EstimateOps(expandedMedium.Ops);
        int mediumBudget = Math.Max(baseSpec.TriangleBudget.MediumFineMax, estimatedMediumTris);

        return new FamilyGeometrySpec(
            baseSpec.Schema,
            baseSpec.SchemaVersion,
            baseSpec.Id,
            string.IsNullOrWhiteSpace(familyNameOverride) ? baseSpec.FamilyName : familyNameOverride!.Trim(),
            baseSpec.DisplayNameEn,
            baseSpec.DisplayNameJa,
            baseSpec.Group,
            baseSpec.Category,
            baseSpec.SeedKind,
            baseSpec.IsPolygonal,
            baseSpec.Subcategories,
            baseSpec.ClearancesMm,
            new FamilyDimensionsSpec(widthMm, baseSpec.DefaultDimensionsMm.DepthMm, baseSpec.DefaultDimensionsMm.HeightMm),
            baseSpec.SharedParameters,
            new FamilyTriangleBudgetSpec(baseSpec.TriangleBudget.CoarseMax, mediumBudget),
            expandedCoarse,
            expandedMedium);
    }

    /// <summary>
    /// Expands <c>KNZ_FIX_BarrierFence</c> to <paramref name="postCount"/> stanchion posts,
    /// scaling the Coarse <c>K_Footprint</c>, top/bottom rails, and tucked <c>K_Glass</c> infill panel width.
    /// </summary>
    public static FamilyGeometrySpec ExpandBarrierFence(
        FamilyGeometrySpec baseSpec,
        int postCount,
        string? familyNameOverride = null)
    {
        if (baseSpec is null)
        {
            throw new ArgumentNullException(nameof(baseSpec));
        }

        if (!string.Equals(baseSpec.Id, "KNZ_FIX_BarrierFence", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"Expected 'KNZ_FIX_BarrierFence' spec, received '{baseSpec.Id}'.",
                nameof(baseSpec));
        }

        if (postCount < MinBarrierFencePostCount || postCount > MaxBarrierFencePostCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(postCount),
                postCount,
                $"Barrier fence post count must be between {MinBarrierFencePostCount} and {MaxBarrierFencePostCount}.");
        }

        double widthMm = BarrierFenceEndMarginMm + ((postCount - 1) * BarrierFencePostStepMm);
        double sideClearMm = Math.Max(AntiZFightingClearances.MinimumSideClearMm, baseSpec.ClearancesMm.SideClearMm);

        CoarseGeometrySpec? expandedCoarse = baseSpec.Coarse == null
            ? null
            : new CoarseGeometrySpec(
                baseSpec.Coarse.Subcategory,
                baseSpec.Coarse.MaterialRef,
                baseSpec.Coarse.RequiresDownwardPlanarBottomAtZero,
                Array.AsReadOnly(baseSpec.Coarse.Ops.Select(op =>
                    string.Equals(op.Name, "footprint_plan", StringComparison.Ordinal) && op.SizeMm != null && op.SizeMm.Count == 3
                        ? CloneWithSizeX(op, widthMm)
                        : op).ToArray()));

        List<GeometryOpSpec> expandedMediumOps = new(baseSpec.MediumFine.Ops.Count);
        foreach (GeometryOpSpec op in baseSpec.MediumFine.Ops)
        {
            if (string.Equals(op.Op, GeometryOpSpec.OpArray, StringComparison.Ordinal) &&
                string.Equals(op.Name, "stanchion_posts", StringComparison.Ordinal))
            {
                expandedMediumOps.Add(CloneWithArrayCount(op, postCount));
            }
            else if ((string.Equals(op.Name, "top_handrail", StringComparison.Ordinal) ||
                      string.Equals(op.Name, "bottom_rail", StringComparison.Ordinal)) &&
                     op.SizeMm != null && op.SizeMm.Count == 3)
            {
                expandedMediumOps.Add(CloneWithSizeX(op, widthMm - (2.0 * sideClearMm)));
            }
            else if (string.Equals(op.Name, "glazed_infill_panel", StringComparison.Ordinal) &&
                     op.OriginMm != null && op.OriginMm.Count == 3 &&
                     op.SizeMm != null && op.SizeMm.Count == 3)
            {
                // Preserve exact symmetrical inset between outer stanchion posts (64 mm on each end).
                double panelInsetX = op.OriginMm[0];
                expandedMediumOps.Add(CloneWithSizeX(op, widthMm - (2.0 * panelInsetX)));
            }
            else
            {
                expandedMediumOps.Add(op);
            }
        }

        MediumFineGeometrySpec expandedMedium = new(Array.AsReadOnly(expandedMediumOps.ToArray()));
        int estimatedMediumTris = TriangleEstimator.EstimateOps(expandedMedium.Ops);
        int mediumBudget = Math.Max(baseSpec.TriangleBudget.MediumFineMax, estimatedMediumTris);

        return new FamilyGeometrySpec(
            baseSpec.Schema,
            baseSpec.SchemaVersion,
            baseSpec.Id,
            string.IsNullOrWhiteSpace(familyNameOverride) ? baseSpec.FamilyName : familyNameOverride!.Trim(),
            baseSpec.DisplayNameEn,
            baseSpec.DisplayNameJa,
            baseSpec.Group,
            baseSpec.Category,
            baseSpec.SeedKind,
            baseSpec.IsPolygonal,
            baseSpec.Subcategories,
            baseSpec.ClearancesMm,
            new FamilyDimensionsSpec(widthMm, baseSpec.DefaultDimensionsMm.DepthMm, baseSpec.DefaultDimensionsMm.HeightMm),
            baseSpec.SharedParameters,
            new FamilyTriangleBudgetSpec(baseSpec.TriangleBudget.CoarseMax, mediumBudget),
            expandedCoarse,
            expandedMedium);
    }

    private static GeometryOpSpec CloneWithSizeX(GeometryOpSpec op, double newSizeXMm)
    {
        IReadOnlyList<double> newSize = Array.AsReadOnly(new[] { newSizeXMm, op.SizeMm![1], op.SizeMm![2] });
        return new GeometryOpSpec(
            op.Op,
            op.Name,
            op.Subcategory,
            op.MaterialRef,
            op.OriginMm,
            newSize,
            op.CenterMm,
            op.RadiusMm,
            op.HeightMm,
            op.Sides,
            op.WidthMm,
            op.RunMm,
            op.RiseMm,
            op.ThicknessMm,
            op.Count,
            op.StepMm,
            op.Child,
            op.Children,
            op.Shape,
            op.ThroughCut,
            op.TargetOp);
    }

    private static GeometryOpSpec CloneWithArrayCount(GeometryOpSpec op, int newCount)
    {
        return new GeometryOpSpec(
            op.Op,
            op.Name,
            op.Subcategory,
            op.MaterialRef,
            op.OriginMm,
            op.SizeMm,
            op.CenterMm,
            op.RadiusMm,
            op.HeightMm,
            op.Sides,
            op.WidthMm,
            op.RunMm,
            op.RiseMm,
            op.ThicknessMm,
            newCount,
            op.StepMm,
            op.Child,
            op.Children,
            op.Shape,
            op.ThroughCut,
            op.TargetOp);
    }
}
