using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Buffer;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>Compact instanced marker prototype shape for Forma Companion F4 point features.</summary>
public enum ShowcaseMarkerShapeKind
{
    /// <summary>6-sided pedestal (20 tris) + 4-sided lower pin stem (12 tris) + 4-sided beacon cap (12 tris) = 44 triangles.</summary>
    PoiPin,

    /// <summary>6-sided compact attachment collar/diamond = 20 triangles.</summary>
    PoiAttachment,

    /// <summary>6-sided junction disc = 20 triangles.</summary>
    NetworkNode,

    /// <summary>8-sided alert puck (28 tris) + 4-sided alert spire (12 tris) = 40 triangles.</summary>
    FindingPuck,
}

/// <summary>
/// Deterministic placement for an instanced 3D marker in Revit internal coordinates (feet).
/// Every prototype strictly stays within a tight planar triangle budget (&lt;= 48 triangles).
/// </summary>
public sealed class ShowcaseMarkerPlacement
{
    public const int MaxAllowedMarkerTriangles = 48;

    public ShowcaseMarkerPlacement(
        ShowcasePoint3D locationFeet,
        ShowcaseMarkerShapeKind shapeKind,
        double baseRadiusFeet,
        double heightFeet,
        double rotationRadians = 0d)
    {
        LocationFeet = locationFeet;
        ShapeKind = shapeKind;
        BaseRadiusFeet = baseRadiusFeet;
        HeightFeet = heightFeet;
        RotationRadians = rotationRadians;
        EstimatedTriangles = GetPrototypeTriangleCount(shapeKind);
    }

    public ShowcasePoint3D LocationFeet { get; }

    public ShowcaseMarkerShapeKind ShapeKind { get; }

    public double BaseRadiusFeet { get; }

    public double HeightFeet { get; }

    public double RotationRadians { get; }

    public int EstimatedTriangles { get; }

    /// <summary>Exact planar triangle count for each compact instanced marker silhouette (all &lt;= 48 triangles).</summary>
    public static int GetPrototypeTriangleCount(ShowcaseMarkerShapeKind shapeKind)
    {
        switch (shapeKind)
        {
            case ShowcaseMarkerShapeKind.PoiPin: return 44;      // 6-sided pedestal (20) + 4-sided stem (12) + 4-sided cap (12)
            case ShowcaseMarkerShapeKind.PoiAttachment: return 20; // 6-sided attachment collar
            case ShowcaseMarkerShapeKind.NetworkNode: return 20;   // 6-sided junction disc
            case ShowcaseMarkerShapeKind.FindingPuck: return 40;   // 8-sided alert puck (28) + 4-sided spire (12)
            default: return 20;
        }
    }
}

/// <summary>
/// One 3D cylindrical/polygonal tube segment in Revit internal coordinates (feet), extruded strictly
/// along <c>EndFeet - StartFeet</c> with a planar N-sided cross-section (<c>4 * Sides - 4</c> triangles).
/// Works identically for vertical elevator shafts, angled escalators/stairs/ramps, and thin POI connector stems.
/// </summary>
public sealed class ShowcaseSweptTubeSegment
{
    public ShowcaseSweptTubeSegment(
        ShowcasePoint3D startFeet,
        ShowcasePoint3D endFeet,
        double radiusFeet,
        int sides = 6)
    {
        if (sides < 3)
        {
            throw new ArgumentOutOfRangeException(nameof(sides), "Tube cross-section must have at least 3 sides.");
        }

        StartFeet = startFeet;
        EndFeet = endFeet;
        RadiusFeet = radiusFeet;
        Sides = sides;
    }

    public ShowcasePoint3D StartFeet { get; }

    public ShowcasePoint3D EndFeet { get; }

    public double RadiusFeet { get; }

    public int Sides { get; }

    public double LengthFeet
    {
        get
        {
            double dx = EndFeet.XFeet - StartFeet.XFeet;
            double dy = EndFeet.YFeet - StartFeet.YFeet;
            double dz = EndFeet.ZFeet - StartFeet.ZFeet;
            return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
    }

    public int EstimatedTriangles => (4 * Sides) - 4;
}

/// <summary>Join outcome for a <c>point_facility_network</c> attachment matched against <c>point_facility</c> by <c>symbol_id</c>.</summary>
public enum ShowcaseAttachmentJoinStatus
{
    Matched,
    UnmatchedPoi,
}

/// <summary>Diagnostic join record for a planned <c>point_facility_network</c> attachment feature.</summary>
public sealed class ShowcasePoiAttachmentJoinRecord
{
    public ShowcasePoiAttachmentJoinRecord(
        string attachmentApplicationDataId,
        string symbolId,
        ShowcaseAttachmentJoinStatus status,
        string? matchedPoiApplicationDataId,
        string? matchedNodeId,
        string? matchedLinkId,
        double? connectorStemLengthMeters)
    {
        AttachmentApplicationDataId = attachmentApplicationDataId ?? string.Empty;
        SymbolId = symbolId ?? string.Empty;
        Status = status;
        MatchedPoiApplicationDataId = matchedPoiApplicationDataId;
        MatchedNodeId = matchedNodeId;
        MatchedLinkId = matchedLinkId;
        ConnectorStemLengthMeters = connectorStemLengthMeters;
    }

    public string AttachmentApplicationDataId { get; }

    public string SymbolId { get; }

    public ShowcaseAttachmentJoinStatus Status { get; }

    public string? MatchedPoiApplicationDataId { get; }

    public string? MatchedNodeId { get; }

    public string? MatchedLinkId { get; }

    public double? ConnectorStemLengthMeters { get; }
}

/// <summary>Geometry, offset, spacing, and triangle-budget parameters for <see cref="ShowcasePoiAndNetworkPlanner"/>.</summary>
public sealed class ShowcasePoiAndNetworkPlanOptions
{
    public static ShowcasePoiAndNetworkPlanOptions Default { get; } = new();

    /// <summary>Vertical offset (metres) floating POI markers cleanly above floor/unit slabs (+0.18 m > 0.15 m unit slab).</summary>
    public double PoiBaseElevationOffsetMeters { get; set; } = 0.18d;

    /// <summary>Base pedestal radius (metres) for compact <c>KNZ_FRM_PoiMarker</c> pins.</summary>
    public double PoiMarkerRadiusMeters { get; set; } = 0.18d;

    /// <summary>Total height (metres) for compact <c>KNZ_FRM_PoiMarker</c> pins.</summary>
    public double PoiMarkerHeightMeters { get; set; } = 0.52d;

    /// <summary>Vertical offset (metres) for <c>point_facility_network</c> attachment markers.</summary>
    public double AttachmentBaseElevationOffsetMeters { get; set; } = 0.36d;

    /// <summary>Radius (metres) for <c>point_facility_network</c> attachment markers.</summary>
    public double AttachmentMarkerRadiusMeters { get; set; } = 0.14d;

    /// <summary>Height (metres) for <c>point_facility_network</c> attachment markers.</summary>
    public double AttachmentMarkerHeightMeters { get; set; } = 0.08d;

    /// <summary>Radius (metres) of thin 3D connector stems linking a POI marker to its network attachment point.</summary>
    public double AttachmentStemRadiusMeters { get; set; } = 0.035d;

    /// <summary>Vertical elevation offset (metres) for horizontal network ribbons (0.35-0.50 m above floor TP to avoid Z-fighting).</summary>
    public double NetworkRibbonElevationOffsetMeters { get; set; } = 0.40d;

    /// <summary>Planar width (metres) for horizontal 3D network ribbons.</summary>
    public double NetworkRibbonWidthMeters { get; set; } = 0.24d;

    /// <summary>Vertical thickness (metres) for horizontal 3D network ribbons.</summary>
    public double NetworkRibbonThicknessMeters { get; set; } = 0.05d;

    /// <summary>Spacing (metres) between directional chevrons along directed/one-way network links.</summary>
    public double ChevronSpacingMeters { get; set; } = 4.0d;

    /// <summary>Minimum horizontal link length (metres) required to place a directional chevron.</summary>
    public double MinChevronLinkLengthMeters { get; set; } = 0.80d;

    /// <summary>Arrow length (metres) for one-way directional chevrons.</summary>
    public double ChevronLengthMeters { get; set; } = 0.36d;

    /// <summary>Arrow half-width (metres) for one-way directional chevrons.</summary>
    public double ChevronHalfWidthMeters { get; set; } = 0.14d;

    /// <summary>Extrusion height (metres) for directional chevrons sitting above the ribbon.</summary>
    public double ChevronHeightMeters { get; set; } = 0.03d;

    /// <summary>Clearance gap (metres) above ribbon top face to prevent Z-fighting on chevrons.</summary>
    public double ChevronElevationGapMeters { get; set; } = 0.005d;

    /// <summary>Upper bound on chevrons per link to keep triangle counts tight on long corridors.</summary>
    public int MaxChevronsPerLink { get; set; } = 24;

    /// <summary>Cross-section radius (metres) for surveyed 3D vertical transition tubes.</summary>
    public double VerticalTubeRadiusMeters { get; set; } = 0.14d;

    /// <summary>Cross-section radius (metres) for schematic/synthesized 3D vertical transition tubes.</summary>
    public double SchematicVerticalTubeRadiusMeters { get; set; } = 0.10d;

    /// <summary>Number of sides for hexagonal vertical transition tubes (6 sides = 20 tris/segment).</summary>
    public int VerticalTubeSides { get; set; } = 6;

    /// <summary>Minimum elevation change (metres) that classifies a <c>network_link</c> as a vertical transition tube.</summary>
    public double VerticalTransitionMinDeltaMeters { get; set; } = 0.25d;

    /// <summary>Vertical elevation offset (metres) for standard <c>network_node</c> discs.</summary>
    public double NetworkNodeElevationOffsetMeters { get; set; } = 0.38d;

    /// <summary>Radius (metres) for standard <c>network_node</c> discs.</summary>
    public double NetworkNodeRadiusMeters { get; set; } = 0.14d;

    /// <summary>Height (metres) for standard <c>network_node</c> discs.</summary>
    public double NetworkNodeHeightMeters { get; set; } = 0.06d;

    /// <summary>Vertical elevation offset (metres) for red <c>net_junction</c> finding pucks.</summary>
    public double FindingPuckElevationOffsetMeters { get; set; } = 0.42d;

    /// <summary>Radius (metres) for red <c>net_junction</c> finding pucks.</summary>
    public double FindingPuckRadiusMeters { get; set; } = 0.30d;

    /// <summary>Height (metres) for red <c>net_junction</c> finding pucks (puck + alert spire).</summary>
    public double FindingPuckHeightMeters { get; set; } = 0.38d;

