using System.Net.Sockets;

namespace IO.Infrastructure.Devices;

/// <summary>Enforces an outer deadline even when a connector fails to observe cancellation.
/// Caller still owns/disposes the transport; native synchronous SDK calls require a separate process.</summary>
public static class TcpProbeDeadline
{
    public static async Task<string> RunAsync(Func<CancellationToken,Task> connect, int timeoutSeconds, CancellationToken stoppingToken = default)
    {
        if (timeoutSeconds is < 1 or > 60) throw new ArgumentException("Invalid timeout.");
        if (stoppingToken.IsCancellationRequested) return "interrupted";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        Task? pending = null;
        try
        {
            pending = connect(timeout.Token);
            await pending.WaitAsync(timeout.Token);
            timeout.Token.ThrowIfCancellationRequested();
            return "tcp-reachable";
        }
        catch (OperationCanceledException) { return stoppingToken.IsCancellationRequested ? "interrupted" : "timed-out"; }
        catch (SocketException) { return stoppingToken.IsCancellationRequested ? "interrupted" : timeout.IsCancellationRequested ? "timed-out" : "unreachable"; }
        finally
        {
            if (pending is not null)
                _ = pending.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }
}
