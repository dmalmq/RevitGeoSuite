using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>Triangle count breakdown for one top-level procedural geometry op.</summary>
public sealed class OpTriangleBreakdown
{
    public OpTriangleBreakdown(string detailLevel, string opKind, string name, int triangles)
    {
        DetailLevel = detailLevel ?? string.Empty;
        OpKind = opKind ?? string.Empty;
        Name = name ?? string.Empty;
        Triangles = triangles;
    }

    public string DetailLevel { get; }

    public string OpKind { get; }

    public string Name { get; }

    public int Triangles { get; }
}

/// <summary>Deterministic Coarse and Medium/Fine triangle estimates and budget checks for one family.</summary>
public sealed class FamilyTriangleEstimate
{
    public FamilyTriangleEstimate(
        string familyId,
        int coarseTriangles,
        int coarseBudget,
        int mediumFineTriangles,
        int mediumFineBudget,
        IReadOnlyList<OpTriangleBreakdown> breakdown)
    {
        FamilyId = familyId ?? string.Empty;
        CoarseTriangles = coarseTriangles;
        CoarseBudget = coarseBudget;
        MediumFineTriangles = mediumFineTriangles;
        MediumFineBudget = mediumFineBudget;
        Breakdown = breakdown ?? Array.Empty<OpTriangleBreakdown>();
    }

    public string FamilyId { get; }

    public int CoarseTriangles { get; }

    public int CoarseBudget { get; }

    public int MediumFineTriangles { get; }

    public int MediumFineBudget { get; }

    public bool CoarseWithinBudget => CoarseTriangles <= CoarseBudget;

    public bool MediumFineWithinBudget => MediumFineTriangles <= MediumFineBudget;

    public bool WithinBudget => CoarseWithinBudget && MediumFineWithinBudget;

    public IReadOnlyList<OpTriangleBreakdown> Breakdown { get; }
}

/// <summary>
/// Pure-C# deterministic triangle counter for procedural planar-only geometry operations.
/// <list type="bullet">
/// <item><description><c>box</c>: 6 rectangular faces × 2 = <c>12</c> triangles.</description></item>
/// <item><description><c>nprism(n)</c> (<c>n &gt;= 3</c>): two <c>n</c>-gon caps (<c>2(n - 2)</c>) + <c>n</c> side quads (<c>2n</c>) = <c>4n - 4</c> triangles.</description></item>
/// <item><description><c>slopedPrism</c>: <c>12</c> triangles for default 4-sided cross-section, or <c>4n - 4</c> for <c>n &gt;= 3</c> sides.</description></item>
/// <item><description><c>array(count, child/children)</c>: <c>count * sum(EstimateOp(child))</c>.</description></item>
/// <item><description><c>void</c>: exact/conservative convex planar cut triangulation delta for a <c>k</c>-gon prism cut
/// (<c>k = 4</c> for <c>box</c>, <c>k = sides</c> for <c>nprism</c>):
/// through-cut (<c>throughCut = true</c>) splits 2 faces (<c>2 * (k + 2)</c> extra tris) + adds <c>k</c> inner wall quads (<c>2k</c> tris) = <c>4k + 4</c> (<c>20</c> for box);
/// blind pocket (<c>throughCut = false</c>) splits 1 face (<c>k + 2</c>) + <c>k</c> inner walls (<c>2k</c>) + 1 pocket cap (<c>k - 2</c>) = <c>4k</c> (<c>16</c> for box).</description></item>
/// </list>
/// </summary>
public static class TriangleEstimator
{
    public const int BoxTriangles = 12;
    public const int MaxNestingDepth = 16;

    /// <summary>Returns the exact planar triangle count for a single procedural geometry op.</summary>
    public static int EstimateOp(GeometryOpSpec op)
    {
        return EstimateOp(op, depth: 0);
    }

