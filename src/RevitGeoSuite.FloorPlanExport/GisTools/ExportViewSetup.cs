using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>Reads the export views for <see cref="ExportViewPlanner"/> and applies its plan inside Set up model's transaction.</summary>
public static class ExportViewSetup
{
    private const double CutPlaneFeet = 1200 / 304.8;
    private const double TopClipFeet = 2300 / 304.8;

    // The plan's list, plus what the export also reads from a view: windows as openings, rooms
    // as units, shaft openings, structural columns, and the parts a stair is drawn from.
    private static readonly BuiltInCategory[] VisibleModelCategories =
    {
        BuiltInCategory.OST_Floors,
        BuiltInCategory.OST_Stairs,
        BuiltInCategory.OST_StairsRuns,
        BuiltInCategory.OST_StairsLandings,
        BuiltInCategory.OST_StairsSupports,
        BuiltInCategory.OST_GenericModel,
        BuiltInCategory.OST_SpecialityEquipment,
        BuiltInCategory.OST_Doors,
        BuiltInCategory.OST_Windows,
        BuiltInCategory.OST_Columns,
        BuiltInCategory.OST_StructuralColumns,
        BuiltInCategory.OST_Rooms,
        BuiltInCategory.OST_ShaftOpening,
        BuiltInCategory.OST_Lines,
        BuiltInCategory.OST_RvtLinks,
    };

    public static ExportViewSnapshot Read(Document document)
    {
        List<Floor> floors = new FilteredElementCollector(document).OfClass(typeof(Floor)).Cast<Floor>().ToList();
        HashSet<long> levelsWithFloors = new(floors.Select(floor => floor.LevelId.Value));
        List<ExportViewLevel> levels = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(level => new ExportViewLevel(
                level.Id.Value,
                level.Name ?? string.Empty,
                levelsWithFloors.Contains(level.Id.Value),
                !string.IsNullOrWhiteSpace(level.LookupParameter(GisSharedParameters.LevelId)?.AsString())))
            .ToList();

        List<View> views = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().ToList();
        List<ExportPlanView> plans = views
            .OfType<ViewPlan>()
            .Where(view => !view.IsTemplate && view.ViewType == ViewType.FloorPlan && view.GenLevel != null)
            .Select(view => new ExportPlanView(
                view.Id.Value,
                view.Name ?? string.Empty,
                view.GenLevel.Id.Value,
                FrameOf(view),
                view.CropBoxActive && IsRectangular(view) ? CropOf(view) : null,
                view.ViewTemplateId == ElementId.InvalidElementId ? null : view.ViewTemplateId.Value))
            .ToList();

        View? template = FindTemplate(document);
        return new ExportViewSnapshot(
            template == null ? null : new ExportTemplate(template.Id.Value, OwnedCategories(document, template)),
            levels,
            plans,
            views.Select(view => view.Name ?? string.Empty),
            floors.SelectMany(OutlinePoints));
    }

    /// <summary>Applies <paramref name="plan"/>. Call inside an open transaction, after the shared parameters are bound.</summary>
    public static void Apply(Document document, ExportViewPlan plan, List<string> log)
    {
        if (plan.IsEmpty)
        {
            return;
        }

        IReadOnlyList<Point2D> floorPoints = new FilteredElementCollector(document)
            .OfClass(typeof(Floor))
            .Cast<Floor>()
            .SelectMany(OutlinePoints)
            .ToList();

        View? template = FindTemplate(document);
        if (plan.Repair is TemplateRepair repair && document.GetElement(new ElementId(repair.TemplateId)) is View repaired)
        {
            foreach (TemplateCategory category in repair.Show)
            {
                repaired.SetCategoryHidden(new ElementId(category.ElementId), false);
            }

            foreach (TemplateCategory category in repair.Hide)
            {
                repaired.SetCategoryHidden(new ElementId(category.ElementId), true);
            }

            log.Add($"Updated view template {ExportViewPlanner.TemplateName}: {repair.Changes()}");
        }

        if (plan.CreatesTemplate || (plan.NewViews.Count > 0 && template == null))
        {
            template = CreateTemplate(document, plan);
            log.Add($"Created view template {ExportViewPlanner.TemplateName}");
        }

        foreach (NewExportView newView in plan.NewViews)
        {
            ViewPlan view = ViewPlan.Create(document, FloorPlanTypeId(document), new ElementId(newView.LevelId));
            view.Name = newView.ViewName;
            if (template != null)
            {
                view.ViewTemplateId = template.Id;
            }

            document.Regenerate();
            SetCrop(view, ExportViewPlanner.CropFor(FrameOf(view), floorPoints));
            log.Add($"Created plan {newView.ViewName}");
        }

        foreach (ExportViewCrop crop in plan.Crops)
        {
            if (document.GetElement(new ElementId(crop.ViewId)) is ViewPlan view)
            {
                SetCrop(view, ExportViewPlanner.CropFor(FrameOf(view), floorPoints));
                log.Add($"Cropped plan {crop.ViewName} to the floors");
            }
        }

        if (plan.LevelIds.Count > 0)
        {
            SharedParameterManager parameters = new(document);
            List<string> warnings = new();
            foreach (ExportViewLevel entry in plan.LevelIds)
            {
                if (document.GetElement(new ElementId(entry.ElementId)) is Level level)
                {
                    parameters.GetOrCreateLevelId(level, warnings);
                }
            }

            log.Add($"Gave {plan.LevelIds.Count} level(s) a level_id");
            log.AddRange(warnings);
        }
    }

