using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.GisTools;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.GisTools;

/// <summary>Reads the <see cref="ModelSnapshot"/> the GIS standard tools plan from. Reads only.</summary>
public static class ModelSnapshotReader
{
    /// <summary>The categories whose family instances can be escalators, elevators or ticket gates.</summary>
    public static IReadOnlyList<BuiltInCategory> FamilyCategories { get; } =
        new[] { BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_SpecialityEquipment };

    public static ModelSnapshot Read(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        Dictionary<long, List<long>> floorsByType = new();
        HashSet<long> levelsWithFloors = new();
        List<FloorInfo> floorsWithCategory = new();
        foreach (Floor floor in new FilteredElementCollector(document).OfClass(typeof(Floor)).WhereElementIsNotElementType().Cast<Floor>())
        {
            long typeId = floor.GetTypeId().Value;
            if (!floorsByType.TryGetValue(typeId, out List<long>? ids))
            {
                floorsByType[typeId] = ids = new List<long>();
            }

            ids.Add(floor.Id.Value);
            levelsWithFloors.Add(floor.LevelId.Value);
            string category = ReadText(floor, GisSharedParameters.Category);
            if (category.Length > 0)
            {
                floorsWithCategory.Add(new FloorInfo(floor.Id.Value, typeId, category));
            }
        }

        List<FloorTypeInfo> floorTypes = new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Select(type => new FloorTypeInfo(
                type.Id.Value,
                type.Name ?? string.Empty,
                ReadText(type, GisSharedParameters.GisCategory),
                ReadText(type, GisSharedParameters.GisRestriction),
                TopLayerMaterial(document, type),
                floorsByType.TryGetValue(type.Id.Value, out List<long>? ids) ? ids : null))
            .ToList();

        List<MaterialInfo> materials = new FilteredElementCollector(document)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .Select(material => new MaterialInfo(material.Id.Value, material.Name ?? string.Empty, ColorHex(material.Color)))
            .ToList();

        List<LevelInfo> levels = new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .Select(level => new LevelInfo(
                level.Id.Value,
                level.Name ?? string.Empty,
                ReadInteger(level, GisSharedParameters.Ordinal),
                ReadText(level, GisSharedParameters.ShortName),
                levelsWithFloors.Contains(level.Id.Value)))
            .ToList();

        return new ModelSnapshot(
            ReadBoundParameters(document),
            floorTypes,
            floorsWithCategory,
            materials,
            levels,
            ReadFamilyTypes(document),
            ReadText(document.ProjectInformation, GisSharedParameters.GisStandardVersion));
    }

    /// <summary>A text parameter's value, trimmed; empty when the parameter is missing or not text.</summary>
    public static string ReadText(Element? element, string name)
    {
        Parameter? parameter = element?.LookupParameter(name);
        return parameter is { StorageType: StorageType.String } ? GisToolStandard.Normalize(parameter.AsString()) : string.Empty;
    }

    /// <summary>
    /// A whole-number parameter's value, or null when it is missing or empty. Older models hold
    /// <c>ordinal</c> as text; a text value that is a whole number counts, as it does in the export.
    /// </summary>
    public static int? ReadInteger(Element? element, string name)
    {
        Parameter? parameter = element?.LookupParameter(name);
        if (parameter == null || !parameter.HasValue)
        {
            return null;
        }

        if (parameter.StorageType == StorageType.Integer)
        {
            return parameter.AsInteger();
        }

        return parameter.StorageType == StorageType.String &&
               int.TryParse(GisToolStandard.Normalize(parameter.AsString()), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value)
            ? value
            : null;
    }

    private static List<BoundParameterInfo> ReadBoundParameters(Document document)
    {
        List<BoundParameterInfo> bound = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition || iterator.Current is not ElementBinding binding || !seen.Add(definition.Name))
            {
                continue;
            }

            Guid? guid = document.GetElement(definition.Id) is SharedParameterElement shared ? shared.GuidValue : null;
            GisParameterBinding kind = binding is TypeBinding ? GisParameterBinding.Type : GisParameterBinding.Instance;
            List<string> categories = new();
            foreach (Category category in binding.Categories)
            {
                categories.Add(category.BuiltInCategory.ToString());
            }

            bound.Add(new BoundParameterInfo(definition.Name, guid, kind, categories));
        }

        return bound;
    }

    private static List<FamilyTypeInfo> ReadFamilyTypes(Document document)
    {
        Dictionary<long, (FamilySymbol Symbol, string Kind, string Category, List<long> Instances)> types = new();
        foreach (BuiltInCategory category in FamilyCategories)
        {
            foreach (FamilyInstance instance in new FilteredElementCollector(document).OfCategory(category).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>())
            {
                FamilySymbol symbol = instance.Symbol;
                if (!types.TryGetValue(symbol.Id.Value, out var entry))
                {
                    string kind = FirstNonEmpty(ReadText(instance, GisSharedParameters.GisKind), ReadText(symbol, GisSharedParameters.GisKind));
                    string gisCategory = FirstNonEmpty(ReadText(instance, GisSharedParameters.Category), ReadText(symbol, GisSharedParameters.GisCategory));
                    entry = (symbol, kind, gisCategory, new List<long>());
                    types[symbol.Id.Value] = entry;
                }

                entry.Instances.Add(instance.Id.Value);
            }
        }

        return types
            .Select(pair => new FamilyTypeInfo(pair.Key, pair.Value.Symbol.FamilyName, pair.Value.Symbol.Name, pair.Value.Kind, pair.Value.Instances, pair.Value.Category))
            .ToList();
    }

    private static string TopLayerMaterial(Document document, FloorType floorType)
    {
        CompoundStructure? structure = floorType.GetCompoundStructure();
        if (structure == null || structure.LayerCount == 0)
        {
            return string.Empty;
        }

        return document.GetElement(structure.GetMaterialId(0))?.Name ?? string.Empty;
    }

    private static string ColorHex(Color? color) =>
        color != null && color.IsValid ? GisToolStandard.RgbToHex(color.Red, color.Green, color.Blue) : string.Empty;

    private static string FirstNonEmpty(string first, string second) => first.Length > 0 ? first : second;
}
