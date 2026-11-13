using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost;

/// <summary>Reads parameter bindings, elevator family instances and doors for the export field checks (A3).</summary>
internal static class ModelCommands
{
    private const double FeetToMetres = 0.3048;

    /// <summary>Each named project parameter: whether it is bound, as type or instance, and to which categories.</summary>
    public static Dictionary<string, object?> Bindings(UIApplication app, string[] names)
    {
        Document document = Active(app);
        var found = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        while (iterator.MoveNext())
        {
            if (iterator.Key is Definition definition && names.Contains(definition.Name) && iterator.Current is ElementBinding binding)
            {
                found[definition.Name] = new Dictionary<string, object?>
                {
                    ["name"] = definition.Name,
                    ["binding"] = binding is TypeBinding ? "type" : "instance",
                    ["categories"] = binding.Categories.Cast<Category>()
                        .Select(category => Enum.IsDefined(typeof(BuiltInCategory), (int)category.Id.Value) ? ((BuiltInCategory)(int)category.Id.Value).ToString() : category.Name)
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToList(),
                };
            }
        }

        return new Dictionary<string, object?>
        {
            ["bindings"] = names.Select(name => found.TryGetValue(name, out var entry) ? entry : new Dictionary<string, object?> { ["name"] = name, ["binding"] = null }).ToList(),
        };
    }

    /// <summary>
    /// Every family instance whose type has gis_kind or gis_category "elevator", by element id: family,
    /// type, host level, bounding box in metres and the type's gis_levels_served.
    /// </summary>
    public static Dictionary<string, object?> Elevators(UIApplication app)
    {
        Document document = Active(app);
        var elevators = new List<Dictionary<string, object?>>();
        foreach (FamilyInstance instance in new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>().OrderBy(item => item.Id.Value))
        {
            ElementType? type = document.GetElement(instance.GetTypeId()) as ElementType;
            if (type == null || !(IsElevator(type, "gis_kind") || IsElevator(type, "gis_category")))
            {
                continue;
            }

            BoundingBoxXYZ? box = instance.get_BoundingBox(null);
            elevators.Add(new Dictionary<string, object?>
            {
                ["id"] = instance.Id.Value,
                ["uniqueId"] = instance.UniqueId,
                ["typeId"] = type.Id.Value,
                ["family"] = instance.Symbol?.FamilyName,
                ["type"] = type.Name,
                ["category"] = instance.Category?.Name,
                ["level"] = document.GetElement(instance.LevelId)?.Name,
                ["levelElementId"] = instance.LevelId == ElementId.InvalidElementId ? null : instance.LevelId.Value,
                ["xMinM"] = box == null ? null : box.Min.X * FeetToMetres,
                ["xMaxM"] = box == null ? null : box.Max.X * FeetToMetres,
                ["yMinM"] = box == null ? null : box.Min.Y * FeetToMetres,
                ["yMaxM"] = box == null ? null : box.Max.Y * FeetToMetres,
                ["zMinM"] = box == null ? null : box.Min.Z * FeetToMetres,
                ["zMaxM"] = box == null ? null : box.Max.Z * FeetToMetres,
                ["levelsServed"] = type.LookupParameter("gis_levels_served")?.AsString(),
            });
        }

        return new Dictionary<string, object?> { ["elevators"] = elevators };
    }

    /// <summary>Every door instance by element id, with its UniqueId, type and family.</summary>
    public static Dictionary<string, object?> Doors(UIApplication app)
    {
        Document document = Active(app);
        return new Dictionary<string, object?>
        {
            ["doors"] = new FilteredElementCollector(document).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType()
                .OrderBy(element => element.Id.Value)
                .Select(element => new Dictionary<string, object?>
                {
                    ["id"] = element.Id.Value,
                    ["uniqueId"] = element.UniqueId,
                    ["typeId"] = element.GetTypeId().Value,
                    ["type"] = document.GetElement(element.GetTypeId())?.Name,
                    ["family"] = (element as FamilyInstance)?.Symbol?.FamilyName,
                })
                .ToList(),
        };
    }

    private static bool IsElevator(Element type, string parameter) =>
        string.Equals(type.LookupParameter(parameter)?.AsString()?.Trim(), "elevator", StringComparison.OrdinalIgnoreCase);

    private static Document Active(UIApplication app) => app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
}