    /// <summary>
    /// The categories and line styles whose visibility the template owns, as they are on
    /// <paramref name="template"/>: what the export reads and every line style.
    /// </summary>
    private static IEnumerable<TemplateCategory> OwnedCategories(Document document, View template)
    {
        HashSet<long> reads = ExportReadCategoryIds(document);
        foreach (Category category in document.Settings.Categories)
        {
            if (category.CategoryType == CategoryType.Model && reads.Contains(category.Id.Value) && template.CanCategoryBeHidden(category.Id))
            {
                yield return new TemplateCategory(category.Id.Value, category.Name ?? string.Empty, TemplateCategoryKind.ExportReads, template.GetCategoryHidden(category.Id));
            }
        }

        foreach (Category style in document.Settings.Categories.get_Item(BuiltInCategory.OST_Lines).SubCategories)
        {
            if (template.CanCategoryBeHidden(style.Id))
            {
                yield return new TemplateCategory(style.Id.Value, style.Name ?? string.Empty, TemplateCategoryKind.LineStyle, template.GetCategoryHidden(style.Id));
            }
        }
    }

    /// <summary>What the export reads from a view, and the category of every family type the standard tags.</summary>
    private static HashSet<long> ExportReadCategoryIds(Document document)
    {
        HashSet<long> ids = new(VisibleModelCategories.Select(category => (long)category));
        ids.UnionWith(TaggedFamilyCategories(document));
        return ids;
    }

