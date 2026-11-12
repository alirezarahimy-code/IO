namespace IO.DeviceContracts;

/// <summary>Task.WhenAll alone waits for every task even after a frame/parser
/// faults. The child can then block forever on the pipe nobody is reading.
/// Fail promptly so the owning supervisor's finally can kill that child.
/// Observe aggregate faults even when we leave on the first individual fault.</summary>
internal static class NativeInteraction
{
    public static async Task RunAsync(params Task[] tasks)
    {
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var task in tasks)
            _ = task.ContinueWith(completed =>
            {
                if (!completed.IsCompletedSuccessfully) failed.TrySetResult();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var all = Task.WhenAll(tasks);
        try
        {
            if (await Task.WhenAny(all, failed.Task) == failed.Task)
                foreach (var task in tasks)
                    if (task.IsFaulted || task.IsCanceled) await task;
            await all;
        }
        finally
        {
            _ = all.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }
}
