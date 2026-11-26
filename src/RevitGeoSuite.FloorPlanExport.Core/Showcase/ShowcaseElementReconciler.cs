using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>
/// Revit-API-free summary of an existing <c>Kanizu.Showcase</c> DirectShape found in a companion model,
/// including the GUIDs of all <c>ks_*</c> shared parameters currently populated (<c>HasValue == true</c>) on it.
/// </summary>
public sealed class ShowcaseExistingElementInfo
{
    public ShowcaseExistingElementInfo(
        long elementId,
        string applicationDataId,
        string categoryName,
        IEnumerable<Guid>? populatedKsParameterGuids = null)
    {
        ElementId = elementId;
        ApplicationDataId = applicationDataId ?? string.Empty;
        CategoryName = categoryName ?? string.Empty;
        PopulatedKsParameterGuids = populatedKsParameterGuids == null
            ? Array.Empty<Guid>()
            : populatedKsParameterGuids.Distinct().ToArray();
    }

    public long ElementId { get; }

    public string ApplicationDataId { get; }

    /// <summary>Resolved Revit category token (e.g. <c>OST_Floors</c>, <c>OST_GenericModel</c>, or category ID string).</summary>
    public string CategoryName { get; }

    /// <summary>
    /// GUIDs of all <c>ks_*</c> shared parameters currently populated (<c>IsShared &amp;&amp; HasValue</c>) on the
    /// existing element, enabling detection of attributes that became <c>null</c>, were omitted, were removed from
    /// the refreshed schema, or were retyped to a new UUIDv5 GUID.
    /// </summary>
    public IReadOnlyList<Guid> PopulatedKsParameterGuids { get; }
}

/// <summary>Reconciliation operation planned for a showcase DirectShape when updating a companion document.</summary>
public enum ShowcaseReconciliationActionKind
{
    Create,
    UpdateInPlace,
    ReplaceCategoryChanged,
    ReplaceParametersCleared,
    DeleteStale,
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
        IReadOnlyList<Guid>? clearedParameterGuids = null)
    {
        Action = action;
        ApplicationDataId = applicationDataId ?? string.Empty;
        ExistingElementId = existingElementId;
        TargetPlan = targetPlan;
        ResolvedTargetCategoryName = resolvedTargetCategoryName;
        ClearedParameterGuids = clearedParameterGuids ?? Array.Empty<Guid>();
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
}

/// <summary>Complete reconciliation plan for refreshing a companion document without duplicating or leaking stale elements.</summary>
public sealed class ShowcaseReconciliationPlan
{
    public ShowcaseReconciliationPlan(
        IReadOnlyList<ShowcaseReconciliationStep> steps,
        int createCount,
        int updateInPlaceCount,
        int replaceCount,
        int deleteStaleCount)
    {
        Steps = steps ?? throw new ArgumentNullException(nameof(steps));
        CreateCount = createCount;
        UpdateInPlaceCount = updateInPlaceCount;
        ReplaceCount = replaceCount;
        DeleteStaleCount = deleteStaleCount;
    }

    public IReadOnlyList<ShowcaseReconciliationStep> Steps { get; }

    public int CreateCount { get; }

    public int UpdateInPlaceCount { get; }

    public int ReplaceCount { get; }

    public int DeleteStaleCount { get; }
}

/// <summary>
/// Pure-C# reconciliation planner matching target <see cref="ShowcaseElementPlan"/> entries against existing
/// <c>Kanizu.Showcase</c> DirectShapes by stable <c>ApplicationDataId</c>:
/// - Same key + same category + no populated <c>ks_*</c> parameters being cleared/removed/retyped
///   -&gt; <see cref="ShowcaseReconciliationActionKind.UpdateInPlace"/> (preserves Revit <c>UniqueId</c> &amp; Forma comments)
/// - Same key + same category + any populated <c>ks_*</c> parameter became <c>null</c>, omitted, removed from schema, or retyped
///   -&gt; <see cref="ShowcaseReconciliationActionKind.ReplaceParametersCleared"/> (recreates cleanly so cleared parameters remain genuinely unset in Revit)
/// - Same key + changed category -&gt; <see cref="ShowcaseReconciliationActionKind.ReplaceCategoryChanged"/>
/// - New key -&gt; <see cref="ShowcaseReconciliationActionKind.Create"/>
/// - Removed or duplicate existing key -&gt; <see cref="ShowcaseReconciliationActionKind.DeleteStale"/>
/// </summary>
public static class ShowcaseElementReconciler
{
    private static readonly Guid OverflowParameterGuid =
        ShowcaseParameterRegistry.ComputeParameterGuid("showcase", "attributes_json", ShowcaseParameterDataType.Text);

