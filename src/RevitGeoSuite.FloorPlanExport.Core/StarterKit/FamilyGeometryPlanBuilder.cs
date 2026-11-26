using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.StarterKit;

/// <summary>Deterministic 3D point stored in both millimetres and Revit internal feet.</summary>
public readonly struct PlanPoint3D
{
    public PlanPoint3D(double xMm, double yMm, double zMm)
    {
        XMm = xMm;
        YMm = yMm;
        ZMm = zMm;
        XFeet = xMm * FamilyGeometryPlanBuilder.MillimetersToFeet;
        YFeet = yMm * FamilyGeometryPlanBuilder.MillimetersToFeet;
        ZFeet = zMm * FamilyGeometryPlanBuilder.MillimetersToFeet;
    }

    public double XMm { get; }

    public double YMm { get; }

    public double ZMm { get; }

    public double XFeet { get; }

    public double YFeet { get; }

    public double ZFeet { get; }
}

/// <summary>Orthonormal sketch-plane frame used to create a Revit <c>SketchPlane</c> for an extrusion.</summary>
public sealed class CompiledSketchPlane
{
    public CompiledSketchPlane(
        PlanPoint3D origin,
        double xAxisX,
        double xAxisY,
        double xAxisZ,
        double yAxisX,
        double yAxisY,
        double yAxisZ,
        double normalX,
        double normalY,
        double normalZ)
    {
        Origin = origin;
        XAxisX = xAxisX;
        XAxisY = xAxisY;
        XAxisZ = xAxisZ;
        YAxisX = yAxisX;
        YAxisY = yAxisY;
        YAxisZ = yAxisZ;
        NormalX = normalX;
        NormalY = normalY;
        NormalZ = normalZ;
    }

    public PlanPoint3D Origin { get; }

    public double XAxisX { get; }

    public double XAxisY { get; }

    public double XAxisZ { get; }

    public double YAxisX { get; }

    public double YAxisY { get; }

    public double YAxisZ { get; }

    public double NormalX { get; }

    public double NormalY { get; }

    public double NormalZ { get; }

    /// <summary>Horizontal XY plane at <paramref name="zMm"/> with normal +Z.</summary>
    public static CompiledSketchPlane HorizontalXY(double zMm) =>
        new(
            new PlanPoint3D(0.0, 0.0, zMm),
            xAxisX: 1.0, xAxisY: 0.0, xAxisZ: 0.0,
            yAxisX: 0.0, yAxisY: 1.0, yAxisZ: 0.0,
            normalX: 0.0, normalY: 0.0, normalZ: 1.0);

    /// <summary>Vertical YZ plane at <paramref name="xMm"/> with normal +X (used for sloped prisms).</summary>
    public static CompiledSketchPlane VerticalYZ(double xMm) =>
        new(
            new PlanPoint3D(xMm, 0.0, 0.0),
            xAxisX: 0.0, xAxisY: 1.0, xAxisZ: 0.0,
            yAxisX: 0.0, yAxisY: 0.0, yAxisZ: 1.0,
            normalX: 1.0, normalY: 0.0, normalZ: 0.0);
}

/// <summary>
/// One deterministic unit-converted solid or void extrusion primitive compiled from a declarative
/// <see cref="GeometryOpSpec"/> (<c>box</c>, <c>nprism</c>, <c>slopedPrism</c>, expanded <c>array</c>, or <c>void</c>).
/// </summary>
public sealed class CompiledExtrusionPrimitive
{
    public CompiledExtrusionPrimitive(
        string name,
        string sourceOpName,
        string opKind,
        bool isSolid,
        bool throughCut,
        string? targetOpName,
        string subcategory,
        string materialRef,
        bool visibleInCoarse,
        bool visibleInMedium,
        bool visibleInFine,
        CompiledSketchPlane sketchPlane,
        IReadOnlyList<PlanPoint3D> profileVertices,
        double extrusionDepthMm,
        double minXMm,
        double maxXMm,
        double minYMm,
        double maxYMm,
        double minZMm,
        double maxZMm,
        int estimatedTriangles,
        IReadOnlyList<RfaPlanarFaceSnapshot> planarFaces)
    {
        Name = name ?? string.Empty;
        SourceOpName = sourceOpName ?? string.Empty;
        OpKind = opKind ?? string.Empty;
        IsSolid = isSolid;
        ThroughCut = throughCut;
        TargetOpName = targetOpName;
        Subcategory = subcategory ?? string.Empty;
        MaterialRef = materialRef ?? string.Empty;
        VisibleInCoarse = visibleInCoarse;
        VisibleInMedium = visibleInMedium;
        VisibleInFine = visibleInFine;
        SketchPlane = sketchPlane ?? throw new ArgumentNullException(nameof(sketchPlane));
        ProfileVertices = profileVertices ?? throw new ArgumentNullException(nameof(profileVertices));
        ExtrusionDepthMm = extrusionDepthMm;
        ExtrusionDepthFeet = extrusionDepthMm * FamilyGeometryPlanBuilder.MillimetersToFeet;
        MinXMm = minXMm;
        MaxXMm = maxXMm;
        MinYMm = minYMm;
        MaxYMm = maxYMm;
        MinZMm = minZMm;
        MaxZMm = maxZMm;
        EstimatedTriangles = estimatedTriangles;
        PlanarFaces = planarFaces ?? Array.Empty<RfaPlanarFaceSnapshot>();
    }

