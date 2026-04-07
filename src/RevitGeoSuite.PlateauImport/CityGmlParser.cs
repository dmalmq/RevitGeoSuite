using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using RevitGeoSuite.Core.Plateau.Schema;

namespace RevitGeoSuite.PlateauImport;

public sealed class CityGmlParser
{
    private static readonly SupportedFeatureDescriptor[] SupportedFeatures =
    {
        new SupportedFeatureDescriptor(PlateauConstants.BuildingNamespace, PlateauFeatureType.Building, "Building"),
        new SupportedFeatureDescriptor(PlateauConstants.BridgeNamespace, PlateauFeatureType.Bridge, "Bridge", "BridgePart", "BridgeConstructionElement"),
        new SupportedFeatureDescriptor(PlateauConstants.TransportationNamespace, PlateauFeatureType.Road, "Road", "TrafficArea", "AuxiliaryTrafficArea"),
        new SupportedFeatureDescriptor(PlateauConstants.VegetationNamespace, PlateauFeatureType.Vegetation, "SolitaryVegetationObject", "PlantCover"),
        new SupportedFeatureDescriptor(PlateauConstants.ReliefNamespace, PlateauFeatureType.Relief, "ReliefFeature", "TINRelief", "MassPointRelief", "BreaklineRelief")
    };

    public PlateauCityModel ParseFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("CityGML file path cannot be empty.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("CityGML file could not be found.", filePath);
        }

        XDocument document = XDocument.Load(filePath, LoadOptions.SetLineInfo);
        XNamespace gml = PlateauConstants.GmlNamespace;

        string srsName = document.Root?.Attribute("srsName")?.Value
            ?? document.Descendants(gml + "Envelope").Attributes("srsName").Select(attribute => attribute.Value).FirstOrDefault()
            ?? string.Empty;

        int? epsgCode = PlateauSchemaHelper.TryExtractEpsgCode(srsName, out int parsedEpsg)
            ? parsedEpsg
            : null;

        string tileId = PlateauSchemaHelper.TryExtractTileIdFromPath(filePath)
            ?? Path.GetFileNameWithoutExtension(filePath)
            ?? string.Empty;

        List<PlateauContextFeature> features = new List<PlateauContextFeature>();
        foreach (SupportedFeatureDescriptor descriptor in SupportedFeatures)
        {
            foreach (string localName in descriptor.LocalNames)
            {
                foreach (XElement element in document.Descendants(descriptor.Namespace + localName))
                {
                    if (ShouldSkipFeatureElement(descriptor.FeatureType, descriptor.Namespace, localName, element))
                    {
                        continue;
                    }

                    features.AddRange(ParseFeatures(element, descriptor.FeatureType, gml, filePath, tileId));
                }
            }
        }

