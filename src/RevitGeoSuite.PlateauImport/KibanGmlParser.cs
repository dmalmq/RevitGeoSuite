using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace RevitGeoSuite.PlateauImport;

public sealed class KibanGmlParser
{
    private const string FgdNamespaceUri = "http://fgd.gsi.go.jp/spec/2008/FGD_GMLSchema";
    private const string GmlNamespaceUri = "http://www.opengis.net/gml/3.2";
    private const string SidewalkLayer = "GSI_SIDEWALKS";
    private const string RailwayLayer = "GSI_RAILWAYS";
    public const string WaterLayer = "GSI_WATER";
    public const string LandUseLayer = "GSI_LANDUSE";

    private static readonly XName RdComptName = XName.Get("RdCompt", FgdNamespaceUri);
    private static readonly XName RailCLName = XName.Get("RailCL", FgdNamespaceUri);
    private static readonly XName WaterAreaName = XName.Get("WA", FgdNamespaceUri);

    private static readonly Regex KibanMeshCodeRegex = new Regex(@"(?<!\d)(\d{6})(?!\d)", RegexOptions.Compiled);
    private static readonly string[] CoreGreenLandUseContainsTokens =
    {
        "緑地",
        "緑道",
        "庭園",
        "樹林",
        "森林",
        "山林",
        "草地",
    };

    public static readonly IReadOnlyList<string> OptionalGreenLandUseContainsTokens = new[]
    {
        "公園",
        "園地",
        "荒地",
    };

    private static readonly string[] GreenLandUseExactTokens =
    {
        "田",
        "畑",
    };

    public KibanParseResult ParseFile(string filePath)
    {
        return ParseFile(filePath, additionalGreenLandUseTokens: null);
    }

