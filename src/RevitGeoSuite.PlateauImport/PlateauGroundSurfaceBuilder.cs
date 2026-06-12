using System;
using System.Collections.Generic;
using RevitGeoSuite.Core.Plateau.Dem;

namespace RevitGeoSuite.PlateauImport;

/// <summary>
/// Source-agnostic ground builder: walks a regular grid over a <see cref="DemSampler"/>'s extent,
/// samples elevations, reconciles the vertical datum with a geoid-undulation offset, and projects
/// each hit into the Revit model frame (feet) via <see cref="PlateauReferenceFrame"/>. The result is
/// a flat point list ready for <c>TopographySurface.Create</c>. It has no Revit dependency, so the
/// same builder serves both the local (dem GML) and online (Cesium Ion terrain) sources.
/// </summary>
public sealed class PlateauGroundSurfaceBuilder
{
    public const int DefaultMaxPoints = 40_000;
    public const double DefaultGridSpacingMeters = 10d;

    private const double DegenerateExtentMeters = 1e-6d;

    private readonly int maxPoints;

    public PlateauGroundSurfaceBuilder(int maxPoints = DefaultMaxPoints)
    {
        if (maxPoints < 4)
        {
            throw new ArgumentOutOfRangeException(nameof(maxPoints), maxPoints, "At least four ground sample points are required.");
        }

        this.maxPoints = maxPoints;
    }

    public GroundSurfaceResult Build(
        DemSampler sampler,
        PlateauImportReferenceContext referenceContext,
        double gridSpacingMeters,
        double geoidOffsetMeters)
    {
        if (sampler is null) throw new ArgumentNullException(nameof(sampler));
        if (referenceContext is null) throw new ArgumentNullException(nameof(referenceContext));

        List<string> warnings = new List<string>();
        if (sampler.IsEmpty)
        {
            warnings.Add("The DEM source produced no elevation data, so no ground surface could be built.");
            return new GroundSurfaceResult(Array.Empty<ContextShapePoint3D>(), gridSpacingMeters, gridSpacingMeters, 0, warnings);
        }

        double width = sampler.MaxX - sampler.MinX;
        double height = sampler.MaxY - sampler.MinY;
        if (!IsFinite(width) || !IsFinite(height) || width < DegenerateExtentMeters || height < DegenerateExtentMeters)
        {
            warnings.Add("The DEM source covers a zero-area or invalid extent, so no ground surface could be built.");
            return new GroundSurfaceResult(Array.Empty<ContextShapePoint3D>(), gridSpacingMeters, gridSpacingMeters, 0, warnings);
        }

        double requestedSpacing = gridSpacingMeters > 0 ? gridSpacingMeters : DefaultGridSpacingMeters;
        // Coarsen the spacing if the requested density would exceed the point cap. area / s^2 ≈ point
        // count, so the smallest spacing that fits the cap is sqrt(area / maxPoints).
        double minSpacingForCap = Math.Sqrt((width * height) / maxPoints);
        double effectiveSpacing = Math.Max(requestedSpacing, minSpacingForCap);
        if (effectiveSpacing > requestedSpacing * 1.0001d)
        {
            warnings.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "Ground grid coarsened from {0:0.##} m to {1:0.##} m to stay within {2:N0} sample points.",
                requestedSpacing,
                effectiveSpacing,
                maxPoints));
        }

        // Per-axis cap: the spacing above bounds the total grid for a roughly square extent, but a
        // very large or elongated extent (e.g. a stray far-off relief triangle, or many tiles) could
        // still make one axis request a gigantic array. Cap each axis independently and never cast a
        // huge/infinite double to int — that's what threw "Array dimensions exceeded supported range".
        int maxAxisSamples = Math.Max(2, (int)Math.Ceiling(Math.Sqrt(maxPoints)) * 4);
        double[] xs = BuildAxisSamples(sampler.MinX, sampler.MaxX, effectiveSpacing, maxAxisSamples, out bool xClamped);
        double[] ys = BuildAxisSamples(sampler.MinY, sampler.MaxY, effectiveSpacing, maxAxisSamples, out bool yClamped);
        if (xClamped || yClamped)
        {
            warnings.Add(string.Format(
                System.Globalization.CultureInfo.InvariantCulture,
                "DEM extent is unusually large/elongated ({0:N0} × {1:N0} m); the ground grid was capped to {2} × {3} samples. Heights stay correct but the surface is coarser — if this looks wrong, the selected DEM tiles may include a stray surface.",
                width,
                height,
                xs.Length,
                ys.Length));
        }

        List<ContextShapePoint3D> points = new List<ContextShapePoint3D>(xs.Length * ys.Length);
        int skipped = 0;
        foreach (double y in ys)
        {
            foreach (double x in xs)
            {
                if (!sampler.TrySampleElevation(x, y, out double elevationMeters))
                {
                    skipped++;
                    continue;
                }

                double adjustedElevation = elevationMeters - geoidOffsetMeters;
                (double xFeet, double yFeet) = PlateauReferenceFrame.ToLocalFeet(x, y, referenceContext);
                double zFeet = PlateauReferenceFrame.ToLocalElevationFeet(adjustedElevation, referenceContext);
                points.Add(new ContextShapePoint3D(xFeet, yFeet, zFeet));
            }
        }

        if (points.Count < 3)
        {
            warnings.Add("Fewer than three ground points fell inside the DEM coverage, so no surface could be built.");
        }

        return new GroundSurfaceResult(points, requestedSpacing, effectiveSpacing, skipped, warnings);
    }

    /// <summary>
    /// Evenly spaced coordinates from <paramref name="min"/> to <paramref name="max"/> inclusive,
    /// using the fewest steps whose interval does not exceed <paramref name="spacing"/>, but never
    /// more than <paramref name="maxSamples"/> coordinates. Both endpoints are exact (no near-duplicate
    /// that would upset topo triangulation). The step count is clamped in double space before the int
    /// cast so a huge or infinite extent/spacing can't overflow into a runaway array allocation.
    /// </summary>
    private static double[] BuildAxisSamples(double min, double max, double spacing, int maxSamples, out bool clamped)
    {
        double desiredSteps = spacing > 0 ? Math.Ceiling((max - min) / spacing) : maxSamples;
        if (double.IsNaN(desiredSteps) || desiredSteps < 1d)
        {
            desiredSteps = 1d;
        }

        int maxSteps = Math.Max(1, maxSamples - 1);
        int steps = (int)Math.Min(maxSteps, desiredSteps);
        clamped = desiredSteps > maxSteps;

        double[] coords = new double[steps + 1];
        for (int i = 0; i <= steps; i++)
        {
            coords[i] = min + ((max - min) * i / steps);
        }

        return coords;
    }

    private static bool IsFinite(double value)
    {
        // net48 has no double.IsFinite.
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }

    public sealed class GroundSurfaceResult
    {
        public GroundSurfaceResult(
            IReadOnlyList<ContextShapePoint3D> points,
            double requestedSpacingMeters,
            double effectiveSpacingMeters,
            int skippedSampleCount,
            IReadOnlyList<string> warnings)
        {
            Points = points;
            RequestedSpacingMeters = requestedSpacingMeters;
            EffectiveSpacingMeters = effectiveSpacingMeters;
            SkippedSampleCount = skippedSampleCount;
            Warnings = warnings;
        }

        public IReadOnlyList<ContextShapePoint3D> Points { get; }

        public double RequestedSpacingMeters { get; }

        public double EffectiveSpacingMeters { get; }

        public int SkippedSampleCount { get; }

        public IReadOnlyList<string> Warnings { get; }

        public int PointCount => Points.Count;
    }
}
