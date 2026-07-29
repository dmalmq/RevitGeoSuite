using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitGeoSuite.FloorPlanExport.Core;
using RevitGeoSuite.FloorPlanExport.Core.Assignments;
using RevitGeoSuite.FloorPlanExport.Core.Diagnostics;
using RevitGeoSuite.FloorPlanExport.Core.Models;
using RevitGeoSuite.FloorPlanExport.Core.Schema;
using RevitGeoSuite.FloorPlanExport.Core.Validation;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.Resources;
using RevitGeoSuite.FloorPlanExport.UI;
using WinForms = System.Windows.Forms;

namespace RevitGeoSuite.FloorPlanExport.Commands;

public sealed class ExportWorkflowCoordinator
{
    private readonly Document _document;
    private readonly UIDocument? _uiDocument;
    private readonly string _projectKey;
    private readonly IReadOnlyList<string> _availableFloorTypeNames;
    private readonly ExportProfileStore _profileStore;

    public ExportWorkflowCoordinator(
        Document document,
        UIDocument? uiDocument,
        string projectKey,
        IReadOnlyList<string> availableFloorTypeNames,
        ExportProfileStore profileStore)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _uiDocument = uiDocument;
        _projectKey = string.IsNullOrWhiteSpace(projectKey)
            ? throw new ArgumentException("A project key is required.", nameof(projectKey))
            : projectKey.Trim();
        _availableFloorTypeNames = availableFloorTypeNames ?? throw new ArgumentNullException(nameof(availableFloorTypeNames));
        _profileStore = profileStore ?? throw new ArgumentNullException(nameof(profileStore));
    }

    public void SaveProfile(ExportProfileScope scope, string name, ExportDialogSettings profileSettings)
    {
        _profileStore.SaveProfile(_projectKey, ExportProfile.FromSettings(name, scope, profileSettings));
    }

    public void DeleteProfile(ExportProfile profile)
    {
        _profileStore.DeleteProfile(_projectKey, profile);
    }

    public void ShowPreview(ExportPreviewRequest previewRequest, WinForms.IWin32Window? owner = null)
    {
        ExportPreviewService previewService = new(
            _document,
            previewRequest.UnitSource,
            previewRequest.UnitGeometrySource,
            previewRequest.UnitAttributeSource,
            previewRequest.RoomCategoryParameterName,
            previewRequest.GeometryRepairOptions,
            previewRequest.LinkExportOptions,
            previewRequest.ActiveSchemaProfile,
            previewRequest.SimplifyStairUnits,
            previewRequest.SimplifyEscalatorUnits,
            previewRequest.Use3DSectionBoxExport,
            previewRequest.SectionBoxAboveFloorMeters,
            previewRequest.SectionBoxBelowFloorMeters,
            previewRequest.Keep3DTempViewsForDebug,
            previewRequest.UnitCategories);

        using WebExportPreviewDialog previewDialog = new(previewRequest, previewService, owner);
        _ = previewDialog.ShowDialog();
    }

    public Result RunExport(ExportDialogResult request, ModelCoordinateInfo coordinateInfo, ref string message)
    {
        try
        {
            FloorGeoPackageExportResult result;
            using (WebExportProgressDialog progressDialog = new(request.UiLanguage))
            {
                progressDialog.Show();
                try
                {
                    progressDialog.Refresh();
                    result = RunExportCore(
                        request,
                        coordinateInfo,
                        update => progressDialog.UpdateProgress(update),
                        progressDialog.CancellationToken);
                }
                finally
                {
                    progressDialog.Close();
                }
            }

            ShowExportResult(result, request);
            return Result.Succeeded;
        }
        catch (OperationCanceledException)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                UiLanguageText.Get(request.UiLanguage, "Command.ExportCancelled", "Export was cancelled. Partial output may have been written to the output directory."));
            return Result.Cancelled;
        }
        catch (Exception ex)
        {
            message = ex.Message;
            ShowExportFailureDialog(ex, request);
            return Result.Failed;
        }
    }

    public FloorGeoPackageExportResult RunExportCore(
        ExportDialogResult request,
        ModelCoordinateInfo coordinateInfo,
        Action<ExportProgressUpdate>? progressCallback = null,
        CancellationToken cancellationToken = default,
        string? baselineKeyOverride = null,
        bool persistBaseline = true)
    {
        FloorGeoPackageExporter exporter = new(_document);
        ExportValidationSnapshotBuilder snapshotBuilder = new();
        ExportValidationService validationService = new();

        PreparedExportSession session = exporter.PrepareExport(
            request.OutputDirectory,
            request.TargetEpsg,
            request.SelectedViews,
            request.FeatureTypes,
            request.GeometryRepairOptions,
            new ExportPackageOptions
            {
                Enabled = request.GeneratePackageOutput,
                IncludeLegendFile = request.IncludePackageLegend,
                PackagingMode = request.PackagingMode,
                ValidateAfterWrite = request.ValidateAfterWrite,
                GenerateQgisArtifacts = request.GenerateQgisArtifacts,
                PostExportActions = request.PostExportActions.Clone(),
            },
            request.SelectedProfileName,
            string.IsNullOrWhiteSpace(baselineKeyOverride)
                ? BuildBaselineKey(_projectKey, request.SelectedProfileName)
                : baselineKeyOverride!.Trim(),
            request.IncrementalExportMode,
            request.CoordinateMode,
            coordinateInfo.ResolvedSourceEpsg,
            coordinateInfo.SiteCoordinateSystemId,
            coordinateInfo.SiteCoordinateSystemDefinition,
            request.UnitSource,
            request.UnitGeometrySource,
            request.UnitAttributeSource,
            request.RoomCategoryParameterName,
            request.LinkExportOptions,
            request.ActiveSchemaProfile,
            request.ActiveValidationPolicyProfile,
            request.SimplifyStairUnits,
            request.SimplifyEscalatorUnits,
            request.Use3DSectionBoxExport,
            request.SectionBoxAboveFloorMeters,
            request.SectionBoxBelowFloorMeters,
            request.Keep3DTempViewsForDebug,
            request.UnitCategories);
        session.OutputFormat = request.OutputFormat;

        // Validation feeds diagnostics only. The wizard surfaces readiness inline at Preview,
        // so there are no blocking readiness/validation dialogs in this core path.
        ExportValidationResult validationResult = validationService.Validate(snapshotBuilder.Build(session));
        return CompleteExport(session, validationResult, request, progressCallback, cancellationToken);
    }

    private FloorGeoPackageExportResult CompleteExport(
        PreparedExportSession session,
        ExportValidationResult validationResult,
        ExportDialogResult request,
        Action<ExportProgressUpdate>? progressCallback,
        CancellationToken cancellationToken)
    {
        FloorGeoPackageExporter exporter = new(_document);
        FloorGeoPackageExportResult result;
        Stopwatch stopwatch = Stopwatch.StartNew();
        Stopwatch phaseStopwatch = Stopwatch.StartNew();
        result = exporter.WritePreparedExport(
            session,
            progressCallback: progressCallback,
            cancellationToken: cancellationToken);

        phaseStopwatch.Stop();
        result.AddPhaseTiming("Artifact writing", phaseStopwatch.Elapsed);

        stopwatch.Stop();
        cancellationToken.ThrowIfCancellationRequested();

        ExportDiagnosticsReportBuilder diagnosticsBuilder = new();
        ExportDiagnosticsReport diagnosticsReport = diagnosticsBuilder.Build(
            session,
            validationResult,
            result,
            DateTimeOffset.UtcNow,
            stopwatch.Elapsed);

        if (request.GenerateDiagnosticsReport)
        {
            try
            {
                phaseStopwatch.Restart();
                ExportDiagnosticsWriter diagnosticsWriter = new();
                string diagnosticsPath = diagnosticsWriter.WriteJson(request.OutputDirectory, diagnosticsReport);
                phaseStopwatch.Stop();
                result.AddPhaseTiming("Diagnostics report", phaseStopwatch.Elapsed);
                result.SetDiagnosticsReportPath(diagnosticsPath);
            }
            catch (Exception diagnosticsException)
            {
                result.AddWarnings(
                    new[]
                    {
                        $"Diagnostics report could not be written: {diagnosticsException.Message}",
                    });
            }
        }

        ExportPackageService packageService = new();
        phaseStopwatch.Restart();
        ExportPackageResult packageResult = packageService.BuildPackage(session, diagnosticsReport, result);
        phaseStopwatch.Stop();
        result.AddPhaseTiming("Package build", phaseStopwatch.Elapsed);
        result.SetPackagePaths(packageResult.PackageDirectory, packageResult.ManifestPath);
        result.SetPackageValidationResult(packageResult.ValidationResult);

        phaseStopwatch.Restart();
        ExportBaselineStore baselineStore = new();
        ExportBaselineLoadResult baseline = baselineStore.Load(session.BaselineKey);
        result.AddWarnings(baseline.Warnings);
        ChangeSummaryService changeSummaryService = new();
        result.SetChangeSummary(changeSummaryService.Compare(
            baseline.Snapshot,
            baseline.Report,
            diagnosticsReport,
            baseline.Manifest,
            packageResult.Manifest,
            result.ExecutionSummary?.ChangedViewCount ?? session.Prepared.Views.Count,
            result.ExecutionSummary?.ReusedViewCount ?? 0,
            result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.Written),
            result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.ReusedFromBaseline),
            result.ExecutionSummary?.MissingBaselineArtifactCount ?? 0,
            result.ExecutionSummary?.FullRewriteReason));

        bool canReplaceBaseline = result.PendingBaselineSnapshot != null &&
                                  (packageResult.ValidationResult == null || !packageResult.ValidationResult.HasErrors);
        if (canReplaceBaseline)
        {
            if (persistBaseline)
            {
                baselineStore.Save(session.BaselineKey, diagnosticsReport, packageResult.Manifest, result.PendingBaselineSnapshot!);
            }
            else
            {
                result.SetPendingBaselineUpdate(diagnosticsReport, packageResult.Manifest);
            }
        }
        else if (packageResult.ValidationResult?.HasErrors == true)
        {
            result.AddWarning("Package validation errors prevented the export baseline from being replaced.");
        }

        phaseStopwatch.Stop();
        result.AddPhaseTiming("Baseline update", phaseStopwatch.Elapsed);

        if (request.GenerateDiagnosticsReport && !string.IsNullOrWhiteSpace(result.DiagnosticsReportPath))
        {
            diagnosticsReport.PhaseTimings = result.PhaseTimings.ToList();
            diagnosticsReport.PackageValidationResult = result.PackageValidationResult;
            try
            {
                File.WriteAllText(result.DiagnosticsReportPath, Newtonsoft.Json.JsonConvert.SerializeObject(diagnosticsReport, Newtonsoft.Json.Formatting.Indented));
                if (!string.IsNullOrWhiteSpace(result.PackageDirectoryPath))
                {
                    string packagedDiagnosticsPath = Path.Combine(result.PackageDirectoryPath, Path.GetFileName(result.DiagnosticsReportPath));
                    if (File.Exists(packagedDiagnosticsPath))
                    {
                        File.Copy(result.DiagnosticsReportPath, packagedDiagnosticsPath, overwrite: true);
                    }
                }
            }
            catch (Exception diagnosticsException)
            {
                result.AddWarning($"Diagnostics report timing details could not be refreshed: {diagnosticsException.Message}");
            }
        }

        return result;
    }

    private static void ShowExportResult(FloorGeoPackageExportResult result, ExportDialogResult request)
    {
        using WebExportResultDialog resultDialog = new(result, request.OutputDirectory, request.UiLanguage);
        _ = resultDialog.ShowDialog();
    }

    private static void ShowExportFailureDialog(Exception exception, ExportDialogResult request)
    {
        UiLanguage language = request.UiLanguage;
        string reportText = BuildFailureReport(exception);

        TaskDialog dialog = new(ProjectInfo.Name)
        {
            MainInstruction = UiLanguageText.Get(language, "Command.ExportFailed", "Export failed."),
            MainContent = UiLanguageText.Get(
                language,
                "Command.ExportFailed.Body",
                "The export could not be completed. You can save an error report as a text file."),
            ExpandedContent = reportText,
            AllowCancellation = true,
            CommonButtons = TaskDialogCommonButtons.Close,
        };
        dialog.AddCommandLink(
            TaskDialogCommandLinkId.CommandLink1,
            UiLanguageText.Get(language, "Command.ExportFailed.SaveReport", "Save Error Report"));

        TaskDialogResult dialogResult = dialog.Show();
        if (dialogResult != TaskDialogResult.CommandLink1)
        {
            return;
        }

        SaveFailureReportToTextFile(reportText, request.OutputDirectory, language);
    }

    private static void SaveFailureReportToTextFile(string reportText, string? preferredDirectory, UiLanguage language)
    {
        string initialDirectory = ResolveReportDirectory(preferredDirectory);
        string defaultFileName = $"RevitGeoSuite-FloorPlanExport-ExportError-{DateTime.Now:yyyyMMdd-HHmmss}.txt";

        using SaveFileDialog saveDialog = new()
        {
            Title = UiLanguageText.Get(language, "Command.ExportFailed.SaveReportTitle", "Save Export Error Report"),
            Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
            DefaultExt = "txt",
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = initialDirectory,
            FileName = defaultFileName,
        };

        if (saveDialog.ShowDialog() != DialogResult.OK ||
            string.IsNullOrWhiteSpace(saveDialog.FileName))
        {
            return;
        }

        try
        {
            File.WriteAllText(saveDialog.FileName, reportText, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            TaskDialog.Show(
                ProjectInfo.Name,
                UiLanguageText.Format(
                    language,
                    "Command.ExportFailed.ReportSaved",
                    "Error report saved.{0}{1}",
                    Environment.NewLine,
                    saveDialog.FileName));
        }
        catch (Exception ex)
        {
            TaskDialog.Show(
                ProjectInfo.Name,
                UiLanguageText.Format(
                    language,
                    "Command.ExportFailed.ReportSaveFailed",
                    "Failed to save error report.{0}{0}{1}",
                    Environment.NewLine,
                    ex.Message));
        }
    }

    private static string ResolveReportDirectory(string? preferredDirectory)
    {
        string trimmedPreferredDirectory = preferredDirectory?.Trim() ?? string.Empty;
        if (trimmedPreferredDirectory.Length > 0 && Directory.Exists(trimmedPreferredDirectory))
        {
            return trimmedPreferredDirectory;
        }

        string documentsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documentsDirectory) ? documentsDirectory : Environment.CurrentDirectory;
    }

    private static string BuildFailureReport(Exception exception)
    {
        StringBuilder reportBuilder = new();
        reportBuilder.AppendLine("RevitGeoSuite GeoPackage / Shapefile Export Error Report");
        reportBuilder.AppendLine($"Timestamp: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        reportBuilder.AppendLine();
        reportBuilder.AppendLine(exception.ToString());
        return reportBuilder.ToString();
    }

    private static string BuildBaselineKey(string projectKey, string? profileName)
    {
        string normalizedProfileName = profileName?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(normalizedProfileName)
            ? projectKey
            : $"{projectKey}__{normalizedProfileName}";
    }

}