    public string Name { get; }

    public string SourceOpName { get; }

    public string OpKind { get; }

    public bool IsSolid { get; }

    public bool ThroughCut { get; }

    public string? TargetOpName { get; }

    public string Subcategory { get; }

    public string MaterialRef { get; }

    public bool VisibleInCoarse { get; }

    public bool VisibleInMedium { get; }

    public bool VisibleInFine { get; }

    public CompiledSketchPlane SketchPlane { get; }

    public IReadOnlyList<PlanPoint3D> ProfileVertices { get; }

    public double ExtrusionDepthMm { get; }

    public double ExtrusionDepthFeet { get; }

    public double MinXMm { get; }

    public double MaxXMm { get; }

    public double MinYMm { get; }

    public double MaxYMm { get; }

    public double MinZMm { get; }

    public double MaxZMm { get; }

    public int EstimatedTriangles { get; }

    public IReadOnlyList<RfaPlanarFaceSnapshot> PlanarFaces { get; }
}

/// <summary>One dimensional family parameter compiled for a Starter Kit family (stored in mm and feet).</summary>
public sealed class CompiledDimensionParameter
{
    public CompiledDimensionParameter(string name, double valueMm, bool isInstance = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        ValueMm = valueMm;
        ValueFeet = valueMm * FamilyGeometryPlanBuilder.MillimetersToFeet;
        IsInstance = isInstance;
    }

    public string Name { get; }

    public double ValueMm { get; }

    public double ValueFeet { get; }

    public bool IsInstance { get; }
}

/// <summary>One resolved Kanizu shared parameter binding compiled for a Starter Kit family.</summary>
public sealed class CompiledSharedParameter
{
    public CompiledSharedParameter(
        string name,
        Guid guid,
        GisParameterDataType dataType,
        GisParameterBinding binding,
        bool required,
        string defaultValue)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Guid = guid;
        DataType = dataType;
        Binding = binding;
        Required = required;
        DefaultValue = defaultValue ?? string.Empty;
    }

    public string Name { get; }

    public Guid Guid { get; }

    public GisParameterDataType DataType { get; }

    public GisParameterBinding Binding { get; }

    public bool IsInstance => Binding == GisParameterBinding.Instance;

    public bool Required { get; }

    public string DefaultValue { get; }
}

/// <summary>
/// Deterministic, Revit-independent build plan compiled from a <see cref="FamilyGeometrySpec"/>.
/// Consumed both by Revit's <c>FamilyBuilder</c> and by pure-C# Linux unit tests.
/// </summary>
public sealed class FamilyGeometryPlan
{
    public FamilyGeometryPlan(
        FamilyGeometrySpec spec,
        IReadOnlyList<string> subcategories,
        IReadOnlyList<Material3DSpec> requiredMaterials,
        IReadOnlyList<CompiledExtrusionPrimitive> coarsePrimitives,
        IReadOnlyList<CompiledExtrusionPrimitive> mediumFinePrimitives,
        IReadOnlyList<CompiledSharedParameter> sharedParameters,
        IReadOnlyList<CompiledDimensionParameter> dimensionParameters,
        FamilyTriangleEstimate triangleEstimate)
    {
        Spec = spec ?? throw new ArgumentNullException(nameof(spec));
        Subcategories = subcategories ?? Array.Empty<string>();
        RequiredMaterials = requiredMaterials ?? Array.Empty<Material3DSpec>();
        CoarsePrimitives = coarsePrimitives ?? Array.Empty<CompiledExtrusionPrimitive>();
        MediumFinePrimitives = mediumFinePrimitives ?? Array.Empty<CompiledExtrusionPrimitive>();
        // Emit Medium/Fine primitives (with each target solid paired immediately with its voids before
        // non-target solids) BEFORE Coarse K_Footprint solids so Revit's family-editor auto-cut never
        // carves notches out of K_Footprint or destroys overlapping glazing panes.
        AllPrimitives = Array.AsReadOnly(MediumFinePrimitives.Concat(CoarsePrimitives).ToArray());
        SharedParameters = sharedParameters ?? Array.Empty<CompiledSharedParameter>();
        DimensionParameters = dimensionParameters ?? Array.Empty<CompiledDimensionParameter>();
        TriangleEstimate = triangleEstimate ?? throw new ArgumentNullException(nameof(triangleEstimate));
    }

    public FamilyGeometrySpec Spec { get; }

    public IReadOnlyList<string> Subcategories { get; }

    public IReadOnlyList<Material3DSpec> RequiredMaterials { get; }

    public IReadOnlyList<CompiledExtrusionPrimitive> CoarsePrimitives { get; }

    public IReadOnlyList<CompiledExtrusionPrimitive> MediumFinePrimitives { get; }

