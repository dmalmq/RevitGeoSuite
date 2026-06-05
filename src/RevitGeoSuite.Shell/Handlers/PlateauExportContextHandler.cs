using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Newtonsoft.Json.Linq;
using System.Text.RegularExpressions;
using RevitGeoSuite.Core.Coordinates;
using RevitGeoSuite.Core.ProjectMetadata;
using RevitGeoSuite.PlateauImport;
using RevitGeoSuite.RevitInterop.GeoPlacement;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.Shell.Handlers;

internal static class PlateauExportContextSupport
{
    private static readonly Regex KibanMeshCodeRegex = new Regex(@"(?<!\d)(\d{6})(?!\d)", RegexOptions.Compiled);

    private static readonly string[] DefaultKibanLayers =
    {
        PlateauContextOutlinesDxfWriter.GsiSidewalksLayer,
        PlateauContextOutlinesDxfWriter.GsiRailwaysLayer,
        KibanGmlParser.WaterLayer,
        KibanGmlParser.LandUseLayer
    };

    public static PlateauContextExportPayload ReadPayload(JObject? payload)
    {
        string folderPath = payload?.Value<string>("folderPath")
            ?? payload?.Value<string>("path")
            ?? string.Empty;
        string outputFolder = payload?.Value<string>("outputFolder") ?? string.Empty;
        string kibanFolderPath = payload?.Value<string>("kibanFolderPath") ?? string.Empty;
        JObject? options = payload?["options"] as JObject;

        return new PlateauContextExportPayload(
            folderPath,
            outputFolder,
            kibanFolderPath,
            ReadStringArray(payload, "selectedTileIds", "tileIds"),
            ReadStringArray(payload, "selectedFeatureTypes", "featureTypes"),
            ReadStringArray(payload, "selectedKibanLayers", "kibanLayers"),
            ReadStringArray(payload, "additionalGreenLandUseTokens", "greenLandUseTokens"),
            new PlateauContextWebExportOptions(
                wantShapefile: IsFormatEnabled(options, "shapefile", defaultValue: true),
                wantDxf: IsFormatEnabled(options, "dxf", defaultValue: true),
                includePlateauContext: options?.Value<bool?>("includeContext") ?? true,
                includeKibanData: options?.Value<bool?>("includeKiban") ?? true,
                includeRevitModel: options?.Value<bool?>("includeRevitModel") ?? true));
    }

    /// <summary>
    /// Reads everything that needs the Revit <see cref="Document"/> (project state, the resolved
    /// reference context, optional model footprints) into a plain, Revit-free bundle. This is the
    /// only part of the export that must run on the Revit API thread; the heavy folder/Kiban scan
    /// and geometry build run off-thread in <see cref="BuildExecutionContext"/>.
    /// </summary>
    public static PlateauExportRevitInputs ReadRevitInputs(
        Document document,
        PlateauContextExportPayload payload,
        bool needRevitFootprints)
    {
        var transformer = new CoordinateTransformer(new CrsRegistry());
        (var _, CurrentProjectStateSummary currentState, GeoProjectInfo? info) = ExportHandlerSupport.ReadContext(document);
        var resolver = new PlateauImportReferenceResolver(transformer, new RevitPlateauImportLocalBasisProvider(document));
        PlateauImportReferenceContext referenceContext = resolver.Resolve(
            currentState,
            info,
            PlateauImportReferenceSource.WorkingProjectBasePoint)
            ?? throw new InvalidOperationException(
                "PLATEAU Context export needs a saved CRS and Project Base Point reference. Complete Georeference Setup before exporting.");

        IReadOnlyList<RevitModelFootprintFeature> revitFootprints = Array.Empty<RevitModelFootprintFeature>();
        List<string> revitWarnings = new List<string>();
        if (needRevitFootprints && payload.Options.IncludeRevitModel)
        {
            try
            {
                revitFootprints = new RevitModelFootprintExportService().ExtractFootprints(document, referenceContext, revitWarnings);
            }
            catch (Exception ex)
            {
                revitWarnings.Add($"Revit footprint extraction failed: {ex.Message}");
            }
        }

        return new PlateauExportRevitInputs(currentState, referenceContext, revitFootprints, revitWarnings, currentState.DocumentTitle);
    }

