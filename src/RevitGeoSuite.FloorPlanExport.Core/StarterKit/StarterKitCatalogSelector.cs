using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>
/// Options controlling Starter Kit catalog selection and <c>StarterKitFamilyCatalogBuilder.BuildCatalog</c> runs.
/// <see cref="Default"/> always returns a fresh instance so mutating one options object can never leak into
/// subsequent catalog builds.
/// </summary>
public sealed class StarterKitCatalogBuildOptions
{
    public static StarterKitCatalogBuildOptions Default => new();

    /// <summary>
    /// When true (default), includes optional Starter Kit families such as <c>KNZ_POI_FacilityMarker</c>
    /// (building all 16 catalog families). When false, builds only the 15 required families.
    /// </summary>
    public bool IncludeOptionalFamilies { get; set; } = true;
}

/// <summary>Result of compiling and linting a Starter Kit catalog selection in pure C# (offline/CI).</summary>
public sealed class StarterKitCatalogPlanResult
{
    public StarterKitCatalogPlanResult(
        bool includedOptionalFamilies,
        IReadOnlyList<FamilyGeometryPlan> plans,
        IReadOnlyList<RfaLintReport> lintReports,
        List<ManifestFamilyEntry> manifestEntries)
    {
        IncludedOptionalFamilies = includedOptionalFamilies;
        Plans = plans ?? Array.Empty<FamilyGeometryPlan>();
        LintReports = lintReports ?? Array.Empty<RfaLintReport>();
        ManifestEntries = manifestEntries ?? new List<ManifestFamilyEntry>();
        AllPassed = LintReports.Count > 0 && LintReports.All(r => r.Passed);
    }

    public bool IncludedOptionalFamilies { get; }

    public IReadOnlyList<FamilyGeometryPlan> Plans { get; }

    public IReadOnlyList<RfaLintReport> LintReports { get; }

    public List<ManifestFamilyEntry> ManifestEntries { get; }

    public bool AllPassed { get; }
}

/// <summary>
/// Canonical family-group taxonomy and deterministic selection order for the 16-family Kanizu Starter Kit
/// catalog (4 Circulation + 5 Openings + 4 Fixtures + 2 Columns + 1 optional POI marker).
/// Shared by Revit-side builders (<c>OpeningFamilyBuilder</c>, <c>FixtureAndColumnFamilyBuilder</c>,
/// <c>StarterKitFamilyCatalogBuilder</c>) and Linux pure-C# CI verification.
/// </summary>
public static class StarterKitCatalogSelector
{
    public const string EscalatorId = "KNZ_CIRC_Escalator";
    public const string MovingWalkwayId = "KNZ_CIRC_MovingWalkway";
    public const string ElevatorId = "KNZ_CIRC_Elevator";
    public const string ElevatorDoorId = "KNZ_CIRC_ElevatorDoor";

    public const string DoorSwingId = "KNZ_OPEN_DoorSwing";
    public const string DoorSlideId = "KNZ_OPEN_DoorSlide";
    public const string DoorAutoId = "KNZ_OPEN_DoorAuto";
    public const string ShutterId = "KNZ_OPEN_Shutter";
    public const string OpeningId = "KNZ_OPEN_Opening";

    public const string TicketGateBankId = "KNZ_FIX_TicketGateBank";
    public const string BarrierFenceId = "KNZ_FIX_BarrierFence";
    public const string KioskId = "KNZ_FIX_Kiosk";
    public const string EquipmentId = "KNZ_FIX_Equipment";
    public const string RectColumnId = "KNZ_COL_Rect";
    public const string RoundColumnId = "KNZ_COL_Round";
    public const string PoiFacilityMarkerId = "KNZ_POI_FacilityMarker";

    public static readonly IReadOnlyList<string> CirculationFamilyIds = Array.AsReadOnly(new[]
    {
        EscalatorId,
        MovingWalkwayId,
        ElevatorId,
        ElevatorDoorId,
    });