    public KibanParseResult ParseFile(string filePath, IReadOnlyCollection<string>? additionalGreenLandUseTokens)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Kiban GML file path cannot be empty.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Kiban GML file could not be found.", filePath);
        }

        XDocument document = XDocument.Load(filePath);
        XNamespace gml = ResolveGmlNamespace(document);

        string meshCode = ExtractKibanMeshCode(filePath) ?? string.Empty;

        List<KibanParsedFeature> lines = new List<KibanParsedFeature>();
        List<KibanParsedPolygonFeature> polygons = new List<KibanParsedPolygonFeature>();

        foreach (XElement element in document.Descendants())
        {
            if (element.Name == RdComptName)
            {
                KibanParsedFeature? feature = ParseRdCompt(element, gml, meshCode, filePath);
                if (feature is not null)
                {
                    lines.Add(feature);
                }
            }
            else if (element.Name == RailCLName)
            {
                KibanParsedFeature? feature = ParseRailCL(element, gml, meshCode, filePath);
                if (feature is not null)
                {
                    lines.Add(feature);
                }
            }
            else if (element.Name == WaterAreaName)
            {
                KibanParsedPolygonFeature? feature = ParseWaterArea(element, gml, meshCode, filePath);
                if (feature is not null)
                {
                    polygons.Add(feature);
                }
            }
            else
            {
                KibanParsedPolygonFeature? feature = ParseGreenLandUseArea(element, gml, meshCode, filePath, additionalGreenLandUseTokens);
                if (feature is not null)
                {
                    polygons.Add(feature);
                }
            }
        }

        return new KibanParseResult(lines, polygons);
    }

    private static KibanParsedPolygonFeature? ParseWaterArea(XElement element, XNamespace gml, string meshCode, string sourcePath)
    {
        XNamespace fgd = FgdNamespaceUri;
        XElement? areaElement = element.Element(fgd + "area");
        if (areaElement is null)
        {
            return null;
        }

        (List<List<(double Latitude, double Longitude)>> exteriorRings,
         List<List<(double Latitude, double Longitude)>> interiorRings) = ParseSurfaceRings(areaElement, gml);

        if (exteriorRings.Count == 0)
        {
            return null;
        }

        return new KibanParsedPolygonFeature
        {
            Layer = WaterLayer,
            ExteriorRings = exteriorRings,
            InteriorRings = interiorRings,
            MeshCode = meshCode,
            SourcePath = sourcePath,
            SourceId = GetGmlId(element, gml),
            Fid = GetChildValue(element, fgd + "fid"),
            FeatureType = GetChildValue(element, fgd + "type"),
            Visibility = GetChildValue(element, fgd + "vis")
        };
    }

    private static KibanParsedPolygonFeature? ParseGreenLandUseArea(XElement element, XNamespace gml, string meshCode, string sourcePath, IReadOnlyCollection<string>? additionalGreenTokens)
    {
        if (!string.Equals(element.Name.NamespaceName, FgdNamespaceUri, StringComparison.Ordinal))
        {
            return null;
        }

        XNamespace fgd = FgdNamespaceUri;
        string featureType = GetChildValue(element, fgd + "type");
        if (!IsGreenLandUseType(featureType, additionalGreenTokens))
        {
            return null;
        }

        XElement? areaElement = element.Element(fgd + "area");
        if (areaElement is null)
        {
            return null;
        }

        (List<List<(double Latitude, double Longitude)>> exteriorRings,
         List<List<(double Latitude, double Longitude)>> interiorRings) = ParseSurfaceRings(areaElement, gml);

        if (exteriorRings.Count == 0)
        {
            return null;
        }

        return new KibanParsedPolygonFeature
        {
            Layer = LandUseLayer,
            ExteriorRings = exteriorRings,
            InteriorRings = interiorRings,
            MeshCode = meshCode,
            SourcePath = sourcePath,
            SourceId = GetGmlId(element, gml),
            Fid = GetChildValue(element, fgd + "fid"),
            FeatureType = featureType,
            Visibility = GetChildValue(element, fgd + "vis")
        };
    }

    internal static bool IsGreenLandUseType(string featureType)
    {
        return IsGreenLandUseType(featureType, additionalGreenTokens: null);
    }

    internal static bool IsGreenLandUseType(string featureType, IReadOnlyCollection<string>? additionalGreenTokens)
    {
        string normalized = featureType?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            return false;
        }

        if (GreenLandUseExactTokens.Any(token => string.Equals(normalized, token, StringComparison.Ordinal)))
        {
            return true;
        }

        if (CoreGreenLandUseContainsTokens.Any(token => normalized.IndexOf(token, StringComparison.Ordinal) >= 0))
        {
            return true;
        }

        if (additionalGreenTokens is not null && additionalGreenTokens.Count > 0)
        {
            foreach (string token in additionalGreenTokens)
            {
                if (!string.IsNullOrEmpty(token)
                    && OptionalGreenLandUseContainsTokens.Contains(token, StringComparer.Ordinal)
                    && normalized.IndexOf(token, StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static (List<List<(double Latitude, double Longitude)>> ExteriorRings,
                    List<List<(double Latitude, double Longitude)>> InteriorRings)
        ParseSurfaceRings(XElement areaElement, XNamespace gml)
    {
        List<List<(double Latitude, double Longitude)>> exteriorRings = new List<List<(double Latitude, double Longitude)>>();
        List<List<(double Latitude, double Longitude)>> interiorRings = new List<List<(double Latitude, double Longitude)>>();

        foreach (XElement polygonPatch in areaElement.Descendants(gml + "PolygonPatch"))
        {
            foreach (XElement exteriorElement in polygonPatch.Elements(gml + "exterior"))
            {
                List<(double Latitude, double Longitude)>? ring = ParseRingVertices(exteriorElement, gml);
                if (ring is not null && ring.Count >= 3)
                {
                    exteriorRings.Add(ring);
                }
            }

            foreach (XElement interiorElement in polygonPatch.Elements(gml + "interior"))
            {
                List<(double Latitude, double Longitude)>? ring = ParseRingVertices(interiorElement, gml);
                if (ring is not null && ring.Count >= 3)
                {
                    interiorRings.Add(ring);
                }
            }
        }

        return (exteriorRings, interiorRings);
    }

    private static List<(double Latitude, double Longitude)>? ParseRingVertices(XElement boundaryElement, XNamespace gml)
    {
        List<(double Latitude, double Longitude)> vertices = new List<(double Latitude, double Longitude)>();
        foreach (XElement posListElement in boundaryElement.Descendants(gml + "posList"))
        {
            List<(double Latitude, double Longitude)> segment = ParsePosListCoordinates(posListElement.Value);
            foreach ((double latitude, double longitude) in segment)
            {
                if (vertices.Count == 0
                    || vertices[vertices.Count - 1].Latitude != latitude
                    || vertices[vertices.Count - 1].Longitude != longitude)
                {
                    vertices.Add((latitude, longitude));
                }
            }
        }

        return vertices.Count == 0 ? null : vertices;
    }

    internal static string? ExtractKibanMeshCode(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
        Match match = KibanMeshCodeRegex.Match(fileName);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static XNamespace ResolveGmlNamespace(XDocument document)
    {
        XElement? root = document.Root;
        if (root is not null)
        {
            foreach (XAttribute attribute in root.Attributes())
            {
                if (attribute.IsNamespaceDeclaration
                    && string.Equals(attribute.Value, GmlNamespaceUri, StringComparison.Ordinal))
                {
                    return XNamespace.Get(attribute.Value);
                }
            }

            XNamespace declaredGml = root.GetNamespaceOfPrefix("gml");
            if (!string.IsNullOrEmpty(declaredGml.NamespaceName))
            {
                return declaredGml;
            }
        }

        return XNamespace.Get(GmlNamespaceUri);
    }

    private static KibanParsedFeature? ParseRdCompt(XElement element, XNamespace gml, string meshCode, string sourcePath)
    {
        XNamespace fgd = FgdNamespaceUri;
        string featureType = GetChildValue(element, fgd + "type");
        if (!string.Equals(featureType, "歩道", StringComparison.Ordinal))
        {
            return null;
        }

        XElement? locElement = element.Element(fgd + "loc");
        if (locElement is null)
        {
            return null;
        }

        List<(double Latitude, double Longitude)>? vertices = ParseCurveVertices(locElement, gml);
        if (vertices is null || vertices.Count < 2)
        {
            return null;
        }

        return new KibanParsedFeature
        {
            Layer = SidewalkLayer,
            Vertices = vertices,
            MeshCode = meshCode,
            SourcePath = sourcePath,
            SourceId = GetGmlId(element, gml),
            Fid = GetChildValue(element, fgd + "fid"),
            FeatureType = featureType,
            Visibility = GetChildValue(element, fgd + "vis")
        };
    }

    private static KibanParsedFeature? ParseRailCL(XElement element, XNamespace gml, string meshCode, string sourcePath)
    {
        XNamespace fgd = FgdNamespaceUri;
        string featureType = GetChildValue(element, fgd + "type");
        XElement? locElement = element.Element(fgd + "loc");
        if (locElement is null)
        {
            return null;
        }

        List<(double Latitude, double Longitude)>? vertices = ParseCurveVertices(locElement, gml);
        if (vertices is null || vertices.Count < 2)
        {
            return null;
        }

        return new KibanParsedFeature
        {
            Layer = RailwayLayer,
            Vertices = vertices,
            MeshCode = meshCode,
            SourcePath = sourcePath,
            SourceId = GetGmlId(element, gml),
            Fid = GetChildValue(element, fgd + "fid"),
            FeatureType = featureType,
            Visibility = GetChildValue(element, fgd + "vis")
        };
    }

    private static List<(double Latitude, double Longitude)>? ParseCurveVertices(XElement locElement, XNamespace gml)
    {
        XElement? curveElement = locElement.Element(gml + "Curve");
        if (curveElement is null)
        {
            return null;
        }

        List<(double Latitude, double Longitude)> vertices = new List<(double, double)>();
        foreach (XElement posListElement in curveElement.Descendants(gml + "posList"))
        {
            List<(double Latitude, double Longitude)> segmentVertices = ParsePosListCoordinates(posListElement.Value);
            foreach ((double latitude, double longitude) in segmentVertices)
            {
                if (vertices.Count == 0 || vertices[vertices.Count - 1].Latitude != latitude || vertices[vertices.Count - 1].Longitude != longitude)
                {
                    vertices.Add((latitude, longitude));
                }
            }
        }

        return vertices.Count == 0 ? null : vertices;
    }

    private static string GetGmlId(XElement element, XNamespace gml)
    {
        return (string?)element.Attribute(gml + "id") ?? string.Empty;
    }

    private static string GetChildValue(XElement element, XName name)
    {
        return element.Element(name)?.Value.Trim() ?? string.Empty;
    }

    internal static List<(double Latitude, double Longitude)> ParsePosListCoordinates(string posListText)
    {
        List<double> rawValues = ParseDoubleValues(posListText);
        if (rawValues.Count < 4)
        {
            return new List<(double, double)>();
        }

        List<(double Latitude, double Longitude)> vertices = new List<(double, double)>(rawValues.Count / 2);
        for (int index = 0; index <= rawValues.Count - 2; index += 2)
        {
            double latitude = rawValues[index];
            double longitude = rawValues[index + 1];
            vertices.Add((latitude, longitude));
        }

        return vertices;
    }

    internal static List<double> ParseDoubleValues(string rawText)
    {
        List<double> values = new List<double>();
        if (string.IsNullOrWhiteSpace(rawText))
        {
            return values;
        }

        int index = 0;
        while (TryReadNextDouble(rawText, ref index, out double value))
        {
            values.Add(value);
        }

        return values;
    }

    private static bool TryReadNextDouble(string rawText, ref int index, out double value)
    {
        int length = rawText.Length;
        while (index < length && char.IsWhiteSpace(rawText[index]))
        {
            index++;
        }

        if (index >= length)
        {
            value = 0d;
            return false;
        }

        bool isNegative = false;
        if (rawText[index] == '+' || rawText[index] == '-')
        {
            isNegative = rawText[index] == '-';
            index++;
        }

        double integerPart = 0d;
        bool hasDigits = false;
        while (index < length && char.IsDigit(rawText[index]))
        {
            hasDigits = true;
            integerPart = (integerPart * 10d) + (rawText[index] - '0');
            index++;
        }

        double fractionalPart = 0d;
        double divisor = 1d;
        if (index < length && rawText[index] == '.')
        {
            index++;
            while (index < length && char.IsDigit(rawText[index]))
            {
                hasDigits = true;
                fractionalPart = (fractionalPart * 10d) + (rawText[index] - '0');
                divisor *= 10d;
                index++;
            }
        }

        if (!hasDigits)
        {
            throw new FormatException("Encountered an invalid coordinate token while parsing Kiban geometry.");
        }

        int exponent = 0;
        bool exponentNegative = false;
        if (index < length && (rawText[index] == 'e' || rawText[index] == 'E'))
        {
            index++;
            if (index < length && (rawText[index] == '+' || rawText[index] == '-'))
            {
                exponentNegative = rawText[index] == '-';
                index++;
            }

            bool hasExponentDigits = false;
            while (index < length && char.IsDigit(rawText[index]))
            {
                hasExponentDigits = true;
                exponent = (exponent * 10) + (rawText[index] - '0');
                index++;
            }

            if (!hasExponentDigits)
            {
                throw new FormatException("Encountered an invalid exponent while parsing Kiban geometry.");
            }
        }

        if (index < length && !char.IsWhiteSpace(rawText[index]))
        {
            throw new FormatException("Encountered an invalid character while parsing Kiban geometry.");
        }

        double parsed = integerPart + (fractionalPart / divisor);
        if (exponent != 0)
        {
            parsed *= Math.Pow(10d, exponentNegative ? -exponent : exponent);
        }

        value = isNegative ? -parsed : parsed;
        return true;
    }
}
