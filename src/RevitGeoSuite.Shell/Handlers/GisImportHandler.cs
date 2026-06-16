using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Gis;
using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>Returns Revit levels used by the floor-aware local GIS DXF import UI.</summary>
public sealed class GisImportOptionsHandler : IRpcHandler
{
    public string Method => "gis.importOptions";

    public async Task<object?> HandleAsync(object? payload)
    {
        return await RevitContext.Instance.InvokeWithDocumentAsync(BuildOptions).ConfigureAwait(false);
    }

    private static GisImportOptionsResponse BuildOptions(Document doc)
    {
        List<GisLevelOption> levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .ThenBy(level => level.Name, StringComparer.Ordinal)
            .Select(level => new GisLevelOption
            {
                Id = level.Id.Value,
                Name = level.Name ?? string.Empty,
                ElevationFeet = level.Elevation
            })
            .ToList();

        long? activeLevelId = null;
        if (doc.ActiveView is ViewPlan plan && plan.GenLevel is Level activeLevel)
        {
            activeLevelId = activeLevel.Id.Value;
        }

        long? defaultLevelId = activeLevelId.HasValue && levels.Any(level => level.Id == activeLevelId.Value)
            ? activeLevelId
            : levels.FirstOrDefault()?.Id;

        return new GisImportOptionsResponse
        {
            Levels = levels.ToArray(),
            DefaultLevelId = defaultLevelId
        };
    }
}

/// <summary>
/// Imports a local Shapefile or GeoPackage as a flat 2D CAD basemap by converting source GIS
/// geometries to the same model-frame DXF feature lists used by the PLATEAU basemap importer.
/// </summary>
public sealed class GisImportHandler : IRpcHandler
{
    private const string OpeningCategory = "opening";
    private const string UnitCategory = "unit";
    private const string LevelCategory = "level";
    private const string DetailCategory = "detail";

    private static readonly string[] CategoryPriority =
    {
        OpeningCategory,
        UnitCategory,
        LevelCategory,
        DetailCategory,
    };

