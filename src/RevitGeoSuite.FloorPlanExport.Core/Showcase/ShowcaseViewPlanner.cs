using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RevitGeoSuite.FloorPlanExport.Core.Models;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>Revit-API-free axis-aligned 3D bounding box in Revit internal coordinates (feet).</summary>
public readonly struct ShowcaseBoundingBox3D : IEquatable<ShowcaseBoundingBox3D>
{
    public ShowcaseBoundingBox3D(
        double minXFeet,
        double minYFeet,
        double minZFeet,
        double maxXFeet,
        double maxYFeet,
        double maxZFeet)
    {
        MinXFeet = minXFeet;
        MinYFeet = minYFeet;
        MinZFeet = minZFeet;
        MaxXFeet = maxXFeet;
        MaxYFeet = maxYFeet;
        MaxZFeet = maxZFeet;
    }

    public double MinXFeet { get; }

    public double MinYFeet { get; }

    public double MinZFeet { get; }

    public double MaxXFeet { get; }

    public double MaxYFeet { get; }

    public double MaxZFeet { get; }

    public double WidthFeet => MaxXFeet - MinXFeet;

    public double DepthFeet => MaxYFeet - MinYFeet;

    public double HeightFeet => MaxZFeet - MinZFeet;

    public bool IsValid =>
        IsFinite(MinXFeet) && IsFinite(MinYFeet) && IsFinite(MinZFeet) &&
        IsFinite(MaxXFeet) && IsFinite(MaxYFeet) && IsFinite(MaxZFeet) &&
        MaxXFeet > MinXFeet &&
        MaxYFeet > MinYFeet &&
        MaxZFeet > MinZFeet;

    public ShowcaseBoundingBox3D Expand(double xyMarginFeet, double bottomMarginFeet, double topMarginFeet) =>
        new(
            MinXFeet - xyMarginFeet,
            MinYFeet - xyMarginFeet,
            MinZFeet - bottomMarginFeet,
            MaxXFeet + xyMarginFeet,
            MaxYFeet + xyMarginFeet,
            MaxZFeet + topMarginFeet);

    public bool Equals(ShowcaseBoundingBox3D other) =>
        MinXFeet == other.MinXFeet &&
        MinYFeet == other.MinYFeet &&
        MinZFeet == other.MinZFeet &&
        MaxXFeet == other.MaxXFeet &&
        MaxYFeet == other.MaxYFeet &&
        MaxZFeet == other.MaxZFeet;

    public override bool Equals(object? obj) => obj is ShowcaseBoundingBox3D other && Equals(other);

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = MinXFeet.GetHashCode();
            hash = (hash * 397) ^ MinYFeet.GetHashCode();
            hash = (hash * 397) ^ MinZFeet.GetHashCode();
            hash = (hash * 397) ^ MaxXFeet.GetHashCode();
            hash = (hash * 397) ^ MaxYFeet.GetHashCode();
            hash = (hash * 397) ^ MaxZFeet.GetHashCode();
            return hash;
        }
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>Role of a planned Autodesk Forma Publish Settings 3D view.</summary>
public enum ShowcaseViewKind
{
    Overview = 0,
    NetworkAndPois = 1,
    PerFloor = 2,
}

/// <summary>Options controlling <see cref="ShowcaseViewPlanner"/> 3D view generation and section-box padding.</summary>
public sealed class ShowcaseViewPlanOptions
{
    public static ShowcaseViewPlanOptions Default { get; } = new();

    /// <summary>Revit Publish Settings <c>ViewSheetSet</c> name containing all generated Forma 3D views.</summary>
    public string PublishViewSetName { get; set; } = "Kanizu Forma Publish";

    /// <summary>Managed view name prefix owned by the showcase builder (stale views with this prefix are pruned on refresh).</summary>
    public string ManagedViewPrefix { get; set; } = "Forma - ";

    /// <summary>Full-station overview 3D view name.</summary>
    public string OverviewViewName { get; set; } = "Forma - 00 Overview";

    /// <summary>Thematic 3D overview highlighting network ribbons, vertical tubes, POI markers, and finding pucks.</summary>
    public string NetworkAndPoisViewName { get; set; } = "Forma - 01 Network & POIs";

    /// <summary>When <c>true</c> (default), includes the dedicated <see cref="NetworkAndPoisViewName"/> thematic 3D view.</summary>
    public bool IncludeNetworkAndPoisView { get; set; } = true;

    /// <summary>Horizontal XY section-box margin (metres) around full-station overview views.</summary>
    public double OverviewXyMarginMeters { get; set; } = 3.0d;

    /// <summary>Bottom Z margin (metres) below the lowest station geometry on overview views.</summary>
    public double OverviewBottomMarginMeters { get; set; } = 1.0d;

