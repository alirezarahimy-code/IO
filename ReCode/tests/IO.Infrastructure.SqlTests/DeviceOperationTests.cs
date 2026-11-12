using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

internal static class DeviceOperationTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);var clock=new TestClock();services.AddSingleton<TimeProvider>(clock);
        await using var provider=services.BuildServiceProvider();var actor=Guid.NewGuid();var setter=Guid.NewGuid();var reader=Guid.NewGuid();var worker=Guid.NewGuid();var outsider=Guid.NewGuid();
        var fullRole=Guid.NewGuid();var setterRole=Guid.NewGuid();var readerRole=Guid.NewGuid();var workerRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var first=Guid.NewGuid();var second=Guid.NewGuid();
        async Task<T> Use<S,T>(Func<S,Task<T>> action)where S:notnull{await using var scope=provider.CreateAsyncScope();return await action(scope.ServiceProvider.GetRequiredService<S>());}
        Task<T> Query<T>(Func<IoDbContext,Task<T>> action)=>Use<IoDbContext,T>(action);
        async Task<bool> Reject<E>(Func<Task> action)where E:Exception{try{await action();return false;}catch(E){return true;}}
        await Query(async db=>{
            db.Organizations.Add(new(){Id=org,Name="Operation fixture"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Operation company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Operation site",TimeZoneId="Asia/Tehran"});
            foreach(var(id,name)in new[]{(actor,"operation-admin"),(setter,"operation-setter"),(reader,"operation-reader"),(worker,"operation-worker"),(outsider,"operation-outsider")})db.Users.Add(new(){Id=id,UserName=name,IsActive=true});
            foreach(var(r,u,name,codes)in new[]{(fullRole,actor,"operation-admin",new[]{DeviceOperations.Read,DeviceOperations.SetClock,DeviceRegistry.Create,DeviceRegistry.Update,DeviceRegistry.Credentials,DeviceGroups.Assign,DeviceConnections.Test}),(setterRole,setter,"operation-setter",new[]{DeviceOperations.SetClock}),(readerRole,reader,"operation-reader",new[]{DeviceOperations.Read}),(workerRole,worker,"operation-worker",new[]{DeviceOperations.Execute})}){
                db.Roles.Add(new(){Id=r,Name=name});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
            await db.SaveChangesAsync();return true;
        });
        var input=new DeviceRegistry.Input(site,"S06-1","Operation device","declared","zk-standalone","127.0.0.1",4370,true);
        await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,first,input));await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,second,input with{Number="S06-2"}));
        Task<byte[]> Version(Guid id)=>Query(db=>db.Devices.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
        Task<byte[]> JobVersion(Guid id)=>Query(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
        Task<string> State(Guid id)=>Query(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
        var target=new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero);var v=await Version(first);
        Task<Guid> Request(Guid id,Guid device,byte[] version,string kind="information.read",Guid? user=null,DateTimeOffset? utc=null,string? confirmation=null)=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(user??actor,id,device,version,kind,5,utc,confirmation));
        Task<DeviceOperations.Claim?> Claim(Guid? user=null)=>Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(user??worker));
        Task<Guid> Finish(DeviceOperations.Claim item,ZkOperationOutput result,Guid? token=null)=>Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,item.Id,token??item.Token,result));
        Task<Guid> Cancel(Guid id,byte[] version,Guid? user=null)=>Use<DeviceOperations,Guid>(s=>s.CancelAsync(user??actor,id,version));
        var info=new ZkDeviceInformation("fixture-serial","fixture-product",null,null,"2026-11-08 13:30:00",0,null,1,100,null,null,null,null,null,true);
        check(await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),first,v,user:outsider))&&await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),first,v,user:setter))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,object>(s=>s.ListAsync(setter,null,0))),"S06 independent information permission is not implied by clock-setting or role name");
        check(await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),first,v,"clock.set",utc:target))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),first,v,"clock.set",utc:target.ToOffset(TimeSpan.FromHours(1)),confirmation:"SET-CLOCK"))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),first,v,"clock.set",utc:target.AddMilliseconds(1),confirmation:"SET-CLOCK")),"S06 clock setting requires explicit confirmation whole UTC seconds and never silently uses host local time");
        check(DeviceOperations.LocalClock(target,"Asia/Tehran")=="2026-11-08 13:30:00"&&await Reject<ArgumentException>(()=>Task.FromResult(DeviceOperations.LocalClock(new(2026,11,1,5,30,0,TimeSpan.Zero),"America/New_York"))),"S06 site time zone conversion rejects ambiguous DST wall clocks");
        var id=Guid.NewGuid();await Request(id,first,v);
        check(await Request(id,first,v)==id&&await Reject<DomainConflictException>(()=>Request(id,first,v,user:reader))&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),first,v)),"S06 exact operation retry preserves one queue identity and rejects changed actor or duplicate work");
        check(await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),first,v,1))),"S06 typed operations and S05 diagnostics share one device execution exclusion");
        check(await Reject<UnauthorizedAccessException>(()=>Claim(actor))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,object>(s=>s.ListAsync(worker,null,0))),"S06 service execution is independent of human read or write rights");
        var claimed=await Claim()??throw new InvalidOperationException("Missing info claim");var runningVersion=await JobVersion(id);
        check(await Reject<DomainConflictException>(()=>Cancel(id,runningVersion))&&await Reject<DomainConflictException>(()=>Finish(claimed,new("succeeded",info),Guid.NewGuid())),"S06 running operations cannot guarantee cancellation and results require an owned claim token");
        check(await Reject<ArgumentException>(()=>Finish(claimed,new("succeeded")))&&await Reject<ArgumentException>(()=>Finish(claimed,new("succeeded",info with{Users=-1}))),"S06 malformed successful information and negative counters cannot be stored");
        await Finish(claimed,new("succeeded",info));
        using(var view=JsonDocument.Parse(JsonSerializer.Serialize(await Use<DeviceOperations,object>(s=>s.ListAsync(reader,first,0)))))
        {
            var item=view.RootElement.GetProperty("items")[0].GetProperty("Information");
            check(item.GetProperty("Records").GetInt32()==0&&item.GetProperty("RecordCapacity").ValueKind==JsonValueKind.Null&&!view.RootElement.ToString().Contains("CommKey",StringComparison.Ordinal),"S06 persisted snapshots preserve unavailable versus reported zero without credential data");
        }
        var cancel=Guid.NewGuid();await Request(cancel,first,v);var cv=await JobVersion(cancel);await Cancel(cancel,cv);
        check(await State(cancel)=="cancelled"&&await Reject<DomainConflictException>(()=>Cancel(cancel,cv)),"S06 pending cancellation retains history and rejects stale replay");
        var changed=Guid.NewGuid();await Request(changed,first,v);await Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,first,v,input with{Name="Changed S06 label"}));
        check(await Claim()==null&&await State(changed)=="device-changed","S06 dispatch rejects device metadata changes after an information request");v=await Version(first);
        var write=Guid.NewGuid();await Request(write,first,v,"clock.set",setter,target,"SET-CLOCK");
        check(await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,first,v,input)))&&await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.SetCredentialAsync(actor,first,v,"123"))),"S06 pending clock work prevents retargeting metadata or credential versions");
        var writeClaim=await Claim()??throw new InvalidOperationException("Missing clock claim");
        check(writeClaim.ClockLocal=="2026-11-08 13:30:00"&&(await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,writeClaim,true)))?.Value==0&&await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,writeClaim,false))==null,"S06 private credential use requires owned current claim and explicit default-key opt-in");
        bool bound=true;foreach(var forgedClaim in new[]{writeClaim with{Host="10.99.99.99"},writeClaim with{Port=1},writeClaim with{Profile="file"},writeClaim with{TimeoutSeconds=60},writeClaim with{ClockLocal="2026-11-08 00:00:00"}})bound&=await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,forgedClaim,true))==null;
        check(bound,"parity operation credentials bind exact endpoint profile budget and server-derived clock intent");
        await Finish(writeClaim,new("timed-out"));
        check(await Reject<DomainConflictException>(()=>Use<DeviceGroups,Guid>(s=>s.AssignAsync(actor,first,v,null)))&&await State(write)=="unknown"&&await Request(write,first,v,"clock.set",setter,target,"SET-CLOCK")==write&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),first,v,"clock.set",setter,target,"SET-CLOCK")),"S06 clock timeout remains unknown and exact retry never repeats a potentially completed write");
        clock.Advance(TimeSpan.FromSeconds(1));var noClock=Guid.NewGuid();await Request(noClock,first,v);var nc=await Claim()??throw new InvalidOperationException();await Finish(nc,new("succeeded",info with{ClockLocal=null}));var wv=await JobVersion(write);
        check(await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(s=>s.ResolveClockAsync(actor,write,wv,id,"REVIEWED-CLOCK")))&&await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(s=>s.ResolveClockAsync(actor,write,wv,noClock,"REVIEWED-CLOCK"))),"S06 old or unavailable clock snapshots cannot resolve an unknown physical write");
        var observation=Guid.NewGuid();await Request(observation,first,v);var oc=await Claim()??throw new InvalidOperationException();await Finish(oc,new("succeeded",info));
        check(await Reject<ArgumentException>(()=>Use<DeviceOperations,Guid>(s=>s.ResolveClockAsync(actor,write,wv,observation,""))),"S06 unknown-clock resolution requires explicit human review confirmation");
        await Use<DeviceOperations,Guid>(s=>s.ResolveClockAsync(setter,write,wv,observation,"REVIEWED-CLOCK"));
        check(await State(write)=="unknown"&&await Query(db=>db.DeviceOperations.Where(x=>x.Id==write).Select(x=>x.ResolutionInfoId).SingleAsync())==observation&&await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(s=>s.ResolveClockAsync(setter,write,wv,observation,"REVIEWED-CLOCK"))),"S06 explicit review appends observation evidence without rewriting the unknown outcome");
        var expired=Guid.NewGuid();await Request(expired,first,v,"clock.set",setter,target,"SET-CLOCK");clock.Advance(TimeSpan.FromMinutes(3));
        check(await Claim()==null&&await State(expired)=="expired","S06 queued clock writes expire rather than applying an old absolute target unexpectedly");
        var a=Guid.NewGuid();var b=Guid.NewGuid();await Request(a,first,v);await Request(b,second,await Version(second));var parallel=await Task.WhenAll(Claim(),Claim());
        check(parallel.All(x=>x is not null)&&parallel.Select(x=>x!.DeviceId).Distinct().Count()==2,"S06 parallel service slots claim distinct device operations without network-wide serialization");foreach(var item in parallel)await Finish(item!,new("unsupported"));
        var crash=Guid.NewGuid();await Request(crash,first,v,"clock.set",setter,target,"SET-CLOCK");var crashClaim=await Claim()??throw new InvalidOperationException();clock.Advance(TimeSpan.FromMinutes(3));
        check(await Claim()==null&&await State(crash)=="unknown"&&await Reject<DomainConflictException>(()=>Finish(crashClaim,new("succeeded"))),"S06 abandoned clock claims become unknown with no replay and late results cannot overwrite recovery");
        var revoked=Guid.NewGuid();await Request(revoked,second,await Version(second));await Query(db=>db.RolePermissions.Where(x=>x.RoleId==fullRole&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==DeviceOperations.Read)).ExecuteDeleteAsync());
        check(await Claim()==null&&await State(revoked)=="revoked"&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,object>(s=>s.ListAsync(actor,null,0))),"S06 read permission revocation denies snapshots and prevents queued reads on the next dispatch");
        bool guard=false;try{await Query(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DeviceOperations WHERE Id={id}"));}catch(SqlException e)when(e.Number==51017){guard=true;}
        bool down=false;try{await Query(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112030000_ZkConnectionProbe");return true;});}catch(SqlException e)when(e.Number==51017){down=true;}
        await Query(async db=>{await db.GetService<IMigrator>().MigrateAsync();return true;});
        check(guard&&down&&await State(id)=="succeeded","S06 SQL history guard and downgrade guard retain operation and snapshot evidence");
        await Query(db=>db.RolePermissions.Where(x=>x.RoleId==workerRole).ExecuteDeleteAsync());
        check(await Reject<UnauthorizedAccessException>(()=>Claim()),"S06 service execution revocation is enforced on the next claim");
    }
    private sealed class TestClock:TimeProvider{private DateTimeOffset value=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>value;public void Advance(TimeSpan amount)=>value=value.Add(amount);}
}