    public IReadOnlyList<CompiledExtrusionPrimitive> AllPrimitives { get; }

    public IReadOnlyList<CompiledSharedParameter> SharedParameters { get; }

    public IReadOnlyList<CompiledDimensionParameter> DimensionParameters { get; }

    public FamilyTriangleEstimate TriangleEstimate { get; }

    /// <summary>
    /// Builds the expected <see cref="RfaInspectionSnapshot"/> corresponding to this compiled plan so
    /// <see cref="RfaLinter.Lint"/> can verify every family spec end-to-end without Revit.
    /// </summary>
    public RfaInspectionSnapshot BuildExpectedSnapshot(long? fileSizeBytes = null)
    {
        List<RfaSolidSnapshot> solids = new();
        foreach (CompiledExtrusionPrimitive primitive in AllPrimitives)
        {
            if (!primitive.IsSolid)
            {
                continue;
            }

            solids.Add(new RfaSolidSnapshot(
                primitive.Name,
                primitive.Subcategory,
                primitive.MaterialRef,
                primitive.VisibleInCoarse,
                primitive.VisibleInMedium,
                primitive.VisibleInFine,
                primitive.MinXMm,
                primitive.MaxXMm,
                primitive.MinYMm,
                primitive.MaxYMm,
                primitive.MinZMm,
                primitive.MaxZMm,
                primitive.EstimatedTriangles,
                primitive.PlanarFaces));
        }

        List<RfaMaterialSnapshot> materials = RequiredMaterials
            .Select(m => new RfaMaterialSnapshot(
                m.Name,
                m.Srgb.Count >= 3 ? m.Srgb[0] : 0,
                m.Srgb.Count >= 3 ? m.Srgb[1] : 0,
                m.Srgb.Count >= 3 ? m.Srgb[2] : 0,
                m.Transparency))
            .ToList();

        List<RfaParameterSnapshot> parameters = new();
        foreach (CompiledSharedParameter sp in SharedParameters)
        {
            parameters.Add(new RfaParameterSnapshot(
                sp.Name,
                isShared: true,
                guid: sp.Guid,
                isInstance: sp.IsInstance,
                storageKind: sp.DataType == GisParameterDataType.Integer ? "Integer" : "Text",
                defaultValue: sp.DefaultValue));
        }

        foreach (CompiledDimensionParameter dp in DimensionParameters)
        {
            parameters.Add(new RfaParameterSnapshot(
                dp.Name,
                isShared: false,
                guid: null,
                isInstance: dp.IsInstance,
                storageKind: "Length",
                defaultValue: dp.ValueMm.ToString("0.###", CultureInfo.InvariantCulture),
                numericValueMm: dp.ValueMm));
        }

        bool? hasHostWallCutOpening = string.Equals(Spec.SeedKind, FamilyGeometrySpec.SeedWallHostedDoor, StringComparison.Ordinal)
            ? true
            : (bool?)null;

        return new RfaInspectionSnapshot(
            Spec.FamilyName,
            Spec.Category,
            Spec.SeedKind,
            fileSizeBytes,
            Subcategories,
            solids,
            materials,
            parameters,
            TriangleEstimate.CoarseTriangles,
            TriangleEstimate.MediumFineTriangles,
            importedCadCount: 0,
            embeddedImageCount: 0,
            nestedFamilies: Array.Empty<RfaNestedFamilySnapshot>(),
            maxNestingDepth: 0,
            typeParameterValues: null,
            hasHostWallCutOpening: hasHostWallCutOpening);
    }

    /// <summary>Emits a canonical <see cref="ManifestFamilyEntry"/> compatible with <see cref="ExpectedManifest"/>.</summary>
    public ManifestFamilyEntry ToExpectedManifestEntry() =>
        BuildExpectedSnapshot().ToManifestFamilyEntry(Spec);
}

/// <summary>
/// Pure-C# compiler transforming any declarative <see cref="FamilyGeometrySpec"/> into deterministic,
/// unit-converted (mm -> feet) extrusion primitives, material requirements, subcategory masks,
/// shared parameter bindings, and dimensional parameters.
/// </summary>
public static class FamilyGeometryPlanBuilder
{
    public const double MillimetersPerFoot = 304.8;
    public const double MillimetersToFeet = 1.0 / MillimetersPerFoot;

    /// <summary>
    /// Computes the placement origin offset in millimetres used when inserting compiled geometry into a Revit
    /// seed template. Wall-hosted door seeds (<c>Metric Door.rft</c>) center their host wall opening cut at
    /// <c>X = [-Width/2, +Width/2]</c>, <c>Y = [-Depth/2, +Depth/2]</c>, so centering aligns the procedural door
    /// frames, leaves, and Coarse <c>K_Footprint</c> symmetrically on the wall opening across flips and mirrors.
    /// Procedural families stay anchored at <c>(0, 0, 0)</c>.
    /// </summary>
    public static PlanPoint3D ComputeSeedPlacementOffsetMm(FamilyGeometrySpec familySpec)
    {
        if (familySpec is null)
        {
            throw new ArgumentNullException(nameof(familySpec));
        }

        if (string.Equals(familySpec.SeedKind, FamilyGeometrySpec.SeedWallHostedDoor, StringComparison.Ordinal))
        {
            return new PlanPoint3D(
                -familySpec.DefaultDimensionsMm.WidthMm * 0.5,
                -familySpec.DefaultDimensionsMm.DepthMm * 0.5,
                0.0);
        }

        return new PlanPoint3D(0.0, 0.0, 0.0);
    }

