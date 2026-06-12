using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Diagnostics;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.FloorPlanExport.UI;

internal static class WebExportResultPayloadBuilder
{
    public static ExportResultInitialStateResponse Build(
        FloorGeoPackageExportResult result,
        string outputDirectory,
        UiLanguage language)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        string resolvedOutputDirectory = ResolveOutputDirectory(result, outputDirectory);
        int viewCount = result.ArtifactResults
            .SelectMany(artifact => artifact.ContributingViewNames)
            .Distinct(StringComparer.Ordinal)
            .Count();
        int artifactCount = result.ArtifactResults.Count;
        int warningCount = result.Warnings.Count;
        int featureCount = result.ArtifactResults.Sum(artifact => artifact.FeatureCount);
        int writtenArtifacts = result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.Written);
        int reusedArtifacts = result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.ReusedFromBaseline);
        int packageErrorCount = result.PackageValidationResult?.Issues.Count(issue => issue.Severity == PackageValidationSeverity.Error) ?? 0;
        int packageWarningCount = result.PackageValidationResult?.Issues.Count(issue => issue.Severity == PackageValidationSeverity.Warning) ?? 0;

        return new ExportResultInitialStateResponse
        {
            Language = language == UiLanguage.Japanese ? "japanese" : "english",
            Title = T(language, "Export Results", "エクスポート結果"),
            Message = warningCount > 0
                ? T(language, "GeoPackage export completed with warnings.", "警告付きでGeoPackageのエクスポートが完了しました。")
                : T(language, "GeoPackage export completed.", "GeoPackageのエクスポートが完了しました。"),
            OutputDirectory = resolvedOutputDirectory,
            CanOpenOutputDirectory = ResolveExistingDirectory(resolvedOutputDirectory) != null,
            Summary = new ExportResultSummaryPayload
            {
                ViewCount = viewCount,
                ArtifactCount = artifactCount,
                WrittenArtifactCount = writtenArtifacts,
                ReusedArtifactCount = reusedArtifacts,
                FeatureCount = featureCount,
                WarningCount = warningCount,
                PackageErrorCount = packageErrorCount,
                PackageWarningCount = packageWarningCount,
            },
            Files = result.ViewResults.Select(export => new ExportResultFilePayload
            {
                ViewName = export.ViewName,
                LevelName = export.LevelName,
                FeatureType = export.FeatureType,
                FeatureCount = export.FeatureCount,
                OutputFilePath = export.OutputFilePath,
            }).ToList(),
            Warnings = result.Warnings.ToList(),
            Changes = result.ChangeSummary == null || !result.ChangeSummary.HasChanges
                ? new List<string>()
                : result.ChangeSummary.Lines.ToList(),
            PackageLines = BuildPackageLines(result, packageErrorCount, packageWarningCount, language),
            Timings = result.PhaseTimings.Select(timing => new ExportResultTimingPayload
            {
                PhaseName = timing.PhaseName,
                DurationMilliseconds = timing.DurationMilliseconds,
                DurationText = FormatDuration(timing.DurationMilliseconds),
            }).ToList(),
        };
    }

    public static ExecutionActionResponse OpenOutputDirectory(string outputDirectory, UiLanguage language)
    {
        string? resolvedDirectory = ResolveExistingDirectory(outputDirectory);
        if (resolvedDirectory == null)
        {
            return new ExecutionActionResponse
            {
                Success = false,
                Error = T(language, "The output directory was not found.", "出力フォルダーが見つかりませんでした。"),
            };
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = resolvedDirectory,
                UseShellExecute = true,
            });
            return new ExecutionActionResponse { Success = true };
        }
        catch (Exception ex)
        {
            return new ExecutionActionResponse
            {
                Success = false,
                Error = language == UiLanguage.Japanese
                    ? $"出力フォルダーを開けませんでした。\n\n{ex.Message}"
                    : $"Unable to open the output directory.\n\n{ex.Message}",
            };
        }
    }

    public static string ResolveOutputDirectory(FloorGeoPackageExportResult result, string outputDirectory)
    {
        foreach (string candidate in EnumerateOutputDirectoryCandidates(result, outputDirectory))
        {
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        string normalizedOutputDirectory = NormalizePath(outputDirectory);
        if (normalizedOutputDirectory.Length > 0)
        {
            return normalizedOutputDirectory;
        }

        return EnumerateOutputDirectoryCandidates(result, outputDirectory).FirstOrDefault() ?? string.Empty;
    }

    private static IEnumerable<string> EnumerateOutputDirectoryCandidates(FloorGeoPackageExportResult result, string outputDirectory)
    {
        string normalizedOutputDirectory = NormalizePath(outputDirectory);
        if (normalizedOutputDirectory.Length > 0)
        {
            yield return normalizedOutputDirectory;
        }

        string packageDirectory = NormalizePath(result.PackageDirectoryPath);
        if (packageDirectory.Length > 0)
        {
            yield return packageDirectory;
        }

        foreach (string path in EnumerateFilePathCandidates(result))
        {
            string directory = GetDirectoryName(path);
            if (directory.Length > 0)
            {
                yield return directory;
            }
        }
    }

    private static IEnumerable<string> EnumerateFilePathCandidates(FloorGeoPackageExportResult result)
    {
        string packageManifestPath = NormalizePath(result.PackageManifestPath);
        if (packageManifestPath.Length > 0)
        {
            yield return packageManifestPath;
        }

        string diagnosticsReportPath = NormalizePath(result.DiagnosticsReportPath);
        if (diagnosticsReportPath.Length > 0)
        {
            yield return diagnosticsReportPath;
        }

        foreach (ExportArtifactResult artifact in result.ArtifactResults)
        {
            string outputFilePath = NormalizePath(artifact.OutputFilePath);
            if (outputFilePath.Length > 0)
            {
                yield return outputFilePath;
            }
        }
    }

    private static string? ResolveExistingDirectory(string path)
    {
        string normalizedPath = NormalizePath(path);
        if (normalizedPath.Length == 0)
        {
            return null;
        }

        if (Directory.Exists(normalizedPath))
        {
            return normalizedPath;
        }

        if (File.Exists(normalizedPath))
        {
            string directory = GetDirectoryName(normalizedPath);
            return directory.Length > 0 && Directory.Exists(directory) ? directory : null;
        }

        return null;
    }

    private static string NormalizePath(string? path)
    {
        return string.IsNullOrWhiteSpace(path) ? string.Empty : path!.Trim().Trim('"');
    }

    private static string GetDirectoryName(string path)
    {
        try
        {
            return Path.GetDirectoryName(path) ?? string.Empty;
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static List<string> BuildPackageLines(
        FloorGeoPackageExportResult result,
        int packageErrorCount,
        int packageWarningCount,
        UiLanguage language)
    {
        List<string> lines = new();
        if (!string.IsNullOrWhiteSpace(result.PackageDirectoryPath))
        {
            lines.Add($"Package directory: {result.PackageDirectoryPath}");
        }

        if (!string.IsNullOrWhiteSpace(result.PackageManifestPath))
        {
            lines.Add($"Manifest: {result.PackageManifestPath}");
        }

        if (result.PackageValidationResult != null)
        {
            lines.Add($"Validation: {packageErrorCount} error(s), {packageWarningCount} warning(s)");
        }

        if (lines.Count == 0)
        {
            lines.Add(T(
                language,
                "No package output was written for this export.",
                "このエクスポートではパッケージ出力は作成されていません。"));
        }

        return lines;
    }

    private static string FormatDuration(long milliseconds)
    {
        if (milliseconds < 1000)
        {
            return $"{milliseconds} ms";
        }

        TimeSpan duration = TimeSpan.FromMilliseconds(milliseconds);
        return duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}:{duration.Minutes:D2}:{duration.Seconds:D2}.{duration.Milliseconds:D3}"
            : $"{(int)duration.TotalMinutes}:{duration.Seconds:D2}.{duration.Milliseconds:D3}";
    }

    private static string T(UiLanguage language, string english, string japanese)
    {
        return UiLanguageText.Select(language, english, japanese);
    }
}
