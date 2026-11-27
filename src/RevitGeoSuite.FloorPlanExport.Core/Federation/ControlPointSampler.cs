using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;

namespace RevitGeoSuite.FloorPlanExport.Core.Federation;

/// <summary>One named shared position of a linked file, as a way to place link-internal points in shared coordinates.</summary>
public sealed class SharedPositionCandidate
{
    public SharedPositionCandidate(string name, Func<FederationPoint3D, FederationPoint3D> linkInternalFeetToSharedFeet)
    {
        Name = name ?? string.Empty;
        LinkInternalFeetToSharedFeet = linkInternalFeetToSharedFeet ?? throw new ArgumentNullException(nameof(linkInternalFeetToSharedFeet));
    }

    public string Name { get; }

    /// <summary>Link-internal point (feet) to the link's own shared East, North, Elevation (feet), as <c>ProjectLocation.GetProjectPosition</c> gives.</summary>
    public Func<FederationPoint3D, FederationPoint3D> LinkInternalFeetToSharedFeet { get; }
}

/// <summary>The shared position a link instance best matches, with its control points and pose.</summary>
public sealed class SampledLinkPose
{
    public SampledLinkPose(string positionName, IReadOnlyList<ControlPointPair> controlPoints, PoseConsistencyResult pose)
    {
        PositionName = positionName;
        ControlPoints = controlPoints;
        Pose = pose;
    }

    public string PositionName { get; }

    public IReadOnlyList<ControlPointPair> ControlPoints { get; }

    public PoseConsistencyResult Pose { get; }
}

/// <summary>
/// Builds the KZ-FED-001 control points without Revit types. Three link-internal points (origin and
/// <c>baseline</c> along +X and +Y) go through two chains into shared coordinates: through the
/// instance transform and the host's shared position, and through the link's own shared position.
/// </summary>
public static class ControlPointSampler
{
    public static IReadOnlyList<(string Label, FederationPoint3D LinkInternalFeet)> SamplePoints(double baselineMeters)
    {
        double meters = Math.Max(1.0d, baselineMeters);
        double feet = meters / CrsTransformer.FeetToMetersFactor;
        string label = meters.ToString("0.#", CultureInfo.InvariantCulture);
        return new[]
        {
            ("Origin (0,0,0)", new FederationPoint3D(0d, 0d, 0d)),
            ($"+X Baseline ({label}m,0,0)", new FederationPoint3D(feet, 0d, 0d)),
            ($"+Y Baseline (0,{label}m,0)", new FederationPoint3D(0d, feet, 0d)),
        };
    }

    public static IReadOnlyList<ControlPointPair> Pair(
        double baselineMeters,
        Func<FederationPoint3D, FederationPoint3D> linkInternalFeetToHostInternalFeet,
        Func<FederationPoint3D, FederationPoint3D> hostInternalFeetToSharedFeet,
        Func<FederationPoint3D, FederationPoint3D> linkInternalFeetToSharedFeet) =>
        SamplePoints(baselineMeters)
            .Select(sample => new ControlPointPair(
                sample.Label,
                ToMeters(hostInternalFeetToSharedFeet(linkInternalFeetToHostInternalFeet(sample.LinkInternalFeet))),
                ToMeters(linkInternalFeetToSharedFeet(sample.LinkInternalFeet))))
            .ToList();

    /// <summary>
    /// The link file's shared position this instance sits at: the aligned one, else the closest.
    /// One file placed twice at named positions ("Site A", "Site B") matches each instance to its own.
    /// Null when the link file has no shared positions.
    /// </summary>
    public static SampledLinkPose? BestPosition(
        double baselineMeters,
        Func<FederationPoint3D, FederationPoint3D> linkInternalFeetToHostInternalFeet,
        Func<FederationPoint3D, FederationPoint3D> hostInternalFeetToSharedFeet,
        IEnumerable<SharedPositionCandidate> candidates,
        FederationCheckOptions? options = null)
    {
        SampledLinkPose? best = null;
        foreach (SharedPositionCandidate candidate in candidates ?? Array.Empty<SharedPositionCandidate>())
        {
            IReadOnlyList<ControlPointPair> points = Pair(
                baselineMeters,
                linkInternalFeetToHostInternalFeet,
                hostInternalFeetToSharedFeet,
                candidate.LinkInternalFeetToSharedFeet);
            SampledLinkPose sampled = new(candidate.Name, points, PoseConsistencyEvaluator.Evaluate(points, options));
            if (best == null || Score(sampled) < Score(best))
            {
                best = sampled;
            }
        }

        return best;
    }

    private static double Score(SampledLinkPose sampled) =>
        sampled.Pose.IsAligned ? -1d
        : sampled.Pose.IsDegenerateBaseline ? double.MaxValue
        : sampled.Pose.MaxHorizontalTranslationMm + sampled.Pose.MaxVerticalDriftMm;

    private static FederationPoint3D ToMeters(FederationPoint3D feet) =>
        new(
            feet.X * CrsTransformer.FeetToMetersFactor,
            feet.Y * CrsTransformer.FeetToMetersFactor,
            feet.Z * CrsTransformer.FeetToMetersFactor);
}
