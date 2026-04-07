using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitGeoSuite.CityGmlExport;

public sealed class AttributeMapper
{
    public IReadOnlyCollection<CityGmlAttribute> Map(Element element)
    {
        if (element is null)
        {
            throw new ArgumentNullException(nameof(element));
        }

        string categoryName = element.Category?.Name ?? "Uncategorized";
        string elementName = string.IsNullOrWhiteSpace(element.Name) ? $"Element {element.Id.Value}" : element.Name;

        List<CityGmlAttribute> attributes = new List<CityGmlAttribute>
        {
            new CityGmlAttribute { Name = "revitElementId", Value = element.Id.Value.ToString() },
            new CityGmlAttribute { Name = "revitCategory", Value = categoryName },
            new CityGmlAttribute { Name = "revitName", Value = elementName }
        };

        string? typeName = element.Document.GetElement(element.GetTypeId())?.Name;
        if (!string.IsNullOrWhiteSpace(typeName))
        {
            attributes.Add(new CityGmlAttribute { Name = "revitTypeName", Value = typeName ?? string.Empty });
        }

        return attributes;
    }

    public IReadOnlyCollection<CityGmlAttribute> BuildBasicAttributes(string elementId, string categoryName, string elementName, string? typeName = null)
    {
        List<CityGmlAttribute> attributes = new List<CityGmlAttribute>
        {
            new CityGmlAttribute { Name = "revitElementId", Value = elementId },
            new CityGmlAttribute { Name = "revitCategory", Value = categoryName },
            new CityGmlAttribute { Name = "revitName", Value = elementName }
        };

        if (!string.IsNullOrWhiteSpace(typeName))
        {
            attributes.Add(new CityGmlAttribute { Name = "revitTypeName", Value = typeName! });
        }

        return attributes;
    }
}

