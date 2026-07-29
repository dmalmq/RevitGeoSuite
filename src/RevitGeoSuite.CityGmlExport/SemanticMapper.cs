using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.CityGmlExport;

public sealed class SemanticMapper
{
    public CityGmlSemanticType Map(Element element, IReadOnlyDictionary<string, string>? overrides = null)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        return MapCategoryName(element.Category?.Name, overrides);
    }

    public CityGmlSemanticType MapCategoryName(string? categoryName, IReadOnlyDictionary<string, string>? overrides = null)
    {
        string normalized = (categoryName ?? string.Empty).Trim();
        if (overrides is not null
            && !string.IsNullOrWhiteSpace(normalized)
            && overrides.TryGetValue(normalized, out string? overrideValue)
            && Enum.TryParse(overrideValue, ignoreCase: true, out CityGmlSemanticType overriddenSemantic))
        {
            return overriddenSemantic;
        }

        string lowered = normalized.ToLowerInvariant();
        if (lowered.Contains("road")
            || lowered.Contains("street")
            || lowered.Contains("bridge")
            || lowered.Contains("rail")
            || lowered.Contains("parking"))
        {
            return CityGmlSemanticType.Road;
        }

        if (lowered.Contains("plant")
            || lowered.Contains("tree")
            || lowered.Contains("veget")
            || lowered.Contains("landscape"))
        {
            return CityGmlSemanticType.Vegetation;
        }

        if (lowered.Contains("topo")
            || lowered.Contains("terrain")
            || lowered.Contains("site")
            || lowered.Contains("graded"))
        {
            return CityGmlSemanticType.Relief;
        }

        return CityGmlSemanticType.Building;
    }
}
