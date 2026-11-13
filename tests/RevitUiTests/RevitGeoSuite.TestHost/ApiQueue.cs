using System;
using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Autodesk.Revit.UI;

namespace RevitGeoSuite.TestHost;

/// <summary>Runs work on Revit's API thread through one ExternalEvent, and waits for it from the pipe thread.</summary>
internal sealed class ApiQueue : IExternalEventHandler
{
    private readonly ConcurrentQueue<Action<UIApplication>> queue = new();
    private readonly ExternalEvent externalEvent;

    public ApiQueue()
    {
        externalEvent = ExternalEvent.Create(this);
    }

    /// <summary>
    /// Runs <paramref name="work"/> in an API context. Revit only runs it while it is idle, so this
    /// times out while a modal dialog (a file dialog, a TaskDialog) is open.
    /// </summary>
    public T Run<T>(Func<UIApplication, T> work, TimeSpan timeout)
    {
        var done = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        bool abandoned = false;
        queue.Enqueue(app =>
        {
            // A request the runner gave up on must not run later, out of order (a late Undo, say).
            if (abandoned)
            {
                done.TrySetCanceled();
                return;
            }

            try { done.SetResult(work(app)); }
            catch (Exception ex) { done.SetException(ex); }
        });
        ExternalEventRequest raised = externalEvent.Raise();
        if (raised != ExternalEventRequest.Accepted && raised != ExternalEventRequest.Pending)
        {
            throw new InvalidOperationException($"Revit refused the request ({raised}).");
        }

        bool finished;
        try
        {
            finished = done.Task.Wait(timeout);
        }
        catch (AggregateException failed) when (failed.InnerException != null)
        {
            // Task.Wait wraps the work's exception; callers catch the real one (InvalidOperationException, say).
            ExceptionDispatchInfo.Capture(failed.InnerException).Throw();
            throw;
        }

        if (!finished)
        {
            abandoned = true;
            throw new TimeoutException($"Revit did not run the request within {timeout.TotalSeconds:0} s. Is a dialog open?");
        }

        return done.Task.GetAwaiter().GetResult();
    }

    public void Execute(UIApplication app)
    {
        while (queue.TryDequeue(out var work))
        {
            work(app);
        }
    }

    public string GetName() => "RevitGeoSuite.TestHost";
}