    /// <summary>Compiles a single <see cref="FamilyGeometrySpec"/> into a deterministic <see cref="FamilyGeometryPlan"/>.</summary>
    public static FamilyGeometryPlan Build(
        FamilyGeometrySpec familySpec,
        StarterKitSpec kitSpec,
        StarterKitReferenceStandards? standards = null)
    {
        if (familySpec is null)
        {
            throw new ArgumentNullException(nameof(familySpec));
        }

        if (kitSpec is null)
        {
            throw new ArgumentNullException(nameof(kitSpec));
        }

        StarterKitReferenceStandards refStandards = standards ?? StarterKitReferenceStandards.LoadEmbedded();
        FamilyTriangleEstimate estimate = TriangleEstimator.EstimateFamily(familySpec);

        List<CompiledExtrusionPrimitive> coarsePrimitives = new();
        if (familySpec.Coarse != null)
        {
            CompileOps(
                familySpec.Coarse.Ops,
                isCoarse: true,
                offsetXMm: 0.0,
                offsetYMm: 0.0,
                offsetZMm: 0.0,
                namePrefix: string.Empty,
                depth: 0,
                coarsePrimitives);
        }

        List<CompiledExtrusionPrimitive> mediumFinePrimitives = new();
        CompileOps(
            familySpec.MediumFine.Ops,
            isCoarse: false,
            offsetXMm: 0.0,
            offsetYMm: 0.0,
            offsetZMm: 0.0,
            namePrefix: string.Empty,
            depth: 0,
            mediumFinePrimitives);

        // Deterministic Revit-safe ordering:
        //   1. Each target solid immediately followed by the void(s) targeting it
        //   2. Any untargeted voids
        //   3. All remaining non-target solids (e.g., glazing panes, frames, trims)
        // Coarse K_Footprint solids are emitted in AllPrimitives AFTER all Medium/Fine primitives.
        List<CompiledExtrusionPrimitive> orderedCoarse = OrderPrimitivesForSafeVoidCuts(coarsePrimitives);
        List<CompiledExtrusionPrimitive> orderedMediumFine = OrderPrimitivesForSafeVoidCuts(mediumFinePrimitives);

        Dictionary<string, Material3DSpec> materialsByName = kitSpec.Materials3D.Materials
            .ToDictionary(m => m.Name, m => m, StringComparer.Ordinal);

        HashSet<string> usedMaterialNames = new(StringComparer.Ordinal);
        if (familySpec.Coarse != null && !string.IsNullOrWhiteSpace(familySpec.Coarse.MaterialRef))
        {
            usedMaterialNames.Add(familySpec.Coarse.MaterialRef);
        }

        foreach (CompiledExtrusionPrimitive p in orderedCoarse.Concat(orderedMediumFine))
        {
            if (p.IsSolid && !string.IsNullOrWhiteSpace(p.MaterialRef))
            {
                usedMaterialNames.Add(p.MaterialRef);
            }
        }

        List<Material3DSpec> requiredMaterials = new();
        foreach (string matName in usedMaterialNames.OrderBy(n => n, StringComparer.Ordinal))
        {
            if (!materialsByName.TryGetValue(matName, out Material3DSpec? matSpec) || matSpec == null)
            {
                throw new InvalidOperationException(
                    $"Family '{familySpec.Id}' references unknown 3D material '{matName}' not present in materials-3d.json.");
            }

            requiredMaterials.Add(matSpec);
        }

        List<CompiledSharedParameter> sharedParams = new();
        foreach (FamilySharedParameterSpec paramSpec in familySpec.SharedParameters)
        {
            if (!refStandards.ParametersByName.TryGetValue(paramSpec.Name, out GisParameterDefinition? def) || def == null)
            {
                throw new InvalidOperationException(
                    $"Family '{familySpec.Id}' references unknown Kanizu shared parameter '{paramSpec.Name}'.");
            }

            sharedParams.Add(new CompiledSharedParameter(
                def.Name,
                def.Guid,
                def.DataType,
                def.Binding,
                paramSpec.Required,
                paramSpec.DefaultValue));
        }

        IReadOnlyList<CompiledDimensionParameter> dimensionParams = BuildDimensionParameters(familySpec);

        IReadOnlyList<string> subcategories = Array.AsReadOnly(
            familySpec.Subcategories
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray());

        return new FamilyGeometryPlan(
            familySpec,
            subcategories,
            Array.AsReadOnly(requiredMaterials.ToArray()),
            Array.AsReadOnly(orderedCoarse.ToArray()),
            Array.AsReadOnly(orderedMediumFine.ToArray()),
            Array.AsReadOnly(sharedParams.ToArray()),
            dimensionParams,
            estimate);
    }

