using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.CesiumHandoff;
using RevitGeoSuite.Core.Text;
using RevitGeoSuite.FloorPlanExport.Core.Standards;
using RevitGeoSuite.FloorPlanExport.Core.StarterKit;

namespace RevitGeoSuite.FloorPlanExport.Core.Federation;

/// <summary>
/// Pure-C# station-folder reader and federation checker (<c>KZ-FED-006</c>) for multi-building
/// independent-georef and hybrid station folders containing <c>gis-models/*.gis-model.json</c>,
/// <c>revit/&lt;model&gt;/&lt;version&gt;/cesium-package.json</c>, and optional <c>tiles/levels.json</c>.
/// </summary>
public static class StationFolderFederationCheck
{
    /// <summary>
    /// Reads all model metadata and latest published packages from a shared station folder into a
    /// pure-C# <see cref="StationFolderSnapshot"/> without requiring Revit API.
    /// </summary>
    public static StationFolderSnapshot LoadSnapshot(string stationFolderPath)
    {
        if (string.IsNullOrWhiteSpace(stationFolderPath))
        {
            return new StationFolderSnapshot(string.Empty, Array.Empty<StationFolderModelEntry>());
        }

        string root = Path.GetFullPath(stationFolderPath.Trim());
        if (!Directory.Exists(root))
        {
            return new StationFolderSnapshot(
                root,
                Array.Empty<StationFolderModelEntry>(),
                new[] { $"Station folder '{root}' does not exist." });
        }

        List<string> warnings = new();
        List<GisModelRecord> gisModels = LoadGisModelFiles(root, warnings);
        List<PublishedPackageRecord> publishedPackages = LoadLatestPublishedPackages(root, warnings);

        List<StationFolderModelEntry> entries = MergeRecords(gisModels, publishedPackages);
        return new StationFolderSnapshot(root, entries, warnings);
    }

    /// <summary>
    /// Evaluates a station folder on disk for full federation consistency (<c>KZ-FED-001..009</c>).
    /// </summary>
    public static FederationCheckReport EvaluateFolder(
        string stationFolderPath,
        FederationCheckOptions? options = null)
    {
        StationFolderSnapshot snapshot = LoadSnapshot(stationFolderPath);
        return Evaluate(snapshot, options);
    }

    /// <summary>
    /// Evaluates an in-memory <see cref="StationFolderSnapshot"/> for full federation consistency
    /// (<c>KZ-FED-001..009</c>) across unlinked independent buildings and/or hub packages.
    /// </summary>
    public static FederationCheckReport Evaluate(
        StationFolderSnapshot snapshot,
        FederationCheckOptions? options = null)
    {
        if (snapshot is null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        return FederationChecker.EvaluateStationFolder(snapshot, options);
    }

    private static List<GisModelRecord> LoadGisModelFiles(string stationRoot, ICollection<string> warnings)
    {
        List<GisModelRecord> records = new();
        string gisModelsDir = Path.Combine(stationRoot, GisModelFile.FolderName);
        if (!Directory.Exists(gisModelsDir))
        {
            return records;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(gisModelsDir, "*" + GisModelFile.Suffix)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            warnings.Add($"Could not enumerate '{gisModelsDir}': {ex.Message}");
            return records;
        }

        foreach (string filePath in files)
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            try
            {
                GisModelFile? parsed = GisModelFile.TryLoad(filePath);
                if (parsed != null)
                {
                    records.Add(new GisModelRecord(filePath, parsed));
                }
            }
            catch (Exception ex) when (IsIoError(ex) || ex is InvalidDataException)
            {
                warnings.Add($"Could not parse '{fileName}': {ex.Message}");
            }
        }

        return records;
    }

