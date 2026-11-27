using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Autodesk.Revit.DB;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.Core.Storage;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Federation;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.Storage;

namespace RevitGeoSuite.FloorPlanExport.Federation;

/// <summary>
/// Read-only Revit API snapshot collector for T10 (<see cref="FederationChecker"/>).
/// Reads the active host document, all loaded/unloaded <see cref="RevitLinkInstance"/>s,
/// 3-noncollinear-control-point shared-coordinate samples, <see cref="Level.ProjectElevation"/>
/// (never <c>Level.Elevation</c>), 64-bit <see cref="ElementId.Value"/> (never <c>IntegerValue</c>),
/// and optional shared station-folder manifests.
/// </summary>
public static class RevitFederationSnapshotCollector
{
    public static FederationCheckSnapshot Collect(
        Document hostDocument,
        string? stationFolderOverride = null,
        bool includeStationFolder = true,
        FederationCheckOptions? options = null)
    {
        if (hostDocument is null)
        {
            throw new ArgumentNullException(nameof(hostDocument));
        }

        FederationCheckOptions effectiveOptions = options ?? FederationCheckOptions.Default;
        GeoProjectInfo? hostGeo = TryLoadGeoProjectInfo(hostDocument);
        (int? hostEpsg, string hostCrsName) = ResolveCrs(hostGeo);
        TilesHeightMetadata? hostTilesMeta = TryLoadTilesHeightMetadata(hostDocument);

        string stationFolder = !string.IsNullOrWhiteSpace(stationFolderOverride)
            ? stationFolderOverride!.Trim()
            : (hostGeo?.GisStationFolder?.Trim() ?? string.Empty);

        GisModelFile? hostGisModel = TryLoadGisModelForDocument(stationFolder, hostGeo?.ModelId);
        (KanizuFederationMode? hostMode, string? hostHubId, string? hostRawMode) = ParseFederation(hostGisModel?.Federation);

        HashSet<string>? recordedExportLinkIds = hostGisModel?.Links != null
            ? new HashSet<string>(hostGisModel.Links.Where(l => !string.IsNullOrWhiteSpace(l.UniqueId)).Select(l => l.UniqueId.Trim()), StringComparer.OrdinalIgnoreCase)
            : null;

        ProjectLocation? hostLocation = hostDocument.ActiveProjectLocation;
        List<FederationLevelSnapshot> hostLevels = CollectDocumentLevels(
            hostDocument,
            hostLocation,
            linkToHostTransform: null);

        List<FederationLinkSnapshot> links = CollectLinkedModels(
            hostDocument,
            hostLocation,
            stationFolder,
            recordedExportLinkIds,
            effectiveOptions);

        StationFolderSnapshot? stationSnapshot = null;
        if (includeStationFolder && stationFolder.Length > 0 && Directory.Exists(stationFolder))
        {
            stationSnapshot = StationFolderFederationCheck.LoadSnapshot(stationFolder);
        }

        return new FederationCheckSnapshot
        {
            HostDocumentTitle = hostDocument.Title ?? string.Empty,
            HostDocumentPath = hostDocument.PathName ?? string.Empty,
            HostDocumentKey = DocumentProjectKeyBuilder.Create(hostDocument),
            HostModelId = hostGeo?.ModelId?.Trim() ?? string.Empty,
            HostModelRole = GisStandardStateReader.ReadModelRoleText(hostDocument),
            HostFederationMode = hostMode,
            HostHubModelId = hostHubId,
            HostRawFederationMode = hostRawMode,
            HostCrsEpsg = hostEpsg,
            HostCrsName = hostCrsName,
            IsHostGeoreferenceVerified = hostGeo?.Confidence == GeoConfidenceLevel.Verified,
            HostHeightDatum = NormalizeStoredDatum(hostTilesMeta?.LastHeightDatum),
            HostGeoidModel = string.Empty,
            HostGeoidOffsetMeters = hostTilesMeta != null && hostTilesMeta.HasGeoidOffset ? hostTilesMeta.LastGeoidHeightOffsetMeters : (double?)null,
            HostStationFolder = stationFolder,
            HostLevels = hostLevels,
            LinkedModels = links,
            StationFolder = stationSnapshot,
            Options = effectiveOptions,
        };
    }

