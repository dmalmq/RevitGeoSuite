using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>
/// Pure-C# guard ensuring the Forma Companion builder NEVER mutates an authoring Revit document.
/// </summary>
public static class ShowcaseCompanionDocumentGuard
{
    /// <summary>
    /// Validates whether a target document with <paramref name="existingModelRole"/> may be populated by the
    /// Forma Companion builder.
    /// - Freshly created companion documents (<paramref name="isFreshNewDocument"/> = <c>true</c>) must not carry
    ///   <c>gis_model_role = "authoring"</c>.
    /// - Existing documents (<paramref name="isFreshNewDocument"/> = <c>false</c>) MUST already carry
    ///   <c>gis_model_role = "showcase"</c> (blank/unset legacy authoring models and explicit authoring models are rejected).
    /// </summary>
    public static void ValidateTargetRole(string? existingModelRole, bool isFreshNewDocument, string? documentTitle = null)
    {
        string label = string.IsNullOrWhiteSpace(documentTitle) ? "target document" : $"document '{documentTitle!.Trim()}'";
        string? normalizedRole = ShowcaseExportGuard.NormalizeModelRole(existingModelRole);

        if (isFreshNewDocument)
        {
            if (string.Equals(normalizedRole, ShowcaseExportGuard.AuthoringModelRole, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Refused to build Forma Companion content in {label}: its template is stamped with gis_model_role = 'authoring'. " +
                    "Use a blank/metric template or a dedicated showcase template.");
            }

            return;
        }

        if (!ShowcaseExportGuard.IsShowcaseModelRole(existingModelRole))
        {
            string roleDisplay = string.IsNullOrWhiteSpace(existingModelRole) ? "(unset / authoring)" : $"'{existingModelRole!.Trim()}'";
            throw new InvalidOperationException(
                $"Refused to mutate {label} with Forma Companion showcase content: gis_model_role is {roleDisplay}, not 'showcase'. " +
                "Companion .rvt builds must never mutate an authoring model; create a new companion document instead.");
        }
    }
}

/// <summary>Geometry extrusion and band parameters for <see cref="ShowcaseGeometryPlanner"/>.</summary>
public sealed class ShowcaseGeometryPlanOptions
{
    public static ShowcaseGeometryPlanOptions Default { get; } = new();

    /// <summary>Extrusion height (metres) for <c>unit</c> (<c>KNZ_FRM_Unit</c>) solids.</summary>
    public double UnitExtrusionHeightMeters { get; set; } = 0.15d;

    /// <summary>Extrusion height (metres) for <c>opening</c> (<c>KNZ_FRM_Opening</c>) prisms.</summary>
    public double OpeningExtrusionHeightMeters { get; set; } = 2.20d;

    /// <summary>Vertical clearance (metres) lifting opening prisms slightly above unit slabs to prevent Z-fighting.</summary>
    public double OpeningBaseOffsetMeters { get; set; } = 0.004d;

    /// <summary>Planar thickness (metres) used when an <c>opening</c> feature is represented as a 2D LineString.</summary>
    public double OpeningLineThicknessMeters { get; set; } = 0.20d;

    /// <summary>Extrusion height (metres) for <c>fixture</c> (<c>KNZ_FRM_Fixture</c>) prisms.</summary>
    public double FixtureExtrusionHeightMeters { get; set; } = 1.10d;

    /// <summary>Vertical clearance (metres) lifting fixture prisms slightly above unit slabs to prevent Z-fighting.</summary>
    public double FixtureBaseOffsetMeters { get; set; } = 0.004d;

    /// <summary>Planar thickness (metres) used when a <c>fixture</c> feature is represented as a 2D LineString.</summary>
    public double FixtureLineThicknessMeters { get; set; } = 0.15d;

    /// <summary>Square footprint side (metres) used when a <c>fixture</c> feature is represented as a 2D Point.</summary>
    public double FixturePointSizeMeters { get; set; } = 0.50d;

    /// <summary>Perimeter ribbon width (metres) for non-occluding <c>level</c> (<c>KNZ_FRM_LevelBand</c>) outlines.</summary>
    public double LevelBandWidthMeters { get; set; } = 0.25d;

    /// <summary>Vertical height (metres) for thin non-occluding <c>level</c> (<c>KNZ_FRM_LevelBand</c>) outlines.</summary>
    public double LevelBandHeightMeters { get; set; } = 0.08d;

    /// <summary>Fallback floor-to-floor spacing (metres) when a level carries no <c>tpM</c> elevation.</summary>
    public double DefaultFloorHeightMeters { get; set; } = 4.5d;