    private static List<PublishedPackageRecord> LoadLatestPublishedPackages(string stationRoot, ICollection<string> warnings)
    {
        List<PublishedPackageRecord> records = new();
        string revitRoot = Path.Combine(stationRoot, CesiumStationFolder.RevitDirectoryName);
        if (!Directory.Exists(revitRoot))
        {
            return records;
        }

        string[] modelDirs;
        try
        {
            modelDirs = Directory.GetDirectories(revitRoot)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            warnings.Add($"Could not enumerate '{revitRoot}': {ex.Message}");
            return records;
        }

        foreach (string modelDir in modelDirs)
        {
            string folderName = Path.GetFileName(modelDir);
            if (folderName.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            PublishedPackageRecord? latest = TryLoadLatestVersionInModelFolder(modelDir, warnings);
            if (latest != null)
            {
                records.Add(latest);
            }
        }

        // Collapse multiple model folders that came from the exact same documentKey + modelId (a simple building rename),
        // keeping the newest version; distinct documentKeys sharing a modelId are preserved so KZ-FED-002 catches Save-As clones.
        List<PublishedPackageRecord> deduped = new();
        Dictionary<string, int> indexByModelAndDocKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (PublishedPackageRecord record in records)
        {
            string modelId = (record.Manifest.Source?.ModelId ?? string.Empty).Trim();
            string docKey = (record.Manifest.Source?.DocumentKey ?? string.Empty).Trim();
            if (modelId.Length > 0 && docKey.Length > 0)
            {
                string compositeKey = modelId + "|" + docKey;
                if (indexByModelAndDocKey.TryGetValue(compositeKey, out int existingIndex))
                {
                    PublishedPackageRecord existing = deduped[existingIndex];
                    if (record.Manifest.CreatedUtc.ToUniversalTime() > existing.Manifest.CreatedUtc.ToUniversalTime())
                    {
                        deduped[existingIndex] = record;
                    }

                    continue;
                }

                indexByModelAndDocKey[compositeKey] = deduped.Count;
            }

            deduped.Add(record);
        }

        return deduped;
    }

    private static PublishedPackageRecord? TryLoadLatestVersionInModelFolder(string modelDir, ICollection<string> warnings)
    {
        string[] versionDirs;
        try
        {
            versionDirs = Directory.GetDirectories(modelDir)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch (Exception ex) when (IsIoError(ex))
        {
            warnings.Add($"Could not enumerate versions in '{modelDir}': {ex.Message}");
            return null;
        }

        string? bestDir = null;
        CesiumPackageManifest? bestManifest = null;
        DateTime bestCreated = DateTime.MinValue;

        foreach (string versionDir in versionDirs)
        {
            string name = Path.GetFileName(versionDir);
            if (name.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            CesiumPackageManifest? manifest = CesiumStationFolder.TryReadManifest(versionDir);
            if (manifest is null)
            {
                continue;
            }

            DateTime created = manifest.CreatedUtc.ToUniversalTime();
            if (bestManifest is null
                || created > bestCreated
                || (created == bestCreated && string.CompareOrdinal(name, Path.GetFileName(bestDir!)) > 0))
            {
                bestDir = versionDir;
                bestManifest = manifest;
                bestCreated = created;
            }
        }

        if (bestDir is null || bestManifest is null)
        {
            return null;
        }

        IReadOnlyList<TilesLevelIndexRow> tilesLevels = TryReadTilesLevelsJson(bestDir, bestManifest, warnings);
        return new PublishedPackageRecord(bestDir, bestManifest, tilesLevels);
    }

    private static IReadOnlyList<TilesLevelIndexRow> TryReadTilesLevelsJson(
        string versionDir,
        CesiumPackageManifest manifest,
        ICollection<string> warnings)
    {
        string relative = string.IsNullOrWhiteSpace(manifest.Tiles?.Levels)
            ? Path.Combine("tiles", "levels.json")
            : manifest.Tiles!.Levels!.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.Combine(versionDir, relative);
        if (!File.Exists(fullPath))
        {
            return Array.Empty<TilesLevelIndexRow>();
        }

        try
        {
            JObject root = JObject.Parse(File.ReadAllText(fullPath, Encoding.UTF8));
            JArray? levelsArray = root["levels"] as JArray;
            if (levelsArray is null)
            {
                return Array.Empty<TilesLevelIndexRow>();
            }

            List<TilesLevelIndexRow> rows = new();
            foreach (JToken token in levelsArray)
            {
                if (token is not JObject obj)
                {
                    continue;
                }

                string key = obj.Value<string>("levelKey")?.Trim() ?? string.Empty;
                string name = obj.Value<string>("levelName")?.Trim() ?? string.Empty;
                double? elev = obj.Value<double?>("levelElevationMeters");
                List<string> uniqueIds = (obj["levelUniqueIds"] as JArray)?
                    .Select(item => item.Value<string>()?.Trim() ?? string.Empty)
                    .Where(id => id.Length > 0)
                    .ToList() ?? new List<string>();
                rows.Add(new TilesLevelIndexRow(key, name, elev, uniqueIds));
            }

            return rows;
        }
        catch (Exception ex) when (IsIoError(ex) || ex is Newtonsoft.Json.JsonException)
        {
            warnings.Add($"Could not read '{fullPath}': {ex.Message}");
            return Array.Empty<TilesLevelIndexRow>();
        }
    }

    private static List<StationFolderModelEntry> MergeRecords(
        List<GisModelRecord> gisModels,
        List<PublishedPackageRecord> packages)
    {
        Dictionary<string, List<GisModelRecord>> gisById = new(StringComparer.OrdinalIgnoreCase);
        foreach (GisModelRecord rec in gisModels)
        {
            string id = (rec.File.Identity?.ModelId ?? string.Empty).Trim();
            if (id.Length == 0)
            {
                continue;
            }

            if (!gisById.TryGetValue(id, out List<GisModelRecord>? list))
            {
                gisById[id] = list = new List<GisModelRecord>();
            }

            list.Add(rec);
        }

        HashSet<GisModelRecord> matchedGis = new();
        List<StationFolderModelEntry> entries = new();

        foreach (PublishedPackageRecord pkg in packages)
        {
            string pkgModelId = (pkg.Manifest.Source?.ModelId ?? string.Empty).Trim();
            GisModelRecord? matchedGisRecord = null;
            if (pkgModelId.Length > 0
                && gisById.TryGetValue(pkgModelId, out List<GisModelRecord>? candidates)
                && candidates.Count == 1
                && matchedGis.Add(candidates[0]))
            {
                matchedGisRecord = candidates[0];
            }

            entries.Add(BuildEntry(matchedGisRecord, pkg));
        }

        foreach (GisModelRecord remaining in gisModels)
        {
            if (matchedGis.Contains(remaining))
            {
                continue;
            }

            entries.Add(BuildEntry(remaining, package: null));
        }

        return entries;
    }

    private static StationFolderModelEntry BuildEntry(GisModelRecord? gis, PublishedPackageRecord? package)
    {
        CesiumPackageManifest? manifest = package?.Manifest;
        GisModelFile? gisFile = gis?.File;

        string modelId = FirstNonEmpty(manifest?.Source?.ModelId, gisFile?.Identity?.ModelId);
        string modelName = FirstNonEmpty(manifest?.Building?.Name, gisFile?.Identity?.Building, manifest?.Source?.Model);
        string documentKey = (manifest?.Source?.DocumentKey ?? string.Empty).Trim();
        string sourceModelFile = (manifest?.Source?.Model ?? string.Empty).Trim();
        string modelRole = (manifest?.Source?.ModelRole ?? string.Empty).Trim();

        KanizuFederationMode? federationMode = null;
        string? rawFederationMode = null;
        string? hubModelId = null;
        if (gisFile?.Federation != null)
        {
            rawFederationMode = string.IsNullOrWhiteSpace(gisFile.Federation.Mode) ? null : gisFile.Federation.Mode!.Trim();
            if (rawFederationMode != null && GisModelFederation.TryParseMode(rawFederationMode, out KanizuFederationMode parsedMode))
            {
                federationMode = parsedMode;
            }

            hubModelId = string.IsNullOrWhiteSpace(gisFile.Federation.HubModelId) ? null : gisFile.Federation.HubModelId!.Trim();
        }

        int? crsEpsg = manifest?.Crs is { ProjectEpsg: > 0 } ? manifest.Crs.ProjectEpsg
            : manifest?.Crs is { GisEpsg: > 0 } ? manifest.Crs.GisEpsg
            : gisFile?.Identity?.CrsEpsg;
        string heightDatum = (manifest?.Anchor?.HeightDatum ?? string.Empty).Trim();
        string geoidModel = (manifest?.Anchor?.GeoidModel ?? string.Empty).Trim();
        double? geoidOffsetMeters = manifest?.Anchor?.GeoidOffsetMeters;

        List<StationFolderPublishedLink> includedLinks = CollectIncludedLinks(gisFile, manifest);
        List<StationFolderLevelMapEntry> levelMapEntries = (manifest?.LevelMap ?? new List<CesiumPackageLevelMapEntry>())
            .Where(entry => entry != null)
            .Select(entry => new StationFolderLevelMapEntry(entry.GisLevelId, entry.TilesLevelKey, entry.Name))
            .ToList();
        List<FederationLevelSnapshot> levels = BuildLevels(gisFile, manifest, package?.TilesLevels);

        return new StationFolderModelEntry(
            modelId: modelId,
            modelName: modelName,
            documentKey: documentKey,
            sourceModelFile: sourceModelFile,
            modelRole: modelRole,
            federationMode: federationMode,
            hubModelId: hubModelId,
            rawFederationMode: rawFederationMode,
            crsEpsg: crsEpsg,
            heightDatum: heightDatum,
            geoidModel: geoidModel,
            geoidOffsetMeters: geoidOffsetMeters,
            hasPublishedPackage: package != null,
            hasGisModelSettings: gis != null,
            publishedPackagePath: package?.VersionDirectory ?? string.Empty,
            gisModelFilePath: gis?.FilePath ?? string.Empty,
            includedLinks: includedLinks,
            levels: levels,
            levelMapEntries: levelMapEntries);
    }

    private static List<StationFolderPublishedLink> CollectIncludedLinks(GisModelFile? gisFile, CesiumPackageManifest? manifest)
    {
        List<StationFolderPublishedLink> links = new();
        Dictionary<string, StationFolderPublishedLink> byUniqueId = new(StringComparer.OrdinalIgnoreCase);

        if (gisFile?.Links != null)
        {
            foreach (GisModelLink link in gisFile.Links)
            {
                if (link == null)
                {
                    continue;
                }

                StationFolderPublishedLink entry = new(link.UniqueId, link.Name, link.ModelId);
                if (entry.UniqueId.Length > 0)
                {
                    byUniqueId[entry.UniqueId] = entry;
                }
                else if (entry.Name.Length > 0 || entry.ModelId.Length > 0)
                {
                    links.Add(entry);
                }
            }
        }

        if (manifest?.Export?.Links != null)
        {
            foreach (CesiumPackageExportItem link in manifest.Export.Links)
            {
                if (link == null)
                {
                    continue;
                }

                string uid = (link.UniqueId ?? string.Empty).Trim();
                string name = (link.Name ?? string.Empty).Trim();
                if (uid.Length > 0)
                {
                    if (byUniqueId.TryGetValue(uid, out StationFolderPublishedLink? existing))
                    {
                        byUniqueId[uid] = new StationFolderPublishedLink(
                            uid,
                            FirstNonEmpty(existing.Name, name),
                            existing.ModelId);
                    }
                    else
                    {
                        byUniqueId[uid] = new StationFolderPublishedLink(uid, name);
                    }
                }
                else if (name.Length > 0)
                {
                    links.Add(new StationFolderPublishedLink(string.Empty, name));
                }
            }
        }

        links.InsertRange(0, byUniqueId.Values);
        return links;
    }

    private static List<FederationLevelSnapshot> BuildLevels(
        GisModelFile? gisFile,
        CesiumPackageManifest? manifest,
        IReadOnlyList<TilesLevelIndexRow>? tilesLevels)
    {
        double? anchorHeight = AnchorOrthometricHeight(manifest?.Anchor);
        Dictionary<string, double> elevByUniqueId = new(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, double> elevByKey = new(StringComparer.OrdinalIgnoreCase);
        foreach (TilesLevelIndexRow row in tilesLevels ?? Array.Empty<TilesLevelIndexRow>())
        {
            double? elevation = SharedElevation(anchorHeight, row.ElevationMeters);
            if (!elevation.HasValue)
            {
                continue;
            }

            foreach (string uid in row.LevelUniqueIds)
            {
                if (!elevByUniqueId.ContainsKey(uid))
                {
                    elevByUniqueId[uid] = elevation.Value;
                }
            }

            if (row.LevelKey.Length > 0 && !elevByKey.ContainsKey(row.LevelKey))
            {
                elevByKey[row.LevelKey] = elevation.Value;
            }
        }

        List<FederationLevelSnapshot> result = new();
        HashSet<string> seenLevelIds = new(StringComparer.OrdinalIgnoreCase);
        ISet<string> exportLevelUniqueIds = gisFile?.ExportLevelUniqueIds() ?? new HashSet<string>();

        if (gisFile?.Levels != null && gisFile.Levels.Count > 0)
        {
            foreach (GisModelLevel level in gisFile.Levels)
            {
                if (level == null)
                {
                    continue;
                }

                string key = LevelKey.Build(level.Name);
                double? elevation = level.ElevationMeters;
                if (!elevation.HasValue && !string.IsNullOrWhiteSpace(level.UniqueId)
                    && elevByUniqueId.TryGetValue(level.UniqueId.Trim(), out double byUid))
                {
                    elevation = byUid;
                }

                if (!elevation.HasValue && elevByKey.TryGetValue(key, out double byKey))
                {
                    elevation = byKey;
                }

                FederationLevelSnapshot snap = new(
                    elementId: 0,
                    uniqueId: level.UniqueId,
                    name: level.Name,
                    levelId: level.LevelId,
                    ordinal: level.Ordinal,
                    shortName: level.ShortName,
                    projectElevationMeters: elevation,
                    hasFloors: true,
                    isExportLevel: !string.IsNullOrWhiteSpace(level.UniqueId) && exportLevelUniqueIds.Contains(level.UniqueId.Trim()));
                result.Add(snap);
                if (snap.LevelId.Length > 0)
                {
                    seenLevelIds.Add(snap.LevelId);
                }
            }
        }

        if (result.Count == 0 && manifest?.LevelMap != null && manifest.LevelMap.Count > 0)
        {
            foreach (CesiumPackageLevelMapEntry mapEntry in manifest.LevelMap)
            {
                if (mapEntry == null)
                {
                    continue;
                }

                string key = !string.IsNullOrWhiteSpace(mapEntry.TilesLevelKey)
                    ? mapEntry.TilesLevelKey.Trim()
                    : LevelKey.Build(mapEntry.Name);
                double? elevation = elevByKey.TryGetValue(key, out double byKey) ? byKey : (double?)null;
                result.Add(new FederationLevelSnapshot(
                    elementId: 0,
                    uniqueId: string.Empty,
                    name: string.IsNullOrWhiteSpace(mapEntry.Name) ? key : mapEntry.Name,
                    levelId: mapEntry.GisLevelId,
                    ordinal: null,
                    shortName: string.Empty,
                    projectElevationMeters: elevation,
                    hasFloors: true));
            }
        }
        else if (result.Count == 0 && tilesLevels != null && tilesLevels.Count > 0)
        {
            foreach (TilesLevelIndexRow row in tilesLevels)
            {
                result.Add(new FederationLevelSnapshot(
                    elementId: 0,
                    uniqueId: row.LevelUniqueIds.FirstOrDefault() ?? string.Empty,
                    name: string.IsNullOrWhiteSpace(row.LevelName) ? row.LevelKey : row.LevelName,
                    levelId: string.Empty,
                    ordinal: null,
                    shortName: string.Empty,
                    projectElevationMeters: SharedElevation(anchorHeight, row.ElevationMeters),
                    hasFloors: true));
            }
        }

        return result;
    }

    /// <summary>
    /// <c>tiles/levels.json</c> elevations are relative to the package anchor; adding the anchor's
    /// orthometric height puts them in the shared (T.P.) frame every building in the station shares.
    /// </summary>
    private static double? AnchorOrthometricHeight(CesiumPackageAnchor? anchor)
    {
        if (anchor is null)
        {
            return null;
        }

        if (anchor.OrthometricHeightMeters.HasValue)
        {
            return anchor.OrthometricHeightMeters.Value;
        }

        return anchor.GeoidOffsetMeters.HasValue ? anchor.ElevationMeters - anchor.GeoidOffsetMeters.Value : null;
    }

    private static double? SharedElevation(double? anchorHeight, double? anchorRelativeMeters)
    {
        if (!anchorHeight.HasValue || !anchorRelativeMeters.HasValue)
        {
            return null;
        }

        double shared = anchorHeight.Value + anchorRelativeMeters.Value;
        return FederationNumbers.IsFinite(shared) ? shared : null;
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value!.Trim();
            }
        }

        return string.Empty;
    }

    private static bool IsIoError(Exception ex) =>
        ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException;

    private sealed class GisModelRecord
    {
        public GisModelRecord(string filePath, GisModelFile file)
        {
            FilePath = filePath;
            File = file;
        }

        public string FilePath { get; }

        public GisModelFile File { get; }
    }

    private sealed class PublishedPackageRecord
    {
        public PublishedPackageRecord(
            string versionDirectory,
            CesiumPackageManifest manifest,
            IReadOnlyList<TilesLevelIndexRow> tilesLevels)
        {
            VersionDirectory = versionDirectory;
            Manifest = manifest;
            TilesLevels = tilesLevels;
        }

        public string VersionDirectory { get; }

        public CesiumPackageManifest Manifest { get; }

        public IReadOnlyList<TilesLevelIndexRow> TilesLevels { get; }
    }

    private sealed class TilesLevelIndexRow
    {
        public TilesLevelIndexRow(string levelKey, string levelName, double? elevationMeters, List<string> levelUniqueIds)
        {
            LevelKey = levelKey;
            LevelName = levelName;
            ElevationMeters = elevationMeters;
            LevelUniqueIds = levelUniqueIds;
        }

        public string LevelKey { get; }

        public string LevelName { get; }

        public double? ElevationMeters { get; }

        public List<string> LevelUniqueIds { get; }
    }
}
