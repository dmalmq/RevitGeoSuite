using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
        reportProgress?.Invoke(new PlateauScanProgress(PlateauScanPhase.Parsing, 0, supportedFiles.Length, string.Empty));

        int processedFileCount = 0;
        foreach (string filePath in supportedFiles)
        {
            try
            {
                PlateauCityModel model = cityGmlParser.ParseFile(filePath);
                if (model.Features.Count == 0)
                {
                    warnings.Add($"Skipped '{Path.GetFileName(filePath)}' because no supported PLATEAU buildings, bridges, roads, vegetation, or relief features were found.");
                }
                else
                {
                    models.Add(model);
                }
            }
            catch (Exception ex)
            {
                warnings.Add($"Skipped '{Path.GetFileName(filePath)}' because it could not be parsed: {ex.Message}");
            }
            finally
            {
                processedFileCount++;
                reportProgress?.Invoke(new PlateauScanProgress(PlateauScanPhase.Parsing, processedFileCount, supportedFiles.Length, filePath));
            }
        }

        if (supportedFiles.Length == 0)
        {
            warnings.Add(scanTarget.IsRecursivePackageScan
                ? "No .gml or .xml files were found under the detected PLATEAU package root (udx)."
                : "No .gml or .xml files were found in the selected folder.");
        }

        reportProgress?.Invoke(new PlateauScanProgress(PlateauScanPhase.Completed, supportedFiles.Length, supportedFiles.Length, string.Empty));

        return new PlateauFolderScanResult
        {
            FolderPath = folderPath,
            SearchRootPath = scanTarget.SearchRootPath,
            IsRecursivePackageScan = scanTarget.IsRecursivePackageScan,
            SupportedFilePaths = supportedFiles,
            CityModels = models,
            WarningMessages = warnings
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
}

