using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RevitGeoSuite.Core.Plateau.Schema;

namespace RevitGeoSuite.Core.Plateau.Codelists;

public sealed class CodelistReader
{
    public IReadOnlyCollection<CodelistEntry> ReadFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Codelist file path cannot be empty.", nameof(filePath));
        }

        return Read(File.ReadAllText(filePath));
    }

    public IReadOnlyCollection<CodelistEntry> Read(string xmlContent)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return Array.Empty<CodelistEntry>();
        }

        XDocument document = XDocument.Parse(xmlContent, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        XNamespace gml = PlateauConstants.GmlNamespace;
        List<CodelistEntry> entries = new List<CodelistEntry>();

        foreach (XElement element in document.Descendants())
        {
            string localName = element.Name.LocalName;
            if (!string.Equals(localName, "Definition", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(localName, "entry", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(localName, "codeEntry", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string code = (string?)element.Attribute(gml + "id")
                ?? (string?)element.Attribute("code")
                ?? element.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, "identifier", StringComparison.OrdinalIgnoreCase))?.Value
                ?? string.Empty;
            string name = element.Elements().FirstOrDefault(child => child.Name == gml + "name")?.Value
                ?? element.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, "name", StringComparison.OrdinalIgnoreCase))?.Value
                ?? code;
            string description = element.Elements().FirstOrDefault(child => child.Name == gml + "description")?.Value
                ?? element.Elements().FirstOrDefault(child => string.Equals(child.Name.LocalName, "description", StringComparison.OrdinalIgnoreCase))?.Value
                ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(code))
            {
                entries.Add(new CodelistEntry
                {
                    Code = code.Trim(),
                    Name = (name ?? string.Empty).Trim(),
                    Description = (description ?? string.Empty).Trim()
                });
            }
        }

        return entries
            .GroupBy(entry => entry.Code, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
    }
}
