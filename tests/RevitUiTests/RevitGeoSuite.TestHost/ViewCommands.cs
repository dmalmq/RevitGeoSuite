using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost;

/// <summary>
/// Reads and edits views, view templates and levels for the GIS export view checks (B1, B2a).
/// Categories are named by BuiltInCategory (OST_Floors); line styles by their subcategory name of
/// Lines, in any of the given spellings (for example "&lt;Thin Lines&gt;" and "&lt;細線&gt;").
/// </summary>
internal static class ViewCommands
{
    private const double FeetToMetres = 0.3048;
    private const double FeetToMillimetres = 304.8;

    public static Dictionary<string, object?> Views(UIApplication app, Dictionary<string, object?> request)
    {
        Document document = Active(app);
        string[] categories = Strings(request, "categories");
        string[] lineStyles = Strings(request, "lineStyles");
        var views = new List<Dictionary<string, object?>>();
        foreach (View view in new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().Where(view => view.ViewType == ViewType.FloorPlan))
        {
            var entry = new Dictionary<string, object?>
            {
                ["id"] = view.Id.Value,
                ["name"] = view.Name,
                ["isTemplate"] = view.IsTemplate,
                ["level"] = (view as ViewPlan)?.GenLevel?.Name,
                ["template"] = view.ViewTemplateId == ElementId.InvalidElementId ? null : document.GetElement(view.ViewTemplateId)?.Name,
                ["cropOn"] = !view.IsTemplate && view.CropBoxActive,
                ["detailLevel"] = view.DetailLevel.ToString(),
                ["discipline"] = view.HasViewDiscipline() ? view.Discipline.ToString() : null,
            };

            if (!view.IsTemplate && view.CropBox is BoundingBoxXYZ crop)
            {
                entry["cropWidthM"] = (crop.Max.X - crop.Min.X) * FeetToMetres;
                entry["cropHeightM"] = (crop.Max.Y - crop.Min.Y) * FeetToMetres;
            }

            if (view is ViewPlan plan && plan.GetViewRange() is PlanViewRange range)
            {
                entry["cutPlaneMm"] = range.GetOffset(PlanViewPlane.CutPlane) * FeetToMillimetres;
                entry["bottomMm"] = range.GetOffset(PlanViewPlane.BottomClipPlane) * FeetToMillimetres;
                entry["depthMm"] = range.GetOffset(PlanViewPlane.ViewDepthPlane) * FeetToMillimetres;
                entry["bottomAtLevel"] = range.GetLevelId(PlanViewPlane.BottomClipPlane) == PlanViewRange.Current;
                entry["depthAtLevel"] = range.GetLevelId(PlanViewPlane.ViewDepthPlane) == PlanViewRange.Current;
            }

            entry["hidden"] = categories.ToDictionary(name => name, name => (object?)Hidden(view, CategoryId(document, name)));
            entry["lineStylesHidden"] = LineStyle(document, lineStyles) is ElementId style ? (object?)view.GetCategoryHidden(style) : null;
            views.Add(entry);
        }

        return new Dictionary<string, object?> { ["views"] = views };
    }

    public static Dictionary<string, object?> Levels(UIApplication app)
    {
        Document document = Active(app);
        HashSet<long> floorLevels = new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType()
            .Select(floor => floor.LevelId.Value).ToHashSet();
        return new Dictionary<string, object?>
        {
            ["levels"] = new FilteredElementCollector(document).OfClass(typeof(Level)).Cast<Level>()
                .Select(level => new Dictionary<string, object?>
                {
                    ["id"] = level.Id.Value,
                    ["name"] = level.Name,
                    ["levelId"] = level.LookupParameter("level_id")?.AsString(),
                    ["hasFloors"] = floorLevels.Contains(level.Id.Value),
                    ["elevationFt"] = level.ProjectElevation,
                })
                .ToList(),
        };
    }

    /// <summary>Every 3D view that is not a template, by name.</summary>
    public static Dictionary<string, object?> Views3D(UIApplication app) => new()
    {
        ["views"] = new FilteredElementCollector(Active(app)).OfClass(typeof(View3D)).Cast<View3D>()
            .Where(view => !view.IsTemplate)
            .Select(view => view.Name)
            .ToList(),
    };