    /// <summary>Fallback floor-to-floor spacing (metres) when a level carries no <c>tpM</c> elevation.</summary>
    public double DefaultFloorHeightMeters { get; set; } = 4.5d;

    /// <summary>Minimum edge length (feet) safely above Revit's ShortCurveTolerance (~0.0026 ft).</summary>
    public double MinSegmentLengthFeet { get; set; } = 0.003d;

    /// <summary>Minimum planar link length (metres) below which a horizontal network segment is treated as degenerate.</summary>
    public double MinNetworkSegmentMeters { get; set; } = 0.05d;
}

/// <summary>Complete output of <see cref="ShowcasePoiAndNetworkPlanner.Plan"/>.</summary>
public sealed class ShowcasePoiAndNetworkPlanResult
{
    public ShowcasePoiAndNetworkPlanResult(
        ShowcaseParameterRegistry parameterRegistry,
        IReadOnlyList<ShowcaseElementPlan> elements,
        IReadOnlyList<ShowcasePoiAttachmentJoinRecord> attachmentJoins,
        IReadOnlyList<string> warnings,
        int poiMarkerCount,
        int attachmentMarkerCount,
        int matchedAttachmentCount,
        int unmatchedAttachmentCount,
        int horizontalLinkCount,
        int verticalTubeCount,
        int chevronCount,
        int networkNodeCount,
        int findingPuckCount,
        int surveyedElementCount,
        int schematicElementCount,
        int totalEstimatedTriangles,
        int uniqueInstancedPrototypeTriangles)
    {
        ParameterRegistry = parameterRegistry ?? throw new ArgumentNullException(nameof(parameterRegistry));
        Elements = elements ?? throw new ArgumentNullException(nameof(elements));
        AttachmentJoins = attachmentJoins ?? throw new ArgumentNullException(nameof(attachmentJoins));
        Warnings = warnings ?? throw new ArgumentNullException(nameof(warnings));
        PoiMarkerCount = poiMarkerCount;
        AttachmentMarkerCount = attachmentMarkerCount;
        MatchedAttachmentCount = matchedAttachmentCount;
        UnmatchedAttachmentCount = unmatchedAttachmentCount;
        HorizontalLinkCount = horizontalLinkCount;
        VerticalTubeCount = verticalTubeCount;
        ChevronCount = chevronCount;
        NetworkNodeCount = networkNodeCount;
        FindingPuckCount = findingPuckCount;
        SurveyedElementCount = surveyedElementCount;
        SchematicElementCount = schematicElementCount;
        TotalEstimatedTriangles = totalEstimatedTriangles;
        UniqueInstancedPrototypeTriangles = uniqueInstancedPrototypeTriangles;
    }

    public ShowcaseParameterRegistry ParameterRegistry { get; }

    public IReadOnlyList<ShowcaseElementPlan> Elements { get; }

    public IReadOnlyList<ShowcasePoiAttachmentJoinRecord> AttachmentJoins { get; }

    public IReadOnlyList<string> Warnings { get; }

    public int PoiMarkerCount { get; }

    public int AttachmentMarkerCount { get; }

    public int MatchedAttachmentCount { get; }

    public int UnmatchedAttachmentCount { get; }

    public int HorizontalLinkCount { get; }

    public int VerticalTubeCount { get; }

    public int ChevronCount { get; }

    public int NetworkNodeCount { get; }

    public int FindingPuckCount { get; }

    public int SurveyedElementCount { get; }

    public int SchematicElementCount { get; }

    public int TotalEstimatedTriangles { get; }

    /// <summary>Sum of triangle counts for unique instanced marker prototypes (reflecting zero mesh duplication across instances).</summary>
    public int UniqueInstancedPrototypeTriangles { get; }
}

/// <summary>
/// Pure-C# Forma Companion F4 planner producing deterministic, geometry-validated placement and 3D solid plans for:
/// <list type="bullet">
/// <item><c>point_facility</c> instanced <c>KNZ_FRM_PoiMarker</c> POI beacons (22+ attributes via UUIDv5 <c>ks_*</c> + overflow JSON)</item>
/// <item><c>point_facility_network</c> attachment markers joined by <c>symbol_id</c> with thin 3D connector stems and unmatched-join diagnostics</item>
/// <item>Horizontal <c>network_link</c> elevated 3D ribbons + directional chevrons</item>
/// <item>Vertical transition tubes (<c>network_connector</c> / multi-level or 3D <c>network_link</c>) with explicit <c>surveyed</c> vs <c>schematic</c> provenance</item>
/// <item>Standard <c>network_node</c> discs and red <c>net_junction</c> QA finding pucks</item>
/// </list>
/// </summary>
public static class ShowcasePoiAndNetworkPlanner
{
    public const string PoiMarkerFamilyName = "KNZ_FRM_PoiMarker";
    public const string PoiMarkerNamePrefix = "KNZ_FRM_PoiMarker";
    public const string PoiAttachmentNamePrefix = "KNZ_FRM_PoiAttachment";
    public const string NetworkRibbonNamePrefix = "KNZ_FRM_NetRibbon";
    public const string VerticalTubeNamePrefix = "KNZ_FRM_VerticalTube";
    public const string NetworkNodeNamePrefix = "KNZ_FRM_NetNode";
    public const string FindingPuckNamePrefix = "KNZ_FRM_NetJunction";

    private static readonly GeometryFactory NtsFactory = new();

    public static bool IsF4SupportedLayerKind(ShowcaseLayerKind kind) =>
        kind == ShowcaseLayerKind.PointFacility ||
        kind == ShowcaseLayerKind.PointFacilityNetwork ||
        kind == ShowcaseLayerKind.NetworkLink ||
        kind == ShowcaseLayerKind.NetworkConnector ||
        kind == ShowcaseLayerKind.NetworkNode;

    public static ShowcasePoiAndNetworkPlanResult Plan(
        ShowcaseSnapshot snapshot,
        ShowcaseCoordinateTransform transform,
        ShowcaseParameterRegistry? parameterRegistry = null,
        ShowcasePoiAndNetworkPlanOptions? options = null)
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
        ShowcasePoiAndNetworkPlanOptions effectiveOptions = options ?? ShowcasePoiAndNetworkPlanOptions.Default;

        LevelLookup levelLookup = BuildLevelLookup(snapshot.Levels);

        List<ShowcaseElementPlan> elements = new();
        List<ShowcasePoiAttachmentJoinRecord> attachmentJoins = new();
        List<string> warnings = new();

        // Counters
        int poiCount = 0;
        int attachmentCount = 0;
        int matchedAttachmentCount = 0;
        int unmatchedAttachmentCount = 0;
        int horizontalLinkCount = 0;
        int verticalTubeCount = 0;
        int chevronCount = 0;
        int networkNodeCount = 0;
        int findingPuckCount = 0;

        // Pass 1: Plan point_facility first and index by symbol_id so point_facility_network joins cleanly.
        Dictionary<string, PlannedPoiIndexEntry> poisBySymbolId = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShowcaseLayer layer in snapshot.Layers.Where(l => l.Kind == ShowcaseLayerKind.PointFacility))
        {
            IReadOnlyDictionary<string, bool> preserveMap = ShowcaseAttributeNormalizer.ComputeColumnPreserveAsTextMap(layer);
            foreach (ShowcaseFeature feature in layer.Features)
            {
                ShowcaseElementPlan? poiPlan = PlanPoiFeature(
                    layer,
                    feature,
                    levelLookup,
                    transform,
                    registry,
                    preserveMap,
                    effectiveOptions,
                    warnings,
                    poisBySymbolId);
                if (poiPlan != null)
                {
                    elements.Add(poiPlan);
                    poiCount++;
                }
            }
        }