    public static readonly IReadOnlyList<string> OpeningFamilyIds = Array.AsReadOnly(new[]
    {
        DoorSwingId,
        DoorSlideId,
        DoorAutoId,
        ShutterId,
        OpeningId,
    });

    public static readonly IReadOnlyList<string> FixtureFamilyIds = Array.AsReadOnly(new[]
    {
        TicketGateBankId,
        BarrierFenceId,
        KioskId,
        EquipmentId,
    });

    public static readonly IReadOnlyList<string> ColumnFamilyIds = Array.AsReadOnly(new[]
    {
        RectColumnId,
        RoundColumnId,
    });

    public static readonly IReadOnlyList<string> RequiredFixtureAndColumnFamilyIds = Array.AsReadOnly(new[]
    {
        TicketGateBankId,
        BarrierFenceId,
        KioskId,
        EquipmentId,
        RectColumnId,
        RoundColumnId,
    });

    public static readonly IReadOnlyList<string> PoiFamilyIds = Array.AsReadOnly(new[]
    {
        PoiFacilityMarkerId,
    });

    public static readonly IReadOnlyList<string> AllRequiredFamilyIds = Array.AsReadOnly(
        CirculationFamilyIds
            .Concat(OpeningFamilyIds)
            .Concat(RequiredFixtureAndColumnFamilyIds)
            .ToArray());

    public static readonly IReadOnlyList<string> AllCatalogFamilyIds = Array.AsReadOnly(
        AllRequiredFamilyIds
            .Concat(PoiFamilyIds)
            .ToArray());

    /// <summary>Selects the 5 T7 wall-hosted door/opening/shutter specs in deterministic order.</summary>
    public static IReadOnlyList<FamilyGeometrySpec> SelectOpeningSpecs(StarterKitSpec kitSpec)
    {
        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        List<FamilyGeometrySpec> selected = new(OpeningFamilyIds.Count);
        foreach (string id in OpeningFamilyIds)
        {
            FamilyGeometrySpec spec = kitSpec.FindFamily(id)
                ?? throw new InvalidOperationException($"Starter Kit spec is missing required T7 opening family '{id}'.");
            selected.Add(spec);
        }

        return Array.AsReadOnly(selected.ToArray());
    }

    /// <summary>
    /// Selects the 6 required T7 fixture &amp; column specs, optionally appending <c>KNZ_POI_FacilityMarker</c>
    /// when <paramref name="includeOptionalPoi"/> is true and present in <paramref name="kitSpec"/>.
    /// </summary>
    public static IReadOnlyList<FamilyGeometrySpec> SelectFixtureAndColumnSpecs(
        StarterKitSpec kitSpec,
        bool includeOptionalPoi = true)
    {
        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        List<FamilyGeometrySpec> selected = new(RequiredFixtureAndColumnFamilyIds.Count + (includeOptionalPoi ? 1 : 0));
        foreach (string id in RequiredFixtureAndColumnFamilyIds)
        {
            FamilyGeometrySpec spec = kitSpec.FindFamily(id)
                ?? throw new InvalidOperationException($"Starter Kit spec is missing required T7 fixture/column family '{id}'.");
            selected.Add(spec);
        }

        if (includeOptionalPoi)
        {
            FamilyGeometrySpec? poi = kitSpec.FindFamily(PoiFacilityMarkerId);
            if (poi != null)
            {
                selected.Add(poi);
            }
        }

        return Array.AsReadOnly(selected.ToArray());
    }

