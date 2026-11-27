using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Showcase;

namespace RevitGeoSuite.FloorPlanExport.Showcase;

/// <summary>Result of configuring Autodesk Forma Publish Settings 3D views inside a companion document.</summary>
public sealed class ShowcaseViewBuildResult
{
    public ShowcaseViewBuildResult(
        IReadOnlyList<string> configuredViewNames,
        int createdViewCount,
        int updatedViewCount,
        int deletedStaleViewCount,
        bool publishViewSetConfigured)
    {
        ConfiguredViewNames = configuredViewNames ?? Array.Empty<string>();
        CreatedViewCount = createdViewCount;
        UpdatedViewCount = updatedViewCount;
        DeletedStaleViewCount = deletedStaleViewCount;
        PublishViewSetConfigured = publishViewSetConfigured;
    }

    public IReadOnlyList<string> ConfiguredViewNames { get; }

    public int CreatedViewCount { get; }

    public int UpdatedViewCount { get; }

    public int DeletedStaleViewCount { get; }

    public bool PublishViewSetConfigured { get; }
}

/// <summary>
/// Creates, updates in-place (preserving view <c>UniqueId</c>s), and cleans up stale pre-configured Autodesk Forma
/// Publish Settings 3D views (Overview + Network &amp; POIs + per-floor section boxes) and registers them in Revit's
/// Publish Settings <c>ViewSheetSet</c>. Compatible with Revit 2024 (<c>net48</c>) and Revit 2027 (<c>net10.0-windows</c>).
/// </summary>
public static class ShowcaseViewBuilder
{
    /// <summary>
    /// Materializes <paramref name="viewSetPlan"/> inside an active Revit transaction on <paramref name="document"/>.
    /// </summary>
    public static ShowcaseViewBuildResult ApplyPublishViews(
        Document document,
        FormaPublishViewSetPlan viewSetPlan,
        ICollection<string> warnings)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        if (viewSetPlan is null)
        {
            throw new ArgumentNullException(nameof(viewSetPlan));
        }

        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        ElementId? threeDTypeId = FindThreeDimensionalViewFamilyTypeId(document);
        if (threeDTypeId == null || threeDTypeId == ElementId.InvalidElementId)
        {
            warnings.Add("Companion document has no 3D ViewFamilyType; skipped configuring Forma Publish 3D views.");
            return new ShowcaseViewBuildResult(Array.Empty<string>(), 0, 0, 0, publishViewSetConfigured: false);
        }

        List<View3D> existing3DViews = new FilteredElementCollector(document)
            .OfClass(typeof(View3D))
            .Cast<View3D>()
            .Where(v => !v.IsTemplate)
            .ToList();

        Dictionary<long, View3D> existingById = existing3DViews.ToDictionary(v => v.Id.Value);
        List<ShowcaseExistingViewInfo> existingInfos = existing3DViews
            .Select(v => new ShowcaseExistingViewInfo(v.Id.Value, v.Name ?? string.Empty, v.UniqueId, v.IsTemplate))
            .ToList();

        ShowcaseViewReconciliationPlan reconciliation = ShowcaseViewPlanner.ReconcileViews(viewSetPlan, existingInfos);

        List<View3D> publishViews = new();
        List<string> configuredNames = new();
        int createdCount = 0;
        int updatedCount = 0;
        int deletedStaleCount = 0;
        List<ElementId> staleIdsToDelete = new();
        ElementId? activeViewId = document.ActiveView?.Id;

        foreach (ShowcaseViewReconciliationStep step in reconciliation.Steps)
        {
            switch (step.Action)
            {
                case ShowcaseViewReconciliationActionKind.Create:
                {
                    ShowcaseView3DPlan target = step.TargetView!;
                    try
                    {
                        View3D created = View3D.CreateIsometric(document, threeDTypeId);
                        ConfigureView3D(document, created, target, isNewlyCreated: true, warnings);
                        publishViews.Add(created);
                        configuredNames.Add(created.Name);
                        createdCount++;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add($"Failed to create Forma 3D view '{target.Name}': {ex.Message}");
                    }

                    break;
                }

                case ShowcaseViewReconciliationActionKind.UpdateInPlace:
                {
                    ShowcaseView3DPlan target = step.TargetView!;
                    if (step.ExistingViewId.HasValue &&
                        existingById.TryGetValue(step.ExistingViewId.Value, out View3D? existingView))
                    {
                        try
                        {
                            ConfigureView3D(document, existingView, target, isNewlyCreated: false, warnings);
                            publishViews.Add(existingView);
                            configuredNames.Add(existingView.Name);
                            updatedCount++;
                        }
                        catch (Exception ex)
                        {
                            warnings.Add($"Failed to update Forma 3D view '{target.Name}' in-place: {ex.Message}");
                        }
                    }

                    break;
                }

                case ShowcaseViewReconciliationActionKind.DeleteStale:
                {
                    if (!step.ExistingViewId.HasValue)
                    {
                        break;
                    }

                    ElementId staleId = new(step.ExistingViewId.Value);
                    if (activeViewId != null && staleId == activeViewId)
                    {
                        warnings.Add($"Skipped deleting stale Forma 3D view '{step.ViewName}' because it is currently the active view.");
                        break;
                    }

                    staleIdsToDelete.Add(staleId);
                    break;
                }
            }
        }