    private static List<FederationLinkSnapshot> CollectLinkedModels(
        Document hostDocument,
        ProjectLocation? hostLocation,
        string stationFolder,
        HashSet<string>? recordedExportLinkIds,
        FederationCheckOptions options)
    {
        List<FederationLinkSnapshot> result = new();
        double baselineFeet = Math.Max(1.0d, options.ControlPointBaselineMeters) / CrsTransformer.FeetToMetersFactor;

        foreach (RevitLinkInstance linkInstance in new FilteredElementCollector(hostDocument)
            .OfClass(typeof(RevitLinkInstance))
            .WhereElementIsNotElementType()
            .Cast<RevitLinkInstance>())
        {
            Document? linkedDoc = linkInstance.GetLinkDocument();
            string linkName = linkedDoc?.Title ?? linkInstance.Name ?? $"Link {linkInstance.Id.Value}";
            string sourcePath = TryReadLinkSourcePath(hostDocument, linkInstance, linkedDoc);
            bool isIncludedInExport = recordedExportLinkIds == null || recordedExportLinkIds.Contains(linkInstance.UniqueId);

            if (linkedDoc is null)
            {
                result.Add(new FederationLinkSnapshot(
                    linkInstanceElementId: linkInstance.Id.Value,
                    linkInstanceUniqueId: linkInstance.UniqueId,
                    name: linkName,
                    isLoaded: false,
                    sourcePath: sourcePath,
                    isIncludedInHubExport: isIncludedInExport));
                continue;
            }

            GeoProjectInfo? linkGeo = TryLoadGeoProjectInfo(linkedDoc);
            (int? linkEpsg, string linkCrsName) = ResolveCrs(linkGeo);
            TilesHeightMetadata? linkTilesMeta = TryLoadTilesHeightMetadata(linkedDoc);
            string linkModelId = linkGeo?.ModelId?.Trim() ?? string.Empty;
            string linkModelRole = GisStandardStateReader.ReadModelRoleText(linkedDoc);

            GisModelFile? linkGisModel = TryLoadGisModelForDocument(stationFolder, linkModelId);
            (KanizuFederationMode? linkFedMode, string? linkHubId, string? linkRawMode) = ParseFederation(linkGisModel?.Federation);

            Transform totalTransform = linkInstance.GetTotalTransform();
            IReadOnlyList<ControlPointPair> controlPoints = SampleControlPoints(
                hostLocation,
                linkedDoc.ActiveProjectLocation,
                totalTransform,
                baselineFeet,
                options.ControlPointBaselineMeters);

            List<FederationLevelSnapshot> linkLevels = CollectDocumentLevels(
                linkedDoc,
                hostLocation,
                linkToHostTransform: totalTransform);

            result.Add(new FederationLinkSnapshot(
                linkInstanceElementId: linkInstance.Id.Value,
                linkInstanceUniqueId: linkInstance.UniqueId,
                name: linkName,
                isLoaded: true,
                sourcePath: sourcePath,
                modelId: linkModelId,
                modelRole: linkModelRole,
                federationMode: linkFedMode,
                hubModelId: linkHubId,
                rawFederationMode: linkRawMode,
                crsEpsg: linkEpsg,
                crsName: linkCrsName,
                isGeoreferenceVerified: linkGeo?.Confidence == GeoConfidenceLevel.Verified,
                heightDatum: NormalizeStoredDatum(linkTilesMeta?.LastHeightDatum),
                geoidModel: string.Empty,
                geoidOffsetMeters: linkTilesMeta != null && linkTilesMeta.HasGeoidOffset ? linkTilesMeta.LastGeoidHeightOffsetMeters : (double?)null,
                controlPoints: controlPoints,
                levels: linkLevels,
                isIncludedInHubExport: isIncludedInExport));
        }

        return result;
    }

