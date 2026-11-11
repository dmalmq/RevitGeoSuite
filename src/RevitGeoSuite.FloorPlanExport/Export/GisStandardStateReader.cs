using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.FloorPlanExport.Core.Standards;

namespace RevitGeoSuite.FloorPlanExport.Export;

/// <summary>Reads what <see cref="GisStandardCheck"/> needs from a Revit model. Reads only.</summary>
public static class GisStandardStateReader
{
    public static GisStandardStatus Check(Document document)
    {
        return GisStandardCheck.Check(Read(document));
    }

    public static GisStandardModelState Read(Document document)
    {
        if (document is null)
        {
            throw new ArgumentNullException(nameof(document));
        }

        return new GisStandardModelState
        {
            StandardVersionText = ReadText(document.ProjectInformation, GisSharedParameters.GisStandardVersion),
            BoundParameters = ReadBoundParameters(document),
            GisViewCount = new FilteredElementCollector(document)
                .OfClass(typeof(ViewPlan))
                .Cast<ViewPlan>()
                .Count(view => !view.IsTemplate &&
                               (view.Name ?? string.Empty).StartsWith(GisModelFile.GisViewPrefix, StringComparison.OrdinalIgnoreCase)),
            FloorTypesWithoutCategory = ReadFloorTypesWithoutCategory(document),
        };
    }

    private static List<GisBoundParameterState> ReadBoundParameters(Document document)
    {
        List<GisBoundParameterState> bound = new();
        HashSet<string> seen = new(StringComparer.Ordinal);
        DefinitionBindingMapIterator iterator = document.ParameterBindings.ForwardIterator();
        iterator.Reset();
        while (iterator.MoveNext())
        {
            if (iterator.Key is not InternalDefinition definition || iterator.Current is not ElementBinding binding)
            {
                continue;
            }

            if (!seen.Add(definition.Name))
            {
                continue;
            }

            Guid? guid = document.GetElement(definition.Id) is SharedParameterElement shared ? shared.GuidValue : null;
            GisParameterBinding kind = binding is TypeBinding ? GisParameterBinding.Type : GisParameterBinding.Instance;
            bound.Add(new GisBoundParameterState(definition.Name, guid, kind));
        }

        return bound;
    }

    private static List<string> ReadFloorTypesWithoutCategory(Document document)
    {
        HashSet<long> typesInUse = new(new FilteredElementCollector(document)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Select(floor => floor.GetTypeId().Value));
        FloorTypeStandard standard = FloorTypeStandard.LoadEmbedded();
        HashSet<string> known = new(FloorTypeStandard.KnownCategories, StringComparer.OrdinalIgnoreCase);
        return new FilteredElementCollector(document)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Where(type => typesInUse.Contains(type.Id.Value))
            .Where(type => !standard.TryGetFloorType(type.Name, out _))
            .Where(type => !known.Contains(ReadText(type, GisSharedParameters.GisCategory)))
            .Select(type => type.Name ?? string.Empty)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    private static string ReadText(Element? element, string parameterName)
    {
        Parameter? parameter = element?.LookupParameter(parameterName);
        if (parameter == null || !parameter.HasValue)
        {
            return string.Empty;
        }

        string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return value?.Trim() ?? string.Empty;
    }
}
