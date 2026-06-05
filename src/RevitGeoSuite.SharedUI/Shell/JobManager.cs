using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using RevitGeoSuite.SharedUI.Web.Contracts;

namespace RevitGeoSuite.SharedUI.Shell;

/// <summary>
/// Runs cancellable long-running work and streams a standard job protocol to the web layer:
/// a <see cref="JobStarted"/> reply, then <c>job.progress</c> events, terminating in
/// <c>job.completed</c> or <c>job.failed</c>. Revit-agnostic — Revit work is marshalled by the
/// job body itself (e.g. via RevitContext). One <see cref="JobCancelHandler"/> handles
/// <c>job.cancel</c>.
/// </summary>
public sealed class JobManager
{
    private readonly Action<string, object?> sendEvent;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> jobs = new();

    /// <param name="sendEvent">Sink for job events — typically <c>WebRpcBridge.SendEvent</c>.</param>
    public JobManager(Action<string, object?> sendEvent)
    {
        this.sendEvent = sendEvent ?? throw new ArgumentNullException(nameof(sendEvent));
    }

    /// <summary>
    /// Starts <paramref name="work"/> and returns its job id immediately. Progress reported through
    /// the supplied <see cref="IProgress{T}"/> is forwarded as <c>job.progress</c>; the result/error
    /// is forwarded as <c>job.completed</c>/<c>job.failed</c>.
    /// </summary>
    public string Start(Func<CancellationToken, IProgress<JobProgress>, Task<object?>> work)
    {
        if (work == null) throw new ArgumentNullException(nameof(work));

        string jobId = Guid.NewGuid().ToString("N");
        var cts = new CancellationTokenSource();
        jobs[jobId] = cts;

        // Capture the calling (UI) SynchronizationContext so every CoreWebView2 post happens on the
        // UI thread, even though the work runs on the thread pool.
        SynchronizationContext? sync = SynchronizationContext.Current;
        void Send(string method, object payload)
        {
            if (sync != null) sync.Post(_ => sendEvent(method, payload), null);
            else sendEvent(method, payload);
        }

        var progress = new Progress<JobProgress>(p =>
        {
            p.JobId = jobId;
            Send("job.progress", p);
        });

        _ = Task.Run(async () =>
        {
            try
            {
                object? result = await work(cts.Token, progress).ConfigureAwait(false);
                Send("job.completed", new JobCompleted { JobId = jobId, Result = result });
            }
            catch (OperationCanceledException)
            {
                Send("job.failed", new JobFailed { JobId = jobId, Error = "Cancelled", Cancelled = true });
            }
            catch (Exception ex)
            {
                Send("job.failed", new JobFailed { JobId = jobId, Error = ex.Message });
            }
            finally
            {
                if (jobs.TryRemove(jobId, out var removed))
                {
                    removed.Dispose();
                }
            }
        });

        return jobId;
    }

    /// <summary>Requests cancellation of a running job. Returns false if the job is unknown/finished.</summary>
    public bool Cancel(string? jobId)
    {
        if (!string.IsNullOrEmpty(jobId) && jobs.TryGetValue(jobId!, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }
}

/// <summary>Handles the <c>job.cancel</c> RPC by cancelling the matching job.</summary>
public sealed class JobCancelHandler : IRpcHandler
{
    private readonly JobManager jobs;

    public JobCancelHandler(JobManager jobs)
    {
        this.jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public string Method => "job.cancel";

    public Task<object?> HandleAsync(object? payload)
    {
        string? jobId = (payload as JObject)?.Value<string>("jobId");
        bool cancelled = jobs.Cancel(jobId);
        return Task.FromResult<object?>(new JobCancelResponse { Cancelled = cancelled });
    }
}
