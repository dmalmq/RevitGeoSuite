using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.Shell;

/// <summary>
/// Marshals work onto Revit's API thread from the long-lived WebView2 shell, which fires bridge
/// handlers outside any <see cref="IExternalCommand"/> context. Work submitted here runs inside a
/// valid Revit API context (so transactions are legal) and resolves the *current* active document
/// at execution time — never a stale reference captured at command-launch.
/// </summary>
public sealed class RevitContext
{
    private static RevitContext? instance;

    private readonly ExternalEvent externalEvent;
    private readonly WorkQueueHandler handler;

    private RevitContext()
    {
        handler = new WorkQueueHandler();
        externalEvent = ExternalEvent.Create(handler);
    }

    /// <summary>The initialized instance. Call <see cref="Initialize"/> from OnStartup first.</summary>
    public static RevitContext Instance =>
        instance ?? throw new InvalidOperationException(
            "RevitContext is not initialized. Call RevitContext.Initialize() in IExternalApplication.OnStartup.");

    /// <summary>
    /// Creates the shared <see cref="ExternalEvent"/>. Must be called from a valid Revit API
    /// context (OnStartup is one). Idempotent.
    /// </summary>
    public static RevitContext Initialize()
    {
        return instance ??= new RevitContext();
    }

    /// <summary>Runs <paramref name="work"/> on the Revit API thread and returns its result.</summary>
    public Task<T> InvokeAsync<T>(Func<UIApplication, T> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Enqueue(app =>
        {
            try { tcs.SetResult(work(app)); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        externalEvent.Raise();
        return tcs.Task;
    }

    /// <summary>Runs <paramref name="work"/> on the Revit API thread (no return value).</summary>
    public Task InvokeAsync(Action<UIApplication> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        return InvokeAsync<object?>(app => { work(app); return null; });
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the API thread against the *current* active document,
    /// throwing a clean error when there is no active document.
    /// </summary>
    public Task<T> InvokeWithDocumentAsync<T>(Func<Document, T> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));
        return InvokeAsync(app =>
        {
            Document? doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                throw new InvalidOperationException("No active Revit document.");
            }
            return work(doc);
        });
    }

    private sealed class WorkQueueHandler : IExternalEventHandler
    {
        private readonly ConcurrentQueue<Action<UIApplication>> queue = new();

        public void Enqueue(Action<UIApplication> work) => queue.Enqueue(work);

        public void Execute(UIApplication app)
        {
            while (queue.TryDequeue(out var work))
            {
                // Each work item already captures its own try/catch around the caller's TCS,
                // so one failure can't starve the rest of the queue.
                work(app);
            }
        }

        public string GetName() => "RevitGeoSuite.RevitContext";
    }
}
