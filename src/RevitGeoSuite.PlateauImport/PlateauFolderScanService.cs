using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RevitGeoSuite.PlateauImport;

public sealed class PlateauFolderScanService
{
    private readonly CityGmlParser cityGmlParser;

    public PlateauFolderScanService(CityGmlParser? cityGmlParser = null)
    {
        this.cityGmlParser = cityGmlParser ?? new CityGmlParser();
    }

    public PlateauFolderScanResult ScanFolder(string folderPath, Action<PlateauScanProgress>? reportProgress = null)
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
        string[] supportedFiles = Directory
            .EnumerateFiles(scanTarget.SearchRootPath, "*.*", scanTarget.SearchOption)
            .Where(IsSupportedFile)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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
