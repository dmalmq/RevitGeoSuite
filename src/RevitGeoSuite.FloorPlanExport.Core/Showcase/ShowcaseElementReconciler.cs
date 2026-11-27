using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>Materialized Revit representation kind for a Forma Companion showcase element.</summary>
public enum ShowcaseElementRepresentationKind
{
    Unknown = 0,
    DirectShape = 1,
    FamilyInstance = 2,
}

/// <summary>
/// Revit-API-free summary of an existing <c>Kanizu.Showcase</c> element (<c>DirectShape</c> or
/// <c>KNZ_FRM_PoiMarker</c> <c>FamilyInstance</c>) found in a companion model, including its Revit
/// <c>UniqueId</c>, stored <c>ks_source_hash</c> content digest, representation kind, and the GUIDs of all
/// <c>ks_*</c> shared parameters currently populated (<c>HasValue == true</c>) on it.
/// </summary>
public sealed class ShowcaseExistingElementInfo
{
    public ShowcaseExistingElementInfo(
        long elementId,
        string applicationDataId,
        string categoryName,
        IEnumerable<Guid>? populatedKsParameterGuids = null,
        string? uniqueId = null,
        string? contentDigest = null,
        ShowcaseElementRepresentationKind representationKind = ShowcaseElementRepresentationKind.Unknown)
    {
        ElementId = elementId;
        ApplicationDataId = applicationDataId ?? string.Empty;
        CategoryName = categoryName ?? string.Empty;
        PopulatedKsParameterGuids = populatedKsParameterGuids == null
            ? Array.Empty<Guid>()
            : populatedKsParameterGuids.Distinct().ToArray();
        UniqueId = string.IsNullOrWhiteSpace(uniqueId) ? null : uniqueId!.Trim();
        ContentDigest = string.IsNullOrWhiteSpace(contentDigest) ? null : contentDigest!.Trim();
        RepresentationKind = representationKind;
    }

    public long ElementId { get; }

    public string ApplicationDataId { get; }

    /// <summary>Resolved Revit category token (e.g. <c>OST_Floors</c>, <c>OST_GenericModel</c>, or category ID string).</summary>
    public string CategoryName { get; }

    /// <summary>
    /// GUIDs of all <c>ks_*</c> shared parameters currently populated (<c>IsShared &amp;&amp; HasValue</c>) on the
    /// existing element (excluding the <c>ks_source_hash</c> tracking digest), enabling detection of attributes that
    /// became <c>null</c>, were omitted, were removed from the refreshed schema, or were retyped to a new UUIDv5 GUID.
    /// </summary>
    public IReadOnlyList<Guid> PopulatedKsParameterGuids { get; }

    /// <summary>Revit <c>Element.UniqueId</c> preserved across in-place refreshes so Autodesk Forma comments/issues stay attached.</summary>
    public string? UniqueId { get; }

    /// <summary>Stored <c>ks_source_hash</c> SHA-256 content digest from the previous showcase build (or <c>null</c> for pre-F5 elements).</summary>
    public string? ContentDigest { get; }

    /// <summary>Materialized Revit representation (<see cref="ShowcaseElementRepresentationKind.DirectShape"/> vs <see cref="ShowcaseElementRepresentationKind.FamilyInstance"/>).</summary>
    public ShowcaseElementRepresentationKind RepresentationKind { get; }
}

/// <summary>Reconciliation operation planned for a showcase element when refreshing a companion document.</summary>
public enum ShowcaseReconciliationActionKind
{
    Create,
    SkipUnchanged,
    UpdateInPlace,
    ReplaceCategoryChanged,
    ReplaceParametersCleared,
    ReplaceRepresentationChanged,
    DeleteStale,
}

/// <summary>Audit entry flagging an element whose Revit <c>UniqueId</c> changed during refresh (meaning attached Forma comments/issues moved).</summary>
public sealed class ShowcaseReplacedUniqueIdEntry
{
    public ShowcaseReplacedUniqueIdEntry(
        string applicationDataId,
        string? priorUniqueId,
        string? newUniqueId,
        ShowcaseReconciliationActionKind reason)
    {
        ApplicationDataId = applicationDataId ?? string.Empty;
        PriorUniqueId = string.IsNullOrWhiteSpace(priorUniqueId) ? null : priorUniqueId!.Trim();
        NewUniqueId = string.IsNullOrWhiteSpace(newUniqueId) ? null : newUniqueId!.Trim();
        Reason = reason;
    }