    /// <summary>Returns the total planar triangle count across a sequence of geometry ops.</summary>
    public static int EstimateOps(IEnumerable<GeometryOpSpec> ops)
    {
        if (ops is null)
        {
            throw new ArgumentNullException(nameof(ops));
        }

        int total = 0;
        foreach (GeometryOpSpec op in ops)
        {
            total = checked(total + EstimateOp(op, depth: 0));
        }

        return total;
    }

    /// <summary>Estimates Coarse and Medium/Fine triangles for one family and checks them against its budget.</summary>
    public static FamilyTriangleEstimate EstimateFamily(FamilyGeometrySpec family)
    {
        if (family is null)
        {
            throw new ArgumentNullException(nameof(family));
        }

        List<OpTriangleBreakdown> breakdown = new();
        int coarseTotal = 0;
        if (family.Coarse != null)
        {
            foreach (GeometryOpSpec op in family.Coarse.Ops)
            {
                int tris = EstimateOp(op, depth: 0);
                coarseTotal = checked(coarseTotal + tris);
                breakdown.Add(new OpTriangleBreakdown("coarse", op.Op, op.Name, tris));
            }
        }

        int mediumFineTotal = 0;
        foreach (GeometryOpSpec op in family.MediumFine.Ops)
        {
            int tris = EstimateOp(op, depth: 0);
            mediumFineTotal = checked(mediumFineTotal + tris);
            breakdown.Add(new OpTriangleBreakdown("mediumFine", op.Op, op.Name, tris));
        }

        return new FamilyTriangleEstimate(
            family.Id,
            coarseTotal,
            family.TriangleBudget.CoarseMax,
            mediumFineTotal,
            family.TriangleBudget.MediumFineMax,
            Array.AsReadOnly(breakdown.ToArray()));
    }

    /// <summary>Estimates every family registered in a <see cref="StarterKitSpec"/>.</summary>
    public static IReadOnlyList<FamilyTriangleEstimate> EstimateKit(StarterKitSpec kit)
    {
        if (kit is null)
        {
            throw new ArgumentNullException(nameof(kit));
        }

        return Array.AsReadOnly(kit.Families.Select(EstimateFamily).ToArray());
    }

    /// <summary>Exact triangle count of a convex planar <paramref name="sides"/>-sided prism: <c>4n - 4</c>.</summary>
    public static int PrismTriangles(int sides)
    {
        if (sides < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(sides), sides, "An n-prism requires at least 3 sides.");
        }