    /// <summary>Minimum edge length (feet) kept in planned CurveLoops to stay safely above Revit's ShortCurveTolerance (~0.0026 ft).</summary>
    public double MinSegmentLengthFeet { get; set; } = 0.003d;
}

/// <summary>
/// One planar extrusion solid in Revit internal coordinates (feet):
/// - <see cref="OuterRingFeet"/> is strictly CCW (positive signed area)
/// - Every loop in <see cref="InteriorRingsFeet"/> is strictly CW (negative signed area)
/// Rings are stored as open vertex lists <c>[v0, v1, ..., v_{n-1}]</c>; edges are <c>v_i -&gt; v_{(i+1)%n}</c>.
/// </summary>
public sealed class ShowcaseExtrusionSolid
{
    public ShowcaseExtrusionSolid(
        IReadOnlyList<Point2D> outerRingFeet,
        IReadOnlyList<IReadOnlyList<Point2D>> interiorRingsFeet,
        double baseElevationFeet,
        double heightFeet)
    {
        OuterRingFeet = outerRingFeet ?? throw new ArgumentNullException(nameof(outerRingFeet));
        InteriorRingsFeet = interiorRingsFeet ?? throw new ArgumentNullException(nameof(interiorRingsFeet));
        BaseElevationFeet = baseElevationFeet;
        HeightFeet = heightFeet;
    }

    public IReadOnlyList<Point2D> OuterRingFeet { get; }

    public IReadOnlyList<IReadOnlyList<Point2D>> InteriorRingsFeet { get; }

    public double BaseElevationFeet { get; }

    public double HeightFeet { get; }
}

/// <summary>
/// Complete Revit-API-free plan for one Forma Companion <c>DirectShape</c> element.
/// Every planned element carries all 4 F2 export-loop isolation markers.
/// </summary>
public sealed class ShowcaseElementPlan
{
    public ShowcaseElementPlan(
        ShowcaseLayerKind layerKind,
        string sourceLayerName,
        string featureKey,
        string applicationId,
        string applicationDataId,
        string elementName,
        string preferredCategoryName,
        string? levelId,
        string? floorKey,
        bool isNonOccludingOutlineBand,
        IReadOnlyList<ShowcaseExtrusionSolid> solids,
        IReadOnlyList<ShowcaseParameterAssignment> parameterAssignments,
        string? overflowAttributesJson)
    {
        LayerKind = layerKind;
        SourceLayerName = sourceLayerName ?? throw new ArgumentNullException(nameof(sourceLayerName));
        FeatureKey = featureKey ?? throw new ArgumentNullException(nameof(featureKey));
        ApplicationId = applicationId ?? throw new ArgumentNullException(nameof(applicationId));
        ApplicationDataId = applicationDataId ?? throw new ArgumentNullException(nameof(applicationDataId));
        ElementName = elementName ?? throw new ArgumentNullException(nameof(elementName));
        PreferredCategoryName = preferredCategoryName ?? throw new ArgumentNullException(nameof(preferredCategoryName));
        LevelId = levelId;
        FloorKey = floorKey;
        IsNonOccludingOutlineBand = isNonOccludingOutlineBand;
        Solids = solids ?? throw new ArgumentNullException(nameof(solids));
        ParameterAssignments = parameterAssignments ?? throw new ArgumentNullException(nameof(parameterAssignments));
        OverflowAttributesJson = overflowAttributesJson;
    }

    public ShowcaseLayerKind LayerKind { get; }

    public string SourceLayerName { get; }

    public string FeatureKey { get; }

    public string ApplicationId { get; }

    /// <summary>Stable incremental-update key formatted strictly as <c>"&lt;layer&gt;:&lt;key&gt;"</c>.</summary>
    public string ApplicationDataId { get; }

    /// <summary>Element name prefixed with <c>KNZ_FRM_</c> (<c>KNZ_FRM_Unit_*</c>, <c>KNZ_FRM_Opening_*</c>, <c>KNZ_FRM_Fixture_*</c>, <c>KNZ_FRM_LevelBand_*</c>).</summary>
    public string ElementName { get; }

    /// <summary>Preferred Revit <c>BuiltInCategory</c> name (builder falls back to <c>OST_GenericModel</c> if invalid).</summary>
    public string PreferredCategoryName { get; }

    public string? LevelId { get; }

    public string? FloorKey { get; }

    /// <summary>True for <c>KNZ_FRM_LevelBand</c> non-occluding perimeter bands.</summary>
    public bool IsNonOccludingOutlineBand { get; }

