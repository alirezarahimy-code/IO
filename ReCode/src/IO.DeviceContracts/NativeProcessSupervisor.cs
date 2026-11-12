using System.Diagnostics;
using System.Text;

namespace IO.DeviceContracts;

/// <summary>One native request per child. Deadline kills the child, not just its awaiting thread.
/// No shell, no secret arguments, bounded stdout/stderr; no automatic replay after unknown completion.</summary>
public static class NativeProcessSupervisor
{
    public static async Task<string> RunAsync(string executable, string childAssembly, ZkProbeInput input, int timeoutSeconds, CancellationToken stoppingToken = default)
    {
        if (!input.IsValid()) return "bridge-failed";
        var exchange=await ExchangeAsync(executable,childAssembly,System.Text.Json.JsonSerializer.Serialize(input),timeoutSeconds,stoppingToken);
        return exchange.State=="exited"&&ZkProbeResults.IsKnown(exchange.Output) ? exchange.Output : exchange.State=="exited"?"bridge-failed":exchange.State;
    }

    public static async Task<ZkOperationOutput> RunOperationAsync(string executable,string childAssembly,ZkOperationInput input,int timeoutSeconds,CancellationToken stoppingToken=default)
    {
        if(!input.IsValid())return new("unsupported");
        var exchange=await ExchangeAsync(executable,childAssembly,System.Text.Json.JsonSerializer.Serialize(input),timeoutSeconds,stoppingToken);
        if(exchange.State!="exited")return new(exchange.State);
        try
        {
            var result=System.Text.Json.JsonSerializer.Deserialize<ZkOperationOutput>(exchange.Output,new System.Text.Json.JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});
            return result is not null&&result.IsValidFor(input)?result:new("bridge-failed");
        }
        catch(System.Text.Json.JsonException){return new("bridge-failed");}
    }

    private sealed record Exchange(string State,string Output="");
    private static async Task<Exchange> ExchangeAsync(string executable,string childAssembly,string json,int timeoutSeconds,CancellationToken stoppingToken)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.IsPathFullyQualified(childAssembly) || !File.Exists(executable) || !File.Exists(childAssembly) ||
            json.Length > 8192 || timeoutSeconds is < 1 or > 60) return new("bridge-failed");
        if (stoppingToken.IsCancellationRequested) return new("interrupted");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true,
            RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(childAssembly)! };
        start.ArgumentList.Add(childAssembly);
        // Child cannot inherit database/DP configuration or user-defined profiler/startup-hook injection.
        start.Environment.Clear();
        foreach (var key in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP" })
            if (Environment.GetEnvironmentVariable(key) is { } value) start.Environment[key] = value;
        using var process = new Process { StartInfo = start };
        Task? interaction = null;
        string? output = null;
        try
        {
            if (!process.Start()) return new("bridge-failed");
            async Task Send()
            {
                await process.StandardInput.WriteAsync(json.AsMemory(), deadline.Token);
                process.StandardInput.Close();
            }
            async Task ReadOutput() => output = await ReadBounded(process.StandardOutput, deadline.Token);
            interaction = Task.WhenAll(Send(), ReadOutput(), ReadBounded(process.StandardError, deadline.Token), process.WaitForExitAsync(deadline.Token));
            await interaction.WaitAsync(deadline.Token);
            var result = output?.Trim() ?? "";
            return process.ExitCode==0?new("exited",result):new("bridge-failed");
        }
        catch (OperationCanceledException) { return new(stoppingToken.IsCancellationRequested ? "interrupted" : "timed-out"); }
        catch (Exception) { return new("bridge-failed"); }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception) { /* No raw process exception or secret-bearing configuration escapes. */ }
            // Observe eventual errors without waiting indefinitely for a broken native process.
            if (interaction is not null)
                _ = interaction.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken ct)
    {
        var text = new StringBuilder(); var buffer = new char[512];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (text.Length + count > 4096) throw new InvalidDataException("Child output limit exceeded.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