        return new PlateauCityModel
        {
            SourcePath = filePath,
            SrsName = PlateauSchemaHelper.NormalizeSrsName(srsName),
            EpsgCode = epsgCode,
            FileTileId = PlateauSchemaHelper.TryExtractTileIdFromPath(filePath),
            Features = features
        };
    }

    private static bool ShouldSkipFeatureElement(PlateauFeatureType featureType, XNamespace featureNamespace, string localName, XElement featureElement)
    {
        if (featureType != PlateauFeatureType.Road)
        {
            return false;
        }

        if (string.Equals(localName, "Road", StringComparison.OrdinalIgnoreCase))
        {
            return featureElement.Descendants(featureNamespace + "TrafficArea").Any()
                || featureElement.Descendants(featureNamespace + "AuxiliaryTrafficArea").Any();
        }

        if (!string.Equals(localName, "TrafficArea", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(localName, "AuxiliaryTrafficArea", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        XElement? parentRoad = featureElement.Ancestors(featureNamespace + "Road").FirstOrDefault();
        if (parentRoad is null)
        {
            return false;
        }

        int currentMaxLod = DetermineElementMaxLod(featureElement);
        int parentMaxChildLod = parentRoad
            .Elements(featureNamespace + "trafficArea")
            .Elements()
            .Concat(parentRoad.Elements(featureNamespace + "auxiliaryTrafficArea").Elements())
            .Select(DetermineElementMaxLod)
            .DefaultIfEmpty(0)
            .Max();

        return currentMaxLod > 0 && currentMaxLod < parentMaxChildLod;
    }

    private static int DetermineElementMaxLod(XElement element)
    {
        int maxLod = 0;
        foreach (string localName in element
                     .DescendantsAndSelf()
                     .Select(descendant => descendant.Name.LocalName))
        {
            if (!localName.StartsWith("lod", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int digitStart = 3;
            int digitLength = 0;
            while (digitStart + digitLength < localName.Length && char.IsDigit(localName[digitStart + digitLength]))
            {
                digitLength++;
            }

            if (digitLength == 0)
            {
                continue;
            }

            if (!int.TryParse(localName.Substring(digitStart, digitLength), NumberStyles.Integer, CultureInfo.InvariantCulture, out int lod))
            {
                continue;
            }

            if (lod > maxLod)
            {
                maxLod = lod;
            }
        }

        return maxLod;
    }

    private static IReadOnlyCollection<PlateauContextFeature> ParseFeatures(
        XElement featureElement,
        PlateauFeatureType featureType,
        XNamespace gml,
        string sourcePath,
        string tileId)
    {
        List<RingCandidate> candidates = GetRingCandidates(featureElement, gml);
        if (candidates.Count == 0)
        {
            return Array.Empty<PlateauContextFeature>();
        }

        string baseId = (string?)featureElement.Attribute(gml + "id") ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string baseName = featureElement.Elements(gml + "name").Select(element => element.Value).FirstOrDefault()
            ?? featureElement.Name.LocalName;

        if (!ShouldPreserveAllTransportRings(featureType))
        {
            RingCandidate selectedRing = SelectBestRing(candidates)!;
            PlateauContextFeature feature = CreateFeature(featureType, baseId, baseName, sourcePath, tileId, selectedRing.Coordinates);
            return new[] { feature };
        }

        List<PlateauContextFeature> features = new List<PlateauContextFeature>(candidates.Count);
        bool needsSuffix = candidates.Count > 1;
        foreach (RingCandidate candidate in candidates)
        {
            string featureId = needsSuffix
                ? string.Format(CultureInfo.InvariantCulture, "{0}::{1}", baseId, candidate.Sequence + 1)
                : baseId;
            string featureName = needsSuffix
                ? string.Format(CultureInfo.InvariantCulture, "{0} [{1}]", baseName, candidate.Sequence + 1)
                : baseName;
            features.Add(CreateFeature(featureType, featureId, featureName, sourcePath, tileId, candidate.Coordinates));
        }

        return features;
    }

    private static bool ShouldPreserveAllTransportRings(PlateauFeatureType featureType)
    {
        return featureType == PlateauFeatureType.Road;
    }

    private static PlateauContextFeature CreateFeature(
        PlateauFeatureType featureType,
        string id,
        string name,
        string sourcePath,
        string tileId,
        PlateauCoordinate3D[] coordinates)
    {
        PlateauContextFeature feature = featureType == PlateauFeatureType.Building
            ? new PlateauBuildingFeature()
            : new PlateauContextFeature { FeatureType = featureType };

        feature.Id = id;
        feature.Name = name;
        feature.SourcePath = sourcePath;
        feature.TileId = tileId;
        feature.ExteriorRing = coordinates;
        return feature;
    }

    private static List<RingCandidate> GetRingCandidates(XElement featureElement, XNamespace gml)
    {
        return featureElement
            .Descendants(gml + "LinearRing")
            .Select((ring, index) => CreateCandidate(ring, featureElement, gml, index))
            .Where(candidate => candidate is not null && candidate.Coordinates.Length >= 4)
            .Select(candidate => candidate!)
            .ToList();
    }

    private static RingCandidate? SelectBestRing(IReadOnlyCollection<RingCandidate> candidates)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderBy(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.HasPlanArea ? 0 : 1)
            .ThenBy(candidate => candidate.AverageZ)
            .ThenByDescending(candidate => candidate.PlanArea)
            .First();
    }

    private static RingCandidate? CreateCandidate(XElement ringElement, XElement featureElement, XNamespace gml, int sequence)
    {
        PlateauCoordinate3D[] coordinates = ParseCoordinates(ringElement, gml);
        if (coordinates.Length < 4)
        {
            return null;
        }

        string[] ancestorNames = ringElement
            .Ancestors()
            .TakeWhile(element => element != featureElement)
            .Select(element => element.Name.LocalName)
            .ToArray();

        double planArea = ComputePlanArea(coordinates);
        return new RingCandidate
        {
            Coordinates = coordinates,
            Sequence = sequence,
            Priority = DeterminePriority(ancestorNames),
            PlanArea = planArea,
            HasPlanArea = planArea > 0.000001d,
            AverageZ = coordinates.Average(point => point.Z)
        };
    }

    private static int DeterminePriority(IReadOnlyCollection<string> ancestorNames)
    {
        if (ancestorNames.Any(name => string.Equals(name, "GroundSurface", StringComparison.OrdinalIgnoreCase)))
        {
            return 0;
        }

        if (ancestorNames.Any(name => name.StartsWith("lod", StringComparison.OrdinalIgnoreCase) && !name.StartsWith("lod0", StringComparison.OrdinalIgnoreCase)))
        {
            return 1;
        }

        if (ancestorNames.Any(name => string.Equals(name, "lod0FootPrint", StringComparison.OrdinalIgnoreCase)))
        {
            return 2;
        }

        return 3;
    }

    private static double ComputePlanArea(IReadOnlyList<PlateauCoordinate3D> coordinates)
    {
        if (coordinates.Count < 3)
        {
            return 0d;
        }

        double areaTwice = 0d;
        for (int index = 0; index < coordinates.Count; index++)
        {
            PlateauCoordinate3D current = coordinates[index];
            PlateauCoordinate3D next = coordinates[(index + 1) % coordinates.Count];
            areaTwice += (current.X * next.Y) - (next.X * current.Y);
        }

        return Math.Abs(areaTwice) * 0.5d;
    }

    private static PlateauCoordinate3D[] ParseCoordinates(XElement ringElement, XNamespace gml)
    {
        XElement? posListElement = ringElement.Element(gml + "posList");
        if (posListElement is not null)
        {
            return ParsePosListCoordinates(posListElement);
        }

        XElement[] posElements = ringElement.Elements(gml + "pos").ToArray();
        if (posElements.Length == 0)
        {
            return Array.Empty<PlateauCoordinate3D>();
        }

        List<PlateauCoordinate3D> coordinates = new List<PlateauCoordinate3D>(posElements.Length);
        foreach (XElement posElement in posElements)
        {
            string[] rawValues = (posElement.Value ?? string.Empty)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (rawValues.Length < 2)
            {
                continue;
            }

            double x = double.Parse(rawValues[0], CultureInfo.InvariantCulture);
            double y = double.Parse(rawValues[1], CultureInfo.InvariantCulture);
            double z = rawValues.Length >= 3
                ? double.Parse(rawValues[2], CultureInfo.InvariantCulture)
                : 0d;
            coordinates.Add(new PlateauCoordinate3D(x, y, z));
        }

        return coordinates.ToArray();
    }

    private static PlateauCoordinate3D[] ParsePosListCoordinates(XElement posListElement)
    {
        string[] rawValues = (posListElement.Value ?? string.Empty)
            .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        if (rawValues.Length < 6)
        {
            return Array.Empty<PlateauCoordinate3D>();
        }

        int dimension;
        if (int.TryParse((string?)posListElement.Attribute("srsDimension"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDimension)
            && parsedDimension >= 2)
        {
            dimension = parsedDimension;
        }
        else if (rawValues.Length % 3 == 0)
        {
            dimension = 3;
        }
        else
        {
            dimension = 2;
        }

        List<PlateauCoordinate3D> coordinates = new List<PlateauCoordinate3D>(rawValues.Length / dimension);
        for (int index = 0; index <= rawValues.Length - dimension; index += dimension)
        {
            double x = double.Parse(rawValues[index], CultureInfo.InvariantCulture);
            double y = double.Parse(rawValues[index + 1], CultureInfo.InvariantCulture);
            double z = dimension >= 3
                ? double.Parse(rawValues[index + 2], CultureInfo.InvariantCulture)
                : 0d;
            coordinates.Add(new PlateauCoordinate3D(x, y, z));
        }

        return coordinates.ToArray();
    }

    private sealed class SupportedFeatureDescriptor
    {
        public SupportedFeatureDescriptor(XNamespace @namespace, PlateauFeatureType featureType, params string[] localNames)
        {
            Namespace = @namespace;
            FeatureType = featureType;
            LocalNames = localNames;
        }

        public XNamespace Namespace { get; }

        public PlateauFeatureType FeatureType { get; }

        public IReadOnlyCollection<string> LocalNames { get; }
    }

    private sealed class RingCandidate
    {
        public PlateauCoordinate3D[] Coordinates { get; set; } = Array.Empty<PlateauCoordinate3D>();

        public int Sequence { get; set; }

        public int Priority { get; set; }

        public bool HasPlanArea { get; set; }

        public double PlanArea { get; set; }

        public double AverageZ { get; set; }
    }
}