    private static IReadOnlyList<CompiledDimensionParameter> BuildDimensionParameters(FamilyGeometrySpec familySpec)
    {
        List<CompiledDimensionParameter> dims = new()
        {
            new CompiledDimensionParameter("Width", familySpec.DefaultDimensionsMm.WidthMm),
        };

        bool usesLengthName =
            string.Equals(familySpec.Id, "KNZ_CIRC_Escalator", StringComparison.Ordinal) ||
            string.Equals(familySpec.Id, "KNZ_CIRC_MovingWalkway", StringComparison.Ordinal);

        if (usesLengthName)
        {
            dims.Add(new CompiledDimensionParameter("Length", familySpec.DefaultDimensionsMm.DepthMm));
        }
        else
        {
            dims.Add(new CompiledDimensionParameter("Depth", familySpec.DefaultDimensionsMm.DepthMm));
        }

        dims.Add(new CompiledDimensionParameter("Height", familySpec.DefaultDimensionsMm.HeightMm));

        if (string.Equals(familySpec.Id, "KNZ_CIRC_Escalator", StringComparison.Ordinal))
        {
            double riseMm = FindPrimaryRiseMm(familySpec.MediumFine.Ops) ?? familySpec.DefaultDimensionsMm.HeightMm;
            dims.Add(new CompiledDimensionParameter("Rise", riseMm));
        }
        else if (string.Equals(familySpec.Id, "KNZ_COL_Round", StringComparison.Ordinal))
        {
            dims.Add(new CompiledDimensionParameter("Diameter", familySpec.DefaultDimensionsMm.WidthMm));
        }

        return Array.AsReadOnly(dims.ToArray());
    }

    private static double? FindPrimaryRiseMm(IReadOnlyList<GeometryOpSpec> ops)
    {
        foreach (GeometryOpSpec op in ops)
        {
            if (string.Equals(op.Op, GeometryOpSpec.OpSlopedPrism, StringComparison.Ordinal) &&
                op.RiseMm.HasValue &&
                op.RiseMm.Value > 0.0)
            {
                return op.RiseMm.Value;
            }
        }

        return null;
    }

    private static List<CompiledExtrusionPrimitive> OrderPrimitivesForSafeVoidCuts(List<CompiledExtrusionPrimitive> primitives)
    {
        Dictionary<string, List<CompiledExtrusionPrimitive>> voidsByTarget = new(StringComparer.Ordinal);
        List<CompiledExtrusionPrimitive> untargetedVoids = new();
        List<CompiledExtrusionPrimitive> allSolids = new();

        foreach (CompiledExtrusionPrimitive primitive in primitives)
        {
            if (primitive.IsSolid)
            {
                allSolids.Add(primitive);
            }
            else if (!string.IsNullOrWhiteSpace(primitive.TargetOpName))
            {
                string key = primitive.TargetOpName!;
                if (!voidsByTarget.TryGetValue(key, out List<CompiledExtrusionPrimitive>? list))
                {
                    list = new List<CompiledExtrusionPrimitive>();
                    voidsByTarget[key] = list;
                }

                list.Add(primitive);
            }
            else
            {
                untargetedVoids.Add(primitive);
            }
        }

        List<CompiledExtrusionPrimitive> ordered = new(primitives.Count);
        List<CompiledExtrusionPrimitive> nonTargetSolids = new();
        HashSet<string> emittedTargets = new(StringComparer.Ordinal);

        // Phase 1: Target solids immediately paired with their targeting voids so voids only cut their
        // intended host solid before unrelated overlapping solids (e.g. K_Glass panes) exist.
        foreach (CompiledExtrusionPrimitive solid in allSolids)
        {
            if (!string.IsNullOrWhiteSpace(solid.SourceOpName) &&
                voidsByTarget.TryGetValue(solid.SourceOpName, out List<CompiledExtrusionPrimitive>? targetVoids))
            {
                ordered.Add(solid);
                if (emittedTargets.Add(solid.SourceOpName))
                {
                    ordered.AddRange(targetVoids);
                }
            }
            else
            {
                nonTargetSolids.Add(solid);
            }
        }

        // Phase 2: Any voids whose TargetOpName didn't match a solid in this band, plus untargeted voids.
        foreach (KeyValuePair<string, List<CompiledExtrusionPrimitive>> kv in voidsByTarget)
        {
            if (!emittedTargets.Contains(kv.Key))
            {
                ordered.AddRange(kv.Value);
            }
        }

        ordered.AddRange(untargetedVoids);

        // Phase 3: Non-target solids (glazing panes, jambs, accents, etc.) created after all void cuts.
        ordered.AddRange(nonTargetSolids);

        return ordered;
    }