    public string ApplicationDataId { get; }

    public string? PriorUniqueId { get; }

    public string? NewUniqueId { get; }

    public ShowcaseReconciliationActionKind Reason { get; }
}

/// <summary>One deterministic reconciliation step produced by <see cref="ShowcaseElementReconciler"/>.</summary>
public sealed class ShowcaseReconciliationStep
{
    public ShowcaseReconciliationStep(
        ShowcaseReconciliationActionKind action,
        string applicationDataId,
        long? existingElementId,
        ShowcaseElementPlan? targetPlan,
        string? resolvedTargetCategoryName = null,
        IReadOnlyList<Guid>? clearedParameterGuids = null,
        string? existingUniqueId = null,
        string? matchedLegacyApplicationDataId = null,
        ShowcaseElementRepresentationKind resolvedTargetRepresentationKind = ShowcaseElementRepresentationKind.Unknown)
    {
        Action = action;
        ApplicationDataId = applicationDataId ?? string.Empty;
        ExistingElementId = existingElementId;
        TargetPlan = targetPlan;
        ResolvedTargetCategoryName = resolvedTargetCategoryName;
        ClearedParameterGuids = clearedParameterGuids ?? Array.Empty<Guid>();
        ExistingUniqueId = string.IsNullOrWhiteSpace(existingUniqueId) ? null : existingUniqueId!.Trim();
        MatchedLegacyApplicationDataId = string.IsNullOrWhiteSpace(matchedLegacyApplicationDataId)
            ? null
            : matchedLegacyApplicationDataId!.Trim();
        ResolvedTargetRepresentationKind = resolvedTargetRepresentationKind;
    }

    public ShowcaseReconciliationActionKind Action { get; }

    public string ApplicationDataId { get; }

    public long? ExistingElementId { get; }

    public ShowcaseElementPlan? TargetPlan { get; }

    public string? ResolvedTargetCategoryName { get; }

    /// <summary>
    /// Existing populated <c>ks_*</c> parameter GUIDs that are absent or <c>null</c> in the refreshed snapshot
    /// (causing <see cref="ShowcaseReconciliationActionKind.ReplaceParametersCleared"/> so fresh Revit null
    /// semantics are preserved instead of writing empty strings or zeros in-place).
    /// </summary>
    public IReadOnlyList<Guid> ClearedParameterGuids { get; }

    /// <summary>Revit <c>UniqueId</c> of the matched existing element, if known.</summary>
    public string? ExistingUniqueId { get; }

    /// <summary>Populated when an existing pre-F5 element was matched via its legacy unescaped <c>layer:key</c> ID and upgraded in-place.</summary>
    public string? MatchedLegacyApplicationDataId { get; }

    public ShowcaseElementRepresentationKind ResolvedTargetRepresentationKind { get; }
}

/// <summary>Complete reconciliation plan for refreshing a companion document without duplicating or leaking stale elements.</summary>
public sealed class ShowcaseReconciliationPlan
{
    public ShowcaseReconciliationPlan(
        IReadOnlyList<ShowcaseReconciliationStep> steps,
        int createCount,
        int updateInPlaceCount,
        int replaceCount,
        int deleteStaleCount,
        int unchangedCount = 0,
        int replaceCategoryChangedCount = 0,
        int replaceParametersClearedCount = 0,
        int replaceRepresentationChangedCount = 0,
        IReadOnlyList<string>? preservedUniqueIds = null,
        IReadOnlyList<ShowcaseReplacedUniqueIdEntry>? replacedUniqueIds = null)
    {
        Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        CreateCount = createCount;
        UnchangedCount = unchangedCount;
        UpdateInPlaceCount = updateInPlaceCount;
        ReplaceCount = replaceCount;
        ReplaceCategoryChangedCount = replaceCategoryChangedCount;
        ReplaceParametersClearedCount = replaceParametersClearedCount;
        ReplaceRepresentationChangedCount = replaceRepresentationChangedCount;
        DeleteStaleCount = deleteStaleCount;
        PreservedUniqueIds = preservedUniqueIds ?? Array.Empty<string>();
        ReplacedUniqueIds = replacedUniqueIds ?? Array.Empty<ShowcaseReplacedUniqueIdEntry>();
    }