    private static View? FindTemplate(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .FirstOrDefault(view => view.IsTemplate && string.Equals(view.Name, ExportViewPlanner.TemplateName, StringComparison.Ordinal));

    /// <summary>
    /// The <c>GIS Export</c> template, made from a scratch plan that is deleted again. It shows only
    /// what the export reads, only <c>GIS_</c> line styles, and cuts at 1200 mm.
    /// </summary>
    private static View CreateTemplate(Document document, ExportViewPlan plan)
    {
        ElementId levelId = plan.NewViews.Count > 0
            ? new ElementId(plan.NewViews[0].LevelId)
            : new FilteredElementCollector(document).OfClass(typeof(Level)).FirstElementId();
        ViewPlan scratch = ViewPlan.Create(document, FloorPlanTypeId(document), levelId);
        View template = scratch.CreateViewTemplate();
        document.Delete(scratch.Id);
        template.Name = ExportViewPlanner.TemplateName;

        HashSet<long> visible = ExportReadCategoryIds(document);
        foreach (Category category in document.Settings.Categories)
        {
            bool show = category.CategoryType == CategoryType.Model && visible.Contains(category.Id.Value);
            if (category.CategoryType is CategoryType.Model or CategoryType.Annotation && template.CanCategoryBeHidden(category.Id))
            {
                template.SetCategoryHidden(category.Id, !show);
            }
        }

        Category lines = document.Settings.Categories.get_Item(BuiltInCategory.OST_Lines);
        foreach (Category style in lines.SubCategories)
        {
            if (template.CanCategoryBeHidden(style.Id))
            {
                template.SetCategoryHidden(style.Id, !(style.Name ?? string.Empty).StartsWith(GisModelFile.GisViewPrefix, StringComparison.Ordinal));
            }
        }

        template.AreImportCategoriesHidden = true;
        template.DetailLevel = ViewDetailLevel.Coarse;
        template.Discipline = ViewDiscipline.Coordination;
        if (template is ViewPlan planTemplate)
        {
            SetViewRange(planTemplate);
            TryClearUnderlay(planTemplate);
        }

        ElementId showComplete = ShowCompletePhaseFilter(document);
        if (showComplete != ElementId.InvalidElementId)
        {
            template.get_Parameter(BuiltInParameter.VIEW_PHASE_FILTER)?.Set(showComplete);
        }

        return template;
    }

    /// <summary>The category of every family type the standard tags with gis_kind or gis_category, such as an escalator modelled as Mechanical Equipment.</summary>
    private static IEnumerable<long> TaggedFamilyCategories(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(FamilySymbol))
            .Cast<FamilySymbol>()
            .Where(symbol => symbol.Category != null &&
                (!string.IsNullOrWhiteSpace(symbol.LookupParameter(GisSharedParameters.GisKind)?.AsString()) ||
                 !string.IsNullOrWhiteSpace(symbol.LookupParameter(GisSharedParameters.GisCategory)?.AsString())))
            .Select(symbol => symbol.Category.Id.Value)
            .Distinct();

    private static void SetViewRange(ViewPlan template)
    {
        PlanViewRange range = template.GetViewRange();
        foreach (PlanViewPlane plane in new[] { PlanViewPlane.CutPlane, PlanViewPlane.BottomClipPlane, PlanViewPlane.ViewDepthPlane, PlanViewPlane.TopClipPlane })
        {
            range.SetLevelId(plane, PlanViewRange.Current);
        }

        range.SetOffset(PlanViewPlane.TopClipPlane, TopClipFeet);
        range.SetOffset(PlanViewPlane.CutPlane, CutPlaneFeet);
        range.SetOffset(PlanViewPlane.BottomClipPlane, 0);
        range.SetOffset(PlanViewPlane.ViewDepthPlane, 0);
        template.SetViewRange(range);
    }

    private static void TryClearUnderlay(ViewPlan template)
    {
        try
        {
            template.SetUnderlayBaseLevel(ElementId.InvalidElementId);
        }
        catch (Autodesk.Revit.Exceptions.ApplicationException)
        {
            // A template that has no underlay setting keeps Revit's default, which is none.
        }
    }

    /// <summary>The phase filter that shows new and existing elements and hides demolished and temporary ones, found by what it does, since its name is translated.</summary>
    private static ElementId ShowCompletePhaseFilter(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(PhaseFilter))
            .Cast<PhaseFilter>()
            .FirstOrDefault(filter =>
                filter.GetPhaseStatusPresentation(ElementOnPhaseStatus.New) == PhaseStatusPresentation.ShowByCategory &&
                filter.GetPhaseStatusPresentation(ElementOnPhaseStatus.Existing) == PhaseStatusPresentation.ShowByCategory &&
                filter.GetPhaseStatusPresentation(ElementOnPhaseStatus.Demolished) == PhaseStatusPresentation.DontShow &&
                filter.GetPhaseStatusPresentation(ElementOnPhaseStatus.Temporary) == PhaseStatusPresentation.DontShow)
            ?.Id ?? ElementId.InvalidElementId;

    private static ElementId FloorPlanTypeId(Document document) =>
        new FilteredElementCollector(document)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .FirstOrDefault(type => type.ViewFamily == ViewFamily.FloorPlan)
            ?.Id ?? throw new InvalidOperationException("The model has no floor plan view type.");

    private static ViewFrame FrameOf(View view)
    {
        Transform transform = view.CropBox.Transform;
        return new ViewFrame(new Point2D(transform.Origin.X, transform.Origin.Y), transform.BasisX.X, transform.BasisX.Y);
    }

    private static CropRect CropOf(View view)
    {
        BoundingBoxXYZ box = view.CropBox;
        return new CropRect(box.Min.X, box.Min.Y, box.Max.X, box.Max.Y);
    }

    /// <summary>Whether the crop is one rectangle: Revit ignores a new crop box on a shaped or split crop.</summary>
    private static bool IsRectangular(View view)
    {
        ViewCropRegionShapeManager shape = view.GetCropRegionShapeManager();
        return !shape.ShapeSet && !shape.Split;
    }

    private static void SetCrop(View view, CropRect? crop)
    {
        if (crop is not CropRect rect)
        {
            return;
        }

        ViewCropRegionShapeManager shape = view.GetCropRegionShapeManager();
        if (shape.ShapeSet)
        {
            shape.RemoveCropRegionShape();
        }

        if (shape.Split)
        {
            shape.RemoveSplit();
        }

        BoundingBoxXYZ box = view.CropBox;
        box.Min = new XYZ(rect.MinX, rect.MinY, box.Min.Z);
        box.Max = new XYZ(rect.MaxX, rect.MaxY, box.Max.Z);
        view.CropBox = box;
        view.CropBoxActive = true;
    }

    /// <summary>The model XY of the floor's top face edges, tessellated.</summary>
    private static IEnumerable<Point2D> OutlinePoints(Floor floor)
    {
        foreach (Reference reference in HostObjectUtils.GetTopFaces(floor))
        {
            if (floor.GetGeometryObjectFromReference(reference) is not Face face)
            {
                continue;
            }

            foreach (CurveLoop loop in face.GetEdgesAsCurveLoops())
            {
                foreach (Curve curve in loop)
                {
                    foreach (XYZ point in curve.Tessellate())
                    {
                        yield return new Point2D(point.X, point.Y);
                    }
                }
            }
        }
    }
}
