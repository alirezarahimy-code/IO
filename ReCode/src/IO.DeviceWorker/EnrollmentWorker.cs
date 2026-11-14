using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

/// <summary>Separate enrollment queue; all endpoints retain the shared durable per-device exclusion fence.</summary>
internal sealed class EnrollmentWorker(IServiceScopeFactory scopes,WorkerConfiguration configuration):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!OperatingSystem.IsWindows())return;
        while(!stoppingToken.IsCancellationRequested)
        {
            try
            {
                RemoteEnrollments.Claim? claim;
                await using(var scope=scopes.CreateAsyncScope())
                {
                    var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();
                    if(!await db.RemoteEnrollments.AnyAsync(x=>x.State=="pending"||x.State=="running",stoppingToken)){await Task.Delay(1000,stoppingToken);continue;}
                    claim=await scope.ServiceProvider.GetRequiredService<RemoteEnrollments>().ClaimAsync(configuration.ServiceUserId,stoppingToken);
                }
                if(claim is null){await Task.Delay(1000,stoppingToken);continue;}
                var result=await Enroll(claim,stoppingToken);
                await using var finish=scopes.CreateAsyncScope();
                await finish.ServiceProvider.GetRequiredService<RemoteEnrollments>().CompleteAsync(configuration.ServiceUserId,claim,result,stoppingToken);
            }
            catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){return;}
            catch(Exception)
            {
                // Lost authority/result commit is recovered as UNKNOWN by lease expiry, never replayed.
                Console.Error.WriteLine("Enrollment unavailable; review service authority and durable job state. No automatic replay.");
                try{await Task.Delay(2000,stoppingToken);}catch(OperationCanceledException){return;}
            }
        }
    }
    private async Task<RemoteEnrollOutput> Enroll(RemoteEnrollments.Claim claim,CancellationToken ct)
    {
        var native=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
        if(native is null||native.ExpectedSerial!=claim.Approval.Serial||native.EnrollmentApprovals?.Count(x=>x==claim.Approval)!=1)return new("unsupported");
        if(claim.Port!=native.Port||!System.Net.IPAddress.TryParse(claim.Host,out var address)||!System.Net.IPAddress.Parse(native.Address).Equals(address))return new("failed-before-dispatch");
        DeviceCommunicationKey? key;
        await using(var scope=scopes.CreateAsyncScope())key=await scope.ServiceProvider.GetRequiredService<RemoteEnrollments>().CredentialAsync(configuration.ServiceUserId,claim,native.AllowDefaultCommunicationKey,ct);
        if(key is null)return new("failed-before-dispatch");
        var input=new RemoteEnrollInput(new(claim.DeviceId,native.Address,native.Port,native.MachineNumber,native.ExpectedSerial,key.Value),claim.Id,claim.PersonIdentifier,claim.Approval,Math.Min(45,claim.TimeoutSeconds-5));
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var process=NativeProcessSupervisor.RunEnrollAsync(configuration.DotnetPath!,configuration.BridgePath!,input,claim.TimeoutSeconds,stop.Token);
        while(!process.IsCompleted)
        {
            var delay=Task.Delay(500,ct);if(await Task.WhenAny(process,delay)==process)break;
            bool shouldStop;
            try{await using var scope=scopes.CreateAsyncScope();shouldStop=await scope.ServiceProvider.GetRequiredService<RemoteEnrollments>().ShouldStopAsync(configuration.ServiceUserId,claim,ct);}
            catch{shouldStop=true;}
            if(shouldStop||ct.IsCancellationRequested){await stop.CancelAsync();break;}
        }
        return await process;
    }
}
