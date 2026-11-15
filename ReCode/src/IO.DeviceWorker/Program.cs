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
// SCM launches services with a system working directory. Always resolve deployed
// configuration relative to the trusted published binary, not System32 or caller cwd.
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args=args,ContentRootPath=AppContext.BaseDirectory });
if(OperatingSystem.IsWindows())builder.Services.AddWindowsService(options=>options.ServiceName="IODeviceWorker");
var connection = builder.Configuration.GetConnectionString("IO");
if (string.IsNullOrWhiteSpace(connection) || !Guid.TryParse(builder.Configuration["IO:DeviceWorker:ServiceUserId"], out var service) || service == Guid.Empty)
    throw new InvalidOperationException("An explicit database connection and provisioned service identity are required.");
var targets = builder.Configuration.GetSection("IO:DeviceWorker:AllowedEndpoints").Get<TcpConnectionDiagnostic.AllowedEndpoint[]>() ?? [];
if (targets.Any(t => t.DeviceId == Guid.Empty || !System.Net.IPAddress.TryParse(t.Address, out _) || t.Port is < 1 or > 65535))
    throw new InvalidOperationException("Invalid pinned diagnostic endpoint.");
var native = builder.Configuration.GetSection("IO:DeviceWorker:NativeEndpoints").Get<NativeEndpoint[]>() ?? [];
if(native.Any(t=>t.RawSettingsApprovals is not null&&(!t.AllowRawSettingsWrite||!t.AllowAdvancedSettingsRead||t.RawSettingsApprovals.Length==0||t.RawSettingsApprovals.Any(a=>a is null||!a.IsValid())||t.RawSettingsApprovals.Select(a=>(a.Product,a.Firmware,a.Parameter,a.Unit,a.Effect,a.EvidenceReference)).Distinct().Count()!=t.RawSettingsApprovals.Length)))throw new InvalidOperationException("Invalid raw settings installation approval.");
if(native.Any(t=>t.FunctionKeyWriteApprovals is not null&&(!t.AllowFunctionKeyWrite||!t.AllowAdvancedSettingsRead||t.FunctionKeyWriteApprovals.Length==0||t.FunctionKeyWriteApprovals.Any(a=>a is null||!a.IsValid()))))throw new InvalidOperationException("Invalid function key write installation approval.");
if(native.Any(t=>t.ProfileWriteApprovals is not null&&(!t.AllowAdvancedSettingsRead||t.ProfileWriteApprovals.Length is <1 or >7||t.ProfileWriteApprovals.Any(a=>a is null||!a.IsValid())||t.ProfileWriteApprovals.Select(a=>a.Parameter).Distinct().Count()!=t.ProfileWriteApprovals.Length)))throw new InvalidOperationException("Invalid profile write installation contracts.");
if(native.Any(t=>t.ProfileReadApprovals is not null&&(!t.AllowAdvancedSettingsRead||t.ProfileReadApprovals.Length is <1 or >7||t.ProfileReadApprovals.Any(a=>a is null||!a.IsValid())||t.ProfileReadApprovals.Select(a=>a.Parameter).Distinct().Count()!=t.ProfileReadApprovals.Length)))throw new InvalidOperationException("Invalid profile read installation contracts.");
if(native.Any(t=>t.BiometricTransferApprovals is not null&&(t.BiometricTransferApprovals.Length==0||t.BiometricTransferApprovals.Any(a=>a?.IsValid()!=true))))throw new InvalidOperationException("Invalid biometric pair/model/policy approval.");
if(native.Where(t=>t.LocalTransport is not null).Any(t=>t.LocalTransport!.StationName!=Environment.MachineName.ToUpperInvariant())||native.Where(t=>t.LocalTransport is not null).GroupBy(t=>(t.LocalTransport!.StationName,t.LocalTransport.Kind,t.LocalTransport.SerialPort)).Any(g=>g.Count()>1))throw new InvalidOperationException("Wrong-station or duplicate physical endpoint binding.");
var dotnetPath = builder.Configuration["IO:DeviceWorker:BridgeDotnetPath"];
var bridgePath = builder.Configuration["IO:DeviceWorker:BridgeAssemblyPath"];
if (native.Any(t => (!new ZkProbeInput(t.DeviceId,t.Address,t.Port,t.MachineNumber,t.ExpectedSerial,0,t.LocalTransport).IsValid()||!ZkUserReadFormats.IsKnown(t.UserReadFormat)||!ZkUserReadFormats.IsKnown(t.UserWriteFormat)||!ZkAttendanceReadFormats.IsKnown(t.AttendanceReadFormat)||(t.DaylightReadApproval is not null&&(!t.AllowAdvancedSettingsRead||!t.DaylightReadApproval.IsValid()))||(t.FunctionKeyReadApproval is not null&&(!t.AllowAdvancedSettingsRead||!t.FunctionKeyReadApproval.IsValid())))) ||
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
builder.Services.AddSingleton(new WorkerConfiguration(service, targets, native, dotnetPath, bridgePath, builder.Configuration.GetValue<bool>("IO:DeviceWorker:EnableOperations"),builder.Configuration.GetValue<bool>("IO:DeviceWorker:EnableUserDownloads"),builder.Configuration.GetValue<bool>("IO:DeviceWorker:EnableUserUploads"),builder.Configuration.GetValue<bool>("IO:DeviceWorker:EnableAttendanceDownloads"),builder.Configuration.GetValue<bool>("IO:DeviceWorker:EnableSchedules"),builder.Configuration.GetValue<bool>("IO:DeviceWorker:EnableSettingsReads"),builder.Configuration.GetSection("IO:Biometrics:PhotoStaging").Get<BiometricPhotoStaging>()));
if(builder.Configuration.GetValue<bool>("IO:DeviceWorker:EnableHealthReports"))builder.Services.AddHostedService<ServiceHeartbeatWorker>();
if(builder.Configuration.GetValue<bool>("IO:RemoteEnroll:EnableRequests"))builder.Services.AddHostedService<EnrollmentWorker>();
if(builder.Configuration.GetValue<bool>("IO:AttendanceErase:EnableDeleteAll")||builder.Configuration.GetValue<bool>("IO:AttendanceErase:EnableRangeDeletes"))builder.Services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp=>new AttendanceEraseWorker(sp.GetRequiredService<IServiceScopeFactory>(),sp.GetRequiredService<WorkerConfiguration>() with{EnableRangeAttendanceErases=builder.Configuration.GetValue<bool>("IO:AttendanceErase:EnableRangeDeletes"),EnableAllAttendanceErases=builder.Configuration.GetValue<bool>("IO:AttendanceErase:EnableDeleteAll")}));
if(builder.Configuration.GetValue<bool>("IO:AllDataErase:all-data:Enabled"))builder.Services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp=>new AllDataEraseWorker(sp.GetRequiredService<IServiceScopeFactory>(),sp.GetRequiredService<WorkerConfiguration>() with{AllDataEraseFamilies=["all-data"]}));
var protectedFamilies=new[]{"users","fingerprints","administrators"}.Where(f=>builder.Configuration.GetValue<bool>("IO:ProtectedErase:"+f+":Enabled")).ToArray();
if(protectedFamilies.Length>0)builder.Services.AddSingleton<Microsoft.Extensions.Hosting.IHostedService>(sp=>new ProtectedEraseWorker(sp.GetRequiredService<IServiceScopeFactory>(),sp.GetRequiredService<WorkerConfiguration>() with{ProtectedEraseFamilies=protectedFamilies}));
if(builder.Configuration.GetValue<bool>("IO:Biometrics:EnableTransfers"))builder.Services.AddHostedService<BiometricTransferWorker>();
builder.Services.AddHostedService<DiagnosticWorker>();
await builder.Build().RunAsync();

