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
internal static class DeviceUserUploadTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);var clock=new Clock();services.AddSingleton<TimeProvider>(clock);await using var provider=services.BuildServiceProvider();
        var actor=Guid.NewGuid();var reader=Guid.NewGuid();var service=Guid.NewGuid();var full=Guid.NewGuid();var read=Guid.NewGuid();var exec=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var employee=Guid.NewGuid();var d1=Guid.NewGuid();var d2=Guid.NewGuid();
        async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
        Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
        async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
        await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Upload org"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Upload company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Upload site",TimeZoneId="UTC"});db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="0001",DisplayName="Ordinary employee"});
            foreach(var(u,n)in new[]{(actor,"upload-actor"),(reader,"upload-reader"),(service,"upload-service")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
            foreach(var(r,u,n,codes)in new[]{(full,actor,"upload-full",new[]{DeviceUserUploads.Read,DeviceUserUploads.Upload,DeviceUserDownloads.Download,DeviceRegistry.Create,DeviceRegistry.Update,DeviceConnections.Test,DeviceOperations.Read}),(read,reader,"upload-read",new[]{DeviceUserUploads.Read}),(exec,service,"upload-exec",new[]{DeviceUserUploads.Execute,DeviceUserDownloads.Execute})}){
                db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
            await db.SaveChangesAsync();return true;});
        var di=new DeviceRegistry.Input(site,"S08-A","Upload target","unverified SSR","zk-standalone","127.0.0.1",4370,true);await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,d1,di));await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,d2,di with{Number="S08-B"}));
        await Q(async db=>{foreach(var d in new[]{d1,d2})db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=d,CompanyId=company,SourcePersonId="0001",EmployeeId=employee,ValidFromUtc=clock.GetUtcNow().AddDays(-1)});await db.SaveChangesAsync();return true;});
        Task<byte[]> DV(Guid d)=>Q(db=>db.Devices.Where(x=>x.Id==d).Select(x=>x.Version).SingleAsync());
        Task<byte[]> V(Guid id)=>Q(db=>db.DeviceUserUploads.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
        Task<string> State(Guid id)=>Q(db=>db.DeviceUserUploads.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
        async Task<Guid> Snapshot(Guid d,bool absent=false){var id=Guid.NewGuid();var version=await DV(d);await Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(actor,id,d,version,30));var c=await Use<DeviceUserDownloads,DeviceUserDownloads.Claim?>(s=>s.ClaimAsync(service))??throw new InvalidOperationException();if(!absent)await Use<DeviceUserDownloads,long>(s=>s.AppendAsync(service,c.Id,c.Token,0,new[]{new ZkUser("0001","Old name","123",0,true)}));await Use<DeviceUserDownloads,Guid>(s=>s.CompleteAsync(service,c.Id,c.Token,"succeeded",absent?0:1));return id;}
        var snap1=await Snapshot(d1);var snap2=await Snapshot(d2,true);
        async Task<DeviceUserUploads.Batch> Batch(params (Guid device,Guid snapshot)[] targets){var items=new List<DeviceUserUploads.Item>();foreach(var t in targets)items.Add(new(Guid.NewGuid(),t.device,await DV(t.device),t.snapshot,employee,"0001","456",true,30));return new(Guid.NewGuid(),items.ToArray(),"UPLOAD-ORDINARY-USERS");}
        Task<Guid> Request(DeviceUserUploads.Batch b,Guid? who=null)=>Use<DeviceUserUploads,Guid>(s=>s.RequestAsync(who??actor,b));
        async Task<DeviceUserUploads.Batch> Preview(DeviceUserUploads.Batch b){var data=JsonSerializer.SerializeToElement(await Use<DeviceUserUploads,object>(s=>s.PreviewAsync(actor,b)));return b with{PreviewHash=data.GetProperty("previewHash").GetString()};}
        Task<DeviceUserUploads.Claim?> Claim()=>Use<DeviceUserUploads,DeviceUserUploads.Claim?>(s=>s.ClaimAsync(service));
        Task<Guid> Finish(DeviceUserUploads.Claim c,string outcome)=>Use<DeviceUserUploads,Guid>(s=>s.CompleteAsync(service,c.Id,c.Token,new(outcome)));
        var b=await Batch((d1,snap1),(d2,snap2));
        check(await Reject<UnauthorizedAccessException>(()=>Request(b,reader))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceUserUploads,DeviceUserUploads.Claim?>(s=>s.ClaimAsync(actor))),"S08 upload read and worker execute are independent real server grants");
        check(await Reject<DomainConflictException>(()=>Request(b))&&await Q(db=>db.DeviceUserUploads.CountAsync())==0,"S08 mutation intents require the exact server preview hash and write nothing before confirmation");
        b=await Preview(b);await Request(b);
        check(await Request(b)==b.BatchId&&await Reject<DomainConflictException>(()=>Request(b with{Items=b.Items.Select(x=>x with{Card="789"}).ToArray()}))&&await Q(db=>db.DeviceUserUploads.CountAsync(x=>x.BatchId==b.BatchId))==2,"S08 a confirmed multi-device batch retains one durable intent per person and exact retry is idempotent");
        check(await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(s=>s.RequestAsync(actor,Guid.NewGuid(),d1,b.Items[0].ExpectedDeviceVersion,1)))&&await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,d1,b.Items[0].ExpectedDeviceVersion,di with{Name="Must not retarget"}))),"S08 shared device exclusion blocks diagnostics and metadata retarget while uploads are unfinished");
        var c1=await Claim()??throw new InvalidOperationException();var c2=await Claim()??throw new InvalidOperationException();
        check(c1.DeviceId!=c2.DeviceId&&await Claim()==null&&await Reject<DomainConflictException>(()=>Use<DeviceUserUploads,Guid>(s=>s.CancelAsync(actor,c1.Id,V(c1.Id).GetAwaiter().GetResult()))),"S08 distinct devices can run independently and executing intents cannot be cancelled as if no write happened");
        check((await Use<DeviceUserUploads,DeviceCommunicationKey?>(s=>s.CredentialAsync(service,c1,true)))?.Value==0&&await Use<DeviceUserUploads,DeviceCommunicationKey?>(s=>s.CredentialAsync(service,c1,false))==null,"S08 credential dispatch requires explicit permission and default-key approval");
        await Finish(c1,"succeeded");await Finish(c2,"unknown");
        check(await State(c1.Id)=="succeeded"&&await State(c2.Id)=="unknown"&&await Reject<DomainConflictException>(()=>Finish(c2,"succeeded")),"S08 partial batch outcomes are independent immutable evidence and a late success cannot replace unknown");
        var unknownDevice=c2.DeviceId;var unknownSnapshot=unknownDevice==d1?snap1:snap2;
        check(await Reject<DomainConflictException>(async()=>await Preview(await Batch((unknownDevice,unknownSnapshot))))&&await Reject<DomainConflictException>(()=>Use<DeviceUserUploads,Guid>(s=>s.ReviewAsync(actor,c2.Id,V(c2.Id).GetAwaiter().GetResult(),unknownSnapshot,"REVIEWED-USER"))),"S08 unknown writes block blind retry and require a genuinely newer successful target snapshot");
        clock.Advance(TimeSpan.FromSeconds(1));var fresh=await Snapshot(unknownDevice);
        await Use<DeviceUserUploads,Guid>(s=>s.ReviewAsync(actor,c2.Id,V(c2.Id).GetAwaiter().GetResult(),fresh,"REVIEWED-USER"));
        var retry=await Preview(await Batch((unknownDevice,fresh)));await Request(retry);var rc=await Claim()??throw new InvalidOperationException();await Finish(rc,"conflict");
        check(await State(c2.Id)=="unknown"&&await State(rc.Id)=="conflict"&&await Q(db=>db.DeviceUserUploads.CountAsync(x=>x.BatchId==b.BatchId&&x.State=="succeeded"))==1,"S08 reviewed selective retry creates a new intent only for the selected target and does not replay prior successes");
        var cancel=await Preview(await Batch((unknownDevice,fresh)));await Request(cancel);var cancelId=cancel.Items[0].Id;await Use<DeviceUserUploads,Guid>(s=>s.CancelAsync(actor,cancelId,V(cancelId).GetAwaiter().GetResult()));
        check(await State(cancelId)=="cancelled"&&await Claim()==null,"S08 versioned pending cancellation prevents dispatch");
        var crash=await Preview(await Batch((unknownDevice,fresh)));await Request(crash);var cc=await Claim()??throw new InvalidOperationException();clock.Advance(TimeSpan.FromMinutes(3));
        check(await Claim()==null&&await State(cc.Id)=="unknown"&&await Reject<DomainConflictException>(()=>Finish(cc,"succeeded")),"S08 restart recovery records uncertainty after the full child deadline and never replays a running write");
        var otherDevice=c1.DeviceId;var otherSnapshot=otherDevice==d1?snap1:snap2;var revoked=await Preview(await Batch((otherDevice,otherSnapshot)));await Request(revoked);
        await Q(db=>db.RolePermissions.Where(x=>x.RoleId==full&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==DeviceUserUploads.Upload)).ExecuteDeleteAsync());
        check(await Claim()==null&&await State(revoked.Items[0].Id)=="revoked"&&await Reject<UnauthorizedAccessException>(()=>Request(b)),"S08 revocation blocks pending execution and exact request replay without relying on role names");
        await Q(db=>db.RolePermissions.Where(x=>x.RoleId==read).ExecuteDeleteAsync());
        check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceUserUploads,object>(s=>s.ListAsync(reader,null,0))),"S08 revoked personal-data reads disappear on the next server operation");
        bool guard=false,down=false;try{await Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceUserUploads SET DesiredJson=N'{{}}' WHERE Id={c1.Id}"));}catch(SqlException e)when(e.Number==51020){guard=true;}
        try{await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112060000_DeviceFileImports");return true;});}catch(SqlException e)when(e.Number==51020){down=true;}
        check(guard&&down&&await State(c1.Id)=="succeeded","S08 SQL intent immutability and guarded downgrade retain per-person device history");
    }
    private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan value)=>now=now.Add(value);}
}
