using System;
using System.Collections.Generic;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Core.GisTools;

// What the model tools need to know about a model, as plain data. The Revit side reads it from a
// document; the tests build it by hand. Element ids are plain integers (ElementId.Value in Revit
// 2024 and later).

/// <summary>A parameter bound in the model. <see cref="Guid"/> is null for a project (non-shared) parameter.</summary>
public sealed class BoundParameterInfo
{
    public BoundParameterInfo(string name, Guid? guid, GisParameterBinding binding, IEnumerable<string> categories)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Guid = guid;
        Binding = binding;
        Categories = (categories ?? Array.Empty<string>()).ToList();
    }

    public string Name { get; }

    public Guid? Guid { get; }

    public GisParameterBinding Binding { get; }

    /// <summary>Revit <c>BuiltInCategory</c> names, such as <c>OST_Floors</c>.</summary>
    public IReadOnlyList<string> Categories { get; }
}

public sealed class FloorTypeInfo
{
    public FloorTypeInfo(
        long elementId,
        string name,
        string? gisCategory = null,
        string? gisRestriction = null,
        string? material = null,
        IEnumerable<long>? floorIds = null)
    {
        ElementId = elementId;
        Name = name ?? string.Empty;
        GisCategory = gisCategory ?? string.Empty;
        GisRestriction = gisRestriction ?? string.Empty;
        Material = material ?? string.Empty;
        FloorIds = (floorIds ?? Array.Empty<long>()).ToList();
    }

    public long ElementId { get; }

    public string Name { get; }

    public string GisCategory { get; set; }

    public string GisRestriction { get; set; }

    /// <summary>The material name of the type's top layer.</summary>
    public string Material { get; set; }

    /// <summary>The floors of this type placed in the model.</summary>
    public IReadOnlyList<long> FloorIds { get; }
}

/// <summary>A floor whose own <c>category</c> parameter is filled.</summary>
public sealed class FloorInfo
{
    public FloorInfo(long elementId, long typeId, string? category)
    {
        ElementId = elementId;
        TypeId = typeId;
        Category = category ?? string.Empty;
    }

    public long ElementId { get; }

    public long TypeId { get; }

    public string Category { get; }
}

public sealed class MaterialInfo
{
    public MaterialInfo(long elementId, string name, string? color)
    {
        ElementId = elementId;
        Name = name ?? string.Empty;
        Color = (color ?? string.Empty).ToUpperInvariant();
    }

    public long ElementId { get; }

    public string Name { get; }

    /// <summary><c>#RRGGBB</c>, upper case.</summary>
    public string Color { get; set; }
}

public sealed class LevelInfo
{
    public LevelInfo(long elementId, string name, int? ordinal = null, string? shortName = null, bool hasFloors = true)
    {
        ElementId = elementId;
        Name = name ?? string.Empty;
        Ordinal = ordinal;
        ShortName = shortName ?? string.Empty;
        HasFloors = hasFloors;
    }

    public long ElementId { get; }

    public string Name { get; }

    public int? Ordinal { get; }

    public string ShortName { get; }

    public bool HasFloors { get; }
}

/// <summary>A family type of the generic model or specialty equipment category placed in the model.</summary>
public sealed class FamilyTypeInfo
{
    public FamilyTypeInfo(
        long elementId,
        string familyName,
        string typeName,
        string? gisKind = null,
        IEnumerable<long>? instanceIds = null,
        string? gisCategory = null)
    {
        ElementId = elementId;
        FamilyName = familyName ?? string.Empty;
        TypeName = typeName ?? string.Empty;
        GisKind = gisKind ?? string.Empty;
        GisCategory = gisCategory ?? string.Empty;
        InstanceIds = (instanceIds ?? Array.Empty<long>()).ToList();
    }

    public long ElementId { get; }

    public string FamilyName { get; }

    public string TypeName { get; }

    public string GisKind { get; }

    public string GisCategory { get; }

    public IReadOnlyList<long> InstanceIds { get; }
}

/// <summary>Everything the model tools read from one model.</summary>
public sealed class ModelSnapshot
{
    public ModelSnapshot(
        IEnumerable<BoundParameterInfo>? boundParameters = null,
        IEnumerable<FloorTypeInfo>? floorTypes = null,
        IEnumerable<FloorInfo>? floors = null,
        IEnumerable<MaterialInfo>? materials = null,
        IEnumerable<LevelInfo>? levels = null,
        IEnumerable<FamilyTypeInfo>? familyTypes = null,
        string? standardVersion = null)
    {
        BoundParameters = ByName(boundParameters, entry => entry.Name);
        FloorTypes = (floorTypes ?? Array.Empty<FloorTypeInfo>()).ToList();
        Floors = (floors ?? Array.Empty<FloorInfo>()).ToList();
        Materials = ByName(materials, entry => entry.Name);
        Levels = (levels ?? Array.Empty<LevelInfo>()).ToList();
        FamilyTypes = (familyTypes ?? Array.Empty<FamilyTypeInfo>()).ToList();
        StandardVersion = standardVersion ?? string.Empty;
    }

    /// <summary>Bound parameters by name; the last one of a name wins.</summary>
    public IReadOnlyDictionary<string, BoundParameterInfo> BoundParameters { get; }

    public IReadOnlyList<FloorTypeInfo> FloorTypes { get; }

    public IReadOnlyList<FloorInfo> Floors { get; }

    public IReadOnlyDictionary<string, MaterialInfo> Materials { get; }

    public IReadOnlyList<LevelInfo> Levels { get; }

    public IReadOnlyList<FamilyTypeInfo> FamilyTypes { get; }

    /// <summary>The model's <c>gis_standard_version</c> as text, empty when it was never set up.</summary>
    public string StandardVersion { get; }

    public FloorTypeInfo? FloorTypeByName(string name) => FloorTypes.FirstOrDefault(floorType => floorType.Name == name);

    private static Dictionary<string, T> ByName<T>(IEnumerable<T>? entries, Func<T, string> name)
    {
        Dictionary<string, T> result = new(StringComparer.Ordinal);
        foreach (T entry in entries ?? Array.Empty<T>())
        {
            result[name(entry)] = entry;
        }

        return result;
    }
}