    /// <summary>
    /// Selects the full Starter Kit catalog in deterministic group order:
    /// Circulation (4) -&gt; Openings (5) -&gt; Fixtures &amp; Columns (6) -&gt; optional POI (0 or 1),
    /// plus any additional non-optional families registered in <c>kit.json</c>.
    /// </summary>
    public static IReadOnlyList<FamilyGeometrySpec> SelectCatalogSpecs(
        StarterKitSpec kitSpec,
        bool includeOptionalFamilies = true)
    {
        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        Dictionary<string, StarterKitFamilyRegistryEntry> registryById = kitSpec.Header.Families
            .GroupBy(f => f.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);

        List<FamilyGeometrySpec> ordered = new();
        HashSet<string> seen = new(StringComparer.Ordinal);

        foreach (string id in AllCatalogFamilyIds)
        {
            registryById.TryGetValue(id, out StarterKitFamilyRegistryEntry? regEntry);
            bool isOptional = regEntry?.Optional ?? string.Equals(id, PoiFacilityMarkerId, StringComparison.Ordinal);
            if (isOptional && !includeOptionalFamilies)
            {
                continue;
            }

            FamilyGeometrySpec? spec = kitSpec.FindFamily(id);
            if (spec == null)
            {
                if (!isOptional)
                {
                    throw new InvalidOperationException($"Starter Kit spec is missing required catalog family '{id}'.");
                }

                continue;
            }

            if (seen.Add(spec.Id))
            {
                ordered.Add(spec);
            }
        }

        // Preserve any extra registered families in kit.json order for forward compatibility.
        foreach (StarterKitFamilyRegistryEntry entry in kitSpec.Header.Families)
        {
            if (entry.Optional && !includeOptionalFamilies)
            {
                continue;
            }

            if (seen.Contains(entry.Id))
            {
                continue;
            }

            FamilyGeometrySpec? spec = kitSpec.FindFamily(entry.Id);
            if (spec != null && seen.Add(spec.Id))
            {
                ordered.Add(spec);
            }
        }

        return Array.AsReadOnly(ordered.ToArray());
    }

    /// <summary>
    /// Builds an <see cref="ExpectedManifest"/> matching either the full 16-family catalog
    /// (<paramref name="includeOptionalFamilies"/> = true) or the 15-family required catalog
    /// (<paramref name="includeOptionalFamilies"/> = false).
    /// </summary>
    public static ExpectedManifest BuildExpectedManifestForSelection(
        StarterKitSpec kitSpec,
        bool includeOptionalFamilies = true,
        StarterKitReferenceStandards? standards = null)
    {
        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        if (includeOptionalFamilies)
        {
            return ExpectedManifest.FromSpec(kitSpec, standards);
        }

        IReadOnlyList<FamilyGeometrySpec> selectedFamilies = SelectCatalogSpecs(kitSpec, includeOptionalFamilies: false);
        StarterKitSpec filteredSpec = new(kitSpec.Header, kitSpec.Rte, kitSpec.Materials3D, selectedFamilies);
        return ExpectedManifest.FromSpec(filteredSpec, standards);
    }

    /// <summary>
    /// Compiles, snapshots, and lints the selected Starter Kit catalog in pure C#, returning the complete
    /// canonical <see cref="List{ManifestFamilyEntry}"/> for offline verification and T8 manifest gating.
    /// </summary>
    public static StarterKitCatalogPlanResult CompileAndLintCatalog(
        StarterKitSpec kitSpec,
        bool includeOptionalFamilies = true,
        StarterKitReferenceStandards? standards = null,
        long? simulatedFileSizeBytes = 128 * 1024)
    {
        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();
        IReadOnlyList<FamilyGeometrySpec> selected = SelectCatalogSpecs(kitSpec, includeOptionalFamilies);

        List<FamilyGeometryPlan> plans = new(selected.Count);
        List<RfaLintReport> reports = new(selected.Count);
        List<ManifestFamilyEntry> entries = new(selected.Count);

        foreach (FamilyGeometrySpec spec in selected)
        {
            FamilyGeometryPlan plan = FamilyGeometryPlanBuilder.Build(spec, kitSpec, refStandards);
            RfaInspectionSnapshot snapshot = plan.BuildExpectedSnapshot(simulatedFileSizeBytes);
            RfaLintReport report = RfaLinter.Lint(snapshot, spec, kitSpec, refStandards);

            plans.Add(plan);
            reports.Add(report);
            entries.Add(snapshot.ToManifestFamilyEntry(spec));
        }

        return new StarterKitCatalogPlanResult(
            includeOptionalFamilies,
            Array.AsReadOnly(plans.ToArray()),
            Array.AsReadOnly(reports.ToArray()),
            entries);
    }
}
