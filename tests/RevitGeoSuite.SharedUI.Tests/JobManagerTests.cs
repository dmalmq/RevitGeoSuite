using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.SharedUI.Shell;
using RevitGeoSuite.SharedUI.Web.Contracts;
using Xunit;

namespace RevitGeoSuite.SharedUI.Tests;

public class JobManagerTests
{
    [Fact]
    public async Task Start_EmitsCompletedWithResult()
    {
        var sink = new RecordingSink();
        var manager = new JobManager(sink.Send);

        string jobId = manager.Start((ct, progress) => Task.FromResult<object?>(new { value = 42 }));

        Assert.True(await WaitAsync(sink.Terminal, 5000), "Job did not complete in time.");
        var completed = Assert.IsType<JobCompleted>(LastPayload(sink, "job.completed"));
        Assert.Equal(jobId, completed.JobId);
        Assert.NotNull(completed.Result);
    }

    [Fact]
    public async Task Start_ForwardsProgress()
    {
        var sink = new RecordingSink();
        var manager = new JobManager(sink.Send);

        string jobId = manager.Start((ct, progress) =>
        {
            progress.Report(new JobProgress { Percent = 50, Message = "half" });
            return Task.FromResult<object?>(null);
        });

        Assert.True(await WaitAsync(sink.Terminal, 5000));
        JobProgress? progressEvent = FindPayload<JobProgress>(sink, "job.progress");
        Assert.NotNull(progressEvent);
        Assert.Equal(jobId, progressEvent!.JobId);
        Assert.Equal(50, progressEvent.Percent);
    }

    [Fact]
    public async Task Start_EmitsFailed_WhenWorkThrows()
    {
        var sink = new RecordingSink();
        var manager = new JobManager(sink.Send);

        manager.Start((ct, progress) => throw new InvalidOperationException("boom"));

        Assert.True(await WaitAsync(sink.Terminal, 5000));
        var failed = Assert.IsType<JobFailed>(LastPayload(sink, "job.failed"));
        Assert.False(failed.Cancelled);
        Assert.Contains("boom", failed.Error);
    }

    [Fact]
    public async Task Cancel_EmitsFailedWithCancelledFlag()
    {
        var sink = new RecordingSink();
        var manager = new JobManager(sink.Send);

        string jobId = manager.Start(async (ct, progress) =>
        {
            await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
            return null;
        });

        Assert.True(manager.Cancel(jobId));

        Assert.True(await WaitAsync(sink.Terminal, 5000));
        var failed = Assert.IsType<JobFailed>(LastPayload(sink, "job.failed"));
        Assert.True(failed.Cancelled);
    }

    [Fact]
    public void Cancel_ReturnsFalse_ForUnknownJob()
    {
        var manager = new JobManager((_, __) => { });
        Assert.False(manager.Cancel("does-not-exist"));
    }

    private static async Task<bool> WaitAsync(Task task, int milliseconds)
    {
        return await Task.WhenAny(task, Task.Delay(milliseconds)).ConfigureAwait(false) == task;
    }

    private static object? LastPayload(RecordingSink sink, string method)
    {
        IReadOnlyList<(string Method, object? Payload)> events = sink.Snapshot();
        for (int i = events.Count - 1; i >= 0; i--)
        {
            if (events[i].Method == method) return events[i].Payload;
        }
        return null;
    }

    private static T? FindPayload<T>(RecordingSink sink, string method) where T : class
    {
        foreach (var (eventMethod, payload) in sink.Snapshot())
        {
            if (eventMethod == method && payload is T typed) return typed;
        }
        return null;
    }

    private sealed class RecordingSink
    {
        private readonly object gate = new();
        private readonly List<(string Method, object? Payload)> events = new();
        private readonly TaskCompletionSource<bool> terminal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Terminal => terminal.Task;

        public void Send(string method, object? payload)
        {
            lock (gate) { events.Add((method, payload)); }
            if (method == "job.completed" || method == "job.failed")
            {
                terminal.TrySetResult(true);
            }
        }

        public IReadOnlyList<(string Method, object? Payload)> Snapshot()
        {
            lock (gate) { return events.ToArray(); }
        }
    }
}