    public IReadOnlyList<ShowcaseExtrusionSolid> Solids { get; }

    public IReadOnlyList<ShowcaseParameterAssignment> ParameterAssignments { get; }

    public string? OverflowAttributesJson { get; }

    /// <summary>Builds a <see cref="ShowcaseElementDescriptor"/> for verifying F2's <see cref="ShowcaseExportGuard"/>.</summary>
    public ShowcaseElementDescriptor ToGuardDescriptor(string? sourceDocumentModelRole = ShowcaseExportGuard.ShowcaseModelRole)
    {
        List<string> parameterNames = ParameterAssignments
            .Where(a => !a.IsNull)
            .Select(a => a.Parameter.Name)
            .ToList();
        if (!string.IsNullOrEmpty(OverflowAttributesJson))
        {
            parameterNames.Add(ShowcaseParameterRegistry.OverflowParameterName);
        }

        return new ShowcaseElementDescriptor(
            applicationId: ApplicationId,
            familyName: null,
            typeName: ElementName,
            parameterNames: parameterNames,
            sourceDocumentModelRole: sourceDocumentModelRole,
            categoryName: PreferredCategoryName);
    }
}

/// <summary>Output of <see cref="ShowcaseGeometryPlanner.Plan"/>.</summary>
public sealed class ShowcaseGeometryPlanResult
{
    public ShowcaseGeometryPlanResult(
        ShowcaseParameterRegistry parameterRegistry,
        IReadOnlyList<ShowcaseElementPlan> elements,
        IReadOnlyList<string> warnings)
    {
        ParameterRegistry = parameterRegistry ?? throw new ArgumentNullException(nameof(parameterRegistry));
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
    }

    public ShowcaseParameterRegistry ParameterRegistry { get; }

    public IReadOnlyList<ShowcaseElementPlan> Elements { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>
/// Pure-C# extrusion planner turning validated <c>kanizu.gis-showcase</c> v1 snapshots into deterministic
/// Revit <c>DirectShape</c> plans for <c>unit</c>, <c>opening</c>, <c>fixture</c>, and non-occluding
/// <c>level</c>-outline perimeter bands.
/// </summary>
public static class ShowcaseGeometryPlanner
{
    public const string UnitNamePrefix = "KNZ_FRM_Unit";
    public const string OpeningNamePrefix = "KNZ_FRM_Opening";
    public const string FixtureNamePrefix = "KNZ_FRM_Fixture";
    public const string LevelBandNamePrefix = "KNZ_FRM_LevelBand";

    private static readonly GeometryFactory NtsFactory = new();

    public static ShowcaseGeometryPlanResult Plan(
        ShowcaseSnapshot snapshot,
        ShowcaseCoordinateTransform transform,
        ShowcaseParameterRegistry? parameterRegistry = null,
        ShowcaseGeometryPlanOptions? options = null)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (transform is null)
        {
            throw new ArgumentNullException(nameof(transform));
        }

        ShowcaseParameterRegistry registry = parameterRegistry ?? ShowcaseParameterRegistry.Build(snapshot);
        ShowcaseGeometryPlanOptions effectiveOptions = options ?? ShowcaseGeometryPlanOptions.Default;

        Dictionary<string, ShowcaseLevel> levelsById = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ShowcaseLevel> levelsByFloorKey = new(StringComparer.OrdinalIgnoreCase);
        List<ShowcaseLevel> knownElevationAnchors = new();
        foreach (ShowcaseLevel level in snapshot.Levels)
        {
            if (!levelsById.ContainsKey(level.LevelId))
            {
                levelsById[level.LevelId] = level;
            }

            if (!levelsByFloorKey.ContainsKey(level.FloorKey))
            {
                levelsByFloorKey[level.FloorKey] = level;
            }

            if (level.TpM.HasValue && !double.IsNaN(level.TpM.Value) && !double.IsInfinity(level.TpM.Value))
            {
                knownElevationAnchors.Add(level);
            }
        }

        // Deterministic tie-breaking order: lowest anchor.Ordinal first, then ordinal-sorted LevelId.
        knownElevationAnchors.Sort((a, b) =>
        {
            int cmp = a.Ordinal.CompareTo(b.Ordinal);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.LevelId, b.LevelId);
        });

        List<ShowcaseElementPlan> elements = new();
        List<string> warnings = new();

        foreach (ShowcaseLayer layer in snapshot.Layers)
        {
            if (!IsF3SupportedLayerKind(layer.Kind))
            {
                continue;
            }

            IReadOnlyDictionary<string, bool> preserveMap = ShowcaseAttributeNormalizer.ComputeColumnPreserveAsTextMap(layer);

            foreach (ShowcaseFeature feature in layer.Features)
            {
                double levelElevationMeters = ResolveLevelElevationMeters(
                    feature,
                    levelsById,
                    levelsByFloorKey,
                    knownElevationAnchors,
                    effectiveOptions);
                IReadOnlyList<ShowcaseExtrusionSolid> solids = BuildFeatureSolids(
                    layer.Kind,
                    feature,
                    levelElevationMeters,
                    transform,
                    effectiveOptions,
                    warnings,
                    layer.Name);

                if (solids.Count == 0)
                {
                    warnings.Add($"Skipped '{layer.Name}:{feature.Key}' because no valid non-degenerate extrusion solid could be planned.");
                    continue;
                }

                (IReadOnlyList<ShowcaseParameterAssignment> assignments, string? overflowJson) =
                    registry.BindFeatureAttributes(layer, feature, preserveMap);

                string applicationDataId = BuildApplicationDataId(layer.Name, feature.Key);
                string elementName = BuildElementName(layer.Kind, feature.Key);
                string preferredCategory = ResolvePreferredCategoryName(layer.Kind);

                elements.Add(new ShowcaseElementPlan(
                    layer.Kind,
                    layer.Name,
                    feature.Key,
                    ShowcaseExportGuard.ShowcaseApplicationId,
                    applicationDataId,
                    elementName,
                    preferredCategory,
                    feature.LevelId,
                    feature.FloorKey,
                    isNonOccludingOutlineBand: layer.Kind == ShowcaseLayerKind.Level,
                    solids,
                    assignments,
                    overflowJson));
            }
        }

        return new ShowcaseGeometryPlanResult(registry, elements, warnings);
    }

