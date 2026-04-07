using System;
using System.Collections.Generic;
using RevitGeoSuite.Core.Plateau.Codelists;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CodelistMapper
{
    private const string BuildingCodeSpace = "urn:revit-geosuite:codelist:building-usage:sample";
    private const string RoadCodeSpace = "urn:revit-geosuite:codelist:transportation-usage:sample";
    private const string VegetationCodeSpace = "urn:revit-geosuite:codelist:vegetation-usage:sample";

    private readonly CodelistRegistry buildingRegistry;
    private readonly CodelistRegistry roadRegistry;
    private readonly CodelistRegistry vegetationRegistry;

    public CodelistMapper(
        CodelistRegistry? buildingRegistry = null,
        CodelistRegistry? roadRegistry = null,
        CodelistRegistry? vegetationRegistry = null)
    {
        CodelistReader reader = new CodelistReader();
        this.buildingRegistry = buildingRegistry ?? new CodelistRegistry(reader.Read(DefaultBuildingUsageXml));
        this.roadRegistry = roadRegistry ?? new CodelistRegistry(reader.Read(DefaultRoadUsageXml));
        this.vegetationRegistry = vegetationRegistry ?? new CodelistRegistry(reader.Read(DefaultVegetationUsageXml));
    }

    public CityGmlCodeAssignment? Resolve(
        CityGmlSemanticType semanticType,
        string categoryName,
        IReadOnlyDictionary<string, string>? codelistOverrides)
    {
        string overrideKey = semanticType.ToString();
        if (codelistOverrides is not null
            && codelistOverrides.TryGetValue(overrideKey, out string? overrideCode)
            && !string.IsNullOrWhiteSpace(overrideCode))
        {
            return TryResolve(semanticType, overrideCode.Trim());
        }

        string lowered = (categoryName ?? string.Empty).ToLowerInvariant();
        if (semanticType == CityGmlSemanticType.Building)
        {
            if (lowered.Contains("office"))
            {
                return TryResolve(semanticType, "402");
            }

            if (lowered.Contains("residential") || lowered.Contains("housing"))
            {
                return TryResolve(semanticType, "401");
            }
        }

        if (semanticType == CityGmlSemanticType.Road && lowered.Contains("bridge"))
        {
            return TryResolve(semanticType, "2200");
        }

        if (semanticType == CityGmlSemanticType.Vegetation && lowered.Contains("tree"))
        {
            return TryResolve(semanticType, "5100");
        }

        return null;
    }

    public bool IsKnownCode(CityGmlSemanticType semanticType, string code)
    {
        return GetRegistry(semanticType).TryGetByCode(code, out _);
    }

    private CityGmlCodeAssignment? TryResolve(CityGmlSemanticType semanticType, string code)
    {
        CodelistRegistry registry = GetRegistry(semanticType);
        if (!registry.TryGetByCode(code, out CodelistEntry? entry) || entry is null)
        {
            return null;
        }

        return new CityGmlCodeAssignment
        {
            Code = entry.Code,
            Name = entry.Name,
            CodeSpace = GetCodeSpace(semanticType)
        };
    }

    private CodelistRegistry GetRegistry(CityGmlSemanticType semanticType)
    {
        return semanticType switch
        {
            CityGmlSemanticType.Building => buildingRegistry,
            CityGmlSemanticType.Road => roadRegistry,
            CityGmlSemanticType.Vegetation => vegetationRegistry,
            _ => new CodelistRegistry(Array.Empty<CodelistEntry>())
        };
    }

    private static string GetCodeSpace(CityGmlSemanticType semanticType)
    {
        return semanticType switch
        {
            CityGmlSemanticType.Building => BuildingCodeSpace,
            CityGmlSemanticType.Road => RoadCodeSpace,
            CityGmlSemanticType.Vegetation => VegetationCodeSpace,
            _ => string.Empty
        };
    }

    private const string DefaultBuildingUsageXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gml:Dictionary xmlns:gml=""http://www.opengis.net/gml""><gml:dictionaryEntry><gml:Definition gml:id=""401""><gml:name>Residential Building</gml:name><gml:description>Example residential building usage.</gml:description></gml:Definition></gml:dictionaryEntry><gml:dictionaryEntry><gml:Definition gml:id=""402""><gml:name>Office Building</gml:name><gml:description>Example office building usage.</gml:description></gml:Definition></gml:dictionaryEntry></gml:Dictionary>";

    private const string DefaultRoadUsageXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gml:Dictionary xmlns:gml=""http://www.opengis.net/gml""><gml:dictionaryEntry><gml:Definition gml:id=""2100""><gml:name>Road Surface</gml:name><gml:description>Example road usage code.</gml:description></gml:Definition></gml:dictionaryEntry><gml:dictionaryEntry><gml:Definition gml:id=""2200""><gml:name>Bridge Or Elevated Road</gml:name><gml:description>Example bridge or elevated transport usage code.</gml:description></gml:Definition></gml:dictionaryEntry></gml:Dictionary>";

    private const string DefaultVegetationUsageXml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<gml:Dictionary xmlns:gml=""http://www.opengis.net/gml""><gml:dictionaryEntry><gml:Definition gml:id=""5100""><gml:name>Tree Cover</gml:name><gml:description>Example vegetation usage code.</gml:description></gml:Definition></gml:dictionaryEntry><gml:dictionaryEntry><gml:Definition gml:id=""5200""><gml:name>Ground Cover</gml:name><gml:description>Example vegetation ground cover usage code.</gml:description></gml:Definition></gml:dictionaryEntry></gml:Dictionary>";
}
