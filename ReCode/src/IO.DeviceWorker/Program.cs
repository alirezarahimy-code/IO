using IO.Infrastructure.Devices;
using IO.DeviceContracts;
using Microsoft.AspNetCore.DataProtection;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// A separate process, never a COM/native library inside the web host.
var builder = Host.CreateApplicationBuilder(args);
var connection = builder.Configuration.GetConnectionString("IO");
if (string.IsNullOrWhiteSpace(connection) || !Guid.TryParse(builder.Configuration["IO:DeviceWorker:ServiceUserId"], out var service) || service == Guid.Empty)
    throw new InvalidOperationException("An explicit database connection and provisioned service identity are required.");
var targets = builder.Configuration.GetSection("IO:DeviceWorker:AllowedEndpoints").Get<TcpConnectionDiagnostic.AllowedEndpoint[]>() ?? [];
if (targets.Any(t => t.DeviceId == Guid.Empty || !System.Net.IPAddress.TryParse(t.Address, out _) || t.Port is < 1 or > 65535))
    throw new InvalidOperationException("Invalid pinned diagnostic endpoint.");
var native = builder.Configuration.GetSection("IO:DeviceWorker:NativeEndpoints").Get<NativeEndpoint[]>() ?? [];
var dotnetPath = builder.Configuration["IO:DeviceWorker:BridgeDotnetPath"];
var bridgePath = builder.Configuration["IO:DeviceWorker:BridgeAssemblyPath"];
if (native.Any(t => !new ZkProbeInput(t.DeviceId,t.Address,t.Port,t.MachineNumber,t.ExpectedSerial,0).IsValid()) ||
    targets.Select(x=>x.DeviceId).Concat(native.Select(x=>x.DeviceId)).Distinct().Count() != targets.Length+native.Length)
    throw new InvalidOperationException("Invalid or ambiguous native endpoint pin.");
if (native.Length > 0)
{
    var keyRing = builder.Configuration["IO:DataProtection:KeyRingPath"];
    var keyIdentity = builder.Configuration["IO:DataProtection:ApplicationName"];
    if (string.IsNullOrWhiteSpace(dotnetPath) || string.IsNullOrWhiteSpace(bridgePath) || !Path.IsPathFullyQualified(dotnetPath) || !Path.IsPathFullyQualified(bridgePath) ||
        !File.Exists(dotnetPath) || !File.Exists(bridgePath) || string.IsNullOrWhiteSpace(keyRing) || !Path.IsPathFullyQualified(keyRing) || !Directory.Exists(keyRing) || string.IsNullOrWhiteSpace(keyIdentity))
        throw new InvalidOperationException("Native bridge requires explicit trusted executable paths and a shared existing key-ring identity.");
    builder.Services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(keyRing)).SetApplicationName(keyIdentity).DisableAutomaticKeyGeneration();
}
builder.Logging.ClearProviders(); // No EF/provider exception strings or configuration secrets in stdout.
builder.Services.AddIoIdentityStorage(connection);
builder.Services.AddSingleton(new WorkerConfiguration(service, targets, native, dotnetPath, bridgePath, builder.Configuration.GetValue<bool>("IO:DeviceWorker:EnableOperations"),builder.Configuration.GetValue<bool>("IO:DeviceWorker:EnableUserDownloads")));
builder.Services.AddHostedService<DiagnosticWorker>();
await builder.Build().RunAsync();