    /// <summary>
    /// Builds the export execution context from the (already read) Revit inputs. Contains only
    /// CPU/IO work (PLATEAU folder scan, Kiban folder scan, request/pipeline assembly) and holds no
    /// live Revit references, so callers run it on a background job thread to keep the UI responsive.
    /// </summary>
    public static PlateauContextExportExecutionContext BuildExecutionContext(
        PlateauContextExportPayload payload,
        PlateauExportRevitInputs revitInputs,
        Action<PlateauExportProgress>? progress = null)
    {
        if (revitInputs is null)
        {
            throw new ArgumentNullException(nameof(revitInputs));
        }

        if (string.IsNullOrWhiteSpace(payload.FolderPath))
        {
            throw new InvalidOperationException("PLATEAU folder path is required.");
        }

        if (payload.SelectedTileIds.Count == 0)
        {
            throw new InvalidOperationException("At least one PLATEAU tile must be selected.");
        }

        if (!payload.Options.WantShapefile && !payload.Options.WantDxf)
        {
            throw new InvalidOperationException("Select at least one PLATEAU context export format: Shapefile or DXF.");
        }

        var transformer = new CoordinateTransformer(new CrsRegistry());

        progress?.Invoke(new PlateauExportProgress("Scanning PLATEAU folder"));
        PlateauFolderScanResult scanResult = new PlateauFolderScanService(new CityGmlParser()).ScanFolder(payload.FolderPath);

        PlateauFeatureType[] selectedFeatureTypes = ResolveFeatureTypes(payload.SelectedFeatureTypes, scanResult);
        if (selectedFeatureTypes.Length == 0)
        {
            throw new InvalidOperationException("No PLATEAU feature types are selected or available in the source folder.");
        }

        string[] selectedKibanLayers = ResolveKibanLayers(payload);
        IReadOnlyList<KibanParsedFeature>? kibanLines = null;
        IReadOnlyList<KibanParsedPolygonFeature>? kibanPolygons = null;
        bool hasKibanLayerOptions = payload.SelectedKibanLayers.Count > 0;
        if (payload.Options.IncludeKibanData && !string.IsNullOrWhiteSpace(payload.KibanFolderPath))
        {
            progress?.Invoke(new PlateauExportProgress("Scanning GSI Kiban folder"));
            KibanScanResult kibanScan = ScanKibanFolder(new KibanScanRequest(
                payload.KibanFolderPath,
                BuildSecondaryMeshCodes(payload.SelectedTileIds),
                payload.AdditionalGreenLandUseTokens));
            kibanLines = kibanScan.Features;
            kibanPolygons = kibanScan.PolygonFeatures;
        }

        var request = new ShapefileExportRequest(
            scanResult,
            revitInputs.ReferenceContext,
            selectedFeatureTypes,
            payload.SelectedTileIds,
            payload.Options.IncludeKibanData ? payload.KibanFolderPath : string.Empty,
            kibanLines,
            kibanPolygons,
            selectedKibanLayers,
            hasKibanLayerOptions,
            payload.AdditionalGreenLandUseTokens);

        var pipeline = new PlateauContextExportPipeline(
            new ContextGeometryBuilder(),
            transformer,
            revitInputs.CurrentState,
            ScanKibanFolder);

        return new PlateauContextExportExecutionContext(
            request,
            pipeline,
            payload.Options,
            revitInputs.RevitFootprints,
            revitInputs.RevitWarnings,
            revitInputs.DocumentTitle);
    }

