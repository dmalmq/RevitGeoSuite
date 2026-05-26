using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using RevitGeoSuite.Core.Plateau.Codelists;
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
        new SupportedFeatureDescriptor(PlateauConstants.ReliefNamespace, PlateauFeatureType.Relief, "ReliefFeature", "TINRelief", "MassPointRelief", "BreaklineRelief"),
        new SupportedFeatureDescriptor(PlateauConstants.LandUseNamespace, PlateauFeatureType.LandUse, "LandUse")
    };
    private static readonly Dictionary<XName, SupportedFeatureDescriptor> SupportedFeatureLookup = BuildSupportedFeatureLookup();

    private readonly Dictionary<string, CodelistRegistry?> codelistCache = new Dictionary<string, CodelistRegistry?>(StringComparer.OrdinalIgnoreCase);
    private readonly CodelistReader codelistReader = new CodelistReader();

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

        XDocument document = XDocument.Load(filePath);
        XNamespace gml = PlateauConstants.GmlNamespace;
        Dictionary<SupportedFeatureDescriptor, List<XElement>> matchedElements = CreateMatchedElementBuckets();
        string envelopeSrsName = string.Empty;
        foreach (XElement element in document.Descendants())
        {
            if (string.IsNullOrWhiteSpace(envelopeSrsName)
                && element.Name == gml + "Envelope")
            {
                envelopeSrsName = (string?)element.Attribute("srsName") ?? string.Empty;
            }

            if (SupportedFeatureLookup.TryGetValue(element.Name, out SupportedFeatureDescriptor? descriptor))
            {
                matchedElements[descriptor].Add(element);
            }
        }

        string? srsName = document.Root?.Attribute("srsName")?.Value;
        if (string.IsNullOrWhiteSpace(srsName))
        {
            srsName = envelopeSrsName;
        }

        srsName ??= string.Empty;

        int? epsgCode = PlateauSchemaHelper.TryExtractEpsgCode(srsName, out int parsedEpsg)
            ? parsedEpsg
            : null;

        string tileId = PlateauSchemaHelper.TryExtractTileIdFromPath(filePath)
            ?? Path.GetFileNameWithoutExtension(filePath)
            ?? string.Empty;

        Dictionary<XElement, RoadChildSummary> roadChildSummaries = new Dictionary<XElement, RoadChildSummary>();
        List<PlateauContextFeature> features = new List<PlateauContextFeature>();
        for (int descriptorIndex = 0; descriptorIndex < SupportedFeatures.Length; descriptorIndex++)
        {
            SupportedFeatureDescriptor descriptor = SupportedFeatures[descriptorIndex];
            List<XElement> elements = matchedElements[descriptor];
            for (int elementIndex = 0; elementIndex < elements.Count; elementIndex++)
            {
                XElement element = elements[elementIndex];
                if (ShouldSkipFeatureElement(descriptor.FeatureType, descriptor.Namespace, element.Name.LocalName, element, roadChildSummaries))
                {
                    continue;
                }

                AppendFeatures(features, ParseFeatures(element, descriptor.FeatureType, gml, filePath, tileId));
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

    private static bool ShouldSkipFeatureElement(
        PlateauFeatureType featureType,
        XNamespace featureNamespace,
        string localName,
        XElement featureElement,
        IDictionary<XElement, RoadChildSummary> roadChildSummaries)
    {
        if (featureType == PlateauFeatureType.Bridge)
        {
            return HasNestedSupportedBridgeFeature(featureElement);
        }

        if (featureType != PlateauFeatureType.Road)
        {
            return false;
        }

        if (string.Equals(localName, "Road", StringComparison.OrdinalIgnoreCase))
        {
            return GetRoadChildSummary(featureElement, featureNamespace, roadChildSummaries).HasChildTrafficAreas;
        }

        if (!string.Equals(localName, "TrafficArea", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(localName, "AuxiliaryTrafficArea", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        XElement? parentRoad = null;
        for (XElement? ancestor = featureElement.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor.Name == featureNamespace + "Road")
            {
                parentRoad = ancestor;
                break;
            }
        }

        if (parentRoad is null)
        {
            return false;
        }

        int currentMaxLod = DetermineElementMaxLod(featureElement);
        int parentMaxChildLod = GetRoadChildSummary(parentRoad, featureNamespace, roadChildSummaries).MaxChildLod;
        return currentMaxLod > 0 && currentMaxLod < parentMaxChildLod;
    }

    private static bool HasNestedSupportedBridgeFeature(XElement featureElement)
    {
        foreach (XElement descendant in featureElement.Descendants())
        {
            if (descendant.Name.Namespace != PlateauConstants.BridgeNamespace)
            {
                continue;
            }

            if (IsSupportedBridgeFeature(descendant)
                && ContainsSupportedSurfaceGeometry(descendant))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSupportedBridgeFeature(XElement element)
    {
        if (element.Name.Namespace != PlateauConstants.BridgeNamespace)
        {
            return false;
        }

        string localName = element.Name.LocalName;
        return string.Equals(localName, "Bridge", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "BridgePart", StringComparison.OrdinalIgnoreCase)
            || string.Equals(localName, "BridgeConstructionElement", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsSupportedSurfaceGeometry(XElement element)
    {
        XNamespace gml = PlateauConstants.GmlNamespace;
        foreach (XElement descendant in element.Descendants())
        {
            if (descendant.Name == gml + "Polygon" || descendant.Name == gml + "Triangle")
            {
                return true;
            }
        }

        return false;
    }

    private static int DetermineElementMaxLod(XElement element)
    {
        int maxLod = 0;
        foreach (XElement descendant in element.DescendantsAndSelf())
        {
            int lod = ExtractLod(descendant.Name.LocalName);
            if (lod > maxLod)
            {
                maxLod = lod;
            }
        }

        return maxLod;
    }

    private IReadOnlyCollection<PlateauContextFeature> ParseFeatures(
        XElement featureElement,
        PlateauFeatureType featureType,
        XNamespace gml,
        string sourcePath,
        string tileId)
    {
        List<GeometrySurfaceCandidate> candidates = GetSurfaceCandidates(featureElement, gml);
        if (candidates.Count == 0)
        {
            return Array.Empty<PlateauContextFeature>();
        }

        (string classCode, string className) classification = featureType == PlateauFeatureType.LandUse
            ? ResolveLandUseClassification(featureElement, sourcePath)
            : (string.Empty, string.Empty);

        string baseId = (string?)featureElement.Attribute(gml + "id") ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string? baseName = null;
        foreach (XElement nameElement in featureElement.Elements(gml + "name"))
        {
            baseName = nameElement.Value;
            break;
        }

        baseName ??= featureElement.Name.LocalName;

        int highestLod = 0;
        for (int index = 0; index < candidates.Count; index++)
        {
            int candidateLod = candidates[index].Surface.Lod;
            if (candidateLod > highestLod)
            {
                highestLod = candidateLod;
            }
        }

        List<GeometrySurfaceCandidate> highestLodCandidates = new List<GeometrySurfaceCandidate>();
        for (int index = 0; index < candidates.Count; index++)
        {
            GeometrySurfaceCandidate candidate = candidates[index];
            if (candidate.Surface.Lod == highestLod)
            {
                highestLodCandidates.Add(candidate);
            }
        }

        if (!ShouldPreserveAllTransportRings(featureType))
        {
            GeometrySurfaceCandidate selectedCandidate = SelectBestSurface(candidates)!;
            PlateauContextFeature feature = CreateFeature(
                featureType,
                baseId,
                baseName,
                sourcePath,
                tileId,
                selectedCandidate.Coordinates,
                candidates,
                highestLod,
                BuildGeometrySurfaces(highestLodCandidates));
            feature.ClassCode = classification.classCode;
            feature.ClassName = classification.className;
            return new[] { feature };
        }

        List<PlateauContextFeature> features = new List<PlateauContextFeature>(highestLodCandidates.Count);
        bool needsSuffix = highestLodCandidates.Count > 1;
        for (int index = 0; index < highestLodCandidates.Count; index++)
        {
            GeometrySurfaceCandidate candidate = highestLodCandidates[index];
            string featureId = needsSuffix
                ? string.Format(CultureInfo.InvariantCulture, "{0}::{1}", baseId, index + 1)
                : baseId;
            string featureName = needsSuffix
                ? string.Format(CultureInfo.InvariantCulture, "{0} [{1}]", baseName, index + 1)
                : baseName;
            PlateauContextFeature feature = CreateFeature(
                featureType,
                featureId,
                featureName,
                sourcePath,
                tileId,
                candidate.Coordinates,
                new[] { candidate },
                candidate.Surface.Lod,
                new[] { candidate.Surface });
            feature.ClassCode = classification.classCode;
            feature.ClassName = classification.className;
            features.Add(feature);
        }

        return features;
    }

    private (string ClassCode, string ClassName) ResolveLandUseClassification(XElement featureElement, string sourcePath)
    {
        XElement? classElement = null;
        foreach (XElement child in featureElement.Elements())
        {
            if (child.Name.Namespace == PlateauConstants.LandUseNamespace
                && string.Equals(child.Name.LocalName, "class", StringComparison.Ordinal))
            {
                classElement = child;
                break;
            }
        }

        if (classElement is null)
        {
            return (string.Empty, string.Empty);
        }

        string code = (classElement.Value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code))
        {
            return (string.Empty, string.Empty);
        }

        string codeSpace = (string?)classElement.Attribute("codeSpace") ?? string.Empty;
        string name = ResolveCodelistName(sourcePath, codeSpace, code);
        return (code, name);
    }

    private string ResolveCodelistName(string sourcePath, string codeSpace, string code)
    {
        if (string.IsNullOrWhiteSpace(codeSpace) || string.IsNullOrWhiteSpace(code))
        {
            return string.Empty;
        }

        string? absoluteCodelistPath = TryResolveCodelistPath(sourcePath, codeSpace);
        if (absoluteCodelistPath is null)
        {
            return string.Empty;
        }

        if (!codelistCache.TryGetValue(absoluteCodelistPath, out CodelistRegistry? registry))
        {
            registry = TryLoadCodelist(absoluteCodelistPath);
            codelistCache[absoluteCodelistPath] = registry;
        }

        if (registry is null)
        {
            return string.Empty;
        }

        return registry.TryGetByCode(code, out CodelistEntry? entry) && entry is not null
            ? entry.Name
            : string.Empty;
    }

    private CodelistRegistry? TryLoadCodelist(string absolutePath)
    {
        try
        {
            if (!File.Exists(absolutePath))
            {
                return null;
            }

            IReadOnlyCollection<CodelistEntry> entries = codelistReader.ReadFromFile(absolutePath);
            return new CodelistRegistry(entries);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryResolveCodelistPath(string sourcePath, string codeSpace)
    {
        try
        {
            string? sourceDirectory = Path.GetDirectoryName(sourcePath);
            if (string.IsNullOrEmpty(sourceDirectory))
            {
                return null;
            }

            string combined = Path.IsPathRooted(codeSpace)
                ? codeSpace
                : Path.GetFullPath(Path.Combine(sourceDirectory, codeSpace));
            return combined;
        }
        catch (Exception)
        {
            return null;
        }
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
        PlateauCoordinate3D[] coordinates,
        IReadOnlyCollection<GeometrySurfaceCandidate> candidates,
        int highestLod,
        IReadOnlyCollection<PlateauGeometrySurface> geometrySurfaces)
    {
        PlateauContextFeature feature;
        if (featureType == PlateauFeatureType.Building)
        {
            (double? baseElevationMeters, double? topElevationMeters) = ResolveBuildingElevationRange(candidates);
            feature = new PlateauBuildingFeature
            {
                FeatureType = featureType,
                BaseElevationMeters = baseElevationMeters,
                TopElevationMeters = topElevationMeters
            };
        }
        else
        {
            feature = new PlateauContextFeature { FeatureType = featureType };
        }

        feature.Id = id;
        feature.Name = name;
        feature.SourcePath = sourcePath;
        feature.TileId = tileId;
        feature.HighestLod = highestLod;
        feature.ExteriorRing = coordinates;
        feature.GeometrySurfaces = geometrySurfaces;
        return feature;
    }

    private static (double? BaseElevationMeters, double? TopElevationMeters) ResolveBuildingElevationRange(
        IReadOnlyCollection<GeometrySurfaceCandidate> candidates)
    {
        double? baseElevationMeters = TryGetSemanticElevation(candidates, preferHighest: false, "GroundSurface");
        double? topElevationMeters = TryGetSemanticElevation(candidates, preferHighest: true, "RoofSurface", "OuterCeilingSurface", "ClosureSurface");

        if (!baseElevationMeters.HasValue)
        {
            baseElevationMeters = TryGetCandidateElevation(candidates, preferHighest: false);
        }

        if (!topElevationMeters.HasValue)
        {
            topElevationMeters = TryGetCandidateElevation(candidates, preferHighest: true);
        }

        return (baseElevationMeters, topElevationMeters);
    }

    private static double? TryGetSemanticElevation(
        IReadOnlyCollection<GeometrySurfaceCandidate> candidates,
        bool preferHighest,
        params string[] semanticSurfaceNames)
    {
        double? selectedElevation = null;
        foreach (GeometrySurfaceCandidate candidate in candidates)
        {
            if (!MatchesSemanticSurface(candidate.Surface.SemanticSurfaceType, semanticSurfaceNames))
            {
                continue;
            }

            double candidateElevation = preferHighest ? candidate.MaxZ : candidate.MinZ;
            if (!selectedElevation.HasValue)
            {
                selectedElevation = candidateElevation;
            }
            else if (preferHighest)
            {
                selectedElevation = Math.Max(selectedElevation.Value, candidateElevation);
            }
            else
            {
                selectedElevation = Math.Min(selectedElevation.Value, candidateElevation);
            }
        }

        return selectedElevation;
    }

    private static double? TryGetCandidateElevation(IReadOnlyCollection<GeometrySurfaceCandidate> candidates, bool preferHighest)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        double selectedElevation = preferHighest ? double.NegativeInfinity : double.PositiveInfinity;
        foreach (GeometrySurfaceCandidate candidate in candidates)
        {
            if (preferHighest)
            {
                if (candidate.MaxZ > selectedElevation)
                {
                    selectedElevation = candidate.MaxZ;
                }
            }
            else if (candidate.MinZ < selectedElevation)
            {
                selectedElevation = candidate.MinZ;
            }
        }

        if (double.IsInfinity(selectedElevation))
        {
            return null;
        }

        return selectedElevation;
    }

    private static List<GeometrySurfaceCandidate> GetSurfaceCandidates(XElement featureElement, XNamespace gml)
    {
        List<XElement> polygonElements = new List<XElement>();
        List<XElement> triangleElements = new List<XElement>();
        foreach (XElement element in featureElement.Descendants())
        {
            if (element.Name == gml + "Polygon")
            {
                polygonElements.Add(element);
            }
            else if (element.Name == gml + "Triangle")
            {
                triangleElements.Add(element);
            }
        }

        List<GeometrySurfaceCandidate> candidates = new List<GeometrySurfaceCandidate>(polygonElements.Count + triangleElements.Count);
        int sequence = 0;
        AppendCandidates(candidates, polygonElements, featureElement, gml, ref sequence);
        AppendCandidates(candidates, triangleElements, featureElement, gml, ref sequence);
        return candidates;
    }

    private static GeometrySurfaceCandidate? SelectBestSurface(IReadOnlyCollection<GeometrySurfaceCandidate> candidates)
    {
        GeometrySurfaceCandidate? bestCandidate = null;
        foreach (GeometrySurfaceCandidate candidate in candidates)
        {
            if (bestCandidate is null || CompareCandidate(candidate, bestCandidate) < 0)
            {
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private static GeometrySurfaceCandidate? CreateCandidate(XElement geometryElement, XElement featureElement, XNamespace gml, int sequence)
    {
        XElement? exteriorRingElement = geometryElement.Element(gml + "exterior")?.Element(gml + "LinearRing");
        if (exteriorRingElement is null)
        {
            foreach (XElement ringElement in geometryElement.Descendants(gml + "LinearRing"))
            {
                exteriorRingElement = ringElement;
                break;
            }
        }

        if (exteriorRingElement is null)
        {
            return null;
        }

        PlateauCoordinate3D[] exteriorCoordinates = ParseCoordinates(exteriorRingElement, gml);
        if (exteriorCoordinates.Length < 3)
        {
            return null;
        }

        double minZ = double.PositiveInfinity;
        double maxZ = double.NegativeInfinity;
        AccumulateElevationRange(exteriorCoordinates, ref minZ, ref maxZ);

        List<IReadOnlyCollection<PlateauCoordinate3D>> interiorRings = new List<IReadOnlyCollection<PlateauCoordinate3D>>();
        foreach (XElement interiorElement in geometryElement.Elements(gml + "interior"))
        {
            foreach (XElement ringElement in interiorElement.Elements(gml + "LinearRing"))
            {
                PlateauCoordinate3D[] interiorCoordinates = ParseCoordinates(ringElement, gml);
                if (interiorCoordinates.Length < 3)
                {
                    continue;
                }

                interiorRings.Add(interiorCoordinates);
                AccumulateElevationRange(interiorCoordinates, ref minZ, ref maxZ);
            }
        }

        ResolveGeometryMetadata(geometryElement, featureElement, out int lod, out int priority, out string semanticSurfaceType);
        double planArea = ComputePlanArea(exteriorCoordinates);
        double averageZ = ComputeAverageZ(exteriorCoordinates);

        PlateauGeometrySurface surface = new PlateauGeometrySurface
        {
            SurfaceId = (string?)geometryElement.Attribute(gml + "id")
                ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture),
            Lod = lod,
            SemanticSurfaceType = semanticSurfaceType,
            ExteriorRing = exteriorCoordinates,
            InteriorRings = interiorRings.Count == 0
                ? Array.Empty<IReadOnlyCollection<PlateauCoordinate3D>>()
                : interiorRings.ToArray()
        };

        return new GeometrySurfaceCandidate
        {
            Surface = surface,
            Coordinates = exteriorCoordinates,
            Sequence = sequence,
            Priority = priority,
            PlanArea = planArea,
            HasPlanArea = planArea > 0.000001d,
            AverageZ = averageZ,
            MinZ = double.IsPositiveInfinity(minZ) ? 0d : minZ,
            MaxZ = double.IsNegativeInfinity(maxZ) ? 0d : maxZ
        };
    }

    private static double ComputeAverageZ(IReadOnlyList<PlateauCoordinate3D> coordinates)
    {
        if (coordinates.Count == 0)
        {
            return 0d;
        }

        double sum = 0d;
        for (int index = 0; index < coordinates.Count; index++)
        {
            sum += coordinates[index].Z;
        }

        return sum / coordinates.Count;
    }

    private static void ResolveGeometryMetadata(
        XElement geometryElement,
        XElement featureElement,
        out int lod,
        out int priority,
        out string semanticSurfaceType)
    {
        lod = 0;
        priority = 3;
        semanticSurfaceType = string.Empty;
        for (XElement? ancestor = geometryElement.Parent; ancestor is not null && ancestor != featureElement; ancestor = ancestor.Parent)
        {
            string name = ancestor.Name.LocalName;
            if (string.IsNullOrWhiteSpace(semanticSurfaceType)
                && name.EndsWith("Surface", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, "MultiSurface", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(name, "CompositeSurface", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("lod", StringComparison.OrdinalIgnoreCase))
            {
                semanticSurfaceType = name;
            }

            int ancestorLod = ExtractLod(name);
            if (ancestorLod > lod)
            {
                lod = ancestorLod;
            }

            if (priority > 0 && string.Equals(name, "GroundSurface", StringComparison.OrdinalIgnoreCase))
            {
                priority = 0;
                continue;
            }

            if (priority > 1
                && name.StartsWith("lod", StringComparison.OrdinalIgnoreCase)
                && !name.StartsWith("lod0", StringComparison.OrdinalIgnoreCase))
            {
                priority = 1;
                continue;
            }

            if (priority > 2 && string.Equals(name, "lod0FootPrint", StringComparison.OrdinalIgnoreCase))
            {
                priority = 2;
            }
        }
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

        List<XElement> posElements = new List<XElement>();
        foreach (XElement posElement in ringElement.Elements(gml + "pos"))
        {
            posElements.Add(posElement);
        }

        if (posElements.Count == 0)
        {
            return Array.Empty<PlateauCoordinate3D>();
        }

        List<PlateauCoordinate3D> coordinates = new List<PlateauCoordinate3D>(posElements.Count);
        for (int index = 0; index < posElements.Count; index++)
        {
            List<double> rawValues = ParseDoubleValues(posElements[index].Value ?? string.Empty);
            if (rawValues.Count < 2)
            {
                continue;
            }

            double x = rawValues[0];
            double y = rawValues[1];
            double z = rawValues.Count >= 3
                ? rawValues[2]
                : 0d;
            coordinates.Add(new PlateauCoordinate3D(x, y, z));
        }

        return coordinates.ToArray();
    }

    private static PlateauCoordinate3D[] ParsePosListCoordinates(XElement posListElement)
    {
        List<double> rawValues = ParseDoubleValues(posListElement.Value ?? string.Empty);
        if (rawValues.Count < 6)
        {
            return Array.Empty<PlateauCoordinate3D>();
        }

        int dimension;
        if (int.TryParse((string?)posListElement.Attribute("srsDimension"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedDimension)
            && parsedDimension >= 2)
        {
            dimension = parsedDimension;
        }
        else if (rawValues.Count % 3 == 0)
        {
            dimension = 3;
        }
        else
        {
            dimension = 2;
        }

        List<PlateauCoordinate3D> coordinates = new List<PlateauCoordinate3D>(rawValues.Count / dimension);
        for (int index = 0; index <= rawValues.Count - dimension; index += dimension)
        {
            double x = rawValues[index];
            double y = rawValues[index + 1];
            double z = dimension >= 3
                ? rawValues[index + 2]
                : 0d;
            coordinates.Add(new PlateauCoordinate3D(x, y, z));
        }

        return coordinates.ToArray();
    }

    private static List<double> ParseDoubleValues(string rawText)
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
            throw new FormatException("Encountered an invalid coordinate token while parsing PLATEAU geometry.");
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
                throw new FormatException("Encountered an invalid exponent while parsing PLATEAU geometry.");
            }
        }

        if (index < length && !char.IsWhiteSpace(rawText[index]))
        {
            throw new FormatException("Encountered an invalid character while parsing PLATEAU geometry.");
        }

        double parsed = integerPart + (fractionalPart / divisor);
        if (exponent != 0)
        {
            parsed *= Math.Pow(10d, exponentNegative ? -exponent : exponent);
        }

        value = isNegative ? -parsed : parsed;
        return true;
    }

    private static void AppendCandidates(
        ICollection<GeometrySurfaceCandidate> candidates,
        IReadOnlyCollection<XElement> geometryElements,
        XElement featureElement,
        XNamespace gml,
        ref int sequence)
    {
        foreach (XElement geometryElement in geometryElements)
        {
            GeometrySurfaceCandidate? candidate = CreateCandidate(geometryElement, featureElement, gml, sequence);
            sequence++;
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }
        }
    }

    private static void AppendFeatures(ICollection<PlateauContextFeature> features, IReadOnlyCollection<PlateauContextFeature> parsedFeatures)
    {
        foreach (PlateauContextFeature feature in parsedFeatures)
        {
            features.Add(feature);
        }
    }

    private static Dictionary<SupportedFeatureDescriptor, List<XElement>> CreateMatchedElementBuckets()
    {
        Dictionary<SupportedFeatureDescriptor, List<XElement>> buckets = new Dictionary<SupportedFeatureDescriptor, List<XElement>>(SupportedFeatures.Length);
        for (int index = 0; index < SupportedFeatures.Length; index++)
        {
            buckets[SupportedFeatures[index]] = new List<XElement>();
        }

        return buckets;
    }

    private static Dictionary<XName, SupportedFeatureDescriptor> BuildSupportedFeatureLookup()
    {
        Dictionary<XName, SupportedFeatureDescriptor> lookup = new Dictionary<XName, SupportedFeatureDescriptor>();
        for (int descriptorIndex = 0; descriptorIndex < SupportedFeatures.Length; descriptorIndex++)
        {
            SupportedFeatureDescriptor descriptor = SupportedFeatures[descriptorIndex];
            foreach (string localName in descriptor.LocalNames)
            {
                lookup[descriptor.Namespace + localName] = descriptor;
            }
        }

        return lookup;
    }

    private static RoadChildSummary GetRoadChildSummary(
        XElement roadElement,
        XNamespace featureNamespace,
        IDictionary<XElement, RoadChildSummary> roadChildSummaries)
    {
        if (roadChildSummaries.TryGetValue(roadElement, out RoadChildSummary? cachedSummary))
        {
            return cachedSummary;
        }

        bool hasChildTrafficAreas = false;
        int maxChildLod = 0;
        foreach (XElement childContainer in roadElement.Elements())
        {
            if (childContainer.Name != featureNamespace + "trafficArea"
                && childContainer.Name != featureNamespace + "auxiliaryTrafficArea")
            {
                continue;
            }

            foreach (XElement childFeature in childContainer.Elements())
            {
                hasChildTrafficAreas = true;
                int childLod = DetermineElementMaxLod(childFeature);
                if (childLod > maxChildLod)
                {
                    maxChildLod = childLod;
                }
            }
        }

        RoadChildSummary summary = new RoadChildSummary(hasChildTrafficAreas, maxChildLod);
        roadChildSummaries[roadElement] = summary;
        return summary;
    }

    private static bool MatchesSemanticSurface(string semanticSurfaceType, IReadOnlyCollection<string> semanticSurfaceNames)
    {
        if (string.IsNullOrWhiteSpace(semanticSurfaceType))
        {
            return false;
        }

        foreach (string surfaceName in semanticSurfaceNames)
        {
            if (string.Equals(semanticSurfaceType, surfaceName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyCollection<PlateauGeometrySurface> BuildGeometrySurfaces(IReadOnlyCollection<GeometrySurfaceCandidate> candidates)
    {
        PlateauGeometrySurface[] surfaces = new PlateauGeometrySurface[candidates.Count];
        int index = 0;
        foreach (GeometrySurfaceCandidate candidate in candidates)
        {
            surfaces[index++] = candidate.Surface;
        }

        return surfaces;
    }

    private static int CompareCandidate(GeometrySurfaceCandidate left, GeometrySurfaceCandidate right)
    {
        int comparison = left.Priority.CompareTo(right.Priority);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = (left.HasPlanArea ? 0 : 1).CompareTo(right.HasPlanArea ? 0 : 1);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = left.AverageZ.CompareTo(right.AverageZ);
        if (comparison != 0)
        {
            return comparison;
        }

        comparison = right.PlanArea.CompareTo(left.PlanArea);
        if (comparison != 0)
        {
            return comparison;
        }

        return left.Sequence.CompareTo(right.Sequence);
    }

    private static int ExtractLod(string localName)
    {
        if (!localName.StartsWith("lod", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        int index = 3;
        int lod = 0;
        bool hasDigits = false;
        while (index < localName.Length && char.IsDigit(localName[index]))
        {
            hasDigits = true;
            lod = (lod * 10) + (localName[index] - '0');
            index++;
        }

        return hasDigits ? lod : 0;
    }

    private static void AccumulateElevationRange(
        IReadOnlyCollection<PlateauCoordinate3D> coordinates,
        ref double minZ,
        ref double maxZ)
    {
        foreach (PlateauCoordinate3D point in coordinates)
        {
            if (point.Z < minZ)
            {
                minZ = point.Z;
            }

            if (point.Z > maxZ)
            {
                maxZ = point.Z;
            }
        }
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

    private sealed class GeometrySurfaceCandidate
    {
        public PlateauGeometrySurface Surface { get; set; } = new PlateauGeometrySurface();

        public PlateauCoordinate3D[] Coordinates { get; set; } = Array.Empty<PlateauCoordinate3D>();

        public int Sequence { get; set; }

        public int Priority { get; set; }

        public bool HasPlanArea { get; set; }

        public double PlanArea { get; set; }

        public double AverageZ { get; set; }

        public double MinZ { get; set; }

        public double MaxZ { get; set; }
    }

    private sealed class RoadChildSummary
    {
        public RoadChildSummary(bool hasChildTrafficAreas, int maxChildLod)
        {
            HasChildTrafficAreas = hasChildTrafficAreas;
            MaxChildLod = maxChildLod;
        }

        public bool HasChildTrafficAreas { get; }

        public int MaxChildLod { get; }
    }
}