    public static bool IsF3SupportedLayerKind(ShowcaseLayerKind kind) =>
        kind == ShowcaseLayerKind.Unit ||
        kind == ShowcaseLayerKind.Opening ||
        kind == ShowcaseLayerKind.Fixture ||
        kind == ShowcaseLayerKind.Level;

    /// <summary>Builds the deterministic F5-ready <c>DirectShape.ApplicationDataId</c> (<c>"&lt;layer&gt;:&lt;key&gt;"</c>).</summary>
    public static string BuildApplicationDataId(string layerName, string featureKey) =>
        $"{layerName}:{featureKey}";

    /// <summary>Builds a deterministic <c>KNZ_FRM_*</c> element name caught by <see cref="ShowcaseExportGuard"/>.</summary>
    public static string BuildElementName(ShowcaseLayerKind kind, string featureKey)
    {
        string prefix = kind switch
        {
            ShowcaseLayerKind.Unit => UnitNamePrefix,
            ShowcaseLayerKind.Opening => OpeningNamePrefix,
            ShowcaseLayerKind.Fixture => FixtureNamePrefix,
            ShowcaseLayerKind.Level => LevelBandNamePrefix,
            _ => ShowcaseExportGuard.ShowcaseFamilyNamePrefix + "Element",
        };

        string safeKey = SanitizeElementKey(featureKey);
        return $"{prefix}_{safeKey}";
    }

    public static string ResolvePreferredCategoryName(ShowcaseLayerKind kind)
    {
        switch (kind)
        {
            case ShowcaseLayerKind.Unit: return "OST_Floors";
            case ShowcaseLayerKind.Opening: return "OST_Doors";
            case ShowcaseLayerKind.Fixture: return "OST_SpecialityEquipment";
            case ShowcaseLayerKind.Level:
            default:
                return "OST_GenericModel";
        }
    }

