using System;
using System.Collections.Generic;
using System.Linq;

namespace RevitGeoSuite.FloorPlanExport.Core.Showcase;

/// <summary>Revit-API-free summary of an existing <c>Kanizu.Showcase</c> DirectShape found in a companion model.</summary>
public sealed class ShowcaseExistingElementInfo
{
    public ShowcaseExistingElementInfo(long elementId, string applicationDataId, string categoryName)
    {
        ElementId = elementId;
        ApplicationDataId = applicationDataId ?? string.Empty;
        CategoryName = categoryName ?? string.Empty;
    }

    public long ElementId { get; }

    public string ApplicationDataId { get; }

    /// <summary>Resolved Revit category token (e.g. <c>OST_Floors</c>, <c>OST_GenericModel</c>, or category ID string).</summary>
    public string CategoryName { get; }
}

/// <summary>Reconciliation operation planned for a showcase DirectShape when updating a companion document.</summary>
public enum ShowcaseReconciliationActionKind
{
    Create,
    UpdateInPlace,
    ReplaceCategoryChanged,
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
        string? resolvedTargetCategoryName = null)
    {
        Action = action;
        ApplicationDataId = applicationDataId ?? string.Empty;
        ExistingElementId = existingElementId;
        TargetPlan = targetPlan;
        ResolvedTargetCategoryName = resolvedTargetCategoryName;
    }

    public ShowcaseReconciliationActionKind Action { get; }

    public string ApplicationDataId { get; }

    public long? ExistingElementId { get; }

    public ShowcaseElementPlan? TargetPlan { get; }

    public string? ResolvedTargetCategoryName { get; }
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
/// - Same key + same category -&gt; <see cref="ShowcaseReconciliationActionKind.UpdateInPlace"/> (preserves Revit <c>UniqueId</c>)
/// - Same key + changed category -&gt; <see cref="ShowcaseReconciliationActionKind.ReplaceCategoryChanged"/>
/// - New key -&gt; <see cref="ShowcaseReconciliationActionKind.Create"/>
/// - Removed or duplicate existing key -&gt; <see cref="ShowcaseReconciliationActionKind.DeleteStale"/>
/// </summary>
public static class ShowcaseElementReconciler
{
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
                if (categoryMatches)
                {
                    steps.Add(new ShowcaseReconciliationStep(
                        ShowcaseReconciliationActionKind.UpdateInPlace,
                        plan.ApplicationDataId,
                        existing.ElementId,
                        plan,
                        resolvedCategory));
                    updateInPlaceCount++;
                }
                else
                {
                    steps.Add(new ShowcaseReconciliationStep(
                        ShowcaseReconciliationActionKind.ReplaceCategoryChanged,
                        plan.ApplicationDataId,
                        existing.ElementId,
                        plan,
                        resolvedCategory));
                    replaceCount++;
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
    /// Computes which direct <c>ks_*</c> parameter definitions must be cleared on an in-place updated DirectShape
    /// because the refreshed feature carries <c>null</c> or omits them.
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

        HashSet<Guid> populatedGuids = new();
        foreach (ShowcaseParameterAssignment assignment in plan.ParameterAssignments)
        {
            if (!assignment.IsNull && !assignment.IsTextFallback)
            {
                populatedGuids.Add(assignment.Parameter.Guid);
            }
        }

        List<ShowcaseParameterDefinition> toClear = new();
        foreach (ShowcaseParameterDefinition def in registry.DirectParameters)
        {
            if (!populatedGuids.Contains(def.Guid))
            {
                toClear.Add(def);
            }
        }

        if (string.IsNullOrEmpty(plan.OverflowAttributesJson))
        {
            toClear.Add(registry.OverflowParameter);
        }

        return toClear;
    }
}
