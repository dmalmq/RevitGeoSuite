using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Interop;
using RevitGeoSuite.FloorPlanExport.Core.Diagnostics;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.FloorPlanExport.Help;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using SharedLanguage = RevitGeoSuite.SharedUI.Localization.UiLanguage;
using SharedLocalizer = RevitGeoSuite.SharedUI.Localization.UiLocalizer;
using WinForms = System.Windows.Forms;

namespace RevitGeoSuite.FloorPlanExport.UI;

internal sealed class WebExportResultDialog : IDisposable
{
    private readonly FloorGeoPackageExportResult _result;
    private readonly string _outputDirectory;
    private readonly UiLanguage _language;
    private readonly WebShellWindow _window;

    public WebExportResultDialog(
        FloorGeoPackageExportResult result,
        string outputDirectory,
        UiLanguage language,
        IntPtr ownerHandle = default)
    {
        _result = result ?? throw new ArgumentNullException(nameof(result));
        _outputDirectory = ResolveOutputDirectory(result, outputDirectory);
        _language = Enum.IsDefined(typeof(UiLanguage), language) ? language : UiLanguage.English;

        SharedLocalizer.Instance.SetLanguage(ToSharedLanguage(_language));

        _window = new WebShellWindow(new WebShellOptions
        {
            InitialRoute = "/execution-result",
            TitleKey = T("Export Results", "エクスポート結果"),
            OwnerHandle = ownerHandle,
            RegisterHandlers = RegisterHandlers,
        });
    }

    public bool? ShowDialog() => _window.ShowDialog();

    public void Dispose()
    {
        if (!_window.IsClosed)
        {
            _window.Close();
        }
    }

    private void RegisterHandlers(WebRpcBridge bridge, JobManager jobs)
    {
        bridge.RegisterHandler(new GetInitialStateHandler(this));
        bridge.RegisterHandler(new OpenOutputFolderHandler(this));
        bridge.RegisterHandler(new OpenHelpHandler(this));
        bridge.RegisterHandler(new CloseHandler(this));
    }

    private ExportResultInitialStateResponse BuildInitialState()
    {
        int viewCount = _result.ArtifactResults
            .SelectMany(artifact => artifact.ContributingViewNames)
            .Distinct(StringComparer.Ordinal)
            .Count();
        int artifactCount = _result.ArtifactResults.Count;
        int warningCount = _result.Warnings.Count;
        int featureCount = _result.ArtifactResults.Sum(artifact => artifact.FeatureCount);
        int writtenArtifacts = _result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.Written);
        int reusedArtifacts = _result.ArtifactResults.Count(artifact => artifact.Disposition == ArtifactDisposition.ReusedFromBaseline);
        int packageErrorCount = _result.PackageValidationResult?.Issues.Count(issue => issue.Severity == PackageValidationSeverity.Error) ?? 0;
        int packageWarningCount = _result.PackageValidationResult?.Issues.Count(issue => issue.Severity == PackageValidationSeverity.Warning) ?? 0;

