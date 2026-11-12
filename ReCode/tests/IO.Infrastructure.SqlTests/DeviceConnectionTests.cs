using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class DeviceConnectionTests
{
    public static async Task RunAsync(string connection, Action<bool,string> check)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        var clock=new DiagnosticClock(); services.AddSingleton<TimeProvider>(clock);
        await using var provider = services.BuildServiceProvider();
        var actor=Guid.NewGuid(); var service=Guid.NewGuid(); var reader=Guid.NewGuid(); var outsider=Guid.NewGuid();
        var actorRole=Guid.NewGuid(); var serviceRole=Guid.NewGuid(); var readerRole=Guid.NewGuid();
        var org=Guid.NewGuid(); var company=Guid.NewGuid(); var site=Guid.NewGuid(); var first=Guid.NewGuid(); var second=Guid.NewGuid();
        async Task<T> Use<S,T>(Func<S,Task<T>> action) where S:notnull { await using var scope=provider.CreateAsyncScope(); return await action(scope.ServiceProvider.GetRequiredService<S>()); }
        Task<T> Query<T>(Func<IoDbContext,Task<T>> action)=>Use<IoDbContext,T>(action);
        async Task<bool> Reject<E>(Func<Task> action) where E:Exception { try { await action(); return false; } catch(E) { return true; } }
        await Query(async db=>{
            db.Organizations.Add(new(){Id=org,Name="Connection fixture"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Connection company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Connection site",TimeZoneId="UTC"});
            foreach(var(id,name)in new[]{(actor,"connection-actor"),(service,"connection-worker"),(reader,"connection-reader"),(outsider,"connection-outsider")})db.Users.Add(new(){Id=id,UserName=name,IsActive=true});
            foreach(var(r,u,name,codes)in new[]{(actorRole,actor,"connection-requester",new[]{DeviceConnections.Test,DeviceRegistry.Create,DeviceRegistry.Update}),(serviceRole,service,"connection-service",new[]{DeviceConnections.Execute}),(readerRole,reader,"connection-reader",new[]{DeviceConnections.Read})}){
                db.Roles.Add(new(){Id=r,Name=name});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
            await db.SaveChangesAsync();return true;
        });
        foreach(var(id,number)in new[]{(first,"CONNECTION-1"),(second,"CONNECTION-2")})
            await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,id,new(site,number,"Connection device","declared","zk-standalone","127.0.0.1",4370,true)));
        Task<byte[]> Version(Guid device)=>Query(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
        Task<Guid> Request(Guid id,Guid device,byte[] version,int timeout=1,Guid? user=null)=>Use<DeviceConnections,Guid>(s=>s.RequestAsync(user??actor,id,device,version,timeout));
        Task<DeviceConnections.Claim?> Claim(Guid? user=null)=>Use<DeviceConnections,DeviceConnections.Claim?>(s=>s.ClaimAsync(user??service));
        Task<Guid> Finish(DeviceConnections.Claim claim,string state,Guid? user=null,Guid? token=null)=>Use<DeviceConnections,Guid>(s=>s.CompleteAsync(user??service,claim.Id,token??claim.Token,state));
        Task<string> State(Guid id)=>Query(db=>db.DeviceConnections.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
        Task<byte[]> JobVersion(Guid id)=>Query(db=>db.DeviceConnections.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
        var v=await Version(first);var operation=Guid.NewGuid();
        check(await Reject<UnauthorizedAccessException>(()=>Request(operation,first,v,user:outsider))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceConnections,object>(s=>s.ListAsync(actor,null,0))),"connection diagnostics independently enforce request and history permissions");
        check(await Reject<ArgumentException>(()=>Request(operation,first,v,0))&&await Reject<ArgumentException>(()=>Request(operation,first,v,61))&&await Reject<ArgumentException>(()=>Request(operation,first,[])),"connection diagnostics reject missing versions and unbounded timeouts");
        await Request(operation,first,v);
        check(await Request(operation,first,v)==operation&&await Reject<DomainConflictException>(()=>Request(operation,first,v,2)),"connection diagnostic unknown-result retry retains exact actor device version and timeout");
        check(await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),first,v)),"connection diagnostics permit only one pending or running operation per device");
        check(await Reject<UnauthorizedAccessException>(()=>Claim(actor))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceConnections,object>(s=>s.ListAsync(service,null,0))),"connection requester cannot impersonate service and service execution does not grant history read");
        var c=await Claim()??throw new InvalidOperationException("Expected claim");
        var runningVersion=await JobVersion(operation);
        check(c.Id==operation&&await State(operation)=="running"&&await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(s=>s.CancelAsync(actor,operation,runningVersion))),"connection claim is durable and running diagnostics cannot claim guaranteed cancellation");
        check(await Reject<DomainConflictException>(()=>Finish(c,"tcp-reachable",token:Guid.NewGuid()))&&await Reject<ArgumentException>(()=>Finish(c,"connected")),"connection results require exact service claim and never invent authenticated connection status");
        await Finish(c,"unsupported");
        check(await State(operation)=="unsupported"&&await Reject<DomainConflictException>(()=>Finish(c,"tcp-reachable")),"connection terminal results are immutable and cannot be replayed as a different outcome");
        using(var page=JsonDocument.Parse(JsonSerializer.Serialize(await Use<DeviceConnections,object>(s=>s.ListAsync(reader,first,0)))))
            check(page.RootElement.GetProperty("items").GetArrayLength()==1&&!page.RootElement.ToString().Contains("127.0.0.1",StringComparison.Ordinal),"connection history is bounded metadata without endpoint credentials or exception payloads");
        var cancel=Guid.NewGuid();await Request(cancel,first,v);var cv=await JobVersion(cancel);await Use<DeviceConnections,Guid>(s=>s.CancelAsync(actor,cancel,cv));
        check(await State(cancel)=="cancelled"&&await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(s=>s.CancelAsync(actor,cancel,cv))),"connection pending cancellation uses optimistic version and retains a terminal history row");
        var revoked=Guid.NewGuid();await Request(revoked,first,v);await Query(db=>db.RolePermissions.Where(x=>x.RoleId==actorRole&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==DeviceConnections.Test)).ExecuteDeleteAsync());
        check(await Claim()==null&&await State(revoked)=="revoked"&&await Reject<UnauthorizedAccessException>(()=>Request(revoked,first,v)),"connection request revocation prevents queued execution and same-operation replay");
        await Query(async db=>{var p=await db.Permissions.SingleAsync(p=>p.Code==DeviceConnections.Test);db.RolePermissions.Add(new(){RoleId=actorRole,PermissionId=p.Id});await db.SaveChangesAsync();return true;});
        var changed=Guid.NewGuid();await Request(changed,first,v);await Query(db=>db.Devices.Where(x=>x.Id==first).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Name,"Changed connection device")));
        check(await Claim()==null&&await State(changed)=="device-changed","connection diagnostics reject metadata or credential version changes before dispatch");
        var a=Guid.NewGuid();var b=Guid.NewGuid();await Request(a,first,await Version(first));await Request(b,second,await Version(second));
        var claims=await Task.WhenAll(Claim(),Claim());
        check(claims.All(x=>x is not null)&&claims.Select(x=>x!.DeviceId).Distinct().Count()==2,"connection independent worker slots claim distinct devices without blocking behind one device network wait");
        await Finish(claims[0]!,"interrupted");await Finish(claims[1]!,"unsupported");
        var expire=Guid.NewGuid();await Request(expire,first,await Version(first));var abandoned=await Claim()??throw new InvalidOperationException("Missing expiry claim");
        clock.Advance(TimeSpan.FromMinutes(3));
        check(await Claim()==null&&await State(expire)=="interrupted"&&await Reject<DomainConflictException>(()=>Finish(abandoned,"tcp-reachable")),"connection expired claims become interrupted without replay and late results cannot overwrite recovery");
        var sqlGuard=false;try{await Query(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DeviceConnections WHERE Id={operation}"));}catch(SqlException e)when(e.Number==51016){sqlGuard=true;}
        check(sqlGuard,"connection database refuses deletion of diagnostic history");
        var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();var port=((IPEndPoint)listener.LocalEndpoint).Port;
        var diagnostic=new DeviceConnections.Claim(Guid.NewGuid(),Guid.NewGuid(),first,"zk-standalone","127.0.0.1",port,1);
        try{
            check(await TcpConnectionDiagnostic.RunAsync(diagnostic,[])=="endpoint-denied"&&await TcpConnectionDiagnostic.RunAsync(diagnostic with{Profile="zk-push"},[])=="unsupported","connection transport denies unpinned endpoints and never probes unsupported PUSH or file profiles");
            check(await TcpConnectionDiagnostic.RunAsync(diagnostic,[new(first,"127.0.0.1",port)])=="tcp-reachable","connection loopback TCP diagnostic reports reachability only and is not hardware acceptance");
        }finally{listener.Stop();}
        check(await TcpConnectionDiagnostic.RunAsync(diagnostic,[new(first,"127.0.0.1",port)])=="unreachable","connection refused TCP transport returns a fixed safe failure without raw exception text");
        using var stopped=new CancellationTokenSource();stopped.Cancel();
        check(await TcpConnectionDiagnostic.RunAsync(diagnostic,[new(first,"127.0.0.1",port)],stopped.Token)=="interrupted","connection stopping token prevents a successful diagnostic result from being fabricated");
        var stalled=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slow=TcpProbeDeadline.RunAsync(_=>stalled.Task,1);
        var fast=TcpProbeDeadline.RunAsync(_=>Task.CompletedTask,1);
        check(await Task.WhenAny(fast,slow)==fast&&await fast=="tcp-reachable","connection one stalled connector does not block another independent diagnostic");
        check(await slow=="timed-out","connection outer deadline bounds a connector even when it ignores cancellation; simulator only");
        stalled.SetResult();
        var nativeId=Guid.NewGuid();await Request(nativeId,first,await Version(first));var nativeClaim=await Claim()??throw new InvalidOperationException("Expected native credential claim");
        Task<DeviceCommunicationKey?> Credential(bool allowDefault,Guid? user=null)=>Use<DeviceConnectionCredential,DeviceCommunicationKey?>(s=>s.LoadAsync(user??service,nativeClaim,allowDefault));
        check(await Credential(false)==null&&(await Credential(true))?.Value==0,"native credential use requires a protected value or explicit operator opt-in to the SDK default key");
        check(await Reject<UnauthorizedAccessException>(()=>Credential(true,actor)),"native protected credential cannot be retrieved through requester or read grants");
        bool bound=true;foreach(var forgedClaim in new[]{nativeClaim with{Host="10.99.99.99"},nativeClaim with{Port=1},nativeClaim with{Profile="file"},nativeClaim with{TimeoutSeconds=60}})bound&=await Use<DeviceConnectionCredential,DeviceCommunicationKey?>(s=>s.LoadAsync(service,forgedClaim,true))==null;
        check(bound,"parity diagnostic credentials bind exact host port profile and timeout, not just an owned token");
        var protectedKey=provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("IO.Devices.Credential.v1",first.ToString("N")).Protect("81237");
        await Query(async db=>{db.DeviceSecrets.Add(new(){DeviceId=first,ProtectedValue=protectedKey});await db.SaveChangesAsync();return true;});
        var loadedKey=await Credential(false);
        check(loadedKey is not null&&loadedKey.Value==81237&&!loadedKey.ToString().Contains("81237",StringComparison.Ordinal),"native credential is unprotected only inside the exact owned claim and string rendering is redacted");
        var wrongPurpose=provider.GetRequiredService<IDataProtectionProvider>().CreateProtector("IO.Devices.Credential.v1",second.ToString("N")).Protect("81237");
        await Query(db=>db.DeviceSecrets.Where(x=>x.DeviceId==first).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.ProtectedValue,wrongPurpose)));
        check(await Credential(false)==null,"native wrong-purpose credentials fail closed without leaking protected or plaintext values");
        await Finish(nativeClaim,"sdk-unavailable");
        var downBlocked=false;try{await Query(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112020000_DeviceConnections");return true;});}catch(SqlException e)when(e.Number==51016){downBlocked=true;}
        // A completed newer empty Down may already have committed before the native
        // history guard rejects this downgrade. Restore latest, retaining its guard.
        await Query(async db=>{await db.GetService<IMigrator>().MigrateAsync();return true;});
        check(downBlocked&&await State(nativeId)=="sdk-unavailable","native probe result history prevents a downgrade that would invalidate its outcome");
        await Query(db=>db.RolePermissions.Where(x=>x.RoleId==serviceRole||x.RoleId==readerRole).ExecuteDeleteAsync());
        check(await Reject<UnauthorizedAccessException>(()=>Claim())&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceConnections,object>(s=>s.ListAsync(reader,null,0))),"connection service and history grant revocations apply on the next operation");
    }
    private sealed class DiagnosticClock : TimeProvider
    {
        private DateTimeOffset now=DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan value)=>now=now.Add(value);
    }
}
