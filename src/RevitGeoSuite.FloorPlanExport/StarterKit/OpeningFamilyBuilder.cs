using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>Aggregated result of building and linting one Starter Kit family group.</summary>
public sealed class FamilyBatchBuildResult
{
    public FamilyBatchBuildResult(string groupName, IReadOnlyList<FamilyBuildResult> results)
    {
        GroupName = groupName ?? string.Empty;
        Results = results ?? Array.Empty<FamilyBuildResult>();
        ManifestEntries = Array.AsReadOnly(Results.Select(r => r.ManifestEntry).ToArray());
        AllPassed = Results.Count > 0 && Results.All(r => r.Succeeded);
    }

    public string GroupName { get; }

    public IReadOnlyList<FamilyBuildResult> Results { get; }

    public IReadOnlyList<ManifestFamilyEntry> ManifestEntries { get; }

    public bool AllPassed { get; }
}

/// <summary>
/// Builds and gates the 5 T7 wall-hosted door/opening/shutter Starter Kit families:
/// <c>KNZ_OPEN_DoorSwing</c>, <c>KNZ_OPEN_DoorSlide</c>, <c>KNZ_OPEN_DoorAuto</c>,
/// <c>KNZ_OPEN_Shutter</c>, and <c>KNZ_OPEN_Opening</c>.
/// Preserves the <c>wallHostedDoor</c> seed's host <c>Wall</c> and wall-cut <c>Opening</c>,
/// synchronizes <c>Width</c>/<c>Height</c> to spec defaults, binds <c>gis_door</c>,
/// <c>gis_opening_category</c>, and <c>gis_access_con</c>, and gates every saved <c>.rfa</c>
/// with <see cref="RevitRfaInspector"/> and <see cref="RfaLinter"/>.
/// </summary>
public static class OpeningFamilyBuilder
{
    public const string DoorSwingId = StarterKitCatalogSelector.DoorSwingId;
    public const string DoorSlideId = StarterKitCatalogSelector.DoorSlideId;
    public const string DoorAutoId = StarterKitCatalogSelector.DoorAutoId;
    public const string ShutterId = StarterKitCatalogSelector.ShutterId;
    public const string OpeningId = StarterKitCatalogSelector.OpeningId;

    public static IReadOnlyList<string> TargetFamilyIds => StarterKitCatalogSelector.OpeningFamilyIds;

    /// <summary>
    /// Returns the 5 T7 opening <see cref="FamilyGeometrySpec"/> definitions from <paramref name="kitSpec"/>,
    /// validating that each declares <c>seedKind == "wallHostedDoor"</c> and required opening shared parameters.
    /// </summary>
    public static IReadOnlyList<FamilyGeometrySpec> SelectOpeningSpecs(StarterKitSpec kitSpec)
    {
        IReadOnlyList<FamilyGeometrySpec> specs = StarterKitCatalogSelector.SelectOpeningSpecs(kitSpec);
        foreach (FamilyGeometrySpec spec in specs)
        {
            ValidateOpeningSpecContract(spec);
        }

        return specs;
    }

    /// <summary>Builds, saves, inspects, and lints all 5 T7 wall-hosted opening <c>.rfa</c> families.</summary>
    public static FamilyBatchBuildResult BuildAll(
        Application application,
        StarterKitSpec kitSpec,
        FamilyBuildOptions options,
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
        IReadOnlyList<FamilyGeometrySpec> specs = SelectOpeningSpecs(kitSpec);
        List<FamilyBuildResult> results = new(specs.Count);

        foreach (FamilyGeometrySpec spec in specs)
        {
            results.Add(FamilyBuilder.BuildFamily(application, spec, kitSpec, options, refStandards));
        }

        return new FamilyBatchBuildResult("openings", Array.AsReadOnly(results.ToArray()));
    }

    private static void ValidateOpeningSpecContract(FamilyGeometrySpec spec)
    {
        if (!string.Equals(spec.SeedKind, FamilyGeometrySpec.SeedWallHostedDoor, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Opening family '{spec.Id}' must declare seedKind '{FamilyGeometrySpec.SeedWallHostedDoor}', found '{spec.SeedKind}'.");
        }

        if (!string.Equals(spec.Category, "OST_Doors", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Opening family '{spec.Id}' must target category 'OST_Doors', found '{spec.Category}'.");
        }

        RequireNonEmptySpecDefault(spec, GisSharedParameters.GisDoor);
        RequireNonEmptySpecDefault(spec, GisSharedParameters.GisOpeningCategory);
        RequireNonEmptySpecDefault(spec, GisSharedParameters.GisAccessCon);
    }

    private static void RequireNonEmptySpecDefault(FamilyGeometrySpec spec, string paramName)
    {
        FamilySharedParameterSpec? param = spec.SharedParameters.FirstOrDefault(
            p => string.Equals(p.Name, paramName, StringComparison.Ordinal));
        if (param == null || string.IsNullOrWhiteSpace(param.DefaultValue))
        {
            throw new InvalidOperationException(
                $"Opening family '{spec.Id}' must declare a non-empty default for shared parameter '{paramName}'.");
        }
    }
}