    private static readonly IReadOnlyDictionary<string, string> DefaultCategoryColorHex = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [OpeningCategory] = "#FF0000",
        [UnitCategory] = "#00B050",
        [LevelCategory] = "#0070C0",
        [DetailCategory] = "#A6A6A6",
    };

    private readonly JobManager jobs;

    public GisImportHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "gis.import";

    public Task<object?> HandleAsync(object? payload)
    {
        JObject? jobj = payload as JObject;
        ImportAssignment[] assignments = GetImportAssignments(jobj, out bool hasExplicitAssignments);
        IReadOnlyDictionary<string, DxfLayerColor> categoryColors = ResolveCategoryColors(jobj);
        string[] importPaths = assignments.Select(assignment => assignment.Path).ToArray();
        string outputFolder = jobj?.Value<string>("outputFolder")?.Trim() ?? string.Empty;

        if (importPaths.Length == 0)
        {
            return Task.FromResult<object?>(new { error = "At least one GIS file is required" });
        }

        if (hasExplicitAssignments && assignments.Any(assignment => !assignment.LevelId.HasValue))
        {
            return Task.FromResult<object?>(new { error = "Select a Revit level for each GIS file." });
        }

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            return Task.FromResult<object?>(new { error = "Choose an output folder for the DXF files." });
        }

        string? unsupportedPath = importPaths.FirstOrDefault(path => !GisFileReader.IsSupported(path));
        if (!string.IsNullOrWhiteSpace(unsupportedPath))
        {
            return Task.FromResult<object?>(new
            {
                error = $"Unsupported GIS file '{Path.GetFileName(unsupportedPath)}'. Supported GIS files are Shapefile (.shp) and GeoPackage (.gpkg)."
            });
        }

        string basemapName = BuildBasemapName(jobj?.Value<string>("basemapName"), importPaths);
        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "preparing", Percent = 0, Message = "Resolving georeference..." });
            PreparedRevitContext prep = await RevitContext.Instance.InvokeWithDocumentAsync(ResolvePreparedContext)
                .ConfigureAwait(false);
            PlateauImportReferenceContext? referenceContext = prep.ReferenceContext;

            if (referenceContext is null)
            {
                throw new InvalidOperationException(
                    "This project isn't georeferenced yet. Complete Georeference Setup (CRS + Project Base Point) before importing a GIS basemap.");
            }

            int projectEpsg = referenceContext.ProjectCrs.EpsgCode;
            if (projectEpsg <= 0)
            {
                throw new InvalidOperationException("The project's CRS is missing an EPSG code; GIS import needs a projected project CRS.");
            }

            ct.ThrowIfCancellationRequested();

            Dictionary<long, GisImportLevel> levelsById = prep.Levels.ToDictionary(level => level.Id);
            if (hasExplicitAssignments)
            {
                long? missingLevelId = assignments
                    .Select(assignment => assignment.LevelId)
                    .FirstOrDefault(levelId => levelId.HasValue && !levelsById.ContainsKey(levelId.Value));
                if (missingLevelId.HasValue)
                {
                    throw new InvalidOperationException(
                        string.Format(CultureInfo.InvariantCulture, "The selected Revit level ({0}) was not found.", missingLevelId.Value));
                }
            }

            List<string> warnings = new();
            Dictionary<string, PreparedGisDxfGroup> groups = CreateImportGroups(assignments, levelsById, hasExplicitAssignments);
            HashSet<int> sourceEpsgCodes = new();
            int totalFeatures = 0;
            int totalPolygons = 0;
            int totalLines = 0;
            int totalPoints = 0;
            bool isSingleFile = assignments.Length == 1;

            for (int fileIndex = 0; fileIndex < assignments.Length; fileIndex++)
            {
                ImportAssignment assignment = assignments[fileIndex];
                string importPath = assignment.Path;
                string sourceFileName = Path.GetFileName(importPath);
                PreparedGisDxfGroup group = groups[assignment.GroupKey];
                int readPercent = 15 + (int)Math.Round(fileIndex * 40d / assignments.Length);

                progress.Report(new JobProgress
                {
                    Phase = "reading",
                    Percent = readPercent,
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "Reading GIS file {0} of {1}: {2}",
                        fileIndex + 1,
                        assignments.Length,
                        sourceFileName)
                });

                GisDataset dataset = GisFileReader.Read(importPath);
                warnings.AddRange(PrefixWarnings(sourceFileName, dataset.Warnings));
                if (dataset.SourceEpsg.HasValue)
                {
                    sourceEpsgCodes.Add(dataset.SourceEpsg.Value);
                }

                if (dataset.FeatureCount == 0)
                {
                    if (isSingleFile)
                    {
                        throw new InvalidOperationException(
                            warnings.FirstOrDefault()
                            ?? "The selected GIS file did not contain any geometry to import.");
                    }

                    warnings.Add(sourceFileName + ": no geometry was found; skipped.");
                    continue;
                }

                ct.ThrowIfCancellationRequested();

                int projectPercent = 45 + (int)Math.Round(fileIndex * 20d / assignments.Length);
                progress.Report(new JobProgress
                {
                    Phase = "reprojecting",
                    Percent = projectPercent,
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "Projecting GIS file {0} of {1}: {2}",
                        fileIndex + 1,
                        assignments.Length,
                        sourceFileName)
                });

                (Func<double, double, (double X, double Y)> projector, string? reprojectionWarning) =
                    CoordinateSystemCatalog.CreateReprojector(dataset.SourceEpsg, dataset.SourceWkt, projectEpsg);
                if (!string.IsNullOrWhiteSpace(reprojectionWarning))
                {
                    warnings.Add(sourceFileName + ": " + reprojectionWarning);
                }

                ct.ThrowIfCancellationRequested();

                IReadOnlyList<GisLayerGeometry> dxfLayers = PrepareDxfLayers(
                    dataset.Layers,
                    importPath,
                    group.SourceFileCount == 1,
                    assignment.Category);
                progress.Report(new JobProgress
                {
                    Phase = "building",
                    Percent = 65 + (int)Math.Round(fileIndex * 10d / assignments.Length),
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "Building 2D basemap geometry {0} of {1}: {2}",
                        fileIndex + 1,
                        assignments.Length,
                        sourceFileName)
                });

                GisBasemapDxfBuilder.BuildResult basemap = GisBasemapDxfBuilder.Build(
                    dxfLayers,
                    projector,
                    referenceContext,
                    group.UsedDxfLayerNames,
                    categoryColors.TryGetValue(assignment.Category, out DxfLayerColor? categoryColor) ? categoryColor : null);
                if (basemap.IsEmpty)
                {
                    if (isSingleFile)
                    {
                        throw new InvalidOperationException("The selected GIS file did not produce any 2D basemap geometry.");
                    }

                    warnings.Add(sourceFileName + ": no supported 2D geometry was produced; skipped.");
                    continue;
                }

                group.AddSource(importPath);
                group.Outlines.AddRange(basemap.Outlines);
                group.Lines.AddRange(basemap.Lines);
                group.PolygonCount += basemap.PolygonCount;
                group.LineCount += basemap.LineCount;
                group.PointCount += basemap.PointCount;
                totalFeatures += dataset.FeatureCount;
                totalPolygons += basemap.PolygonCount;
                totalLines += basemap.LineCount;
                totalPoints += basemap.PointCount;
                group.LayerSummaries.AddRange(dxfLayers.Select(layer => new GisLayerImportSummary(
                    sourceFileName,
                    layer.Name,
                    layer.Geometries.Count,
                    assignment.Category)));

                ct.ThrowIfCancellationRequested();
            }

            List<PreparedGisDxfGroup> outputGroups = groups.Values
                .Where(group => !group.IsEmpty)
                .OrderBy(group => group.Level?.ElevationFeet ?? double.NegativeInfinity)
                .ThenBy(group => group.Level?.Name ?? string.Empty, StringComparer.Ordinal)
                .ToList();

            if (outputGroups.Count == 0)
            {
                throw new InvalidOperationException(
                    warnings.FirstOrDefault()
                    ?? "The selected GIS files did not produce any 2D basemap geometry.");
            }

            ct.ThrowIfCancellationRequested();

            AssignOutputNames(outputGroups, basemapName);
            string resolvedOutputFolder = Path.GetFullPath(outputFolder);
            Directory.CreateDirectory(resolvedOutputFolder);
            HashSet<string> usedDxfPaths = new(StringComparer.OrdinalIgnoreCase);
            PlateauContextDxfImporter importer = new();

            List<string> finalWarnings = new(warnings);
            for (int outputIndex = 0; outputIndex < outputGroups.Count; outputIndex++)
            {
                PreparedGisDxfGroup group = outputGroups[outputIndex];
                progress.Report(new JobProgress
                {
                    Phase = "building",
                    Current = outputIndex + 1,
                    Total = outputGroups.Count,
                    Percent = 75 + (int)Math.Round(outputIndex / (double)Math.Max(1, outputGroups.Count) * 10d),
                    Message = string.Format(
                        CultureInfo.InvariantCulture,
                        "Writing DXF basemap {0} of {1}: {2}",
                        outputIndex + 1,
                        outputGroups.Count,
                        group.OutputName)
                });

                group.DxfPath = ReserveUniqueDxfPath(resolvedOutputFolder, group.OutputName, usedDxfPaths);
                PlateauContextDxfImporter.DxfBuildResult dxfBuild = importer.WriteOutlineDxf(
                    group.Outlines,
                    Array.Empty<PlateauContextOutlinesDxfWriter.AreaFeature>(),
                    group.Lines,
                    referenceContext,
                    group.DxfPath,
                    Array.Empty<string>());

                group.DxfEntityCount = dxfBuild.FeatureCount;
                finalWarnings.AddRange(dxfBuild.Warnings.Select(warning => group.OutputName + ": " + warning));
            }

            if (outputGroups.All(group => group.DxfEntityCount == 0))
            {
                throw new InvalidOperationException(
                    finalWarnings.FirstOrDefault()
                    ?? "The selected GIS files did not produce any DXF entities to link.");
            }

            warnings = finalWarnings;
            ct.ThrowIfCancellationRequested();

            progress.Report(new JobProgress { Phase = "importing", Percent = 90, Message = "Linking DXF basemap into Revit..." });
            DxfImportOutcome outcome = await RevitContext.Instance.InvokeWithDocumentAsync(doc =>
                ImportDxfGroups(doc, importer, outputGroups, progress, ct)).ConfigureAwait(false);
            warnings.AddRange(outcome.Warnings);

            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Link complete" });

            int? sourceEpsg = sourceEpsgCodes.Count == 1 ? sourceEpsgCodes.First() : null;
            int linkedDxfCount = outputGroups.Count(group => group.ImportedElementId.HasValue);
            int totalDxfEntities = outputGroups.Sum(group => group.DxfEntityCount);
            int totalLayerCount = outputGroups.Sum(group => group.LayerSummaries.Count);
            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Linked {0} GIS feature(s) from {1} file(s) as {2} 2D DXF CAD link(s) ({3} DXF entities, {4} layer(s)).",
                totalFeatures,
                importPaths.Length,
                linkedDxfCount,
                totalDxfEntities,
                totalLayerCount);

            return (object?)new
            {
                mode = "gis-dxf",
                linked = true,
                outputFolder = resolvedOutputFolder,
                importedElements = totalFeatures,
                featuresImported = totalFeatures,
                dxfEntities = totalDxfEntities,
                importedElementId = outputGroups.FirstOrDefault(group => group.ImportedElementId.HasValue)?.ImportedElementId,
                importedElementIds = outputGroups
                    .Where(group => group.ImportedElementId.HasValue)
                    .Select(group => group.ImportedElementId!.Value)
                    .ToArray(),
                outputs = outputGroups.Select(group => new
                {
                    name = group.OutputName,
                    levelId = group.Level?.Id,
                    levelName = group.Level?.Name,
                    importedElementId = group.ImportedElementId,
                    dxfPath = group.DxfPath,
                    features = group.FeatureCount,
                    dxfEntities = group.DxfEntityCount,
                    sourceFiles = group.SourcePaths.ToArray()
                }).ToArray(),
                layers = outputGroups.SelectMany(group => group.LayerSummaries.Select(layer => new
                {
                    name = layer.Name,
                    features = layer.FeatureCount,
                    category = layer.Category,
                    sourceFile = layer.SourceFile,
                    output = group.OutputName,
                    levelName = group.Level?.Name
                })).ToArray(),
                polygons = totalPolygons,
                lines = totalLines,
                points = totalPoints,
                sourceEpsg,
                sourceFile = importPaths[0],
                sourceFiles = importPaths,
                summary,
                warnings
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static ImportAssignment[] GetImportAssignments(JObject? payload, out bool hasExplicitAssignments)
    {
        hasExplicitAssignments = payload?["fileAssignments"] is JArray { Count: > 0 };
        if (hasExplicitAssignments && payload?["fileAssignments"] is JArray assignmentArray)
        {
            List<ImportAssignment> assignments = new();
            foreach (JToken token in assignmentArray)
            {
                if (token is not JObject assignmentObject)
                {
                    continue;
                }

                string? path = assignmentObject.Value<string>("path");
                string? trimmedPath = path?.Trim();
                if (string.IsNullOrWhiteSpace(trimmedPath))
                {
                    continue;
                }

                long? levelId = assignmentObject.Value<long?>("levelId");
                string category = NormalizeCategory(assignmentObject.Value<string>("category"), trimmedPath!);
                assignments.Add(new ImportAssignment(trimmedPath!, levelId, category));
            }

            return assignments
                .GroupBy(assignment => assignment.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        return GetImportPaths(payload)
            .Select(path => new ImportAssignment(path, levelId: null, category: InferGisCategory(path)))
            .ToArray();
    }

    private static IReadOnlyDictionary<string, DxfLayerColor> ResolveCategoryColors(JObject? payload)
    {
        Dictionary<string, DxfLayerColor> colors = new Dictionary<string, DxfLayerColor>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in DefaultCategoryColorHex)
        {
            if (DxfLayerColor.TryParseHex(pair.Value, out DxfLayerColor? color) && color is not null)
            {
                colors[pair.Key] = color;
            }
        }

        if (payload?["categoryColors"] is not JArray categoryColorArray)
        {
            return colors;
        }

        foreach (JToken token in categoryColorArray)
        {
            if (token is not JObject colorObject)
            {
                continue;
            }

            string category = NormalizeCategory(colorObject.Value<string>("category"), fallbackPath: null);
            string? hex = colorObject.Value<string>("color");
            if (DxfLayerColor.TryParseHex(hex, out DxfLayerColor? color) && color is not null)
            {
                colors[category] = color;
            }
        }

        return colors;
    }

    private static string NormalizeCategory(string? category, string? fallbackPath)
    {
        string normalized = (category ?? string.Empty).Trim().ToLowerInvariant();
        foreach (string knownCategory in CategoryPriority)
        {
            if (string.Equals(normalized, knownCategory, StringComparison.Ordinal))
            {
                return knownCategory;
            }
        }

        return string.IsNullOrWhiteSpace(fallbackPath)
            ? DetailCategory
            : InferGisCategory(fallbackPath!);
    }

    internal static string InferGisCategory(string path)
    {
        string stem = Path.GetFileNameWithoutExtension(path) ?? path;
        foreach (string category in CategoryPriority)
        {
            if (stem.IndexOf(category, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return category;
            }
        }

        return DetailCategory;
    }

    private static string[] GetImportPaths(JObject? payload)
    {
        if (payload is null)
        {
            return Array.Empty<string>();
        }

        List<string> paths = new();
        if (payload["paths"] is JArray pathArray)
        {
            foreach (JToken token in pathArray)
            {
                AddPath(paths, token.Type == JTokenType.String ? token.ToString() : null);
            }
        }

        AddPath(paths, payload.Value<string>("path"));

        return paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildBasemapName(string? requestedName, IReadOnlyList<string> importPaths)
    {
        string? trimmed = requestedName?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed!;
        }

        if (importPaths.Count == 1)
        {
            string stem = Path.GetFileNameWithoutExtension(importPaths[0]);
            return string.IsNullOrWhiteSpace(stem) ? "GIS Basemap" : stem;
        }

        return "GIS Basemap";
    }

    private static Dictionary<string, PreparedGisDxfGroup> CreateImportGroups(
        IReadOnlyList<ImportAssignment> assignments,
        IReadOnlyDictionary<long, GisImportLevel> levelsById,
        bool hasExplicitAssignments)
    {
        Dictionary<string, int> sourceCountByGroup = assignments
            .GroupBy(assignment => assignment.GroupKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(assignment => assignment.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                StringComparer.Ordinal);

        Dictionary<string, PreparedGisDxfGroup> groups = new(StringComparer.Ordinal);
        foreach (ImportAssignment assignment in assignments)
        {
            if (groups.ContainsKey(assignment.GroupKey))
            {
                continue;
            }

            GisImportLevel? level = null;
            if (hasExplicitAssignments && assignment.LevelId.HasValue)
            {
                levelsById.TryGetValue(assignment.LevelId.Value, out level);
            }

            groups[assignment.GroupKey] = new PreparedGisDxfGroup(
                assignment.GroupKey,
                level,
                sourceCountByGroup[assignment.GroupKey]);
        }

        return groups;
    }

    private static void AssignOutputNames(IReadOnlyList<PreparedGisDxfGroup> groups, string basemapName)
    {
        HashSet<string> usedNames = new(StringComparer.OrdinalIgnoreCase);
        bool appendLevelName = groups.Count > 1;
        foreach (PreparedGisDxfGroup group in groups)
        {
            string preferred = appendLevelName
                ? basemapName + " - " + (group.Level?.Name ?? "Origin")
                : basemapName;
            group.OutputName = ReserveUniqueName(preferred, usedNames);
        }
    }

    private static string ReserveUniqueName(string preferredName, ISet<string> usedNames)
    {
        string baseName = string.IsNullOrWhiteSpace(preferredName) ? "GIS Basemap" : preferredName.Trim();
        if (usedNames.Add(baseName))
        {
            return baseName;
        }

        for (int index = 2; index < 1000; index++)
        {
            string candidate = string.Format(CultureInfo.InvariantCulture, "{0} {1}", baseName, index);
            if (usedNames.Add(candidate))
            {
                return candidate;
            }
        }

        string fallback = "GIS Basemap " + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant();
        usedNames.Add(fallback);
        return fallback;
    }

    private static void AddPath(ICollection<string> paths, string? path)
    {
        string? trimmed = path?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            paths.Add(trimmed!);
        }
    }

    private static IReadOnlyList<GisLayerGeometry> PrepareDxfLayers(
        IReadOnlyList<GisLayerGeometry> layers,
        string filePath,
        bool isSingleFile,
        string category)
    {
        return layers
            .Select(layer => new GisLayerGeometry(
                BuildDxfLayerSourceName(filePath, layer.Name, isSingleFile, category),
                layer.Geometries))
            .ToArray();
    }

    private static string BuildDxfLayerSourceName(string filePath, string layerName, bool isSingleFile, string category)
    {
        string categoryLayerName = CategoryDxfLayerName(category);
        string sourceName = BuildSourceDxfLayerName(filePath, layerName, isSingleFile);

        if (string.IsNullOrWhiteSpace(sourceName) || IsGenericGisLayerName(sourceName))
        {
            return categoryLayerName;
        }

        if (sourceName.IndexOf(category, StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return sourceName;
        }

        return categoryLayerName + "_" + sourceName;
    }

    private static string BuildSourceDxfLayerName(string filePath, string layerName, bool isSingleFile)
    {
        if (isSingleFile)
        {
            return layerName;
        }

        string fileStem = Path.GetFileNameWithoutExtension(filePath);
        if (string.IsNullOrWhiteSpace(fileStem))
        {
            fileStem = "GIS";
        }

        if (string.Equals(fileStem, layerName, StringComparison.OrdinalIgnoreCase))
        {
            return fileStem;
        }

        return fileStem + "_" + layerName;
    }

    private static string CategoryDxfLayerName(string category)
    {
        return NormalizeCategory(category, fallbackPath: null).ToUpperInvariant();
    }

    private static bool IsGenericGisLayerName(string layerName)
    {
        string normalized = layerName.Trim();
        return string.Equals(normalized, "GIS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "LAYER", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "LAYERS", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "FEATURE", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "FEATURES", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "GEOMETRY", StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalized, "GEOMETRIES", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> PrefixWarnings(string sourceFileName, IEnumerable<string> warnings)
    {
        foreach (string warning in warnings)
        {
            if (!string.IsNullOrWhiteSpace(warning))
            {
                yield return sourceFileName + ": " + warning;
            }
        }
    }

    private static PreparedRevitContext ResolvePreparedContext(Document doc)
    {
        GeoProjectInfoStorage geoStore = new();
        ModuleStateStorage moduleStateStore = new();
        ProjectLocationReader reader = new(geoStore, moduleStateStore: moduleStateStore);
        CurrentProjectStateSummary currentState = reader.Read(doc);
        RevitDocumentHandle handle = new(doc);
        var info = geoStore.Load(handle);
        CoordinateTransformer transformer = new(new CrsRegistry());
        PlateauImportReferenceResolver resolver = new(transformer, new RevitPlateauImportLocalBasisProvider(doc));
        PlateauImportReferenceContext? referenceContext = resolver.Resolve(currentState, info, PlateauImportReferenceSource.WorkingProjectBasePoint);

        IReadOnlyList<GisImportLevel> levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .ThenBy(level => level.Name, StringComparer.Ordinal)
            .Select(level => new GisImportLevel(level.Id.Value, level.Name ?? string.Empty, level.Elevation))
            .ToList();

        return new PreparedRevitContext(referenceContext, levels);
    }

    private static DxfImportOutcome ImportDxfGroups(
        Document doc,
        PlateauContextDxfImporter importer,
        IReadOnlyList<PreparedGisDxfGroup> groups,
        IProgress<JobProgress> progress,
        CancellationToken ct)
    {
        List<string> warnings = new();
        List<long> importedIds = new();

        using TransactionGroup transactionGroup = new(doc, "Link GIS Basemap");
        transactionGroup.Start();

        for (int index = 0; index < groups.Count; index++)
        {
            ct.ThrowIfCancellationRequested();
            PreparedGisDxfGroup group = groups[index];
            if (group.DxfEntityCount == 0 || string.IsNullOrWhiteSpace(group.DxfPath))
            {
                continue;
            }

            try
            {
                View? preferredView = group.Level is null ? null : ResolveFloorPlanView(doc, group.Level.Id);
                ElementId importId = importer.LinkDxf(doc, group.DxfPath!, preferredView);
                group.ImportedElementId = importId.Value;
                importedIds.Add(importId.Value);
                warnings.AddRange(TryPlaceAndTagImportedElement(doc, importId, group));
            }
            catch (Exception ex)
            {
                string pathText = string.IsNullOrWhiteSpace(group.DxfPath) ? string.Empty : " (" + group.DxfPath + ")";
                warnings.Add(group.OutputName + pathText + ": " + ex.Message);
            }

            progress.Report(new JobProgress
            {
                Phase = "importing",
                Current = index + 1,
                Total = groups.Count,
                Percent = 90 + (int)Math.Round((index + 1) / (double)Math.Max(1, groups.Count) * 10d),
                Message = string.Format(
                    CultureInfo.InvariantCulture,
                    "Linking DXF basemap {0} of {1}: {2}",
                    index + 1,
                    groups.Count,
                    group.OutputName)
            });
        }

        if (importedIds.Count == 0)
        {
            transactionGroup.RollBack();
            throw new InvalidOperationException(BuildDxfLinkFailureMessage(warnings));
        }

        transactionGroup.Assimilate();
        return new DxfImportOutcome(importedIds, warnings);
    }

    private static string BuildDxfLinkFailureMessage(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return "None of the GIS DXF basemaps could be linked. Revit did not return a detailed failure reason.";
        }

        string detail = string.Join(" | ", warnings.Take(8));
        if (warnings.Count > 8)
        {
            detail += string.Format(CultureInfo.InvariantCulture, " | +{0} more", warnings.Count - 8);
        }

        return "None of the GIS DXF basemaps could be linked. Details: " + detail;
    }

    private static View? ResolveFloorPlanView(Document doc, long levelId)
    {
        return new FilteredElementCollector(doc)
            .OfClass(typeof(ViewPlan))
            .Cast<ViewPlan>()
            .Where(view => !view.IsTemplate && view.ViewType == ViewType.FloorPlan)
            .Where(view => view.GenLevel is not null && view.GenLevel.Id.Value == levelId)
            .OrderBy(view => view.Name, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static IReadOnlyList<string> TryPlaceAndTagImportedElement(
        Document doc,
        ElementId importId,
        PreparedGisDxfGroup group)
    {
        List<string> warnings = new();
        using Transaction transaction = new(doc, "Place GIS Basemap");
        transaction.Start();
        try
        {
            Element? element = doc.GetElement(importId);
            if (element is not null && group.Level is not null)
            {
                double currentZFeet = 0d;
                if (element is ImportInstance importInstance)
                {
                    currentZFeet = importInstance.GetTransform().Origin.Z;
                }
                else if (element.Location is LocationPoint locationPoint)
                {
                    currentZFeet = locationPoint.Point.Z;
                }

                double deltaZFeet = group.Level.ElevationFeet - currentZFeet;
                if (Math.Abs(deltaZFeet) > 1e-7)
                {
                    ElementTransformUtils.MoveElement(doc, importId, new XYZ(0d, 0d, deltaZFeet));
                }
            }

            Parameter? comments = element?.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            if (comments is not null && !comments.IsReadOnly)
            {
                comments.Set(BuildImportComment(group));
            }

            transaction.Commit();
        }
        catch (Exception ex)
        {
            if (transaction.GetStatus() == TransactionStatus.Started)
            {
                transaction.RollBack();
            }

            warnings.Add(group.OutputName + ": linked but could not set placement/comments: " + ex.Message);
        }

        return warnings;
    }

    private static string BuildImportComment(PreparedGisDxfGroup group)
    {
        string levelText = group.Level is null ? "Origin" : group.Level.Name;
        string[] sourceNames = group.SourcePaths
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(8)
            .Select(name => name!)
            .ToArray();
        string sourceText = string.Join(", ", sourceNames);
        if (group.SourcePaths.Count > sourceNames.Length)
        {
            sourceText += string.Format(CultureInfo.InvariantCulture, " (+{0} more)", group.SourcePaths.Count - sourceNames.Length);
        }

        return string.Format(
            CultureInfo.InvariantCulture,
            "GIS basemap: {0}; Level: {1}; Sources: {2}",
            group.OutputName,
            levelText,
            sourceText);
    }

    private static string SanitizeFileName(string value)
    {
        value = string.IsNullOrWhiteSpace(value) ? "GIS Basemap" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalid, '_');
        }

        return string.IsNullOrWhiteSpace(value) ? "GIS Basemap" : value;
    }

    private static string ReserveUniqueDxfPath(string outputFolder, string outputName, ISet<string> usedPaths)
    {
        string safeName = SanitizeFileName(outputName);
        string firstPath = Path.Combine(outputFolder, safeName + ".dxf");
        if (TryReserveDxfPath(firstPath, usedPaths))
        {
            return firstPath;
        }

        for (int index = 2; index < 1000; index++)
        {
            string candidate = Path.Combine(
                outputFolder,
                string.Format(CultureInfo.InvariantCulture, "{0} ({1}).dxf", safeName, index));
            if (TryReserveDxfPath(candidate, usedPaths))
            {
                return candidate;
            }
        }

        string fallback = Path.Combine(outputFolder, safeName + " " + Guid.NewGuid().ToString("N") + ".dxf");
        usedPaths.Add(fallback);
        return fallback;
    }

    private static bool TryReserveDxfPath(string path, ISet<string> usedPaths)
    {
        if (File.Exists(path))
        {
            return false;
        }

        return usedPaths.Add(path);
    }

    private sealed class PreparedRevitContext
    {
        public PreparedRevitContext(
            PlateauImportReferenceContext? referenceContext,
            IReadOnlyList<GisImportLevel> levels)
        {
            ReferenceContext = referenceContext;
            Levels = levels;
        }

        public PlateauImportReferenceContext? ReferenceContext { get; }

        public IReadOnlyList<GisImportLevel> Levels { get; }
    }

    private sealed class GisImportLevel
    {
        public GisImportLevel(long id, string name, double elevationFeet)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id.ToString(CultureInfo.InvariantCulture) : name;
            ElevationFeet = elevationFeet;
        }

        public long Id { get; }

        public string Name { get; }

        public double ElevationFeet { get; }
    }

    private sealed class ImportAssignment
    {
        public ImportAssignment(string path, long? levelId, string category)
        {
            Path = path;
            LevelId = levelId;
            Category = NormalizeCategory(category, path);
            GroupKey = levelId.HasValue
                ? levelId.Value.ToString(CultureInfo.InvariantCulture)
                : "origin";
        }

        public string Path { get; }

        public long? LevelId { get; }

        public string Category { get; }

        public string GroupKey { get; }
    }

    private sealed class PreparedGisDxfGroup
    {
        private readonly HashSet<string> sourcePathSet = new(StringComparer.OrdinalIgnoreCase);

        public PreparedGisDxfGroup(string key, GisImportLevel? level, int sourceFileCount)
        {
            Key = key;
            Level = level;
            SourceFileCount = sourceFileCount;
        }

        public string Key { get; }

        public GisImportLevel? Level { get; }

        public int SourceFileCount { get; }

        public string OutputName { get; set; } = "GIS Basemap";

        public string? DxfPath { get; set; }

        public List<string> SourcePaths { get; } = new();

        public List<PlateauContextOutlinesDxfWriter.OutlineFeature> Outlines { get; } = new();

        public List<PlateauContextOutlinesDxfWriter.LineFeature> Lines { get; } = new();

        public List<GisLayerImportSummary> LayerSummaries { get; } = new();

        public HashSet<string> UsedDxfLayerNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int PolygonCount { get; set; }

        public int LineCount { get; set; }

        public int PointCount { get; set; }

        public int DxfEntityCount { get; set; }

        public long? ImportedElementId { get; set; }

        public int FeatureCount => LayerSummaries.Sum(layer => layer.FeatureCount);

        public bool IsEmpty => Outlines.Count == 0 && Lines.Count == 0;

        public void AddSource(string path)
        {
            if (sourcePathSet.Add(path))
            {
                SourcePaths.Add(path);
            }
        }
    }

    private sealed class DxfImportOutcome
    {
        public DxfImportOutcome(IReadOnlyList<long> importedIds, IReadOnlyList<string> warnings)
        {
            ImportedIds = importedIds;
            Warnings = warnings;
        }

        public IReadOnlyList<long> ImportedIds { get; }

        public IReadOnlyList<string> Warnings { get; }
    }

    private sealed class GisLayerImportSummary
    {
        public GisLayerImportSummary(string sourceFile, string name, int featureCount, string category)
        {
            SourceFile = sourceFile;
            Name = name;
            FeatureCount = featureCount;
            Category = category;
        }

        public string SourceFile { get; }

        public string Name { get; }

        public int FeatureCount { get; }

        public string Category { get; }
    }
}
