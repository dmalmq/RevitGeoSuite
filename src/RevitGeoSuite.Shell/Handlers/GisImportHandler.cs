using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.Plateau.Tiles3D;
using RevitGeoSuite.FloorPlanExport.Core.Coordinates;
using RevitGeoSuite.FloorPlanExport.Core.Gis;
using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.RevitInterop;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.RevitInterop.Storage;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

/// <summary>Returns Revit levels used by the floor-aware local GIS DXF export UI.</summary>
public sealed class GisExportOptionsHandler : IRpcHandler
{
    public string Method => "gis.exportOptions";

    public async Task<object?> HandleAsync(object? payload)
    {
        return await RevitContext.Instance.InvokeWithDocumentAsync(BuildOptions).ConfigureAwait(false);
    }

    private static GisExportOptionsResponse BuildOptions(Document doc)
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

        return new GisExportOptionsResponse
        {
            Levels = levels.ToArray(),
            DefaultLevelId = defaultLevelId
        };
    }
}

/// <summary>
/// Exports a local Shapefile or GeoPackage to a georeferenced DXF file in the project CRS.
/// The generated DXF should be linked into Revit manually using:
/// Link CAD → Positioning: Auto - By Shared Coordinates → Import units: meter.
/// </summary>
public sealed class GisExportHandler : IRpcHandler
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

    public GisExportHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "gis.export";

    public Task<object?> HandleAsync(object? payload)
    {
        JObject? jobj = payload as JObject;
        ExportAssignment[] assignments = GetExportAssignments(jobj, out bool hasExplicitAssignments);
        IReadOnlyDictionary<string, DxfLayerColor> categoryColors = ResolveCategoryColors(jobj);
        string[] exportPaths = assignments.Select(assignment => assignment.Path).ToArray();
        string outputFolder = jobj?.Value<string>("outputFolder")?.Trim() ?? string.Empty;

        if (exportPaths.Length == 0)
        {
            return Task.FromResult<object?>(new { error = "At least one GIS file is required" });
        }

        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            return Task.FromResult<object?>(new { error = "Choose an output folder for the DXF files." });
        }

        string? unsupportedPath = exportPaths.FirstOrDefault(path => !GisFileReader.IsSupported(path));
        if (!string.IsNullOrWhiteSpace(unsupportedPath))
        {
            return Task.FromResult<object?>(new
            {
                error = $"Unsupported GIS file '{Path.GetFileName(unsupportedPath)}'. Supported GIS files are Shapefile (.shp) and GeoPackage (.gpkg)."
            });
        }

        string basemapName = BuildBasemapName(jobj?.Value<string>("basemapName"), exportPaths);
        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "preparing", Percent = 0, Message = "Resolving georeference..." });
            PreparedRevitContext prep = await RevitContext.Instance.InvokeWithDocumentAsync(ResolvePreparedContext)
                .ConfigureAwait(false);
            PlateauImportReferenceContext? referenceContext = prep.ReferenceContext;

            if (referenceContext is null)
            {
                throw new InvalidOperationException(
                    "This project isn't georeferenced yet. Complete Georeference Setup (CRS + Project Base Point) before exporting a GIS basemap.");
            }

            int projectEpsg = referenceContext.ProjectCrs.EpsgCode;
            if (projectEpsg <= 0)
            {
                throw new InvalidOperationException("The project's CRS is missing an EPSG code; GIS export needs a projected project CRS.");
            }

            ct.ThrowIfCancellationRequested();

            Dictionary<long, GisExportLevel> levelsById = prep.Levels.ToDictionary(level => level.Id);
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
            Dictionary<string, PreparedGisDxfGroup> groups = CreateExportGroups(assignments, levelsById, hasExplicitAssignments);
            HashSet<int> sourceEpsgCodes = new();
            int totalFeatures = 0;
            int totalPolygons = 0;
            int totalLines = 0;
            int totalPoints = 0;
            bool isSingleFile = assignments.Length == 1;

            for (int fileIndex = 0; fileIndex < assignments.Length; fileIndex++)
            {
                ExportAssignment assignment = assignments[fileIndex];
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
                            ?? "The selected GIS file did not contain any geometry to export.");
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
                group.LayerSummaries.AddRange(dxfLayers.Select(layer => new GisLayerExportSummary(
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

            Vector3d crsMarker = new Vector3d(
                referenceContext.AnchorProjectedCoordinate.Easting,
                referenceContext.AnchorProjectedCoordinate.Northing,
                referenceContext.AnchorElevationMeters);

            for (int outputIndex = 0; outputIndex < outputGroups.Count; outputIndex++)
            {
                PreparedGisDxfGroup group = outputGroups[outputIndex];
                progress.Report(new JobProgress
                {
                    Phase = "building",
                    Current = outputIndex + 1,
                    Total = outputGroups.Count,
                    Percent = 75 + (int)Math.Round(outputIndex / (double)Math.Max(1, outputGroups.Count) * 20d),
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
                    Array.Empty<string>(),
                    markerOverride: crsMarker);

                group.DxfEntityCount = dxfBuild.FeatureCount;
                finalWarnings.AddRange(dxfBuild.Warnings.Select(w => group.OutputName + ": " + w));
                WritePrjSidecar(group.DxfPath, referenceContext.ProjectCrs);
            }

            if (outputGroups.All(group => group.DxfEntityCount == 0))
            {
                throw new InvalidOperationException(
                    finalWarnings.FirstOrDefault()
                    ?? "The selected GIS files did not produce any DXF entities.");
            }

            warnings = finalWarnings;
            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Export complete" });

            int? sourceEpsg = sourceEpsgCodes.Count == 1 ? sourceEpsgCodes.First() : null;
            int totalDxfEntities = outputGroups.Sum(group => group.DxfEntityCount);
            int totalLayerCount = outputGroups.Sum(group => group.LayerSummaries.Count);
            string summary = string.Format(
                CultureInfo.InvariantCulture,
                "Exported {0} GIS feature(s) from {1} file(s) as {2} DXF file(s) ({3} entities, {4} layer(s)).",
                totalFeatures,
                exportPaths.Length,
                outputGroups.Count,
                totalDxfEntities,
                totalLayerCount);

            return (object?)new
            {
                mode = "gis-dxf",
                linked = false,
                outputFolder = resolvedOutputFolder,
                importedElements = totalFeatures,
                featuresImported = totalFeatures,
                dxfEntities = totalDxfEntities,
                outputs = outputGroups.Select(group => new
                {
                    name = group.OutputName,
                    levelId = group.Level?.Id,
                    levelName = group.Level?.Name,
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
                sourceFile = exportPaths[0],
                sourceFiles = exportPaths,
                summary,
                warnings
            };
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static ExportAssignment[] GetExportAssignments(JObject? payload, out bool hasExplicitAssignments)
    {
        hasExplicitAssignments = payload?["fileAssignments"] is JArray { Count: > 0 };
        if (hasExplicitAssignments && payload?["fileAssignments"] is JArray assignmentArray)
        {
            List<ExportAssignment> assignments = new();
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
                assignments.Add(new ExportAssignment(trimmedPath!, levelId, category));
            }

            return assignments
                .GroupBy(assignment => assignment.Path, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToArray();
        }

        return GetExportPaths(payload)
            .Select(path => new ExportAssignment(path, levelId: null, category: InferGisCategory(path)))
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

    private static string[] GetExportPaths(JObject? payload)
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

    private static string BuildBasemapName(string? requestedName, IReadOnlyList<string> exportPaths)
    {
        string? trimmed = requestedName?.Trim();
        if (!string.IsNullOrWhiteSpace(trimmed))
        {
            return trimmed!;
        }

        if (exportPaths.Count == 1)
        {
            string stem = Path.GetFileNameWithoutExtension(exportPaths[0]);
            return string.IsNullOrWhiteSpace(stem) ? "GIS Basemap" : stem;
        }

        return "GIS Basemap";
    }

    private static Dictionary<string, PreparedGisDxfGroup> CreateExportGroups(
        IReadOnlyList<ExportAssignment> assignments,
        IReadOnlyDictionary<long, GisExportLevel> levelsById,
        bool hasExplicitAssignments)
    {
        Dictionary<string, int> sourceCountByGroup = assignments
            .GroupBy(assignment => assignment.GroupKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Select(assignment => assignment.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                StringComparer.Ordinal);

        Dictionary<string, PreparedGisDxfGroup> groups = new(StringComparer.Ordinal);
        foreach (ExportAssignment assignment in assignments)
        {
            if (groups.ContainsKey(assignment.GroupKey))
            {
                continue;
            }

            GisExportLevel? level = null;
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

        IReadOnlyList<GisExportLevel> levels = new FilteredElementCollector(doc)
            .OfClass(typeof(Level))
            .Cast<Level>()
            .OrderBy(level => level.Elevation)
            .ThenBy(level => level.Name, StringComparer.Ordinal)
            .Select(level => new GisExportLevel(level.Id.Value, level.Name ?? string.Empty, level.Elevation))
            .ToList();

        return new PreparedRevitContext(referenceContext, levels);
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

    private static void WritePrjSidecar(string dxfPath, CrsReference projectCrs)
    {
        CrsRegistry registry = new CrsRegistry();
        if (!registry.TryGetByEpsgCode(projectCrs.EpsgCode, out CrsDefinition? definition) || definition is null)
        {
            return;
        }

        string? wkt = BuildPrjWkt(definition);
        if (string.IsNullOrWhiteSpace(wkt))
        {
            return;
        }

        try
        {
            File.WriteAllText(Path.ChangeExtension(dxfPath, ".prj"), wkt, Encoding.ASCII);
        }
        catch
        {
            // Non-critical — the DXF still works without it.
        }
    }

    private static string? BuildPrjWkt(CrsDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.Wkt))
        {
            return definition.Wkt;
        }

        (string spheroidName, double semiMajor, double inverseFlattening) = ResolveSpheroid(definition.DatumName);
        string datumEsri = definition.DatumName.Replace(" ", "_");

        if (string.Equals(definition.ProjectionMethod, "Transverse_Mercator", StringComparison.OrdinalIgnoreCase))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "PROJCS[\"{0}\",GEOGCS[\"GCS_{1}\",DATUM[\"D_{1}\",SPHEROID[\"{2}\",{3},{4}]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Transverse_Mercator\"],PARAMETER[\"False_Easting\",{5}],PARAMETER[\"False_Northing\",{6}],PARAMETER[\"Central_Meridian\",{7}],PARAMETER[\"Scale_Factor\",{8}],PARAMETER[\"Latitude_Of_Origin\",{9}],UNIT[\"Meter\",1.0],AUTHORITY[\"EPSG\",\"{10}\"]]",
                definition.Name,
                datumEsri,
                spheroidName,
                semiMajor,
                inverseFlattening,
                definition.FalseEasting,
                definition.FalseNorthing,
                definition.CentralMeridian,
                definition.ScaleFactor,
                definition.LatitudeOfOrigin,
                definition.EpsgCode);
        }

        if (string.Equals(definition.ProjectionMethod, "Lambert_Conformal_Conic_2SP", StringComparison.OrdinalIgnoreCase))
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "PROJCS[\"{0}\",GEOGCS[\"GCS_{1}\",DATUM[\"D_{1}\",SPHEROID[\"{2}\",{3},{4}]],PRIMEM[\"Greenwich\",0.0],UNIT[\"Degree\",0.0174532925199433]],PROJECTION[\"Lambert_Conformal_Conic_2SP\"],PARAMETER[\"False_Easting\",{5}],PARAMETER[\"False_Northing\",{6}],PARAMETER[\"Central_Meridian\",{7}],PARAMETER[\"Latitude_Of_Origin\",{8}],PARAMETER[\"Standard_Parallel_1\",{9}],PARAMETER[\"Standard_Parallel_2\",{10}],UNIT[\"Meter\",1.0],AUTHORITY[\"EPSG\",\"{11}\"]]",
                definition.Name,
                datumEsri,
                spheroidName,
                semiMajor,
                inverseFlattening,
                definition.FalseEasting,
                definition.FalseNorthing,
                definition.CentralMeridian,
                definition.LatitudeOfOrigin,
                definition.StandardParallel1,
                definition.StandardParallel2,
                definition.EpsgCode);
        }

        return null;
    }

    private static (string Name, double SemiMajor, double InverseFlattening) ResolveSpheroid(string datumName)
    {
        if (datumName.StartsWith("WGS", StringComparison.OrdinalIgnoreCase))
        {
            return ("WGS_1984", 6378137.0, 298.257223563);
        }

        if (datumName.StartsWith("OSGB", StringComparison.OrdinalIgnoreCase))
        {
            return ("Airy_1830", 6377563.396, 299.3249646);
        }

        return ("GRS_1980", 6378137.0, 298.257222101);
    }

    private sealed class PreparedRevitContext
    {
        public PreparedRevitContext(
            PlateauImportReferenceContext? referenceContext,
            IReadOnlyList<GisExportLevel> levels)
        {
            ReferenceContext = referenceContext;
            Levels = levels;
        }

        public PlateauImportReferenceContext? ReferenceContext { get; }

        public IReadOnlyList<GisExportLevel> Levels { get; }
    }

    private sealed class GisExportLevel
    {
        public GisExportLevel(long id, string name, double elevationFeet)
        {
            Id = id;
            Name = string.IsNullOrWhiteSpace(name) ? id.ToString(CultureInfo.InvariantCulture) : name;
            ElevationFeet = elevationFeet;
        }

        public long Id { get; }

        public string Name { get; }

        public double ElevationFeet { get; }
    }

    private sealed class ExportAssignment
    {
        public ExportAssignment(string path, long? levelId, string category)
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

        public PreparedGisDxfGroup(string key, GisExportLevel? level, int sourceFileCount)
        {
            Key = key;
            Level = level;
            SourceFileCount = sourceFileCount;
        }

        public string Key { get; }

        public GisExportLevel? Level { get; }

        public int SourceFileCount { get; }

        public string OutputName { get; set; } = "GIS Basemap";

        public string? DxfPath { get; set; }

        public List<string> SourcePaths { get; } = new();

        public List<PlateauContextOutlinesDxfWriter.OutlineFeature> Outlines { get; } = new();

        public List<PlateauContextOutlinesDxfWriter.LineFeature> Lines { get; } = new();

        public List<GisLayerExportSummary> LayerSummaries { get; } = new();

        public HashSet<string> UsedDxfLayerNames { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int PolygonCount { get; set; }

        public int LineCount { get; set; }

        public int PointCount { get; set; }

        public int DxfEntityCount { get; set; }

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

    private sealed class GisLayerExportSummary
    {
        public GisLayerExportSummary(string sourceFile, string name, int featureCount, string category)
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
