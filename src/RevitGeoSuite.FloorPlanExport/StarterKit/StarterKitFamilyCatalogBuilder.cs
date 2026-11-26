using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>Options controlling <see cref="StarterKitFamilyCatalogBuilder.BuildCatalog"/> runs.</summary>
public sealed class StarterKitCatalogBuildOptions
{
    public static StarterKitCatalogBuildOptions Default { get; } = new();

    /// <summary>
    /// When true (default), includes optional Starter Kit families such as <c>KNZ_POI_FacilityMarker</c>
    /// (building all 16 catalog families). When false, builds only the 15 required families.
    /// </summary>
    public bool IncludeOptionalFamilies { get; set; } = true;
}

/// <summary>
/// Complete deterministic result of building and linting the full Starter Kit family catalog
/// (Circulation + Openings + Fixtures &amp; Columns + optional POI) and emitting the canonical
/// <see cref="List{ManifestFamilyEntry}"/> consumed by T8's <c>TemplateBuilder</c> and <c>ManifestDumper</c>.
/// </summary>
public sealed class StarterKitCatalogBuildResult
{
    public StarterKitCatalogBuildResult(
        bool includedOptionalFamilies,
        CirculationBuildBatchResult circulationResult,
        FamilyBatchBuildResult openingResult,
        FamilyBatchBuildResult fixtureAndColumnResult,
        IReadOnlyList<FamilyBuildResult> extraResults)
    {
        IncludedOptionalFamilies = includedOptionalFamilies;
        CirculationResult = circulationResult ?? throw new ArgumentNullException(nameof(circulationResult));
        OpeningResult = openingResult ?? throw new ArgumentNullException(nameof(openingResult));
        FixtureAndColumnResult = fixtureAndColumnResult ?? throw new ArgumentNullException(nameof(fixtureAndColumnResult));
        ExtraResults = extraResults ?? Array.Empty<FamilyBuildResult>();

        List<FamilyBuildResult> all = new();
        all.AddRange(CirculationResult.Results);
        all.AddRange(OpeningResult.Results);
        all.AddRange(FixtureAndColumnResult.Results);
        all.AddRange(ExtraResults);

        Results = Array.AsReadOnly(all.ToArray());
        ManifestEntries = all.Select(r => r.ManifestEntry).ToList();
        AllPassed = Results.Count > 0 && Results.All(r => r.Succeeded);
    }

    public bool IncludedOptionalFamilies { get; }

    public CirculationBuildBatchResult CirculationResult { get; }

    public FamilyBatchBuildResult OpeningResult { get; }

    public FamilyBatchBuildResult FixtureAndColumnResult { get; }

    public IReadOnlyList<FamilyBuildResult> ExtraResults { get; }

    public IReadOnlyList<FamilyBuildResult> Results { get; }

    /// <summary>Complete list of built <see cref="ManifestFamilyEntry"/> items ready for T8 manifest gating.</summary>
    public List<ManifestFamilyEntry> ManifestEntries { get; }

    public bool AllPassed { get; }

    public IReadOnlyList<FamilyBuildResult> FailedFamilies =>
        Array.AsReadOnly(Results.Where(r => !r.Succeeded).ToArray());
}

/// <summary>
/// Unified entry point building all required (and optionally included) Kanizu Starter Kit <c>.rfa</c> families
/// in deterministic order: Circulation (4) -&gt; Openings (5) -&gt; Fixtures &amp; Columns (6) -&gt; optional POI (0 or 1),
/// linting every built <c>.rfa</c> with <see cref="RevitRfaInspector"/> + <see cref="RfaLinter"/>, and emitting the
/// complete <see cref="List{ManifestFamilyEntry}"/> for T8's <c>TemplateBuilder</c> &amp; <c>ManifestDumper</c>.
/// </summary>
public static class StarterKitFamilyCatalogBuilder
{
    public static StarterKitCatalogBuildResult BuildCatalog(
        Application application,
        StarterKitSpec kitSpec,
        FamilyBuildOptions options,
        StarterKitCatalogBuildOptions? catalogOptions = null,
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

        StarterKitCatalogBuildOptions catOpts = catalogOptions ?? StarterKitCatalogBuildOptions.Default;
        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();

        CirculationBuildBatchResult circulation = CirculationFamilyBuilder.BuildAll(
            application,
            kitSpec,
            options,
            refStandards);

        FamilyBatchBuildResult openings = OpeningFamilyBuilder.BuildAll(
            application,
            kitSpec,
            options,
            refStandards);

        FamilyBatchBuildResult fixturesAndColumns = FixtureAndColumnFamilyBuilder.BuildAll(
            application,
            kitSpec,
            options,
            includeOptionalPoi: catOpts.IncludeOptionalFamilies,
            standards: refStandards);

        // Build any extra non-standard families registered in kit.json beyond the canonical 16.
        HashSet<string> builtIds = new(
            circulation.Results.Select(r => r.FamilyId)
                .Concat(openings.Results.Select(r => r.FamilyId))
                .Concat(fixturesAndColumns.Results.Select(r => r.FamilyId)),
            StringComparer.Ordinal);

        List<FamilyBuildResult> extraResults = new();
        foreach (FamilyGeometrySpec spec in StarterKitCatalogSelector.SelectCatalogSpecs(kitSpec, catOpts.IncludeOptionalFamilies))
        {
            if (builtIds.Add(spec.Id))
            {
                extraResults.Add(FamilyBuilder.BuildFamily(application, spec, kitSpec, options, refStandards));
            }
        }

        return new StarterKitCatalogBuildResult(
            catOpts.IncludeOptionalFamilies,
            circulation,
            openings,
            fixturesAndColumns,
            Array.AsReadOnly(extraResults.ToArray()));
    }
}