    /// <summary>Top Z margin (metres) above the highest station geometry on overview views.</summary>
    public double OverviewTopMarginMeters { get; set; } = 2.5d;

    /// <summary>Horizontal XY section-box margin (metres) around per-floor geometry.</summary>
    public double PerFloorXyMarginMeters { get; set; } = 2.0d;

    /// <summary>Downward Z pad (metres) below each floor's base elevation so floor slabs and level bands stay cleanly visible.</summary>
    public double PerFloorBottomPadMeters { get; set; } = 0.30d;

    /// <summary>Clearance (metres) below the next upper level's base elevation so upper floors never occlude the active floor.</summary>
    public double PerFloorTopClearanceMeters { get; set; } = 0.25d;

    /// <summary>Fallback floor-to-floor height (metres) used for the topmost level or when levels lack <c>tpM</c>.</summary>
    public double DefaultFloorHeightMeters { get; set; } = 4.5d;

    /// <summary>Minimum fallback XY half-extent (metres) when a snapshot contains zero geometric bounds.</summary>
    public double FallbackHalfExtentMeters { get; set; } = 10.0d;
}

/// <summary>Revit-API-free specification of one pre-configured Forma 3D view.</summary>
public sealed class ShowcaseView3DPlan
{
    public ShowcaseView3DPlan(
        string name,
        ShowcaseViewKind kind,
        ShowcaseBoundingBox3D sectionBoxFeet,
        IReadOnlyList<string> hiddenCategoryNames,
        IReadOnlyList<string> visibleCategoryNames,
        string? levelId = null,
        string? floorKey = null,
        long? ordinal = null,
        bool usedStationXyFallback = false)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Kind = kind;
        SectionBoxFeet = sectionBoxFeet;
        HiddenCategoryNames = hiddenCategoryNames ?? Array.Empty<string>();
        VisibleCategoryNames = visibleCategoryNames ?? Array.Empty<string>();
        LevelId = levelId;
        FloorKey = floorKey;
        Ordinal = ordinal;
        UsedStationXyFallback = usedStationXyFallback;
    }

    public string Name { get; }

    public ShowcaseViewKind Kind { get; }

    public ShowcaseBoundingBox3D SectionBoxFeet { get; }

    public IReadOnlyList<string> HiddenCategoryNames { get; }

    public IReadOnlyList<string> VisibleCategoryNames { get; }

    public string? LevelId { get; }

    public string? FloorKey { get; }

    public long? Ordinal { get; }

    /// <summary>True when a sparse per-floor view fell back to the overall station XY footprint.</summary>
    public bool UsedStationXyFallback { get; }
}

/// <summary>Complete Forma Publish Settings 3D view set plan computed by <see cref="ShowcaseViewPlanner"/>.</summary>
public sealed class FormaPublishViewSetPlan
{
    public FormaPublishViewSetPlan(
        string viewSetName,
        string managedViewPrefix,
        IReadOnlyList<ShowcaseView3DPlan> views,
        ShowcaseBoundingBox3D stationBoundsFeet,
        IReadOnlyList<string> warnings)
    {
        ViewSetName = viewSetName ?? throw new ArgumentNullException(nameof(viewSetName));
        ManagedViewPrefix = managedViewPrefix ?? throw new ArgumentNullException(nameof(managedViewPrefix));
        Views = views ?? throw new ArgumentNullException(nameof(views));
        StationBoundsFeet = stationBoundsFeet;
        Warnings = warnings ?? Array.Empty<string>();
    }

    public string ViewSetName { get; }

    public string ManagedViewPrefix { get; }

    public IReadOnlyList<ShowcaseView3DPlan> Views { get; }

    public ShowcaseBoundingBox3D StationBoundsFeet { get; }

    public IReadOnlyList<string> Warnings { get; }
}

/// <summary>Revit-API-free summary of an existing 3D view in the companion document.</summary>
public sealed class ShowcaseExistingViewInfo
{
    public ShowcaseExistingViewInfo(long elementId, string name, string? uniqueId = null, bool isTemplate = false)
    {
        ElementId = elementId;
        Name = name ?? string.Empty;
        UniqueId = string.IsNullOrWhiteSpace(uniqueId) ? null : uniqueId!.Trim();
        IsTemplate = isTemplate;
    }

    public long ElementId { get; }

    public string Name { get; }

    public string? UniqueId { get; }

    public bool IsTemplate { get; }
}

/// <summary>Reconciliation operation planned for a Forma 3D view during companion refresh.</summary>
public enum ShowcaseViewReconciliationActionKind
{
    Create,
    UpdateInPlace,
    DeleteStale,
}