    private static void CompileOps(
        IReadOnlyList<GeometryOpSpec> ops,
        bool isCoarse,
        double offsetXMm,
        double offsetYMm,
        double offsetZMm,
        string namePrefix,
        int depth,
        List<CompiledExtrusionPrimitive> output)
    {
        if (depth > TriangleEstimator.MaxNestingDepth)
        {
            throw new InvalidOperationException($"Geometry op nesting depth exceeded {TriangleEstimator.MaxNestingDepth}.");
        }

        foreach (GeometryOpSpec op in ops)
        {
            CompileSingleOp(op, isCoarse, offsetXMm, offsetYMm, offsetZMm, namePrefix, depth, output);
        }
    }

    private static void CompileSingleOp(
        GeometryOpSpec op,
        bool isCoarse,
        double offsetXMm,
        double offsetYMm,
        double offsetZMm,
        string namePrefix,
        int depth,
        List<CompiledExtrusionPrimitive> output)
    {
        string scopedName = string.IsNullOrEmpty(namePrefix) ? op.Name : $"{namePrefix}_{op.Name}";

        switch (op.Op)
        {
            case GeometryOpSpec.OpArray:
            {
                int count = op.Count ?? throw new InvalidOperationException($"Array op '{op.Name}' is missing count.");
                if (count < 1)
                {
                    throw new InvalidOperationException($"Array op '{op.Name}' count must be >= 1.");
                }

                IReadOnlyList<double> step = RequireVector3(op.StepMm, "stepMm", op.Name);
                List<GeometryOpSpec> children = TriangleEstimator.CollectArrayChildren(op);
                if (children.Count == 0)
                {
                    throw new InvalidOperationException($"Array op '{op.Name}' has no child or children.");
                }

                for (int i = 0; i < count; i++)
                {
                    string itemPrefix = $"{scopedName}_{i:D2}";
                    CompileOps(
                        children,
                        isCoarse,
                        offsetXMm + (i * step[0]),
                        offsetYMm + (i * step[1]),
                        offsetZMm + (i * step[2]),
                        itemPrefix,
                        depth + 1,
                        output);
                }

                break;
            }

            case GeometryOpSpec.OpBox:
                output.Add(CompileBox(
                    scopedName,
                    op.Name,
                    isSolid: true,
                    throughCut: false,
                    targetOpName: null,
                    op.Subcategory ?? string.Empty,
                    op.MaterialRef ?? string.Empty,
                    isCoarse,
                    RequireVector3(op.OriginMm, "originMm", op.Name),
                    RequirePositiveVector3(op.SizeMm, "sizeMm", op.Name),
                    offsetXMm,
                    offsetYMm,
                    offsetZMm,
                    TriangleEstimator.BoxTriangles));
                break;

            case GeometryOpSpec.OpNPrism:
            {
                int sides = op.Sides ?? throw new InvalidOperationException($"nprism op '{op.Name}' is missing sides.");
                double radius = RequirePositiveScalar(op.RadiusMm, "radiusMm", op.Name);
                double height = RequirePositiveScalar(op.HeightMm, "heightMm", op.Name);
                output.Add(CompileNPrism(
                    scopedName,
                    op.Name,
                    isSolid: true,
                    throughCut: false,
                    targetOpName: null,
                    op.Subcategory ?? string.Empty,
                    op.MaterialRef ?? string.Empty,
                    isCoarse,
                    RequireVector3(op.CenterMm, "centerMm", op.Name),
                    radius,
                    height,
                    sides,
                    offsetXMm,
                    offsetYMm,
                    offsetZMm,
                    TriangleEstimator.PrismTriangles(sides)));
                break;
            }

            case GeometryOpSpec.OpSlopedPrism:
                output.Add(CompileSlopedPrism(
                    scopedName,
                    op,
                    isCoarse,
                    offsetXMm,
                    offsetYMm,
                    offsetZMm));
                break;

            case GeometryOpSpec.OpVoid:
            {
                string shape = string.IsNullOrWhiteSpace(op.Shape) ? GeometryOpSpec.OpBox : op.Shape!.Trim();
                bool throughCut = op.ThroughCut ?? true;
                if (string.Equals(shape, GeometryOpSpec.OpBox, StringComparison.Ordinal))
                {
                    output.Add(CompileBox(
                        scopedName,
                        op.Name,
                        isSolid: false,
                        throughCut: throughCut,
                        targetOpName: op.TargetOp,
                        subcategory: string.Empty,
                        materialRef: string.Empty,
                        isCoarse,
                        RequireVector3(op.OriginMm, "originMm", op.Name),
                        RequirePositiveVector3(op.SizeMm, "sizeMm", op.Name),
                        offsetXMm,
                        offsetYMm,
                        offsetZMm,
                        TriangleEstimator.VoidCutTriangles(4, throughCut)));
                }
                else if (string.Equals(shape, GeometryOpSpec.OpNPrism, StringComparison.Ordinal))
                {
                    int sides = op.Sides ?? throw new InvalidOperationException($"nprism void op '{op.Name}' is missing sides.");
                    double radius = RequirePositiveScalar(op.RadiusMm, "radiusMm", op.Name);
                    double height = RequirePositiveScalar(op.HeightMm, "heightMm", op.Name);
                    output.Add(CompileNPrism(
                        scopedName,
                        op.Name,
                        isSolid: false,
                        throughCut: throughCut,
                        targetOpName: op.TargetOp,
                        subcategory: string.Empty,
                        materialRef: string.Empty,
                        isCoarse,
                        RequireVector3(op.CenterMm, "centerMm", op.Name),
                        radius,
                        height,
                        sides,
                        offsetXMm,
                        offsetYMm,
                        offsetZMm,
                        TriangleEstimator.VoidCutTriangles(sides, throughCut)));
                }
                else
                {
                    throw new InvalidOperationException($"Void op '{op.Name}' has unsupported shape '{shape}'.");
                }

                break;
            }

            default:
                throw new InvalidOperationException($"Unsupported geometry op '{op.Op}' in op '{op.Name}'.");
        }
    }