internal sealed record NativeEndpoint(Guid DeviceId, string Address, int Port, int MachineNumber, string ExpectedSerial, bool AllowDefaultCommunicationKey, bool AllowClockSet=false,bool AllowUserRead=false);
internal sealed record WorkerConfiguration(Guid ServiceUserId, TcpConnectionDiagnostic.AllowedEndpoint[] AllowedEndpoints, NativeEndpoint[] NativeEndpoints, string? DotnetPath, string? BridgePath, bool EnableOperations,bool EnableUserDownloads);
internal sealed class DiagnosticWorker(IServiceScopeFactory scopes, WorkerConfiguration configuration) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Loop(stoppingToken)));

    private async Task Loop(CancellationToken ct)
    {
        int queueCursor=0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                DeviceConnections.Claim? claim=null;DeviceOperations.Claim? operation=null;DeviceUserDownloads.Claim? users=null;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();var now=DateTimeOffset.UtcNow;var cutoff=now.AddMinutes(-2);
                    var available=await db.DeviceConnections.AnyAsync(x=>x.State=="pending"||(x.State=="running"&&x.ClaimedAtUtc<cutoff),ct);
                    var operationsAvailable=configuration.EnableOperations&&await db.DeviceOperations.AnyAsync(x=>x.State=="pending"||(x.State=="running"&&x.ClaimedAtUtc<cutoff),ct);
                    var usersAvailable=configuration.EnableUserDownloads&&await db.DeviceUserDownloads.AnyAsync(x=>x.State=="pending"||(x.State=="running"&&x.LeaseAtUtc!=null&&x.LeaseAtUtc.Value.AddSeconds(x.TimeoutSeconds+120)<now),ct);
                    var queues=new[]{usersAvailable,operationsAvailable,available};int selected=-1;
                    for(int i=0;i<3;i++){var candidate=(queueCursor+i)%3;if(queues[candidate]){selected=candidate;break;}}
                    // Advance even if the selected independent service grant was revoked.
                    queueCursor=(queueCursor+1)%3;
                    if(selected==0)users=await scope.ServiceProvider.GetRequiredService<DeviceUserDownloads>().ClaimAsync(configuration.ServiceUserId,ct);
                    else if(selected==1)operation=await scope.ServiceProvider.GetRequiredService<DeviceOperations>().ClaimAsync(configuration.ServiceUserId,ct);
                    else if(selected==2)claim=await scope.ServiceProvider.GetRequiredService<DeviceConnections>().ClaimAsync(configuration.ServiceUserId,ct);
                }
                if(users is not null)
                {
                    var result=await DownloadUsers(users,ct);await using var finish=scopes.CreateAsyncScope();
                    await finish.ServiceProvider.GetRequiredService<DeviceUserDownloads>().CompleteAsync(configuration.ServiceUserId,users.Id,users.Token,result.State,result.Count,ct);
                }
                else if(operation is not null)
                {
                    var result=await Operate(operation,ct);await using var finish=scopes.CreateAsyncScope();
                    await finish.ServiceProvider.GetRequiredService<DeviceOperations>().CompleteAsync(configuration.ServiceUserId,operation.Id,operation.Token,result,ct);
                }
                else if(claim is not null)
                {
                    var result=await Probe(claim,ct);await using var finish=scopes.CreateAsyncScope();
                    await finish.ServiceProvider.GetRequiredService<DeviceConnections>().CompleteAsync(configuration.ServiceUserId,claim.Id,claim.Token,result,ct);
                }
                else await Task.Delay(TimeSpan.FromSeconds(3),ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (Exception)
            {
                // Failure of one test or revoked service grant must not terminate the other slots.
                // Claimed-but-uncommitted results become interrupted via the durable expiry rule.
                Console.Error.WriteLine("Device diagnostic unavailable; check database, service grant and configuration. No automatic command replay.");
                try { await Task.Delay(TimeSpan.FromSeconds(5), ct); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            }
        }
    }
    private async Task<string> Probe(DeviceConnections.Claim claim, CancellationToken ct)
    {
        var native = configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
        if (native is null) return await TcpConnectionDiagnostic.RunAsync(claim, configuration.AllowedEndpoints, ct);
        if (claim.Profile != "zk-standalone") return "unsupported";
        if (claim.Port!=native.Port || !System.Net.IPAddress.TryParse(claim.Host,out var address) || !System.Net.IPAddress.Parse(native.Address).Equals(address)) return "endpoint-denied";
        if (!OperatingSystem.IsWindows()) return "unsupported";
        await using var scope = scopes.CreateAsyncScope();
        var credential = await scope.ServiceProvider.GetRequiredService<DeviceConnectionCredential>().LoadAsync(configuration.ServiceUserId,claim,native.AllowDefaultCommunicationKey,ct);
        if (credential is null) return "credential-unavailable";
        var result = await NativeProcessSupervisor.RunAsync(configuration.DotnetPath!,configuration.BridgePath!,
            new ZkProbeInput(claim.DeviceId,native.Address,native.Port,native.MachineNumber,native.ExpectedSerial,credential.Value),claim.TimeoutSeconds,ct);
        return result == "invalid-input" ? "bridge-failed" : result;
    }

    private async Task<NativeUserReadSupervisor.Result> DownloadUsers(DeviceUserDownloads.Claim claim,CancellationToken ct)
    {
        var native=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
        if(native is null||!native.AllowUserRead||!OperatingSystem.IsWindows())return new("unsupported",0);
        if(claim.Port!=native.Port||!System.Net.IPAddress.TryParse(claim.Host,out var address)||!System.Net.IPAddress.Parse(native.Address).Equals(address))return new("endpoint-denied",0);
        DeviceCommunicationKey? credential;
        await using(var scope=scopes.CreateAsyncScope())credential=await scope.ServiceProvider.GetRequiredService<DeviceUserDownloads>().CredentialAsync(configuration.ServiceUserId,claim,native.AllowDefaultCommunicationKey,ct);
        if(credential is null)return new("credential-unavailable",0);
        long cursor=0;
        return await NativeUserReadSupervisor.RunAsync(configuration.DotnetPath!,configuration.BridgePath!,new(new(claim.DeviceId,native.Address,native.Port,native.MachineNumber,native.ExpectedSerial,credential.Value),true,native.AllowUserRead),claim.TimeoutSeconds,async(batch,token)=>
        {
            await using var scope=scopes.CreateAsyncScope();cursor=await scope.ServiceProvider.GetRequiredService<DeviceUserDownloads>().AppendAsync(configuration.ServiceUserId,claim.Id,claim.Token,cursor,batch,token);
        },ct);
    }

    private async Task<ZkOperationOutput> Operate(DeviceOperations.Claim claim,CancellationToken ct)
    {
        var native=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
        if(native is null||claim.Profile!="zk-standalone")return new("unsupported");
        if(claim.Port!=native.Port||!System.Net.IPAddress.TryParse(claim.Host,out var address)||!System.Net.IPAddress.Parse(native.Address).Equals(address))return new("endpoint-denied");
        if(!OperatingSystem.IsWindows()||(claim.Kind=="clock.set"&&!native.AllowClockSet))return new("unsupported");
        await using var scope=scopes.CreateAsyncScope();
        var credential=await scope.ServiceProvider.GetRequiredService<DeviceOperations>().CredentialAsync(configuration.ServiceUserId,claim,native.AllowDefaultCommunicationKey,ct);
        if(credential is null)return new("credential-unavailable");
        return await NativeProcessSupervisor.RunOperationAsync(configuration.DotnetPath!,configuration.BridgePath!,
            new(new(claim.DeviceId,native.Address,native.Port,native.MachineNumber,native.ExpectedSerial,credential.Value),claim.Kind,claim.ClockLocal,native.AllowClockSet),claim.TimeoutSeconds,ct);
    }

}