        if (staleIdsToDelete.Count > 0)
        {
            try
            {
                document.Delete(staleIdsToDelete);
                deletedStaleCount = staleIdsToDelete.Count;
            }
            catch (Exception ex)
            {
                warnings.Add($"Failed to delete {staleIdsToDelete.Count} stale Forma 3D view(s): {ex.Message}");
            }
        }

        bool publishViewSetConfigured = TryConfigurePublishViewSheetSet(
            document,
            viewSetPlan.ViewSetName,
            publishViews,
            warnings);

        return new ShowcaseViewBuildResult(
            configuredNames,
            createdCount,
            updatedCount,
            deletedStaleCount,
            publishViewSetConfigured);
    }

    private static ElementId? FindThreeDimensionalViewFamilyTypeId(Document document)
    {
        ViewFamilyType? vft = new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(t => t.ViewFamily == ViewFamily.ThreeDimensional);
        return vft?.Id;
    }

    private static void ConfigureView3D(
        Document document,
        View3D view3D,
        ShowcaseView3DPlan plan,
        bool isNewlyCreated,
        ICollection<string> warnings)
    {
        if (isNewlyCreated && !string.Equals(view3D.Name, plan.Name, StringComparison.Ordinal))
        {
            view3D.Name = plan.Name;
        }

        try
        {
            if (view3D.ViewTemplateId != ElementId.InvalidElementId)
            {
                view3D.ViewTemplateId = ElementId.InvalidElementId;
            }
        }
        catch (Exception ex)
        {
            warnings.Add($"Could not detach view template from Forma 3D view '{plan.Name}': {ex.Message}");
        }

        if (plan.SectionBoxFeet.IsValid)
        {
            BoundingBoxXYZ sectionBox = new()
            {
                Transform = Transform.Identity,
                Min = new XYZ(plan.SectionBoxFeet.MinXFeet, plan.SectionBoxFeet.MinYFeet, plan.SectionBoxFeet.MinZFeet),
                Max = new XYZ(plan.SectionBoxFeet.MaxXFeet, plan.SectionBoxFeet.MaxYFeet, plan.SectionBoxFeet.MaxZFeet),
            };
            view3D.SetSectionBox(sectionBox);
            view3D.IsSectionBoxActive = true;
        }

        try
        {
            view3D.DetailLevel = ViewDetailLevel.Fine;
        }
        catch
        {
            // Non-fatal if view detail level cannot be adjusted in a locked view.
        }

        try
        {
            view3D.DisplayStyle = DisplayStyle.Shading;
        }
        catch
        {
            // Non-fatal if display style cannot be adjusted.
        }

        ApplyCategoryVisibility(document, view3D, plan.HiddenCategoryNames, hide: true);
        ApplyCategoryVisibility(document, view3D, plan.VisibleCategoryNames, hide: false);
    }

    private static void ApplyCategoryVisibility(
        Document document,
        View3D view3D,
        IReadOnlyList<string> builtInCategoryNames,
        bool hide)
    {
        for (int i = 0; i < builtInCategoryNames.Count; i++)
        {
            if (!Enum.TryParse(builtInCategoryNames[i], ignoreCase: false, out BuiltInCategory bic))
            {
                continue;
            }

            Category? category = Category.GetCategory(document, bic);
            if (category == null)
            {
                continue;
            }

            try
            {
                if (view3D.CanCategoryBeHidden(category.Id))
                {
                    view3D.SetCategoryHidden(category.Id, hide);
                }
            }
            catch
            {
                // Ignore categories that cannot be toggled in this template/document.
            }
        }
    }

    /// <summary>
    /// Saves or updates Revit's Publish Settings <c>ViewSheetSet</c> (<c>"Kanizu Forma Publish"</c>) containing
    /// every generated Forma 3D view. Gracefully emits a non-fatal warning in headless/no-printer environments.
    /// </summary>
    private static bool TryConfigurePublishViewSheetSet(
        Document document,
        string viewSetName,
        IReadOnlyList<View3D> views,
        ICollection<string> warnings)
    {
        if (views.Count == 0)
        {
            return false;
        }

        try
        {
            PrintManager? printManager = document.PrintManager;
            if (printManager == null)
            {
                warnings.Add($"PrintManager is unavailable; skipped creating Forma Publish ViewSheetSet '{viewSetName}'.");
                return false;
            }

            printManager.PrintRange = PrintRange.Select;
            ViewSheetSetting viewSheetSetting = printManager.ViewSheetSetting;

            ViewSet viewSet = new();
            for (int i = 0; i < views.Count; i++)
            {
                viewSet.Insert(views[i]);
            }

            ViewSheetSet? existingSet = new FilteredElementCollector(document)
                .OfClass(typeof(ViewSheetSet))
                .Cast<ViewSheetSet>()
                .FirstOrDefault(s => string.Equals(s.Name, viewSetName, StringComparison.OrdinalIgnoreCase));

            if (existingSet != null)
            {
                viewSheetSetting.CurrentViewSheetSet = existingSet;
                existingSet.Views = viewSet;
                viewSheetSetting.Save();
            }
            else
            {
                viewSheetSetting.CurrentViewSheetSet = viewSheetSetting.InSession;
                viewSheetSetting.CurrentViewSheetSet.Views = viewSet;
                viewSheetSetting.SaveAs(viewSetName);
            }

            printManager.Apply();
            return true;
        }
        catch (Exception ex)
        {
            warnings.Add(
                $"Could not save Forma Publish ViewSheetSet '{viewSetName}' (likely headless or no printer configured): {ex.Message}");
            return false;
        }
    }
}