    public IReadOnlyList<ShowcaseReconciliationStep> Steps { get; }

    public int CreateCount { get; }

    public int UnchangedCount { get; }

    public int UpdateInPlaceCount { get; }

    public int ReplaceCount { get; }

    public int ReplaceCategoryChangedCount { get; }

    public int ReplaceParametersClearedCount { get; }

    public int ReplaceRepresentationChangedCount { get; }

    public int DeleteStaleCount { get; }

    /// <summary>Count of surviving existing elements whose Revit <c>UniqueId</c> (and attached Forma comments) stayed intact.</summary>
    public int PreservedUniqueIdCount => UnchangedCount + UpdateInPlaceCount;

    /// <summary>Total count of existing matched elements surviving into the refreshed companion document.</summary>
    public int SurvivingElementCount => UnchangedCount + UpdateInPlaceCount + ReplaceCount;

    /// <summary>Fraction (<c>0.0..1.0</c>) of surviving elements whose Revit <c>UniqueId</c> stayed intact (<c>1.0</c> when no existing elements survive).</summary>
    public double UniqueIdPreservationRate =>
        SurvivingElementCount == 0 ? 1.0d : (double)PreservedUniqueIdCount / SurvivingElementCount;

    /// <summary>Revit <c>UniqueId</c>s explicitly preserved by <c>SkipUnchanged</c> and <c>UpdateInPlace</c> steps.</summary>
    public IReadOnlyList<string> PreservedUniqueIds { get; }

    /// <summary>Existing elements whose <c>UniqueId</c> will be replaced (flagging Forma comment displacement risk).</summary>
    public IReadOnlyList<ShowcaseReplacedUniqueIdEntry> ReplacedUniqueIds { get; }
}

/// <summary>
/// Companion refresh ledger and audit summary recording prior/new snapshot hashes, exact reconciliation diff counts,
/// and <c>UniqueId</c> preservation metrics for Autodesk Forma comment continuity.
/// </summary>
public sealed class ShowcaseRefreshSummary
{
    public ShowcaseRefreshSummary(
        string? priorSnapshotContentHash,
        string newSnapshotContentHash,
        int createdCount,
        int updatedInPlaceCount,
        int unchangedCount,
        int replacedCategoryChangedCount,
        int replacedParametersClearedCount,
        int replacedRepresentationChangedCount,
        int deletedStaleCount,
        int skippedCount,
        IReadOnlyList<string> preservedUniqueIds,
        IReadOnlyList<ShowcaseReplacedUniqueIdEntry> replacedUniqueIds)
    {
        PriorSnapshotContentHash = string.IsNullOrWhiteSpace(priorSnapshotContentHash)
            ? null
            : priorSnapshotContentHash!.Trim();
        NewSnapshotContentHash = newSnapshotContentHash ?? string.Empty;
        CreatedCount = createdCount;
        UpdatedInPlaceCount = updatedInPlaceCount;
        UnchangedCount = unchangedCount;
        ReplacedCategoryChangedCount = replacedCategoryChangedCount;
        ReplacedParametersClearedCount = replacedParametersClearedCount;
        ReplacedRepresentationChangedCount = replacedRepresentationChangedCount;
        DeletedStaleCount = deletedStaleCount;
        SkippedCount = skippedCount;
        PreservedUniqueIds = preservedUniqueIds ?? Array.Empty<string>();
        ReplacedUniqueIds = replacedUniqueIds ?? Array.Empty<ShowcaseReplacedUniqueIdEntry>();
    }

    public string? PriorSnapshotContentHash { get; }

    public string NewSnapshotContentHash { get; }

    public bool SnapshotContentHashUnchanged =>
        !string.IsNullOrEmpty(PriorSnapshotContentHash) &&
        string.Equals(PriorSnapshotContentHash, NewSnapshotContentHash, StringComparison.Ordinal);

    public int CreatedCount { get; }

