using System.Text.Json;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class DeviceGroupTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();
        var actor=Guid.NewGuid();var reader=Guid.NewGuid();var assigner=Guid.NewGuid();var editor=Guid.NewGuid();var outsider=Guid.NewGuid();
        var company=Guid.NewGuid();var otherCompany=Guid.NewGuid();var site=Guid.NewGuid();var otherSite=Guid.NewGuid();var org=Guid.NewGuid();
        var role=Guid.NewGuid();var readRole=Guid.NewGuid();var assignRole=Guid.NewGuid();var editRole=Guid.NewGuid();
        async Task<T> Use<S,T>(Func<S,Task<T>> action)where S:notnull{await using var scope=provider.CreateAsyncScope();return await action(scope.ServiceProvider.GetRequiredService<S>());}
        Task<T> Query<T>(Func<IoDbContext,Task<T>> action)=>Use<IoDbContext,T>(action);
        async Task<bool> Reject<E>(Func<Task> action)where E:Exception{try{await action();return false;}catch(E){return true;}}
        await Query(async db=>{
            db.Organizations.Add(new(){Id=org,Name="Group fixture"});db.Companies.AddRange(new CompanyRecord{Id=company,OrganizationId=org,Name="Group company"},new CompanyRecord{Id=otherCompany,OrganizationId=org,Name="Other group company"});
            db.Sites.AddRange(new SiteRecord{Id=site,CompanyId=company,Name="Group site",TimeZoneId="UTC"},new SiteRecord{Id=otherSite,CompanyId=otherCompany,Name="Other group site",TimeZoneId="UTC"});
            foreach(var(id,name)in new[]{(actor,"group-admin"),(reader,"group-reader"),(assigner,"group-assigner"),(editor,"group-editor"),(outsider,"group-outsider")})db.Users.Add(new(){Id=id,UserName=name,IsActive=true});
            foreach(var(r,u,name,codes)in new[]{(role,actor,"group-admin",new[]{DeviceGroups.Read,DeviceGroups.Manage,DeviceGroups.Assign,DeviceRegistry.Read,DeviceRegistry.Create,DeviceRegistry.Update}),(readRole,reader,"group-reader",new[]{DeviceGroups.Read}),(assignRole,assigner,"group-assigner",new[]{DeviceGroups.Assign}),(editRole,editor,"group-editor",new[]{DeviceGroups.Manage})}){
                db.Roles.Add(new(){Id=r,Name=name});db.UserRoles.Add(new(){RoleId=r,UserId=u});
                foreach(var code in codes){var p=db.Permissions.Local.FirstOrDefault(p=>p.Code==code)??await db.Permissions.SingleOrDefaultAsync(p=>p.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}
            }await db.SaveChangesAsync();return true;
        });
        Task<Guid> Create(Guid id,Guid? parent,string name,Guid? co=null,Guid? user=null)=>Use<DeviceGroups,Guid>(s=>s.CreateAsync(user??actor,id,new(co??company,parent,name)));
        Task<byte[]> Version(Guid id)=>Query(db=>db.DeviceGroups.Where(g=>g.Id==id).Select(g=>g.Version).SingleAsync());
        Task<byte[]> DeviceVersion(Guid id)=>Query(db=>db.Devices.Where(g=>g.Id==id).Select(g=>g.Version).SingleAsync());
        Task<Guid> Edit(Guid id,byte[] version,Guid? parent,string name,Guid? co=null)=>Use<DeviceGroups,Guid>(s=>s.UpdateAsync(actor,id,version,new(co??company,parent,name)));
        Task<Guid> Delete(Guid id,byte[] version,Guid? user=null)=>Use<DeviceGroups,Guid>(s=>s.DeleteAsync(user??actor,id,version));
        Task<Guid> Assign(Guid id,byte[] version,Guid? group,Guid? user=null)=>Use<DeviceGroups,Guid>(s=>s.AssignAsync(user??actor,id,version,group));
        var root=Guid.NewGuid();var child=Guid.NewGuid();var leaf=Guid.NewGuid();
        check(await Reject<UnauthorizedAccessException>(()=>Create(root,null,"denied",user:outsider))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceGroups,object>(s=>s.ListAsync(outsider,null,null,0))),"device groups deny ungranted read and management independently of role labels");
        await Create(root,null,"Group root");await Create(child,root,"Group child");await Create(leaf,child,"Group leaf");
        check(await Query(db=>db.DeviceGroups.Where(g=>g.Id==leaf).Select(g=>g.ParentId).SingleAsync())==child,"device groups persist a three-level reference hierarchy without physical door operations");
        check(await Create(root,null,"Group root")==root&&await Reject<DomainConflictException>(()=>Create(root,null,"changed"))&&await Reject<DomainConflictException>(()=>Create(root,null,"Group root",user:editor)),"device groups exact create retry preserves one identity and changed payload or actor conflicts");
        await Create(Guid.NewGuid(),null,"Group root",otherCompany);await Create(Guid.NewGuid(),child,"Group root");
        check(await Reject<DomainConflictException>(()=>Create(Guid.NewGuid(),null," Group root ")),"device group names are unique among active same-company siblings but reusable in other branches and companies");
        check(await Reject<ArgumentException>(()=>Create(Guid.NewGuid(),Guid.NewGuid(),"unknown parent"))&&await Reject<ArgumentException>(()=>Create(Guid.NewGuid(),null,"   "))&&await Reject<ArgumentException>(()=>Create(Guid.NewGuid(),null,"unknown company",Guid.NewGuid()))&&await Reject<DomainConflictException>(()=>Create(root,root,"self")),"device groups reject invalid company parent blank labels and self-parent replays");
        var version=await Version(child);await Edit(child,version,root,"Group child renamed");
        check(await Reject<DomainConflictException>(()=>Edit(child,version,root,"stale"))&&await Reject<DomainConflictException>(()=>Edit(child,version,root,"foreign",otherCompany)),"device group metadata uses optimistic versions and never transfers group company");
        version=await Version(root);
        check(await Reject<DomainConflictException>(()=>Edit(root,version,leaf,"Group root")),"device groups refuse ancestor moves under descendants without modifying hierarchy");
        var a=Guid.NewGuid();var b=Guid.NewGuid();await Create(a,null,"Race root A");await Create(b,null,"Race root B");var va=await Version(a);var vb=await Version(b);
        async Task<bool> Move(Guid id,byte[] v,Guid target,string name){try{await Edit(id,v,target,name);return true;}catch(DomainConflictException){return false;}}
        var moves=await Task.WhenAll(Move(a,va,b,"Race root A"),Move(b,vb,a,"Race root B"));
        check(moves.Count(x=>x)==1,"device group opposing concurrent reparent operations cannot commit a cycle");
        var page=JsonSerializer.SerializeToElement(await Use<DeviceGroups,object>(s=>s.ListAsync(reader,company,root,0)));
        check(page.GetProperty("items").EnumerateArray().Any(g=>g.GetProperty("Id").GetGuid()==child)&&await Reject<ArgumentException>(()=>Use<DeviceGroups,object>(s=>s.ListAsync(reader,otherCompany,root,0)))&&await Reject<ArgumentException>(()=>Use<DeviceGroups,object>(s=>s.ListAsync(reader,null,null,100001))),"device group branch reads validate company parent and bounded paging rather than claiming a complete tree");
        check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceGroups,Guid>(s=>s.CreateAsync(reader,Guid.NewGuid(),new(company,null,"reader forbidden"))))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceGroups,object>(s=>s.ListAsync(editor,null,null,0))),"device group read does not imply management and management does not imply read");
        var device=Guid.NewGuid();var deviceInput=new DeviceRegistry.Input(site,"GROUP-DEVICE-01","Group device","declared","offline-file",null,null,true);
        await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,deviceInput));var dv=await DeviceVersion(device);
        await Assign(device,dv,leaf,assigner);
        check(await Query(db=>db.Devices.Where(d=>d.Id==device).Select(d=>d.GroupId).SingleAsync())==leaf&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceRegistry,object>(s=>s.GetAsync(assigner,device))),"device group assignment is an independent permission and does not grant inventory read");
        var foreign=Guid.NewGuid();await Create(foreign,null,"Foreign assignment target",otherCompany);dv=await DeviceVersion(device);
        check(await Reject<ArgumentException>(()=>Assign(device,dv,foreign))&&await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,device,dv,deviceInput with{SiteId=otherSite}))),"device group and installation company cannot diverge through assignment or metadata relocation");
        version=await Version(root);var lv=await Version(leaf);
        check(await Reject<DomainConflictException>(()=>Delete(root,version))&&await Reject<DomainConflictException>(()=>Delete(leaf,lv)),"device groups refuse deletion with active children or assigned devices including disabled inventory");
        await Assign(device,dv,null,assigner);
        check(await Reject<DomainConflictException>(()=>Assign(device,dv,leaf,assigner))&&await Query(db=>db.Devices.Where(d=>d.Id==device).Select(d=>d.GroupId).SingleAsync())==null,"device group detach preserves device identity and stale assignment version cannot reattach it");
        await Delete(leaf,lv);await Delete(leaf,lv);
        check(await Query(db=>db.DeviceGroups.Where(g=>g.Id==leaf).Select(g=>g.DeletedAtUtc).SingleAsync())!=null&&await Reject<DomainConflictException>(()=>Delete(leaf,lv,editor)),"device group leaf deletion retains a tombstone and exact same-actor delete retry is idempotent");
        dv=await DeviceVersion(device);
        check(await Reject<DomainConflictException>(()=>Create(leaf,child,"Group leaf"))&&await Reject<ArgumentException>(()=>Create(Guid.NewGuid(),leaf,"deleted parent"))&&await Reject<ArgumentException>(()=>Assign(device,dv,leaf)),"device group tombstones cannot be resurrected reused as parents or assigned to devices");
        async Task<bool> SqlGuard(string sql){try{await Query(db=>db.Database.ExecuteSqlRawAsync(sql));return false;}catch(SqlException e)when(e.Number==51015){return true;}}
        check(await SqlGuard($"UPDATE dbo.DeviceGroups SET ParentId='{child}' WHERE Id='{root}'")&&await SqlGuard($"UPDATE dbo.DeviceGroups SET ParentId='{foreign}' WHERE Id='{root}'")&&await SqlGuard($"UPDATE dbo.DeviceGroups SET Name=N'changed' WHERE Id='{leaf}'")&&await SqlGuard($"UPDATE dbo.Devices SET GroupId='{foreign}' WHERE Id='{device}'"),"device group database guards reject direct SQL cycles cross-company edges tombstone mutation and foreign device assignment");
        await Query(db=>db.RolePermissions.Where(r=>r.RoleId==assignRole||r.RoleId==readRole).ExecuteDeleteAsync());
        check(await Reject<UnauthorizedAccessException>(()=>Assign(device,dv,child,assigner))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceGroups,object>(s=>s.ListAsync(reader,null,null,0))),"device group assignment and read permission revocations apply on the next request");
        var race=Guid.NewGuid();await Create(race,null,"Attach delete race");var rv=await Version(race);dv=await DeviceVersion(device);
        async Task<bool> Attach(){try{await Assign(device,dv,race);return true;}catch(Exception e)when(e is DomainConflictException or ArgumentException){return false;}}
        async Task<bool> Remove(){try{await Delete(race,rv);return true;}catch(DomainConflictException){return false;}}
        var raceResults=await Task.WhenAll(Attach(),Remove());
        var assigned=await Query(db=>db.Devices.Where(d=>d.Id==device).Select(d=>d.GroupId).SingleAsync());
        var deleted=await Query(db=>db.DeviceGroups.Where(g=>g.Id==race).Select(g=>g.DeletedAtUtc).SingleAsync());
        check(raceResults.Count(x=>x)==1&&!(assigned==race&&deleted is not null),"device group concurrent attach and leaf deletion cannot produce an assigned deleted group");
        check(!await Query(db=>db.RawAttendanceEvents.AnyAsync(e=>e.SourceId==device))&&await Use<LoginIdentityDbContext,bool>(db=>db.SecurityAudit.AnyAsync(a=>a.Action=="device-groups.deleted:"+leaf.ToString("N")))&&await Query(db=>db.Devices.Where(d=>d.Id==device).Select(d=>d.SourceId).SingleAsync())==device,"device group operations audit metadata without raw events new sources or physical commands");
    }
}

