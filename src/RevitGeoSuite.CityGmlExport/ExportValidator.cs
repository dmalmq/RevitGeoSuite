using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using RevitGeoSuite.Core.Plateau.Schema;

namespace RevitGeoSuite.CityGmlExport;

public sealed class ExportValidator
{
    private static readonly XNamespace Core = PlateauConstants.CoreNamespace;
    private static readonly XNamespace Gml = PlateauConstants.GmlNamespace;

    public CityGmlValidationReport Validate(string xmlContent, CityGmlExportPackage package)
    {
        if (string.IsNullOrWhiteSpace(xmlContent))
        {
            return new CityGmlValidationReport
            {
                Errors = new[] { "The generated CityGML document is empty." }
            };
        }

        List<string> errors = new List<string>();
        List<string> warnings = new List<string>();
        XDocument document = XDocument.Parse(xmlContent, LoadOptions.PreserveWhitespace | LoadOptions.SetLineInfo);
        XElement? root = document.Root;
        if (root is null || root.Name != Core + "CityModel")
        {
            errors.Add("The generated XML root is not core:CityModel.");
            return new CityGmlValidationReport { Errors = errors, Warnings = warnings };
        }

        string? srsName = (string?)root.Attribute("srsName")
            ?? (string?)root.Element(Gml + "boundedBy")?.Element(Gml + "Envelope")?.Attribute("srsName");
        if (string.IsNullOrWhiteSpace(srsName))
        {
            errors.Add("The CityModel does not declare an srsName.");
        }

        IReadOnlyCollection<XElement> members = root.Elements(Core + "cityObjectMember").ToArray();
        if (members.Count != package.Features.Count)
        {
            errors.Add($"The CityModel contains {members.Count} cityObjectMember elements, but {package.Features.Count} features were prepared.");
        }

        foreach (XElement polygon in document.Descendants(Gml + "Polygon").Concat(document.Descendants(Gml + "Triangle")))
        {
            string? posList = polygon.Descendants(Gml + "posList").Select(element => element.Value).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(posList))
            {
                errors.Add("A geometry patch is missing gml:posList coordinates.");
                continue;
            }

            string[] tokens = posList.Split(new[] { ' ', '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 12 || tokens.Length % 3 != 0)
            {
                errors.Add("A geometry patch does not contain a valid 3D coordinate list.");
                continue;
            }

            foreach (string token in tokens)
            {
                if (!double.TryParse(token, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out _))
                {
                    errors.Add("A geometry patch contains a non-numeric coordinate token.");
                    break;
                }
            }
        }

        if (package.ValidationReport.Warnings.Count > 0)
        {
            warnings.AddRange(package.ValidationReport.Warnings);
        }

        return new CityGmlValidationReport
        {
            Errors = errors.Distinct(StringComparer.Ordinal).ToArray(),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }
}