    public int UpdatedInPlaceCount { get; }

    public int UnchangedCount { get; }

    public int ReplacedCategoryChangedCount { get; }

    public int ReplacedParametersClearedCount { get; }

    public int ReplacedRepresentationChangedCount { get; }

    public int ReplacedCount =>
        ReplacedCategoryChangedCount + ReplacedParametersClearedCount + ReplacedRepresentationChangedCount;

    public int DeletedStaleCount { get; }

    public int SkippedCount { get; }

    public int PreservedUniqueIdCount => UnchangedCount + UpdatedInPlaceCount;

    public int SurvivingElementCount => UnchangedCount + UpdatedInPlaceCount + ReplacedCount;

    public double UniqueIdPreservationRate =>
        SurvivingElementCount == 0 ? 1.0d : (double)PreservedUniqueIdCount / SurvivingElementCount;

    public IReadOnlyList<string> PreservedUniqueIds { get; }

    public IReadOnlyList<ShowcaseReplacedUniqueIdEntry> ReplacedUniqueIds { get; }

    /// <summary>True when at least one surviving element had to be recreated, which can displace Forma comments/issues.</summary>
    public bool HasFormaCommentDisplacementRisk => ReplacedCount > 0;

    /// <summary>True when the refresh was a complete no-op with zero mutations and 100% <c>UniqueId</c> preservation.</summary>
    public bool IsIdempotentNoOp =>
        CreatedCount == 0 &&
        UpdatedInPlaceCount == 0 &&
        ReplacedCount == 0 &&
        DeletedStaleCount == 0 &&
        SkippedCount == 0;

    public static ShowcaseRefreshSummary FromReconciliationPlan(
        string? priorSnapshotContentHash,
        string newSnapshotContentHash,
        ShowcaseReconciliationPlan plan,
        int skippedCount = 0)
    {
        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        return new ShowcaseRefreshSummary(
            priorSnapshotContentHash,
            newSnapshotContentHash,
            plan.CreateCount,
            plan.UpdateInPlaceCount,
            plan.UnchangedCount,
            plan.ReplaceCategoryChangedCount,
            plan.ReplaceParametersClearedCount,
            plan.ReplaceRepresentationChangedCount,
            plan.DeleteStaleCount,
            skippedCount,
            plan.PreservedUniqueIds,
            plan.ReplacedUniqueIds);
    }
}

/// <summary>
/// Pure-C# reconciliation planner matching target <see cref="ShowcaseElementPlan"/> entries against existing
/// <c>Kanizu.Showcase</c> elements by collision-free <c>ApplicationDataId</c>:
/// - Same key + same category + same representation + no cleared <c>ks_*</c> parameters + matching <c>ks_source_hash</c>
///   -&gt; <see cref="ShowcaseReconciliationActionKind.SkipUnchanged"/> (zero Revit writes, preserves <c>UniqueId</c> &amp; Forma comments)
/// - Same key + same category + same representation + no cleared <c>ks_*</c> parameters (content changed or pre-F5 digest absent)
///   -&gt; <see cref="ShowcaseReconciliationActionKind.UpdateInPlace"/> (preserves Revit <c>UniqueId</c> &amp; Forma comments)
/// - Same key + switched representation (<c>DirectShape</c> &lt;-&gt; <c>FamilyInstance</c>)
///   -&gt; <see cref="ShowcaseReconciliationActionKind.ReplaceRepresentationChanged"/>
/// - Same key + changed category -&gt; <see cref="ShowcaseReconciliationActionKind.ReplaceCategoryChanged"/>
/// - Same key + any populated <c>ks_*</c> parameter became <c>null</c>, omitted, removed from schema, or retyped
///   -&gt; <see cref="ShowcaseReconciliationActionKind.ReplaceParametersCleared"/>
/// - New key -&gt; <see cref="ShowcaseReconciliationActionKind.Create"/>
/// - Removed or duplicate existing key -&gt; <see cref="ShowcaseReconciliationActionKind.DeleteStale"/>
/// </summary>
public static class ShowcaseElementReconciler
{
    private static readonly Guid OverflowParameterGuid =
        ShowcaseParameterRegistry.ComputeParameterGuid("showcase", "attributes_json", ShowcaseParameterDataType.Text);