/// <summary>One deterministic view reconciliation step produced by <see cref="ShowcaseViewPlanner.ReconcileViews"/>.</summary>
public sealed class ShowcaseViewReconciliationStep
{
    public ShowcaseViewReconciliationStep(
        ShowcaseViewReconciliationActionKind action,
        string viewName,
        long? existingViewId,
        string? existingUniqueId,
        ShowcaseView3DPlan? targetView)
    {
        Action = action;
        ViewName = viewName ?? string.Empty;
        ExistingViewId = existingViewId;
        ExistingUniqueId = existingUniqueId;
        TargetView = targetView;
    }

    public ShowcaseViewReconciliationActionKind Action { get; }

    public string ViewName { get; }

    public long? ExistingViewId { get; }

    public string? ExistingUniqueId { get; }

    public ShowcaseView3DPlan? TargetView { get; }
}

/// <summary>Reconciliation diff for refreshing Forma Publish Settings 3D views while preserving view <c>UniqueId</c>s.</summary>
public sealed class ShowcaseViewReconciliationPlan
{
    public ShowcaseViewReconciliationPlan(
        IReadOnlyList<ShowcaseViewReconciliationStep> steps,
        int createCount,
        int updateInPlaceCount,
        int deleteStaleCount)
    {
        Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        CreateCount = createCount;
        UpdateInPlaceCount = updateInPlaceCount;
        DeleteStaleCount = deleteStaleCount;
    }

    public IReadOnlyList<ShowcaseViewReconciliationStep> Steps { get; }

    public int CreateCount { get; }

    public int UpdateInPlaceCount { get; }

    public int DeleteStaleCount { get; }
}

/// <summary>
/// Pure-C# planner for Autodesk Forma / ACC Cloud Model Publish Settings 3D views:
/// <list type="number">
/// <item><c>Forma - 00 Overview</c>: full-station 3D view with clean XY/Z margin and clutter categories hidden</item>
/// <item><c>Forma - 01 Network &amp; POIs</c>: thematic 3D overview hiding floor/door/fixture slabs to spotlight network ribbons, tubes, POIs, and finding pucks</item>
/// <item>Per-floor section-box 3D views ordered deterministically by ordinal with monotonic non-overlapping Z slices and Revit-safe view names</item>
/// </list>
/// </summary>
public static class ShowcaseViewPlanner
{
    private static readonly char[] ForbiddenRevitViewNameChars =
    {
        '\\', ':', '{', '}', '[', ']', '|', ';', '<', '>', '?', '`', '~',
    };

    private static readonly string[] BaseClutterHiddenCategories =
    {
        "OST_Levels",
        "OST_Grids",
        "OST_CLines",
    };

    private static readonly string[] AllShowcaseVisibleCategories =
    {
        "OST_Floors",
        "OST_Doors",
        "OST_SpecialityEquipment",
        "OST_GenericModel",
    };

    private static readonly string[] NetworkFocusedHiddenCategories =
    {
        "OST_Levels",
        "OST_Grids",
        "OST_CLines",
        "OST_Floors",
        "OST_Doors",
        "OST_SpecialityEquipment",
    };

    private static readonly string[] NetworkFocusedVisibleCategories =
    {
        "OST_GenericModel",
    };

    /// <summary>Computes the complete deterministic <see cref="FormaPublishViewSetPlan"/> for a snapshot and its planned F3+F4 elements.</summary>
    public static FormaPublishViewSetPlan Plan(
        ShowcaseSnapshot snapshot,
        ShowcaseCoordinateTransform transform,
        IReadOnlyList<ShowcaseElementPlan> plannedElements,
        ShowcaseViewPlanOptions? options = null)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (transform is null)
        {
            throw new ArgumentNullException(nameof(transform));
        }

        if (plannedElements is null)
        {
            throw new ArgumentNullException(nameof(plannedElements));
        }

        ShowcaseViewPlanOptions effective = options ?? ShowcaseViewPlanOptions.Default;
        List<string> warnings = new();

        double defaultFloorHeightFeet = ShowcaseCoordinateTransform.MetersToFeet(Math.Max(1.0d, effective.DefaultFloorHeightMeters));
        double fallbackHalfExtentFeet = ShowcaseCoordinateTransform.MetersToFeet(Math.Max(1.0d, effective.FallbackHalfExtentMeters));

        // 1. Order levels deterministically by Ordinal ascending, then LevelId ordinal.
        List<ShowcaseLevel> orderedLevels = snapshot.Levels
            .OrderBy(l => l.Ordinal)
            .ThenBy(l => l.LevelId, StringComparer.Ordinal)
            .ToList();

        List<ShowcaseLevel> knownElevationAnchors = orderedLevels
            .Where(l => l.TpM.HasValue && IsFinite(l.TpM.Value))
            .ToList();

