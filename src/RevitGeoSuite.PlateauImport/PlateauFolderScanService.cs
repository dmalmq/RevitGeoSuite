using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.Core.Plateau.Schema;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauFolderScanService
{
    private readonly CityGmlParser cityGmlParser;

    public PlateauFolderScanService(CityGmlParser? cityGmlParser = null)
    {
        this.cityGmlParser = cityGmlParser ?? new CityGmlParser();
    }

    /// <summary>
    /// Scans the PLATEAU folder and parses its CityGML features. When <paramref name="selectedTileIds"/>
    /// is non-empty, only files belonging to those tiles are parsed — parse memory then scales with the
    /// selection instead of the whole municipality, which keeps large imports from exhausting memory.
    /// A <c>null</c>/empty selection parses every supported file (the folder enumeration behaviour).
    /// </summary>
    public PlateauFolderScanResult ScanFolder(
        string folderPath,
        Action<PlateauScanProgress>? reportProgress = null,
        IReadOnlyCollection<string>? selectedTileIds = null)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("PLATEAU folder path cannot be empty.", nameof(folderPath));
        }

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"PLATEAU folder '{folderPath}' could not be found.");
        }

        ScanTarget scanTarget = ResolveScanTarget(folderPath);
        reportProgress?.Invoke(new PlateauScanProgress(PlateauScanPhase.Enumerating, 0, 0, string.Empty));
        List<PlateauCityModel> models = new List<PlateauCityModel>();
        List<string> warnings = new List<string>();
        string[] supportedFiles = FilterFilesForSelectedTiles(
            Directory
                .EnumerateFiles(scanTarget.SearchRootPath, "*.*", scanTarget.SearchOption)
                .Where(IsSupportedFile)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase),
            selectedTileIds);
        string cacheKey = PlateauScanSessionCache.BuildPlateauKey(
            folderPath,
            scanTarget.SearchRootPath,
            scanTarget.IsRecursivePackageScan,
            supportedFiles);
        if (PlateauScanSessionCache.TryGetPlateau(cacheKey, out PlateauFolderScanResult? cachedResult) && cachedResult is not null)
        {
            reportProgress?.Invoke(new PlateauScanProgress(PlateauScanPhase.Completed, supportedFiles.Length, supportedFiles.Length, string.Empty));
            return CreateCachedResult(cachedResult);
        }

        reportProgress?.Invoke(new PlateauScanProgress(PlateauScanPhase.Parsing, 0, supportedFiles.Length, string.Empty));

        IndexedParseResult[] parseResults = new IndexedParseResult[supportedFiles.Length];
        int processedFileCount = 0;
        ParallelOptions parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
        };

        Parallel.For(0, supportedFiles.Length, parallelOptions, index =>
        {
            string filePath = supportedFiles[index];
            PlateauCityModel? model = null;
            string? warning = null;
            try
            {
                model = cityGmlParser.ParseFile(filePath);
                if (model.Features.Count == 0)
                {
                    warning = $"Skipped '{Path.GetFileName(filePath)}' because no supported PLATEAU buildings, bridges, roads, vegetation, or relief features were found.";
                    model = null;
                }
            }
            catch (Exception ex)
            {
                warning = $"Skipped '{Path.GetFileName(filePath)}' because it could not be parsed: {ex.Message}";
            }
            finally
            {
                parseResults[index] = new IndexedParseResult(model, warning);
                int processed = Interlocked.Increment(ref processedFileCount);
                reportProgress?.Invoke(new PlateauScanProgress(PlateauScanPhase.Parsing, processed, supportedFiles.Length, filePath));
            }
        });

        for (int index = 0; index < parseResults.Length; index++)
        {
            IndexedParseResult parseResult = parseResults[index];
            if (parseResult.Model is not null)
            {
                models.Add(parseResult.Model);
            }

            string? parseWarning = parseResult.Warning;
            if (!string.IsNullOrWhiteSpace(parseWarning))
            {
                warnings.Add(parseWarning!);
            }
        }

        if (supportedFiles.Length == 0)
        {
            warnings.Add(scanTarget.IsRecursivePackageScan
                ? "No .gml or .xml files were found under the detected PLATEAU package root (udx)."
                : "No .gml or .xml files were found in the selected folder.");
        }

        reportProgress?.Invoke(new PlateauScanProgress(PlateauScanPhase.Completed, supportedFiles.Length, supportedFiles.Length, string.Empty));

        PlateauFolderScanResult result = new PlateauFolderScanResult
        {
            FolderPath = folderPath,
            SearchRootPath = scanTarget.SearchRootPath,
            IsRecursivePackageScan = scanTarget.IsRecursivePackageScan,
            SupportedFilePaths = supportedFiles,
            CityModels = models,
            WarningMessages = warnings
        };
        PlateauScanSessionCache.StorePlateau(cacheKey, result);
        return result;
    }

    /// <summary>
    /// Lists the selectable tiles in a PLATEAU folder <em>without parsing any geometry</em> — tile id and
    /// total file size come straight from the file names (<see cref="PlateauSchemaHelper.TryExtractTileIdFromPath"/>).
    /// Used to draw the tile grid on folder selection; parsing the whole package just to count features
    /// loads the entire municipality into memory and is what crashed large 3D imports.
    /// </summary>
    public IReadOnlyList<PlateauTileFileSummary> EnumerateTileFiles(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            throw new ArgumentException("PLATEAU folder path cannot be empty.", nameof(folderPath));
        }

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"PLATEAU folder '{folderPath}' could not be found.");
        }

        ScanTarget scanTarget = ResolveScanTarget(folderPath);
        Dictionary<string, long> sizeByTileId = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (string filePath in Directory.EnumerateFiles(scanTarget.SearchRootPath, "*.*", scanTarget.SearchOption).Where(IsSupportedFile))
        {
            string? tileId = PlateauSchemaHelper.TryExtractTileIdFromPath(filePath);
            if (string.IsNullOrWhiteSpace(tileId))
            {
                continue;
            }

            long length;
            try
            {
                length = new FileInfo(filePath).Length;
            }
            catch
            {
                length = 0L;
            }

            sizeByTileId.TryGetValue(tileId!, out long existing);
            sizeByTileId[tileId!] = existing + length;
        }

        return sizeByTileId
            .Select(pair => new PlateauTileFileSummary(pair.Key, pair.Value))
            .OrderBy(summary => summary.TileId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Keeps only the files that fall <em>within</em> the selected tiles. Japan mesh codes are
    /// hierarchical by prefix (tertiary <c>53393559</c> sits inside secondary <c>533935</c>), so a
    /// file is kept when it is the selected tile itself or a finer child of it
    /// (<c>fileTileId.StartsWith(selected)</c>). A file's coarser <em>parent</em> mesh is deliberately
    /// NOT pulled in: those parents are the municipality-wide coverage layers (relief/DEM, land use)
    /// whose secondary-mesh files run to hundreds of MB and millions of triangles — extruding them
    /// for a few 1 km tiles is what hung the 3D import. Terrain/land use have their own import modes.
    /// A <c>null</c>/empty selection keeps everything.
    /// </summary>
    private static string[] FilterFilesForSelectedTiles(IEnumerable<string> files, IReadOnlyCollection<string>? selectedTileIds)
    {
        if (selectedTileIds is null || selectedTileIds.Count == 0)
        {
            return files.ToArray();
        }

        string[] selected = selectedTileIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (selected.Length == 0)
        {
            return files.ToArray();
        }

        List<string> kept = new List<string>();
        foreach (string filePath in files)
        {
            string? fileTileId = PlateauSchemaHelper.TryExtractTileIdFromPath(filePath);
            if (string.IsNullOrWhiteSpace(fileTileId))
            {
                continue;
            }

            foreach (string selectedTileId in selected)
            {
                // Keep the selected tile's own file and any finer child within it; skip coarser
                // parent-mesh coverage files (DEM/land use) that span far beyond the selection.
                if (fileTileId!.StartsWith(selectedTileId, StringComparison.Ordinal))
                {
                    kept.Add(filePath);
                    break;
                }
            }
        }

        return kept.ToArray();
    }

    private static PlateauFolderScanResult CreateCachedResult(PlateauFolderScanResult cachedResult)
    {
        return new PlateauFolderScanResult
        {
            FolderPath = cachedResult.FolderPath,
            SearchRootPath = cachedResult.SearchRootPath,
            IsRecursivePackageScan = cachedResult.IsRecursivePackageScan,
            SupportedFilePaths = cachedResult.SupportedFilePaths,
            CityModels = cachedResult.CityModels,
            WarningMessages = cachedResult.WarningMessages,
            IsFromCache = true
        };
    }

    private static ScanTarget ResolveScanTarget(string folderPath)
    {
        string normalizedPath = Path.GetFullPath(folderPath);
        string folderName = Path.GetFileName(normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.Equals(folderName, "udx", StringComparison.OrdinalIgnoreCase))
        {
            return new ScanTarget(normalizedPath, SearchOption.AllDirectories, isRecursivePackageScan: true);
        }

        string? udxFolder = Directory
            .EnumerateDirectories(normalizedPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => string.Equals(Path.GetFileName(path), "udx", StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(udxFolder))
        {
            return new ScanTarget(udxFolder, SearchOption.AllDirectories, isRecursivePackageScan: true);
        }

        return new ScanTarget(normalizedPath, SearchOption.TopDirectoryOnly, isRecursivePackageScan: false);
    }

    private static bool IsSupportedFile(string path)
    {
        string extension = Path.GetExtension(path);
        return string.Equals(extension, ".gml", StringComparison.OrdinalIgnoreCase)
            || string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class ScanTarget
    {
        public ScanTarget(string searchRootPath, SearchOption searchOption, bool isRecursivePackageScan)
        {
            SearchRootPath = searchRootPath;
            SearchOption = searchOption;
            IsRecursivePackageScan = isRecursivePackageScan;
        }

        public string SearchRootPath { get; }

        public SearchOption SearchOption { get; }

        public bool IsRecursivePackageScan { get; }
    }

    private sealed class IndexedParseResult
    {
        public IndexedParseResult(PlateauCityModel? model, string? warning)
        {
            Model = model;
            Warning = warning;
        }

        public PlateauCityModel? Model { get; }

        public string? Warning { get; }
    }
}

/// <summary>One selectable tile, summarised from file names only (no geometry parsed).</summary>
public sealed class PlateauTileFileSummary
{
    public PlateauTileFileSummary(string tileId, long fileSizeBytes)
    {
        TileId = tileId ?? throw new ArgumentNullException(nameof(tileId));
        FileSizeBytes = fileSizeBytes;
    }

    public string TileId { get; }

    public long FileSizeBytes { get; }
}