    private static readonly Guid SourceHashParameterGuid =
        ShowcaseParameterRegistry.SourceHashParameterGuid;

    /// <summary>
    /// Verifies that every planned element across F3 + F4 carries a globally unique, non-blank
    /// <see cref="ShowcaseElementPlan.ApplicationDataId"/> before mutating Revit.
    /// </summary>
    public static void ValidateUniqueApplicationDataIds(IReadOnlyList<ShowcaseElementPlan> targetPlans)
    {
        if (targetPlans is null)
        {
            throw new ArgumentNullException(nameof(targetPlans));
        }

        HashSet<string> seen = new(StringComparer.Ordinal);
        List<string> duplicates = new();
        for (int i = 0; i < targetPlans.Count; i++)
        {
            ShowcaseElementPlan plan = targetPlans[i];
            if (plan is null)
            {
                throw new ArgumentException($"Planned showcase element at index {i} is null.", nameof(targetPlans));
            }

            if (string.IsNullOrWhiteSpace(plan.ApplicationDataId))
            {
                throw new InvalidOperationException(
                    $"Planned showcase element '{plan.SourceLayerName}' / '{plan.FeatureKey}' has a blank ApplicationDataId.");
            }

            if (!seen.Add(plan.ApplicationDataId))
            {
                duplicates.Add(plan.ApplicationDataId);
            }
        }

        if (duplicates.Count > 0)
        {
            string sample = string.Join(", ", duplicates.Distinct(StringComparer.Ordinal).Take(5));
            throw new InvalidOperationException(
                $"Duplicate showcase ApplicationDataId(s) detected across planned elements ({duplicates.Count} collision(s)): {sample}. " +
                "Every showcase element must have a globally unique ApplicationDataId before mutating Revit.");
        }
    }