        Dictionary<string, double> levelBaseZFeetById = new(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < orderedLevels.Count; i++)
        {
            ShowcaseLevel level = orderedLevels[i];
            double elevMeters = ResolveLevelElevationMeters(level, knownElevationAnchors, effective.DefaultFloorHeightMeters);
            levelBaseZFeetById[level.LevelId] = transform.ToRevitInternalZFeet(elevMeters);
        }

        // 2. Accumulate overall station 3D bounds & per-level XY/Z bounds from planned elements.
        BoundsAccumulator stationBounds = new();
        BoundsAccumulator networkBounds = new();
        Dictionary<string, BoundsAccumulator> boundsByLevelId = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, BoundsAccumulator> boundsByFloorKey = new(StringComparer.OrdinalIgnoreCase);

        foreach (ShowcaseElementPlan element in plannedElements)
        {
            BoundsAccumulator elemBounds = ComputeElementBounds(element);
            if (!elemBounds.HasData)
            {
                continue;
            }

            stationBounds.Include(elemBounds);

            if (IsNetworkOrPoiViewElement(element))
            {
                networkBounds.Include(elemBounds);
            }

            // Exclude multi-level vertical tubes from distorting a single floor's tight Z/XY slice unless
            // we only attribute single-level elements to per-floor slices.
            if (element.ElementKind == ShowcaseElementKind.VerticalTube)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(element.LevelId))
            {
                string lid = element.LevelId!.Trim();
                if (!boundsByLevelId.TryGetValue(lid, out BoundsAccumulator? acc))
                {
                    acc = new BoundsAccumulator();
                    boundsByLevelId[lid] = acc;
                }

                acc.Include(elemBounds);
            }