        // Pass 2: Pre-index network nodes (by node_id / key) and network links (by link_id / pathid / key)
        // for secondary attachment validation and node connector stems.
        Dictionary<string, ShowcasePoint3D> nodePositionsFeetById = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShowcaseLayer layer in snapshot.Layers.Where(l => l.Kind == ShowcaseLayerKind.NetworkNode))
        {
            foreach (ShowcaseFeature feature in layer.Features)
            {
                IReadOnlyList<ShowcasePointGeometry> pts = ExtractPoints(feature.Geometry);
                if (pts.Count == 0)
                {
                    continue;
                }

                ResolvedElevation elev = ResolvePointElevation(
                    feature,
                    pts[0].Z,
                    levelLookup,
                    effectiveOptions.DefaultFloorHeightMeters,
                    warnings: null,
                    featureLabel: null);
                double zMeters = elev.ElevationMeters + effectiveOptions.NetworkNodeElevationOffsetMeters;
                ShowcasePoint3D posFeet = transform.ToRevitInternal(pts[0].Xy.X, pts[0].Xy.Y, zMeters);

                string nodeKey = ReadAttributeString(feature.Attributes, "node_id", "id") ?? feature.Key;
                if (!string.IsNullOrWhiteSpace(nodeKey) && !nodePositionsFeetById.ContainsKey(nodeKey))
                {
                    nodePositionsFeetById[nodeKey] = posFeet;
                }

                if (!nodePositionsFeetById.ContainsKey(feature.Key))
                {
                    nodePositionsFeetById[feature.Key] = posFeet;
                }
            }
        }

        HashSet<string> knownLinkIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShowcaseLayer layer in snapshot.Layers.Where(l => l.Kind == ShowcaseLayerKind.NetworkLink || l.Kind == ShowcaseLayerKind.NetworkConnector))
        {
            foreach (ShowcaseFeature feature in layer.Features)
            {
                knownLinkIds.Add(feature.Key);
                string? linkId = ReadAttributeString(feature.Attributes, "link_id", "pathid", "id");
                if (!string.IsNullOrWhiteSpace(linkId))
                {
                    knownLinkIds.Add(linkId!);
                }
            }
        }

        // Pass 3: Plan point_facility_network attachment markers joined by symbol_id.
        foreach (ShowcaseLayer layer in snapshot.Layers.Where(l => l.Kind == ShowcaseLayerKind.PointFacilityNetwork))
        {
            IReadOnlyDictionary<string, bool> preserveMap = ShowcaseAttributeNormalizer.ComputeColumnPreserveAsTextMap(layer);
            foreach (ShowcaseFeature feature in layer.Features)
            {
                ShowcaseElementPlan? attachmentPlan = PlanPoiAttachmentFeature(
                    layer,
                    feature,
                    levelLookup,
                    transform,
                    registry,
                    preserveMap,
                    effectiveOptions,
                    poisBySymbolId,
                    nodePositionsFeetById,
                    knownLinkIds,
                    attachmentJoins,
                    warnings,
                    out bool matchedPoi);
                if (attachmentPlan != null)
                {
                    elements.Add(attachmentPlan);
                    attachmentCount++;
                    if (matchedPoi)
                    {
                        matchedAttachmentCount++;
                    }
                    else
                    {
                        unmatchedAttachmentCount++;
                    }
                }
            }
        }

        // Pass 4: Plan network_link (horizontal elevated 3D ribbons + directional chevrons OR vertical transition tubes).
        foreach (ShowcaseLayer layer in snapshot.Layers.Where(l => l.Kind == ShowcaseLayerKind.NetworkLink))
        {
            IReadOnlyDictionary<string, bool> preserveMap = ShowcaseAttributeNormalizer.ComputeColumnPreserveAsTextMap(layer);
            foreach (ShowcaseFeature feature in layer.Features)
            {
                ShowcaseElementPlan? linkPlan = PlanNetworkLinkFeature(
                    layer,
                    feature,
                    levelLookup,
                    transform,
                    registry,
                    preserveMap,
                    effectiveOptions,
                    warnings,
                    out int linkChevrons);
                if (linkPlan != null)
                {
                    elements.Add(linkPlan);
                    if (linkPlan.ElementKind == ShowcaseElementKind.VerticalTube)
                    {
                        verticalTubeCount++;
                    }
                    else
                    {
                        horizontalLinkCount++;
                        chevronCount += linkChevrons;
                    }
                }
            }
        }

        // Pass 5: Plan network_connector (3D vertical/angled transition tubes with explicit provenance).
        foreach (ShowcaseLayer layer in snapshot.Layers.Where(l => l.Kind == ShowcaseLayerKind.NetworkConnector))
        {
            IReadOnlyDictionary<string, bool> preserveMap = ShowcaseAttributeNormalizer.ComputeColumnPreserveAsTextMap(layer);
            foreach (ShowcaseFeature feature in layer.Features)
            {
                ShowcaseElementPlan? connectorPlan = PlanNetworkConnectorFeature(
                    layer,
                    feature,
                    levelLookup,
                    transform,
                    registry,
                    preserveMap,
                    effectiveOptions,
                    warnings);
                if (connectorPlan != null)
                {
                    elements.Add(connectorPlan);
                    verticalTubeCount++;
                }
            }
        }

        // Pass 6: Plan network_node & net_junction QA finding pucks.
        foreach (ShowcaseLayer layer in snapshot.Layers.Where(l => l.Kind == ShowcaseLayerKind.NetworkNode))
        {
            IReadOnlyDictionary<string, bool> preserveMap = ShowcaseAttributeNormalizer.ComputeColumnPreserveAsTextMap(layer);
            foreach (ShowcaseFeature feature in layer.Features)
            {
                ShowcaseElementPlan? nodePlan = PlanNetworkNodeOrFindingFeature(
                    layer,
                    feature,
                    levelLookup,
                    transform,
                    registry,
                    preserveMap,
                    effectiveOptions,
                    warnings);
                if (nodePlan != null)
                {
                    elements.Add(nodePlan);
                    if (nodePlan.ElementKind == ShowcaseElementKind.FindingPuck)
                    {
                        findingPuckCount++;
                    }
                    else
                    {
                        networkNodeCount++;
                    }
                }
            }
        }

        int surveyedCount = 0;
        int schematicCount = 0;
        int totalTris = 0;
        HashSet<(ShowcaseMarkerShapeKind Shape, string Material)> uniquePrototypes = new();
        int uniquePrototypeTris = 0;

        foreach (ShowcaseElementPlan element in elements)
        {
            if (element.GeometryProvenance == ShowcaseGeometryProvenance.Schematic)
            {
                schematicCount++;
            }
            else
            {
                surveyedCount++;
            }

            totalTris += element.EstimatedTriangleCount;
            foreach (ShowcaseMarkerPlacement marker in element.MarkerPlacements)
            {
                if (uniquePrototypes.Add((marker.ShapeKind, element.MaterialName ?? string.Empty)))
                {
                    uniquePrototypeTris += marker.EstimatedTriangles;
                }
            }
        }

        return new ShowcasePoiAndNetworkPlanResult(
            registry,
            elements,
            attachmentJoins,
            warnings,
            poiCount,
            attachmentCount,
            matchedAttachmentCount,
            unmatchedAttachmentCount,
            horizontalLinkCount,
            verticalTubeCount,
            chevronCount,
            networkNodeCount,
            findingPuckCount,
            surveyedCount,
            schematicCount,
            totalTris,
            uniquePrototypeTris);
    }

    /// <summary>Determines whether a <c>networkNode</c> layer or feature represents a <c>net_junction</c> QA finding.</summary>
    public static bool IsFindingNode(string layerName, ShowcaseFeature feature)
    {
        if (!string.IsNullOrWhiteSpace(layerName))
        {
            string lowerLayer = layerName.Trim().ToLowerInvariant();
            if (lowerLayer == "net_junction" ||
                lowerLayer.Contains("net_junction") ||
                lowerLayer.Contains("finding") ||
                lowerLayer.Contains("qa_alert") ||
                lowerLayer.Contains("topology_issue"))
            {
                return true;
            }
        }

        IReadOnlyDictionary<string, ShowcaseAttributeValue> attrs = feature.Attributes;
        foreach (string key in new[] { "finding_kind", "finding", "issue_type", "qa_finding", "alert_type", "error_kind" })
        {
            if (!string.IsNullOrWhiteSpace(ReadAttributeString(attrs, key)))
            {
                return true;
            }
        }

        foreach (string boolKey in new[] { "is_finding", "has_finding", "is_error", "is_warning", "is_alert" })
        {
            if (TryReadBoolean(attrs, boolKey, out bool flag) && flag)
            {
                return true;
            }
        }

        string? severity = ReadAttributeString(attrs, "severity", "finding_severity")?.ToLowerInvariant();
        if (severity is "error" or "warning" or "alert" or "critical" or "fail" or "high" or "medium" or "info")
        {
            return true;
        }

        string? nodeType = ReadAttributeString(attrs, "node_type", "junction_type", "type", "category")?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(nodeType) &&
            (nodeType == "net_junction" ||
             nodeType == "finding" ||
             nodeType == "dead_end" ||
             nodeType == "broken_vertical" ||
             nodeType == "disconnected" ||
             nodeType == "orphan" ||
             nodeType == "isolated" ||
             nodeType == "dangling" ||
             nodeType == "alert" ||
             nodeType == "warning" ||
             nodeType == "error"))
        {
            return true;
        }

        return false;
    }

    /// <summary>Parses link directionality from <c>direction</c> / <c>oneway</c> attributes (<c>+1</c> forward, <c>-1</c> reverse, <c>0</c> bidirectional).</summary>
    public static int ResolveLinkDirection(IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes)
    {
        string? dirText = ReadAttributeString(attributes, "direction", "dir", "travel_direction", "flow_direction")?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(dirText))
        {
            if (dirText is "forward" or "1" or "+1" or "positive" or "oneway" or "one_way" or "one-way" or "up" or "in" or "ft")
            {
                return 1;
            }

            if (dirText is "reverse" or "-1" or "backward" or "backwards" or "negative" or "down" or "out" or "tf")
            {
                return -1;
            }

            if (dirText is "both" or "0" or "bidirectional" or "two_way" or "two-way" or "none")
            {
                return 0;
            }
        }

        foreach (string key in new[] { "oneway", "one_way", "is_oneway", "is_one_way" })
        {
            if (!attributes.TryGetValue(key, out ShowcaseAttributeValue? val) || val is null || val.IsNull)
            {
                continue;
            }

            if (val.Kind == ShowcaseAttributeKind.Boolean)
            {
                return val.BoolValue == true ? 1 : 0;
            }

            if (val.Kind == ShowcaseAttributeKind.Integer)
            {
                long v = val.Int64Value!.Value;
                return v > 0 ? 1 : v < 0 ? -1 : 0;
            }

            if (val.Kind == ShowcaseAttributeKind.Text)
            {
                string t = (val.TextValue ?? string.Empty).Trim().ToLowerInvariant();
                if (t is "true" or "yes" or "1" or "+1" or "forward" or "y")
                {
                    return 1;
                }

                if (t is "-1" or "reverse" or "backward")
                {
                    return -1;
                }

                if (t is "false" or "no" or "0" or "both" or "n")
                {
                    return 0;
                }
            }
        }

        return 0;
    }

    private static ShowcaseElementPlan? PlanPoiFeature(
        ShowcaseLayer layer,
        ShowcaseFeature feature,
        LevelLookup levelLookup,
        ShowcaseCoordinateTransform transform,
        ShowcaseParameterRegistry registry,
        IReadOnlyDictionary<string, bool> preserveMap,
        ShowcasePoiAndNetworkPlanOptions options,
        List<string> warnings,
        Dictionary<string, PlannedPoiIndexEntry> poisBySymbolId)
    {
        string appDataId = ShowcaseGeometryPlanner.BuildApplicationDataId(layer.Name, feature.Key);
        IReadOnlyList<ShowcasePointGeometry> points = ExtractPoints(feature.Geometry);
        if (points.Count == 0)
        {
            warnings.Add($"Skipped '{appDataId}' because point_facility feature does not have valid Point/MultiPoint geometry.");
            return null;
        }

        List<ShowcaseMarkerPlacement> placements = new(points.Count);
        ShowcaseGeometryProvenance provenance = ShowcaseGeometryProvenance.Surveyed;
        double radiusFeet = ShowcaseCoordinateTransform.MetersToFeet(options.PoiMarkerRadiusMeters);
        double heightFeet = ShowcaseCoordinateTransform.MetersToFeet(options.PoiMarkerHeightMeters);

        for (int i = 0; i < points.Count; i++)
        {
            ShowcasePointGeometry pt = points[i];
            if (!IsFinite(pt.Xy.X) || !IsFinite(pt.Xy.Y))
            {
                continue;
            }

            ResolvedElevation elev = ResolvePointElevation(
                feature,
                pt.Z,
                levelLookup,
                options.DefaultFloorHeightMeters,
                warnings,
                appDataId);
            if (elev.Provenance == ShowcaseGeometryProvenance.Schematic)
            {
                provenance = ShowcaseGeometryProvenance.Schematic;
            }

            double markerBaseMeters = elev.ElevationMeters + options.PoiBaseElevationOffsetMeters;
            ShowcasePoint3D locFeet = transform.ToRevitInternal(pt.Xy.X, pt.Xy.Y, markerBaseMeters);
            placements.Add(new ShowcaseMarkerPlacement(locFeet, ShowcaseMarkerShapeKind.PoiPin, radiusFeet, heightFeet));
        }

        if (placements.Count == 0)
        {
            warnings.Add($"Skipped '{appDataId}' because no finite point coordinates were found.");
            return null;
        }

        string materialName = ShowcaseMaterialCatalog.ResolvePoiMaterialName(feature.Attributes);
        (IReadOnlyList<ShowcaseParameterAssignment> assignments, string? overflowJson) =
            BindWithProvenance(registry, layer, feature, preserveMap, provenance);

        string elementName = $"{PoiMarkerNamePrefix}_{ShowcaseGeometryPlanner.SanitizeElementKey(feature.Key)}";

        string symbolId = ReadAttributeString(feature.Attributes, "symbol_id", "poi_symbol_id", "id") ?? feature.Key;
        if (!string.IsNullOrWhiteSpace(symbolId) && !poisBySymbolId.ContainsKey(symbolId))
        {
            poisBySymbolId[symbolId] = new PlannedPoiIndexEntry(appDataId, symbolId, placements[0].LocationFeet);
        }

        if (!poisBySymbolId.ContainsKey(feature.Key))
        {
            poisBySymbolId[feature.Key] = new PlannedPoiIndexEntry(appDataId, symbolId, placements[0].LocationFeet);
        }

        return new ShowcaseElementPlan(
            layer.Kind,
            layer.Name,
            feature.Key,
            ShowcaseExportGuard.ShowcaseApplicationId,
            appDataId,
            elementName,
            preferredCategoryName: "OST_GenericModel",
            feature.LevelId,
            feature.FloorKey,
            isNonOccludingOutlineBand: false,
            solids: Array.Empty<ShowcaseExtrusionSolid>(),
            parameterAssignments: assignments,
            overflowAttributesJson: overflowJson,
            elementKind: ShowcaseElementKind.PoiMarker,
            materialName: materialName,
            geometryProvenance: provenance,
            markerPlacements: placements);
    }

    private static ShowcaseElementPlan? PlanPoiAttachmentFeature(
        ShowcaseLayer layer,
        ShowcaseFeature feature,
        LevelLookup levelLookup,
        ShowcaseCoordinateTransform transform,
        ShowcaseParameterRegistry registry,
        IReadOnlyDictionary<string, bool> preserveMap,
        ShowcasePoiAndNetworkPlanOptions options,
        Dictionary<string, PlannedPoiIndexEntry> poisBySymbolId,
        Dictionary<string, ShowcasePoint3D> nodePositionsFeetById,
        HashSet<string> knownLinkIds,
        List<ShowcasePoiAttachmentJoinRecord> attachmentJoins,
        List<string> warnings,
        out bool matchedPoi)
    {
        matchedPoi = false;
        string appDataId = ShowcaseGeometryPlanner.BuildApplicationDataId(layer.Name, feature.Key);
        IReadOnlyList<ShowcasePointGeometry> points = ExtractPoints(feature.Geometry);
        if (points.Count == 0)
        {
            warnings.Add($"Skipped '{appDataId}' because point_facility_network feature does not have valid Point/MultiPoint geometry.");
            return null;
        }

        List<ShowcaseMarkerPlacement> placements = new(points.Count);
        ShowcaseGeometryProvenance provenance = ShowcaseGeometryProvenance.Surveyed;
        double radiusFeet = ShowcaseCoordinateTransform.MetersToFeet(options.AttachmentMarkerRadiusMeters);
        double heightFeet = ShowcaseCoordinateTransform.MetersToFeet(options.AttachmentMarkerHeightMeters);

        for (int i = 0; i < points.Count; i++)
        {
            ShowcasePointGeometry pt = points[i];
            if (!IsFinite(pt.Xy.X) || !IsFinite(pt.Xy.Y))
            {
                continue;
            }

            ResolvedElevation elev = ResolvePointElevation(
                feature,
                pt.Z,
                levelLookup,
                options.DefaultFloorHeightMeters,
                warnings,
                appDataId);
            if (elev.Provenance == ShowcaseGeometryProvenance.Schematic)
            {
                provenance = ShowcaseGeometryProvenance.Schematic;
            }

            double attachBaseMeters = elev.ElevationMeters + options.AttachmentBaseElevationOffsetMeters;
            ShowcasePoint3D locFeet = transform.ToRevitInternal(pt.Xy.X, pt.Xy.Y, attachBaseMeters);
            placements.Add(new ShowcaseMarkerPlacement(locFeet, ShowcaseMarkerShapeKind.PoiAttachment, radiusFeet, heightFeet));
        }

        if (placements.Count == 0)
        {
            warnings.Add($"Skipped '{appDataId}' because no finite attachment coordinates were found.");
            return null;
        }

        string symbolId = ReadAttributeString(feature.Attributes, "symbol_id", "poi_symbol_id", "facility_id") ?? feature.Key;
        string? nodeId = ReadAttributeString(feature.Attributes, "node_id", "net_node_id");
        string? linkId = ReadAttributeString(feature.Attributes, "link_id", "pathid", "net_link_id");

        string? matchedNodeId = null;
        ShowcasePoint3D? matchedNodePosFeet = null;
        if (!string.IsNullOrWhiteSpace(nodeId))
        {
            if (nodePositionsFeetById.TryGetValue(nodeId!, out ShowcasePoint3D nodePos))
            {
                matchedNodeId = nodeId;
                matchedNodePosFeet = nodePos;
            }
            else
            {
                warnings.Add($"point_facility_network '{appDataId}' references node_id '{nodeId}' which was not found in network_node layers.");
            }
        }

        string? matchedLinkId = null;
        if (!string.IsNullOrWhiteSpace(linkId))
        {
            if (knownLinkIds.Contains(linkId!))
            {
                matchedLinkId = linkId;
            }
            else
            {
                warnings.Add($"point_facility_network '{appDataId}' references link_id '{linkId}' which was not found in network_link/network_connector layers.");
            }
        }

        List<ShowcaseSweptTubeSegment> connectorStems = new();
        double stemRadiusFeet = ShowcaseCoordinateTransform.MetersToFeet(options.AttachmentStemRadiusMeters);
        double? stemLengthMeters = null;
        string? matchedPoiAppDataId = null;
        ShowcaseAttachmentJoinStatus joinStatus;

        if (!string.IsNullOrWhiteSpace(symbolId) && poisBySymbolId.TryGetValue(symbolId, out PlannedPoiIndexEntry? poiEntry))
        {
            matchedPoi = true;
            joinStatus = ShowcaseAttachmentJoinStatus.Matched;
            matchedPoiAppDataId = poiEntry.ApplicationDataId;

            ShowcasePoint3D attachPoint = placements[0].LocationFeet;
            // Connect from POI pedestal/stem mid-point cleanly to the network attachment point.
            ShowcasePoint3D poiAnchor = new(
                poiEntry.LocationFeet.XFeet,
                poiEntry.LocationFeet.YFeet,
                poiEntry.LocationFeet.ZFeet + ShowcaseCoordinateTransform.MetersToFeet(options.PoiMarkerHeightMeters * 0.35d));
            double stemLenFeet = Distance3D(poiAnchor, attachPoint);
            if (stemLenFeet >= options.MinSegmentLengthFeet)
            {
                connectorStems.Add(new ShowcaseSweptTubeSegment(poiAnchor, attachPoint, stemRadiusFeet, sides: 4));
                stemLengthMeters = ShowcaseCoordinateTransform.FeetToMeters(stemLenFeet);
            }
            else
            {
                stemLengthMeters = 0d;
            }
        }
        else
        {
            joinStatus = ShowcaseAttachmentJoinStatus.UnmatchedPoi;
            warnings.Add(
                $"point_facility_network '{appDataId}' has symbol_id '{symbolId}' with no matching point_facility feature (non-fatal unmatched join).");
        }

        // If attachment also resolves a distinct network_node position, emit a thin tie stem to that node as well.
        if (matchedNodePosFeet.HasValue)
        {
            double nodeStemFeet = Distance3D(placements[0].LocationFeet, matchedNodePosFeet.Value);
            if (nodeStemFeet >= options.MinSegmentLengthFeet)
            {
                connectorStems.Add(new ShowcaseSweptTubeSegment(placements[0].LocationFeet, matchedNodePosFeet.Value, stemRadiusFeet, sides: 4));
            }
        }

        attachmentJoins.Add(new ShowcasePoiAttachmentJoinRecord(
            appDataId,
            symbolId,
            joinStatus,
            matchedPoiAppDataId,
            matchedNodeId,
            matchedLinkId,
            stemLengthMeters));

        string materialName = matchedPoi
            ? ShowcaseMaterialCatalog.PoiAttachment
            : ShowcaseMaterialCatalog.PoiAttachmentUnmatched;

        (IReadOnlyList<ShowcaseParameterAssignment> assignments, string? overflowJson) =
            BindWithProvenance(registry, layer, feature, preserveMap, provenance);

        string elementName = $"{PoiAttachmentNamePrefix}_{ShowcaseGeometryPlanner.SanitizeElementKey(feature.Key)}";

        return new ShowcaseElementPlan(
            layer.Kind,
            layer.Name,
            feature.Key,
            ShowcaseExportGuard.ShowcaseApplicationId,
            appDataId,
            elementName,
            preferredCategoryName: "OST_GenericModel",
            feature.LevelId,
            feature.FloorKey,
            isNonOccludingOutlineBand: false,
            solids: Array.Empty<ShowcaseExtrusionSolid>(),
            parameterAssignments: assignments,
            overflowAttributesJson: overflowJson,
            elementKind: ShowcaseElementKind.PoiAttachment,
            materialName: materialName,
            geometryProvenance: provenance,
            markerPlacements: placements,
            tubeSegments: connectorStems);
    }

    private static ShowcaseElementPlan? PlanNetworkLinkFeature(
        ShowcaseLayer layer,
        ShowcaseFeature feature,
        LevelLookup levelLookup,
        ShowcaseCoordinateTransform transform,
        ShowcaseParameterRegistry registry,
        IReadOnlyDictionary<string, bool> preserveMap,
        ShowcasePoiAndNetworkPlanOptions options,
        List<string> warnings,
        out int chevronCount)
    {
        chevronCount = 0;
        string appDataId = ShowcaseGeometryPlanner.BuildApplicationDataId(layer.Name, feature.Key);

        List<(IReadOnlyList<Point2D> Coords, IReadOnlyList<double?>? Zs)> polylines = ExtractPolylines(feature.Geometry);
        if (polylines.Count == 0)
        {
            warnings.Add($"Skipped '{appDataId}' because network_link feature does not contain LineString/MultiLineString geometry.");
            return null;
        }

        // Check if this network_link is actually a 3D / multi-level vertical transition!
        if (TryPlanVerticalNetworkLink(
                layer,
                feature,
                polylines,
                levelLookup,
                transform,
                registry,
                preserveMap,
                options,
                warnings,
                appDataId,
                out ShowcaseElementPlan? verticalPlan))
        {
            return verticalPlan;
        }

        // Otherwise build elevated horizontal 3D ribbons + optional directional chevrons.
        double? firstZ = polylines[0].Zs?.FirstOrDefault(z => z.HasValue);
        ResolvedElevation floorElev = ResolvePointElevation(
            feature,
            firstZ,
            levelLookup,
            options.DefaultFloorHeightMeters,
            warnings,
            appDataId);

        double ribbonBaseMeters = floorElev.ElevationMeters + options.NetworkRibbonElevationOffsetMeters;
        double ribbonBaseFeet = transform.ToRevitInternalZFeet(ribbonBaseMeters);
        double ribbonHeightFeet = ShowcaseCoordinateTransform.MetersToFeet(options.NetworkRibbonThicknessMeters);

        List<ShowcaseExtrusionSolid> ribbonSolids = new();
        List<ShowcaseExtrusionSolid> chevronSolids = new();
        int direction = ResolveLinkDirection(feature.Attributes);

        double chevronBaseFeet = transform.ToRevitInternalZFeet(
            ribbonBaseMeters + options.NetworkRibbonThicknessMeters + options.ChevronElevationGapMeters);
        double chevronHeightFeet = ShowcaseCoordinateTransform.MetersToFeet(options.ChevronHeightMeters);

        foreach ((IReadOnlyList<Point2D> coords, _) in polylines)
        {
            IReadOnlyList<Point2D> cleaned = CleanPlanarPolyline(coords, 1e-4d);
            double lengthMeters = ComputePolylineLength2D(cleaned);
            if (cleaned.Count < 2 || lengthMeters < options.MinNetworkSegmentMeters)
            {
                warnings.Add(
                    $"Skipped degenerate network_link segment in '{appDataId}' (length {lengthMeters:F4} m < {options.MinNetworkSegmentMeters:F2} m).");
                continue;
            }

            AppendBufferedRibbonSolids(
                ribbonSolids,
                cleaned,
                options.NetworkRibbonWidthMeters,
                ribbonBaseFeet,
                ribbonHeightFeet,
                transform,
                options.MinSegmentLengthFeet);

            if (direction != 0 && lengthMeters >= options.MinChevronLinkLengthMeters)
            {
                IReadOnlyList<Point2D> oriented = direction > 0 ? cleaned : cleaned.Reverse().ToArray();
                AppendDirectionalChevrons(
                    chevronSolids,
                    oriented,
                    lengthMeters,
                    chevronBaseFeet,
                    chevronHeightFeet,
                    transform,
                    options);
            }
        }

        if (ribbonSolids.Count == 0)
        {
            warnings.Add($"Skipped '{appDataId}' because no valid non-degenerate network ribbon solid could be planned.");
            return null;
        }

        chevronCount = chevronSolids.Count;
        string materialName = ShowcaseMaterialCatalog.ResolveNetworkLinkMaterialName(feature.Attributes, floorElev.Provenance);
        (IReadOnlyList<ShowcaseParameterAssignment> assignments, string? overflowJson) =
            BindWithProvenance(registry, layer, feature, preserveMap, floorElev.Provenance);

        string elementName = $"{NetworkRibbonNamePrefix}_{ShowcaseGeometryPlanner.SanitizeElementKey(feature.Key)}";

        return new ShowcaseElementPlan(
            layer.Kind,
            layer.Name,
            feature.Key,
            ShowcaseExportGuard.ShowcaseApplicationId,
            appDataId,
            elementName,
            preferredCategoryName: "OST_GenericModel",
            feature.LevelId,
            feature.FloorKey,
            isNonOccludingOutlineBand: false,
            solids: ribbonSolids,
            parameterAssignments: assignments,
            overflowAttributesJson: overflowJson,
            elementKind: ShowcaseElementKind.NetworkRibbon,
            materialName: materialName,
            accentMaterialName: chevronSolids.Count > 0 ? ShowcaseMaterialCatalog.NetChevron : null,
            geometryProvenance: floorElev.Provenance,
            accentSolids: chevronSolids);
    }

    private static bool TryPlanVerticalNetworkLink(
        ShowcaseLayer layer,
        ShowcaseFeature feature,
        IReadOnlyList<(IReadOnlyList<Point2D> Coords, IReadOnlyList<double?>? Zs)> polylines,
        LevelLookup levelLookup,
        ShowcaseCoordinateTransform transform,
        ShowcaseParameterRegistry registry,
        IReadOnlyDictionary<string, bool> preserveMap,
        ShowcasePoiAndNetworkPlanOptions options,
        List<string> warnings,
        string appDataId,
        out ShowcaseElementPlan? plan)
    {
        plan = null;

        // Case A: Explicit surveyed 3D Z coordinates spanning >= VerticalTransitionMinDeltaMeters.
        bool hasSurveyed3D = false;
        double minZ = double.PositiveInfinity;
        double maxZ = double.NegativeInfinity;
        foreach ((_, IReadOnlyList<double?>? zs) in polylines)
        {
            if (zs is null)
            {
                continue;
            }

            for (int i = 0; i < zs.Count; i++)
            {
                if (zs[i].HasValue && IsFinite(zs[i]!.Value))
                {
                    double z = zs[i]!.Value;
                    if (z < minZ) minZ = z;
                    if (z > maxZ) maxZ = z;
                }
            }
        }

        if (!double.IsPositiveInfinity(minZ) && (maxZ - minZ) >= options.VerticalTransitionMinDeltaMeters)
        {
            hasSurveyed3D = true;
        }

        // Case B: Distinct start_level / end_level attributes on a 2D network_link.
        string? startLevelToken = ReadAttributeString(feature.Attributes, "start_level", "from_level", "level_from", "start_level_id", "start_floor");
        string? endLevelToken = ReadAttributeString(feature.Attributes, "end_level", "to_level", "level_to", "end_level_id", "end_floor");

        bool hasLevelSpan = false;
        double startLevelElevMeters = 0d;
        double endLevelElevMeters = 0d;
        if (!hasSurveyed3D && !string.IsNullOrWhiteSpace(startLevelToken) && !string.IsNullOrWhiteSpace(endLevelToken))
        {
            ResolvedElevation startElev = ResolveExplicitLevelToken(startLevelToken!, levelLookup, options.DefaultFloorHeightMeters, warnings, appDataId, "start_level");
            ResolvedElevation endElev = ResolveExplicitLevelToken(endLevelToken!, levelLookup, options.DefaultFloorHeightMeters, warnings, appDataId, "end_level");
            if (Math.Abs(endElev.ElevationMeters - startElev.ElevationMeters) >= options.VerticalTransitionMinDeltaMeters)
            {
                hasLevelSpan = true;
                startLevelElevMeters = startElev.ElevationMeters + options.NetworkRibbonElevationOffsetMeters;
                endLevelElevMeters = endElev.ElevationMeters + options.NetworkRibbonElevationOffsetMeters;
            }
        }

        if (!hasSurveyed3D && !hasLevelSpan)
        {
            return false;
        }

        ShowcaseGeometryProvenance provenance = hasSurveyed3D
            ? ShowcaseGeometryProvenance.Surveyed
            : ShowcaseGeometryProvenance.Schematic;

        if (provenance == ShowcaseGeometryProvenance.Schematic)
        {
            warnings.Add(
                $"Vertical network_link '{appDataId}' has schematic elevation provenance (interpolated from start_level '{startLevelToken}' to end_level '{endLevelToken}').");
        }

        double tubeRadiusFeet = ShowcaseCoordinateTransform.MetersToFeet(
            provenance == ShowcaseGeometryProvenance.Schematic
                ? options.SchematicVerticalTubeRadiusMeters
                : options.VerticalTubeRadiusMeters);
        int sides = Math.Max(3, options.VerticalTubeSides);

        List<ShowcaseSweptTubeSegment> segments = new();
        foreach ((IReadOnlyList<Point2D> coords, IReadOnlyList<double?>? zs) in polylines)
        {
            if (coords.Count < 2)
            {
                continue;
            }

            if (hasSurveyed3D && zs != null)
            {
                double fallbackZ = minZ;
                for (int i = 0; i < coords.Count - 1; i++)
                {
                    double z0 = (zs[i] ?? fallbackZ) + options.NetworkRibbonElevationOffsetMeters;
                    double z1 = (zs[i + 1] ?? z0 - options.NetworkRibbonElevationOffsetMeters) + options.NetworkRibbonElevationOffsetMeters;
                    fallbackZ = zs[i + 1] ?? fallbackZ;

                    ShowcasePoint3D p0 = transform.ToRevitInternal(coords[i].X, coords[i].Y, z0);
                    ShowcasePoint3D p1 = transform.ToRevitInternal(coords[i + 1].X, coords[i + 1].Y, z1);
                    if (Distance3D(p0, p1) >= options.MinSegmentLengthFeet)
                    {
                        segments.Add(new ShowcaseSweptTubeSegment(p0, p1, tubeRadiusFeet, sides));
                    }
                }
            }
            else
            {
                AppendInterpolatedVerticalTubeSegments(
                    segments,
                    coords,
                    startLevelElevMeters,
                    endLevelElevMeters,
                    tubeRadiusFeet,
                    sides,
                    transform,
                    options.MinSegmentLengthFeet);
            }
        }

        if (segments.Count == 0)
        {
            warnings.Add($"Skipped vertical network_link '{appDataId}' because all 3D tube segments were below minimum length.");
            return true;
        }

        string materialName = ShowcaseMaterialCatalog.ResolveVerticalTubeMaterialName(feature.Attributes, provenance);
        (IReadOnlyList<ShowcaseParameterAssignment> assignments, string? overflowJson) =
            BindWithProvenance(registry, layer, feature, preserveMap, provenance);

        string elementName = $"{VerticalTubeNamePrefix}_{ShowcaseGeometryPlanner.SanitizeElementKey(feature.Key)}";
        plan = new ShowcaseElementPlan(
            layer.Kind,
            layer.Name,
            feature.Key,
            ShowcaseExportGuard.ShowcaseApplicationId,
            appDataId,
            elementName,
            preferredCategoryName: "OST_GenericModel",
            feature.LevelId,
            feature.FloorKey,
            isNonOccludingOutlineBand: false,
            solids: Array.Empty<ShowcaseExtrusionSolid>(),
            parameterAssignments: assignments,
            overflowAttributesJson: overflowJson,
            elementKind: ShowcaseElementKind.VerticalTube,
            materialName: materialName,
            geometryProvenance: provenance,
            tubeSegments: segments);
        return true;
    }

    private static ShowcaseElementPlan? PlanNetworkConnectorFeature(
        ShowcaseLayer layer,
        ShowcaseFeature feature,
        LevelLookup levelLookup,
        ShowcaseCoordinateTransform transform,
        ShowcaseParameterRegistry registry,
        IReadOnlyDictionary<string, bool> preserveMap,
        ShowcasePoiAndNetworkPlanOptions options,
        List<string> warnings)
    {
        string appDataId = ShowcaseGeometryPlanner.BuildApplicationDataId(layer.Name, feature.Key);

        if (feature.Geometry is not ShowcaseConnector3DGeometry connector)
        {
            // Fallback: if a connector layer carries a 3D LineString, route through vertical link planning.
            List<(IReadOnlyList<Point2D> Coords, IReadOnlyList<double?>? Zs)> polylines = ExtractPolylines(feature.Geometry);
            if (polylines.Count > 0 && TryPlanVerticalNetworkLink(
                    layer,
                    feature,
                    polylines,
                    levelLookup,
                    transform,
                    registry,
                    preserveMap,
                    options,
                    warnings,
                    appDataId,
                    out ShowcaseElementPlan? fallbackPlan))
            {
                return fallbackPlan;
            }

            warnings.Add($"Skipped '{appDataId}' because network_connector feature lacks valid connector3D geometry.");
            return null;
        }

        ResolvedElevation fromElev = ResolveConnectorEndpointElevation(connector.From, levelLookup, options.DefaultFloorHeightMeters, warnings, appDataId, "from");
        ResolvedElevation toElev = ResolveConnectorEndpointElevation(connector.To, levelLookup, options.DefaultFloorHeightMeters, warnings, appDataId, "to");

        bool isSchematic =
            connector.GeometryOrigin == ShowcaseConnectorGeometryOrigin.Schematic ||
            fromElev.Provenance == ShowcaseGeometryProvenance.Schematic ||
            toElev.Provenance == ShowcaseGeometryProvenance.Schematic;

        ShowcaseGeometryProvenance provenance = isSchematic
            ? ShowcaseGeometryProvenance.Schematic
            : ShowcaseGeometryProvenance.Surveyed;

        if (provenance == ShowcaseGeometryProvenance.Schematic)
        {
            string reason = connector.GeometryOrigin == ShowcaseConnectorGeometryOrigin.Schematic
                ? "connector geometryOrigin is 'schematic'"
                : "endpoint elevation was synthesized from estimated/missing level data";
            warnings.Add($"Connector '{appDataId}' recorded as schematic provenance ({reason}).");
        }

        double zStartMeters = fromElev.ElevationMeters + options.NetworkRibbonElevationOffsetMeters;
        double zEndMeters = toElev.ElevationMeters + options.NetworkRibbonElevationOffsetMeters;

        double tubeRadiusFeet = ShowcaseCoordinateTransform.MetersToFeet(
            provenance == ShowcaseGeometryProvenance.Schematic
                ? options.SchematicVerticalTubeRadiusMeters
                : options.VerticalTubeRadiusMeters);
        int sides = Math.Max(3, options.VerticalTubeSides);

        List<ShowcaseSweptTubeSegment> segments = new();
        IReadOnlyList<Point2D> path = connector.Path.Count >= 2
            ? connector.Path
            : new[] { connector.From.Xy, connector.To.Xy };

        AppendInterpolatedVerticalTubeSegments(
            segments,
            path,
            zStartMeters,
            zEndMeters,
            tubeRadiusFeet,
            sides,
            transform,
            options.MinSegmentLengthFeet);

        if (segments.Count == 0)
        {
            warnings.Add($"Skipped connector '{appDataId}' because its 3D path is shorter than minimum segment tolerance.");
            return null;
        }

        string materialName = ShowcaseMaterialCatalog.ResolveVerticalTubeMaterialName(feature.Attributes, provenance);
        (IReadOnlyList<ShowcaseParameterAssignment> assignments, string? overflowJson) =
            BindWithProvenance(registry, layer, feature, preserveMap, provenance);

        string elementName = $"{VerticalTubeNamePrefix}_{ShowcaseGeometryPlanner.SanitizeElementKey(feature.Key)}";

        return new ShowcaseElementPlan(
            layer.Kind,
            layer.Name,
            feature.Key,
            ShowcaseExportGuard.ShowcaseApplicationId,
            appDataId,
            elementName,
            preferredCategoryName: "OST_GenericModel",
            feature.LevelId ?? connector.From.LevelId,
            feature.FloorKey ?? connector.From.FloorKey,
            isNonOccludingOutlineBand: false,
            solids: Array.Empty<ShowcaseExtrusionSolid>(),
            parameterAssignments: assignments,
            overflowAttributesJson: overflowJson,
            elementKind: ShowcaseElementKind.VerticalTube,
            materialName: materialName,
            geometryProvenance: provenance,
            tubeSegments: segments);
    }

    private static ShowcaseElementPlan? PlanNetworkNodeOrFindingFeature(
        ShowcaseLayer layer,
        ShowcaseFeature feature,
        LevelLookup levelLookup,
        ShowcaseCoordinateTransform transform,
        ShowcaseParameterRegistry registry,
        IReadOnlyDictionary<string, bool> preserveMap,
        ShowcasePoiAndNetworkPlanOptions options,
        List<string> warnings)
    {
        string appDataId = ShowcaseGeometryPlanner.BuildApplicationDataId(layer.Name, feature.Key);
        IReadOnlyList<ShowcasePointGeometry> points = ExtractPoints(feature.Geometry);
        if (points.Count == 0)
        {
            warnings.Add($"Skipped '{appDataId}' because network_node feature does not have valid Point/MultiPoint geometry.");
            return null;
        }

        bool isFinding = IsFindingNode(layer.Name, feature);
        ShowcaseMarkerShapeKind shapeKind = isFinding
            ? ShowcaseMarkerShapeKind.FindingPuck
            : ShowcaseMarkerShapeKind.NetworkNode;
        double elevOffsetMeters = isFinding
            ? options.FindingPuckElevationOffsetMeters
            : options.NetworkNodeElevationOffsetMeters;
        double radiusFeet = ShowcaseCoordinateTransform.MetersToFeet(
            isFinding ? options.FindingPuckRadiusMeters : options.NetworkNodeRadiusMeters);
        double heightFeet = ShowcaseCoordinateTransform.MetersToFeet(
            isFinding ? options.FindingPuckHeightMeters : options.NetworkNodeHeightMeters);

        List<ShowcaseMarkerPlacement> placements = new(points.Count);
        ShowcaseGeometryProvenance provenance = ShowcaseGeometryProvenance.Surveyed;

        for (int i = 0; i < points.Count; i++)
        {
            ShowcasePointGeometry pt = points[i];
            if (!IsFinite(pt.Xy.X) || !IsFinite(pt.Xy.Y))
            {
                continue;
            }

            ResolvedElevation elev = ResolvePointElevation(
                feature,
                pt.Z,
                levelLookup,
                options.DefaultFloorHeightMeters,
                warnings,
                appDataId);
            if (elev.Provenance == ShowcaseGeometryProvenance.Schematic)
            {
                provenance = ShowcaseGeometryProvenance.Schematic;
            }

            ShowcasePoint3D locFeet = transform.ToRevitInternal(pt.Xy.X, pt.Xy.Y, elev.ElevationMeters + elevOffsetMeters);
            placements.Add(new ShowcaseMarkerPlacement(locFeet, shapeKind, radiusFeet, heightFeet));
        }

        if (placements.Count == 0)
        {
            warnings.Add($"Skipped '{appDataId}' because no finite node coordinates were found.");
            return null;
        }

        string materialName = isFinding
            ? ShowcaseMaterialCatalog.ResolveFindingMaterialName(feature.Attributes)
            : ShowcaseMaterialCatalog.NetNode;

        (IReadOnlyList<ShowcaseParameterAssignment> assignments, string? overflowJson) =
            BindWithProvenance(registry, layer, feature, preserveMap, provenance);

        string elementName = isFinding
            ? $"{FindingPuckNamePrefix}_{ShowcaseGeometryPlanner.SanitizeElementKey(feature.Key)}"
            : $"{NetworkNodeNamePrefix}_{ShowcaseGeometryPlanner.SanitizeElementKey(feature.Key)}";

        return new ShowcaseElementPlan(
            layer.Kind,
            layer.Name,
            feature.Key,
            ShowcaseExportGuard.ShowcaseApplicationId,
            appDataId,
            elementName,
            preferredCategoryName: "OST_GenericModel",
            feature.LevelId,
            feature.FloorKey,
            isNonOccludingOutlineBand: false,
            solids: Array.Empty<ShowcaseExtrusionSolid>(),
            parameterAssignments: assignments,
            overflowAttributesJson: overflowJson,
            elementKind: isFinding ? ShowcaseElementKind.FindingPuck : ShowcaseElementKind.NetworkNode,
            materialName: materialName,
            geometryProvenance: provenance,
            markerPlacements: placements);
    }

    private static (IReadOnlyList<ShowcaseParameterAssignment> Assignments, string? OverflowJson) BindWithProvenance(
        ShowcaseParameterRegistry registry,
        ShowcaseLayer layer,
        ShowcaseFeature feature,
        IReadOnlyDictionary<string, bool> preserveMap,
        ShowcaseGeometryProvenance provenance)
    {
        (IReadOnlyList<ShowcaseParameterAssignment> baseAssignments, string? overflowJson) =
            registry.BindFeatureAttributes(layer, feature, preserveMap);

        ShowcaseParameterDefinition? provenanceDef = registry.FindByName(ShowcaseParameterRegistry.GeometryProvenanceParameterName);
        if (provenanceDef is null)
        {
            return (baseAssignments, overflowJson);
        }

        List<ShowcaseParameterAssignment> combined = new(baseAssignments.Count + 1);
        combined.AddRange(baseAssignments);
        combined.Add(ShowcaseParameterAssignment.ForText(provenanceDef, ShowcaseMaterialCatalog.ToProvenanceString(provenance)));
        return (combined, overflowJson);
    }

    private static void AppendInterpolatedVerticalTubeSegments(
        List<ShowcaseSweptTubeSegment> segments,
        IReadOnlyList<Point2D> path2D,
        double zStartMeters,
        double zEndMeters,
        double tubeRadiusFeet,
        int sides,
        ShowcaseCoordinateTransform transform,
        double minSegmentLengthFeet)
    {
        if (path2D.Count < 2)
        {
            return;
        }

        double total2D = ComputePolylineLength2D(path2D);
        if (total2D < 1e-6d)
        {
            // Pure vertical shaft (e.g. elevator where From.Xy == To.Xy): emit one clean vertical segment!
            ShowcasePoint3D p0 = transform.ToRevitInternal(path2D[0].X, path2D[0].Y, zStartMeters);
            ShowcasePoint3D p1 = transform.ToRevitInternal(path2D[path2D.Count - 1].X, path2D[path2D.Count - 1].Y, zEndMeters);
            if (Distance3D(p0, p1) >= minSegmentLengthFeet)
            {
                segments.Add(new ShowcaseSweptTubeSegment(p0, p1, tubeRadiusFeet, sides));
            }

            return;
        }

        double cumulative = 0d;
        for (int i = 0; i < path2D.Count - 1; i++)
        {
            Point2D a = path2D[i];
            Point2D b = path2D[i + 1];
            double segLen2D = Distance2D(a, b);
            double t0 = cumulative / total2D;
            double t1 = (cumulative + segLen2D) / total2D;
            cumulative += segLen2D;

            double z0 = zStartMeters + ((zEndMeters - zStartMeters) * t0);
            double z1 = zStartMeters + ((zEndMeters - zStartMeters) * t1);

            ShowcasePoint3D p0 = transform.ToRevitInternal(a.X, a.Y, z0);
            ShowcasePoint3D p1 = transform.ToRevitInternal(b.X, b.Y, z1);
            if (Distance3D(p0, p1) >= minSegmentLengthFeet)
            {
                segments.Add(new ShowcaseSweptTubeSegment(p0, p1, tubeRadiusFeet, sides));
            }
        }
    }

    private static void AppendBufferedRibbonSolids(
        List<ShowcaseExtrusionSolid> solids,
        IReadOnlyList<Point2D> coordsMeters,
        double widthMeters,
        double baseFeet,
        double heightFeet,
        ShowcaseCoordinateTransform transform,
        double minSegmentLengthFeet)
    {
        if (coordsMeters.Count < 2 || widthMeters <= 0d || heightFeet <= minSegmentLengthFeet)
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
        NtsGeometry buffered = lineString.Buffer(widthMeters * 0.5d, bufferParams);
        AppendNtsPolygons(solids, buffered, baseFeet, heightFeet, transform, minSegmentLengthFeet);
    }

    private static void AppendDirectionalChevrons(
        List<ShowcaseExtrusionSolid> chevronSolids,
        IReadOnlyList<Point2D> orientedPolylineMeters,
        double totalLengthMeters,
        double chevronBaseFeet,
        double chevronHeightFeet,
        ShowcaseCoordinateTransform transform,
        ShowcasePoiAndNetworkPlanOptions options)
    {
        if (chevronHeightFeet <= options.MinSegmentLengthFeet || totalLengthMeters < options.MinChevronLinkLengthMeters)
        {
            return;
        }

        double spacing = Math.Max(options.ChevronSpacingMeters, options.ChevronLengthMeters * 1.5d);
        int count = Math.Max(1, (int)Math.Floor(totalLengthMeters / spacing));
        if (count > options.MaxChevronsPerLink)
        {
            count = options.MaxChevronsPerLink;
        }

        double step = totalLengthMeters / (count + 1);
        for (int i = 1; i <= count; i++)
        {
            double targetDist = step * i;
            if (!TrySamplePolyline(orientedPolylineMeters, targetDist, out Point2D center, out Point2D unitTangent))
            {
                continue;
            }

            Point2D leftNormal = new(-unitTangent.Y, unitTangent.X);
            double halfLenFront = options.ChevronLengthMeters * 0.55d;
            double halfLenBack = options.ChevronLengthMeters * 0.45d;
            double notchBack = options.ChevronLengthMeters * 0.15d;
            double halfWidth = options.ChevronHalfWidthMeters;

            // 4-vertex dart chevron (tip -> rearLeft -> notch -> rearRight), strictly simple & 12 triangles.
            Point2D tip = new(center.X + (unitTangent.X * halfLenFront), center.Y + (unitTangent.Y * halfLenFront));
            Point2D rearLeft = new(
                center.X - (unitTangent.X * halfLenBack) + (leftNormal.X * halfWidth),
                center.Y - (unitTangent.Y * halfLenBack) + (leftNormal.Y * halfWidth));
            Point2D notch = new(center.X - (unitTangent.X * notchBack), center.Y - (unitTangent.Y * notchBack));
            Point2D rearRight = new(
                center.X - (unitTangent.X * halfLenBack) - (leftNormal.X * halfWidth),
                center.Y - (unitTangent.Y * halfLenBack) - (leftNormal.Y * halfWidth));

            IReadOnlyList<Point2D>? ringFeet = ShowcaseGeometryPlanner.TransformAndNormalizeRing(
                new[] { tip, rearLeft, notch, rearRight, tip },
                transform,
                wantCounterClockwise: true,
                options.MinSegmentLengthFeet);
            if (ringFeet != null)
            {
                chevronSolids.Add(new ShowcaseExtrusionSolid(
                    ringFeet,
                    Array.Empty<IReadOnlyList<Point2D>>(),
                    chevronBaseFeet,
                    chevronHeightFeet));
            }
        }
    }

    private static bool TrySamplePolyline(
        IReadOnlyList<Point2D> polyline,
        double targetDistance,
        out Point2D position,
        out Point2D unitTangent)
    {
        double accumulated = 0d;
        for (int i = 0; i < polyline.Count - 1; i++)
        {
            Point2D a = polyline[i];
            Point2D b = polyline[i + 1];
            double segLen = Distance2D(a, b);
            if (segLen <= 1e-9d)
            {
                continue;
            }

            if (accumulated + segLen >= targetDistance || i == polyline.Count - 2)
            {
                double t = Math.Max(0d, Math.Min(1d, (targetDistance - accumulated) / segLen));
                position = new Point2D(a.X + ((b.X - a.X) * t), a.Y + ((b.Y - a.Y) * t));
                unitTangent = new Point2D((b.X - a.X) / segLen, (b.Y - a.Y) / segLen);
                return true;
            }

            accumulated += segLen;
        }

        position = default;
        unitTangent = default;
        return false;
    }

    private static void AppendNtsPolygons(
        List<ShowcaseExtrusionSolid> solids,
        NtsGeometry ntsGeometry,
        double baseFeet,
        double heightFeet,
        ShowcaseCoordinateTransform transform,
        double minSegmentLengthFeet)
    {
        if (ntsGeometry == null || ntsGeometry.IsEmpty)
        {
            return;
        }

        if (ntsGeometry is Polygon polygon)
        {
            if (polygon.IsEmpty || polygon.ExteriorRing.NumPoints < 4)
            {
                return;
            }

            IReadOnlyList<Point2D>? outerFeet = ShowcaseGeometryPlanner.TransformAndNormalizeRing(
                polygon.ExteriorRing.Coordinates.Select(c => new Point2D(c.X, c.Y)).ToArray(),
                transform,
                wantCounterClockwise: true,
                minSegmentLengthFeet);
            if (outerFeet == null)
            {
                return;
            }

            List<IReadOnlyList<Point2D>> holesFeet = new(polygon.NumInteriorRings);
            for (int i = 0; i < polygon.NumInteriorRings; i++)
            {
                LineString hole = polygon.GetInteriorRingN(i);
                if (hole.NumPoints < 4)
                {
                    continue;
                }

                IReadOnlyList<Point2D>? holeFeet = ShowcaseGeometryPlanner.TransformAndNormalizeRing(
                    hole.Coordinates.Select(c => new Point2D(c.X, c.Y)).ToArray(),
                    transform,
                    wantCounterClockwise: false,
                    minSegmentLengthFeet);
                if (holeFeet != null)
                {
                    holesFeet.Add(holeFeet);
                }
            }

            solids.Add(new ShowcaseExtrusionSolid(outerFeet, holesFeet, baseFeet, heightFeet));
            return;
        }

        if (ntsGeometry is GeometryCollection collection)
        {
            for (int i = 0; i < collection.NumGeometries; i++)
            {
                AppendNtsPolygons(solids, collection.GetGeometryN(i), baseFeet, heightFeet, transform, minSegmentLengthFeet);
            }
        }
    }

    private static IReadOnlyList<ShowcasePointGeometry> ExtractPoints(ShowcaseGeometry geometry)
    {
        switch (geometry)
        {
            case ShowcasePointGeometry pt:
                return new[] { pt };
            case ShowcaseMultiPointGeometry mpt:
                return mpt.Points;
            default:
                return Array.Empty<ShowcasePointGeometry>();
        }
    }

    private static List<(IReadOnlyList<Point2D> Coords, IReadOnlyList<double?>? Zs)> ExtractPolylines(ShowcaseGeometry geometry)
    {
        List<(IReadOnlyList<Point2D>, IReadOnlyList<double?>?)> result = new();
        switch (geometry)
        {
            case ShowcaseLineStringGeometry line:
                result.Add((line.Coords, line.Zs));
                break;
            case ShowcaseMultiLineStringGeometry multi:
                for (int i = 0; i < multi.Lines.Count; i++)
                {
                    IReadOnlyList<double?>? zs = multi.LineZs != null && i < multi.LineZs.Count
                        ? multi.LineZs[i]
                        : null;
                    result.Add((multi.Lines[i], zs));
                }

                break;
        }

        return result;
    }

    private static IReadOnlyList<Point2D> CleanPlanarPolyline(IReadOnlyList<Point2D> coords, double minStepMeters)
    {
        List<Point2D> cleaned = new(coords.Count);
        for (int i = 0; i < coords.Count; i++)
        {
            Point2D pt = coords[i];
            if (!IsFinite(pt.X) || !IsFinite(pt.Y))
            {
                continue;
            }

            if (cleaned.Count == 0 || Distance2D(cleaned[cleaned.Count - 1], pt) >= minStepMeters)
            {
                cleaned.Add(pt);
            }
        }

        return cleaned;
    }

    private static ResolvedElevation ResolvePointElevation(
        ShowcaseFeature feature,
        double? explicitPointZ,
        LevelLookup lookup,
        double defaultFloorHeightMeters,
        List<string>? warnings,
        string? featureLabel)
    {
        if (explicitPointZ.HasValue && IsFinite(explicitPointZ.Value))
        {
            return new ResolvedElevation(explicitPointZ.Value, ShowcaseGeometryProvenance.Surveyed);
        }

        ShowcaseLevel? level = FindLevel(lookup, feature.LevelId, feature.FloorKey);
        if (level == null)
        {
            string? attrLevel = ReadAttributeString(feature.Attributes, "level_id", "floor_key", "floor", "level");
            if (!string.IsNullOrWhiteSpace(attrLevel))
            {
                level = FindLevel(lookup, attrLevel, attrLevel);
            }
        }

        if (level != null)
        {
            return ResolveFromShowcaseLevel(level, lookup, defaultFloorHeightMeters, warnings, featureLabel);
        }

        if (warnings != null && !string.IsNullOrEmpty(featureLabel))
        {
            warnings.Add(
                $"Feature '{featureLabel}' has missing or unresolved levelId/floorKey ('{feature.LevelId ?? ""}' / '{feature.FloorKey ?? ""}') and no Z coordinate; falling back to 0.0 m elevation (schematic).");
        }

        return new ResolvedElevation(0d, ShowcaseGeometryProvenance.Schematic);
    }

    private static ResolvedElevation ResolveConnectorEndpointElevation(
        ShowcaseEndpoint endpoint,
        LevelLookup lookup,
        double defaultFloorHeightMeters,
        List<string> warnings,
        string featureLabel,
        string endName)
    {
        if (endpoint.TpM.HasValue && IsFinite(endpoint.TpM.Value))
        {
            return new ResolvedElevation(endpoint.TpM.Value, ShowcaseGeometryProvenance.Surveyed);
        }

        ShowcaseLevel? level = FindLevel(lookup, endpoint.LevelId, endpoint.FloorKey);
        if (level != null)
        {
            return ResolveFromShowcaseLevel(level, lookup, defaultFloorHeightMeters, warnings, $"{featureLabel}.{endName}");
        }

        warnings.Add(
            $"Connector '{featureLabel}' ({endName} endpoint) has no tpM and unresolved level '{endpoint.LevelId ?? endpoint.FloorKey ?? "(none)"}'; falling back to 0.0 m elevation (schematic).");
        return new ResolvedElevation(0d, ShowcaseGeometryProvenance.Schematic);
    }

    private static ResolvedElevation ResolveExplicitLevelToken(
        string token,
        LevelLookup lookup,
        double defaultFloorHeightMeters,
        List<string> warnings,
        string featureLabel,
        string attributeName)
    {
        ShowcaseLevel? level = FindLevel(lookup, token, token);
        if (level != null)
        {
            return ResolveFromShowcaseLevel(level, lookup, defaultFloorHeightMeters, warnings, $"{featureLabel}.{attributeName}");
        }

        warnings.Add(
            $"Feature '{featureLabel}' {attributeName} '{token}' did not match any snapshot level; falling back to 0.0 m elevation (schematic).");
        return new ResolvedElevation(0d, ShowcaseGeometryProvenance.Schematic);
    }

    private static ResolvedElevation ResolveFromShowcaseLevel(
        ShowcaseLevel level,
        LevelLookup lookup,
        double defaultFloorHeightMeters,
        List<string>? warnings,
        string? contextLabel)
    {
        if (level.TpM.HasValue && IsFinite(level.TpM.Value))
        {
            ShowcaseGeometryProvenance prov = level.ElevationSource == ShowcaseElevationSource.Estimated
                ? ShowcaseGeometryProvenance.Schematic
                : ShowcaseGeometryProvenance.Surveyed;
            return new ResolvedElevation(level.TpM.Value, prov);
        }

        double synthesized;
        if (lookup.KnownAnchors.Count > 0)
        {
            ShowcaseLevel bestAnchor = lookup.KnownAnchors[0];
            decimal bestDistance = Math.Abs((decimal)level.Ordinal - bestAnchor.Ordinal);
            for (int i = 1; i < lookup.KnownAnchors.Count; i++)
            {
                ShowcaseLevel candidate = lookup.KnownAnchors[i];
                decimal distance = Math.Abs((decimal)level.Ordinal - candidate.Ordinal);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestAnchor = candidate;
                }
            }

            synthesized = bestAnchor.TpM!.Value + ((level.Ordinal - bestAnchor.Ordinal) * defaultFloorHeightMeters);
        }
        else
        {
            synthesized = level.Ordinal * defaultFloorHeightMeters;
        }

        if (warnings != null && !string.IsNullOrEmpty(contextLabel))
        {
            warnings.Add(
                $"Feature '{contextLabel}' level '{level.LevelId}' has no tpM; synthesized {synthesized.ToString("F2", CultureInfo.InvariantCulture)} m from ordinal {level.Ordinal} (schematic).");
        }

        return new ResolvedElevation(synthesized, ShowcaseGeometryProvenance.Schematic);
    }

    private static LevelLookup BuildLevelLookup(IReadOnlyList<ShowcaseLevel> levels)
    {
        Dictionary<string, ShowcaseLevel> byId = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ShowcaseLevel> byFloorKey = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ShowcaseLevel> byShortName = new(StringComparer.OrdinalIgnoreCase);
        List<ShowcaseLevel> anchors = new();

        foreach (ShowcaseLevel level in levels)
        {
            if (!byId.ContainsKey(level.LevelId))
            {
                byId[level.LevelId] = level;
            }

            if (!byFloorKey.ContainsKey(level.FloorKey))
            {
                byFloorKey[level.FloorKey] = level;
            }

            if (!string.IsNullOrWhiteSpace(level.ShortName) && !byShortName.ContainsKey(level.ShortName))
            {
                byShortName[level.ShortName] = level;
            }

            if (level.TpM.HasValue && IsFinite(level.TpM.Value))
            {
                anchors.Add(level);
            }
        }

        anchors.Sort((a, b) =>
        {
            int cmp = a.Ordinal.CompareTo(b.Ordinal);
            return cmp != 0 ? cmp : string.CompareOrdinal(a.LevelId, b.LevelId);
        });

        return new LevelLookup(byId, byFloorKey, byShortName, anchors);
    }

    private static ShowcaseLevel? FindLevel(LevelLookup lookup, string? levelId, string? floorKey)
    {
        if (!string.IsNullOrWhiteSpace(levelId))
        {
            string trimmed = levelId!.Trim();
            if (lookup.ById.TryGetValue(trimmed, out ShowcaseLevel? byId))
            {
                return byId;
            }

            if (lookup.ByFloorKey.TryGetValue(trimmed, out ShowcaseLevel? byKey))
            {
                return byKey;
            }

            if (lookup.ByShortName.TryGetValue(trimmed, out ShowcaseLevel? byShort))
            {
                return byShort;
            }
        }

        if (!string.IsNullOrWhiteSpace(floorKey))
        {
            string trimmed = floorKey!.Trim();
            if (lookup.ByFloorKey.TryGetValue(trimmed, out ShowcaseLevel? byKey))
            {
                return byKey;
            }

            if (lookup.ByShortName.TryGetValue(trimmed, out ShowcaseLevel? byShort))
            {
                return byShort;
            }

            if (lookup.ById.TryGetValue(trimmed, out ShowcaseLevel? byId))
            {
                return byId;
            }
        }

        return null;
    }

    private static string? ReadAttributeString(IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (attributes.TryGetValue(key, out ShowcaseAttributeValue? val) && val != null && !val.IsNull)
            {
                string? text = val.ToExactString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text!.Trim();
                }
            }
        }

        return null;
    }

    private static bool TryReadBoolean(IReadOnlyDictionary<string, ShowcaseAttributeValue> attributes, string key, out bool result)
    {
        result = false;
        if (!attributes.TryGetValue(key, out ShowcaseAttributeValue? val) || val is null || val.IsNull)
        {
            return false;
        }

        if (val.Kind == ShowcaseAttributeKind.Boolean)
        {
            result = val.BoolValue!.Value;
            return true;
        }

        if (val.Kind == ShowcaseAttributeKind.Integer)
        {
            result = val.Int64Value!.Value != 0;
            return true;
        }

        if (val.Kind == ShowcaseAttributeKind.Text)
        {
            string t = (val.TextValue ?? string.Empty).Trim().ToLowerInvariant();
            if (t is "true" or "yes" or "1")
            {
                result = true;
                return true;
            }

            if (t is "false" or "no" or "0")
            {
                result = false;
                return true;
            }
        }

        return false;
    }

    private static double ComputePolylineLength2D(IReadOnlyList<Point2D> coords)
    {
        double total = 0d;
        for (int i = 0; i < coords.Count - 1; i++)
        {
            total += Distance2D(coords[i], coords[i + 1]);
        }

        return total;
    }

    private static double Distance2D(Point2D a, Point2D b)
    {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static double Distance3D(ShowcasePoint3D a, ShowcasePoint3D b)
    {
        double dx = b.XFeet - a.XFeet;
        double dy = b.YFeet - a.YFeet;
        double dz = b.ZFeet - a.ZFeet;
        return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private readonly struct ResolvedElevation
    {
        public ResolvedElevation(double elevationMeters, ShowcaseGeometryProvenance provenance)
        {
            ElevationMeters = elevationMeters;
            Provenance = provenance;
        }

        public double ElevationMeters { get; }

        public ShowcaseGeometryProvenance Provenance { get; }
    }

    private sealed class LevelLookup
    {
        public LevelLookup(
            Dictionary<string, ShowcaseLevel> byId,
            Dictionary<string, ShowcaseLevel> byFloorKey,
            Dictionary<string, ShowcaseLevel> byShortName,
            List<ShowcaseLevel> knownAnchors)
        {
            ById = byId;
            ByFloorKey = byFloorKey;
            ByShortName = byShortName;
            KnownAnchors = knownAnchors;
        }

        public Dictionary<string, ShowcaseLevel> ById { get; }

        public Dictionary<string, ShowcaseLevel> ByFloorKey { get; }

        public Dictionary<string, ShowcaseLevel> ByShortName { get; }

        public List<ShowcaseLevel> KnownAnchors { get; }
    }

    private sealed class PlannedPoiIndexEntry
    {
        public PlannedPoiIndexEntry(string applicationDataId, string symbolId, ShowcasePoint3D locationFeet)
        {
            ApplicationDataId = applicationDataId;
            SymbolId = symbolId;
            LocationFeet = locationFeet;
        }

        public string ApplicationDataId { get; }

        public string SymbolId { get; }

        public ShowcasePoint3D LocationFeet { get; }
    }
}