    public static ShowcaseReconciliationPlan BuildPlan(
        IReadOnlyList<ShowcaseElementPlan> targetPlans,
        IReadOnlyList<ShowcaseExistingElementInfo> existingShowcaseElements,
        Func<ShowcaseElementPlan, string>? resolveTargetCategoryName = null,
        Func<ShowcaseElementPlan, ShowcaseElementRepresentationKind>? resolveTargetRepresentationKind = null)
    {
        if (targetPlans is null)
        {
            throw new ArgumentNullException(nameof(targetPlans));
        }

        if (existingShowcaseElements is null)
        {
            throw new ArgumentNullException(nameof(existingShowcaseElements));
        }

        ValidateUniqueApplicationDataIds(targetPlans);

        Dictionary<string, ShowcaseExistingElementInfo> primaryByKey = new(StringComparer.Ordinal);
        List<ShowcaseExistingElementInfo> duplicateOrUnkeyedStale = new();

        foreach (ShowcaseExistingElementInfo existing in existingShowcaseElements)
        {
            if (existing is null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(existing.ApplicationDataId))
            {
                duplicateOrUnkeyedStale.Add(existing);
                continue;
            }

            if (!primaryByKey.ContainsKey(existing.ApplicationDataId))
            {
                primaryByKey[existing.ApplicationDataId] = existing;
            }
            else
            {
                // Duplicate legacy showcase element with the same ApplicationDataId -> prune cleanly.
                duplicateOrUnkeyedStale.Add(existing);
            }
        }

        // Count legacy unescaped IDs across target plans so legacy fallback matching is strictly unambiguous
        // (e.g. if both ("a:b", "c") and ("a", "b:c") exist simultaneously, neither steals the ambiguous legacy "a:b:c").
        Dictionary<string, int> legacyIdCounts = new(StringComparer.Ordinal);
        HashSet<string> canonicalTargetIds = new(StringComparer.Ordinal);
        foreach (ShowcaseElementPlan plan in targetPlans)
        {
            canonicalTargetIds.Add(plan.ApplicationDataId);
            string legacyId = ShowcaseGeometryPlanner.BuildLegacyApplicationDataId(plan.SourceLayerName, plan.FeatureKey);
            if (legacyIdCounts.TryGetValue(legacyId, out int count))
            {
                legacyIdCounts[legacyId] = count + 1;
            }
            else
            {
                legacyIdCounts[legacyId] = 1;
            }
        }

        List<ShowcaseReconciliationStep> steps = new(targetPlans.Count + existingShowcaseElements.Count);
        HashSet<string> matchedExistingKeys = new(StringComparer.Ordinal);
        List<string> preservedUniqueIds = new();
        List<ShowcaseReplacedUniqueIdEntry> replacedUniqueIds = new();

        int createCount = 0;
        int unchangedCount = 0;
        int updateInPlaceCount = 0;
        int replaceCategoryChangedCount = 0;
        int replaceParametersClearedCount = 0;
        int replaceRepresentationChangedCount = 0;
        int deleteStaleCount = 0;

        foreach (ShowcaseElementPlan plan in targetPlans)
        {
            string resolvedCategory = resolveTargetCategoryName != null
                ? resolveTargetCategoryName(plan)
                : plan.PreferredCategoryName;
            ShowcaseElementRepresentationKind resolvedRep = resolveTargetRepresentationKind != null
                ? resolveTargetRepresentationKind(plan)
                : ShowcaseElementRepresentationKind.Unknown;

            ShowcaseExistingElementInfo? existing = null;
            string? matchedLegacyId = null;

            if (primaryByKey.TryGetValue(plan.ApplicationDataId, out ShowcaseExistingElementInfo? directMatch) &&
                matchedExistingKeys.Add(directMatch.ApplicationDataId))
            {
                existing = directMatch;
            }
            else
            {
                string legacyId = ShowcaseGeometryPlanner.BuildLegacyApplicationDataId(plan.SourceLayerName, plan.FeatureKey);
                if (!string.Equals(legacyId, plan.ApplicationDataId, StringComparison.Ordinal) &&
                    !canonicalTargetIds.Contains(legacyId) &&
                    legacyIdCounts.TryGetValue(legacyId, out int legacyRefCount) &&
                    legacyRefCount == 1 &&
                    primaryByKey.TryGetValue(legacyId, out ShowcaseExistingElementInfo? legacyMatch) &&
                    matchedExistingKeys.Add(legacyMatch.ApplicationDataId))
                {
                    existing = legacyMatch;
                    matchedLegacyId = legacyId;
                }
            }

            if (existing != null)
            {
                bool representationChanged =
                    resolvedRep != ShowcaseElementRepresentationKind.Unknown &&
                    existing.RepresentationKind != ShowcaseElementRepresentationKind.Unknown &&
                    existing.RepresentationKind != resolvedRep;
                bool categoryMatches = string.Equals(existing.CategoryName, resolvedCategory, StringComparison.OrdinalIgnoreCase);
                IReadOnlyList<Guid> clearedExistingGuids = ComputeClearedExistingParameterGuids(existing, plan);

                if (representationChanged)
                {
                    steps.Add(new ShowcaseReconciliationStep(
                        ShowcaseReconciliationActionKind.ReplaceRepresentationChanged,
                        plan.ApplicationDataId,
                        existing.ElementId,
                        plan,
                        resolvedCategory,
                        clearedExistingGuids,
                        existing.UniqueId,
                        matchedLegacyId,
                        resolvedRep));
                    replaceRepresentationChangedCount++;
                    replacedUniqueIds.Add(new ShowcaseReplacedUniqueIdEntry(
                        plan.ApplicationDataId,
                        existing.UniqueId,
                        newUniqueId: null,
                        ShowcaseReconciliationActionKind.ReplaceRepresentationChanged));
                }
                else if (!categoryMatches)
                {
                    steps.Add(new ShowcaseReconciliationStep(
                        ShowcaseReconciliationActionKind.ReplaceCategoryChanged,
                        plan.ApplicationDataId,
                        existing.ElementId,
                        plan,
                        resolvedCategory,
                        clearedExistingGuids,
                        existing.UniqueId,
                        matchedLegacyId,
                        resolvedRep));
                    replaceCategoryChangedCount++;
                    replacedUniqueIds.Add(new ShowcaseReplacedUniqueIdEntry(
                        plan.ApplicationDataId,
                        existing.UniqueId,
                        newUniqueId: null,
                        ShowcaseReconciliationActionKind.ReplaceCategoryChanged));
                }
                else if (clearedExistingGuids.Count > 0)
                {
                    // Revit cannot unset Parameter.HasValue back to null in-place (writing "" / 0 / 0.0
                    // conflates refreshed nulls with deliberate empty/zero values and cannot remove values
                    // on schema-dropped/retyped GUIDs). Recreate cleanly to preserve exact fresh-build semantics.
                    steps.Add(new ShowcaseReconciliationStep(
                        ShowcaseReconciliationActionKind.ReplaceParametersCleared,
                        plan.ApplicationDataId,
                        existing.ElementId,
                        plan,
                        resolvedCategory,
                        clearedExistingGuids,
                        existing.UniqueId,
                        matchedLegacyId,
                        resolvedRep));
                    replaceParametersClearedCount++;
                    replacedUniqueIds.Add(new ShowcaseReplacedUniqueIdEntry(
                        plan.ApplicationDataId,
                        existing.UniqueId,
                        newUniqueId: null,
                        ShowcaseReconciliationActionKind.ReplaceParametersCleared));
                }
                else if (matchedLegacyId == null &&
                         !string.IsNullOrEmpty(existing.ContentDigest) &&
                         string.Equals(existing.ContentDigest, plan.ComputeContentDigest(), StringComparison.Ordinal))
                {
                    // Geometry, materials, category, representation, and all parameters match byte-for-byte -> no-op!
                    steps.Add(new ShowcaseReconciliationStep(
                        ShowcaseReconciliationActionKind.SkipUnchanged,
                        plan.ApplicationDataId,
                        existing.ElementId,
                        plan,
                        resolvedCategory,
                        clearedParameterGuids: null,
                        existing.UniqueId,
                        matchedLegacyApplicationDataId: null,
                        resolvedRep));
                    unchangedCount++;
                    if (!string.IsNullOrEmpty(existing.UniqueId))
                    {
                        preservedUniqueIds.Add(existing.UniqueId!);
                    }
                }
                else
                {
                    steps.Add(new ShowcaseReconciliationStep(
                        ShowcaseReconciliationActionKind.UpdateInPlace,
                        plan.ApplicationDataId,
                        existing.ElementId,
                        plan,
                        resolvedCategory,
                        clearedParameterGuids: null,
                        existing.UniqueId,
                        matchedLegacyId,
                        resolvedRep));
                    updateInPlaceCount++;
                    if (!string.IsNullOrEmpty(existing.UniqueId))
                    {
                        preservedUniqueIds.Add(existing.UniqueId!);
                    }
                }
            }
            else
            {
                steps.Add(new ShowcaseReconciliationStep(
                    ShowcaseReconciliationActionKind.Create,
                    plan.ApplicationDataId,
                    existingElementId: null,
                    plan,
                    resolvedCategory,
                    clearedParameterGuids: null,
                    existingUniqueId: null,
                    matchedLegacyApplicationDataId: null,
                    resolvedRep));
                createCount++;
            }
        }

        // Delete any existing Kanizu.Showcase elements whose ApplicationDataId is no longer in the snapshot,
        // plus any duplicate or unkeyed legacy showcase elements.
        foreach (ShowcaseExistingElementInfo stale in primaryByKey.Values
                     .Where(e => !matchedExistingKeys.Contains(e.ApplicationDataId))
                     .Concat(duplicateOrUnkeyedStale))
        {
            steps.Add(new ShowcaseReconciliationStep(
                ShowcaseReconciliationActionKind.DeleteStale,
                stale.ApplicationDataId,
                stale.ElementId,
                targetPlan: null,
                resolvedTargetCategoryName: null,
                clearedParameterGuids: null,
                stale.UniqueId));
            deleteStaleCount++;
        }

        int totalReplaceCount =
            replaceCategoryChangedCount +
            replaceParametersClearedCount +
            replaceRepresentationChangedCount;

        return new ShowcaseReconciliationPlan(
            steps,
            createCount,
            updateInPlaceCount,
            totalReplaceCount,
            deleteStaleCount,
            unchangedCount,
            replaceCategoryChangedCount,
            replaceParametersClearedCount,
            replaceRepresentationChangedCount,
            preservedUniqueIds,
            replacedUniqueIds);
    }