            if (!string.IsNullOrWhiteSpace(element.FloorKey))
            {
                string fkey = element.FloorKey!.Trim();
                if (!boundsByFloorKey.TryGetValue(fkey, out BoundsAccumulator? acc))
                {
                    acc = new BoundsAccumulator();
                    boundsByFloorKey[fkey] = acc;
                }

                acc.Include(elemBounds);
            }
        }

        // Ensure station bounds also encompass level elevations if elements were sparse.
        if (orderedLevels.Count > 0)
        {
            double lowestLevelZ = levelBaseZFeetById[orderedLevels[0].LevelId];
            double highestLevelTopZ = levelBaseZFeetById[orderedLevels[orderedLevels.Count - 1].LevelId] + defaultFloorHeightFeet;
            if (!stationBounds.HasData)
            {
                stationBounds.IncludePoint(-fallbackHalfExtentFeet, -fallbackHalfExtentFeet, lowestLevelZ);
                stationBounds.IncludePoint(fallbackHalfExtentFeet, fallbackHalfExtentFeet, highestLevelTopZ);
            }
            else
            {
                stationBounds.IncludeZ(lowestLevelZ);
                stationBounds.IncludeZ(highestLevelTopZ);
            }
        }
        else if (!stationBounds.HasData)
        {
            stationBounds.IncludePoint(-fallbackHalfExtentFeet, -fallbackHalfExtentFeet, 0d);
            stationBounds.IncludePoint(fallbackHalfExtentFeet, fallbackHalfExtentFeet, defaultFloorHeightFeet);
        }

        ShowcaseBoundingBox3D rawStationBox = stationBounds.ToNonDegenerateBox(fallbackHalfExtentFeet, defaultFloorHeightFeet);

        double overviewXyMarginFeet = ShowcaseCoordinateTransform.MetersToFeet(Math.Max(0d, effective.OverviewXyMarginMeters));
        double overviewBottomMarginFeet = ShowcaseCoordinateTransform.MetersToFeet(Math.Max(0d, effective.OverviewBottomMarginMeters));
        double overviewTopMarginFeet = ShowcaseCoordinateTransform.MetersToFeet(Math.Max(0d, effective.OverviewTopMarginMeters));

        ShowcaseBoundingBox3D overviewBox = rawStationBox.Expand(overviewXyMarginFeet, overviewBottomMarginFeet, overviewTopMarginFeet);

        List<ShowcaseView3DPlan> views = new();
        HashSet<string> usedViewNames = new(StringComparer.OrdinalIgnoreCase);

        // 3. View 00: Full Station Overview
        string overviewName = EnsureUniqueViewName(SanitizeRevitViewName(effective.OverviewViewName), usedViewNames);
        views.Add(new ShowcaseView3DPlan(
            overviewName,
            ShowcaseViewKind.Overview,
            overviewBox,
            BaseClutterHiddenCategories,
            AllShowcaseVisibleCategories));

        // 4. View 01: Thematic Network & POIs Overview (optional, default enabled)
        if (effective.IncludeNetworkAndPoisView)
        {
            ShowcaseBoundingBox3D netSourceBox = networkBounds.HasData
                ? networkBounds.ToNonDegenerateBox(fallbackHalfExtentFeet, defaultFloorHeightFeet)
                : rawStationBox;
            ShowcaseBoundingBox3D netBox = netSourceBox.Expand(overviewXyMarginFeet, overviewBottomMarginFeet, overviewTopMarginFeet);
            string netViewName = EnsureUniqueViewName(SanitizeRevitViewName(effective.NetworkAndPoisViewName), usedViewNames);
            views.Add(new ShowcaseView3DPlan(
                netViewName,
                ShowcaseViewKind.NetworkAndPois,
                netBox,
                NetworkFocusedHiddenCategories,
                NetworkFocusedVisibleCategories));
        }

        // 5. Per-floor section-box 3D views ordered deterministically by ordinal.
        double perFloorXyMarginFeet = ShowcaseCoordinateTransform.MetersToFeet(Math.Max(0d, effective.PerFloorXyMarginMeters));
        double perFloorBottomPadFeet = ShowcaseCoordinateTransform.MetersToFeet(Math.Max(0d, effective.PerFloorBottomPadMeters));
        double perFloorTopClearanceFeet = ShowcaseCoordinateTransform.MetersToFeet(Math.Max(0d, effective.PerFloorTopClearanceMeters));
        double minSliceThicknessFeet = ShowcaseCoordinateTransform.MetersToFeet(0.25d);
        double minLevelSeparationFeet = ShowcaseCoordinateTransform.MetersToFeet(0.10d);

        // Compute distinct strictly ascending level Z elevations so adjacent floors never overlap.
        List<double> sortedDistinctBaseZs = orderedLevels
            .Select(l => levelBaseZFeetById[l.LevelId])
            .OrderBy(z => z)
            .ToList();

        for (int i = 0; i < orderedLevels.Count; i++)
        {
            ShowcaseLevel level = orderedLevels[i];
            double baseZFeet = levelBaseZFeetById[level.LevelId];

            // Find the next strictly higher level base Z above this level.
            double? nextBaseZFeet = null;
            for (int j = 0; j < sortedDistinctBaseZs.Count; j++)
            {
                if (sortedDistinctBaseZs[j] > baseZFeet + minLevelSeparationFeet)
                {
                    nextBaseZFeet = sortedDistinctBaseZs[j];
                    break;
                }
            }

            // Look up elements assigned to this floor (LevelId first, then FloorKey).
            BoundsAccumulator? floorAcc = null;
            if (boundsByLevelId.TryGetValue(level.LevelId, out BoundsAccumulator? byId) && byId.HasData)
            {
                floorAcc = byId;
            }
            else if (boundsByFloorKey.TryGetValue(level.FloorKey, out BoundsAccumulator? byKey) && byKey.HasData)
            {
                floorAcc = byKey;
            }

            bool usedXyFallback = floorAcc == null || !floorAcc.HasData;
            double minXFeet = usedXyFallback ? rawStationBox.MinXFeet : floorAcc!.MinX;
            double maxXFeet = usedXyFallback ? rawStationBox.MaxXFeet : floorAcc!.MaxX;
            double minYFeet = usedXyFallback ? rawStationBox.MinYFeet : floorAcc!.MinY;
            double maxYFeet = usedXyFallback ? rawStationBox.MaxYFeet : floorAcc!.MaxY;

            // Guard against single-point floors (zero XY width or depth).
            if (maxXFeet - minXFeet < 1e-3d)
            {
                minXFeet -= 1.0d;
                maxXFeet += 1.0d;
            }

            if (maxYFeet - minYFeet < 1e-3d)
            {
                minYFeet -= 1.0d;
                maxYFeet += 1.0d;
            }

            // Compute tight monotonic Z slice for this floor without occluding from upper floors.
            double minZFeet = baseZFeet - perFloorBottomPadFeet;
            double maxZFeet;
            if (nextBaseZFeet.HasValue)
            {
                double idealMaxZ = nextBaseZFeet.Value - perFloorTopClearanceFeet;
                if (idealMaxZ >= baseZFeet + minSliceThicknessFeet)
                {
                    maxZFeet = idealMaxZ;
                }
                else
                {
                    // Tightly spaced mezzanine: stop strictly below next floor's base while staying above this floor's base.
                    double span = nextBaseZFeet.Value - baseZFeet;
                    maxZFeet = baseZFeet + Math.Max(span * 0.85d, Math.Min(minSliceThicknessFeet, span * 0.95d));
                    if (minZFeet >= maxZFeet)
                    {
                        minZFeet = baseZFeet - Math.Min(perFloorBottomPadFeet, span * 0.10d);
                    }
                }
            }
            else
            {
                double defaultTopZ = baseZFeet + defaultFloorHeightFeet;
                double contentTopZ = floorAcc != null && floorAcc.HasData
                    ? floorAcc.MaxZ + ShowcaseCoordinateTransform.MetersToFeet(0.50d)
                    : defaultTopZ;
                maxZFeet = Math.Max(defaultTopZ, contentTopZ);
            }

            ShowcaseBoundingBox3D floorBox = new(
                minXFeet - perFloorXyMarginFeet,
                minYFeet - perFloorXyMarginFeet,
                minZFeet,
                maxXFeet + perFloorXyMarginFeet,
                maxYFeet + perFloorXyMarginFeet,
                maxZFeet);

            string rawFloorViewName = BuildPerFloorRawViewName(effective.ManagedViewPrefix, level);
            string safeFloorViewName = EnsureUniqueViewName(SanitizeRevitViewName(rawFloorViewName), usedViewNames);

            views.Add(new ShowcaseView3DPlan(
                safeFloorViewName,
                ShowcaseViewKind.PerFloor,
                floorBox,
                BaseClutterHiddenCategories,
                AllShowcaseVisibleCategories,
                level.LevelId,
                level.FloorKey,
                level.Ordinal,
                usedXyFallback));
        }

        string safeSetName = SanitizeRevitViewName(effective.PublishViewSetName, fallback: "Kanizu Forma Publish");
        return new FormaPublishViewSetPlan(
            safeSetName,
            effective.ManagedViewPrefix ?? "Forma - ",
            views,
            rawStationBox,
            warnings);
    }

    /// <summary>
    /// Computes deterministic view reconciliation steps comparing <paramref name="plan"/> against existing 3D views:
    /// - Matching existing non-template view by name -&gt; <see cref="ShowcaseViewReconciliationActionKind.UpdateInPlace"/> (preserves view <c>UniqueId</c>)
    /// - Missing planned view -&gt; <see cref="ShowcaseViewReconciliationActionKind.Create"/>
    /// - Existing managed view starting with <see cref="FormaPublishViewSetPlan.ManagedViewPrefix"/> no longer in plan -&gt; <see cref="ShowcaseViewReconciliationActionKind.DeleteStale"/>
    /// Unmanaged views (not starting with <c>ManagedViewPrefix</c>) are never modified or deleted.
    /// </summary>
    public static ShowcaseViewReconciliationPlan ReconcileViews(
        FormaPublishViewSetPlan plan,
        IReadOnlyList<ShowcaseExistingViewInfo> existingViews)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (existingViews is null)
        {
            throw new ArgumentNullException(nameof(existingViews));
        }

        Dictionary<string, ShowcaseExistingViewInfo> existingByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (ShowcaseExistingViewInfo view in existingViews)
        {
            if (view is null || view.IsTemplate || string.IsNullOrWhiteSpace(view.Name))
            {
                continue;
            }

            if (!existingByName.ContainsKey(view.Name.Trim()))
            {
                existingByName[view.Name.Trim()] = view;
            }
        }

        List<ShowcaseViewReconciliationStep> steps = new();
        HashSet<string> plannedNames = new(StringComparer.OrdinalIgnoreCase);
        int createCount = 0;
        int updateInPlaceCount = 0;
        int deleteStaleCount = 0;

        foreach (ShowcaseView3DPlan targetView in plan.Views)
        {
            plannedNames.Add(targetView.Name);
            if (existingByName.TryGetValue(targetView.Name, out ShowcaseExistingViewInfo? existing))
            {
                steps.Add(new ShowcaseViewReconciliationStep(
                    ShowcaseViewReconciliationActionKind.UpdateInPlace,
                    targetView.Name,
                    existing.ElementId,
                    existing.UniqueId,
                    targetView));
                updateInPlaceCount++;
            }
            else
            {
                steps.Add(new ShowcaseViewReconciliationStep(
                    ShowcaseViewReconciliationActionKind.Create,
                    targetView.Name,
                    existingViewId: null,
                    existingUniqueId: null,
                    targetView));
                createCount++;
            }
        }

        if (!string.IsNullOrEmpty(plan.ManagedViewPrefix))
        {
            foreach (ShowcaseExistingViewInfo existing in existingByName.Values
                         .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase))
            {
                if (existing.Name.StartsWith(plan.ManagedViewPrefix, StringComparison.OrdinalIgnoreCase) &&
                    !plannedNames.Contains(existing.Name))
                {
                    steps.Add(new ShowcaseViewReconciliationStep(
                        ShowcaseViewReconciliationActionKind.DeleteStale,
                        existing.Name,
                        existing.ElementId,
                        existing.UniqueId,
                        targetView: null));
                    deleteStaleCount++;
                }
            }
        }

        return new ShowcaseViewReconciliationPlan(steps, createCount, updateInPlaceCount, deleteStaleCount);
    }

    /// <summary>
    /// Sanitizes a candidate view name against all Revit forbidden characters
    /// (<c>\ : { } [ ] | ; &lt; &gt; ? ` ~</c> and control characters), collapsing duplicate spaces cleanly.
    /// </summary>
    public static string SanitizeRevitViewName(string? rawName, string fallback = "Forma - View")
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return fallback;
        }

        StringBuilder sb = new(rawName!.Length);
        bool prevWasSpace = false;
        for (int i = 0; i < rawName.Length; i++)
        {
            char c = rawName[i];
            if (c < 32 || Array.IndexOf(ForbiddenRevitViewNameChars, c) >= 0)
            {
                c = '_';
            }

            if (char.IsWhiteSpace(c))
            {
                if (!prevWasSpace && sb.Length > 0)
                {
                    sb.Append(' ');
                    prevWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                prevWasSpace = false;
            }
        }

        string cleaned = sb.ToString().Trim();
        return cleaned.Length == 0 ? fallback : cleaned;
    }

    /// <summary>Returns <c>true</c> when <paramref name="viewName"/> contains zero Revit-forbidden characters.</summary>
    public static bool IsValidRevitViewName(string? viewName)
    {
        if (string.IsNullOrWhiteSpace(viewName))
        {
            return false;
        }

        for (int i = 0; i < viewName!.Length; i++)
        {
            char c = viewName[i];
            if (c < 32 || Array.IndexOf(ForbiddenRevitViewNameChars, c) >= 0)
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildPerFloorRawViewName(string managedPrefix, ShowcaseLevel level)
    {
        string prefix = string.IsNullOrEmpty(managedPrefix) ? "Forma - " : managedPrefix;
        string shortName = (level.ShortName ?? string.Empty).Trim();
        string floorKey = (level.FloorKey ?? string.Empty).Trim();
        string levelId = (level.LevelId ?? string.Empty).Trim();

        string primary = shortName.Length > 0
            ? shortName
            : floorKey.Length > 0
                ? floorKey
                : levelId.Length > 0
                    ? levelId
                    : $"L{level.Ordinal.ToString(CultureInfo.InvariantCulture)}";

        string secondary = floorKey.Length > 0 && !string.Equals(floorKey, primary, StringComparison.OrdinalIgnoreCase)
            ? floorKey
            : levelId.Length > 0 && !string.Equals(levelId, primary, StringComparison.OrdinalIgnoreCase)
                ? levelId
                : string.Empty;

        return secondary.Length > 0
            ? $"{prefix}{primary} ({secondary})"
            : $"{prefix}{primary}";
    }

    private static string EnsureUniqueViewName(string baseName, HashSet<string> usedNames)
    {
        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        int suffix = 2;
        while (true)
        {
            string candidate = $"{baseName} ({suffix.ToString(CultureInfo.InvariantCulture)})";
            if (usedNames.Add(candidate))
            {
                return candidate;
            }

            suffix++;
        }
    }

    private static bool IsNetworkOrPoiViewElement(ShowcaseElementPlan element) =>
        element.LayerKind == ShowcaseLayerKind.PointFacility ||
        element.LayerKind == ShowcaseLayerKind.PointFacilityNetwork ||
        element.LayerKind == ShowcaseLayerKind.NetworkLink ||
        element.LayerKind == ShowcaseLayerKind.NetworkConnector ||
        element.LayerKind == ShowcaseLayerKind.NetworkNode ||
        element.LayerKind == ShowcaseLayerKind.Level;

    private static double ResolveLevelElevationMeters(
        ShowcaseLevel level,
        IReadOnlyList<ShowcaseLevel> knownAnchors,
        double defaultFloorHeightMeters)
    {
        if (level.TpM.HasValue && IsFinite(level.TpM.Value))
        {
            return level.TpM.Value;
        }

        if (knownAnchors.Count > 0)
        {
            ShowcaseLevel bestAnchor = knownAnchors[0];
            decimal bestDistance = Math.Abs((decimal)level.Ordinal - bestAnchor.Ordinal);
            for (int i = 1; i < knownAnchors.Count; i++)
            {
                ShowcaseLevel candidate = knownAnchors[i];
                decimal distance = Math.Abs((decimal)level.Ordinal - candidate.Ordinal);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestAnchor = candidate;
                }
            }

            return bestAnchor.TpM!.Value + ((level.Ordinal - bestAnchor.Ordinal) * defaultFloorHeightMeters);
        }

        return level.Ordinal * defaultFloorHeightMeters;
    }

    private static BoundsAccumulator ComputeElementBounds(ShowcaseElementPlan element)
    {
        BoundsAccumulator acc = new();

        foreach (ShowcaseExtrusionSolid solid in element.Solids)
        {
            IncludeSolid(acc, solid);
        }

        foreach (ShowcaseExtrusionSolid accent in element.AccentSolids)
        {
            IncludeSolid(acc, accent);
        }

        foreach (ShowcaseMarkerPlacement marker in element.MarkerPlacements)
        {
            double r = Math.Max(0d, marker.BaseRadiusFeet);
            double h = Math.Max(0d, marker.HeightFeet);
            acc.IncludePoint(marker.LocationFeet.XFeet - r, marker.LocationFeet.YFeet - r, marker.LocationFeet.ZFeet);
            acc.IncludePoint(marker.LocationFeet.XFeet + r, marker.LocationFeet.YFeet + r, marker.LocationFeet.ZFeet + h);
        }

        foreach (ShowcaseSweptTubeSegment tube in element.TubeSegments)
        {
            double r = Math.Max(0d, tube.RadiusFeet);
            acc.IncludePoint(tube.StartFeet.XFeet - r, tube.StartFeet.YFeet - r, tube.StartFeet.ZFeet - r);
            acc.IncludePoint(tube.StartFeet.XFeet + r, tube.StartFeet.YFeet + r, tube.StartFeet.ZFeet + r);
            acc.IncludePoint(tube.EndFeet.XFeet - r, tube.EndFeet.YFeet - r, tube.EndFeet.ZFeet - r);
            acc.IncludePoint(tube.EndFeet.XFeet + r, tube.EndFeet.YFeet + r, tube.EndFeet.ZFeet + r);
        }

        return acc;
    }

    private static void IncludeSolid(BoundsAccumulator acc, ShowcaseExtrusionSolid solid)
    {
        double minZ = solid.BaseElevationFeet;
        double maxZ = solid.BaseElevationFeet + solid.HeightFeet;
        for (int i = 0; i < solid.OuterRingFeet.Count; i++)
        {
            Point2D pt = solid.OuterRingFeet[i];
            acc.IncludePoint(pt.X, pt.Y, minZ);
            acc.IncludePoint(pt.X, pt.Y, maxZ);
        }
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private sealed class BoundsAccumulator
    {
        public double MinX { get; private set; } = double.PositiveInfinity;
        public double MaxX { get; private set; } = double.NegativeInfinity;
        public double MinY { get; private set; } = double.PositiveInfinity;
        public double MaxY { get; private set; } = double.NegativeInfinity;
        public double MinZ { get; private set; } = double.PositiveInfinity;
        public double MaxZ { get; private set; } = double.NegativeInfinity;
        public bool HasData { get; private set; }

        public void IncludePoint(double x, double y, double z)
        {
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(z))
            {
                return;
            }

            if (x < MinX) MinX = x;
            if (x > MaxX) MaxX = x;
            if (y < MinY) MinY = y;
            if (y > MaxY) MaxY = y;
            if (z < MinZ) MinZ = z;
            if (z > MaxZ) MaxZ = z;
            HasData = true;
        }

        public void IncludeZ(double z)
        {
            if (!IsFinite(z))
            {
                return;
            }

            if (z < MinZ) MinZ = z;
            if (z > MaxZ) MaxZ = z;
        }

        public void Include(BoundsAccumulator other)
        {
            if (!other.HasData)
            {
                return;
            }

            IncludePoint(other.MinX, other.MinY, other.MinZ);
            IncludePoint(other.MaxX, other.MaxY, other.MaxZ);
        }

        public ShowcaseBoundingBox3D ToNonDegenerateBox(double fallbackHalfXyFeet, double fallbackHeightFeet)
        {
            if (!HasData)
            {
                return new ShowcaseBoundingBox3D(
                    -fallbackHalfXyFeet,
                    -fallbackHalfXyFeet,
                    0d,
                    fallbackHalfXyFeet,
                    fallbackHalfXyFeet,
                    fallbackHeightFeet);
            }

            double minX = MinX;
            double maxX = MaxX;
            double minY = MinY;
            double maxY = MaxY;
            double minZ = MinZ;
            double maxZ = MaxZ;

            if (maxX - minX < 1e-3d)
            {
                minX -= 1.0d;
                maxX += 1.0d;
            }

            if (maxY - minY < 1e-3d)
            {
                minY -= 1.0d;
                maxY += 1.0d;
            }

            if (maxZ - minZ < 1e-3d)
            {
                maxZ = minZ + fallbackHeightFeet;
            }

            return new ShowcaseBoundingBox3D(minX, minY, minZ, maxX, maxY, maxZ);
        }
    }
}
