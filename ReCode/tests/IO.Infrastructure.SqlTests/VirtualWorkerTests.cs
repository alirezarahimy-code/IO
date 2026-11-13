using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

// Disposable SQL only (called behind Program's CI guard). Real web, queue, worker,
// credentials and child supervisors; ONLY the hardware endpoint is a private test relay.
internal static class VirtualWorkerTests
{
    private sealed class LaboratoryWorker(IServiceScopeFactory scopes, WorkerConfiguration configuration)
        : DiagnosticWorker(scopes, configuration)
    {
        protected override bool NativePlatformSupported => true;
    }
    public static async Task RunAsync(string connection, Action<bool,string> check)
    {
        await using var a = new VirtualDeviceLab.Device("virtual-lab-SQL-A");
        await using var b = new VirtualDeviceLab.Device("virtual-lab-SQL-B");
        await using var legacy = new VirtualDeviceLab.Device("virtual-lab-SQL-legacy");
        a.IdleMinutes=15;
        a.Write(new("00001","Virtual person","123",0,true),"synthetic-only");
        b.Write(new("00001","Virtual person","123",0,true),"synthetic-only");
        using var rsa = RSA.Create(2048);
        var certificateRequest = new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        using var certificate = certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
        await using var app = IoWebHost.Build([], builder =>
        {
            builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?> { ["IO:EnableExperimentalIdentity"]="true", ["ConnectionStrings:IO"]=connection });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0,l=>l.UseHttps(certificate)));
        });
        async Task<T> Use<S,T>(Func<S,Task<T>> f) where S:notnull
        { await using var scope=app.Services.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>()); }
        Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
        var actor=Guid.NewGuid();var service=Guid.NewGuid();var role=Guid.NewGuid();var workerRole=Guid.NewGuid();
        var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var employee=Guid.NewGuid();
        var name="virtual-operator-"+actor.ToString("N");
        var humanRights=new[]{DevicePollSchedules.Read,DevicePollSchedules.Manage,DeviceAccess.Manage,DeviceRegistry.Read,DeviceRegistry.Create,DeviceRegistry.Credentials,DeviceConnections.Read,DeviceConnections.Test,DeviceOperations.Read,DeviceOperations.SetClock,DeviceOperations.ReadSettings,DeviceOperations.SetIdle,DeviceOperations.Restart,DeviceUserDownloads.Read,DeviceUserDownloads.Download,DeviceUserUploads.Upload,DeviceAttendanceDownloads.Read,DeviceAttendanceDownloads.Download,DeviceAttendanceDownloads.Commit,"attendance.events.receive"};
        var serviceRights=new[]{DevicePollSchedules.Execute,DeviceConnections.Execute,DeviceOperations.Execute,DeviceOperations.ExecuteSettings,DeviceOperations.ExecuteIdle,DeviceOperations.ExecuteRestart,DeviceUserDownloads.Execute,DeviceUserUploads.Execute,DeviceAttendanceDownloads.Execute};
        await Q(async db=>
        {
            db.Organizations.Add(new(){Id=org,Name="Virtual worker org"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Virtual worker company"});
            db.Sites.Add(new(){Id=site,CompanyId=company,Name="Virtual UTC site",TimeZoneId="UTC"});
            db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="00001",DisplayName="Virtual person"});
            db.Users.AddRange(new UserRecord{Id=actor,UserName=name,IsActive=true},new UserRecord{Id=service,UserName="virtual-worker-"+service.ToString("N"),IsActive=true});
            foreach(var (r,u,n,rights) in new[]{(role,actor,"virtual-human",humanRights),(workerRole,service,"virtual-worker",serviceRights)})
            {
                db.Roles.Add(new(){Id=r,Name=n+u.ToString("N")});db.UserRoles.Add(new(){UserId=u,RoleId=r});
                foreach(var right in rights){var permission=db.Permissions.Local.FirstOrDefault(x=>x.Code==right)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==right);if(permission is null){permission=new(){Id=Guid.NewGuid(),Code=right};db.Permissions.Add(permission);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=permission.Id});}
            }
            await db.SaveChangesAsync();return true;
        });
        await Use<UserManager<LoginAccount>,bool>(async users=>{var result=await users.CreateAsync(new LoginAccount{Id=actor,UserName=name,LockoutEnabled=true},"Synthetic!Password-2026");return result.Succeeded?true:throw new InvalidOperationException("Virtual login fixture failed");});
        foreach(var device in new[]{a,b,legacy})
        {
            await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device.Pin.DeviceId,new(site,"VIRTUAL-"+device.Pin.ExpectedSerial,device.Pin.ExpectedSerial,"synthetic-not-ZK","zk-standalone",device.Pin.Address,device.Pin.Port,true)));
            var version=await Q(db=>db.Devices.Where(x=>x.Id==device.Pin.DeviceId).Select(x=>x.Version).SingleAsync());
            await Use<DeviceRegistry,Guid>(s=>s.SetCredentialAsync(actor,device.Pin.DeviceId,version,"123456"));
            await Q(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=device.Pin.DeviceId,CompanyId=company,SourcePersonId=device==legacy?"1":"00001",EmployeeId=employee,ValidFromUtc=new(2026,1,1,0,0,0,TimeSpan.Zero)});await db.SaveChangesAsync();return true;});
        }
        await app.StartAsync();
        var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client=new HttpClient(new HttpClientHandler{CookieContainer=new(),ServerCertificateCustomValidationCallback=(_,_,_,_)=>true}){BaseAddress=new(address)};
        async Task<HttpResponseMessage> Post(string path,object body)
        {
            using var csrf=await client.GetFromJsonAsync<JsonDocument>("/auth/csrf")??throw new InvalidOperationException();
            using var request=new HttpRequestMessage(HttpMethod.Post,path){Content=JsonContent.Create(body)};
            request.Headers.Add("X-CSRF-TOKEN",csrf.RootElement.GetProperty("token").GetString());
            return await client.SendAsync(request);
        }
        async Task Send(string path,object body){using var response=await Post(path,body);if(!response.IsSuccessStatusCode)throw new InvalidOperationException("Virtual HTTP rejected "+path+": "+response.StatusCode);}
        await Send("/auth/login",new{UserName=name,Password="Synthetic!Password-2026"});
        async Task<byte[]> Version(Guid id)=>await Q(db=>db.Devices.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
        var av=await Version(a.Pin.DeviceId);var bv=await Version(b.Pin.DeviceId);var lv=await Version(legacy.Pin.DeviceId);
        async Task Wait(Func<Task<bool>> ready,string label)
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(90));
            while(!await ready()){await Task.Delay(100,deadline.Token);}
            _=label;
        }
        var host=Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..",OperatingSystem.IsWindows()?"dotnet.exe":"dotnet"));
        var relay=Path.Combine(AppContext.BaseDirectory,"virtual-relay","IO.DeviceRuntime.Tests.dll");
        if(!File.Exists(relay)||!File.Exists(Path.ChangeExtension(relay,"runtimeconfig.json")))throw new InvalidOperationException("Missing built virtual relay");
        NativeEndpoint Pin(VirtualDeviceLab.Device d)=>new(d.Pin.DeviceId,d.Pin.Address,d.Pin.Port,1,d.Pin.ExpectedSerial,false,AllowClockSet:true,AllowUserRead:true,AttendanceOnlyUserWriteApproved:true,AllowAttendanceRead:true,AllowAdvancedSettingsRead:true,AllowRestart:true,AllowIdleMinutesWrite:true,IdleMinutesApproval:new("IO-virtual-not-ZK","virtual-fw-1",[20],"synthetic-idle-approval","standby-or-power-off"));
        var configuration=new WorkerConfiguration(service,[],[Pin(a),Pin(b),Pin(legacy) with{AttendanceReadFormat=ZkAttendanceReadFormats.Legacy}],host,relay,true,true,true,true,true,true);
        if(await Q(db=>db.DevicePollSchedules.AnyAsync(x=>x.Enabled)))throw new InvalidOperationException("Virtual worker fixture requires all previous synthetic schedules paused");
        using var worker=new LaboratoryWorker(app.Services.GetRequiredService<IServiceScopeFactory>(),configuration);
        try
        {
            await worker.StartAsync(CancellationToken.None);
            var probe=Guid.NewGuid();await Send("/device-connection",new DeviceConnectionInput(probe,a.Pin.DeviceId,av,10));
            await Wait(()=>Q(db=>db.DeviceConnections.AnyAsync(x=>x.Id==probe&&x.State!="pending"&&x.State!="running")),"probe");
            check(await Q(db=>db.DeviceConnections.AnyAsync(x=>x.Id==probe&&x.State=="sdk-serial-matched")),"virtual-e2e HTTPS queue and real worker decrypt bound credential and verify the stateful device through a real child");
            async Task<Guid> Users(VirtualDeviceLab.Device d,byte[] version)
            {
                var id=Guid.NewGuid();await Send("/device-user-downloads",new DeviceUserDownloadInput(id,d.Pin.DeviceId,version,10));
                await Wait(()=>Q(db=>db.DeviceUserDownloads.AnyAsync(x=>x.Id==id&&x.State!="pending"&&x.State!="running")),"users");
                if(!await Q(db=>db.DeviceUserDownloads.AnyAsync(x=>x.Id==id&&x.State=="succeeded"&&x.ReceivedCount==1)))throw new InvalidOperationException("Virtual user download failed");return id;
            }
            var sa=await Users(a,av);var sb=await Users(b,bv);
            check(await Q(db=>db.DeviceDownloadedUsers.CountAsync(x=>x.DownloadId==sa||x.DownloadId==sb))==2,"virtual-e2e two device user streams persist independently through installed worker append and completion paths");
            var itemA=Guid.NewGuid();var itemB=Guid.NewGuid();
            var batch=new DeviceUserUploads.Batch(Guid.NewGuid(),[new(itemA,a.Pin.DeviceId,av,sa,employee,"00001","456",true,10),new(itemB,b.Pin.DeviceId,bv,sb,employee,"00001","789",true,10)],"UPLOAD-ORDINARY-USERS");
            using(var preview=await Post("/device-user-uploads/preview",batch))
            {preview.EnsureSuccessStatusCode();using var json=JsonDocument.Parse(await preview.Content.ReadAsStringAsync());batch=batch with{PreviewHash=json.RootElement.GetProperty("result").GetProperty("previewHash").GetString()};}
            a.Fault="drop-write-ack";await Send("/device-user-uploads",batch);
            await Wait(()=>Q(async db=>await db.DeviceUserUploads.CountAsync(x=>x.BatchId==batch.BatchId&&x.State!="pending"&&x.State!="running")==2),"upload");
            check(await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==itemA&&x.State=="unknown"))&&await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==itemB&&x.State=="succeeded"))&&a.ReadExact("00001")!.User.Card=="456"&&b.ReadExact("00001")!.User.Card=="789","virtual-e2e lost write acknowledgement retains SQL unknown while the second device succeeds and both remote states persist");
            var mutations=a.Mutations;using(var blocked=await Post("/device-user-uploads/preview",batch with{BatchId=Guid.NewGuid(),Items=[batch.Items[0] with{Id=Guid.NewGuid()}]}))
                check(blocked.StatusCode==HttpStatusCode.Conflict&&a.Mutations==mutations,"virtual-e2e retained unknown blocks a new HTTP mutation without blindly replaying the applied device write");
            a.Fault="none";var recovery=await Users(a,av);
            var uploadVersion=await Q(db=>db.DeviceUserUploads.Where(x=>x.Id==itemA).Select(x=>x.Version).SingleAsync());
            await Send("/device-user-uploads/"+itemA+"/review",new{ExpectedVersion=uploadVersion,SnapshotId=recovery,Confirmation="REVIEWED-USER"});
            check(await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==itemA&&x.State=="unknown"&&x.ResolutionSnapshotId==recovery))&&a.Mutations==mutations,"virtual-e2e post-write state is recovered by fresh worker download and explicit HTTPS review without rewriting unknown or replaying mutation");
            var clock=Guid.NewGuid();await Send("/device-operations",new DeviceOperationInput(clock,a.Pin.DeviceId,av,"clock.set",10,new(2026,11,8,12,34,56,TimeSpan.Zero),"SET-CLOCK"));
            await Wait(()=>Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==clock&&x.State!="pending"&&x.State!="running")),"clock");
            check(await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==clock&&x.State=="succeeded"))&&a.ClockLocal=="2026-11-08 12:34:56"&&b.ClockLocal!="2026-11-08 12:34:56","virtual-e2e confirmed UTC clock intent changes only its selected remote device through the real worker");
            var restartId=Guid.NewGuid();await Send("/device-operations",new DeviceOperationInput(restartId,b.Pin.DeviceId,bv,"device.restart",10,null,"RESTART-DEVICE"));
            await Wait(()=>Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==restartId&&x.State!="pending"&&x.State!="running")),"restart");
            check(await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==restartId&&x.State=="succeeded"))&&b.Restarts==1&&a.Restarts==0&&b.ClockLocal=="2026-11-08 10:00:00","virtual-e2e independently granted restart uses the production executor without mutating another device or its clock");
            async Task<Guid> Settings()
            {
                var id=Guid.NewGuid();await Send("/device-settings-snapshots",new DeviceSettingsInput(id,a.Pin.DeviceId,av,10,"READ-ADVANCED-SETTINGS"));
                await Wait(()=>Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==id&&x.State!="pending"&&x.State!="running")),"settings");
                if(!await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==id&&x.State=="succeeded")))throw new InvalidOperationException("Virtual settings read failed");return id;
            }
            var settingSnapshot=await Settings();ZkIdlePlan idlePlan;string idleHash;
            using(var preview=await Post("/device-settings/idle-preview",new DeviceIdlePreview(a.Pin.DeviceId,av,settingSnapshot,20,"synthetic-idle-approval")))
            {preview.EnsureSuccessStatusCode();using var json=JsonDocument.Parse(await preview.Content.ReadAsStringAsync());var result=json.RootElement.GetProperty("result");idlePlan=result.GetProperty("plan").Deserialize<ZkIdlePlan>(new JsonSerializerOptions{PropertyNameCaseInsensitive=true})!;idleHash=result.GetProperty("previewHash").GetString()!;}
            var idleId=Guid.NewGuid();a.Fault="drop-operation-ack";
            await Send("/device-operations",new DeviceOperationInput(idleId,a.Pin.DeviceId,av,"settings.idle.set",10,null,ZkIdlePlan.Confirmation,IdlePlan:idlePlan,IdlePreviewHash:idleHash));
            await Wait(()=>Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==idleId&&x.State!="pending"&&x.State!="running")),"idle write");
            check(await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==idleId&&x.State=="unknown"))&&a.IdleMinutes==20&&a.IdleMutations==1,"virtual-e2e exact-model approved idle mutation with lost acknowledgement retains SQL unknown after one actual virtual mutation");
            a.Fault="none";var idleRecovery=await Settings();var idleVersion=await Q(db=>db.DeviceOperations.Where(x=>x.Id==idleId).Select(x=>x.Version).SingleAsync());
            await Send("/device-operations/"+idleId+"/resolve-idle",new DeviceIdleResolution(idleVersion,idleRecovery,"REVIEWED-IDLE-MINUTES"));
            check(await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==idleId&&x.State=="unknown"&&x.ResolutionInfoId==idleRecovery))&&a.IdleMutations==1,"virtual-e2e fresh model-bound settings read and human review recover uncertain idle without rewriting evidence or replaying mutation");
            var log=Guid.NewGuid();a.Fault="truncate";
            await Send("/device-attendance-downloads",new DeviceAttendanceInput(log,a.Pin.DeviceId,av,10,"2026-11-08T00:00:00Z","2026-11-09T00:00:00Z",4,5));
            await Wait(()=>Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==log&&x.State!="pending"&&x.State!="running")),"partial logs");
            check(await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==log&&x.State!="succeeded"&&x.ReceivedCount==50))&&await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>x.DownloadId==log))==50,"virtual-e2e TCP outage after one batch retains fifty original SQL tuples without successful extraction or automatic ingestion");
            a.Fault="hang";
            async Task<Guid> Logs(VirtualDeviceLab.Device d,byte[] version,int seconds)
            {
                var id=Guid.NewGuid();await Send("/device-attendance-downloads",new DeviceAttendanceInput(id,d.Pin.DeviceId,version,seconds,"2026-11-08T00:00:00Z","2026-11-09T00:00:00Z",4,5));return id;
            }
            var hung=await Logs(a,av,2);var healthy=await Logs(b,bv,10);
            await Wait(()=>Q(async db=>await db.DeviceAttendanceDownloads.CountAsync(x=>(x.Id==hung||x.Id==healthy)&&x.State!="pending"&&x.State!="running")==2),"independent outage");
            check(await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==hung&&x.State=="timed-out"&&x.ReceivedCount==0))&&await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==healthy&&x.State=="succeeded"&&x.ReceivedCount==103)),"virtual-e2e a hung remote child times out while another worker slot completes the healthy device without fabricated progress");
            a.Fault="none";var recoveredLogs=await Logs(a,av,10);
            await Wait(()=>Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==recoveredLogs&&x.State!="pending"&&x.State!="running")),"outage recovery");
            check(await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==recoveredLogs&&x.State=="succeeded"&&x.ReceivedCount==103))&&await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>x.DownloadId==log))==50&&await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==a.Pin.DeviceId))==0,"virtual-e2e a fresh worker read recovers after outage without erasing the partial attempt or silently ingesting its quarantined tuples");
            var mismatched=Guid.NewGuid();await Send("/device-attendance-downloads",new DeviceAttendanceInput(mismatched,legacy.Pin.DeviceId,lv,10,"2026-11-08T00:00:00Z","2026-11-09T00:00:00Z",4,5));
            await Wait(()=>Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==mismatched&&x.State!="pending"&&x.State!="running")),"format gate");
            check(await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==mismatched&&x.State=="unsupported"&&x.ReceivedCount==0)),"virtual-e2e SSR request cannot dispatch to an installed legacy attendance endpoint or silently switch its contract");
            legacy.Fault="truncate";var legacyPartial=Guid.NewGuid();await Send("/device-attendance-downloads",new DeviceAttendanceInput(legacyPartial,legacy.Pin.DeviceId,lv,10,"2026-11-08T00:00:00Z","2026-11-09T00:00:00Z",4,5,ZkAttendanceReadFormats.Legacy));
            await Wait(()=>Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==legacyPartial&&x.State!="pending"&&x.State!="running")),"legacy partial");
            check(await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==legacyPartial&&x.State=="bridge-failed"&&x.ReceivedCount==50))&&await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==legacy.Pin.DeviceId))==0,"virtual-e2e explicit legacy HTTPS request retains interrupted minute tuples through the real worker without ingesting incomplete evidence");legacy.Fault="none";
            // Real browser forms submit to the same running HTTPS host while the worker
            // is live. No routes, fetch, responses, SQL receipts or outcomes are mocked.
            var browser=new ProcessStartInfo("node"){RedirectStandardOutput=true,RedirectStandardError=true,UseShellExecute=false};
            browser.Environment.Remove("MSSQL_SA_PASSWORD");browser.ArgumentList.Add("tests/IO.Infrastructure.SqlTests/virtual-worker-browser.cjs");
            foreach(var argument in new[]{address,name,b.Pin.DeviceId.ToString(),Convert.ToBase64String(bv),service.ToString(),a.Pin.DeviceId.ToString(),legacy.Pin.DeviceId.ToString(),Convert.ToBase64String(lv)})browser.ArgumentList.Add(argument);
            using(var process=Process.Start(browser)??throw new InvalidOperationException())
            {
                var stdout=process.StandardOutput.ReadToEndAsync();var stderr=process.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(150));
                try{await process.WaitForExitAsync(timeout.Token);}catch{process.Kill(true);throw;}
                if(process.ExitCode!=0)throw new InvalidOperationException("Virtual browser failed: "+await stderr);
                check((await stdout).Contains("Virtual browser device flow passed.",StringComparison.Ordinal),"virtual-e2e real Chromium form queues downloads and commits all three pages from the live worker and two-device laboratory");
            }
            check(await Use<LoginIdentityDbContext,bool>(async db=>await db.DeviceOperatorPolicies.AnyAsync(x=>x.UserId==service&&x.Restricted)&&await db.DeviceOperatorGrants.CountAsync(x=>x.UserId==service)==2&&await db.DeviceOperatorGrants.CountAsync(x=>x.UserId==service&&x.RevokedAtUtc==null&&x.DeviceId==a.Pin.DeviceId)==1),"virtual-e2e real Chromium scope forms restrict the worker and retain explicit grant revoke and new-grant history through HTTPS SQL");
            check(await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==b.Pin.DeviceId))==103&&await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>db.DeviceAttendanceDownloads.Any(d=>d.Id==x.DownloadId&&d.DeviceId==b.Pin.DeviceId)&&x.RawEventId!=null))==206,"virtual-e2e repeated browser extraction retains 206 original receipts but only 103 immutable raw events including tuple multiplicity");
            check(await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==legacy.Pin.DeviceId&&x.ExternalEventId!.StartsWith("zk-legacy-int32-minute-v1:")))==103&&await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>db.DeviceAttendanceDownloads.Any(d=>d.Id==x.DownloadId&&d.DeviceId==legacy.Pin.DeviceId)&&x.RawEventId!=null&&!db.DevicePollRuns.Any(r=>r.DownloadId==x.DownloadId)))==206,"virtual-e2e real Chromium legacy format selection recovers after outage and repeated minute-precision extraction retains 206 receipts with 103 raw multiplicity identities");
            check(await Q(db=>db.DevicePollSchedules.AnyAsync(s=>s.DeviceId==legacy.Pin.DeviceId&&s.ReadFormat==ZkAttendanceReadFormats.Legacy&&s.AutoCommit&&!s.Enabled&&s.CursorUtc==new DateTimeOffset(2026,11,8,9,0,0,TimeSpan.Zero)&&db.DevicePollRuns.Any(r=>r.ScheduleId==s.Id&&r.Outcome=="succeeded"&&r.NextAfter==103&&db.DeviceAttendanceDownloads.Any(j=>j.Id==r.DownloadId&&j.ReadFormat==s.ReadFormat&&j.State=="succeeded"&&j.ReceivedCount==103))))&&await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>x.RawEventId!=null&&db.DevicePollRuns.Any(r=>r.DownloadId==x.DownloadId&&db.DevicePollSchedules.Any(s=>s.Id==r.ScheduleId&&s.DeviceId==legacy.Pin.DeviceId))))==103,"virtual-e2e real Chromium legacy schedule approval reaches actual scheduler worker and minute extraction then automatic dedup review before versioned pause with 103 retained poll receipts");
            // Stop before revocation to prove a queued intent cannot execute after its
            // original operator loses authority (not merely that new HTTP calls fail).
            await worker.StopAsync(CancellationToken.None);
            var denied=Guid.NewGuid();await Send("/device-user-downloads",new DeviceUserDownloadInput(denied,a.Pin.DeviceId,av,10));
            await Q(db=>db.RolePermissions.Where(x=>x.RoleId==role&&x.PermissionId==db.Permissions.Where(p=>p.Code==DeviceUserDownloads.Download).Select(p=>p.Id).Single()).ExecuteDeleteAsync());
            using(var restart=new LaboratoryWorker(app.Services.GetRequiredService<IServiceScopeFactory>(),configuration))
            {await restart.StartAsync(CancellationToken.None);try{await Wait(()=>Q(db=>db.DeviceUserDownloads.AnyAsync(x=>x.Id==denied&&x.State=="revoked")),"revoked queued read");}finally{await restart.StopAsync(CancellationToken.None);}}
            check(await Q(db=>db.DeviceUserDownloads.AnyAsync(x=>x.Id==denied&&x.State=="revoked"&&x.ReceivedCount==0)),"virtual-e2e worker restart checks current operator authority and refuses a previously queued read after server-side revoke");
            await Q(db=>db.RolePermissions.Where(x=>x.RoleId==role&&x.PermissionId==db.Permissions.Where(p=>p.Code==DeviceAttendanceDownloads.Read).Select(p=>p.Id).Single()).ExecuteDeleteAsync());
            using var hidden=await client.GetAsync("/device-attendance-downloads?deviceId="+b.Pin.DeviceId);
            check(hidden.StatusCode==HttpStatusCode.Forbidden,"virtual-e2e retained personal device history disappears on the next HTTPS operation after read grant revocation");
        }
        finally{await worker.StopAsync(CancellationToken.None);await app.StopAsync();}
    }
}