    private static CompiledExtrusionPrimitive CompileBox(
        string scopedName,
        string sourceOpName,
        bool isSolid,
        bool throughCut,
        string? targetOpName,
        string subcategory,
        string materialRef,
        bool isCoarse,
        IReadOnlyList<double> originMm,
        IReadOnlyList<double> sizeMm,
        double offsetXMm,
        double offsetYMm,
        double offsetZMm,
        int estimatedTriangles)
    {
        double x0 = originMm[0] + offsetXMm;
        double y0 = originMm[1] + offsetYMm;
        double z0 = originMm[2] + offsetZMm;
        double x1 = x0 + sizeMm[0];
        double y1 = y0 + sizeMm[1];
        double z1 = z0 + sizeMm[2];

        PlanPoint3D[] vertices =
        {
            new(x0, y0, z0),
            new(x1, y0, z0),
            new(x1, y1, z0),
            new(x0, y1, z0),
        };

        double xyArea = sizeMm[0] * sizeMm[1];
        RfaPlanarFaceSnapshot[] faces = isSolid
            ? new[]
            {
                new RfaPlanarFaceSnapshot(0.0, 0.0, -1.0, z0, z0, z0, xyArea),
                new RfaPlanarFaceSnapshot(0.0, 0.0, 1.0, z1, z1, z1, xyArea),
            }
            : Array.Empty<RfaPlanarFaceSnapshot>();

        return new CompiledExtrusionPrimitive(
            scopedName,
            sourceOpName,
            isSolid ? GeometryOpSpec.OpBox : GeometryOpSpec.OpVoid,
            isSolid,
            throughCut,
            targetOpName,
            subcategory,
            materialRef,
            visibleInCoarse: isCoarse,
            visibleInMedium: !isCoarse,
            visibleInFine: !isCoarse,
            CompiledSketchPlane.HorizontalXY(z0),
            Array.AsReadOnly(vertices),
            extrusionDepthMm: sizeMm[2],
            minXMm: x0,
            maxXMm: x1,
            minYMm: y0,
            maxYMm: y1,
            minZMm: z0,
            maxZMm: z1,
            estimatedTriangles,
            Array.AsReadOnly(faces));
    }

