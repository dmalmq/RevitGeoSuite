using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using RevitGeoSuite.FloorPlanExport.Export;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using SharedLanguage = RevitGeoSuite.SharedUI.Localization.UiLanguage;
using SharedLocalizer = RevitGeoSuite.SharedUI.Localization.UiLocalizer;

namespace RevitGeoSuite.FloorPlanExport.UI;

internal sealed class WebExportProgressDialog : IDisposable
{
    private readonly UiLanguage _language;
    private readonly WebShellWindow _window;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Stopwatch _stopwatch = new();
    private ExecutionProgressPayload _latestProgress;

    public WebExportProgressDialog(UiLanguage language, IntPtr ownerHandle = default)
    {
        _language = Enum.IsDefined(typeof(UiLanguage), language) ? language : UiLanguage.English;
        SharedLocalizer.Instance.SetLanguage(ToSharedLanguage(_language));

        _latestProgress = new ExecutionProgressPayload
        {
            StatusText = UiLanguageText.Select(_language, "Preparing export...", "出力を準備中..."),
            CompletedSteps = 0,
            TotalSteps = 1,
            StartedAtUtc = DateTimeOffset.UtcNow.ToString("O"),
        };

        _window = new WebShellWindow(new WebShellOptions
        {
            InitialRoute = "/execution-progress",
            TitleKey = UiLanguageText.Select(_language, "Export Progress", "出力の進行状況"),
            OwnerHandle = ownerHandle,
            RegisterHandlers = RegisterHandlers,
        });
    }

    public CancellationToken CancellationToken => _cancellationTokenSource.Token;

    public void Show()
    {
        _latestProgress.StartedAtUtc = DateTimeOffset.UtcNow.ToString("O");
        _stopwatch.Start();
        _window.Show();
    }

    public void Refresh()
    {
        if (_window.IsClosed)
        {
            return;
        }

        _window.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
    }

    public void Close()
    {
        _stopwatch.Stop();
        if (_window.IsClosed)
        {
            return;
        }

        _window.Dispatcher.Invoke(_window.Close);
    }

    public void UpdateProgress(ExportProgressUpdate update)
    {
        if (update is null)
        {
            throw new ArgumentNullException(nameof(update));
        }

        int total = Math.Max(1, update.TotalSteps);
        int completed = Math.Max(0, Math.Min(update.CompletedSteps, total));
        _latestProgress = new ExecutionProgressPayload
        {
            StatusText = string.IsNullOrWhiteSpace(update.StatusText)
                ? UiLanguageText.Select(_language, "Exporting...", "出力中...")
                : update.StatusText,
            CompletedSteps = completed,
            TotalSteps = total,
            IsCancelling = _cancellationTokenSource.IsCancellationRequested,
            StartedAtUtc = _latestProgress.StartedAtUtc,
        };

        SendProgressEvent();
        Refresh();
    }

    public void Dispose()
    {
        if (!_window.IsClosed)
        {
            _window.Close();
        }

        _cancellationTokenSource.Dispose();
    }

    private void RegisterHandlers(WebRpcBridge bridge, JobManager jobs)
    {
        bridge.RegisterHandler(new GetInitialStateHandler(this));
        bridge.RegisterHandler(new CancelHandler(this));
        bridge.RegisterHandler(new WindowCloseAsCancelHandler(this));
    }

    private ExecutionProgressInitialStateResponse BuildInitialState()
    {
        return new ExecutionProgressInitialStateResponse
        {
            Language = _language == UiLanguage.Japanese ? "japanese" : "english",
            Progress = _latestProgress,
        };
    }

    private void Cancel()
    {
        if (!_cancellationTokenSource.IsCancellationRequested)
        {
            _cancellationTokenSource.Cancel();
        }

        _latestProgress = new ExecutionProgressPayload
        {
            StatusText = UiLanguageText.Select(_language, "Cancelling...", "キャンセル中..."),
            CompletedSteps = _latestProgress.CompletedSteps,
            TotalSteps = _latestProgress.TotalSteps,
            IsCancelling = true,
            StartedAtUtc = _latestProgress.StartedAtUtc,
        };

        SendProgressEvent();
    }

    private void CancelAndClose()
    {
        Cancel();
        if (!_window.IsClosed)
        {
            _window.Dispatcher.BeginInvoke(new Action(_window.Close));
        }
    }

    private void SendProgressEvent()
    {
        if (_window.IsClosed)
        {
            return;
        }

        _window.Dispatcher.BeginInvoke(new Action(() =>
        {
            if (!_window.IsClosed)
            {
                _window.Bridge.SendEvent("execution.progress.updated", _latestProgress);
            }
        }));
    }

    private static SharedLanguage ToSharedLanguage(UiLanguage language)
    {
        return language == UiLanguage.Japanese ? SharedLanguage.Japanese : SharedLanguage.English;
    }

    private sealed class GetInitialStateHandler : IRpcHandler
    {
        private readonly WebExportProgressDialog _dialog;

        public GetInitialStateHandler(WebExportProgressDialog dialog) => _dialog = dialog;

        public string Method => "execution.progress.getInitialState";

        public Task<object?> HandleAsync(object? payload) => Task.FromResult<object?>(_dialog.BuildInitialState());
    }

    private sealed class CancelHandler : IRpcHandler
    {
        private readonly WebExportProgressDialog _dialog;

        public CancelHandler(WebExportProgressDialog dialog) => _dialog = dialog;

        public string Method => "execution.progress.cancel";

        public Task<object?> HandleAsync(object? payload)
        {
            _dialog.Cancel();
            return Task.FromResult<object?>(null);
        }
    }

    private sealed class WindowCloseAsCancelHandler : IRpcHandler
    {
        private readonly WebExportProgressDialog _dialog;

        public WindowCloseAsCancelHandler(WebExportProgressDialog dialog) => _dialog = dialog;

        public string Method => "window.close";

        public Task<object?> HandleAsync(object? payload)
        {
            _dialog.CancelAndClose();
            return Task.FromResult<object?>(null);
        }
    }
}
