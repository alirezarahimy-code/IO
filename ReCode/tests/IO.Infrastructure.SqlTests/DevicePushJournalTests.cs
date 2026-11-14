using System.Text;
using System.Text.Json;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

internal static class DevicePushJournalTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);
        await using var provider=services.BuildServiceProvider();
        async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull
        {await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
        Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
        Task<T> I<T>(Func<LoginIdentityDbContext,Task<T>> f)=>Use<LoginIdentityDbContext,T>(f);
        async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
        var manager=Guid.NewGuid();var actor=Guid.NewGuid();var reader=Guid.NewGuid();var other=Guid.NewGuid();
        var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();var offline=Guid.NewGuid();
        var roles=new Dictionary<Guid,Guid>();
        await Q(async db=>
        {
            await db.Database.MigrateAsync();
            db.Organizations.Add(new(){Id=org,Name="PUSH evidence org"});
            db.Companies.Add(new(){Id=company,OrganizationId=org,Name="PUSH evidence company"});
            db.Sites.Add(new(){Id=site,CompanyId=company,Name="PUSH evidence site",TimeZoneId="UTC"});
            foreach(var(u,codes)in new[]{(manager,new[]{DeviceRegistry.Create,DeviceRegistry.Update,DeviceAccess.Manage}),
                (actor,new[]{DevicePushJournal.Retain}),(other,new[]{DevicePushJournal.Retain}),(reader,new[]{DevicePushJournal.Read})})
            {
                var role=Guid.NewGuid();roles[u]=role;db.Users.Add(new(){Id=u,UserName="push-"+u.ToString("N"),IsActive=true});
                db.Roles.Add(new(){Id=role,Name="push-"+role.ToString("N")});db.UserRoles.Add(new(){UserId=u,RoleId=role});
                foreach(var code in codes)
                {
                    var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);
                    if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}
                    db.RolePermissions.Add(new(){RoleId=role,PermissionId=p.Id});
                }
            }
            await db.SaveChangesAsync();return true;
        });
        foreach(var(deviceKey,profile)in new[]{(device,"zk-push"),(offline,"offline-file")})
            await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(manager,deviceKey,new(site,"PUSH-"+deviceKey.ToString("N"),"PUSH evidence fixture","unverified",profile,null,null,true)));
        Task<byte[]> Version(Guid deviceKey)=>Q(db=>db.Devices.Where(x=>x.Id==deviceKey).Select(x=>x.Version).SingleAsync());
        var offlineVersion=await Version(offline);var version=await Version(device);var id=Guid.NewGuid();
        var line="001\t2026-11-08 08:00:00\t0\t15\t000\t0\t0";
        var bytes=Encoding.UTF8.GetBytes(line+"\r\n"+line+"\r\nnot-seven-columns\r\n");
        var input=new DevicePushJournal.Input(id,device,version,"CLAIMED-SERIAL","opaque-stamp",bytes);
        Task<DevicePushJournal.RetentionReceipt> Retain(DevicePushJournal.Input value,Guid? user=null)=>
            Use<DevicePushJournal,DevicePushJournal.RetentionReceipt>(s=>s.RetainForReviewAsync(user??actor,value));
        Task<object> Read(Guid evidence,Guid? user=null)=>Use<DevicePushJournal,object>(s=>s.ReadAsync(user??reader,evidence));
        check(!PermissionCatalog.Templates.Any(t=>t.Permissions.Contains(DevicePushJournal.Read)||t.Permissions.Contains(DevicePushJournal.Retain)),
            "R08 evidence retention and personal-data read grants are independent and absent from default role templates");
        check(await Reject<UnauthorizedAccessException>(()=>Retain(input,reader))&&await Reject<UnauthorizedAccessException>(()=>Read(id,actor)),
            "R08 retain-only actors cannot read originals and read-only actors cannot retain evidence");
        check(await Reject<ArgumentException>(()=>Retain(input with{OperationId=Guid.Empty}))&&
            await Reject<ArgumentException>(()=>Retain(input with{Body=new byte[1048577]}))&&
            await Reject<ArgumentException>(()=>Retain(input with{Stamp="bad\nstamp"}))&&
            await Reject<ArgumentException>(()=>Retain(input with{Serial="bad\nserial"}))&&
            await Reject<ArgumentException>(()=>Retain(input with{Body=[255]})),
            "R08 invalid ids oversized bodies control metadata and invalid UTF8 fail without a retained receipt");
        check(await Reject<DomainConflictException>(()=>Retain(input with{DeviceId=offline,ExpectedDeviceVersion=offlineVersion}))&&
            await Reject<DomainConflictException>(()=>Retain(input with{ExpectedDeviceVersion=new byte[8]})),
            "R08 a file profile or stale device version cannot stage new PUSH evidence");
        var receipt=await Retain(input);
        check(receipt.Id==id&&receipt.State=="retained-unverified-requires-review"&&receipt.RowCount==3&&receipt.ParsedCount==2,
            "R08 returned committed receipt explicitly distinguishes evidence retention from verified attendance processing");
        var row=await Q(db=>db.DevicePushJournal.AsNoTracking().SingleAsync(x=>x.Id==id));
        var snapshot=JsonSerializer.Deserialize<JsonElement>(row.RowsJson);
        check(row.OriginalBody.SequenceEqual(bytes)&&row.PayloadHash==receipt.PayloadHash&&row.Serial=="CLAIMED-SERIAL"&&row.Stamp=="opaque-stamp"&&
            row.TimeZoneId=="UTC"&&row.SourceId==device&&row.SiteId==site&&row.CompanyId==company&&row.ExpectedDeviceVersion.SequenceEqual(version),
            "R08 journal pins original CRLF bytes claimed serial opaque Stamp source installation zone and device version without claiming identity verification");
        check(snapshot.GetArrayLength()==3&&snapshot[0].GetProperty("MultiplicityRank").GetInt32()==1&&
            snapshot[1].GetProperty("MultiplicityRank").GetInt32()==2&&snapshot[2].GetProperty("Status").GetString()=="layout-mismatch"&&
            snapshot[0].GetProperty("OccurredAtUtc").GetDateTimeOffset()==new DateTimeOffset(2026,11,8,8,0,0,TimeSpan.Zero),
            "R08 immutable parsed snapshot preserves repeated punches UTC interpretation and malformed-row quarantine together");
        check(!input.ToString().Contains(line,StringComparison.Ordinal)&&!row.ToString().Contains("CLAIMED-SERIAL",StringComparison.Ordinal)&&
            await I(db=>db.SecurityAudit.AnyAsync(x=>x.Action=="device-push.evidence-retained:"+id.ToString("N")))&&
            !await I(db=>db.SecurityAudit.AnyAsync(x=>x.Action.Contains("CLAIMED-SERIAL")))&&
            !await Q(db=>db.RawAttendanceEvents.AnyAsync(x=>x.SourceId==device)),
            "R08 retention commits an identifier-only audit but performs no attendance writes or default personal-data logging");
        check((await Retain(input)).Id==id&&await Q(db=>db.DevicePushJournal.CountAsync(x=>x.Id==id))==1&&
            await Reject<DomainConflictException>(()=>Retain(input with{Serial="OTHER"}))&&
            await Reject<DomainConflictException>(()=>Retain(input with{Stamp="changed"}))&&
            await Reject<DomainConflictException>(()=>Retain(input with{Body=Encoding.UTF8.GetBytes(line)}))&&
            await Reject<DomainConflictException>(()=>Retain(input,other)),
            "R08 exact operator-operation retry reuses its journal while changed actor body serial or cursor conflicts");
        var parallelId=Guid.NewGuid();
        var parallel=await Task.WhenAll(Retain(input with{OperationId=parallelId}),Retain(input with{OperationId=parallelId}));
        check(parallel.All(x=>x.Id==parallelId)&&await Q(db=>db.DevicePushJournal.CountAsync(x=>x.Id==parallelId))==1,
            "R08 simultaneous exact operation retries serialize to one retained journal receipt");
        var sameBytesId=Guid.NewGuid();await Retain(input with{OperationId=sameBytesId});
        check(await Q(db=>db.DevicePushJournal.CountAsync(x=>x.DeviceId==device&&x.PayloadHash==receipt.PayloadHash))==3,
            "R08 a new explicit operation preserves identical batches instead of treating a hash or Stamp as a vendor event id");
        var original=JsonSerializer.SerializeToElement(await Read(id));
        check(Convert.FromBase64String(original.GetProperty("originalBase64").GetString()!).SequenceEqual(bytes)&&
            original.GetProperty("state").GetString()==receipt.State,
            "R08 independent scoped evidence read returns exact original bytes and explicit unverified state");
        // Force identity-audit persistence to fail AFTER the domain SaveChanges. Both share the same transaction.
        await I(db=>db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.SecurityAudit WITH NOCHECK ADD CONSTRAINT CK_Test_PushAuditFailure CHECK ([Action] NOT LIKE N'device-push.evidence-retained:%')"));
        var failed=Guid.NewGuid();
        try
        {
            check(await Reject<DbUpdateException>(()=>Retain(input with{OperationId=failed}))&&
                !await Q(db=>db.DevicePushJournal.AnyAsync(x=>x.Id==failed)),
                "R08 failed audit save rolls back already-saved domain evidence and returns no retention receipt");
        }
        finally{await I(db=>db.Database.ExecuteSqlRawAsync("ALTER TABLE dbo.SecurityAudit DROP CONSTRAINT CK_Test_PushAuditFailure"));}
        check((await Retain(input with{OperationId=failed})).Id==failed,
            "R08 a deliberate retry after rollback can commit the same operation without a phantom prior receipt");
        async Task<bool> SqlReject(Func<Task> f,int number)
        {try{await f();return false;}catch(SqlException e)when(e.Number==number){return true;}}
        check(await SqlReject(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DevicePushJournal SET Stamp=N'changed' WHERE Id={id}")),51028)&&
            await SqlReject(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DevicePushJournal WHERE Id={id}")),51028),
            "R08 SQL triggers forbid original evidence or receipt mutation and deletion");
        var badHashId=Guid.NewGuid();
        check(await SqlReject(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.DevicePushJournal SELECT {badHashId},DeviceId,SourceId,SiteId,CompanyId,RetainedByUserId,RetainedAtUtc,ExpectedDeviceVersion,0x01,Serial,Stamp,Contract,TimeZoneId,PayloadHash,RowsJson,[RowCount],ParsedCount FROM dbo.DevicePushJournal WHERE Id={id}")),547),
            "R08 SQL independently verifies original payload SHA256 instead of trusting a supplied hash");
        var badCountId=Guid.NewGuid();
        check(await SqlReject(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"INSERT dbo.DevicePushJournal SELECT {badCountId},DeviceId,SourceId,SiteId,CompanyId,RetainedByUserId,RetainedAtUtc,ExpectedDeviceVersion,OriginalBody,Serial,Stamp,Contract,TimeZoneId,PayloadHash,RowsJson,[RowCount]+1,ParsedCount FROM dbo.DevicePushJournal WHERE Id={id}")),51028),
            "R08 SQL rejects a snapshot whose row counts disagree with its retained receipt");
        check(await SqlReject(()=>Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261114100000_DeviceProfileSettings");return true;}),51028)&&
            await Q(db=>db.DevicePushJournal.AnyAsync(x=>x.Id==id)),
            "R08 destructive downgrade refuses to discard retained PUSH evidence and leaves the migration applied");
        await Q(db=>db.Sites.Where(x=>x.Id==site).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TimeZoneId,"Asia/Tehran")));
        await Retain(input);
        check(await Q(db=>db.DevicePushJournal.Where(x=>x.Id==id).Select(x=>x.TimeZoneId).SingleAsync())=="UTC",
            "R08 a lost-response retry never reinterprets its immutable snapshot after installation zone changes");
        await Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(manager,actor,null,true,"RESTRICT-DEVICE-OPERATOR"));
        await Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(manager,reader,null,true,"RESTRICT-DEVICE-OPERATOR"));
        check(await Reject<ArgumentException>(()=>Retain(input))&&await Reject<ArgumentException>(()=>Read(id)),
            "R08 restricted actors without a device grant cannot replay or read retained evidence by substituting a known id");
        var ag=Guid.NewGuid();var rg=Guid.NewGuid();
        await Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,ag,actor,device));
        await Use<DeviceAccess,Guid>(s=>s.GrantAsync(manager,rg,reader,device));
        check((await Retain(input)).Id==id&&(await Read(id)) is not null,
            "R08 explicit device grants narrow existing retain and read rights without inventing cryptographic device identity");
        var rv=await I(db=>db.DeviceOperatorGrants.Where(x=>x.Id==rg).Select(x=>x.Version).SingleAsync());
        await Use<DeviceAccess,Guid>(s=>s.RevokeAsync(manager,rg,rv));
        check(await Reject<ArgumentException>(()=>Read(id)),"R08 device-grant revocation immediately removes original evidence access");
        await Q(db=>db.RolePermissions.Where(x=>x.RoleId==roles[actor]).ExecuteDeleteAsync());
        check(await Reject<UnauthorizedAccessException>(()=>Retain(input)),"R08 retain permission revocation also denies an otherwise exact retry");
        await Q(db=>db.Devices.Where(x=>x.Id==device).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.Enabled,false)));
        check(await Reject<DomainConflictException>(()=>Retain(input,other)),"R08 disabled device rejects retries even when the operator retains its grant");
    }
}
