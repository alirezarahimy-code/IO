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
        var exchange=await ExchangeAsync(executable,childAssembly,System.Text.Json.JsonSerializer.Serialize(input),timeoutSeconds,stoppingToken,input.Operation=="settings.read"?32768:(input.Operation=="settings.raw.set"||input.Operation=="settings.key.set"||input.Operation=="settings.profile.set"||input.Operation=="settings.profile.inspect")?8192:4096,(input.Operation=="settings.read"||input.Operation=="settings.raw.set"||input.Operation=="settings.key.set"||input.Operation=="settings.profile.set"||input.Operation=="settings.profile.inspect")?32768:8192);
        if(exchange.State!="exited")return new((input.Operation=="device.restart"||input.Operation=="network.ip.set"||input.Operation=="settings.idle.set"||input.Operation=="settings.raw.set"||input.Operation=="settings.key.set"||input.Operation=="settings.profile.set")?"unknown":exchange.State);
        try
        {
            var result=System.Text.Json.JsonSerializer.Deserialize<ZkOperationOutput>(exchange.Output,new System.Text.Json.JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});
            return result is not null&&result.IsValidFor(input)?result:new((input.Operation=="device.restart"||input.Operation=="network.ip.set"||input.Operation=="settings.idle.set"||input.Operation=="settings.raw.set"||input.Operation=="settings.key.set"||input.Operation=="settings.profile.set")?"unknown":"bridge-failed");
        }
        catch(System.Text.Json.JsonException){return new((input.Operation=="device.restart"||input.Operation=="network.ip.set"||input.Operation=="settings.idle.set"||input.Operation=="settings.raw.set"||input.Operation=="settings.key.set"||input.Operation=="settings.profile.set")?"unknown":"bridge-failed");}
    }

    public static async Task<ZkUserWriteOutput> RunUserWriteAsync(string executable,string childAssembly,ZkUserWriteInput input,int timeoutSeconds,CancellationToken stoppingToken=default)
    {
        if(!input.IsValid())return new("unsupported");
        if(stoppingToken.IsCancellationRequested)return new("failed-before-write");
        var exchange=await ExchangeAsync(executable,childAssembly,ZkUserWriteWire.Serialize(input),timeoutSeconds,stoppingToken);
        // After dispatch even missing/invalid output or process kill is UNKNOWN,
        // never evidence that retrying a write is safe.
        if(exchange.State!="exited")return new("unknown");
        try
        {
            var result=System.Text.Json.JsonSerializer.Deserialize<ZkUserWriteOutput>(exchange.Output,new System.Text.Json.JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});
            return result?.IsValid()==true?result:new("unknown");
        }
        catch(System.Text.Json.JsonException){return new("unknown");}
    }

    public static async Task<RemoteEnrollOutput> RunEnrollAsync(string executable,string childAssembly,RemoteEnrollInput input,int timeoutSeconds,CancellationToken ct=default)
    {
        if(!input.IsValid()||timeoutSeconds<input.SubjectWaitSeconds+5||timeoutSeconds>60)return new("unsupported");
        if(ct.IsCancellationRequested)return new("cancelled-before-dispatch");
        var exchange=await ExchangeAsync(executable,childAssembly,System.Text.Json.JsonSerializer.Serialize(input),timeoutSeconds,ct);
        if(exchange.State!="exited")return new("unknown");
        try
        {
            var result=System.Text.Json.JsonSerializer.Deserialize<RemoteEnrollOutput>(exchange.Output,new System.Text.Json.JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});
            return result?.IsValidFor(input)==true?result:new("unknown");
        }
        catch(System.Text.Json.JsonException){return new("unknown");}
    }

    public static async Task<BiometricTransferOutput> RunBiometricTransferAsync(string executable,string childAssembly,BiometricTransferInput input,int timeoutSeconds,CancellationToken ct=default,BiometricPhotoStaging? photoStaging=null)
    {
        var failure=input.InspectOnly?"inspection-failed":"unknown";
        if(!input.IsValid(false)||timeoutSeconds is <1 or >60)return new(input.OperationId,input.InspectOnly?"inspection-failed":"unsupported");
        if(ct.IsCancellationRequested)return new(input.OperationId,input.InspectOnly?"inspection-failed":"failed-before-write");
        bool dispatched=false;
        BiometricPhotoLease? staging=null;
        BiometricTransferOutput result=new(input.OperationId,failure);
        try
        {
            if(input.Approval.Kind=="photo")
            {
                if(!OperatingSystem.IsWindows()||photoStaging is null||photoStaging.StoragePolicyReference!=input.Approval.PhotoHandlingReference)return new(input.OperationId,input.InspectOnly?"inspection-failed":"failed-before-write");
                staging=BiometricPhotoLease.Create(photoStaging,timeoutSeconds);input=input with{PhotoWorkspace=staging.Workspace};
            }
            string wire=input.InspectOnly?System.Text.Json.JsonSerializer.Serialize(new BiometricInspectionInput(input)):System.Text.Json.JsonSerializer.Serialize(input);
            dispatched=true;
            var exchange=await ExchangeAsync(executable,childAssembly,wire,timeoutSeconds,ct,4096,32768);
            if(exchange.State=="exited")
            {
                var observed=System.Text.Json.JsonSerializer.Deserialize<BiometricTransferOutput>(exchange.Output,new System.Text.Json.JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});
                if(observed?.IsValidFor(input)==true)result=observed;
            }
        }
        catch{result=new(input.OperationId,!dispatched&&!input.InspectOnly?"failed-before-write":failure);}
        finally{if(OperatingSystem.IsWindows())staging?.Dispose();}
        return OperatingSystem.IsWindows()&&staging is not null&&!staging.Cleaned?new(input.OperationId,failure):result;
    }

    public static async Task<AttendanceEraseOutput> RunAttendanceEraseAsync(string executable,string assembly,AttendanceEraseInput input,int seconds,CancellationToken ct=default)
    {
        if(!input.IsValid()||seconds is <1 or >60)return new(input.OperationId,"unsupported");
        if(ct.IsCancellationRequested)return new(input.OperationId,"failed-before-write");
        var exchange=await ExchangeAsync(executable,assembly,System.Text.Json.JsonSerializer.Serialize(input),seconds,ct,4096,16384);
        if(exchange.State!="exited")return new(input.OperationId,"unknown");
        try{var result=System.Text.Json.JsonSerializer.Deserialize<AttendanceEraseOutput>(exchange.Output,new System.Text.Json.JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});return result?.IsValidFor(input)==true?result:new(input.OperationId,"unknown");}
        catch(System.Text.Json.JsonException){return new(input.OperationId,"unknown");}
    }

    public static async Task<AttendanceEraseOutput> RunAttendanceRangeEraseAsync(string executable,string assembly,AttendanceRangeEraseInput input,int seconds,CancellationToken ct=default)
    {
        if(!input.IsValid()||seconds is <1 or >60)return new(input.OperationId,"unsupported");
        if(ct.IsCancellationRequested)return new(input.OperationId,"failed-before-write");
        var exchange=await ExchangeAsync(executable,assembly,System.Text.Json.JsonSerializer.Serialize(input),seconds,ct,4096,16384);
        if(exchange.State!="exited")return new(input.OperationId,"unknown");
        try{var result=System.Text.Json.JsonSerializer.Deserialize<AttendanceEraseOutput>(exchange.Output,new System.Text.Json.JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});return result?.OperationId==input.OperationId&&AttendanceRangeEraseExecutor.Known(result.State)?result:new(input.OperationId,"unknown");}
        catch(System.Text.Json.JsonException){return new(input.OperationId,"unknown");}
    }

    private sealed record Exchange(string State,string Output="");
    private static async Task<Exchange> ExchangeAsync(string executable,string childAssembly,string json,int timeoutSeconds,CancellationToken stoppingToken,int maxOutputChars=4096,int maxInputChars=8192)
    {
        if (!Path.IsPathFullyQualified(executable) || !Path.IsPathFullyQualified(childAssembly) || !File.Exists(executable) || !File.Exists(childAssembly) ||
            json.Length > maxInputChars || timeoutSeconds is < 1 or > 60) return new("bridge-failed");
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
            async Task ReadOutput() => output = await ReadBounded(process.StandardOutput, deadline.Token,maxOutputChars);
            interaction = NativeInteraction.RunAsync(Send(), ReadOutput(), ReadBounded(process.StandardError, deadline.Token), process.WaitForExitAsync(deadline.Token));
            await interaction.WaitAsync(deadline.Token);
            var result = output?.Trim() ?? "";
            return process.ExitCode==0?new("exited",result):new("bridge-failed");
        }
        catch (OperationCanceledException) { return new(stoppingToken.IsCancellationRequested ? "interrupted" : "timed-out"); }
        catch (Exception) { return new("bridge-failed"); }
        finally
        {
            try { if (process.Id > 0 && !process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5)); } }
            catch (Exception) { /* No raw process exception or secret-bearing configuration escapes. */ }
            // Observe eventual errors without waiting indefinitely for a broken native process.
            if (interaction is not null)
                _ = interaction.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously | TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }
    }

    private static async Task<string> ReadBounded(StreamReader reader, CancellationToken ct,int maxChars=4096)
    {
        var text = new StringBuilder(); var buffer = new char[512];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), ct)) > 0)
        {
            if (text.Length + count > maxChars) throw new InvalidDataException("Child output limit exceeded.");
            text.Append(buffer, 0, count);
        }
        return text.ToString();
    }
}