    public static Dictionary<string, object?> ActivateView(UIApplication app, string name)
    {
        UIDocument ui = app.ActiveUIDocument ?? throw new InvalidOperationException("No active document.");
        View view = new FilteredElementCollector(ui.Document).OfClass(typeof(View)).Cast<View>()
            .FirstOrDefault(candidate => !candidate.IsTemplate && candidate.Name == name)
            ?? throw new ArgumentException($"No view named '{name}'.");
        ui.ActiveView = view;
        return new Dictionary<string, object?> { ["view"] = view.Name, ["id"] = view.Id.Value };
    }

    /// <summary>
    /// One view edit inside the fixture transaction: {view, hide: [...], show: [...], hideLineStyles: [...],
    /// showLineStyles: [...], template: "none" or a view template name}. The view may be a view template.
    /// </summary>
    public static IEnumerable<string> Edit(Document document, Dictionary<string, object?> edit)
    {
        string name = edit.TryGetValue("view", out object? value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
        View view = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().FirstOrDefault(candidate => candidate.Name == name)
            ?? throw new ArgumentException($"No view named '{name}'.");
        foreach (string category in Strings(edit, "hide"))
        {
            view.SetCategoryHidden(CategoryId(document, category), true);
            yield return $"hid {category} in {name}";
        }

        foreach (string category in Strings(edit, "show"))
        {
            view.SetCategoryHidden(CategoryId(document, category), false);
            yield return $"showed {category} in {name}";
        }

        foreach ((string key, bool hide) in new[] { ("hideLineStyles", true), ("showLineStyles", false) })
        {
            string[] spellings = Strings(edit, key);
            if (spellings.Length == 0)
            {
                continue;
            }

            ElementId style = LineStyle(document, spellings) ?? throw new ArgumentException($"No line style named {string.Join(" or ", spellings)}.");
            view.SetCategoryHidden(style, hide);
            yield return $"{(hide ? "hid" : "showed")} line style {document.GetElement(style)?.Name ?? string.Join("/", spellings)} in {name}";
        }

        string template = edit.TryGetValue("template", out object? given) ? Convert.ToString(given, CultureInfo.InvariantCulture) ?? string.Empty : string.Empty;
        if (template == "none")
        {
            view.ViewTemplateId = ElementId.InvalidElementId;
            yield return $"removed the view template of {name}";
        }
        else if (template.Length > 0)
        {
            View applied = new FilteredElementCollector(document).OfClass(typeof(View)).Cast<View>().FirstOrDefault(candidate => candidate.IsTemplate && candidate.Name == template)
                ?? throw new ArgumentException($"No view template named '{template}'.");
            view.ViewTemplateId = applied.Id;
            yield return $"gave {name} the view template {template}";
        }
    }

    /// <summary>Copies elements onto themselves, as Paste Aligned to Same Place does.</summary>
    public static IEnumerable<string> CopyInPlace(Document document, IEnumerable<long> ids)
    {
        foreach (long id in ids)
        {
            ICollection<ElementId> copies = ElementTransformUtils.CopyElement(document, new ElementId(id), XYZ.Zero);
            yield return $"copied {id} in place as {string.Join(", ", copies.Select(copy => copy.Value))}";
        }
    }

    private static ElementId CategoryId(Document document, string name) =>
        Enum.TryParse(name, out BuiltInCategory builtIn) && Category.GetCategory(document, builtIn) is Category category
            ? category.Id
            : throw new ArgumentException($"No category {name}.");

    private static bool? Hidden(View view, ElementId category) => view.CanCategoryBeHidden(category) ? view.GetCategoryHidden(category) : null;

    private static ElementId? LineStyle(Document document, string[] spellings)
    {
        if (spellings.Length == 0)
        {
            return null;
        }

        Category lines = Category.GetCategory(document, BuiltInCategory.OST_Lines);
        return lines.SubCategories.Cast<Category>().FirstOrDefault(sub => spellings.Contains(sub.Name, StringComparer.Ordinal))?.Id;
    }

    private static Document Active(UIApplication app) => app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");

    private static string[] Strings(Dictionary<string, object?> request, string key) =>
        request.TryGetValue(key, out object? value) && value is IEnumerable items and not string
            ? items.Cast<object>().Select(item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty).ToArray()
            : Array.Empty<string>();
}