    private static double ResolveLevelElevationMeters(
        ShowcaseFeature feature,
        Dictionary<string, ShowcaseLevel> levelsById,
        Dictionary<string, ShowcaseLevel> levelsByFloorKey,
        IReadOnlyList<ShowcaseLevel> knownElevationAnchors,
        ShowcaseGeometryPlanOptions options)
    {
        ShowcaseLevel? level = null;
        if (!string.IsNullOrWhiteSpace(feature.LevelId) && levelsById.TryGetValue(feature.LevelId!, out ShowcaseLevel? byId))
        {
            level = byId;
        }
        else if (!string.IsNullOrWhiteSpace(feature.FloorKey) && levelsByFloorKey.TryGetValue(feature.FloorKey!, out ShowcaseLevel? byKey))
        {
            level = byKey;
        }

        if (level != null)
        {
            if (level.TpM.HasValue)
            {
                return level.TpM.Value;
            }

            // Anchor missing elevations to the nearest known-elevation level by |level.Ordinal - anchor.Ordinal|
            // (tie-breaking deterministically by lowest anchor.Ordinal, then ordinal-sorted LevelId).
            if (knownElevationAnchors.Count > 0)
            {
                ShowcaseLevel bestAnchor = knownElevationAnchors[0];
                decimal bestDistance = Math.Abs((decimal)level.Ordinal - bestAnchor.Ordinal);
                for (int i = 1; i < knownElevationAnchors.Count; i++)
                {
                    ShowcaseLevel candidate = knownElevationAnchors[i];
                    decimal distance = Math.Abs((decimal)level.Ordinal - candidate.Ordinal);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        bestAnchor = candidate;
                    }
                }

                return bestAnchor.TpM!.Value + ((level.Ordinal - bestAnchor.Ordinal) * options.DefaultFloorHeightMeters);
            }

            return level.Ordinal * options.DefaultFloorHeightMeters;
        }

        if (feature.Geometry is ShowcasePointGeometry pt && pt.Z.HasValue)
        {
            return pt.Z.Value;
        }