    private static CompiledExtrusionPrimitive CompileNPrism(
        string scopedName,
        string sourceOpName,
        bool isSolid,
        bool throughCut,
        string? targetOpName,
        string subcategory,
        string materialRef,
        bool isCoarse,
        IReadOnlyList<double> centerMm,
        double radiusMm,
        double heightMm,
        int sides,
        double offsetXMm,
        double offsetYMm,
        double offsetZMm,
        int estimatedTriangles)
    {
        if (sides < 3)
        {
            throw new InvalidOperationException($"nprism '{sourceOpName}' requires sides >= 3.");
        }

        double cx = centerMm[0] + offsetXMm;
        double cy = centerMm[1] + offsetYMm;
        double z0 = centerMm[2] + offsetZMm;
        double z1 = z0 + heightMm;

        PlanPoint3D[] vertices = new PlanPoint3D[sides];
        double minX = double.PositiveInfinity;
        double maxX = double.NegativeInfinity;
        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;

        for (int i = 0; i < sides; i++)
        {
            double angle = (2.0 * Math.PI * i) / sides;
            double x = cx + (radiusMm * Math.Cos(angle));
            double y = cy + (radiusMm * Math.Sin(angle));
            vertices[i] = new PlanPoint3D(x, y, z0);
            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        double capArea = 0.5 * sides * radiusMm * radiusMm * Math.Sin((2.0 * Math.PI) / sides);
        RfaPlanarFaceSnapshot[] faces = isSolid
            ? new[]
            {
                new RfaPlanarFaceSnapshot(0.0, 0.0, -1.0, z0, z0, z0, capArea),
                new RfaPlanarFaceSnapshot(0.0, 0.0, 1.0, z1, z1, z1, capArea),
            }
            : Array.Empty<RfaPlanarFaceSnapshot>();

        return new CompiledExtrusionPrimitive(
            scopedName,
            sourceOpName,
            isSolid ? GeometryOpSpec.OpNPrism : GeometryOpSpec.OpVoid,
            isSolid,
            throughCut,
            targetOpName,
            subcategory,
            materialRef,
            visibleInCoarse: isCoarse,
            visibleInMedium: !isCoarse,
            visibleInFine: !isCoarse,
            CompiledSketchPlane.HorizontalXY(z0),
            Array.AsReadOnly(vertices),
            extrusionDepthMm: heightMm,
            minXMm: minX,
            maxXMm: maxX,
            minYMm: minY,
            maxYMm: maxY,
            minZMm: z0,
            maxZMm: z1,
            estimatedTriangles,
            Array.AsReadOnly(faces));
    }

    private static CompiledExtrusionPrimitive CompileSlopedPrism(
        string scopedName,
        GeometryOpSpec op,
        bool isCoarse,
        double offsetXMm,
        double offsetYMm,
        double offsetZMm)
    {
        IReadOnlyList<double> origin = RequireVector3(op.OriginMm, "originMm", op.Name);
        double widthMm = RequirePositiveScalar(op.WidthMm, "widthMm", op.Name);
        double runMm = RequirePositiveScalar(op.RunMm, "runMm", op.Name);
        double thicknessMm = RequirePositiveScalar(op.ThicknessMm, "thicknessMm", op.Name);
        double riseMm = op.RiseMm ?? 0.0;
        if (double.IsNaN(riseMm) || double.IsInfinity(riseMm))
        {
            throw new InvalidOperationException($"slopedPrism op '{op.Name}' has non-finite riseMm.");
        }

        int sides = op.Sides ?? 4;
        if (sides < 3)
        {
            throw new InvalidOperationException($"slopedPrism op '{op.Name}' requires sides >= 3.");
        }

        double x0 = origin[0] + offsetXMm;
        double y0 = origin[1] + offsetYMm;
        double z0 = origin[2] + offsetZMm;

        // Sketched on the vertical YZ plane at X = x0 and extruded along +X by widthMm.
        // Parallelogram vertices in world coordinates:
        //   (x0, y0, z0) -> (x0, y0 + run, z0 + rise) -> (x0, y0 + run, z0 + rise + thickness) -> (x0, y0, z0 + thickness)
        PlanPoint3D[] vertices =
        {
            new(x0, y0, z0),
            new(x0, y0 + runMm, z0 + riseMm),
            new(x0, y0 + runMm, z0 + riseMm + thicknessMm),
            new(x0, y0, z0 + thicknessMm),
        };

        double minZ = Math.Min(z0, z0 + riseMm);
        double maxZ = Math.Max(z0, z0 + riseMm) + thicknessMm;

        double slopeLen = Math.Sqrt((runMm * runMm) + (riseMm * riseMm));
        double downNormalY = slopeLen > 1e-9 ? riseMm / slopeLen : 0.0;
        double downNormalZ = slopeLen > 1e-9 ? -runMm / slopeLen : -1.0;

        RfaPlanarFaceSnapshot[] faces =
        {
            new(0.0, downNormalY, downNormalZ, z0, minZ, Math.Max(z0, z0 + riseMm), widthMm * slopeLen),
            new(0.0, -downNormalY, -downNormalZ, z0 + thicknessMm, minZ + thicknessMm, maxZ, widthMm * slopeLen),
        };

        return new CompiledExtrusionPrimitive(
            scopedName,
            op.Name,
            GeometryOpSpec.OpSlopedPrism,
            isSolid: true,
            throughCut: false,
            targetOpName: null,
            op.Subcategory ?? string.Empty,
            op.MaterialRef ?? string.Empty,
            visibleInCoarse: isCoarse,
            visibleInMedium: !isCoarse,
            visibleInFine: !isCoarse,
            CompiledSketchPlane.VerticalYZ(x0),
            Array.AsReadOnly(vertices),
            extrusionDepthMm: widthMm,
            minXMm: x0,
            maxXMm: x0 + widthMm,
            minYMm: y0,
            maxYMm: y0 + runMm,
            minZMm: minZ,
            maxZMm: maxZ,
            TriangleEstimator.PrismTriangles(sides),
            Array.AsReadOnly(faces));
    }

    private static IReadOnlyList<double> RequireVector3(IReadOnlyList<double>? vector, string fieldName, string opName)
    {
        if (vector == null || vector.Count != 3 ||
            !IsFinite(vector[0]) || !IsFinite(vector[1]) || !IsFinite(vector[2]))
        {
            throw new InvalidOperationException($"Geometry op '{opName}' requires a finite 3-element {fieldName} vector.");
        }

        return vector;
    }

    private static IReadOnlyList<double> RequirePositiveVector3(IReadOnlyList<double>? vector, string fieldName, string opName)
    {
        IReadOnlyList<double> valid = RequireVector3(vector, fieldName, opName);
        if (valid[0] <= 0.0 || valid[1] <= 0.0 || valid[2] <= 0.0)
        {
            throw new InvalidOperationException($"Geometry op '{opName}' requires all {fieldName} components to be > 0.");
        }

        return valid;
    }

    private static double RequirePositiveScalar(double? value, string fieldName, string opName)
    {
        if (!value.HasValue || !IsFinite(value.Value) || value.Value <= 0.0)
        {
            throw new InvalidOperationException($"Geometry op '{opName}' requires finite positive {fieldName}.");
        }

        return value.Value;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