    public static ShowcaseReconciliationPlan BuildPlan(
        IReadOnlyList<ShowcaseElementPlan> targetPlans,
        IReadOnlyList<ShowcaseExistingElementInfo> existingShowcaseElements,
        Func<ShowcaseElementPlan, string>? resolveTargetCategoryName = null)
    {
        if (targetPlans is null)
        {
            throw new ArgumentNullException(nameof(targetPlans));
        }

        if (existingShowcaseElements is null)
        {
            throw new ArgumentNullException(nameof(existingShowcaseElements));
        }

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
                // Duplicate legacy showcase DirectShape with the same ApplicationDataId -> prune cleanly.
                duplicateOrUnkeyedStale.Add(existing);
            }
        }

        List<ShowcaseReconciliationStep> steps = new(targetPlans.Count + existingShowcaseElements.Count);
        HashSet<string> matchedKeys = new(StringComparer.Ordinal);
        int createCount = 0;
        int updateInPlaceCount = 0;
        int replaceCount = 0;
        int deleteStaleCount = 0;

        foreach (ShowcaseElementPlan plan in targetPlans)
        {
            string resolvedCategory = resolveTargetCategoryName != null
                ? resolveTargetCategoryName(plan)
                : plan.PreferredCategoryName;

            if (primaryByKey.TryGetValue(plan.ApplicationDataId, out ShowcaseExistingElementInfo? existing))
            {
                matchedKeys.Add(plan.ApplicationDataId);
                bool categoryMatches = string.Equals(existing.CategoryName, resolvedCategory, StringComparison.OrdinalIgnoreCase);
                IReadOnlyList<Guid> clearedExistingGuids = ComputeClearedExistingParameterGuids(existing, plan);

                if (!categoryMatches)
                {
                    steps.Add(new ShowcaseReconciliationStep(
                        ShowcaseReconciliationActionKind.ReplaceCategoryChanged,
                        plan.ApplicationDataId,
                        existing.ElementId,
                        plan,
                        resolvedCategory,
                        clearedExistingGuids));
                    replaceCount++;
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
                        clearedExistingGuids));
                    replaceCount++;
                }
                else
                {
                    steps.Add(new ShowcaseReconciliationStep(
                        ShowcaseReconciliationActionKind.UpdateInPlace,
                        plan.ApplicationDataId,
                        existing.ElementId,
                        plan,
                        resolvedCategory));
                    updateInPlaceCount++;
                }
            }
            else
            {
                steps.Add(new ShowcaseReconciliationStep(
                    ShowcaseReconciliationActionKind.Create,
                    plan.ApplicationDataId,
                    existingElementId: null,
                    plan,
                    resolvedCategory));
                createCount++;
            }
        }

        // Delete any existing Kanizu.Showcase elements whose ApplicationDataId is no longer in the snapshot,
        // plus any duplicate or unkeyed legacy showcase elements.
        foreach (ShowcaseExistingElementInfo stale in primaryByKey.Values
                     .Where(e => !matchedKeys.Contains(e.ApplicationDataId))
                     .Concat(duplicateOrUnkeyedStale))
        {
            steps.Add(new ShowcaseReconciliationStep(
                ShowcaseReconciliationActionKind.DeleteStale,
                stale.ApplicationDataId,
                stale.ElementId,
                targetPlan: null));
            deleteStaleCount++;
        }

        return new ShowcaseReconciliationPlan(steps, createCount, updateInPlaceCount, replaceCount, deleteStaleCount);
    }

    /// <summary>
    /// Returns every <c>ks_*</c> parameter GUID currently populated on <paramref name="existing"/> that is
    /// absent, <c>null</c>, removed from the refreshed schema, or retyped in <paramref name="plan"/>.
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