        return 0d;
    }

    private static IReadOnlyList<ShowcaseExtrusionSolid> BuildFeatureSolids(
        ShowcaseLayerKind kind,
        ShowcaseFeature feature,
        double levelElevationMeters,
        ShowcaseCoordinateTransform transform,
        ShowcaseGeometryPlanOptions options,
        List<string> warnings,
        string layerName)
    {
        switch (kind)
        {
            case ShowcaseLayerKind.Unit:
                return BuildPolygonExtrusions(
                    feature.Geometry,
                    baseElevationMeters: levelElevationMeters,
                    heightMeters: options.UnitExtrusionHeightMeters,
                    transform,
                    options,
                    lineThicknessMeters: null,
                    pointSizeMeters: null,
                    warnings,
                    $"{layerName}:{feature.Key}");

            case ShowcaseLayerKind.Opening:
                return BuildPolygonExtrusions(
                    feature.Geometry,
                    baseElevationMeters: levelElevationMeters + options.OpeningBaseOffsetMeters,
                    heightMeters: options.OpeningExtrusionHeightMeters,
                    transform,
                    options,
                    lineThicknessMeters: options.OpeningLineThicknessMeters,
                    pointSizeMeters: null,
                    warnings,
                    $"{layerName}:{feature.Key}");

            case ShowcaseLayerKind.Fixture:
                return BuildPolygonExtrusions(
                    feature.Geometry,
                    baseElevationMeters: levelElevationMeters + options.FixtureBaseOffsetMeters,
                    heightMeters: options.FixtureExtrusionHeightMeters,
                    transform,
                    options,
                    lineThicknessMeters: options.FixtureLineThicknessMeters,
                    pointSizeMeters: options.FixturePointSizeMeters,
                    warnings,
                    $"{layerName}:{feature.Key}");

            case ShowcaseLayerKind.Level:
                return BuildLevelOutlineBandExtrusions(
                    feature.Geometry,
                    baseElevationMeters: levelElevationMeters,
                    heightMeters: options.LevelBandHeightMeters,
                    bandWidthMeters: options.LevelBandWidthMeters,
                    transform,
                    options,
                    warnings,
                    $"{layerName}:{feature.Key}");

            default:
                return Array.Empty<ShowcaseExtrusionSolid>();
        }
    }

    private static IReadOnlyList<ShowcaseExtrusionSolid> BuildPolygonExtrusions(
        ShowcaseGeometry geometry,
        double baseElevationMeters,
        double heightMeters,
        ShowcaseCoordinateTransform transform,
        ShowcaseGeometryPlanOptions options,
        double? lineThicknessMeters,
        double? pointSizeMeters,
        List<string> warnings,
        string label)
    {
        double baseFeet = transform.ToRevitInternalZFeet(baseElevationMeters);
        double heightFeet = ShowcaseCoordinateTransform.MetersToFeet(heightMeters);
        if (heightFeet <= options.MinSegmentLengthFeet)
        {
            warnings.Add($"Extrusion height for '{label}' is below minimum tolerance.");
            return Array.Empty<ShowcaseExtrusionSolid>();
        }

        List<ShowcaseExtrusionSolid> solids = new();

        switch (geometry)
        {
            case ShowcasePolygonGeometry poly:
                TryAppendPolygonSolid(solids, poly, baseFeet, heightFeet, transform, options);
                break;

            case ShowcaseMultiPolygonGeometry multiPoly:
                foreach (ShowcasePolygonGeometry part in multiPoly.Polygons)
                {
                    TryAppendPolygonSolid(solids, part, baseFeet, heightFeet, transform, options);
                }

                break;

            case ShowcaseLineStringGeometry line when lineThicknessMeters.HasValue:
                AppendBufferedLineSolids(solids, line.Coords, lineThicknessMeters.Value, baseFeet, heightFeet, transform, options);
                break;

            case ShowcaseMultiLineStringGeometry multiLine when lineThicknessMeters.HasValue:
                foreach (IReadOnlyList<Point2D> lineCoords in multiLine.Lines)
                {
                    AppendBufferedLineSolids(solids, lineCoords, lineThicknessMeters.Value, baseFeet, heightFeet, transform, options);
                }

                break;

            case ShowcasePointGeometry point when pointSizeMeters.HasValue:
            {
                double half = pointSizeMeters.Value * 0.5d;
                IReadOnlyList<Point2D> square = new[]
                {
                    new Point2D(point.Xy.X - half, point.Xy.Y - half),
                    new Point2D(point.Xy.X + half, point.Xy.Y - half),
                    new Point2D(point.Xy.X + half, point.Xy.Y + half),
                    new Point2D(point.Xy.X - half, point.Xy.Y + half),
                    new Point2D(point.Xy.X - half, point.Xy.Y - half),
                };
                TryAppendPolygonSolid(solids, new ShowcasePolygonGeometry(new[] { square }), baseFeet, heightFeet, transform, options);
                break;
            }
        }

        return solids;
    }

    /// <summary>
    /// Builds thin, non-occluding perimeter bands around <c>level</c> outlines using
    /// <c>outerPolygon.Buffer(bandWidth).Difference(outerPolygon)</c> so the interior floor area always remains
    /// open and never occludes lower floors in Autodesk Forma.
    /// </summary>
    private static IReadOnlyList<ShowcaseExtrusionSolid> BuildLevelOutlineBandExtrusions(
        ShowcaseGeometry geometry,
        double baseElevationMeters,
        double heightMeters,
        double bandWidthMeters,
        ShowcaseCoordinateTransform transform,
        ShowcaseGeometryPlanOptions options,
        List<string> warnings,
        string label)
    {
        double baseFeet = transform.ToRevitInternalZFeet(baseElevationMeters);
        double heightFeet = ShowcaseCoordinateTransform.MetersToFeet(heightMeters);
        if (heightFeet <= options.MinSegmentLengthFeet || bandWidthMeters <= 0d)
        {
            warnings.Add($"Level outline band dimensions for '{label}' are invalid.");
            return Array.Empty<ShowcaseExtrusionSolid>();
        }

        List<ShowcasePolygonGeometry> sourcePolygons = new();
        if (geometry is ShowcasePolygonGeometry singlePoly)
        {
            sourcePolygons.Add(singlePoly);
        }
        else if (geometry is ShowcaseMultiPolygonGeometry multiPoly)
        {
            sourcePolygons.AddRange(multiPoly.Polygons);
        }
        else if (geometry is ShowcaseLineStringGeometry line)
        {
            List<ShowcaseExtrusionSolid> lineBands = new();
            AppendBufferedLineSolids(lineBands, line.Coords, bandWidthMeters, baseFeet, heightFeet, transform, options);
            return lineBands;
        }
        else
        {
            return Array.Empty<ShowcaseExtrusionSolid>();
        }

        List<ShowcaseExtrusionSolid> solids = new();
        BufferParameters bufferParams = new(quadrantSegments: 2, EndCapStyle.Flat, JoinStyle.Mitre, mitreLimit: 2.0d);

        foreach (ShowcasePolygonGeometry srcPoly in sourcePolygons)
        {
            Polygon? ntsOuterOnly = ToNtsOuterPolygon(srcPoly.ExteriorRing);
            if (ntsOuterOnly == null || ntsOuterOnly.IsEmpty)
            {
                continue;
            }

            NtsGeometry expanded = ntsOuterOnly.Buffer(bandWidthMeters, bufferParams);
            NtsGeometry bandGeometry = expanded.Difference(ntsOuterOnly);
            AppendNtsPolygons(solids, bandGeometry, baseFeet, heightFeet, transform, options);

            // Also outline any interior atrium hole boundaries as thin non-occluding rims inside the hole.
            foreach (IReadOnlyList<Point2D> holeRing in srcPoly.InteriorRings)
            {
                Polygon? holePoly = ToNtsOuterPolygon(holeRing);
                if (holePoly == null || holePoly.IsEmpty)
                {
                    continue;
                }

                NtsGeometry innerVoid = holePoly.Buffer(-bandWidthMeters * 0.5d, bufferParams);
                NtsGeometry holeRim = (innerVoid == null || innerVoid.IsEmpty)
                    ? holePoly
                    : holePoly.Difference(innerVoid);
                AppendNtsPolygons(solids, holeRim, baseFeet, heightFeet, transform, options);
            }
        }

        return solids;
    }

    private static void AppendBufferedLineSolids(
        List<ShowcaseExtrusionSolid> solids,
        IReadOnlyList<Point2D> coordsMeters,
        double thicknessMeters,
        double baseFeet,
        double heightFeet,
        ShowcaseCoordinateTransform transform,
        ShowcaseGeometryPlanOptions options)
    {
        if (coordsMeters.Count < 2 || thicknessMeters <= 0d)
        {
            return;
        }

        Coordinate[] ntsCoords = coordsMeters.Select(p => new Coordinate(p.X, p.Y)).ToArray();
        LineString lineString = NtsFactory.CreateLineString(ntsCoords);
        if (lineString.IsEmpty || lineString.Length <= 1e-6d)
        {
            return;
        }

        BufferParameters bufferParams = new(quadrantSegments: 2, EndCapStyle.Flat, JoinStyle.Mitre, mitreLimit: 2.0d);
        NtsGeometry buffered = lineString.Buffer(thicknessMeters * 0.5d, bufferParams);
        AppendNtsPolygons(solids, buffered, baseFeet, heightFeet, transform, options);
    }

    private static void AppendNtsPolygons(
        List<ShowcaseExtrusionSolid> solids,
        NtsGeometry ntsGeometry,
        double baseFeet,
        double heightFeet,
        ShowcaseCoordinateTransform transform,
        ShowcaseGeometryPlanOptions options)
    {
        if (ntsGeometry == null || ntsGeometry.IsEmpty)
        {
            return;
        }

        if (ntsGeometry is Polygon polygon)
        {
            ShowcasePolygonGeometry? converted = FromNtsPolygon(polygon);
            if (converted != null)
            {
                TryAppendPolygonSolid(solids, converted, baseFeet, heightFeet, transform, options);
            }

            return;
        }

        if (ntsGeometry is GeometryCollection collection)
        {
            for (int i = 0; i < collection.NumGeometries; i++)
            {
                AppendNtsPolygons(solids, collection.GetGeometryN(i), baseFeet, heightFeet, transform, options);
            }
        }
    }

    private static Polygon? ToNtsOuterPolygon(IReadOnlyList<Point2D> ringMeters)
    {
        if (ringMeters.Count < 4)
        {
            return null;
        }

        List<Coordinate> coords = ringMeters.Select(p => new Coordinate(p.X, p.Y)).ToList();
        if (!coords[0].Equals2D(coords[coords.Count - 1]))
        {
            coords.Add(new Coordinate(coords[0].X, coords[0].Y));
        }

        LinearRing shell = NtsFactory.CreateLinearRing(coords.ToArray());
        Polygon polygon = NtsFactory.CreatePolygon(shell);
        if (!polygon.IsValid)
        {
            NtsGeometry repaired = polygon.Buffer(0d);
            if (repaired is Polygon validPoly && !validPoly.IsEmpty)
            {
                return validPoly;
            }

            return null;
        }

        return polygon;
    }

    private static ShowcasePolygonGeometry? FromNtsPolygon(Polygon polygon)
    {
        if (polygon.IsEmpty || polygon.ExteriorRing.NumPoints < 4)
        {
            return null;
        }

        List<IReadOnlyList<Point2D>> rings = new(1 + polygon.NumInteriorRings)
        {
            polygon.ExteriorRing.Coordinates.Select(c => new Point2D(c.X, c.Y)).ToArray(),
        };

        for (int i = 0; i < polygon.NumInteriorRings; i++)
        {
            LineString hole = polygon.GetInteriorRingN(i);
            if (hole.NumPoints >= 4)
            {
                rings.Add(hole.Coordinates.Select(c => new Point2D(c.X, c.Y)).ToArray());
            }
        }

        return new ShowcasePolygonGeometry(rings);
    }

    private static void TryAppendPolygonSolid(
        List<ShowcaseExtrusionSolid> solids,
        ShowcasePolygonGeometry polygonMeters,
        double baseFeet,
        double heightFeet,
        ShowcaseCoordinateTransform transform,
        ShowcaseGeometryPlanOptions options)
    {
        IReadOnlyList<Point2D>? outerFeet = TransformAndNormalizeRing(
            polygonMeters.ExteriorRing,
            transform,
            wantCounterClockwise: true,
            options.MinSegmentLengthFeet);
        if (outerFeet == null)
        {
            return;
        }

        List<IReadOnlyList<Point2D>> holesFeet = new(polygonMeters.InteriorRings.Count);
        foreach (IReadOnlyList<Point2D> holeMeters in polygonMeters.InteriorRings)
        {
            IReadOnlyList<Point2D>? holeFeet = TransformAndNormalizeRing(
                holeMeters,
                transform,
                wantCounterClockwise: false,
                options.MinSegmentLengthFeet);
            if (holeFeet != null)
            {
                holesFeet.Add(holeFeet);
            }
        }

        solids.Add(new ShowcaseExtrusionSolid(outerFeet, holesFeet, baseFeet, heightFeet));
    }

    /// <summary>
    /// Transforms a closed CRS ring (metres) into an open Revit internal vertex loop (feet), pruning short/duplicate
    /// segments below <paramref name="minSegmentLengthFeet"/> and enforcing exact winding orientation
    /// (CCW for outer rings, CW for interior hole rings).
    /// </summary>
    internal static IReadOnlyList<Point2D>? TransformAndNormalizeRing(
        IReadOnlyList<Point2D> ringMeters,
        ShowcaseCoordinateTransform transform,
        bool wantCounterClockwise,
        double minSegmentLengthFeet)
    {
        if (ringMeters.Count < 3)
        {
            return null;
        }

        // Convert to Revit internal feet and strip closing vertex duplication.
        int count = ringMeters.Count;
        List<Point2D> transformed = new(count);
        for (int i = 0; i < count; i++)
        {
            transformed.Add(transform.ToRevitInternal2D(ringMeters[i]));
        }

        // Clean consecutive vertices shorter than minSegmentLengthFeet.
        List<Point2D> cleaned = new(transformed.Count);
        for (int i = 0; i < transformed.Count; i++)
        {
            Point2D candidate = transformed[i];
            if (cleaned.Count == 0 || Distance(cleaned[cleaned.Count - 1], candidate) >= minSegmentLengthFeet)
            {
                cleaned.Add(candidate);
            }
        }

        // Trim trailing vertices too close to the starting vertex (including the GeoJSON closing point).
        while (cleaned.Count >= 2 && Distance(cleaned[0], cleaned[cleaned.Count - 1]) < minSegmentLengthFeet)
        {
            cleaned.RemoveAt(cleaned.Count - 1);
        }

        if (cleaned.Count < 3)
        {
            return null;
        }

        double signedArea = ComputeSignedAreaOpen(cleaned);
        if (Math.Abs(signedArea) <= 1e-8d)
        {
            return null;
        }

        bool isCounterClockwise = signedArea > 0d;
        if (isCounterClockwise != wantCounterClockwise)
        {
            cleaned.Reverse();
        }

        return cleaned;
    }

    /// <summary>
    /// Computes the 2D signed area of an open vertex loop (<c>&gt; 0</c> = CCW, <c>&lt; 0</c> = CW),
    /// shifted relative to <c>openRing[0]</c> to avoid floating-point cancellation on large coordinates.
    /// </summary>
    public static double ComputeSignedAreaOpen(IReadOnlyList<Point2D> openRing)
    {
        if (openRing is null || openRing.Count < 3)
        {
            return 0d;
        }

        double x0 = openRing[0].X;
        double y0 = openRing[0].Y;
        double twiceArea = 0d;
        for (int i = 0; i < openRing.Count; i++)
        {
            Point2D a = openRing[i];
            Point2D b = openRing[(i + 1) % openRing.Count];
            twiceArea += ((a.X - x0) * (b.Y - y0)) - ((b.X - x0) * (a.Y - y0));
        }

        return twiceArea * 0.5d;
    }

    private static double Distance(Point2D a, Point2D b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static string SanitizeElementKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "item";
        }

        StringBuilder sb = new(key.Length);
        foreach (char c in key.Trim())
        {
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('_');
            }
        }

        string cleaned = sb.ToString().Trim('_');
        return cleaned.Length == 0 ? "item" : cleaned;
    }
}
