using System.Security.Cryptography;
using System.Text.Json;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

internal static class DeviceRegistryTests
{
    public static async Task RunAsync(string connection, Action<bool,string> check)
    {
        var keys = new EphemeralDataProtectionProvider();
        var services = new ServiceCollection(); services.AddLogging(); services.AddIoIdentityStorage(connection);
        services.AddSingleton<IDataProtectionProvider>(keys);
        await using var provider = services.BuildServiceProvider();
        var company=Guid.NewGuid();var otherCompany=Guid.NewGuid();var org=Guid.NewGuid();var site=Guid.NewGuid();var otherSite=Guid.NewGuid();
        var actor=Guid.NewGuid();var outsider=Guid.NewGuid();var receiver=Guid.NewGuid();var creator=Guid.NewGuid();
        var role=Guid.NewGuid();var receiveRole=Guid.NewGuid();var createRole=Guid.NewGuid();
        async Task<T> Use<S,T>(Func<S,Task<T>> action) where S:notnull
        {await using var scope=provider.CreateAsyncScope();return await action(scope.ServiceProvider.GetRequiredService<S>());}
        Task<T> Query<T>(Func<IoDbContext,Task<T>> action)=>Use<IoDbContext,T>(action);
        async Task<bool> Reject<E>(Func<Task> action) where E:Exception {try{await action();return false;}catch(E){return true;}}
        await Query(async db=>{
            db.Organizations.Add(new(){Id=org,Name="Device fixture organization"});
            db.Companies.AddRange(new CompanyRecord{Id=company,OrganizationId=org,Name="Device fixture company"},new CompanyRecord{Id=otherCompany,OrganizationId=org,Name="Device fixture other company"});
            db.Sites.AddRange(new SiteRecord{Id=site,CompanyId=company,Name="Device fixture site",TimeZoneId="UTC"},new SiteRecord{Id=otherSite,CompanyId=otherCompany,Name="Device fixture other site",TimeZoneId="UTC"});
            db.Users.AddRange(new UserRecord{Id=actor,UserName="devices-admin",IsActive=true},new UserRecord{Id=outsider,UserName="devices-outsider",IsActive=true},new UserRecord{Id=receiver,UserName="devices-receiver",IsActive=true},new UserRecord{Id=creator,UserName="devices-creator",IsActive=true});
            db.Roles.AddRange(new RoleRecord{Id=role,Name="devices-admin"},new RoleRecord{Id=receiveRole,Name="devices-receiver"},new RoleRecord{Id=createRole,Name="devices-creator"});
            db.UserRoles.AddRange(new UserRoleRecord{UserId=actor,RoleId=role},new UserRoleRecord{UserId=receiver,RoleId=receiveRole},new UserRoleRecord{UserId=creator,RoleId=createRole});
            foreach(var (r,codes)in new[]{(role,new[]{DeviceRegistry.Read,DeviceRegistry.Create,DeviceRegistry.Update,DeviceRegistry.Credentials}),(receiveRole,new[]{"devices.receive"}),(createRole,new[]{DeviceRegistry.Create})})
                foreach(var code in codes){var p=db.Permissions.Local.FirstOrDefault(p=>p.Code==code)??await db.Permissions.SingleOrDefaultAsync(p=>p.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}
            await db.SaveChangesAsync();return true;
        });
        var input=new DeviceRegistry.Input(site,"S04-A","<fixture>","declared model","zk-standalone","192.0.2.10",4370,true);
        Task<Guid> Create(Guid id,DeviceRegistry.Input? value=null,Guid? user=null)=>Use<DeviceRegistry,Guid>(s=>s.CreateAsync(user??actor,id,value??input));
        Task<Guid> Update(Guid id,byte[] version,DeviceRegistry.Input value)=>Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,id,version,value));
        Task<byte[]> Version(Guid id)=>Query(db=>db.Devices.Where(d=>d.Id==id).Select(d=>d.Version).SingleAsync());
        Task<Guid> Secret(Guid id,byte[] version,string? secret,Guid? user=null)=>Use<DeviceRegistry,Guid>(s=>s.SetCredentialAsync(user??actor,id,version,secret));
        check(await Reject<UnauthorizedAccessException>(()=>Create(Guid.NewGuid(),user:outsider))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceRegistry,object>(s=>s.ListAsync(outsider,null,0))),"devices deny ungranted inventory read and creation without role-name shortcuts");
        var first=Guid.NewGuid();var second=Guid.NewGuid();await Create(first);await Create(second,input with{Number="S04-B",Host="192.0.2.11"});
        check(await Query(db=>db.Devices.CountAsync(d=>d.Id==first||d.Id==second))==2&&await Query(db=>db.EventSources.CountAsync(s=>s.Id==first||s.Id==second))==2,"devices persist two independent registrations and event sources in one transaction each");
        check(await Create(first)==first&&await Reject<DomainConflictException>(()=>Create(first,input with{Name="changed replay"})),"devices same create operation retries once while altered payload conflicts");
        check(await Reject<DomainConflictException>(()=>Create(Guid.NewGuid(),input with{Number=" s04-a "})),"devices normalized case and boundary spaces cannot bypass unique device number");
        check(await Reject<ArgumentException>(()=>Create(Guid.NewGuid(),input with{Number="bad number"}))&&await Reject<ArgumentException>(()=>Create(Guid.NewGuid(),input with{Host="http://user:secret@example.test/path"}))&&await Reject<ArgumentException>(()=>Create(Guid.NewGuid(),input with{Profile="zk-push"}))&&await Reject<ArgumentException>(()=>Create(Guid.NewGuid(),input with{Port=null}))&&await Reject<ArgumentException>(()=>Create(Guid.NewGuid(),input with{SiteId=Guid.NewGuid()})),"devices reject invalid identity profile endpoint port and unknown location before writes");
        var version=await Version(first);var renamed=input with{Name="renamed inventory",Enabled=false};await Update(first,version,renamed);
        check(await Reject<DomainConflictException>(()=>Update(first,version,input))&&await Query(db=>db.EventSources.Where(s=>s.Id==first).Select(s=>s.Name).SingleAsync())==renamed.Name,"devices optimistic edit prevents stale overwrite and updates source metadata atomically");
        check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(creator,first,version,input)))&&await Reject<UnauthorizedAccessException>(()=>Secret(first,version,"denied",creator)),"devices creation permission does not imply update or credential rights");
        const string secret="Synthetic device secret 04!";
        await Secret(first,await Version(first),secret);
        var ciphertext=await Query(db=>db.DeviceSecrets.Where(s=>s.DeviceId==first).Select(s=>s.ProtectedValue).SingleAsync());
        var view=JsonSerializer.Serialize(await Use<DeviceRegistry,object>(s=>s.GetAsync(actor,first)));
        check(ciphertext!=secret&&!ciphertext.Contains(secret,StringComparison.Ordinal)&&keys.CreateProtector("IO.Devices.Credential.v1",first.ToString("N")).Unprotect(ciphertext)==secret&&!view.Contains(secret,StringComparison.Ordinal)&&!view.Contains(ciphertext,StringComparison.Ordinal),"devices protect communication secret at rest and never expose plaintext or ciphertext in inventory API");
        version=await Version(first);
        var protectedEndpoint=await Reject<DomainConflictException>(()=>Update(first,version,renamed with{Host="192.0.2.99"}));
        await Secret(first,version,secret+" rotated");
        check(protectedEndpoint&&await Reject<DomainConflictException>(()=>Update(first,version,renamed)),"devices stored credential cannot be retargeted and credential change invalidates stale metadata edit");
        await Secret(first,await Version(first),null);
        check(!await Query(db=>db.DeviceSecrets.AnyAsync(s=>s.DeviceId==first)),"devices explicit credential removal clears only protected value and preserves device history");
        check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceRegistry,object>(s=>s.GetAsync(receiver,first)))&&await Reject<UnauthorizedAccessException>(()=>Create(Guid.NewGuid(),input with{Number="S04-C"},receiver)),"devices future receive permission grants neither inventory read nor creation");
        await Query(async db=>{var read=await db.Permissions.SingleAsync(p=>p.Code==DeviceRegistry.Read);db.RolePermissions.Add(new(){RoleId=receiveRole,PermissionId=read.Id});await db.SaveChangesAsync();return true;});
        var capabilities=JsonSerializer.SerializeToElement(await Use<DeviceRegistry,object>(s=>s.CapabilitiesAsync(receiver,first))).GetProperty("operations");
        check(capabilities.GetArrayLength()==4&&capabilities.EnumerateArray().All(o=>!o.GetProperty("available").GetBoolean())&&capabilities.EnumerateArray().Single(o=>o.GetProperty("permission").GetString()=="devices.receive").GetProperty("authorized").GetBoolean()&&!capabilities.EnumerateArray().Single(o=>o.GetProperty("permission").GetString()=="devices.send").GetProperty("authorized").GetBoolean(),"devices distinguish granted future command rights from unavailable unverified adapters");
        await Query(async db=>await db.RolePermissions.Where(r=>r.RoleId==receiveRole&&r.PermissionId==db.Permissions.Where(p=>p.Code==DeviceRegistry.Read).Select(p=>p.Id).Single()).ExecuteDeleteAsync());
        check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceRegistry,object>(s=>s.CapabilitiesAsync(receiver,first))),"devices read revocation denies capability and metadata requests immediately");
        await Query(db=>db.Users.Where(u=>u.Id==actor).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.IsActive,false)));
        check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceRegistry,object>(s=>s.ListAsync(actor,null,0))),"devices disabled account loses inventory access even with retained role grant");
        await Query(db=>db.Users.Where(u=>u.Id==actor).ExecuteUpdateAsync(s=>s.SetProperty(u=>u.IsActive,true)));
        var page=JsonSerializer.SerializeToElement(await Use<DeviceRegistry,object>(s=>s.ListAsync(actor,otherCompany,0)));
        check(page.GetProperty("items").GetArrayLength()==0&&await Reject<ArgumentException>(()=>Use<DeviceRegistry,object>(s=>s.ListAsync(actor,null,100001))),"devices company filter and bounded paging return only the requested metadata slice");
        await Query(async db=>{db.RawAttendanceEvents.Add(new(){Id=Guid.NewGuid(),SourceId=second,ReceivedAtUtc=DateTimeOffset.UtcNow,SourcePayload=[1]});await db.SaveChangesAsync();return true;});
        version=await Version(second);
        check(await Reject<DomainConflictException>(()=>Update(second,version,input with{Number="S04-B",SiteId=otherSite}))&&await Query(db=>db.EventSources.Where(s=>s.Id==second).Select(s=>s.SiteId).SingleAsync())==site,"devices historical raw events prevent silent company relocation and source reassignment");
        async Task<bool> Compete(Guid id){try{await Create(id,input with{Number="S04-RACE"});return true;}catch(DomainConflictException){return false;}}
        var results=await Task.WhenAll(Compete(Guid.NewGuid()),Compete(Guid.NewGuid()));
        check(results.Count(x=>x)==1&&await Query(db=>db.Devices.CountAsync(d=>d.Number=="S04-RACE"))==1,"devices concurrent distinct operations claiming one number commit at most one device");
        var identityProtected=false;
        try{await Query(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Devices SET SourceId=SourceId WHERE Id={first}"));}catch(SqlException e)when(e.Number==51014){identityProtected=true;}
        var numberProtected=false;
        try{await Query(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.Devices SET Number={"BAD SPACE"} WHERE Id={first}"));}catch(SqlException e)when(e.Number==547){numberProtected=true;}
        check(identityProtected&&numberProtected&&await Query(db=>db.Devices.AnyAsync(d=>d.Id==first)),"devices database protects source identity and rejects malformed numbers even through direct SQL");
        check(await Use<LoginIdentityDbContext,bool>(async db=>!await db.SecurityAudit.AnyAsync(a=>a.Action.Contains(secret))&&await db.SecurityAudit.AnyAsync(a=>a.Action=="devices.credential.changed:"+first.ToString("N"))),"devices credential audit records action and identity only without secret payload");
        var wrongPurpose=false;var lostKeys=false;
        try{keys.CreateProtector("IO.Devices.Credential.v1",second.ToString("N")).Unprotect(ciphertext);}catch(CryptographicException){wrongPurpose=true;}
        try{new EphemeralDataProtectionProvider().CreateProtector("IO.Devices.Credential.v1",first.ToString("N")).Unprotect(ciphertext);}catch(CryptographicException){lostKeys=true;}
        check(wrongPurpose&&lostKeys,"devices ciphertext is bound to device purpose and cannot be decrypted without original key ring");
        var downgradeRefused=false;
        try{await Query(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261111000000_DecisionReversals");return true;});}catch(SqlException e)when(e.Number==51014){downgradeRefused=true;}
        // EF commits completed Down migrations individually. A newer empty group
        // schema may already have reverted before the device-history guard stops
        // the target downgrade. Restore latest before testing other services.
        await Query(async db=>{await db.GetService<IMigrator>().MigrateAsync();return true;});
        check(downgradeRefused&&await Query(db=>db.Devices.AnyAsync(d=>d.Id==first)),"devices downgrade refuses destructive removal of registered device history");
        check(!await Query(db=>db.RawAttendanceEvents.AnyAsync(e=>e.SourceId==first))&&await Query(db=>db.Devices.Where(d=>d.Id==first).Select(d=>d.Enabled).SingleAsync())==false,"devices metadata and secret writes do not invent attendance events or connection success");
        check(JsonSerializer.SerializeToElement(await Use<DeviceRegistry,object>(s=>s.LocationsAsync(creator,0))).GetProperty("items").GetArrayLength()>0&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceRegistry,object>(s=>s.ListAsync(creator,null,0))),"devices create-only operator may choose a real location without receiving inventory-read rights");
        check(await Reject<ArgumentException>(()=>Update(first,[1],input))&&await Reject<ArgumentException>(()=>Secret(first,[1],secret)),"devices metadata and credential writes reject missing or malformed optimistic versions");
    }
}

