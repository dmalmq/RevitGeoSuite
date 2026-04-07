using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RevitGeoSuite.Core.Plateau.Schema;

namespace RevitGeoSuite.CityGmlExport;

public sealed class CityGmlWriter
{
    private static readonly XNamespace Core = PlateauConstants.CoreNamespace;
    private static readonly XNamespace Building = PlateauConstants.BuildingNamespace;
    private static readonly XNamespace Transportation = PlateauConstants.TransportationNamespace;
    private static readonly XNamespace Vegetation = PlateauConstants.VegetationNamespace;
    private static readonly XNamespace Relief = PlateauConstants.ReliefNamespace;
    private static readonly XNamespace Gml = PlateauConstants.GmlNamespace;
    private static readonly XNamespace Generic = "http://www.opengis.net/citygml/generics/2.0";
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public string BuildXml(CityGmlExportPackage package)
    {
        if (package is null)
        {
            throw new ArgumentNullException(nameof(package));
        }

        XElement root = new XElement(
            Core + "CityModel",
            new XAttribute(XNamespace.Xmlns + "core", Core),
            new XAttribute(XNamespace.Xmlns + "bldg", Building),
            new XAttribute(XNamespace.Xmlns + "tran", Transportation),
            new XAttribute(XNamespace.Xmlns + "veg", Vegetation),
            new XAttribute(XNamespace.Xmlns + "dem", Relief),
            new XAttribute(XNamespace.Xmlns + "gml", Gml),
            new XAttribute(XNamespace.Xmlns + "gen", Generic),
            new XAttribute(XNamespace.Xmlns + "xsi", Xsi),
            new XAttribute("srsName", package.ReferenceContext.SrsName),
            BuildEnvelope(package.ReferenceContext.SrsName, package.Features));

        foreach (CityGmlFeature feature in package.Features)
        {
            root.Add(new XElement(Core + "cityObjectMember", BuildFeatureElement(feature, package.ReferenceContext.SrsName)));
        }

        XDocument document = new XDocument(new XDeclaration("1.0", "utf-8", "yes"), root);
        return document.ToString(SaveOptions.DisableFormatting);
    }

    public string Write(string outputDirectory, CityGmlExportPackage package)
    {
        if (string.IsNullOrWhiteSpace(outputDirectory))
        {
            throw new ArgumentException("CityGML output directory cannot be empty.", nameof(outputDirectory));
        }

        Directory.CreateDirectory(outputDirectory);
        string path = Path.Combine(outputDirectory, package.OutputFileName);
        File.WriteAllText(path, BuildXml(package));
        return path;
    }