    public static PlateauContextExportPreviewResponse BuildPreviewResult(
        PlateauOutlineDxfExportPackage package,
        PlateauContextWebExportOptions options,
        IEnumerable<string> warnings)
    {
        Dictionary<string, int> layerCounts = BuildPerLayerCounts(package, options);
        int featureCount = layerCounts.Values.Sum();

        return new PlateauContextExportPreviewResponse
        {
            FeatureCount = featureCount,
            PerLayerCounts = layerCounts,
            Crs = package.ProjectCrs.EpsgCode > 0
                ? string.Format(CultureInfo.InvariantCulture, "EPSG:{0}", package.ProjectCrs.EpsgCode)
                : package.ProjectCrs.NameSnapshot,
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public static PlateauContextExportResponse BuildExportResult(PlateauContextExportResult result, IReadOnlyList<string> revitWarnings)
    {
        List<string> files = new List<string>();
        if (result.ShapefileResult is not null)
        {
            files.AddRange(result.ShapefileResult.Files);
        }

        if (result.DxfResult is not null)
        {
            files.AddRange(result.DxfResult.Files);
        }

        List<string> warnings = new List<string>();
        warnings.AddRange(result.OutlineWarnings);
        if (result.ShapefileResult is not null)
        {
            warnings.AddRange(result.ShapefileResult.Warnings);
        }

        if (result.DxfResult is not null)
        {
            warnings.AddRange(result.DxfResult.Warnings);
        }

        warnings.AddRange(revitWarnings);

        int exportedFeatures = result.ShapefileResult?.FeatureCount
            ?? ((result.DxfResult?.PolylineCount ?? 0) + (result.DxfResult?.AreaFillCount ?? 0));

        return new PlateauContextExportResponse
        {
            Files = files.Count,
            FilePaths = files.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            ExportedFeatures = exportedFeatures,
            ExportedElements = exportedFeatures,
            Summary = result.IsEmpty
                ? "No PLATEAU context features were available to export."
                : string.Format(CultureInfo.InvariantCulture, "Exported {0} PLATEAU context feature(s) to {1} file set(s).", exportedFeatures, files.Count),
            Warnings = warnings.Distinct(StringComparer.Ordinal).ToArray()
        };
    }

    public static string BuildOutputBaseName(string outputFolder)
    {
        if (string.IsNullOrWhiteSpace(outputFolder))
        {
            throw new InvalidOperationException("Output folder is required.");
        }

        Directory.CreateDirectory(outputFolder);
        return Path.Combine(outputFolder, "plateau-context");
    }

    private static string[] ResolveKibanLayers(PlateauContextExportPayload payload)
    {
        if (!payload.Options.IncludeKibanData || string.IsNullOrWhiteSpace(payload.KibanFolderPath))
        {
            return Array.Empty<string>();
        }

        return payload.SelectedKibanLayers.Count > 0
            ? payload.SelectedKibanLayers
                .Where(layer => !string.IsNullOrWhiteSpace(layer))
                .Distinct(StringComparer.Ordinal)
                .ToArray()
            : DefaultKibanLayers.ToArray();
    }

    private static PlateauFeatureType[] ResolveFeatureTypes(
        IReadOnlyList<string> requestedTypes,
        PlateauFolderScanResult scanResult)
    {
        if (requestedTypes.Count == 0)
        {
            return scanResult.CityModels
                .SelectMany(model => model.Features)
                .Select(feature => feature.FeatureType)
                .Distinct()
                .ToArray();
        }

        return requestedTypes
            .Select(ParseFeatureType)
            .Where(type => type.HasValue)
            .Select(type => type!.Value)
            .Distinct()
            .ToArray();
    }

    private static PlateauFeatureType? ParseFeatureType(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .Replace("-", string.Empty);

        foreach (PlateauFeatureType candidate in Enum.GetValues(typeof(PlateauFeatureType)))
        {
            string enumName = candidate.ToString();
            string pluralName = candidate.GetPluralDisplayName().Replace(" ", string.Empty);
            if (string.Equals(normalized, enumName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(normalized, pluralName, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Dictionary<string, int> BuildPerLayerCounts(
        PlateauOutlineDxfExportPackage package,
        PlateauContextWebExportOptions options)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (options.IncludePlateauContext)
        {
            AddCounts(counts, package.Features.Select(feature => feature.Layer));
            AddCounts(counts, package.RoadAreas.Select(feature => feature.Layer));
            AddCounts(counts, package.KibanFeatures.Select(feature => feature.Layer));
        }

        if (options.IncludeKibanData)
        {
            AddCounts(counts, package.KibanLineFeatures.Select(feature => feature.Layer));
            AddCounts(counts, package.KibanPolygonFeatures.Select(feature => feature.Layer));
        }

        if (options.IncludeRevitModel)
        {
            AddCounts(counts, package.RevitModelFeatures.Select(feature => feature.Layer));
        }

        return counts;
    }

    private static void AddCounts(Dictionary<string, int> counts, IEnumerable<string> layers)
    {
        foreach (string layer in layers)
        {
            if (counts.ContainsKey(layer))
            {
                counts[layer]++;
            }
            else
            {
                counts[layer] = 1;
            }
        }
    }

    private static KibanScanResult ScanKibanFolder(KibanScanRequest request)
    {
        HashSet<string>? plateauSecondaryMeshCodes = request.PlateauSecondaryMeshCodes.Count == 0
            ? null
            : new HashSet<string>(request.PlateauSecondaryMeshCodes, StringComparer.Ordinal);

        string[] xmlFiles = Directory
            .EnumerateFiles(request.FolderPath, "FG-GML-*.xml", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        List<string> relevantFiles = new List<string>(xmlFiles.Length);
        int skippedCount = 0;
        foreach (string filePath in xmlFiles)
        {
            if (plateauSecondaryMeshCodes is not null)
            {
                string? fileMeshCode = ExtractKibanMeshCode(filePath);
                if (fileMeshCode is null || !plateauSecondaryMeshCodes.Contains(fileMeshCode))
                {
                    skippedCount++;
                    continue;
                }
            }

            relevantFiles.Add(filePath);
        }

        var parser = new KibanGmlParser();
        List<KibanParsedFeature> allFeatures = new List<KibanParsedFeature>();
        List<KibanParsedPolygonFeature> allPolygonFeatures = new List<KibanParsedPolygonFeature>();
        int parsedFileCount = 0;
        foreach (string filePath in relevantFiles)
        {
            try
            {
                KibanParseResult parseResult = parser.ParseFile(filePath, request.AdditionalGreenLandUseTokens);
                allFeatures.AddRange(parseResult.Lines);
                allPolygonFeatures.AddRange(parseResult.Polygons);
                parsedFileCount++;
            }
            catch
            {
            }
        }

        return new KibanScanResult(allFeatures, allPolygonFeatures, parsedFileCount, skippedCount);
    }

    private static IReadOnlyCollection<string> BuildSecondaryMeshCodes(IEnumerable<string> tileIds)
    {
        return tileIds
            .Where(tileId => !string.IsNullOrWhiteSpace(tileId))
            .Select(tileId => tileId.Trim())
            .Where(tileId => tileId.Length >= 6)
            .Select(tileId => tileId.Substring(0, 6))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string? ExtractKibanMeshCode(string filePath)
    {
        string fileName = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
        Match match = KibanMeshCodeRegex.Match(fileName);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static IReadOnlyList<string> ReadStringArray(JObject? payload, params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (payload?[propertyName] is JArray array)
            {
                return array
                    .Select(token => token.ToString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
            }
        }

        return Array.Empty<string>();
    }

    private static bool IsFormatEnabled(JObject? options, string formatName, bool defaultValue)
    {
        JToken? formats = options?["formats"];
        if (formats is JArray array)
        {
            return array.Any(token => string.Equals(token.ToString(), formatName, StringComparison.OrdinalIgnoreCase)
                || (string.Equals(formatName, "shapefile", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(token.ToString(), "shp", StringComparison.OrdinalIgnoreCase)));
        }

        if (formats is JObject objectFormats)
        {
            if (objectFormats.Value<bool?>(formatName).HasValue)
            {
                return objectFormats.Value<bool>(formatName);
            }

            if (string.Equals(formatName, "shapefile", StringComparison.OrdinalIgnoreCase)
                && objectFormats.Value<bool?>("shp").HasValue)
            {
                return objectFormats.Value<bool>("shp");
            }
        }

        return options?.Value<bool?>(formatName)
            ?? (string.Equals(formatName, "shapefile", StringComparison.OrdinalIgnoreCase)
                ? options?.Value<bool?>("shp")
                : null)
            ?? defaultValue;
    }
}

/// <summary>
/// <c>plateau.exportContext.prepare</c> — builds the context package for preview. Runs as a
/// background job so the heavy folder/Kiban scan and geometry build never block Revit's UI thread
/// (which also drives the WebView2 shell); only the Revit reads are marshalled to the API thread.
/// </summary>
public sealed class PlateauExportContextPrepareHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public PlateauExportContextPrepareHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.exportContext.prepare";

    public Task<object?> HandleAsync(object? payload)
    {
        PlateauContextExportPayload request = PlateauExportContextSupport.ReadPayload(payload as JObject);

        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "preparing", Percent = 5, Message = "Preparing preview…" });
            ct.ThrowIfCancellationRequested();

            PlateauExportRevitInputs inputs = await RevitContext.Instance.InvokeWithDocumentAsync(document =>
                PlateauExportContextSupport.ReadRevitInputs(document, request, needRevitFootprints: true)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            PlateauContextExportExecutionContext context = PlateauExportContextSupport.BuildExecutionContext(
                request,
                inputs,
                exportProgress => ReportProgress(progress, exportProgress));
            ct.ThrowIfCancellationRequested();

            PlateauOutlineDxfExportPackage package = context.Pipeline.BuildOutlineDxfExportPackage(
                context.Request,
                acceptedLandUseClassNames: null,
                context.RevitFootprints,
                exportProgress => ReportProgress(progress, exportProgress),
                out IReadOnlyList<string> outlineWarnings);

            List<string> warnings = new List<string>(context.Request.ScanResult.WarningMessages);
            warnings.AddRange(outlineWarnings);
            warnings.AddRange(context.RevitWarnings);

            return PlateauExportContextSupport.BuildPreviewResult(package, context.Options, warnings);
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static void ReportProgress(IProgress<JobProgress> progress, PlateauExportProgress exportProgress)
    {
        progress.Report(new JobProgress
        {
            Phase = "preparing",
            Percent = 50,
            Message = string.IsNullOrWhiteSpace(exportProgress.Detail)
                ? exportProgress.Stage
                : exportProgress.Stage + ": " + exportProgress.Detail
        });
    }
}

/// <summary><c>plateau.exportContext</c> — runs the native PLATEAU context export as a job.</summary>
public sealed class PlateauExportContextHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public PlateauExportContextHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "plateau.exportContext";

    public Task<object?> HandleAsync(object? payload)
    {
        PlateauContextExportPayload request = PlateauExportContextSupport.ReadPayload(payload as JObject);

        string jobId = jobs.Start(async (ct, progress) =>
        {
            progress.Report(new JobProgress { Phase = "preparing", Percent = 5, Message = "Preparing PLATEAU context export…" });
            ct.ThrowIfCancellationRequested();

            string outputBaseName = PlateauExportContextSupport.BuildOutputBaseName(request.OutputFolder);

            // Only the Revit reads run on the API thread; the heavy scan/geometry/write work below
            // runs on this background job thread so the WebView2 UI stays responsive and progress paints.
            PlateauExportRevitInputs inputs = await RevitContext.Instance.InvokeWithDocumentAsync(document =>
                PlateauExportContextSupport.ReadRevitInputs(document, request, needRevitFootprints: true)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            PlateauContextExportExecutionContext context = PlateauExportContextSupport.BuildExecutionContext(
                request,
                inputs,
                exportProgress => ReportProgress(progress, exportProgress));
            ct.ThrowIfCancellationRequested();

            PlateauContextExportResult exportResult = context.Pipeline.Run(
                outputBaseName,
                context.Request,
                context.Options.WantShapefile,
                context.Options.WantDxf,
                context.Options.IncludePlateauContext,
                context.Options.IncludeKibanData,
                context.Options.IncludeRevitModel,
                context.RevitFootprints,
                acceptedLandUseClassNames: null,
                exportProgress => ReportProgress(progress, exportProgress));

            object? result = PlateauExportContextSupport.BuildExportResult(exportResult, context.RevitWarnings);

            ct.ThrowIfCancellationRequested();
            progress.Report(new JobProgress { Phase = "completed", Percent = 100, Message = "Export complete" });
            return result;
        });

        return Task.FromResult<object?>(new JobStarted { JobId = jobId });
    }

    private static void ReportProgress(IProgress<JobProgress> progress, PlateauExportProgress exportProgress)
    {
        progress.Report(new JobProgress
        {
            Phase = "exporting",
            Percent = 50,
            Message = string.IsNullOrWhiteSpace(exportProgress.Detail)
                ? exportProgress.Stage
                : exportProgress.Stage + ": " + exportProgress.Detail
        });
    }
}

internal sealed class PlateauContextExportPayload
{
    public PlateauContextExportPayload(
        string folderPath,
        string outputFolder,
        string kibanFolderPath,
        IReadOnlyList<string> selectedTileIds,
        IReadOnlyList<string> selectedFeatureTypes,
        IReadOnlyList<string> selectedKibanLayers,
        IReadOnlyList<string> additionalGreenLandUseTokens,
        PlateauContextWebExportOptions options)
    {
        FolderPath = folderPath ?? string.Empty;
        OutputFolder = outputFolder ?? string.Empty;
        KibanFolderPath = kibanFolderPath ?? string.Empty;
        SelectedTileIds = selectedTileIds ?? Array.Empty<string>();
        SelectedFeatureTypes = selectedFeatureTypes ?? Array.Empty<string>();
        SelectedKibanLayers = selectedKibanLayers ?? Array.Empty<string>();
        AdditionalGreenLandUseTokens = additionalGreenLandUseTokens ?? Array.Empty<string>();
        Options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string FolderPath { get; }

    public string OutputFolder { get; }

    public string KibanFolderPath { get; }

    public IReadOnlyList<string> SelectedTileIds { get; }

    public IReadOnlyList<string> SelectedFeatureTypes { get; }

    public IReadOnlyList<string> SelectedKibanLayers { get; }

    public IReadOnlyList<string> AdditionalGreenLandUseTokens { get; }

    public PlateauContextWebExportOptions Options { get; }
}

internal sealed class PlateauContextWebExportOptions
{
    public PlateauContextWebExportOptions(
        bool wantShapefile,
        bool wantDxf,
        bool includePlateauContext,
        bool includeKibanData,
        bool includeRevitModel)
    {
        WantShapefile = wantShapefile;
        WantDxf = wantDxf;
        IncludePlateauContext = includePlateauContext;
        IncludeKibanData = includeKibanData;
        IncludeRevitModel = includeRevitModel;
    }

    public bool WantShapefile { get; }

    public bool WantDxf { get; }

    public bool IncludePlateauContext { get; }

    public bool IncludeKibanData { get; }

    public bool IncludeRevitModel { get; }
}

/// <summary>
/// Revit-free snapshot of everything the export needs from the active <see cref="Document"/>,
/// read on the Revit API thread and then consumed on a background job thread.
/// </summary>
internal sealed class PlateauExportRevitInputs
{
    public PlateauExportRevitInputs(
        CurrentProjectStateSummary currentState,
        PlateauImportReferenceContext referenceContext,
        IReadOnlyList<RevitModelFootprintFeature> revitFootprints,
        IReadOnlyList<string> revitWarnings,
        string documentTitle)
    {
        CurrentState = currentState ?? throw new ArgumentNullException(nameof(currentState));
        ReferenceContext = referenceContext ?? throw new ArgumentNullException(nameof(referenceContext));
        RevitFootprints = revitFootprints ?? Array.Empty<RevitModelFootprintFeature>();
        RevitWarnings = revitWarnings ?? Array.Empty<string>();
        DocumentTitle = documentTitle ?? string.Empty;
    }

    public CurrentProjectStateSummary CurrentState { get; }

    public PlateauImportReferenceContext ReferenceContext { get; }

    public IReadOnlyList<RevitModelFootprintFeature> RevitFootprints { get; }

    public IReadOnlyList<string> RevitWarnings { get; }

    public string DocumentTitle { get; }
}

internal sealed class PlateauContextExportExecutionContext
{
    public PlateauContextExportExecutionContext(
        ShapefileExportRequest request,
        PlateauContextExportPipeline pipeline,
        PlateauContextWebExportOptions options,
        IReadOnlyList<RevitModelFootprintFeature> revitFootprints,
        IReadOnlyList<string> revitWarnings,
        string documentTitle)
    {
        Request = request ?? throw new ArgumentNullException(nameof(request));
        Pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        Options = options ?? throw new ArgumentNullException(nameof(options));
        RevitFootprints = revitFootprints ?? Array.Empty<RevitModelFootprintFeature>();
        RevitWarnings = revitWarnings ?? Array.Empty<string>();
        DocumentTitle = documentTitle ?? string.Empty;
    }

    public ShapefileExportRequest Request { get; }

    public PlateauContextExportPipeline Pipeline { get; }

    public PlateauContextWebExportOptions Options { get; }

    public IReadOnlyList<RevitModelFootprintFeature> RevitFootprints { get; }

    public IReadOnlyList<string> RevitWarnings { get; }

    public string DocumentTitle { get; }
}
