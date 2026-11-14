using System.Net;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

internal static class GatewayEnrollmentOperationalTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
    var ring=Path.Combine(Path.GetTempPath(),"io-gateway-integration-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(ring);
    try
    {
    var config=new ConfigurationManager();var services=new ServiceCollection();services.AddLogging();services.AddSingleton<IConfiguration>(config);services.AddIoIdentityStorage(connection);
    services.AddDataProtection().PersistKeysToFileSystem(new DirectoryInfo(ring)).SetApplicationName("IO.Gateway.Integration");
    await using var provider=services.BuildServiceProvider();
    async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
    Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
    async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
    var actor=Guid.NewGuid();var service=Guid.NewGuid();var reader=Guid.NewGuid();var company=Guid.NewGuid();var org=Guid.NewGuid();var site=Guid.NewGuid();var push=Guid.NewGuid();var native=Guid.NewGuid();var employee=Guid.NewGuid();
    var actorRole=Guid.NewGuid();var serviceRole=Guid.NewGuid();var readerRole=Guid.NewGuid();
    await Q(async db=>
    {
        if(db.Database.HasPendingModelChanges())
        {
            var snapshot=db.GetService<IMigrationsAssembly>().ModelSnapshot!.Model;
            var initialized=db.GetService<IModelRuntimeInitializer>().Initialize(snapshot,designTime:true);
            var current=db.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
            var differences=db.GetService<IMigrationsModelDiffer>().GetDifferences(initialized.GetRelationalModel(),current.GetRelationalModel());
            throw new InvalidOperationException("Pending schema differences: "+string.Join(" | ",db.GetService<IMigrationsSqlGenerator>().Generate(differences,current).Select(x=>x.CommandText)));
        }
        check(!db.Database.HasPendingModelChanges(),"operational migration snapshot equals production EF model");
        await db.Database.MigrateAsync();
        await using var scope=provider.CreateAsyncScope();await scope.ServiceProvider.GetRequiredService<LoginIdentityDbContext>().Database.MigrateAsync();
        db.Organizations.Add(new(){Id=org,Name="SYNTHETIC operational org"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="SYNTHETIC company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="SYNTHETIC site",TimeZoneId="UTC"});
        db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="SYNTHETIC-001",DisplayName="Synthetic subject"});
        foreach(var (user,role,codes)in new[]{(actor,actorRole,new[]{DeviceRegistry.Create,DeviceRegistry.Credentials,DevicePushJournal.Retain,DevicePushJournal.Read,RemoteEnrollments.Request,RemoteEnrollments.Read,DeviceConnections.Test,DeviceOperations.Read,DeviceOperations.SetClock,DeviceUserDownloads.Download,DeviceAttendanceDownloads.Download}),
            (service,serviceRole,new[]{DevicePushJournal.Retain,RemoteEnrollments.Execute}),(reader,readerRole,new[]{RemoteEnrollments.Read})})
        {
            db.Users.Add(new(){Id=user,UserName="synthetic-"+user.ToString("N"),IsActive=true});db.Roles.Add(new(){Id=role,Name="synthetic-"+role.ToString("N")});db.UserRoles.Add(new(){UserId=user,RoleId=role});
            foreach(var code in codes){var permission=db.Permissions.Local.FirstOrDefault(x=>x.Code==code);if(permission is null){permission=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(permission);}db.RolePermissions.Add(new(){RoleId=role,PermissionId=permission.Id});}
        }
        await db.SaveChangesAsync();return true;
    });
    await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,push,new(site,"SYNTHETIC-PUSH","Synthetic push","unverified","zk-push",null,null,true)));
    await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,native,new(site,"SYNTHETIC-NATIVE","Synthetic enroll","unverified","zk-standalone","192.0.2.31",4370,true)));
    var pushVersion=await Q(db=>db.Devices.Where(x=>x.Id==push).Select(x=>x.Version).SingleAsync());var nativeVersion=await Q(db=>db.Devices.Where(x=>x.Id==native).Select(x=>x.Version).SingleAsync());
    await Q(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=native,EmployeeId=employee,CompanyId=company,SourcePersonId="001",ValidFromUtc=DateTimeOffset.UtcNow.AddDays(-1)});await db.SaveChangesAsync();return true;});
    var key=Enumerable.Range(0,32).Select(x=>(byte)x).ToArray();var bindingId=Guid.NewGuid();var gateway=Guid.NewGuid();
    var provision=new PushGatewayIngress.Provision(bindingId,gateway,push,service,pushVersion,"SYNTHETIC-SERIAL","key-1",Convert.ToBase64String(key));
    check(await Use<PushGatewayIngress,Guid>(s=>s.ProvisionAsync(actor,provision))==bindingId&&await Use<PushGatewayIngress,Guid>(s=>s.ProvisionAsync(actor,provision))==bindingId,"gateway provisioning and exact retry pin service principal device version and protected key");
    check(await Reject<DomainConflictException>(()=>Use<PushGatewayIngress,Guid>(s=>s.ProvisionAsync(actor,provision with{KeyVersion="changed"}))),"gateway provisioning rejects changed retries");
    async Task<bool> SqlReject(string sql,int number)
    {
        try{await Q(async db=>{await db.Database.ExecuteSqlRawAsync(sql);return true;});return false;}
        catch(SqlException e)when(e.Number==number){return true;}
        catch(DbUpdateException e)when(e.InnerException is SqlException inner&&inner.Number==number){return true;}
    }
    var stored=await Q(db=>db.PushGatewayBindings.AsNoTracking().SingleAsync(x=>x.Id==bindingId));
    check(stored.ProtectedKey!=provision.KeyBase64&&!stored.ToString().Contains(provision.KeyBase64,StringComparison.Ordinal),"gateway key is protected at rest and redacted from diagnostics");
    var binding=new PushGatewayBinding(gateway,push,push,"SYNTHETIC-SERIAL","key-1",TimeZoneInfo.Utc,key);
    var body=Encoding.UTF8.GetBytes("001\t2026-11-08 08:00:00\t0\t15\t000\t0\t0\r\n001\t2026-11-08 08:00:00\t0\t15\t000\t0\t0\r\n");
    PushGatewayEnvelope Envelope(Guid id,byte[]? bytes=null,string? stamp="opaque"){bytes??=body;var now=DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());return new(id,now,stamp,bytes,AuthenticatedPushGateway.Sign(binding,id,now,stamp,bytes));}
    Task<PushGatewayRetention> Receive(PushGatewayEnvelope envelope)=>Use<PushGatewayIngress,PushGatewayRetention>(s=>s.ReceiveAsync(bindingId,envelope));
    var transfer=Guid.NewGuid();var envelope=Envelope(transfer);
    check(await Reject<UnauthorizedAccessException>(()=>Receive(envelope with{Signature=new string('0',64)}))&&await Q(db=>db.DevicePushJournal.CountAsync())==0,"invalid gateway MAC cannot create evidence or receipt");
    var receipt=await Receive(envelope);var retry=await Receive(Envelope(transfer));
    check(receipt.ReceiptId==retry.ReceiptId&&receipt.RetainedRows==2&&receipt.GatewayAuthenticated&&!receipt.DeviceIdentityVerified&&receipt.State=="gateway-retained-device-unverified","SQL-committed gateway receipt preserves repeated punches and stable retry without claiming device identity");
    check(await Q(db=>db.PushGatewayReceipts.CountAsync())==1&&await Q(db=>db.DevicePushJournal.CountAsync())==1&&await Q(db=>db.RawAttendanceEvents.CountAsync())==0,"gateway commits one journal and receipt atomically and never processes attendance");
    check(await Reject<DomainConflictException>(()=>Receive(Envelope(transfer,stamp:"different"))),"same transfer id cannot change Stamp interpretation even with a valid MAC");
    check(await SqlReject("UPDATE dbo.PushGatewayBindings SET KeyVersion=N'tampered' WHERE Id='"+bindingId+"'",51037)&&await SqlReject("DELETE FROM dbo.PushGatewayReceipts WHERE Id='"+receipt.ReceiptId+"'",51037),"SQL guards independently prevent gateway binding mutation and receipt deletion");
    await Q(async db=>{await db.Users.Where(x=>x.Id==service).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsActive,false));return true;});
    check(await Reject<UnauthorizedAccessException>(()=>Receive(Envelope(Guid.NewGuid()))),"gateway service-principal disable is rechecked transactionally for each signed transfer");
    await Q(async db=>{await db.Users.Where(x=>x.Id==service).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsActive,true));await db.Users.Where(x=>x.Id==actor).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsActive,false));return true;});
    check(await Reject<UnauthorizedAccessException>(()=>Receive(Envelope(Guid.NewGuid()))),"gateway provisioner authority revoke also blocks a still-valid key");
    await Q(async db=>{await db.Users.Where(x=>x.Id==actor).ExecuteUpdateAsync(x=>x.SetProperty(p=>p.IsActive,true));return true;});
    var parallelId=Guid.NewGuid();var parallel=await Task.WhenAll(Receive(Envelope(parallelId)),Receive(Envelope(parallelId)));
    check(parallel[0].ReceiptId==parallel[1].ReceiptId&&await Q(db=>db.PushGatewayReceipts.CountAsync())==2,"concurrent delivery serializes to one durable receipt");
    // Failure after domain inserts must roll back both evidence and receipt and produce no successful result.
    await Q(async db=>{await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER dbo.TR_SyntheticGatewayAuditFail ON dbo.SecurityAudit AFTER INSERT AS BEGIN THROW 51039,'Synthetic rollback',1; END");return true;});
    var before=await Q(db=>db.DevicePushJournal.CountAsync());
    check(await Reject<DbUpdateException>(()=>Receive(Envelope(Guid.NewGuid())))&&await Q(db=>db.DevicePushJournal.CountAsync())==before&&await Q(db=>db.PushGatewayReceipts.CountAsync())==before,"security audit failure rolls back SQL evidence and receipt before any ACK");
    await Q(async db=>{await db.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.TR_SyntheticGatewayAuditFail");return true;});

    using var rsa=RSA.Create(2048);var certificateRequest=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);var san=new SubjectAlternativeNameBuilder();san.AddIpAddress(IPAddress.Loopback);san.AddDnsName("localhost");certificateRequest.CertificateExtensions.Add(san.Build());certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,false));certificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,false));using var certificate=certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
    async Task<WebApplicationHandle> Host(bool ingress)
    {
        var app=IoWebHost.Build([],builder=>{builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["IO:EnableExperimentalIdentity"]="true",["ConnectionStrings:IO"]=connection,["IO:DataProtection:KeyRingPath"]=ring,["IO:DataProtection:ApplicationName"]="IO.Gateway.Integration",["IO:PushGateway:EnableIngress"]=ingress.ToString()});builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0,l=>l.UseHttps(certificate)));});
        await app.StartAsync();var url=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();return new(app,new HttpClient(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,_,_,_)=>true,AllowAutoRedirect=false}){BaseAddress=new Uri(url)});
    }
    HttpRequestMessage Wire(PushGatewayEnvelope e){var request=new HttpRequestMessage(HttpMethod.Post,AuthenticatedPushGateway.Path){Content=new ByteArrayContent(e.Body)};request.Content.Headers.ContentType=new("application/octet-stream");request.Headers.Add("X-IO-Binding",bindingId.ToString("D"));request.Headers.Add("X-IO-Transfer",e.TransferId.ToString("D"));request.Headers.Add("X-IO-Sent",e.SentAtUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));request.Headers.Add("X-IO-Signature",e.Signature);if(e.Stamp is not null)request.Headers.Add("X-IO-Stamp",Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Stamp)));return request;}
    await using(var host=await Host(false)){using var request=Wire(Envelope(Guid.NewGuid()));using var response=await host.Client.SendAsync(request);check(response.StatusCode==HttpStatusCode.NotFound,"gateway HTTPS ingress remains absent by default");}
    await using(var host=await Host(true))
    {
        var httpId=Guid.NewGuid();using var request=Wire(Envelope(httpId));using var response=await host.Client.SendAsync(request);var httpReceipt=await response.Content.ReadFromJsonAsync<JsonElement>();
        check(response.StatusCode==HttpStatusCode.OK&&httpReceipt.GetProperty("state").GetString()=="gateway-retained-device-unverified"&&await Q(db=>db.PushGatewayReceipts.AnyAsync(x=>x.TransferId==httpId)),"actual HTTPS endpoint responds only with committed quarantine receipt");
        // Only inside the disposable Windows CI guard: trust this one ephemeral loopback certificate for the real courier,
        // then remove exactly this certificate. Production courier has no validation override or insecure flag.
        using(var roots=new X509Store(StoreName.Root,StoreLocation.CurrentUser))
        {
            roots.Open(OpenFlags.ReadWrite);roots.Add(certificate);
            var spool=Path.Combine(ring,"synthetic-spool");Directory.CreateDirectory(spool);
            try
            {
                var courierId=Guid.NewGuid();var courierConfig=Path.Combine(spool,"gateway.json");var courierTransfer=Path.Combine(spool,"transfer.json");var bodyFile=Path.Combine(spool,"body.attlog");
                await File.WriteAllBytesAsync(bodyFile,body);
                await File.WriteAllTextAsync(courierConfig,JsonSerializer.Serialize(new{Server=host.Client.BaseAddress!.AbsoluteUri,BindingId=bindingId,GatewayId=gateway,DeviceId=push,SourceId=push,Serial=binding.Serial,TimeZoneId="UTC",KeyVersion="key-1",KeyBase64=Convert.ToBase64String(key)}));
                await File.WriteAllTextAsync(courierTransfer,JsonSerializer.Serialize(new{TransferId=courierId,BodyFile=bodyFile,Stamp="courier-stamp"}));
                var executable=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..","dotnet.exe"));
                async Task<int> Courier()
                {
                    var start=new ProcessStartInfo(executable){UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=spool};start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory,"gateway-courier","IO.PushGateway.dll"));start.ArgumentList.Add(courierConfig);start.ArgumentList.Add(courierTransfer);
                    using var child=Process.Start(start)!;var stdout=child.StandardOutput.ReadToEndAsync();var stderr=child.StandardError.ReadToEndAsync();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    try{await child.WaitForExitAsync(timeout.Token);}catch{child.Kill(true);throw;}
                    var log=await stdout+await stderr;
                    check(!log.Contains(Convert.ToBase64String(key),StringComparison.Ordinal)&&!log.Contains("001\t",StringComparison.Ordinal),"real gateway courier logs no HMAC key or attendance body");
                    return child.ExitCode;
                }
                check(await Courier()==0&&File.Exists(courierTransfer+".receipt.json")&&File.Exists(bodyFile)&&await Q(db=>db.PushGatewayReceipts.CountAsync(x=>x.TransferId==courierId))==1,"real courier validates trusted HTTPS endpoint and persists receipt without deleting spool");
                var firstReceipt=await File.ReadAllTextAsync(courierTransfer+".receipt.json");
                check(await Courier()==0&&await File.ReadAllTextAsync(courierTransfer+".receipt.json")==firstReceipt&&await Q(db=>db.PushGatewayReceipts.CountAsync(x=>x.TransferId==courierId))==1,"real courier retry reuses the same transfer and SQL receipt without duplicate quarantine evidence");
            }
            finally{roots.Remove(certificate);Directory.Delete(spool,true);}
        }
        using var bad=Wire(Envelope(Guid.NewGuid()) with{Signature=new string('0',64)});using var rejected=await host.Client.SendAsync(bad);check(rejected.StatusCode==HttpStatusCode.Unauthorized,"actual HTTPS ingress rejects tampered MAC without session cookies");
        using var admin=await host.Client.PostAsJsonAsync("/devices/gateway-bindings",provision);check(admin.StatusCode==HttpStatusCode.Unauthorized,"gateway HMAC route does not grant anonymous binding administration");
        using var nativePost=await host.Client.PostAsync("/iclock/cdata",new StringContent("not-native-auth"));check(nativePost.StatusCode==HttpStatusCode.NotFound,"native unauthenticated iclock route remains absent");
    }
    await Use<PushGatewayIngress,Guid>(s=>s.RevokeAsync(actor,bindingId,stored.Version));
    check(await Reject<UnauthorizedAccessException>(()=>Receive(Envelope(transfer))),"gateway revocation blocks new deliveries and even previously committed transfer retries");

    var approval=new RemoteEnrollApproval("SYNTHETIC-SERIAL","SYNTHETIC-PRODUCT","SYNTHETIC-FIRMWARE",1,0,"SYNTHETIC-model-only","SYNTHETIC-authorization-only","SYNTHETIC-retention-only",true,-404,65535);
    var input=new RemoteEnrollments.Input(Guid.NewGuid(),native,employee,nativeVersion,"001",approval,60,RemoteEnrollments.Confirmation);
    Task<Guid> Request(RemoteEnrollments.Input value,Guid? user=null)=>Use<RemoteEnrollments,Guid>(s=>s.RequestAsync(user??actor,value));
    check(await Reject<DomainConflictException>(()=>Request(input)),"enrollment request is disabled by default despite a syntactically valid browser approval");
    config["IO:RemoteEnroll:EnableRequests"]="true";
    foreach(var p in JsonSerializer.SerializeToElement(approval).EnumerateObject())config["IO:RemoteEnroll:Approvals:0:"+p.Name]=p.Value.ToString();
    check(await Reject<UnauthorizedAccessException>(()=>Request(input,reader)),"enrollment read permission is not enrollment request authority");
    check(await Request(input)==input.Id&&await Request(input)==input.Id,"approved synthetic enrollment intent has durable exact idempotency");
    check(await Reject<DomainConflictException>(()=>Request(input with{PersonIdentifier="01"})),"enrollment retry preserves exact person identity including leading zeros");
    check(await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),native,nativeVersion,30)))&&await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),native,nativeVersion,"information.read",30,null,null)))&&await Reject<DomainConflictException>(()=>Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),native,nativeVersion,30)))&&await Reject<DomainConflictException>(()=>Use<DeviceAttendanceDownloads,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),native,nativeVersion,30,DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddHours(-1).ToUnixTimeSeconds()),DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),0,1))),"pending enrollment excludes diagnostic operations and user/attendance download queues");
    var version=await Q(db=>db.RemoteEnrollments.Where(x=>x.Id==input.Id).Select(x=>x.Version).SingleAsync());
    await Use<RemoteEnrollments,Guid>(s=>s.CancelAsync(actor,input.Id,version));
    check(await Q(db=>db.RemoteEnrollments.Where(x=>x.Id==input.Id).Select(x=>x.State).SingleAsync())=="cancelled-before-dispatch","pending enrollment cancellation is durable and never invokes native code");
    input=input with{Id=Guid.NewGuid()};await Request(input);
    var claim=await Use<RemoteEnrollments,RemoteEnrollments.Claim?>(s=>s.ClaimAsync(service))??throw new InvalidOperationException("Expected synthetic enrollment claim");
    check(await Use<RemoteEnrollments,DeviceCommunicationKey?>(s=>s.CredentialAsync(service,claim,true)) is not null&&!await Use<RemoteEnrollments,bool>(s=>s.ShouldStopAsync(service,claim)),"claimed enrollment rechecks exact policy mapping device version and service/operator authority before credential use");
    var completion=new RemoteEnrollOutput("completed",new(claim.Id,claim.PersonIdentifier,claim.Approval.FingerIndex,claim.Approval.SuccessActionCode,128));
    check(await Reject<ArgumentException>(()=>Use<RemoteEnrollments,Guid>(s=>s.CompleteAsync(service,claim,completion with{Completion=completion.Completion! with{OperationId=Guid.NewGuid()}}))),"foreign enrollment completion cannot finish a claimed job");
    await Use<RemoteEnrollments,Guid>(s=>s.CompleteAsync(service,claim,completion));
    check(await Q(db=>db.RemoteEnrollments.Where(x=>x.Id==claim.Id).Select(x=>x.State).SingleAsync())=="completed"&&await Reject<DomainConflictException>(()=>Use<RemoteEnrollments,Guid>(s=>s.CompleteAsync(service,claim,completion))),"correlated synthetic completion commits once and terminal jobs cannot be redispatched");
    input=input with{Id=Guid.NewGuid()};await Request(input);
    var dotnet=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..","dotnet.exe"));
    var relay=Path.Combine(AppContext.BaseDirectory,"virtual-relay","IO.DeviceRuntime.Tests.dll");
    var workerConfig=new WorkerConfiguration(service,[],[new(native,"192.0.2.31",4370,1,approval.Serial,true,EnrollmentApprovals:[approval])],dotnet,relay,false,false,false,false,false,false);
    using(var worker=new EnrollmentWorker(provider.GetRequiredService<IServiceScopeFactory>(),workerConfig))
    {
        await worker.StartAsync(CancellationToken.None);
        try
        {
            using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));string workerState;
            do{await Task.Delay(100,deadline.Token);workerState=await Q(db=>db.RemoteEnrollments.Where(x=>x.Id==input.Id).Select(x=>x.State).SingleAsync());}while(workerState is "pending" or "running");
            check(workerState=="completed","actual Windows enrollment worker claims SQL intent rechecks credentials and commits bounded synthetic-child completion");
        }
        finally{await worker.StopAsync(CancellationToken.None);}
    }
    input=input with{Id=Guid.NewGuid()};await Request(input);claim=await Use<RemoteEnrollments,RemoteEnrollments.Claim?>(s=>s.ClaimAsync(service))??throw new InvalidOperationException();
    version=await Q(db=>db.RemoteEnrollments.Where(x=>x.Id==claim.Id).Select(x=>x.Version).SingleAsync());await Use<RemoteEnrollments,Guid>(s=>s.CancelAsync(actor,claim.Id,version));
    check(await Use<RemoteEnrollments,bool>(s=>s.ShouldStopAsync(service,claim)),"post-dispatch cancellation requests termination rather than claiming cancellation proof");
    await Use<RemoteEnrollments,Guid>(s=>s.CompleteAsync(service,claim,new("unknown")));
    check(await Reject<DomainConflictException>(()=>Request(input with{Id=Guid.NewGuid()}))&&await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),native,nativeVersion,"clock.set",30,DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),"SET-CLOCK"))),"unknown enrollment fences another enrollment and unrelated native writes; no automatic replay");
    var readJob=Guid.NewGuid();check(await Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,readJob,native,nativeVersion,"information.read",30,null,null))==readJob,"unknown enrollment permits nonwriting recovery observations");
    check(!PermissionCatalog.Templates.Any(x=>x.Permissions.Any(p=>p.StartsWith("devices.enrollment.",StringComparison.Ordinal))),"no default role silently acquires biometric authority");
    check(await SqlReject("UPDATE dbo.RemoteEnrollments SET State=N'pending',ServiceUserId=NULL,ClaimId=NULL,ClaimedAtUtc=NULL,CancelRequestedAtUtc=NULL,FinishedAtUtc=NULL,ResultJson=NULL WHERE Id='"+claim.Id+"'",51038),"SQL terminal-history guard independently prevents unknown enrollment requeue");
    var rawAfter=await Q(db=>db.RawAttendanceEvents.CountAsync());check(rawAfter==0,"operational gateway/enrollment tests never fabricate processed attendance or store fingerprint bytes");
    }
    finally{try{Directory.Delete(ring,true);}catch(IOException){} }
 }
 private sealed record WebApplicationHandle(Microsoft.AspNetCore.Builder.WebApplication App,HttpClient Client):IAsyncDisposable
 {public async ValueTask DisposeAsync(){Client.Dispose();await App.StopAsync();await App.DisposeAsync();}}
}
