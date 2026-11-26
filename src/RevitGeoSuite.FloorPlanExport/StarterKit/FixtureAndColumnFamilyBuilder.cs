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
    /// Builds a parameterized <c>KNZ_FIX_TicketGateBank</c> variant expanded to <paramref name="laneCount"/> aisles.
    /// </summary>
    public static FamilyBuildResult BuildTicketGateBankVariant(
        Application application,
        StarterKitSpec kitSpec,
        FamilyBuildOptions options,
        int laneCount,
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

        FamilyGeometrySpec baseSpec = kitSpec.FindFamily(TicketGateBankId)
            ?? throw new InvalidOperationException($"Starter Kit spec is missing '{TicketGateBankId}'.");
        FamilyGeometrySpec expanded = StarterKitArrayExpander.ExpandTicketGateBank(baseSpec, laneCount, familyNameOverride);
        return FamilyBuilder.BuildFamily(application, expanded, kitSpec, options, standards);
    }

    /// <summary>
    /// Builds a parameterized <c>KNZ_FIX_BarrierFence</c> variant expanded to <paramref name="postCount"/> stanchion posts.
    /// </summary>
    public static FamilyBuildResult BuildBarrierFenceVariant(
        Application application,
        StarterKitSpec kitSpec,
        FamilyBuildOptions options,
        int postCount,
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
        FamilyGeometrySpec expanded = StarterKitArrayExpander.ExpandBarrierFence(baseSpec, postCount, familyNameOverride);
        return FamilyBuilder.BuildFamily(application, expanded, kitSpec, options, standards);
    }
}