    private static IReadOnlyList<ControlPointPair> SampleControlPoints(
        ProjectLocation? hostLocation,
        ProjectLocation? linkLocation,
        Transform linkToHostTransform,
        double baselineFeet,
        double baselineMeters)
    {
        if (hostLocation is null || linkLocation is null)
        {
            return Array.Empty<ControlPointPair>();
        }

        string bLabel = baselineMeters.ToString("0.#", CultureInfo.InvariantCulture);
        (string Label, XYZ LinkPoint)[] samples =
        {
            ("Origin (0,0,0)", XYZ.Zero),
            ($"+X Baseline ({bLabel}m,0,0)", new XYZ(baselineFeet, 0d, 0d)),
            ($"+Y Baseline (0,{bLabel}m,0)", new XYZ(0d, baselineFeet, 0d)),
        };

        List<ControlPointPair> pairs = new(samples.Length);
        foreach ((string label, XYZ linkPoint) in samples)
        {
            XYZ hostPoint = linkToHostTransform.OfPoint(linkPoint);
            ProjectPosition hostPos = hostLocation.GetProjectPosition(hostPoint);
            ProjectPosition linkPos = linkLocation.GetProjectPosition(linkPoint);
            pairs.Add(new ControlPointPair(
                label,
                ToSharedMeters(hostPos),
                ToSharedMeters(linkPos)));
        }

        return pairs;
    }

    private static FederationPoint3D ToSharedMeters(ProjectPosition position) =>
        new(
            position.EastWest * CrsTransformer.FeetToMetersFactor,
            position.NorthSouth * CrsTransformer.FeetToMetersFactor,
            position.Elevation * CrsTransformer.FeetToMetersFactor);

    private static List<FederationLevelSnapshot> CollectDocumentLevels(
        Document document,
        ProjectLocation? hostLocation,
        Transform? linkToHostTransform)
    {
        HashSet<long> levelsWithFloors = new();
        foreach (Floor floor in new FilteredElementCollector(document)
            .OfClass(typeof(Floor))
            .WhereElementIsNotElementType()
            .Cast<Floor>())
        {
            if (floor.LevelId != ElementId.InvalidElementId)
            {
                levelsWithFloors.Add(floor.LevelId.Value);
            }
        }

        List<FederationLevelSnapshot> levels = new();
        foreach (Level level in new FilteredElementCollector(document)
            .OfClass(typeof(Level))
            .Cast<Level>())
        {
            double zFeetInHostFrame = linkToHostTransform != null
                ? linkToHostTransform.OfPoint(new XYZ(0d, 0d, level.ProjectElevation)).Z
                : level.ProjectElevation;

            double elevationMeters = hostLocation != null
                ? hostLocation.GetProjectPosition(new XYZ(0d, 0d, zFeetInHostFrame)).Elevation * CrsTransformer.FeetToMetersFactor
                : zFeetInHostFrame * CrsTransformer.FeetToMetersFactor;

            string levelId = ReadSharedString(level, GisSharedParameters.LevelId);
            int? ordinal = ReadSharedInt(level, GisSharedParameters.Ordinal);
            string shortName = ReadSharedString(level, GisSharedParameters.ShortName);

            levels.Add(new FederationLevelSnapshot(
                elementId: level.Id.Value,
                uniqueId: level.UniqueId,
                name: level.Name ?? string.Empty,
                levelId: levelId,
                ordinal: ordinal,
                shortName: shortName,
                projectElevationMeters: elevationMeters,
                hasFloors: levelsWithFloors.Contains(level.Id.Value)));
        }

        return levels;
    }

    private static (int? Epsg, string Name) ResolveCrs(GeoProjectInfo? geoInfo)
    {
        if (geoInfo?.ProjectCrs is { EpsgCode: > 0 } rawCrs)
        {
            CrsReference migrated = JapanCrsPresets.MigrateLegacyReference(rawCrs);
            return (migrated.EpsgCode, migrated.NameSnapshot ?? string.Empty);
        }

        return (null, string.Empty);
    }

