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
using RevitGeoSuite.FloorPlanExport.Core.Standards;
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

    /// <summary>"Saved for station ..." once a profile went to the station's rules file; null while profiles are this PC's.</summary>
    public string? RulesSavedStatus => _profileStore.SavedStatus;

    /// <summary>
    /// Saves a profile and returns the scope it was saved in. For a model with a station rules file,
    /// a profile named as the station profile the model exports with is saved to the station whatever
    /// scope was asked for, so saving the settings under that name changes the station's on purpose.
    /// </summary>
    public ExportProfileScope SaveProfile(ExportProfileScope scope, string name, ExportDialogSettings profileSettings)
    {
        GisRulesSource rules = GisRulesSourceFactory.For(_document);
        if (rules.IsStation && string.Equals(name.Trim(), StationProfileName(), StringComparison.OrdinalIgnoreCase))
        {
            scope = ExportProfileScope.Project;
        }

        _profileStore.SaveProfile(_projectKey, ExportProfile.FromSettings(name, scope, profileSettings));
        return scope;
    }

    /// <summary>
    /// The settings of <paramref name="request"/> that the station profile will replace when it is
    /// exported, with how to change the station's value instead, for the dialog to show before the
    /// export; null when the model has no station rules file or nothing differs.
    /// </summary>
    public string? DescribeStationOverrides(ExportDialogResult request)
    {
        GisRulesSource rules = GisRulesSourceFactory.For(_document);
        if (!rules.IsStation)
        {
            return null;
        }

        string profile = StationProfileName();
        IReadOnlyList<string> overrides = rules.StationOverrides(profile, GisRulesProfileMapper.FromResult(profile, request));
        if (overrides.Count == 0)
        {
            return null;
        }

        string list = string.Join("; ", overrides);
        return UiLanguageText.Select(
            request.UiLanguage,
            $"Station {rules.StationName} exports with its profile '{profile}', which replaces these settings (yours -> station): {list}. To change the station's value on purpose, save these settings as the profile '{profile}'.",
            $"ステーション「{rules.StationName}」はプロファイル「{profile}」で出力するため、次の設定はステーションの値に置き換わります (自分 -> ステーション): {list}。ステーションの値を意図して変えるには、この設定をプロファイル「{profile}」として保存してください。");
    }

    private string StationProfileName()
    {
        return GisModelSettings.Load(_document, null).File?.ProfileName ?? GisModelFile.DefaultProfileName;
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
        bool persistBaseline = true,
        bool recordViewsInGisModel = false)
    {
        List<string> rulesWarnings = new();
        GisRulesSource rules = ResolveRulesForExport(rulesWarnings);
        GisRulesStamp rulesStamp;
        (request, rulesStamp) = ApplyStationProfile(request, rules, rulesWarnings);

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
            request.UnitCategories,
            rules);
        session.OutputFormat = request.OutputFormat;

        // Validation feeds diagnostics only. The wizard surfaces readiness inline at Preview,
        // so there are no blocking readiness/validation dialogs in this core path.
        ExportValidationResult validationResult = validationService.Validate(snapshotBuilder.Build(session));
        FloorGeoPackageExportResult result = CompleteExport(session, validationResult, request, progressCallback, cancellationToken, persistBaseline);

        // The model's gis-model.json on the share remembers what this export used. Only the
        // station export (headless) records its views, so a one-off dialog export of a single
        // view does not change what every PC exports next. Every export records its linked models,
        // since a station profile no longer carries them and the dialog is where they are picked.
        List<string> gisModelWarnings = new();
        GisModelSettings.RecordExport(
            _document,
            recordViewsInGisModel ? request.SelectedViews : Array.Empty<ViewPlan>(),
            request.CoordinateMode == CoordinateExportMode.ConvertToTargetCrs ? request.TargetEpsg : coordinateInfo.ResolvedSourceEpsg,
            request.LinkExportOptions,
            gisModelWarnings);
        result.AddWarnings(gisModelWarnings);
        result.AddWarnings(rulesWarnings);
        result.GisRules = rulesStamp;
        return result;
    }

    /// <summary>
    /// The rules this export runs on. A model with a station rules file first has this PC's rules and
    /// project profiles added to it. If the station cannot be written, the export runs on this PC's
    /// rules and says so; nothing is set aside then.
    /// </summary>
    private GisRulesSource ResolveRulesForExport(ICollection<string> warnings)
    {
        GisRulesSource rules = GisRulesSourceFactory.For(_document).ForExport(warnings);
        if (!rules.IsStation)
        {
            return rules;
        }

        try
        {
            _profileStore.MoveLocalToStation(_projectKey, rules, warnings);
            return rules;
        }
        catch (IOException ex)
        {
            warnings.Add($"This PC's rules could not be added to the station's rules file ({ex.Message}). This export uses this PC's rules, not the station's, and its package says so.");
            return rules.WithoutStation();
        }
    }

    private (ExportDialogResult Request, GisRulesStamp Stamp) ApplyStationProfile(
        ExportDialogResult request,
        GisRulesSource rules,
        ICollection<string> warnings)
    {
        IEnumerable<MappingRule> categoryRules = rules.Load().Value.Rules;
        if (!rules.IsStation)
        {
            GisRulesProfile local = GisRulesProfileMapper.FromResult(request.SelectedProfileName ?? "Local", request);
            return (request, new GisRulesStamp(GisRulesStamp.LocalKind, local.Name, GisRulesHash.Compute(categoryRules, local)));
        }

        string profileName = GisModelSettings.Load(_document, warnings).File?.ProfileName ?? GisModelFile.DefaultProfileName;
        GisRulesProfile profile = rules.ResolveStationProfile(profileName, GisRulesProfileMapper.FromResult(profileName, request), warnings);
        return (GisRulesProfileMapper.Overlay(request, profile), new GisRulesStamp(GisRulesStamp.StationKind, profile.Name, GisRulesHash.Compute(categoryRules, profile)));
    }

    private FloorGeoPackageExportResult CompleteExport(
        PreparedExportSession session,
        ExportValidationResult validationResult,
        ExportDialogResult request,
        Action<ExportProgressUpdate>? progressCallback,
        CancellationToken cancellationToken,
        bool persistBaseline)
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
