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
                        .Select(category => BuiltInName(category))
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
    /// Elevator objects with their plan box, height and host level. With <paramref name="uniqueIds"/>
    /// (the export's elevator unit rows), exactly those elements, of any kind (family instances,
    /// floors, rooms); without, the family instances whose type has gis_kind or gis_category 'elevator'.
    /// </summary>
    public static Dictionary<string, object?> Elevators(UIApplication app, string[] uniqueIds)
    {
        Document document = Active(app);
        var elevators = new List<Dictionary<string, object?>>();
        var missing = new List<string>();
        if (uniqueIds.Length > 0)
        {
            foreach (string uniqueId in uniqueIds.Distinct())
            {
                Element? element = document.GetElement(uniqueId);
                if (element == null)
                {
                    missing.Add(uniqueId);
                    continue;
                }

                elevators.Add(ElevatorRow(document, element));
            }
        }
        else
        {
            foreach (FamilyInstance instance in new FilteredElementCollector(document).OfClass(typeof(FamilyInstance)).WhereElementIsNotElementType().Cast<FamilyInstance>().OrderBy(item => item.Id.Value))
            {
                if (document.GetElement(instance.GetTypeId()) is ElementType type && (IsElevator(type, "gis_kind") || IsElevator(type, "gis_category")))
                {
                    elevators.Add(ElevatorRow(document, instance));
                }
            }
        }

        return new Dictionary<string, object?> { ["elevators"] = elevators, ["missing"] = missing };
    }

    private static Dictionary<string, object?> ElevatorRow(Document document, Element element)
    {
        ElementType? type = document.GetElement(element.GetTypeId()) as ElementType;
        BoundingBoxXYZ? box = element.get_BoundingBox(null);
        ElementId levelId = element.LevelId;
        Level? level = document.GetElement(levelId) as Level;
        return new Dictionary<string, object?>
        {
            ["id"] = element.Id.Value,
            ["uniqueId"] = element.UniqueId,
            ["typeId"] = type?.Id.Value ?? -1,
            ["family"] = (element as FamilyInstance)?.Symbol?.FamilyName ?? type?.FamilyName,
            ["type"] = type?.Name,
            ["category"] = element.Category?.Name,
            ["level"] = level?.Name,
            ["levelElementId"] = level == null ? null : levelId.Value,
            ["levelElevationFt"] = level?.ProjectElevation,
            ["xMinM"] = box == null ? null : box.Min.X * FeetToMetres,
            ["xMaxM"] = box == null ? null : box.Max.X * FeetToMetres,
            ["yMinM"] = box == null ? null : box.Min.Y * FeetToMetres,
            ["yMaxM"] = box == null ? null : box.Max.Y * FeetToMetres,
            ["zMinM"] = box == null ? null : box.Min.Z * FeetToMetres,
            ["zMaxM"] = box == null ? null : box.Max.Z * FeetToMetres,
            // An instance parameter from RevitGeoSuite #45's c1c09d0 on; a type parameter before.
            ["levelsServed"] = element.LookupParameter("gis_levels_served")?.AsString() ?? type?.LookupParameter("gis_levels_served")?.AsString(),
        };
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

    // BuiltInCategory is backed by long from Revit 2024 on; Enum.ToObject converts to whatever it is.
    private static string BuiltInName(Category category)
    {
        object value = Enum.ToObject(typeof(BuiltInCategory), category.Id.Value);
        return Enum.IsDefined(typeof(BuiltInCategory), value) ? value.ToString()! : category.Name;
    }

    private static bool IsElevator(Element type, string parameter) =>
        string.Equals(type.LookupParameter(parameter)?.AsString()?.Trim(), "elevator", StringComparison.OrdinalIgnoreCase);

    private static Document Active(UIApplication app) => app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
}