        return new ExportResultInitialStateResponse
        {
            Language = _language == UiLanguage.Japanese ? "japanese" : "english",
            Title = T("Export Results", "エクスポート結果"),
            Message = warningCount > 0
                ? T("GeoPackage export completed with warnings.", "警告付きでGeoPackageのエクスポートが完了しました。")
                : T("GeoPackage export completed.", "GeoPackageのエクスポートが完了しました。"),
            OutputDirectory = _outputDirectory,
            CanOpenOutputDirectory = !string.IsNullOrWhiteSpace(_outputDirectory) && Directory.Exists(_outputDirectory),
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
            Files = _result.ViewResults.Select(export => new ExportResultFilePayload
            {
                ViewName = export.ViewName,
                LevelName = export.LevelName,
                FeatureType = export.FeatureType,
                FeatureCount = export.FeatureCount,
                OutputFilePath = export.OutputFilePath,
            }).ToList(),
            Warnings = _result.Warnings.ToList(),
            Changes = _result.ChangeSummary == null || !_result.ChangeSummary.HasChanges
                ? new List<string>()
                : _result.ChangeSummary.Lines.ToList(),
            PackageLines = BuildPackageLines(packageErrorCount, packageWarningCount),
            Timings = _result.PhaseTimings.Select(timing => new ExportResultTimingPayload
            {
                PhaseName = timing.PhaseName,
                DurationMilliseconds = timing.DurationMilliseconds,
                DurationText = FormatDuration(timing.DurationMilliseconds),
            }).ToList(),
        };
    }

    private List<string> BuildPackageLines(int packageErrorCount, int packageWarningCount)
    {
        List<string> lines = new();
        if (!string.IsNullOrWhiteSpace(_result.PackageDirectoryPath))
        {
            lines.Add($"Package directory: {_result.PackageDirectoryPath}");
        }

        if (!string.IsNullOrWhiteSpace(_result.PackageManifestPath))
        {
            lines.Add($"Manifest: {_result.PackageManifestPath}");
        }

        if (_result.PackageValidationResult != null)
        {
            lines.Add($"Validation: {packageErrorCount} error(s), {packageWarningCount} warning(s)");
        }

        if (lines.Count == 0)
        {
            lines.Add(T(
                "No package output was written for this export.",
                "このエクスポートではパッケージ出力は作成されていません。"));
        }

        return lines;
    }

    private ExecutionActionResponse OpenOutputDirectory()
    {
        if (string.IsNullOrWhiteSpace(_outputDirectory) || !Directory.Exists(_outputDirectory))
        {
            return new ExecutionActionResponse
            {
                Success = false,
                Error = T("The output directory was not found.", "出力フォルダーが見つかりませんでした。"),
            };
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _outputDirectory,
                UseShellExecute = true,
            });
            return new ExecutionActionResponse { Success = true };
        }
        catch (Exception ex)
        {
            return new ExecutionActionResponse
            {
                Success = false,
                Error = _language == UiLanguage.Japanese
                    ? $"出力フォルダーを開けませんでした。\n\n{ex.Message}"
                    : $"Unable to open the output directory.\n\n{ex.Message}",
            };
        }
    }

    private void OpenHelp()
    {
        _window.Dispatcher.BeginInvoke(new Action(() =>
            HelpLauncher.Show(TryGetOwner(), HelpTopic.TroubleshootingFaq, _language, _window.Title)));
    }

    private void Close()
    {
        _window.Dispatcher.BeginInvoke(new Action(_window.Close));
    }

    private WinForms.IWin32Window? TryGetOwner()
    {
        IntPtr handle = new WindowInteropHelper(_window).EnsureHandle();
        return handle == IntPtr.Zero ? null : new Win32WindowOwner(handle);
    }

    private string T(string english, string japanese) => UiLanguageText.Select(_language, english, japanese);

    private static string ResolveOutputDirectory(FloorGeoPackageExportResult result, string outputDirectory)
    {
        if (!string.IsNullOrWhiteSpace(outputDirectory))
        {
            return outputDirectory.Trim();
        }

        string? firstPath = result.ViewResults
            .Select(export => export.OutputFilePath)
            .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

        return firstPath is null
            ? string.Empty
            : Path.GetDirectoryName(firstPath) ?? string.Empty;
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

    private static SharedLanguage ToSharedLanguage(UiLanguage language)
    {
        return language == UiLanguage.Japanese ? SharedLanguage.Japanese : SharedLanguage.English;
    }

    private sealed class Win32WindowOwner : WinForms.IWin32Window
    {
        public Win32WindowOwner(IntPtr handle)
        {
            Handle = handle;
        }

        public IntPtr Handle { get; }
    }

    private sealed class GetInitialStateHandler : IRpcHandler
    {
        private readonly WebExportResultDialog _dialog;

        public GetInitialStateHandler(WebExportResultDialog dialog) => _dialog = dialog;

        public string Method => "execution.result.getInitialState";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.BuildInitialState());
    }

    private sealed class OpenOutputFolderHandler : IRpcHandler
    {
        private readonly WebExportResultDialog _dialog;

        public OpenOutputFolderHandler(WebExportResultDialog dialog) => _dialog = dialog;

        public string Method => "execution.result.openOutputFolder";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.OpenOutputDirectory());
    }

    private sealed class OpenHelpHandler : IRpcHandler
    {
        private readonly WebExportResultDialog _dialog;

        public OpenHelpHandler(WebExportResultDialog dialog) => _dialog = dialog;

        public string Method => "execution.result.openHelp";

        public Task<object?> HandleAsync(object? payload)
        {
            _dialog.OpenHelp();
            return Task.FromResult<object?>(null);
        }
    }

    private sealed class CloseHandler : IRpcHandler
    {
        private readonly WebExportResultDialog _dialog;

        public CloseHandler(WebExportResultDialog dialog) => _dialog = dialog;

        public string Method => "execution.result.close";

        public Task<object?> HandleAsync(object? payload)
        {
            _dialog.Close();
            return Task.FromResult<object?>(null);
        }
    }
}
