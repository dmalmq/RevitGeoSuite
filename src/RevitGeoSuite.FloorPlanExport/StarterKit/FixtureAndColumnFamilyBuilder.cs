using System;
using System.Collections.Generic;
using Autodesk.Revit.ApplicationServices;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>
/// Builds and gates the 6 required T7 fixture &amp; column Starter Kit families
/// (<c>KNZ_FIX_TicketGateBank</c>, <c>KNZ_FIX_BarrierFence</c>, <c>KNZ_FIX_Kiosk</c>,
/// <c>KNZ_FIX_Equipment</c>, <c>KNZ_COL_Rect</c>, <c>KNZ_COL_Round</c>) plus the optional
/// non-polygonal POI marker (<c>KNZ_POI_FacilityMarker</c>).
/// Supports parameterized array expansion for ticket gate bank lane counts and barrier fence posts.
/// </summary>
public static class FixtureAndColumnFamilyBuilder
{
    public const string TicketGateBankId = StarterKitCatalogSelector.TicketGateBankId;
    public const string BarrierFenceId = StarterKitCatalogSelector.BarrierFenceId;
    public const string KioskId = StarterKitCatalogSelector.KioskId;
    public const string EquipmentId = StarterKitCatalogSelector.EquipmentId;
    public const string RectColumnId = StarterKitCatalogSelector.RectColumnId;
    public const string RoundColumnId = StarterKitCatalogSelector.RoundColumnId;
    public const string PoiFacilityMarkerId = StarterKitCatalogSelector.PoiFacilityMarkerId;

    public static IReadOnlyList<string> RequiredTargetFamilyIds =>
        StarterKitCatalogSelector.RequiredFixtureAndColumnFamilyIds;

    /// <summary>
    /// Selects the 6 required fixture &amp; column specs plus optionally <c>KNZ_POI_FacilityMarker</c>
    /// when <paramref name="includeOptionalPoi"/> is true.
    /// </summary>
    public static IReadOnlyList<FamilyGeometrySpec> SelectFixtureAndColumnSpecs(
        StarterKitSpec kitSpec,
        bool includeOptionalPoi = true) =>
        StarterKitCatalogSelector.SelectFixtureAndColumnSpecs(kitSpec, includeOptionalPoi);

    /// <summary>
    /// Builds, saves, inspects, and lints the 6 required fixture &amp; column families and (when
    /// <paramref name="includeOptionalPoi"/> is true) the optional <c>KNZ_POI_FacilityMarker</c> family.
    /// </summary>
    public static FamilyBatchBuildResult BuildAll(
        Application application,
        StarterKitSpec kitSpec,
        FamilyBuildOptions options,
        bool includeOptionalPoi = true,
        StarterKitReferenceStandards? standards = null)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();
        IReadOnlyList<FamilyGeometrySpec> specs = SelectFixtureAndColumnSpecs(kitSpec, includeOptionalPoi);
        List<FamilyBuildResult> results = new(specs.Count);

        foreach (FamilyGeometrySpec spec in specs)
        {
            results.Add(FamilyBuilder.BuildFamily(application, spec, kitSpec, options, refStandards));
        }

        return new FamilyBatchBuildResult("fixtures-columns-poi", Array.AsReadOnly(results.ToArray()));
    }

    /// <summary>
    /// Builds a G2-compliant parameterized <c>KNZ_FIX_TicketGateBank</c> variant expanded to
    /// <paramref name="laneCount"/> aisles. Preserves the canonical G2-registered family name
    /// <c>KNZ_FIX_TicketGateBank</c> (passing both <c>RfaLinter</c> and downstream <c>GisStandardCheck</c> /
    /// <c>TemplateReadinessCheck</c>) and distinguishes variants via <c>FamilyType</c> (<c>{laneCount}-Lane</c>)
    /// and a non-colliding output subdirectory (<c>variants/ticket-gate-{laneCount}lane/</c>).
    /// </summary>
    public static FamilyBuildResult BuildTicketGateBankVariant(
        Application application,
        StarterKitSpec kitSpec,
        FamilyBuildOptions options,
        int laneCount,
        string? typeNameOverride = null,
        string? variantSubdirectory = null,
        StarterKitReferenceStandards? standards = null)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        FamilyGeometrySpec baseSpec = kitSpec.FindFamily(TicketGateBankId)
            ?? throw new InvalidOperationException($"Starter Kit spec is missing '{TicketGateBankId}'.");
        StarterKitVariantPlan variant = StarterKitArrayExpander.PlanTicketGateBankVariant(
            baseSpec,
            laneCount,
            options.OutputDirectory,
            typeNameOverride,
            variantSubdirectory,
            standards);
        FamilyBuildOptions variantOptions = CreateVariantBuildOptions(options, variant.OutputDirectory);
        return FamilyBuilder.BuildFamily(
            application,
            variant.Spec,
            kitSpec,
            variantOptions,
            standards,
            defaultTypeNameOverride: variant.TypeName);
    }

    /// <summary>
    /// Builds a parameterized <c>KNZ_FIX_BarrierFence</c> variant expanded to <paramref name="postCount"/>
    /// stanchion posts, distinguishing variants via <c>FamilyType</c> (<c>{postCount}-Post</c>) and a
    /// non-colliding output subdirectory (<c>variants/barrier-fence-{postCount}post/</c>).
    /// </summary>
    public static FamilyBuildResult BuildBarrierFenceVariant(
        Application application,
        StarterKitSpec kitSpec,
        FamilyBuildOptions options,
        int postCount,
        string? typeNameOverride = null,
        string? variantSubdirectory = null,
        string? familyNameOverride = null,
        StarterKitReferenceStandards? standards = null)
    {
        if (application is null)
        {
            throw new ArgumentNullException(nameof(application));
        }

        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        FamilyGeometrySpec baseSpec = kitSpec.FindFamily(BarrierFenceId)
            ?? throw new InvalidOperationException($"Starter Kit spec is missing '{BarrierFenceId}'.");
        StarterKitVariantPlan variant = StarterKitArrayExpander.PlanBarrierFenceVariant(
            baseSpec,
            postCount,
            options.OutputDirectory,
            typeNameOverride,
            variantSubdirectory,
            familyNameOverride);
        FamilyBuildOptions variantOptions = CreateVariantBuildOptions(options, variant.OutputDirectory);
        return FamilyBuilder.BuildFamily(
            application,
            variant.Spec,
            kitSpec,
            variantOptions,
            standards,
            defaultTypeNameOverride: variant.TypeName);
    }

    private static FamilyBuildOptions CreateVariantBuildOptions(FamilyBuildOptions source, string outputDirectory) =>
        new(
            source.SeedTemplatesDirectory,
            outputDirectory,
            source.SharedParametersFilePath,
            source.OverwriteExisting,
            source.LintBeforeReturn,
            source.CustomSeedPaths);
}
