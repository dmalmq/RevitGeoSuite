using System;
using System.Threading.Tasks;

namespace RevitGeoSuite.SharedUI.Controls;

internal sealed class AsyncInitializationGate
{
    private readonly object syncRoot = new object();
    private Task? inFlightTask;
    private bool isCompleted;

    public Task RunAsync(Func<Task> operation)
    {
        if (operation is null)
        {
            throw new ArgumentNullException(nameof(operation));
        }

        lock (syncRoot)
        {
            if (isCompleted)
            {
                return Task.CompletedTask;
            }

            return inFlightTask ??= RunCoreAsync(operation);
        }
    }

    private async Task RunCoreAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(false);

            lock (syncRoot)
            {
                isCompleted = true;
                inFlightTask = null;
            }
        }
        catch
        {
            lock (syncRoot)
            {
                inFlightTask = null;
            }

            throw;
        }
    }
}