internal sealed record NativeEndpoint(Guid DeviceId, string Address, int Port, int MachineNumber, string ExpectedSerial, bool AllowDefaultCommunicationKey, bool AllowClockSet=false,bool AllowUserRead=false,bool AttendanceOnlyUserWriteApproved=false,bool AllowAttendanceRead=false,bool AllowAdvancedSettingsRead=false,string UserReadFormat=ZkUserReadFormats.Ssr,string UserWriteFormat=ZkUserReadFormats.Ssr,bool AllowRestart=false,bool AllowNetworkIpChange=false,bool AllowNetworkInspect=false,string[]? ApprovedNetworkAddresses=null,bool AllowIdleMinutesWrite=false,ZkIdleApproval? IdleMinutesApproval=null,string AttendanceReadFormat=ZkAttendanceReadFormats.Ssr,ZkFunctionKeyReadApproval? FunctionKeyReadApproval=null,bool AllowRawSettingsWrite=false,ZkRawSettingApproval[]? RawSettingsApprovals=null,bool AllowFunctionKeyWrite=false,ZkFunctionKeyWriteApproval[]? FunctionKeyWriteApprovals=null,ZkDaylightReadApproval? DaylightReadApproval=null,bool AllowProfileWrite=false,ZkProfileWriteApproval[]? ProfileWriteApprovals=null,ZkProfileReadApproval[]? ProfileReadApprovals=null,RemoteEnrollApproval[]? EnrollmentApprovals=null,BiometricTransferApproval[]? BiometricTransferApprovals=null,AttendanceEraseApproval[]? AttendanceEraseApprovals=null,AttendanceRangeEraseApproval[]? AttendanceRangeEraseApprovals=null,ProtectedEraseApproval[]? ProtectedEraseApprovals=null,AllDataEraseApproval[]? AllDataEraseApprovals=null,ZkLocalTransport? LocalTransport=null)
{
 public bool Matches(string? host,int? port)=>port==Port&&(LocalTransport is null?System.Net.IPAddress.TryParse(host,out var address)&&System.Net.IPAddress.TryParse(Address,out var pinned)&&pinned.Equals(address):host==Address);
 public async Task<bool> RegisteredAsync(IServiceScopeFactory scopes,CancellationToken ct)
 {
  if(LocalTransport is null)return true;
  if(LocalTransport.StationName!=Environment.MachineName.ToUpperInvariant())return false;
  await using var scope=scopes.CreateAsyncScope();
  return await scope.ServiceProvider.GetRequiredService<IoDbContext>().Devices.AnyAsync(d=>d.Id==DeviceId&&d.Enabled&&d.Profile==LocalTransport.Profile&&d.Host==Address&&d.Port==Port,ct);
 }
 public override string ToString()=>"Native endpoint [private station/credential policy redacted]";
}
internal sealed record WorkerConfiguration(Guid ServiceUserId, TcpConnectionDiagnostic.AllowedEndpoint[] AllowedEndpoints, NativeEndpoint[] NativeEndpoints, string? DotnetPath, string? BridgePath, bool EnableOperations,bool EnableUserDownloads,bool EnableUserUploads,bool EnableAttendanceDownloads,bool EnableSchedules,bool EnableSettingsReads,BiometricPhotoStaging? PhotoStaging=null,bool EnableRangeAttendanceErases=false,bool EnableAllAttendanceErases=true,string[]? ProtectedEraseFamilies=null,string[]? AllDataEraseFamilies=null);
internal class DiagnosticWorker(IServiceScopeFactory scopes, WorkerConfiguration configuration) : BackgroundService
{
    // No configuration switch can enable COM on Linux. The internal test subclass
    // exercises the identical queue/credential/process path with a test-only relay.
    protected virtual bool NativePlatformSupported => OperatingSystem.IsWindows();
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Loop(stoppingToken)));

    private async Task Loop(CancellationToken ct)
    {
        int queueCursor=0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                DeviceConnections.Claim? claim=null;DeviceOperations.Claim? operation=null;DeviceUserDownloads.Claim? users=null;DeviceUserUploads.Claim? upload=null;DeviceAttendanceDownloads.Claim? attendance=null;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    var db=scope.ServiceProvider.GetRequiredService<IoDbContext>();var now=DateTimeOffset.UtcNow;var cutoff=now.AddMinutes(-2);
                    var available=await db.DeviceConnections.AnyAsync(x=>x.State=="pending"||(x.State=="running"&&x.ClaimedAtUtc<cutoff),ct);
                    var operationsAvailable=configuration.EnableOperations&&await db.DeviceOperations.AnyAsync(x=>(x.Kind!="settings.read"||configuration.EnableSettingsReads)&&(x.State=="pending"||(x.State=="running"&&x.ClaimedAtUtc<cutoff)),ct);
                    var usersAvailable=configuration.EnableUserDownloads&&await db.DeviceUserDownloads.AnyAsync(x=>x.State=="pending"||(x.State=="running"&&x.LeaseAtUtc!=null&&x.LeaseAtUtc.Value.AddSeconds(x.TimeoutSeconds+120)<now),ct);
                    var uploadsAvailable=configuration.EnableUserUploads&&await db.DeviceUserUploads.AnyAsync(x=>x.State=="pending"||(x.State=="running"&&x.ClaimedAtUtc!=null&&x.ClaimedAtUtc.Value.AddSeconds(x.TimeoutSeconds+120)<now),ct);
                    var attendanceAvailable=configuration.EnableAttendanceDownloads&&await db.DeviceAttendanceDownloads.AnyAsync(x=>x.State=="pending"||(x.State=="running"&&x.LeaseAtUtc!=null&&x.LeaseAtUtc.Value.AddSeconds(x.TimeoutSeconds+120)<now),ct);
                    var schedulesAvailable=configuration.EnableSchedules&&await db.DevicePollSchedules.AnyAsync(x=>x.Enabled&&x.NextDueUtc<=now,ct);
                    var queues=new[]{usersAvailable,operationsAvailable,available,uploadsAvailable,attendanceAvailable,schedulesAvailable};int selected=-1;
                    for(int i=0;i<6;i++){var candidate=(queueCursor+i)%6;if(queues[candidate]){selected=candidate;break;}}
                    // Advance even if the selected independent service grant was revoked.
                    queueCursor=(queueCursor+1)%6;
                    if(selected==5)await scope.ServiceProvider.GetRequiredService<DevicePollSchedules>().StepAsync(configuration.ServiceUserId,ct);
                    else if(selected==0)users=await scope.ServiceProvider.GetRequiredService<DeviceUserDownloads>().ClaimAsync(configuration.ServiceUserId,ct);
                    else if(selected==1)operation=await scope.ServiceProvider.GetRequiredService<DeviceOperations>().ClaimAsync(configuration.ServiceUserId,ct,configuration.EnableSettingsReads);
                    else if(selected==4)attendance=await scope.ServiceProvider.GetRequiredService<DeviceAttendanceDownloads>().ClaimAsync(configuration.ServiceUserId,ct);
                    else if(selected==3)upload=await scope.ServiceProvider.GetRequiredService<DeviceUserUploads>().ClaimAsync(configuration.ServiceUserId,ct);
                    else if(selected==2)claim=await scope.ServiceProvider.GetRequiredService<DeviceConnections>().ClaimAsync(configuration.ServiceUserId,ct);
                }
                if(attendance is not null)
                {
                    var result=await DownloadAttendance(attendance,ct);await using var finish=scopes.CreateAsyncScope();
                    await finish.ServiceProvider.GetRequiredService<DeviceAttendanceDownloads>().CompleteAsync(configuration.ServiceUserId,attendance.Id,attendance.Token,result.State,result.Count,ct);
                }
                else if(upload is not null)
                {
                    var result=await UploadUser(upload,ct);await using var finish=scopes.CreateAsyncScope();
                    await finish.ServiceProvider.GetRequiredService<DeviceUserUploads>().CompleteAsync(configuration.ServiceUserId,upload.Id,upload.Token,result,ct);
                }
                else if(users is not null)
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
        if (!DeviceTransportSupport.IsSdkProfile(claim.Profile)||!await native.RegisteredAsync(scopes,ct)) return "unsupported";
        if (!native.Matches(claim.Host,claim.Port)) return "endpoint-denied";
        if (!NativePlatformSupported) return "unsupported";
        await using var scope = scopes.CreateAsyncScope();
        var credential = await scope.ServiceProvider.GetRequiredService<DeviceConnectionCredential>().LoadAsync(configuration.ServiceUserId,claim,native.AllowDefaultCommunicationKey,ct);
        if (credential is null) return "credential-unavailable";
        var result = await NativeProcessSupervisor.RunAsync(configuration.DotnetPath!,configuration.BridgePath!,
            new ZkProbeInput(claim.DeviceId,native.Address,native.Port,native.MachineNumber,native.ExpectedSerial,credential.Value,native.LocalTransport),claim.TimeoutSeconds,ct);
        return result == "invalid-input" ? "bridge-failed" : result;
    }

    private async Task<ZkUserWriteOutput> UploadUser(DeviceUserUploads.Claim claim,CancellationToken ct)
    {
        var native=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
        // Installation approval is NOT a claim of model compatibility. No access-control
        // terminals are admitted; approval requires the isolated lab acceptance procedure.
        if(native is null||!native.AttendanceOnlyUserWriteApproved||native.UserReadFormat!=claim.WriteFormat||native.UserWriteFormat!=claim.WriteFormat||!ZkUserReadFormats.IsKnown(claim.WriteFormat)||!NativePlatformSupported)return new("unsupported");
        if(!native.Matches(claim.Host,claim.Port)||!await native.RegisteredAsync(scopes,ct))return new("failed-before-write");
        DeviceCommunicationKey? credential;
        await using(var scope=scopes.CreateAsyncScope())credential=await scope.ServiceProvider.GetRequiredService<DeviceUserUploads>().CredentialAsync(configuration.ServiceUserId,claim,native.AllowDefaultCommunicationKey,ct);
        if(credential is null)return new("failed-before-write");
        using var stop=CancellationTokenSource.CreateLinkedTokenSource(ct);
        var process=NativeProcessSupervisor.RunUserWriteAsync(configuration.DotnetPath!,configuration.BridgePath!,new(new(claim.DeviceId,native.Address,native.Port,native.MachineNumber,native.ExpectedSerial,credential.Value,native.LocalTransport),true,true,claim.Expected,claim.Desired,claim.WriteFormat),claim.TimeoutSeconds,stop.Token);
        while(!process.IsCompleted)
        {
            if(await Task.WhenAny(process,Task.Delay(500,ct))==process)break;
            bool revoked;try{await using var scope=scopes.CreateAsyncScope();revoked=await scope.ServiceProvider.GetRequiredService<DeviceUserUploads>().ShouldStopAsync(configuration.ServiceUserId,claim,ct);}catch{revoked=true;}
            if(revoked||ct.IsCancellationRequested){await stop.CancelAsync();break;}
        }
        return await process;
    }

    private async Task<NativeAttendanceReadSupervisor.Result> DownloadAttendance(DeviceAttendanceDownloads.Claim claim,CancellationToken ct)
    {
        var native=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
        if(native is null||!native.AllowAttendanceRead||native.AttendanceReadFormat!=claim.ReadFormat||!ZkAttendanceReadFormats.IsKnown(claim.ReadFormat)||!NativePlatformSupported)return new("unsupported",0);
        if(!native.Matches(claim.Host,claim.Port)||!await native.RegisteredAsync(scopes,ct))return new("endpoint-denied",0);
        DeviceCommunicationKey? credential;
        await using(var scope=scopes.CreateAsyncScope())credential=await scope.ServiceProvider.GetRequiredService<DeviceAttendanceDownloads>().CredentialAsync(configuration.ServiceUserId,claim,native.AllowDefaultCommunicationKey,ct);
        if(credential is null)return new("credential-unavailable",0);
        long cursor=0;
        return await NativeAttendanceReadSupervisor.RunAsync(configuration.DotnetPath!,configuration.BridgePath!,new(new(claim.DeviceId,native.Address,native.Port,native.MachineNumber,native.ExpectedSerial,credential.Value,native.LocalTransport),true,native.AllowAttendanceRead,claim.ReadFormat),claim.TimeoutSeconds,async(batch,token)=>
        {
            await using var scope=scopes.CreateAsyncScope();cursor=await scope.ServiceProvider.GetRequiredService<DeviceAttendanceDownloads>().AppendAsync(configuration.ServiceUserId,claim.Id,claim.Token,cursor,batch,token);
        },ct);
    }

    private async Task<NativeUserReadSupervisor.Result> DownloadUsers(DeviceUserDownloads.Claim claim,CancellationToken ct)
    {
        var native=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
        if(native is null||!native.AllowUserRead||native.UserReadFormat!=claim.ReadFormat||!ZkUserReadFormats.IsKnown(native.UserReadFormat)||!NativePlatformSupported)return new("unsupported",0);
        if(!native.Matches(claim.Host,claim.Port)||!await native.RegisteredAsync(scopes,ct))return new("endpoint-denied",0);
        DeviceCommunicationKey? credential;
        await using(var scope=scopes.CreateAsyncScope())credential=await scope.ServiceProvider.GetRequiredService<DeviceUserDownloads>().CredentialAsync(configuration.ServiceUserId,claim,native.AllowDefaultCommunicationKey,ct);
        if(credential is null)return new("credential-unavailable",0);
        long cursor=0;
        return await NativeUserReadSupervisor.RunAsync(configuration.DotnetPath!,configuration.BridgePath!,new(new(claim.DeviceId,native.Address,native.Port,native.MachineNumber,native.ExpectedSerial,credential.Value,native.LocalTransport),true,native.AllowUserRead,claim.ReadFormat),claim.TimeoutSeconds,async(batch,token)=>
        {
            await using var scope=scopes.CreateAsyncScope();cursor=await scope.ServiceProvider.GetRequiredService<DeviceUserDownloads>().AppendAsync(configuration.ServiceUserId,claim.Id,claim.Token,cursor,batch,token);
        },ct);
    }

    private async Task<ZkOperationOutput> Operate(DeviceOperations.Claim claim,CancellationToken ct)
    {
        var native=configuration.NativeEndpoints.SingleOrDefault(x=>x.DeviceId==claim.DeviceId);
        if(native is null||!DeviceTransportSupport.IsSdkProfile(claim.Profile))return new("unsupported");
        if(!native.Matches(claim.Host,claim.Port)||!await native.RegisteredAsync(scopes,ct))return new("endpoint-denied");
        if(!NativePlatformSupported||(claim.Kind=="device.restart"&&!native.AllowRestart)||(claim.Kind=="clock.set"&&!native.AllowClockSet)||(claim.Kind=="settings.read"&&(!configuration.EnableSettingsReads||!native.AllowAdvancedSettingsRead)))return new("unsupported");
        if(native.LocalTransport is not null&&claim.Kind is not("information.read" or "settings.read" or "clock.set" or "device.restart"))return new("unsupported");
        if(claim.Kind=="settings.idle.set"&&(!native.AllowIdleMinutesWrite||claim.IdlePlan is not{} idle||idle.Serial!=native.ExpectedSerial||native.IdleMinutesApproval?.Allows(idle)!=true))return new("unsupported");
        ZkFunctionKeyWriteApproval? keyApproval=null;
        if(claim.Kind=="settings.key.set")
        {
            if(!native.AllowFunctionKeyWrite||!native.AllowAdvancedSettingsRead||!configuration.EnableSettingsReads||claim.FunctionKeyPlan is not{} plan)return new("unsupported");
            var approvals=native.FunctionKeyWriteApprovals?.Where(a=>a.Allows(plan)).ToArray();if(approvals is not{Length:1})return new("unsupported");keyApproval=approvals[0];
        }
        ZkRawSettingApproval? rawApproval=null;
        if(claim.Kind=="settings.raw.set")
        {
            if(!native.AllowRawSettingsWrite||!native.AllowAdvancedSettingsRead||!configuration.EnableSettingsReads||claim.RawSettingPlan is not{} raw)return new("unsupported");
            rawApproval=native.RawSettingsApprovals?.SingleOrDefault(a=>a.Allows(raw));if(rawApproval is null)return new("unsupported");
        }
        ZkProfileWriteApproval? profileApproval=null;var profileEndpoint=new ZkProfileEndpoint(native.Address,native.Port);
        if(claim.Kind is "settings.profile.set" or "settings.profile.inspect")
        {
            if(!configuration.EnableSettingsReads||!native.AllowAdvancedSettingsRead||claim.ProfilePlan is not{} p||p.Serial!=native.ExpectedSerial||p.Original!=profileEndpoint||(claim.Kind=="settings.profile.set"&&!native.AllowProfileWrite))return new("unsupported");
            var matches=native.ProfileWriteApprovals?.Where(a=>a.Allows(p)).ToArray();if(matches is not{Length:1})return new("unsupported");profileApproval=matches[0];profileEndpoint=claim.ProfileObserveRecovery?p.Recovery:p.Original;
        }
        var target=profileEndpoint.Address;
        if(claim.Kind is "network.ip.set" or "network.inspect")
        {
            if(claim.NetworkPlan is not{} plan||!ZkNetworkPolicy.Allows(plan,claim.Kind,native.Address,native.ExpectedSerial,native.AllowNetworkIpChange,native.AllowNetworkInspect,native.ApprovedNetworkAddresses))return new("endpoint-denied");
            target=plan.TargetAddress(claim.Kind);
        }
        else if(claim.NetworkPlan is not null)return new("unsupported");
        await using var scope=scopes.CreateAsyncScope();
        var credential=await scope.ServiceProvider.GetRequiredService<DeviceOperations>().CredentialAsync(configuration.ServiceUserId,claim,native.AllowDefaultCommunicationKey,ct);
        if(credential is null)return new("credential-unavailable");
        return await NativeProcessSupervisor.RunOperationAsync(configuration.DotnetPath!,configuration.BridgePath!,
            new(new(claim.DeviceId,target,profileEndpoint.Port,native.MachineNumber,native.ExpectedSerial,credential.Value,native.LocalTransport),claim.Kind,claim.ClockLocal,native.AllowClockSet,native.AllowAdvancedSettingsRead,native.AllowRestart,claim.NetworkPlan,native.AllowNetworkIpChange,native.AllowNetworkInspect,claim.IdlePlan,native.AllowIdleMinutesWrite,claim.Kind=="settings.idle.set"?native.IdleMinutesApproval:null,claim.Kind=="settings.read"&&claim.ReadFunctionKeys?native.FunctionKeyReadApproval:null,claim.RawSettingPlan,native.AllowRawSettingsWrite,rawApproval,claim.FunctionKeyPlan,native.AllowFunctionKeyWrite,keyApproval,claim.Kind=="settings.read"?native.DaylightReadApproval:null,claim.ProfilePlan,native.AllowProfileWrite,profileApproval,claim.ProfileObserveRecovery,claim.Kind=="settings.read"?native.ProfileReadApprovals:null),claim.TimeoutSeconds,ct);
    }

}


// Independent of the four native execution slots: a heartbeat does not certify
// that an endpoint is reachable or that a queued command has completed.
internal sealed class ServiceHeartbeatWorker(IServiceScopeFactory scopes,WorkerConfiguration configuration):BackgroundService
{
 private readonly Guid instance=Guid.NewGuid();
 protected override async Task ExecuteAsync(CancellationToken stoppingToken)
 {
  try
  {
   while(!stoppingToken.IsCancellationRequested)
   {
    await Report("running",stoppingToken);
    await Task.Delay(TimeSpan.FromSeconds(30),stoppingToken);
   }
  }
  catch(OperationCanceledException)when(stoppingToken.IsCancellationRequested){}
  finally
  {
   using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(5));
   await Report("stopped",timeout.Token);
  }
 }
 private async Task Report(string state,CancellationToken ct)
 {
  try{await using var scope=scopes.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<DeviceHealth>().ReportAsync(configuration.ServiceUserId,instance,state,ct);}
  catch(Exception){/* A missing/revoked grant or unavailable DB is not a success. Last SQL heartbeat ages naturally; no secrets or fabricated stopped row. */}
 }
}
