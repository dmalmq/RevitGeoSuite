using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RevitGeoSuite.FloorPlanExport.Core.Diagnostics;

namespace RevitGeoSuite.FloorPlanExport.Export;

public sealed class FloorGeoPackageExportResult
{
    private readonly List<ExportArtifactResult> _artifacts = new();
    private readonly List<string> _warnings = new();
    private readonly List<ExportDiagnosticsPhaseTiming> _phaseTimings = new();

    public IReadOnlyList<ExportArtifactResult> ArtifactResults => _artifacts;

    public IReadOnlyList<ViewExportResult> ViewResults => _artifacts
        .Select(artifact => new ViewExportResult(
            artifact.ContributingViewNames.FirstOrDefault() ?? artifact.ArtifactName,
            artifact.ContributingLevelNames.FirstOrDefault() ?? string.Empty,
            artifact.LayerSummary,
            artifact.OutputFilePath,
            artifact.FeatureCount))
        .ToList();

    public IReadOnlyList<string> Warnings => _warnings;

    public IReadOnlyList<ExportDiagnosticsPhaseTiming> PhaseTimings => _phaseTimings;

    public string? DiagnosticsReportPath { get; private set; }

    public string? PackageDirectoryPath { get; private set; }

    public string? PackageManifestPath { get; private set; }

    public ExportChangeSummary? ChangeSummary { get; private set; }

    public ExportExecutionSummary? ExecutionSummary { get; private set; }

    public PackageValidationResult? PackageValidationResult { get; private set; }

    public ExportBaselineSnapshot? PendingBaselineSnapshot { get; private set; }

    public ExportDiagnosticsReport? PendingBaselineReport { get; private set; }

    public ExportPackageManifest? PendingBaselineManifest { get; private set; }

    public void AddArtifactResult(ExportArtifactResult result)
    {
        if (result is null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        _artifacts.Add(result);
    }

    public void AddWarning(string warning)
    {
        if (string.IsNullOrWhiteSpace(warning))
        {
            return;
        }

        _warnings.Add(warning.Trim());
    }
    public void AddWarnings(IEnumerable<string> warnings)
    {
        if (warnings is null)
        {
            throw new ArgumentNullException(nameof(warnings));
        }

        _warnings.AddRange(warnings);
    }

    public void SetDiagnosticsReportPath(string? diagnosticsReportPath)
    {
        DiagnosticsReportPath = string.IsNullOrWhiteSpace(diagnosticsReportPath)
            ? null
            : diagnosticsReportPath!.Trim();
    }

    public void SetPackagePaths(string? packageDirectoryPath, string? packageManifestPath)
    {
        PackageDirectoryPath = string.IsNullOrWhiteSpace(packageDirectoryPath) ? null : packageDirectoryPath!.Trim();
        PackageManifestPath = string.IsNullOrWhiteSpace(packageManifestPath) ? null : packageManifestPath!.Trim();
    }

    public void SetChangeSummary(ExportChangeSummary? changeSummary)
    {
        ChangeSummary = changeSummary;
    }

    public void SetExecutionSummary(ExportExecutionSummary? executionSummary)
    {
        ExecutionSummary = executionSummary;
    }

    public void SetPackageValidationResult(PackageValidationResult? validationResult)
    {
        PackageValidationResult = validationResult;
    }

    public void SetPendingBaselineSnapshot(ExportBaselineSnapshot? snapshot)
    {
        PendingBaselineSnapshot = snapshot;
    }

    public void SetPendingBaselineUpdate(ExportDiagnosticsReport report, ExportPackageManifest manifest)
    {
        PendingBaselineReport = report ?? throw new ArgumentNullException(nameof(report));
        PendingBaselineManifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    public void CommitPendingBaseline(
        string sourceRoot,
        string destinationRoot,
        ExportBaselineStore? baselineStore = null)
    {
        if (PendingBaselineSnapshot is null || PendingBaselineReport is null || PendingBaselineManifest is null)
        {
            return;
        }

        foreach (ExportBaselineArtifactSnapshot artifact in PendingBaselineSnapshot.Artifacts)
        {
            artifact.OutputFilePath = RebasePath(artifact.OutputFilePath, sourceRoot, destinationRoot);
        }
        foreach (ExportDiagnosticsOutputFile outputFile in PendingBaselineReport.OutputFiles)
        {
            outputFile.Path = RebasePath(outputFile.Path, sourceRoot, destinationRoot);
        }
        PendingBaselineManifest.PackageDirectory = RebasePath(
            PendingBaselineManifest.PackageDirectory, sourceRoot, destinationRoot);
        foreach (ExportPackageManifestFile file in PendingBaselineManifest.Files)
        {
            file.OutputFilePath = RebasePath(file.OutputFilePath, sourceRoot, destinationRoot);
        }

        (baselineStore ?? new ExportBaselineStore()).Save(
            PendingBaselineSnapshot.BaselineKey,
            PendingBaselineReport,
            PendingBaselineManifest,
            PendingBaselineSnapshot);
    }

    private static string RebasePath(string path, string sourceRoot, string destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        string source = Path.GetFullPath(sourceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string fullPath = Path.GetFullPath(path);
        if (!fullPath.Equals(source, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            !fullPath.StartsWith(source + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return path;
        }

        string relative = fullPath.Substring(source.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(destinationRoot, relative);
    }

    public void AddPhaseTiming(string phaseName, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(phaseName))
        {
            return;
        }

        _phaseTimings.Add(new ExportDiagnosticsPhaseTiming
        {
            PhaseName = phaseName.Trim(),
            DurationMilliseconds = (long)Math.Max(0d, duration.TotalMilliseconds),
        });
    }
}

public sealed class ViewExportResult
{
    public ViewExportResult(
        string viewName,
        string levelName,
        string featureType,
        string outputFilePath,
        int featureCount)
    {
        ViewName = viewName ?? throw new ArgumentNullException(nameof(viewName));
        LevelName = levelName ?? throw new ArgumentNullException(nameof(levelName));
        FeatureType = featureType ?? throw new ArgumentNullException(nameof(featureType));
        OutputFilePath = outputFilePath ?? throw new ArgumentNullException(nameof(outputFilePath));
        FeatureCount = featureCount;
    }

    public string ViewName { get; }

    public string LevelName { get; }

    public string FeatureType { get; }

    public string OutputFilePath { get; }

    public int FeatureCount { get; }
}
