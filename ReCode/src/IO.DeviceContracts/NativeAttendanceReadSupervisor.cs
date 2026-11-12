using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace IO.DeviceContracts;

/// <summary>Bounded frames, not a bounded number of records. Backpressure goes through the pipe.
/// An incomplete stream never becomes a complete extraction. Callbacks must honour their token.</summary>
public static class NativeAttendanceReadSupervisor
{
    public sealed record Result(string State,long Count);
    public static async Task<Result> RunAsync(string executable,string assembly,ZkAttendanceReadInput input,int seconds,
        Func<IReadOnlyList<ZkAttendanceRaw>,CancellationToken,Task> accept,CancellationToken ct=default)
    {
        if(!input.IsValid()||seconds is <1 or >600||!Path.IsPathFullyQualified(executable)||!Path.IsPathFullyQualified(assembly)||!File.Exists(executable)||!File.Exists(assembly))return new("unsupported",0);
        if(ct.IsCancellationRequested)return new("interrupted",0);
        using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);deadline.CancelAfter(TimeSpan.FromSeconds(seconds));
        var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(assembly)!};
        start.ArgumentList.Add(assembly);start.Environment.Clear();foreach(var key in new[]{"SystemRoot","WINDIR","TEMP","TMP"})if(Environment.GetEnvironmentVariable(key) is {} value)start.Environment[key]=value;
        using var process=new Process{StartInfo=start};long count=0;Task? interaction=null;string state="incomplete";
        try
        {
            if(!process.Start())return new("bridge-failed",count);
            async Task Send(){await process.StandardInput.WriteAsync(JsonSerializer.Serialize(input).AsMemory(),deadline.Token);process.StandardInput.Close();}
            async Task Error(){var b=new char[256];int length=0,n;while((n=await process.StandardError.ReadAsync(b.AsMemory(),deadline.Token))>0)if((length+=n)>4096)throw new InvalidDataException();}
            async Task Frames()
            {
                var started=false;var ended=false;var batch=new List<ZkAttendanceRaw>(50);
                while(await ReadFrame(process.StandardOutput,deadline.Token) is {} line)
                {
                    if(ended)throw new InvalidDataException();
                    var frame=JsonSerializer.Deserialize<ZkAttendanceFrame>(line,new JsonSerializerOptions{UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow})??throw new InvalidDataException();
                    if(frame.Type=="start"&&!started&&frame.Serial==input.Connection.ExpectedSerial&&frame.Record is null&&frame.Count is null&&frame.State is null){started=true;continue;}
                    if(frame.Type=="record"&&started&&frame.Serial is null&&frame.Count is null&&frame.State is null&&frame.Record?.IsBounded()==true)
                    {
                        count=checked(count+1);batch.Add(frame.Record);if(batch.Count==50){await accept(batch.ToArray(),deadline.Token);batch.Clear();}continue;
                    }
                    if(frame.Type=="end"&&frame.Record is null&&frame.Serial is null&&frame.Count==count&&
                        ((started&&frame.State is "succeeded" or "incomplete" or "failed")||(!started&&count==0&&frame.State is "unsupported" or "identity-mismatch" or "failed")))
                    {
                        if(batch.Count>0){await accept(batch.ToArray(),deadline.Token);batch.Clear();}state=frame.State!;ended=true;continue;
                    }
                    throw new InvalidDataException();
                }
                if(!ended)throw new InvalidDataException();
            }
            interaction=Task.WhenAll(Send(),Error(),Frames(),process.WaitForExitAsync(deadline.Token));await interaction.WaitAsync(deadline.Token);
            return new(process.ExitCode==0?state:"bridge-failed",count);
        }
        catch(OperationCanceledException){return new(ct.IsCancellationRequested?"interrupted":"timed-out",count);}
        catch{return new("bridge-failed",count);}
        finally
        {
            try{if(process.Id>0&&!process.HasExited)process.Kill(entireProcessTree:true);}catch{}
            if(interaction is not null)_=interaction.ContinueWith(t=>{_=t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
        }
    }
    private static async Task<string?> ReadFrame(StreamReader reader,CancellationToken ct)
    {
        // ReadLineAsync alone allocates an unbounded line before a length check.
        var text=new StringBuilder();var buffer=new char[1];
        while(await reader.ReadAsync(buffer.AsMemory(),ct)>0){var c=buffer[0];if(c=='\n')return text.ToString().TrimEnd('\r');if(text.Length>=4096)throw new InvalidDataException();text.Append(c);}
        return text.Length==0?null:throw new InvalidDataException();
    }
}
