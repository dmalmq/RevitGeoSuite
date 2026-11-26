using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Pure-C# G2-compliant build plan for a parameterized Starter Kit fixture variant
/// (e.g., a <c>KNZ_FIX_TicketGateBank</c> lane-count variant or <c>KNZ_FIX_BarrierFence</c> post-count variant).
/// Keeps the canonical registered family name (preserving <c>standards/families.json</c> G2 compliance)
/// and isolates variants via <see cref="TypeName"/> and <see cref="OutputDirectory"/>.
/// </summary>
public sealed class StarterKitVariantPlan
{
    public StarterKitVariantPlan(
        FamilyGeometrySpec spec,
        string typeName,
        string outputDirectory)
    {
        Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        TypeName = typeName ?? throw new ArgumentNullException(nameof(typeName));
        OutputDirectory = outputDirectory ?? throw new ArgumentNullException(nameof(outputDirectory));
        OutputFilePath = Path.Combine(outputDirectory, spec.FamilyName + ".rfa");
    }

    public FamilyGeometrySpec Spec { get; }

    public string TypeName { get; }

    public string OutputDirectory { get; }

    public string OutputFilePath { get; }
}

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

    /// <summary>Returns the canonical <c>FamilyType</c> name for a ticket-gate lane-count variant (e.g. <c>5-Lane</c>).</summary>
    public static string FormatTicketGateLaneTypeName(int laneCount) =>
        string.Format(CultureInfo.InvariantCulture, "{0}-Lane", laneCount);

    /// <summary>Returns the default relative output subdirectory isolating a ticket-gate lane variant from the canonical RFA.</summary>
    public static string FormatTicketGateVariantSubdirectory(int laneCount) =>
        Path.Combine("variants", string.Format(CultureInfo.InvariantCulture, "ticket-gate-{0}lane", laneCount));

    /// <summary>Returns the canonical <c>FamilyType</c> name for a barrier-fence post-count variant (e.g. <c>4-Post</c>).</summary>
    public static string FormatBarrierFencePostTypeName(int postCount) =>
        string.Format(CultureInfo.InvariantCulture, "{0}-Post", postCount);

    /// <summary>Returns the default relative output subdirectory isolating a barrier-fence post variant from the canonical RFA.</summary>
    public static string FormatBarrierFenceVariantSubdirectory(int postCount) =>
        Path.Combine("variants", string.Format(CultureInfo.InvariantCulture, "barrier-fence-{0}post", postCount));

    /// <summary>
    /// Validates that <paramref name="familyName"/> is registered under <c>ticket_gate</c> in
    /// <c>standards/families.json</c> (G2 closed allow-list) so variants cannot silently pass lint and fail
    /// downstream <c>GisStandardCheck</c> / <c>TemplateReadinessCheck</c>.
    /// </summary>
    public static void ValidateTicketGateFamilyName(
        string familyName,
        StarterKitReferenceStandards? standards = null)
    {
        if (string.IsNullOrWhiteSpace(familyName))
        {
            throw new ArgumentException("Ticket gate family name is required.", nameof(familyName));
        }

        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();
        if (refStandards.FamiliesByGisKind.TryGetValue("ticket_gate", out IReadOnlySetLike? registered) &&
            registered != null &&
            !registered.Contains(familyName))
        {
            string allowed = string.Join(", ", registered.OrderBy(n => n, StringComparer.Ordinal));
            throw new ArgumentException(
                $"Ticket gate family name '{familyName}' is not registered under 'ticket_gate' in standards/families.json (G2 closed list: {allowed}). Keep the canonical family name 'KNZ_FIX_TicketGateBank' and distinguish lane variants via FamilyType and output subdirectory, or supply custom standards registering '{familyName}'.",
                nameof(familyName));
        }
    }

    /// <summary>
    /// Resolves a G2-compliant <see cref="StarterKitVariantPlan"/> for a <c>KNZ_FIX_TicketGateBank</c> lane-count variant:
    /// preserves the registered canonical family name <c>KNZ_FIX_TicketGateBank</c>, assigns a distinct
    /// <c>FamilyType</c> (<c>{laneCount}-Lane</c> by default), and writes into a non-colliding output subdirectory.
    /// </summary>
    public static StarterKitVariantPlan PlanTicketGateBankVariant(
        FamilyGeometrySpec baseSpec,
        int laneCount,
        string baseOutputDirectory,
        string? typeNameOverride = null,
        string? variantSubdirectory = null,
        StarterKitReferenceStandards? standards = null)
    {
        if (string.IsNullOrWhiteSpace(baseOutputDirectory))
        {
            throw new ArgumentException("Base output directory is required.", nameof(baseOutputDirectory));
        }

        FamilyGeometrySpec expanded = ExpandTicketGateBank(baseSpec, laneCount, familyNameOverride: null, standards: standards);
        string typeName = string.IsNullOrWhiteSpace(typeNameOverride)
            ? FormatTicketGateLaneTypeName(laneCount)
            : typeNameOverride!.Trim();
        string subDir = string.IsNullOrWhiteSpace(variantSubdirectory)
            ? FormatTicketGateVariantSubdirectory(laneCount)
            : variantSubdirectory!.Trim();
        string variantOutputDir = Path.GetFullPath(Path.Combine(baseOutputDirectory, subDir));

        return new StarterKitVariantPlan(expanded, typeName, variantOutputDir);
    }

    /// <summary>
    /// Resolves a <see cref="StarterKitVariantPlan"/> for a <c>KNZ_FIX_BarrierFence</c> post-count variant,
    /// isolating its output directory and <c>FamilyType</c> from the canonical catalog RFA.
    /// </summary>
    public static StarterKitVariantPlan PlanBarrierFenceVariant(
        FamilyGeometrySpec baseSpec,
        int postCount,
        string baseOutputDirectory,
        string? typeNameOverride = null,
        string? variantSubdirectory = null,
        string? familyNameOverride = null)
    {
        if (string.IsNullOrWhiteSpace(baseOutputDirectory))
        {
            throw new ArgumentException("Base output directory is required.", nameof(baseOutputDirectory));
        }

        FamilyGeometrySpec expanded = ExpandBarrierFence(baseSpec, postCount, familyNameOverride);
        string typeName = string.IsNullOrWhiteSpace(typeNameOverride)
            ? FormatBarrierFencePostTypeName(postCount)
            : typeNameOverride!.Trim();
        string subDir = string.IsNullOrWhiteSpace(variantSubdirectory)
            ? FormatBarrierFenceVariantSubdirectory(postCount)
            : variantSubdirectory!.Trim();
        string variantOutputDir = Path.GetFullPath(Path.Combine(baseOutputDirectory, subDir));

        return new StarterKitVariantPlan(expanded, typeName, variantOutputDir);
    }

    /// <summary>
    /// Expands <c>KNZ_FIX_TicketGateBank</c> to <paramref name="laneCount"/> aisles
    /// (<c>laneCount + 1</c> cabinets / IC readers / top trim caps, <c>laneCount</c> flapper leaves),
    /// scaling the Coarse <c>K_Footprint</c> and <c>floor_guide_plinth</c> width deterministically.
    /// Enforces G2 <c>standards/families.json</c> registration when <paramref name="familyNameOverride"/> is supplied.
    /// </summary>
    public static FamilyGeometrySpec ExpandTicketGateBank(
        FamilyGeometrySpec baseSpec,
        int laneCount,
        string? familyNameOverride = null,
        StarterKitReferenceStandards? standards = null)
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

        string effectiveFamilyName = string.IsNullOrWhiteSpace(familyNameOverride)
            ? baseSpec.FamilyName
            : familyNameOverride!.Trim();
        if (!string.IsNullOrWhiteSpace(familyNameOverride))
        {
            ValidateTicketGateFamilyName(effectiveFamilyName, standards);
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
            effectiveFamilyName,
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
        string effectiveFamilyName = string.IsNullOrWhiteSpace(familyNameOverride)
            ? baseSpec.FamilyName
            : familyNameOverride!.Trim();

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
            effectiveFamilyName,
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
