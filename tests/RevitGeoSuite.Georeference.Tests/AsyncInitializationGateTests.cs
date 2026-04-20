using System;
using System.Threading;
using System.Threading.Tasks;
using RevitGeoSuite.SharedUI.Controls;
using Xunit;

namespace RevitGeoSuite.Georeference.Tests;

public sealed class AsyncInitializationGateTests
{
    [Fact]
    public async Task RunAsync_shares_in_flight_initialization_and_completes_once()
    {
        AsyncInitializationGate gate = new AsyncInitializationGate();
        int invocationCount = 0;
        TaskCompletionSource<bool> release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Task InitializeAsync()
        {
            Interlocked.Increment(ref invocationCount);
            return release.Task;
        }

        Task first = gate.RunAsync(InitializeAsync);
        Task second = gate.RunAsync(InitializeAsync);

        Assert.Same(first, second);
        Assert.Equal(1, Volatile.Read(ref invocationCount));

        release.SetResult(true);
        await Task.WhenAll(first, second);

        await gate.RunAsync(InitializeAsync);
        Assert.Equal(1, Volatile.Read(ref invocationCount));
    }

    [Fact]
    public async Task RunAsync_allows_retry_after_failed_initialization()
    {
        AsyncInitializationGate gate = new AsyncInitializationGate();
        int invocationCount = 0;

        await Assert.ThrowsAsync<InvalidOperationException>(() => gate.RunAsync(async () =>
        {
            Interlocked.Increment(ref invocationCount);
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }));

        await gate.RunAsync(() =>
        {
            Interlocked.Increment(ref invocationCount);
            return Task.CompletedTask;
        });

        Assert.Equal(2, Volatile.Read(ref invocationCount));
    }
}
