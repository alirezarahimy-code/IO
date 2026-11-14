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
using Microsoft.AspNetCore.Routing;
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
        foreach(var (user,role,codes)in new[]{(actor,actorRole,new[]{DeviceRegistry.Create,DeviceRegistry.Credentials,DevicePushCommit.Permission,"attendance.events.receive",RemoteEnrollmentRecovery.Propose,DevicePushJournal.Retain,DevicePushJournal.Read,RemoteEnrollments.Request,RemoteEnrollments.Read,DeviceConnections.Test,DeviceOperations.Read,DeviceOperations.SetClock,DeviceUserDownloads.Download,DeviceAttendanceDownloads.Download}),
            (service,serviceRole,new[]{DevicePushJournal.Retain,RemoteEnrollments.Execute}),(reader,readerRole,new[]{RemoteEnrollments.Read,RemoteEnrollmentRecovery.Approve})})
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

    using var rsa=RSA.Create(2048);var certificateRequest=new CertificateRequest("CN=localhost",rsa,HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);var san=new SubjectAlternativeNameBuilder();san.AddIpAddress(IPAddress.Loopback);san.AddDnsName("localhost");certificateRequest.CertificateExtensions.Add(san.Build());certificateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false,false,0,false));certificateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature|X509KeyUsageFlags.KeyEncipherment,false));using var generatedCertificate=certificateRequest.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1),DateTimeOffset.UtcNow.AddDays(1));
    // Schannel cannot use an ephemeral CNG private key as server credentials. Persist this synthetic key only
    // in the disposable runner user store; this does not weaken the production courier's certificate validation.
    using var certificate=X509CertificateLoader.LoadPkcs12(generatedCertificate.Export(X509ContentType.Pfx),null,X509KeyStorageFlags.UserKeySet|X509KeyStorageFlags.PersistKeySet|X509KeyStorageFlags.Exportable);
    async Task<WebApplicationHandle> Host(bool ingress)
    {
        var app=IoWebHost.Build([],builder=>{builder.Configuration.AddInMemoryCollection(new Dictionary<string,string?>{["IO:EnableExperimentalIdentity"]="true",["ConnectionStrings:IO"]=connection,["IO:DataProtection:KeyRingPath"]=ring,["IO:DataProtection:ApplicationName"]="IO.Gateway.Integration",["IO:PushGateway:EnableIngress"]=ingress.ToString()});builder.Logging.ClearProviders();builder.WebHost.ConfigureKestrel(k=>k.Listen(IPAddress.Loopback,0,l=>l.UseHttps(certificate)));});
        await app.StartAsync();var url=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();return new(app,new HttpClient(new HttpClientHandler{ServerCertificateCustomValidationCallback=(_,_,_,_)=>true,AllowAutoRedirect=false}){BaseAddress=new Uri(url)});
    }
    HttpRequestMessage Wire(PushGatewayEnvelope e){var request=new HttpRequestMessage(HttpMethod.Post,AuthenticatedPushGateway.Path){Content=new ByteArrayContent(e.Body)};request.Content.Headers.ContentType=new("application/octet-stream");request.Headers.Add("X-IO-Binding",bindingId.ToString("D"));request.Headers.Add("X-IO-Transfer",e.TransferId.ToString("D"));request.Headers.Add("X-IO-Sent",e.SentAtUtc.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture));request.Headers.Add("X-IO-Signature",e.Signature);if(e.Stamp is not null)request.Headers.Add("X-IO-Stamp",Convert.ToBase64String(Encoding.UTF8.GetBytes(e.Stamp)));return request;}
    await using(var host=await Host(false)){using var request=Wire(Envelope(Guid.NewGuid()));using var response=await host.Client.SendAsync(request);check(response.StatusCode==HttpStatusCode.Unauthorized&&!host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Any(x=>x.RoutePattern.RawText==AuthenticatedPushGateway.Path),"gateway HTTPS ingress remains unmapped by default and protected fallback rejects anonymous requests");}
    await using(var host=await Host(true))
    {
        var httpId=Guid.NewGuid();using var request=Wire(Envelope(httpId));using var response=await host.Client.SendAsync(request);var httpReceipt=await response.Content.ReadFromJsonAsync<JsonElement>();
        check(response.StatusCode==HttpStatusCode.OK&&httpReceipt.GetProperty("state").GetString()=="gateway-retained-device-unverified"&&await Q(db=>db.PushGatewayReceipts.AnyAsync(x=>x.TransferId==httpId)),"actual HTTPS endpoint responds only with committed quarantine receipt");
        // Only inside the disposable Windows CI guard: trust this one ephemeral loopback certificate for the real courier,
        // then remove exactly this certificate. Production courier has no validation override or insecure flag.
        using(var roots=new X509Store(StoreName.Root,StoreLocation.LocalMachine))
        {
            // Hosted Windows runner is disposable and elevated. CurrentUser Root can display a trust-confirmation
            // dialog and hang noninteractive CI; LocalMachine Root is an explicit administrative fixture only.
            Console.WriteLine("::notice title=Windows HTTPS fixture::Installing one temporary synthetic loopback trust anchor on disposable runner.");
            roots.Open(OpenFlags.ReadWrite);roots.Add(certificate);
            Console.WriteLine("::notice title=Windows HTTPS fixture::Temporary synthetic trust anchor installed; exercising production courier TLS validation.");
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
                var collectorSpool=Path.Combine(spool,"native");Directory.CreateDirectory(collectorSpool);
                var collectorConfig=new NativePushCollectorConfiguration(new(host.Client.BaseAddress!.AbsoluteUri,bindingId,gateway,push,push,binding.Serial,"UTC","key-1",Convert.ToBase64String(key)),"127.0.0.1",0,"127.0.0.1",collectorSpool,"SYNTHETIC classic ATTLOG model","SYNTHETIC durable quarantine ACK approval","Encrypt=0\nATTLOGStamp=0",true,true,true);
                check(await Reject<InvalidOperationException>(()=>{IoPushCollectorHost.Build(collectorConfig with{EnableNativeCollector=false},true);return Task.CompletedTask;}),"native collector refuses to start without explicit deployment enablement");
                await using(var collector=IoPushCollectorHost.Build(collectorConfig,true))
                {
                    await collector.StartAsync();var collectorUrl=collector.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();using var terminal=new HttpClient{BaseAddress=new Uri(collectorUrl)};
                    using var options=await terminal.GetAsync("/iclock/cdata?SN="+binding.Serial+"&options=all");check(options.IsSuccessStatusCode&&(await options.Content.ReadAsStringAsync()).Contains("Encrypt=0"),"classic LAN handshake serves only installed-approved bounded options");
                    using var wrong=await terminal.PostAsync("/iclock/cdata?SN=OTHER&table=ATTLOG",new ByteArrayContent(body));check(wrong.StatusCode==HttpStatusCode.Forbidden,"collector serial pin rejects another terminal without trusting serial as authentication");
                    using var nativeResponse=await terminal.PostAsync("/iclock/cdata?SN="+binding.Serial+"&table=ATTLOG&Stamp=synthetic",new ByteArrayContent(body));
                    var nativeTransfer=Directory.GetFiles(collectorSpool,"transfer.json",SearchOption.AllDirectories).Single();var nativeId=JsonSerializer.Deserialize<GatewayCourierTransfer>(await File.ReadAllTextAsync(nativeTransfer))!.TransferId;
                    check(nativeResponse.StatusCode==HttpStatusCode.OK&&await nativeResponse.Content.ReadAsStringAsync()=="OK: 2"&&File.Exists(nativeTransfer+".receipt.json")&&File.Exists(nativeTransfer+".ack-intent.json")&&await Q(db=>db.PushGatewayReceipts.AnyAsync(x=>x.TransferId==nativeId)),"actual native HTTP upload ACK follows durable original, authenticated HTTPS SQL receipt and ACK intent");
                    using var unsupported=await terminal.PostAsync("/iclock/cdata?SN="+binding.Serial+"&table=OPERLOG",new ByteArrayContent(body));check(unsupported.StatusCode==HttpStatusCode.BadRequest,"collector refuses biometric/user/unknown tables instead of acknowledging unsupported data");
                    using var malformed=await terminal.PostAsync("/iclock/cdata?SN="+binding.Serial+"&table=ATTLOG",new StringContent("bad-line"));check(malformed.StatusCode==HttpStatusCode.BadRequest&&Directory.GetFiles(collectorSpool,"*.ack-intent.json",SearchOption.AllDirectories).Length==1,"malformed ATTLOG remains evidence and never earns vendor ACK");
                    await collector.StopAsync();
                }
                var outageSpool=Path.Combine(spool,"outage");Directory.CreateDirectory(outageSpool);
                var outageConfig=collectorConfig with{SpoolDirectory=outageSpool,Gateway=collectorConfig.Gateway with{KeyBase64=Convert.ToBase64String(new byte[32])}};
                await using(var unavailable=IoPushCollectorHost.Build(outageConfig,true))
                {
                    await unavailable.StartAsync();var url=unavailable.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();using var terminal=new HttpClient{BaseAddress=new Uri(url)};
                    using var denied=await terminal.PostAsync("/iclock/cdata?SN="+binding.Serial+"&table=ATTLOG",new ByteArrayContent(body));
                    check(denied.StatusCode==HttpStatusCode.ServiceUnavailable&&Directory.GetFiles(outageSpool,"original.attlog",SearchOption.AllDirectories).Length==1&&Directory.GetFiles(outageSpool,"*.ack-intent.json",SearchOption.AllDirectories).Length==0,"failed authenticated handoff keeps original evidence and withholds native ACK");
                    await unavailable.StopAsync();
                }
                var outageTransfer=Directory.GetFiles(outageSpool,"transfer.json",SearchOption.AllDirectories).Single();var outageId=JsonSerializer.Deserialize<GatewayCourierTransfer>(await File.ReadAllTextAsync(outageTransfer))!.TransferId;
                await using(var recovered=IoPushCollectorHost.Build(collectorConfig with{SpoolDirectory=outageSpool},true))
                {
                    await recovered.StartAsync();using var wait=new CancellationTokenSource(TimeSpan.FromSeconds(15));while(!File.Exists(outageTransfer+".receipt.json"))await Task.Delay(100,wait.Token);
                    check(await Q(db=>db.PushGatewayReceipts.CountAsync(x=>x.TransferId==outageId))==1&&Directory.GetFiles(outageSpool,"*.ack-intent.json",SearchOption.AllDirectories).Length==0,"collector restart redelivers stable transfer id without acknowledging a closed device connection");
                    await recovered.StopAsync();
                }
                var firstReceipt=await File.ReadAllTextAsync(courierTransfer+".receipt.json");
                check(await Courier()==0&&await File.ReadAllTextAsync(courierTransfer+".receipt.json")==firstReceipt&&await Q(db=>db.PushGatewayReceipts.CountAsync(x=>x.TransferId==courierId))==1,"real courier retry reuses the same transfer and SQL receipt without duplicate quarantine evidence");
            }
            finally{roots.Remove(certificate);Directory.Delete(spool,true);Console.WriteLine("::notice title=Windows HTTPS fixture::Temporary synthetic trust anchor removed.");}
        }
        using var bad=Wire(Envelope(Guid.NewGuid()) with{Signature=new string('0',64)});using var rejected=await host.Client.SendAsync(bad);check(rejected.StatusCode==HttpStatusCode.Unauthorized,"actual HTTPS ingress rejects tampered MAC without session cookies");
        using var admin=await host.Client.PostAsJsonAsync("/devices/gateway-bindings",provision);check(admin.StatusCode==HttpStatusCode.Unauthorized,"gateway HMAC route does not grant anonymous binding administration");
        using var nativePost=await host.Client.PostAsync("/iclock/cdata",new StringContent("not-native-auth"));check(nativePost.StatusCode==HttpStatusCode.Unauthorized&&!host.App.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().Any(x=>x.RoutePattern.RawText=="/iclock/cdata"),"native unauthenticated iclock route remains unmapped behind the protected fallback");
    }
    // Review is separate from delivery. An evidence-line acceptance id is not a physical-event id.
    await Q(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=push,EmployeeId=employee,CompanyId=company,SourcePersonId="001",ValidFromUtc=DateTimeOffset.Parse("2026-01-01T00:00:00Z")});await db.SaveChangesAsync();return true;});
    var review=new DevicePushCommit.Input(Guid.NewGuid(),receipt.ReceiptId,1,pushVersion,"in","SYNTHETIC isolated-LAN inspection","SYNTHETIC cross-pull and retransmission review");
    Task<object> Preview(DevicePushCommit.Input i)=>Use<DevicePushCommit,object>(x=>x.PreviewAsync(actor,i));
    check(await Reject<DomainConflictException>(()=>Preview(review)),"PUSH row promotion is default-off even with exact mapping and commit authority");
    config["IO:PushGateway:EnableReviewedAttendanceCommit"]="true";
    check(await Reject<UnauthorizedAccessException>(()=>Use<DevicePushCommit,object>(x=>x.PreviewAsync(reader,review))),"read authority alone never promotes gateway evidence");
    var preview=JsonSerializer.SerializeToElement(await Preview(review));
    var commit=new DevicePushCommit.CommitInput(review,preview.GetProperty("previewHash").GetString()!,DevicePushCommit.Confirmation);
    Task<Guid> Commit(DevicePushCommit.CommitInput i)=>Use<DevicePushCommit,Guid>(x=>x.CommitAsync(actor,i));
    check(await Reject<DomainConflictException>(()=>Commit(commit with{PreviewHash=new string('A',64)})),"changed or stale PUSH preview cannot promote a row");
    await Q(async db=>{await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER dbo.TR_SyntheticPushCommitAuditFail ON dbo.SecurityAudit AFTER INSERT AS BEGIN THROW 51039,'Synthetic review rollback',1; END");return true;});
    check(await Reject<DbUpdateException>(()=>Commit(commit))&&await Q(db=>db.DevicePushCommits.CountAsync())==0&&await Q(db=>db.RawAttendanceEvents.CountAsync())==0,"reviewed PUSH audit failure rolls back both raw attendance and immutable acceptance");
    await Q(async db=>{await db.Database.ExecuteSqlRawAsync("DROP TRIGGER dbo.TR_SyntheticPushCommitAuditFail");return true;});
    var receivePermission=await Q(db=>db.Permissions.Where(x=>x.Code=="attendance.events.receive").Select(x=>x.Id).SingleAsync());
    await Q(async db=>{await db.RolePermissions.Where(x=>x.RoleId==actorRole&&x.PermissionId==receivePermission).ExecuteDeleteAsync();return true;});
    check(await Reject<UnauthorizedAccessException>(()=>Commit(commit)),"attendance receive authority is rechecked after PUSH preview and before commit");
    await Q(async db=>{db.RolePermissions.Add(new(){RoleId=actorRole,PermissionId=receivePermission});await db.SaveChangesAsync();return true;});
    var rawId=await Commit(commit);
    check(await Commit(commit)==rawId&&await Q(db=>db.DevicePushCommits.CountAsync())==1&&await Q(db=>db.RawAttendanceEvents.CountAsync())==1,"exact reviewed-row retry returns immutable commitment without duplicating attendance");
    check(await Reject<DomainConflictException>(()=>Commit(commit with{Review=review with{EventType="out"}})),"accepted PUSH review cannot be changed on retry");
    check(await Reject<DomainConflictException>(()=>Commit(commit with{Review=review with{Id=Guid.NewGuid()}})),"a second review id cannot accept the same evidence line twice");
    var repeated=review with{Id=Guid.NewGuid(),LineNumber=2};var repeatedPreview=JsonSerializer.SerializeToElement(await Preview(repeated));
    await Commit(new(repeated,repeatedPreview.GetProperty("previewHash").GetString()!,DevicePushCommit.Confirmation));
    check(await Q(db=>db.RawAttendanceEvents.CountAsync())==2,"explicitly reviewed repeated physical punches preserve multiplicity rather than content-hash collapse");
    check(await SqlReject("DELETE FROM dbo.DevicePushCommits WHERE Id='"+review.Id+"'",51039),"SQL independently preserves PUSH acceptance history");
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
    var readVersion=await Q(db=>db.DeviceOperations.Where(x=>x.Id==readJob).Select(x=>x.Version).SingleAsync());
    await Use<DeviceOperations,Guid>(x=>x.CancelAsync(actor,readJob,readVersion));
    var recoveryApproval=new RemoteEnrollmentRecoveryApproval(approval,"SYNTHETIC power-cycle-clears-pending evidence",10,true,true);
    var resetAt=DateTimeOffset.UtcNow;
    await Task.Delay(11000); // Exercise actual settling validation; no wall-clock bypass in production.
    var recovery=new RemoteEnrollmentRecovery.Input(Guid.NewGuid(),claim.Id,nativeVersion,recoveryApproval,resetAt,"SYNTHETIC worker and child stopped","SYNTHETIC physical reset performed","SYNTHETIC slot inspection no template", "present",RemoteEnrollmentRecovery.Confirmation);
    Task<Guid> Propose(RemoteEnrollmentRecovery.Input i)=>Use<RemoteEnrollmentRecovery,Guid>(x=>x.ProposeAsync(actor,i));
    check(await Reject<DomainConflictException>(()=>Propose(recovery)),"physical recovery remains default-off; unknown cannot be unlocked by browser assertions alone");
    config["IO:RemoteEnroll:EnablePhysicalRecovery"]="true";
    void Configure(string prefix,JsonElement value){foreach(var p in value.EnumerateObject()){if(p.Value.ValueKind==JsonValueKind.Object)Configure(prefix+":"+p.Name,p.Value);else config[prefix+":"+p.Name]=p.Value.ToString();}}
    Configure("IO:RemoteEnroll:RecoveryApprovals:0",JsonSerializer.SerializeToElement(recoveryApproval));
    await Propose(recovery);
    check(await Reject<DomainConflictException>(()=>Request(input with{Id=Guid.NewGuid()})),"a recovery proposal alone does not release the unknown write fence");
    var recoveryVersion=await Q(db=>db.RemoteEnrollmentRecoveries.Where(x=>x.Id==recovery.Id).Select(x=>x.Version).SingleAsync());
    var recoveryReview=new RemoteEnrollmentRecovery.Review(recoveryVersion,"SYNTHETIC independent technician review",RemoteEnrollmentRecovery.Confirmation);
    // Give the proposer the approve grant solely to test separation of duties beyond RBAC.
    await Q(async db=>{var p=await db.Permissions.SingleAsync(x=>x.Code==RemoteEnrollmentRecovery.Approve);db.RolePermissions.Add(new(){RoleId=actorRole,PermissionId=p.Id});await db.SaveChangesAsync();return true;});
    check(await Reject<DomainConflictException>(()=>Use<RemoteEnrollmentRecovery,Guid>(x=>x.ApproveAsync(actor,recovery.Id,recoveryReview))),"even a proposer with both permissions cannot countersign their own physical recovery");
    await Use<RemoteEnrollmentRecovery,Guid>(x=>x.ApproveAsync(reader,recovery.Id,recoveryReview));
    check(await Q(db=>db.RemoteEnrollments.Where(x=>x.Id==claim.Id).Select(x=>x.State=="unknown"&&x.ResultJson==null).SingleAsync()),"countersigned physical recovery preserves original UNKNOWN and does not fabricate enrollment completion");
    check(await SqlReject("DELETE FROM dbo.RemoteEnrollmentRecoveries WHERE Id='"+recovery.Id+"'",51040),"physical recovery evidence is immutable at SQL boundary");
    var afterRecovery=input with{Id=Guid.NewGuid()};await Request(afterRecovery);var afterVersion=await Q(db=>db.RemoteEnrollments.Where(x=>x.Id==afterRecovery.Id).Select(x=>x.Version).SingleAsync());await Use<RemoteEnrollments,Guid>(x=>x.CancelAsync(actor,afterRecovery.Id,afterVersion));
    check(await Q(db=>db.RemoteEnrollments.Where(x=>x.Id==claim.Id).Select(x=>x.State).SingleAsync())=="unknown","recovered device accepts only a new explicit intent, never requeues the old unknown job");
    var rawAfter=await Q(db=>db.RawAttendanceEvents.CountAsync());check(rawAfter==2,"only explicitly reviewed PUSH rows enter raw attendance; enrollment never fabricates attendance");
    }
    finally{try{Directory.Delete(ring,true);}catch(IOException){} }
 }
 private sealed record WebApplicationHandle(Microsoft.AspNetCore.Builder.WebApplication App,HttpClient Client):IAsyncDisposable
 {public async ValueTask DisposeAsync(){Client.Dispose();await App.StopAsync();await App.DisposeAsync();}}
}
