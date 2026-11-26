using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.StarterKit;

/// <summary>Aggregated result of building and linting the 4 T6 Starter Kit circulation families.</summary>
public sealed class CirculationBuildBatchResult
{
    public CirculationBuildBatchResult(IReadOnlyList<FamilyBuildResult> results)
    {
        Results = results ?? Array.Empty<FamilyBuildResult>();
        ManifestEntries = Array.AsReadOnly(Results.Select(r => r.ManifestEntry).ToArray());
        AllPassed = Results.Count > 0 && Results.All(r => r.Succeeded);
    }

    public IReadOnlyList<FamilyBuildResult> Results { get; }

    public IReadOnlyList<ManifestFamilyEntry> ManifestEntries { get; }

    public bool AllPassed { get; }
}

/// <summary>
/// Builds and gates the 4 T6 Circulation Starter Kit families:
/// <c>KNZ_CIRC_Escalator</c>, <c>KNZ_CIRC_MovingWalkway</c>, <c>KNZ_CIRC_Elevator</c>, and <c>KNZ_CIRC_ElevatorDoor</c>.
/// Emits canonical <see cref="ManifestFamilyEntry"/> records compatible with <see cref="ExpectedManifest"/>
/// and <see cref="StarterKitManifestComparer"/>.
/// </summary>
public static class CirculationFamilyBuilder
{
    public const string EscalatorId = "KNZ_CIRC_Escalator";
    public const string MovingWalkwayId = "KNZ_CIRC_MovingWalkway";
    public const string ElevatorId = "KNZ_CIRC_Elevator";
    public const string ElevatorDoorId = "KNZ_CIRC_ElevatorDoor";

    public static readonly IReadOnlyList<string> TargetFamilyIds = Array.AsReadOnly(new[]
    {
        EscalatorId,
        MovingWalkwayId,
        ElevatorId,
        ElevatorDoorId,
    });

    /// <summary>Returns the 4 T6 circulation <see cref="FamilyGeometrySpec"/> definitions from <paramref name="kitSpec"/>.</summary>
    public static IReadOnlyList<FamilyGeometrySpec> SelectCirculationSpecs(StarterKitSpec kitSpec)
    {
        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        List<FamilyGeometrySpec> selected = new(TargetFamilyIds.Count);
        foreach (string id in TargetFamilyIds)
        {
            FamilyGeometrySpec spec = kitSpec.FindFamily(id)
                ?? throw new InvalidOperationException($"Starter Kit spec is missing required T6 circulation family '{id}'.");
            selected.Add(spec);
        }

        return Array.AsReadOnly(selected.ToArray());
    }

    /// <summary>Builds, saves, inspects, and lints all 4 T6 circulation <c>.rfa</c> families.</summary>
    public static CirculationBuildBatchResult BuildAll(
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
        IReadOnlyList<FamilyGeometrySpec> specs = SelectCirculationSpecs(kitSpec);
        List<FamilyBuildResult> results = new(specs.Count);

        foreach (FamilyGeometrySpec spec in specs)
        {
            results.Add(FamilyBuilder.BuildFamily(application, spec, kitSpec, options, refStandards));
        }

        return new CirculationBuildBatchResult(Array.AsReadOnly(results.ToArray()));
    }
}