    private static XElement BuildEnvelope(string srsName, IReadOnlyList<CityGmlFeature> features)
    {
        List<CityGmlCoordinate> coordinates = features
            .SelectMany(feature => feature.Surfaces)
            .SelectMany(surface => surface.ExteriorRing)
            .ToList();

        if (coordinates.Count == 0)
        {
            return new XElement(
                Gml + "boundedBy",
                new XElement(
                    Gml + "Envelope",
                    new XAttribute("srsName", srsName),
                    new XElement(Gml + "lowerCorner", "0 0 0"),
                    new XElement(Gml + "upperCorner", "0 0 0")));
        }

        double minX = coordinates.Min(point => point.X);
        double minY = coordinates.Min(point => point.Y);
        double minZ = coordinates.Min(point => point.Z);
        double maxX = coordinates.Max(point => point.X);
        double maxY = coordinates.Max(point => point.Y);
        double maxZ = coordinates.Max(point => point.Z);

        return new XElement(
            Gml + "boundedBy",
            new XElement(
                Gml + "Envelope",
                new XAttribute("srsName", srsName),
                new XElement(Gml + "lowerCorner", string.Format(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3}", minX, minY, minZ)),
                new XElement(Gml + "upperCorner", string.Format(CultureInfo.InvariantCulture, "{0:F3} {1:F3} {2:F3}", maxX, maxY, maxZ))));
    }

    private static XElement BuildFeatureElement(CityGmlFeature feature, string srsName)
    {
        XElement element = feature.SemanticType switch
        {
            CityGmlSemanticType.Building => new XElement(Building + "Building"),
            CityGmlSemanticType.Road => new XElement(Transportation + "Road"),
            CityGmlSemanticType.Vegetation => new XElement(Vegetation + "PlantCover"),
            CityGmlSemanticType.Relief => new XElement(Relief + "ReliefFeature"),
            _ => new XElement(Building + "Building")
        };

        element.Add(new XAttribute(Gml + "id", feature.Id));
        element.Add(new XElement(Gml + "name", feature.Name));
        foreach (CityGmlAttribute attribute in feature.Attributes)
        {
            element.Add(
                new XElement(
                    Generic + "stringAttribute",
                    new XAttribute("name", attribute.Name),
                    new XElement(Generic + "value", attribute.Value)));
        }

        if (feature.CodeAssignment is not null)
        {
            element.Add(BuildCodeElement(feature.SemanticType, feature.CodeAssignment));
        }

        element.Add(BuildGeometryElement(feature, srsName));
        return element;
    }

    private static XElement BuildCodeElement(CityGmlSemanticType semanticType, CityGmlCodeAssignment codeAssignment)
    {
        XName name = semanticType switch
        {
            CityGmlSemanticType.Building => Building + "function",
            CityGmlSemanticType.Road => Transportation + "function",
            CityGmlSemanticType.Vegetation => Vegetation + "function",
            _ => Core + "externalReference"
        };

        return new XElement(name, new XAttribute("codeSpace", codeAssignment.CodeSpace), codeAssignment.Code);
    }

    private static XElement BuildGeometryElement(CityGmlFeature feature, string srsName)
    {
        return feature.SemanticType switch
        {
            CityGmlSemanticType.Building => new XElement(Building + "lod2MultiSurface", BuildMultiSurface(feature.Surfaces, srsName)),
            CityGmlSemanticType.Road => new XElement(Transportation + "lod2MultiSurface", BuildMultiSurface(feature.Surfaces, srsName)),
            CityGmlSemanticType.Vegetation => new XElement(Vegetation + "lod2MultiSurface", BuildMultiSurface(feature.Surfaces, srsName)),
            CityGmlSemanticType.Relief => new XElement(
                Relief + "reliefComponent",
                new XElement(
                    Relief + "TINRelief",
                    new XAttribute(Gml + "id", $"{feature.Id}-tin"),
                    new XElement(Relief + "lod", 1),
                    new XElement(Relief + "tin", BuildTriangulatedSurface(feature.Surfaces, srsName)))),
            _ => new XElement(Building + "lod2MultiSurface", BuildMultiSurface(feature.Surfaces, srsName))
        };
    }

    private static XElement BuildMultiSurface(IReadOnlyList<CityGmlSurface> surfaces, string srsName)
    {
        XElement multiSurface = new XElement(Gml + "MultiSurface", new XAttribute("srsName", srsName));
        foreach (CityGmlSurface surface in surfaces)
        {
            multiSurface.Add(new XElement(Gml + "surfaceMember", BuildPolygon(surface)));
        }

        return multiSurface;
    }

    private static XElement BuildTriangulatedSurface(IReadOnlyList<CityGmlSurface> surfaces, string srsName)
    {
        XElement triangulatedSurface = new XElement(Gml + "TriangulatedSurface", new XAttribute("srsName", srsName));
        XElement patches = new XElement(Gml + "trianglePatches");
        foreach (CityGmlSurface surface in surfaces)
        {
            patches.Add(new XElement(Gml + "Triangle", BuildExterior(surface)));
        }

        triangulatedSurface.Add(patches);
        return triangulatedSurface;
    }

    private static XElement BuildPolygon(CityGmlSurface surface)
    {
        return new XElement(
            Gml + "Polygon",
            BuildExterior(surface));
    }

    private static XElement BuildExterior(CityGmlSurface surface)
    {
        return new XElement(
            Gml + "exterior",
            new XElement(
                Gml + "LinearRing",
                new XElement(Gml + "posList", string.Join(" ", EnsureClosedRing(surface.ExteriorRing).Select(point => point.ToPosString())))));
    }

    private static IReadOnlyList<CityGmlCoordinate> EnsureClosedRing(IReadOnlyList<CityGmlCoordinate> coordinates)
    {
        if (coordinates.Count == 0)
        {
            return coordinates;
        }

        CityGmlCoordinate first = coordinates[0];
        CityGmlCoordinate last = coordinates[coordinates.Count - 1];
        if (first.X.Equals(last.X) && first.Y.Equals(last.Y) && first.Z.Equals(last.Z))
        {
            return coordinates;
        }

        List<CityGmlCoordinate> closed = new List<CityGmlCoordinate>(coordinates);
        closed.Add(first);
        return closed;
    }
}

