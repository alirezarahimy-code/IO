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
internal static class DeviceUserDownloadTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);var clock=new Clock();services.AddSingleton<TimeProvider>(clock);await using var provider=services.BuildServiceProvider();
        var actor=Guid.NewGuid();var reader=Guid.NewGuid();var service=Guid.NewGuid();var outsider=Guid.NewGuid();var fullRole=Guid.NewGuid();var readRole=Guid.NewGuid();var serviceRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var employeeA=Guid.NewGuid();var employeeB=Guid.NewGuid();var d1=Guid.NewGuid();var d2=Guid.NewGuid();
        async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
        Task<T> Query<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
        async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
        await Query(async db=>{
            db.Organizations.Add(new(){Id=org,Name="User read fixture"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="User read company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="User read site",TimeZoneId="UTC"});
            db.Employees.AddRange(new EmployeeRecord{Id=employeeA,CompanyId=company,PersonnelNumber="001",DisplayName="Employee unchanged"},new EmployeeRecord{Id=employeeB,CompanyId=company,PersonnelNumber="002",DisplayName="Second employee"});
            foreach(var(u,n)in new[]{(actor,"download-actor"),(reader,"download-reader"),(service,"download-service"),(outsider,"download-outsider")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
            foreach(var(r,u,n,codes)in new[]{(fullRole,actor,"download-full",new[]{DeviceUserDownloads.Read,DeviceUserDownloads.Download,DeviceRegistry.Create,DeviceRegistry.Update,DeviceConnections.Test,DeviceOperations.Read}),(readRole,reader,"download-read",new[]{DeviceUserDownloads.Read}),(serviceRole,service,"download-service",new[]{DeviceUserDownloads.Execute})}){
                db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
            await db.SaveChangesAsync();return true;
        });
        var input=new DeviceRegistry.Input(site,"S07-A","Download device","unverified SSR","zk-standalone","127.0.0.1",4370,true);
        await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,d1,input));await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,d2,input with{Number="S07-B"}));
        Task<byte[]> Version(Guid id)=>Query(db=>db.Devices.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
        Task<byte[]> JobVersion(Guid id)=>Query(db=>db.DeviceUserDownloads.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
        Task<string> State(Guid id)=>Query(db=>db.DeviceUserDownloads.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
        var v=await Version(d1);var v2=await Version(d2);
        Task<Guid> Request(Guid id,Guid? device=null,byte[]? version=null,Guid? user=null,int seconds=30)=>Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(user??actor,id,device??d1,version??v,seconds));
        Task<DeviceUserDownloads.Claim?> Claim(Guid? user=null)=>Use<DeviceUserDownloads,DeviceUserDownloads.Claim?>(s=>s.ClaimAsync(user??service));
        Task<long> Append(DeviceUserDownloads.Claim job,long after,params ZkUser[] rows)=>Use<DeviceUserDownloads,long>(s=>s.AppendAsync(service,job.Id,job.Token,after,rows));
        Task<Guid> Complete(DeviceUserDownloads.Claim job,string state,long count)=>Use<DeviceUserDownloads,Guid>(s=>s.CompleteAsync(service,job.Id,job.Token,state,count));
        Task<Guid> Cancel(Guid id,byte[] version)=>Use<DeviceUserDownloads,Guid>(s=>s.CancelAsync(actor,id,version));
        check(await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),user:reader))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceUserDownloads,object>(s=>s.ListAsync(outsider,null,0)))&&await Reject<UnauthorizedAccessException>(()=>Claim(actor)),"S07 user snapshot read request and service execution are independent server grants");
        check(await Reject<ArgumentException>(()=>Request(Guid.Empty))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),version:[1]))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),seconds:601)),"S07 malformed ids versions and operation deadlines are rejected without imposing a device or user-count ceiling");
        var id=Guid.NewGuid();await Request(id);
        check(await Request(id)==id&&await Reject<DomainConflictException>(()=>Request(id,seconds:31))&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid())),"S07 exact request retry preserves one snapshot identity and changed retry or active duplicate is denied");
        check(await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),d1,v,1)))&&await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),d1,v,"information.read",1,null,null))),"S07 user downloads share per-device exclusion with diagnostics and typed operations");
        var job=await Claim()??throw new InvalidOperationException();
        var runningVersion=await JobVersion(id);
        check(await Reject<DomainConflictException>(()=>Cancel(id,runningVersion))&&await Reject<DomainConflictException>(()=>Use<DeviceUserDownloads,long>(s=>s.AppendAsync(service,id,Guid.NewGuid(),0,new[]{new ZkUser("001","name",null,0,true)}))),"S07 cancellation stops only pending work and stream batches require an owned claim token");
        check((await Use<DeviceUserDownloads,DeviceCommunicationKey?>(s=>s.CredentialAsync(service,job,true)))?.Value==0&&await Use<DeviceUserDownloads,DeviceCommunicationKey?>(s=>s.CredentialAsync(service,job,false))==null,"S07 private credential access fails closed unless default-key use is explicitly allowed");
        await Query(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=d1,SourcePersonId="002",EmployeeId=employeeA,CompanyId=company,ValidFromUtc=clock.GetUtcNow().AddDays(-1)});await db.SaveChangesAsync();return true;});
        ZkUser[] users=[new("001","Different device name","000123",0,true),new("001","Duplicate",null,0,false),new("002","Conflict",null,3,true),new("009","Unknown",null,0,true),new("0001","Not normalized",null,0,true)];
        check(await Append(job,0,users)==5&&await Append(job,0,users)==5&&await Query(db=>db.DeviceDownloadedUsers.CountAsync(x=>x.DownloadId==id))==5,"S07 durable sequential batches retain exact identifiers cards and duplicates while exact batch replay is idempotent");
        check(await Reject<DomainConflictException>(()=>Append(job,0,users.Select(x=>x with{Name="changed"}).ToArray()))&&await Reject<ArgumentException>(()=>Append(job,5,new ZkUser("x","n",null,8,true))),"S07 changed batch replay and malformed user data cannot alter retained rows");
        check(await Reject<DomainConflictException>(()=>Complete(job,"succeeded",99)),"S07 successful completion requires all reported stream rows to be committed");
        await Complete(job,"succeeded",5);
        var preview=JsonSerializer.SerializeToElement(await Use<DeviceUserDownloads,object>(s=>s.PreviewAsync(reader,id,0)));var rows=preview.GetProperty("items").EnumerateArray().ToArray();
        check(rows.Count(x=>x.GetProperty("status").GetString()=="duplicate-identifier")==2&&rows.Any(x=>x.GetProperty("status").GetString()=="mapping-conflict")&&rows.Count(x=>x.GetProperty("status").GetString()=="unknown-identifier")==2&&rows[0].GetProperty("nameDiffers").GetBoolean(),"S07 preview flags duplicate unknown conflicting and name-different users without guessing leading-zero equivalence");
        check(await Query(db=>db.Employees.Where(x=>x.Id==employeeA).Select(x=>x.DisplayName).SingleAsync())=="Employee unchanged"&&await Query(db=>db.SourceEmployeeMappings.CountAsync(x=>x.SourceId==d1))==1&&!preview.ToString().Contains("Password",StringComparison.Ordinal),"S07 reading and preview never overwrite employees create mappings or persist device passwords or biometric templates");
        var cursor=JsonSerializer.SerializeToElement(await Use<DeviceUserDownloads,object>(s=>s.PreviewAsync(reader,id,3)));
        check(cursor.GetProperty("items").GetArrayLength()==2&&cursor.GetProperty("nextAfter").GetInt64()==5&&await Reject<ArgumentException>(()=>Use<DeviceUserDownloads,object>(s=>s.PreviewAsync(reader,id,-1))),"S07 preview uses stable sequence cursors without a product user-count ceiling");
        var partial=Guid.NewGuid();await Request(partial);var pj=await Claim()??throw new InvalidOperationException();await Append(pj,0,new ZkUser("001","Partial",null,0,true));await Complete(pj,"timed-out",1);
        var partialView=JsonSerializer.SerializeToElement(await Use<DeviceUserDownloads,object>(s=>s.PreviewAsync(reader,partial,0)));
        check(await State(partial)=="timed-out"&&partialView.GetProperty("items")[0].GetProperty("status").GetString()=="incomplete-download"&&await Request(partial)==partial,"S07 timeout preserves partial rows and exact retry does not fabricate a complete or resumed snapshot");
        var cancelled=Guid.NewGuid();await Request(cancelled);var cv=await JobVersion(cancelled);await Cancel(cancelled,cv);
        check(await State(cancelled)=="cancelled"&&await Reject<DomainConflictException>(()=>Cancel(cancelled,cv)),"S07 pending cancellation retains immutable history and rejects stale replay");
        var a=Guid.NewGuid();var b=Guid.NewGuid();await Request(a);await Request(b,d2,v2);var claims=await Task.WhenAll(Claim(),Claim());
        check(claims.All(x=>x is not null)&&claims.Select(x=>x!.DeviceId).Distinct().Count()==2,"S07 independent service slots claim different devices without serializing hardware waits");foreach(var c in claims)await Complete(c!,"unsupported",0);
        var crashed=Guid.NewGuid();await Request(crashed);var cj=await Claim()??throw new InvalidOperationException();await Append(cj,0,new ZkUser("001","Before outage",null,0,true));clock.Advance(TimeSpan.FromMinutes(3));
        check(await Claim()==null&&await State(crashed)=="interrupted"&&await Reject<DomainConflictException>(()=>Complete(cj,"succeeded",1))&&await Query(db=>db.DeviceDownloadedUsers.CountAsync(x=>x.DownloadId==crashed))==1,"S07 restart recovery retains partial evidence without blind reread or accepting a late success");
        var changed=Guid.NewGuid();await Request(changed);await Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,d1,v,input with{Name="Changed metadata"}));v=await Version(d1);
        check(await Claim()==null&&await State(changed)=="device-changed"&&JsonSerializer.SerializeToElement(await Use<DeviceUserDownloads,object>(s=>s.PreviewAsync(reader,id,0))).GetProperty("items")[0].GetProperty("status").GetString()=="device-changed","S07 changed device versions prevent dispatch and mark older mapping previews stale");
        var revoked=Guid.NewGuid();await Request(revoked);await Query(db=>db.RolePermissions.Where(x=>x.RoleId==fullRole&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==DeviceUserDownloads.Download)).ExecuteDeleteAsync());
        check(await Claim()==null&&await State(revoked)=="revoked"&&await Reject<UnauthorizedAccessException>(()=>Request(revoked)),"S07 request revocation blocks queued work and idempotency replay on the next server operation");
        await Query(db=>db.RolePermissions.Where(x=>x.RoleId==readRole).ExecuteDeleteAsync());
        check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceUserDownloads,object>(s=>s.PreviewAsync(reader,id,0))),"S07 read revocation removes retained personal data access immediately");
        bool rowGuard=false,downGuard=false;try{await Query(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceDownloadedUsers SET Name=N'changed' WHERE DownloadId={id}"));}catch(SqlException e)when(e.Number==51018){rowGuard=true;}
        try{await Query(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112040000_DeviceOperations");return true;});}catch(SqlException e)when(e.Number==51018){downGuard=true;}
        await Query(async db=>{await db.GetService<IMigrator>().MigrateAsync();return true;});
        check(rowGuard&&downGuard&&await State(id)=="succeeded","S07 database row immutability and guarded downgrade preserve complete and incomplete snapshot evidence");
        await Query(db=>db.RolePermissions.Where(x=>x.RoleId==serviceRole).ExecuteDeleteAsync());check(await Reject<UnauthorizedAccessException>(()=>Claim()),"S07 service execution revocation is enforced independently of human read grants");
    }
    private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan amount)=>now=now.Add(amount);}
}