    private static GeoProjectInfo? TryLoadGeoProjectInfo(Document document)
    {
        try
        {
            return new GeoProjectInfoStorage().Load(new RevitDocumentHandle(document));
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static TilesHeightMetadata? TryLoadTilesHeightMetadata(Document document)
    {
        try
        {
            return new ModuleStateStorage().Load<TilesHeightMetadata>(
                new RevitDocumentHandle(document),
                ModuleStateIds.Tiles3DExport);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static GisModelFile? TryLoadGisModelForDocument(string stationFolder, string? modelId)
    {
        if (string.IsNullOrWhiteSpace(stationFolder) || string.IsNullOrWhiteSpace(modelId))
        {
            return null;
        }

        try
        {
            string path = GisModelFile.PathFor(stationFolder, modelId!);
            return GisModelFile.TryLoad(path);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (KanizuFederationMode? Mode, string? HubModelId, string? RawMode) ParseFederation(GisModelFederation? federation)
    {
        if (federation == null)
        {
            return (null, null, null);
        }

        string? raw = string.IsNullOrWhiteSpace(federation.Mode) ? null : federation.Mode!.Trim();
        KanizuFederationMode? parsed = raw != null && GisModelFederation.TryParseMode(raw, out KanizuFederationMode mode)
            ? mode
            : (KanizuFederationMode?)null;
        string? hubId = string.IsNullOrWhiteSpace(federation.HubModelId) ? null : federation.HubModelId!.Trim();
        return (parsed, hubId, raw);
    }

    private static string TryReadLinkSourcePath(Document hostDocument, RevitLinkInstance instance, Document? linkedDoc)
    {
        if (linkedDoc != null && !string.IsNullOrWhiteSpace(linkedDoc.PathName))
        {
            return linkedDoc.PathName.Trim();
        }

        try
        {
            if (hostDocument.GetElement(instance.GetTypeId()) is RevitLinkType linkType)
            {
                ExternalFileReference? extRef = linkType.GetExternalFileReference();
                if (extRef != null)
                {
                    ModelPath? modelPath = extRef.GetAbsolutePath();
                    if (modelPath != null)
                    {
                        return ModelPathUtils.ConvertModelPathToUserVisiblePath(modelPath) ?? string.Empty;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Cloud/workshared links may not have a local file reference; degrade gracefully.
        }

        return string.Empty;
    }

    private static string NormalizeStoredDatum(string? datum)
    {
        if (string.IsNullOrWhiteSpace(datum))
        {
            return string.Empty;
        }

        string trimmed = datum!.Trim();
        return string.Equals(trimmed, CesiumHeightDatums.Unknown, StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : CesiumHeightDatums.Normalize(trimmed);
    }

    private static string ReadSharedString(Element element, string parameterName)
    {
        Parameter? parameter = GisStandardStateReader.FindSharedParameter(element, parameterName, allowNameFallback: true);
        if (parameter == null || !parameter.HasValue)
        {
            return string.Empty;
        }

        string? value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return value?.Trim() ?? string.Empty;
    }

    private static int? ReadSharedInt(Element element, string parameterName)
    {
        Parameter? parameter = GisStandardStateReader.FindSharedParameter(element, parameterName, allowNameFallback: true);
        if (parameter == null || !parameter.HasValue)
        {
            return null;
        }

        if (parameter.StorageType == StorageType.Integer)
        {
            return parameter.AsInteger();
        }

        string? text = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : (int?)null;
    }

    /// <summary>
    /// Minimal read-only projection of <c>Tiles3DExportState</c> JSON read via <see cref="ModuleStateStorage"/>
    /// without adding a project dependency from <c>FloorPlanExport</c> onto <c>Tiles3DExport</c>.
    /// </summary>
    private sealed class TilesHeightMetadata
    {
        public string? LastHeightDatum { get; set; }

        public double LastGeoidHeightOffsetMeters { get; set; }

        public bool HasGeoidOffset =>
            !double.IsNaN(LastGeoidHeightOffsetMeters)
            && !double.IsInfinity(LastGeoidHeightOffsetMeters)
            && Math.Abs(LastGeoidHeightOffsetMeters) > 1e-9d;
    }
}

/// <summary>
/// High-level Revit API entry point for running the complete T10 Federation Checker on an open document.
/// Designed for direct invocation by readiness inspection and T11 Contractor Handover Check.
/// </summary>
public static class RevitFederationChecker
{
    public static FederationCheckReport CheckDocument(
        Document document,
        string? stationFolderOverride = null,
        bool includeStationFolder = true,
        FederationCheckOptions? options = null)
    {
        FederationCheckSnapshot snapshot = RevitFederationSnapshotCollector.Collect(
            document,
            stationFolderOverride,
            includeStationFolder,
            options);
        return FederationChecker.Evaluate(snapshot);
    }
}
