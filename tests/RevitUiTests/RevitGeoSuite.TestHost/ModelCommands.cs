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

    /// <summary>
    /// Every group type: model or detail, how many instances, and what one instance holds (nested groups
    /// counted in), by the categories the IMDF export writes to: floors, rooms, doors, stairs, columns,
    /// openings and other family instances, then everything else. Each category carries up to 20 member
    /// ids. Reads only. Writing to a grouped element raises "A group has been changed outside group edit
    /// mode" (a warning with one instance; with two or more, an error, or the value goes to every copy).
    /// </summary>
    public static Dictionary<string, object?> Groups(UIApplication app)
    {
        Document document = Active(app);
        ILookup<long, Group> instances = new FilteredElementCollector(document).OfClass(typeof(Group)).Cast<Group>()
            .ToLookup(group => group.GroupType.Id.Value);
        return new Dictionary<string, object?>
        {
            ["groups"] = new FilteredElementCollector(document).OfClass(typeof(GroupType)).Cast<GroupType>()
                .Select(type =>
                {
                    Group[] placed = instances[type.Id.Value].ToArray();
                    var members = GroupMemberKinds.ToDictionary(kind => kind, _ => new List<long>());
                    if (placed.Length > 0)
                    {
                        CollectMembers(document, placed[0], members);
                    }

                    var row = new Dictionary<string, object?>
                    {
                        ["id"] = type.Id.Value,
                        ["name"] = type.Name,
                        ["kind"] = type.Category is Category category ? BuiltInName(category) : null,
                        ["instances"] = placed.Length,
                        ["instanceIds"] = placed.Take(20).Select(group => group.Id.Value).ToList(),
                    };
                    foreach (KeyValuePair<string, List<long>> pair in members)
                    {
                        row[pair.Key] = pair.Value.Count;
                    }

                    row["memberIds"] = members.Where(pair => pair.Key != "other" && pair.Value.Count > 0)
                        .ToDictionary(pair => pair.Key, pair => pair.Value.Take(20).ToList());
                    return row;
                })
                .OrderByDescending(row => (int)row["instances"]!)
                .ToList(),
        };
    }

    private static readonly string[] GroupMemberKinds = { "floors", "rooms", "doors", "stairs", "columns", "openings", "familyInstances", "other" };

    private static void CollectMembers(Document document, Group group, Dictionary<string, List<long>> members)
    {
        foreach (ElementId id in group.GetMemberIds())
        {
            Element? element = document.GetElement(id);
            if (element is Group nested)
            {
                CollectMembers(document, nested, members);
            }
            else if (element != null)
            {
                members[MemberKind(element)].Add(element.Id.Value);
            }
        }
    }

    private static string MemberKind(Element element)
    {
        long category = element.Category?.Id.Value ?? 0;
        bool Is(BuiltInCategory builtIn) => category == (long)builtIn;
        return element switch
        {
            Floor => "floors",
            _ when Is(BuiltInCategory.OST_Rooms) => "rooms",
            _ when Is(BuiltInCategory.OST_Doors) => "doors",
            _ when Is(BuiltInCategory.OST_Stairs) || Is(BuiltInCategory.OST_StairsRuns) || Is(BuiltInCategory.OST_StairsLandings) => "stairs",
            _ when Is(BuiltInCategory.OST_Columns) || Is(BuiltInCategory.OST_StructuralColumns) => "columns",
            Opening => "openings",
            _ when Is(BuiltInCategory.OST_ShaftOpening) || Is(BuiltInCategory.OST_FloorOpening) => "openings",
            FamilyInstance => "familyInstances",
            _ => "other",
        };
    }

    private static Document Active(UIApplication app) => app.ActiveUIDocument?.Document ?? throw new InvalidOperationException("No active document.");
}