        return checked((4 * sides) - 4);
    }

    /// <summary>
    /// Exact/conservative triangle delta for a planar void cut of <paramref name="sides"/> sides:
    /// <c>4k + 4</c> for a through-cut (2 punctured faces + inner side walls) or <c>4k</c> for a blind pocket
    /// (1 punctured face + inner side walls + pocket cap).
    /// </summary>
    public static int VoidCutTriangles(int sides, bool throughCut)
    {
        if (sides < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(sides), sides, "A void prism requires at least 3 sides.");
        }

        return throughCut ? checked((4 * sides) + 4) : checked(4 * sides);
    }

    private static int EstimateOp(GeometryOpSpec op, int depth)
    {
        if (op is null)
        {
            throw new ArgumentNullException(nameof(op));
        }

        if (depth > MaxNestingDepth)
        {
            throw new ArgumentException($"Geometry op '{op.Name}' exceeds maximum nesting depth {MaxNestingDepth}.", nameof(op));
        }

        switch (op.Op)
        {
            case GeometryOpSpec.OpBox:
                ValidatePositiveVector3(op.SizeMm, "sizeMm", op.Name);
                return BoxTriangles;

            case GeometryOpSpec.OpNPrism:
                if (!op.Sides.HasValue || op.Sides.Value < 3)
                {
                    throw new ArgumentException($"nprism op '{op.Name}' requires sides >= 3.", nameof(op));
                }

                ValidatePositiveScalar(op.RadiusMm, "radiusMm", op.Name);
                ValidatePositiveScalar(op.HeightMm, "heightMm", op.Name);
                return PrismTriangles(op.Sides.Value);

            case GeometryOpSpec.OpSlopedPrism:
                ValidatePositiveScalar(op.WidthMm, "widthMm", op.Name);
                ValidatePositiveScalar(op.RunMm, "runMm", op.Name);
                ValidatePositiveScalar(op.ThicknessMm, "thicknessMm", op.Name);
                if (op.RiseMm.HasValue && (double.IsNaN(op.RiseMm.Value) || double.IsInfinity(op.RiseMm.Value)))
                {
                    throw new ArgumentException($"slopedPrism op '{op.Name}' has non-finite riseMm.", nameof(op));
                }

                int slopedSides = op.Sides ?? 4;
                if (slopedSides < 3)
                {
                    throw new ArgumentException($"slopedPrism op '{op.Name}' requires sides >= 3 when specified.", nameof(op));
                }

                return PrismTriangles(slopedSides);

            case GeometryOpSpec.OpArray:
                if (!op.Count.HasValue || op.Count.Value < 1)
                {
                    throw new ArgumentException($"array op '{op.Name}' requires count >= 1.", nameof(op));
                }

                List<GeometryOpSpec> nested = CollectArrayChildren(op);
                if (nested.Count == 0)
                {
                    throw new ArgumentException($"array op '{op.Name}' requires a child or non-empty children list.", nameof(op));
                }

                int perItem = 0;
                foreach (GeometryOpSpec child in nested)
                {
                    perItem = checked(perItem + EstimateOp(child, depth + 1));
                }

                return checked(op.Count.Value * perItem);

            case GeometryOpSpec.OpVoid:
                string shape = string.IsNullOrWhiteSpace(op.Shape) ? GeometryOpSpec.OpBox : op.Shape!.Trim();
                bool throughCut = op.ThroughCut ?? true;
                if (string.Equals(shape, GeometryOpSpec.OpBox, StringComparison.Ordinal))
                {
                    ValidatePositiveVector3(op.SizeMm, "sizeMm", op.Name);
                    return VoidCutTriangles(4, throughCut);
                }

                if (string.Equals(shape, GeometryOpSpec.OpNPrism, StringComparison.Ordinal))
                {
                    if (!op.Sides.HasValue || op.Sides.Value < 3)
                    {
                        throw new ArgumentException($"nprism void op '{op.Name}' requires sides >= 3.", nameof(op));
                    }

                    ValidatePositiveScalar(op.RadiusMm, "radiusMm", op.Name);
                    ValidatePositiveScalar(op.HeightMm, "heightMm", op.Name);
                    return VoidCutTriangles(op.Sides.Value, throughCut);
                }

                throw new ArgumentException($"void op '{op.Name}' has unsupported shape '{shape}'. Expected 'box' or 'nprism'.", nameof(op));

            default:
                throw new ArgumentException($"Unknown geometry op kind '{op.Op}' in op '{op.Name}'.", nameof(op));
        }
    }

    internal static List<GeometryOpSpec> CollectArrayChildren(GeometryOpSpec op)
    {
        List<GeometryOpSpec> result = new();
        if (op.Child != null)
        {
            result.Add(op.Child);
        }

        if (op.Children != null)
        {
            result.AddRange(op.Children);
        }

        return result;
    }

    private static void ValidatePositiveScalar(double? value, string fieldName, string opName)
    {
        if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value) || value.Value <= 0.0)
        {
            throw new ArgumentException($"Geometry op '{opName}' requires finite positive {fieldName}.", fieldName);
        }
    }

    private static void ValidatePositiveVector3(IReadOnlyList<double>? vector, string fieldName, string opName)
    {
        if (vector == null || vector.Count != 3)
        {
            throw new ArgumentException($"Geometry op '{opName}' requires a 3-element {fieldName} vector.", fieldName);
        }

        for (int i = 0; i < 3; i++)
        {
            double component = vector[i];
            if (double.IsNaN(component) || double.IsInfinity(component) || component <= 0.0)
            {
                throw new ArgumentException($"Geometry op '{opName}' requires all {fieldName} components to be finite and > 0.", fieldName);
            }
        }
    }
}