    /// <summary>
    /// Returns every <c>ks_*</c> parameter GUID currently populated on <paramref name="existing"/> that is
    /// absent, <c>null</c>, removed from the refreshed schema, or retyped in <paramref name="plan"/>.
    /// Excludes the internal <c>ks_source_hash</c> tracking digest parameter.
    /// </summary>
    public static IReadOnlyList<Guid> ComputeClearedExistingParameterGuids(
        ShowcaseExistingElementInfo existing,
        ShowcaseElementPlan plan)
    {
        if (existing is null)
        {
            throw new ArgumentNullException(nameof(existing));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        if (existing.PopulatedKsParameterGuids.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        HashSet<Guid> targetPopulatedGuids = CollectTargetPopulatedGuids(plan);
        List<Guid> cleared = new();
        foreach (Guid existingGuid in existing.PopulatedKsParameterGuids)
        {
            if (existingGuid == SourceHashParameterGuid)
            {
                continue;
            }

            if (!targetPopulatedGuids.Contains(existingGuid))
            {
                cleared.Add(existingGuid);
            }
        }

        return cleared;
    }

    /// <summary>
    /// Computes every parameter GUID that should not remain populated on an updated element, combining both
    /// <paramref name="registry"/> (<c>AllParameters</c>) and <paramref name="existingElement"/>'s actually
    /// populated <c>ks_*</c> shared parameter GUIDs (catching columns removed from the refreshed schema or retyped
    /// to a new UUIDv5 GUID).
    /// </summary>
    public static IReadOnlyList<Guid> GetClearedParameterGuids(
        ShowcaseParameterRegistry registry,
        ShowcaseElementPlan plan,
        ShowcaseExistingElementInfo? existingElement = null)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        HashSet<Guid> targetPopulatedGuids = CollectTargetPopulatedGuids(plan);
        HashSet<Guid> cleared = new();
        List<Guid> ordered = new();

        foreach (ShowcaseParameterDefinition def in registry.AllParameters)
        {
            if (!targetPopulatedGuids.Contains(def.Guid) && cleared.Add(def.Guid))
            {
                ordered.Add(def.Guid);
            }
        }

        if (existingElement != null)
        {
            foreach (Guid existingGuid in existingElement.PopulatedKsParameterGuids)
            {
                if (existingGuid == SourceHashParameterGuid)
                {
                    continue;
                }

                if (!targetPopulatedGuids.Contains(existingGuid) && cleared.Add(existingGuid))
                {
                    ordered.Add(existingGuid);
                }
            }
        }

        return ordered;
    }

    /// <summary>
    /// Computes which <paramref name="registry"/> parameter definitions are unset/null on <paramref name="plan"/>.
    /// Prefer <see cref="GetClearedParameterGuids"/> when reconciling against an existing element so schema-removed
    /// and retyped GUIDs are also detected.
    /// </summary>
    public static IReadOnlyList<ShowcaseParameterDefinition> GetParametersToClearOnUpdate(
        ShowcaseParameterRegistry registry,
        ShowcaseElementPlan plan)
    {
        if (registry is null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        if (plan is null)
        {
            throw new ArgumentNullException(nameof(plan));
        }

        HashSet<Guid> targetPopulatedGuids = CollectTargetPopulatedGuids(plan);
        List<ShowcaseParameterDefinition> toClear = new();
        foreach (ShowcaseParameterDefinition def in registry.AllParameters)
        {
            if (!targetPopulatedGuids.Contains(def.Guid))
            {
                toClear.Add(def);
            }
        }

        return toClear;
    }

    private static HashSet<Guid> CollectTargetPopulatedGuids(ShowcaseElementPlan plan)
    {
        HashSet<Guid> populated = new();
        foreach (ShowcaseParameterAssignment assignment in plan.ParameterAssignments)
        {
            if (!assignment.IsNull && !assignment.IsTextFallback)
            {
                populated.Add(assignment.Parameter.Guid);
            }
        }

        if (!string.IsNullOrEmpty(plan.OverflowAttributesJson))
        {
            populated.Add(OverflowParameterGuid);
        }

        return populated;
    }
}
