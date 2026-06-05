using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml.Linq;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
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
        HashSet<XElement> consumedSidewalkAreas = new HashSet<XElement>();
        List<PlateauContextFeature> features = new List<PlateauContextFeature>();
        for (int descriptorIndex = 0; descriptorIndex < SupportedFeatures.Length; descriptorIndex++)
        {
            SupportedFeatureDescriptor descriptor = SupportedFeatures[descriptorIndex];
            List<XElement> elements = matchedElements[descriptor];

            if (descriptor.FeatureType == PlateauFeatureType.Road)
            {
                for (int elementIndex = 0; elementIndex < elements.Count; elementIndex++)
                {
                    XElement element = elements[elementIndex];
                    if (!string.Equals(element.Name.LocalName, "Road", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!GetRoadChildSummary(element, descriptor.Namespace, roadChildSummaries).HasChildTrafficAreas)
                    {
                        continue;
                    }

                    AppendFeatures(features, ParseRoadSidewalkGroup(element, descriptor.Namespace, gml, filePath, tileId, epsgCode, consumedSidewalkAreas));
                }
            }

            for (int elementIndex = 0; elementIndex < elements.Count; elementIndex++)
            {
                XElement element = elements[elementIndex];
                if (consumedSidewalkAreas.Contains(element))
                {
                    continue;
                }

                if (ShouldSkipFeatureElement(descriptor.FeatureType, descriptor.Namespace, element.Name.LocalName, element, roadChildSummaries))
                {
                    continue;
                }

                AppendFeatures(features, ParseFeatures(element, descriptor.FeatureType, gml, filePath, tileId, epsgCode));
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
            return HasNestedBridgePartGeometry(featureElement);
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

    private static bool HasNestedBridgePartGeometry(XElement featureElement)
    {
        foreach (XElement descendant in featureElement.Descendants())
        {
            if (descendant.Name.Namespace != PlateauConstants.BridgeNamespace)
            {
                continue;
            }

            if (string.Equals(descendant.Name.LocalName, "BridgePart", StringComparison.OrdinalIgnoreCase)
                && ContainsSupportedSurfaceGeometry(descendant))
            {
                return true;
            }
        }

        return false;
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
        string tileId,
        int? epsgCode)
    {
        List<GeometrySurfaceCandidate> candidates = GetSurfaceCandidates(featureElement, gml, epsgCode);
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
            GeometrySurfaceCandidate selectedCandidate = SelectBestFootprintSurface(candidates, featureType) ?? SelectBestSurface(candidates)!;
            IReadOnlyList<GeometrySurfaceCandidate> footprintCandidates = SelectFootprintUnionCandidates(candidates, selectedCandidate, featureType);

            IReadOnlyList<PlateauCoordinate3D[]> mergedRings = footprintCandidates.Count >= 2
                ? TryUnionCandidateRings(footprintCandidates, selectedCandidate)
                : Array.Empty<PlateauCoordinate3D[]>();

            if (mergedRings.Count == 0)
            {
                mergedRings = new[] { selectedCandidate.Coordinates };
            }

            List<PlateauContextFeature> singleBucketFeatures = new List<PlateauContextFeature>(mergedRings.Count);
            bool ringsNeedSuffix = mergedRings.Count > 1;
            bool keepBaseIdForFirstRing = ShouldKeepBaseIdForFirstMultipartFeature(featureType, featureElement);
            for (int ringIndex = 0; ringIndex < mergedRings.Count; ringIndex++)
            {
                bool needsRingSuffix = ringsNeedSuffix && (!keepBaseIdForFirstRing || ringIndex > 0);
                string featureId = needsRingSuffix
                    ? string.Format(CultureInfo.InvariantCulture, "{0}::{1}", baseId, ringIndex + 1)
                    : baseId;
                string featureName = needsRingSuffix
                    ? string.Format(CultureInfo.InvariantCulture, "{0} [{1}]", baseName, ringIndex + 1)
                    : baseName;

                PlateauContextFeature feature = CreateFeature(
                    featureType,
                    featureId,
                    featureName,
                    sourcePath,
                    tileId,
                    mergedRings[ringIndex],
                    candidates,
                    highestLod,
                    BuildGeometrySurfaces(highestLodCandidates));
                feature.ClassCode = classification.classCode;
                feature.ClassName = classification.className;
                singleBucketFeatures.Add(feature);
            }

            return singleBucketFeatures;
        }

        List<GeometrySurfaceCandidate> filteredHighestLod = new List<GeometrySurfaceCandidate>(highestLodCandidates.Count);
        foreach (GeometrySurfaceCandidate candidate in highestLodCandidates)
        {
            if (featureType == PlateauFeatureType.Road
                && ComputeVerticalCosine(candidate.Coordinates, epsgCode) < MinVerticalCosineForRoadSurface)
            {
                // Skip near-vertical curb / road-wall faces; their XY projection becomes
                // a thin sliver polygon that shows up as a "spike" artifact in the shapefile.
                continue;
            }

            filteredHighestLod.Add(candidate);
        }

        List<PlateauContextFeature> features = new List<PlateauContextFeature>(filteredHighestLod.Count);
        bool needsSuffix = filteredHighestLod.Count > 1;
        for (int index = 0; index < filteredHighestLod.Count; index++)
        {
            GeometrySurfaceCandidate candidate = filteredHighestLod[index];
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

    private static bool ShouldKeepBaseIdForFirstMultipartFeature(PlateauFeatureType featureType, XElement featureElement)
    {
        return featureType == PlateauFeatureType.Bridge
            && string.Equals(featureElement.Name.LocalName, "Bridge", StringComparison.OrdinalIgnoreCase);
    }

    // cos(70°) ≈ 0.342. Polygons with a vertical-cosine below this are treated as
    // near-vertical and skipped from the Road shapefile output.
    private const double MinVerticalCosineForRoadSurface = 0.34d;
    private const double MinimumPlanAreaSquareMetres = 0.01d;
    private const double FootprintElevationToleranceMetres = 0.5d;

    private IReadOnlyCollection<PlateauContextFeature> ParseRoadSidewalkGroup(
        XElement roadElement,
        XNamespace featureNamespace,
        XNamespace gml,
        string sourcePath,
        string tileId,
        int? epsgCode,
        ICollection<XElement> consumedSidewalkAreas)
    {
        List<XElement> sidewalkAreaElements = new List<XElement>();
        (string Code, string Name) classification = (string.Empty, string.Empty);
        foreach (XElement childContainer in roadElement.Elements())
        {
            if (childContainer.Name != featureNamespace + "trafficArea")
            {
                continue;
            }

            foreach (XElement childFeature in childContainer.Elements())
            {
                if (childFeature.Name != featureNamespace + "TrafficArea")
                {
                    continue;
                }

                (string Code, string Name) function = ResolveTrafficAreaFunction(childFeature, sourcePath);
                if (!IsSidewalkFunction(function.Code, function.Name))
                {
                    continue;
                }

                sidewalkAreaElements.Add(childFeature);
                if (string.IsNullOrEmpty(classification.Code))
                {
                    classification = function;
                }
            }
        }

        if (sidewalkAreaElements.Count == 0)
        {
            return Array.Empty<PlateauContextFeature>();
        }

        List<GeometrySurfaceCandidate> aggregatedCandidates = new List<GeometrySurfaceCandidate>();
        foreach (XElement sidewalkArea in sidewalkAreaElements)
        {
            aggregatedCandidates.AddRange(GetSurfaceCandidates(sidewalkArea, gml, epsgCode));
        }

        if (aggregatedCandidates.Count == 0)
        {
            foreach (XElement sidewalkArea in sidewalkAreaElements)
            {
                consumedSidewalkAreas.Add(sidewalkArea);
            }
            return Array.Empty<PlateauContextFeature>();
        }

        GeometrySurfaceCandidate selectedCandidate = SelectBestSurface(aggregatedCandidates)!;
        List<GeometrySurfaceCandidate> planAreaCandidates = new List<GeometrySurfaceCandidate>();
        foreach (GeometrySurfaceCandidate candidate in aggregatedCandidates)
        {
            if (candidate.HasPlanArea)
            {
                planAreaCandidates.Add(candidate);
            }
        }

        IReadOnlyList<PlateauCoordinate3D[]> mergedRings = planAreaCandidates.Count >= 2
            ? TryUnionCandidateRings(planAreaCandidates, selectedCandidate)
            : Array.Empty<PlateauCoordinate3D[]>();

        if (mergedRings.Count == 0)
        {
            mergedRings = new[] { selectedCandidate.Coordinates };
        }

        int highestLod = 0;
        for (int index = 0; index < aggregatedCandidates.Count; index++)
        {
            int candidateLod = aggregatedCandidates[index].Surface.Lod;
            if (candidateLod > highestLod)
            {
                highestLod = candidateLod;
            }
        }

        List<GeometrySurfaceCandidate> highestLodCandidates = new List<GeometrySurfaceCandidate>();
        for (int index = 0; index < aggregatedCandidates.Count; index++)
        {
            GeometrySurfaceCandidate candidate = aggregatedCandidates[index];
            if (candidate.Surface.Lod == highestLod)
            {
                highestLodCandidates.Add(candidate);
            }
        }

        string baseId = (string?)roadElement.Attribute(gml + "id") ?? Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
        string? baseName = null;
        foreach (XElement nameElement in roadElement.Elements(gml + "name"))
        {
            baseName = nameElement.Value;
            break;
        }
        baseName ??= roadElement.Name.LocalName;
        baseId = string.Concat(baseId, ":sidewalk");
        baseName = string.Concat(baseName, " (歩道)");

        List<PlateauContextFeature> emitted = new List<PlateauContextFeature>(mergedRings.Count);
        bool needsSuffix = mergedRings.Count > 1;
        for (int ringIndex = 0; ringIndex < mergedRings.Count; ringIndex++)
        {
            string featureId = needsSuffix
                ? string.Format(CultureInfo.InvariantCulture, "{0}::{1}", baseId, ringIndex + 1)
                : baseId;
            string featureName = needsSuffix
                ? string.Format(CultureInfo.InvariantCulture, "{0} [{1}]", baseName, ringIndex + 1)
                : baseName;

            PlateauContextFeature feature = CreateFeature(
                PlateauFeatureType.Sidewalk,
                featureId,
                featureName,
                sourcePath,
                tileId,
                mergedRings[ringIndex],
                aggregatedCandidates,
                highestLod,
                BuildGeometrySurfaces(highestLodCandidates));
            feature.ClassCode = classification.Code;
            feature.ClassName = classification.Name;
            emitted.Add(feature);
        }

        foreach (XElement sidewalkArea in sidewalkAreaElements)
        {
            consumedSidewalkAreas.Add(sidewalkArea);
        }

        return emitted;
    }

    private (string Code, string Name) ResolveTrafficAreaFunction(XElement trafficAreaElement, string sourcePath)
    {
        XElement? functionElement = trafficAreaElement.Element(PlateauConstants.TransportationNamespace + "function");
        if (functionElement is null)
        {
            return (string.Empty, string.Empty);
        }

        string code = (functionElement.Value ?? string.Empty).Trim();
        if (string.IsNullOrEmpty(code))
        {
            return (string.Empty, string.Empty);
        }

        string codeSpace = (string?)functionElement.Attribute("codeSpace") ?? string.Empty;
        string name = ResolveCodelistName(sourcePath, codeSpace, code);
        return (code, name);
    }

    private static bool IsSidewalkFunction(string code, string name)
    {
        if (string.Equals(code, "2000", StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrEmpty(name) && name.IndexOf("歩道", StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        return false;
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

    private static List<GeometrySurfaceCandidate> GetSurfaceCandidates(XElement featureElement, XNamespace gml, int? epsgCode)
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
        AppendCandidates(candidates, polygonElements, featureElement, gml, epsgCode, ref sequence);
        AppendCandidates(candidates, triangleElements, featureElement, gml, epsgCode, ref sequence);
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

    private static GeometrySurfaceCandidate? SelectBestFootprintSurface(
        IReadOnlyCollection<GeometrySurfaceCandidate> candidates,
        PlateauFeatureType featureType)
    {
        if (featureType != PlateauFeatureType.Building && featureType != PlateauFeatureType.Bridge)
        {
            return SelectBestSurface(candidates);
        }

        GeometrySurfaceCandidate? bestCandidate = null;
        foreach (GeometrySurfaceCandidate candidate in candidates)
        {
            if (ShouldExcludeNestedComponentFromFootprint(candidate, featureType))
            {
                continue;
            }

            if (bestCandidate is null || CompareCandidate(candidate, bestCandidate) < 0)
            {
                bestCandidate = candidate;
            }
        }

        return bestCandidate;
    }

    private static IReadOnlyList<GeometrySurfaceCandidate> SelectFootprintUnionCandidates(
        IReadOnlyCollection<GeometrySurfaceCandidate> candidates,
        GeometrySurfaceCandidate selectedCandidate,
        PlateauFeatureType featureType)
    {
        List<GeometrySurfaceCandidate> eligibleCandidates = new List<GeometrySurfaceCandidate>();
        foreach (GeometrySurfaceCandidate candidate in candidates)
        {
            if (!candidate.HasPlanArea)
            {
                continue;
            }

            if (ShouldExcludeNestedComponentFromFootprint(candidate, featureType))
            {
                continue;
            }

            eligibleCandidates.Add(candidate);
        }

        if (eligibleCandidates.Count == 0)
        {
            return Array.Empty<GeometrySurfaceCandidate>();
        }

        if (featureType == PlateauFeatureType.Building || featureType == PlateauFeatureType.Bridge)
        {
            List<GeometrySurfaceCandidate> groundSurfaceCandidates = new List<GeometrySurfaceCandidate>();
            foreach (GeometrySurfaceCandidate candidate in eligibleCandidates)
            {
                if (string.Equals(candidate.Surface.SemanticSurfaceType, "GroundSurface", StringComparison.OrdinalIgnoreCase))
                {
                    groundSurfaceCandidates.Add(candidate);
                }
            }

            if (groundSurfaceCandidates.Count > 1)
            {
                return groundSurfaceCandidates;
            }

            List<GeometrySurfaceCandidate> sameElevationCandidates = new List<GeometrySurfaceCandidate>();
            foreach (GeometrySurfaceCandidate candidate in eligibleCandidates)
            {
                if (IsSameFootprintElevation(candidate, selectedCandidate))
                {
                    sameElevationCandidates.Add(candidate);
                }
            }

            if (sameElevationCandidates.Count > 1)
            {
                return sameElevationCandidates;
            }

            if (groundSurfaceCandidates.Count == 1)
            {
                return groundSurfaceCandidates;
            }
        }

        List<GeometrySurfaceCandidate> selectedBucketCandidates = new List<GeometrySurfaceCandidate>();
        foreach (GeometrySurfaceCandidate candidate in eligibleCandidates)
        {
            if (candidate.Priority == selectedCandidate.Priority
                && IsSameFootprintElevation(candidate, selectedCandidate))
            {
                selectedBucketCandidates.Add(candidate);
            }
        }

        return selectedBucketCandidates;
    }

    private static bool ShouldExcludeNestedComponentFromFootprint(GeometrySurfaceCandidate candidate, PlateauFeatureType featureType)
    {
        return (featureType == PlateauFeatureType.Building && candidate.IsNestedBuildingInstallation)
            || (featureType == PlateauFeatureType.Bridge && candidate.IsNestedBridgeConstructionElement);
    }

    private static bool IsSameFootprintElevation(GeometrySurfaceCandidate candidate, GeometrySurfaceCandidate selectedCandidate)
    {
        return Math.Abs(candidate.AverageZ - selectedCandidate.AverageZ) <= FootprintElevationToleranceMetres;
    }

    private static GeometrySurfaceCandidate? CreateCandidate(XElement geometryElement, XElement featureElement, XNamespace gml, int? epsgCode, int sequence)
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
        double planArea = ComputePlanAreaSquareMetres(exteriorCoordinates, epsgCode);
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
            HasPlanArea = planArea > MinimumPlanAreaSquareMetres,
            AverageZ = averageZ,
            MinZ = double.IsPositiveInfinity(minZ) ? 0d : minZ,
            MaxZ = double.IsNegativeInfinity(maxZ) ? 0d : maxZ,
            IsNestedBuildingInstallation = IsNestedBuildingInstallation(geometryElement, featureElement),
            IsNestedBridgeConstructionElement = IsNestedBridgeConstructionElement(geometryElement, featureElement)
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

    private static bool IsNestedBuildingInstallation(XElement geometryElement, XElement featureElement)
    {
        for (XElement? ancestor = geometryElement.Parent; ancestor is not null && ancestor != featureElement; ancestor = ancestor.Parent)
        {
            if (ancestor.Name.Namespace != PlateauConstants.BuildingNamespace)
            {
                continue;
            }

            string localName = ancestor.Name.LocalName;
            if (string.Equals(localName, "BuildingInstallation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(localName, "IntBuildingInstallation", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNestedBridgeConstructionElement(XElement geometryElement, XElement featureElement)
    {
        for (XElement? ancestor = geometryElement.Parent; ancestor is not null && ancestor != featureElement; ancestor = ancestor.Parent)
        {
            if (ancestor.Name.Namespace != PlateauConstants.BridgeNamespace)
            {
                continue;
            }

            if (string.Equals(ancestor.Name.LocalName, "BridgeConstructionElement", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
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

    private static double ComputePlanAreaSquareMetres(IReadOnlyList<PlateauCoordinate3D> coordinates, int? epsgCode)
    {
        if (coordinates.Count < 3)
        {
            return 0d;
        }

        if (!IsGeographicJgd2011(epsgCode))
        {
            return ComputePlanArea(coordinates);
        }

        PlateauCoordinate3D origin = coordinates[0];
        double latitudeRadians = origin.X * (Math.PI / 180d);
        double metresPerDegreeLatitude = 111_320d;
        double metresPerDegreeLongitude = Math.Max(1d, metresPerDegreeLatitude * Math.Cos(latitudeRadians));

        double areaTwice = 0d;
        for (int index = 0; index < coordinates.Count; index++)
        {
            PlateauCoordinate3D current = coordinates[index];
            PlateauCoordinate3D next = coordinates[(index + 1) % coordinates.Count];

            double currentX = (current.Y - origin.Y) * metresPerDegreeLongitude;
            double currentY = (current.X - origin.X) * metresPerDegreeLatitude;
            double nextX = (next.Y - origin.Y) * metresPerDegreeLongitude;
            double nextY = (next.X - origin.X) * metresPerDegreeLatitude;
            areaTwice += (currentX * nextY) - (nextX * currentY);
        }

        return Math.Abs(areaTwice) * 0.5d;
    }

    // Returns |n.Z| / |n| for the 3D normal of the polygon's plane, computed from
    // the first non-collinear triangle. 1.0 == perfectly horizontal, 0.0 == perfectly
    // vertical. For geographic PLATEAU CRSs, horizontal deltas are approximated as
    // local metres before comparing to metre Z. Degenerate input is treated as
    // horizontal so it is not filtered.
    private static double ComputeVerticalCosine(IReadOnlyList<PlateauCoordinate3D> coordinates, int? epsgCode)
    {
        if (coordinates.Count < 3)
        {
            return 1d;
        }

        PlateauCoordinate3D a = coordinates[0];
        bool useGeographicScale = IsGeographicJgd2011(epsgCode);
        double metresPerDegreeLatitude = 1d;
        double metresPerDegreeLongitude = 1d;
        if (useGeographicScale)
        {
            double latitudeRadians = a.X * (Math.PI / 180d);
            metresPerDegreeLatitude = 111_320d;
            metresPerDegreeLongitude = Math.Max(1d, metresPerDegreeLatitude * Math.Cos(latitudeRadians));
        }

        for (int index = 1; index < coordinates.Count - 1; index++)
        {
            PlateauCoordinate3D b = coordinates[index];
            for (int next = index + 1; next < coordinates.Count; next++)
            {
                PlateauCoordinate3D c = coordinates[next];
                double v1X = b.X - a.X;
                double v1Y = b.Y - a.Y;
                double v1Z = b.Z - a.Z;
                double v2X = c.X - a.X;
                double v2Y = c.Y - a.Y;
                double v2Z = c.Z - a.Z;
                if (useGeographicScale)
                {
                    // Parser geographic coordinates follow ContextGeometryBuilder's convention:
                    // X is latitude and Y is longitude.
                    v1X = (b.Y - a.Y) * metresPerDegreeLongitude;
                    v1Y = (b.X - a.X) * metresPerDegreeLatitude;
                    v2X = (c.Y - a.Y) * metresPerDegreeLongitude;
                    v2Y = (c.X - a.X) * metresPerDegreeLatitude;
                }

                double nX = (v1Y * v2Z) - (v1Z * v2Y);
                double nY = (v1Z * v2X) - (v1X * v2Z);
                double nZ = (v1X * v2Y) - (v1Y * v2X);

                double magnitude = Math.Sqrt((nX * nX) + (nY * nY) + (nZ * nZ));
                if (magnitude <= 0d)
                {
                    continue;
                }

                return Math.Abs(nZ) / magnitude;
            }
        }

        return 1d;
    }

    private static bool IsGeographicJgd2011(int? epsgCode)
    {
        return epsgCode == 6668 || epsgCode == 6697;
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
        int? epsgCode,
        ref int sequence)
    {
        foreach (XElement geometryElement in geometryElements)
        {
            GeometrySurfaceCandidate? candidate = CreateCandidate(geometryElement, featureElement, gml, epsgCode, sequence);
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

    private static IReadOnlyList<PlateauCoordinate3D[]> TryUnionCandidateRings(
        IReadOnlyList<GeometrySurfaceCandidate> candidates,
        GeometrySurfaceCandidate elevationSource)
    {
        GeometryFactory geometryFactory = new GeometryFactory();
        List<Polygon> polygons = new List<Polygon>(candidates.Count);

        for (int index = 0; index < candidates.Count; index++)
        {
            GeometrySurfaceCandidate candidate = candidates[index];
            (double X, double Y)[] xy = new (double X, double Y)[candidate.Coordinates.Length];
            for (int i = 0; i < candidate.Coordinates.Length; i++)
            {
                xy[i] = (candidate.Coordinates[i].X, candidate.Coordinates[i].Y);
            }

            LinearRing? ring = PlateauPolygonHelpers.CreateLinearRing(geometryFactory, xy);
            if (ring is null)
            {
                continue;
            }

            Polygon polygon;
            try
            {
                polygon = geometryFactory.CreatePolygon(ring);
            }
            catch (Exception)
            {
                continue;
            }

            if (!polygon.IsValid)
            {
                Geometry repaired = polygon.Buffer(0d);
                if (repaired is Polygon repairedPolygon && !repairedPolygon.IsEmpty)
                {
                    polygon = repairedPolygon;
                }
                else if (repaired is MultiPolygon repairedMulti && !repairedMulti.IsEmpty)
                {
                    for (int partIndex = 0; partIndex < repairedMulti.NumGeometries; partIndex++)
                    {
                        if (repairedMulti.GetGeometryN(partIndex) is Polygon multiPart && !multiPart.IsEmpty)
                        {
                            polygons.Add(multiPart);
                        }
                    }
                    continue;
                }
                else
                {
                    continue;
                }
            }

            if (polygon.IsEmpty)
            {
                continue;
            }

            polygons.Add(polygon);
        }

        if (polygons.Count == 0)
        {
            return Array.Empty<PlateauCoordinate3D[]>();
        }

        double elevationZ = elevationSource.MinZ;

        Geometry? unioned = TryRunUnion(polygons);
        if (unioned is null || unioned.IsEmpty)
        {
            return Array.Empty<PlateauCoordinate3D[]>();
        }

        List<PlateauCoordinate3D[]> rings = new List<PlateauCoordinate3D[]>();
        AppendUnionPolygons(unioned, elevationZ, rings);
        return rings;
    }

    private static Geometry? TryRunUnion(IReadOnlyList<Polygon> polygons)
    {
        List<Geometry> unionInputs = new List<Geometry>(polygons.Count);
        foreach (Polygon polygon in polygons)
        {
            unionInputs.Add(polygon);
        }

        try
        {
            return CascadedPolygonUnion.Union(unionInputs);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine("CityGmlParser: CascadedPolygonUnion failed, retrying with UnaryUnionOp. " + ex.Message);
        }

        try
        {
            return UnaryUnionOp.Union(unionInputs);
        }
        catch (Exception ex) when (ex is TopologyException || ex is ArgumentException || ex is InvalidOperationException)
        {
            System.Diagnostics.Debug.WriteLine("CityGmlParser: UnaryUnionOp also failed, falling back to first valid input. " + ex.Message);
        }

        return polygons.Count > 0 ? polygons[0] : null;
    }

    private static void AppendUnionPolygons(Geometry geometry, double z, List<PlateauCoordinate3D[]> rings)
    {
        if (geometry is Polygon polygon)
        {
            PlateauCoordinate3D[]? ring = BuildRingFromShell(polygon, z);
            if (ring is not null)
            {
                rings.Add(ring);
            }
            return;
        }

        if (geometry is GeometryCollection collection)
        {
            for (int index = 0; index < collection.NumGeometries; index++)
            {
                AppendUnionPolygons(collection.GetGeometryN(index), z, rings);
            }
        }
    }

    private static PlateauCoordinate3D[]? BuildRingFromShell(Polygon polygon, double z)
    {
        Coordinate[] shellCoordinates = polygon.ExteriorRing.Coordinates;
        if (shellCoordinates.Length < 4)
        {
            return null;
        }

        // NTS LinearRing repeats the first coordinate at the end; drop the closing duplicate.
        int pointCount = shellCoordinates.Length - 1;
        PlateauCoordinate3D[] ring = new PlateauCoordinate3D[pointCount];
        for (int index = 0; index < pointCount; index++)
        {
            Coordinate coordinate = shellCoordinates[index];
            ring[index] = new PlateauCoordinate3D(coordinate.X, coordinate.Y, z);
        }

        return ring;
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

        public bool IsNestedBuildingInstallation { get; set; }

        public bool IsNestedBridgeConstructionElement { get; set; }
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
