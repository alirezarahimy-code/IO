using System.Text;
using System.Text.Json;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
internal static class DeviceFileImportTests
{
    public static async Task RunAsync(string connection,Action<bool,string> check)
    {
        var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();var actor=Guid.NewGuid();var reader=Guid.NewGuid();var previewer=Guid.NewGuid();var committer=Guid.NewGuid();var fullRole=Guid.NewGuid();var readRole=Guid.NewGuid();var previewRole=Guid.NewGuid();var commitRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var employee=Guid.NewGuid();var second=Guid.NewGuid();var device=Guid.NewGuid();
        async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
        Task<T> Query<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
        async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
        await Query(async db=>{
            db.Organizations.Add(new(){Id=org,Name="File fixture"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="File company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="File site",TimeZoneId="UTC"});db.Employees.AddRange(new EmployeeRecord{Id=employee,CompanyId=company,PersonnelNumber="001",DisplayName="Untouched file employee"},new EmployeeRecord{Id=second,CompanyId=company,PersonnelNumber="099",DisplayName="Second employee"});
            foreach(var(u,n)in new[]{(actor,"file-actor"),(reader,"file-reader"),(previewer,"file-preview"),(committer,"file-commit")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
            foreach(var(r,u,n,codes)in new[]{(fullRole,actor,"file-full",new[]{DeviceFileImports.Read,DeviceFileImports.Preview,DeviceFileImports.Commit,"attendance.events.receive",DeviceRegistry.Create,DeviceRegistry.Update}),(readRole,reader,"file-read",new[]{DeviceFileImports.Read}),(previewRole,previewer,"file-preview",new[]{DeviceFileImports.Preview}),(commitRole,committer,"file-commit",new[]{DeviceFileImports.Commit})}){
                db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
            await db.SaveChangesAsync();return true;
        });
        var deviceInput=new DeviceRegistry.Input(site,"S12-FILE","USB file device","reference declared","offline-file",null,null,true);await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,deviceInput));var dv=await Query(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
        await Query(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=device,SourcePersonId="001",EmployeeId=employee,CompanyId=company,ValidFromUtc=new(2026,1,1,0,0,0,TimeSpan.Zero)});await db.SaveChangesAsync();return true;});
        var text="\uFEFF001\t2026-11-08 08:00:00\t1\t4\t1\r\n001\t2026-11-08 08:00:00\t1\t4\t1\r\n099\t2026-11-08 08:01:00\t1\t4\t1\r\n001\t2026-02-30 08:00:00\t1\t4\t1\r\n001\t2026-11-08 17:00:00\t1\t5\t1\r\n001\t2026-11-08 12:00:00\t2\t4\t1\r\n001\t2026-11-08 12:00:00\t1\t9\t1\r\n";
        var bytes=Encoding.UTF8.GetBytes(text);var id=Guid.NewGuid();var input=new DeviceFileImports.Input(id,device,dv,"gregorian",1,4,5,Convert.ToBase64String(bytes));
        Task<Guid> Create(DeviceFileImports.Input value,Guid? user=null)=>Use<DeviceFileImports,Guid>(s=>s.CreateAsync(user??actor,value));
        Task<byte[]> Version(Guid value)=>Query(db=>db.DeviceFileImports.Where(x=>x.Id==value).Select(x=>x.Version).SingleAsync());
        Task<object> Commit(Guid value,byte[] version,Guid? user=null,string ack="IMPORT-READY-ROWS")=>Use<DeviceFileImports,object>(s=>s.CommitPageAsync(user??actor,value,version,0,ack));
        check(await Reject<UnauthorizedAccessException>(()=>Create(input,reader))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceFileImports,object>(s=>s.ListAsync(previewer,null,0))),"S12 file preview and personal file/history reads are independent server grants");
        check(await Reject<ArgumentException>(()=>Create(input with{FileBase64="not-base64"}))&&await Reject<ArgumentException>(()=>Create(input with{FileBase64=Convert.ToBase64String(new byte[]{255})}))&&await Reject<ArgumentException>(()=>Create(input with{InCode=5}))&&await Reject<ArgumentException>(()=>Create(input with{Calendar="guess"})),"S12 corrupt encoding malformed base64 guessed calendar and ambiguous check-code interpretation are rejected");
        check(await Reject<ArgumentException>(()=>Create(input with{FileBase64=Convert.ToBase64String(Encoding.UTF8.GetBytes("001\t2026-11-08 08:00:00\t1\t4\t1\t0"))})),"S12 a different vendor layout cannot silently pass as the Personnel five-column format");
        await Create(input);check(await Create(input)==id&&await Reject<DomainConflictException>(()=>Create(input with{OutCode=6}))&&await Reject<DomainConflictException>(()=>Create(input,previewer))&&await Reject<DomainConflictException>(()=>Create(input with{OperationId=Guid.NewGuid()})),"S12 exact preview retry is stable while changed actor interpretation or a second import of identical device bytes conflicts");
        var original=JsonSerializer.SerializeToElement(await Use<DeviceFileImports,object>(s=>s.OriginalAsync(reader,id)));
        check(Convert.FromBase64String(original.GetProperty("fileBase64").GetString()!).SequenceEqual(bytes)&&!await Query(db=>db.RawAttendanceEvents.AnyAsync(x=>x.SourceId==device)),"S12 preview retains exact original BOM CRLF and file bytes without creating any attendance event");
        var preview=JsonSerializer.SerializeToElement(await Use<DeviceFileImports,object>(s=>s.RowsAsync(reader,id,0)));var rows=preview.GetProperty("items").EnumerateArray().ToArray();
        check(rows.Count(x=>x.GetProperty("status").GetString()=="ready")==2&&rows.Any(x=>x.GetProperty("status").GetString()=="unknown-employee")&&rows.Any(x=>x.GetProperty("status").GetString()=="duplicate-in-file-requires-review")&&rows.Any(x=>x.GetProperty("status").GetString()=="invalid-time")&&rows.Any(x=>x.GetProperty("status").GetString()=="machine-mismatch")&&rows.Any(x=>x.GetProperty("status").GetString()=="unmapped-check-code"),"S12 database-backed preview distinguishes mapped ready rows from unknown duplicate invalid-time foreign-machine and unmapped-code quarantine");
        var version=await Version(id);
        check(await Reject<UnauthorizedAccessException>(()=>Commit(id,version,reader))&&await Reject<UnauthorizedAccessException>(()=>Commit(id,version,committer))&&await Reject<ArgumentException>(()=>Commit(id,version,ack:"")),"S12 commit requires its own grant plus attendance receive authority and explicit page confirmation");
        var result=JsonSerializer.SerializeToElement(await Commit(id,version));
        check(result.GetProperty("added").GetInt32()==2&&result.GetProperty("quarantined").GetInt32()==5&&await Query(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device))==2,"S12 confirmed ready rows enter the real raw stream while five rejected rows remain in retained file quarantine");
        var raw=await Query(db=>db.RawAttendanceEvents.Where(x=>x.SourceId==device).OrderBy(x=>x.OccurredAtUtc).ToListAsync());
        check(raw[0].ReportedEventType=="in"&&raw[1].ReportedEventType=="out"&&raw.All(x=>x.SourceEmployeeIdentifier=="001"&&x.SourceTimeZoneId=="UTC"&&x.ExternalEventId!.StartsWith("usb-file:",StringComparison.Ordinal))&&Encoding.UTF8.GetString(raw[0].SourcePayload)=="001\t2026-11-08 08:00:00\t1\t4\t1","S12 explicit check codes produce in/out envelopes with exact source identity UTC raw row payload and file-local rather than invented device-native ids");
        check(await Reject<DomainConflictException>(()=>Commit(id,version)),"S12 stale page version after a committed response cannot silently replay with changed review state");
        var replay=JsonSerializer.SerializeToElement(await Commit(id,await Version(id)));
        check(replay.GetProperty("added").GetInt32()==0&&replay.GetProperty("already").GetInt32()==2&&await Query(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device))==2,"S12 deliberate re-review with a fresh version reuses exact receipts without duplicate raw events");
        await Query(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=device,SourcePersonId="099",EmployeeId=second,CompanyId=company,ValidFromUtc=new(2026,1,1,0,0,0,TimeSpan.Zero)});await db.SaveChangesAsync();return true;});
        var retried=JsonSerializer.SerializeToElement(await Commit(id,await Version(id)));
        check(retried.GetProperty("added").GetInt32()==1&&retried.GetProperty("quarantined").GetInt32()==4&&await Query(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device))==3&&await Query(db=>db.Employees.Where(x=>x.Id==employee).Select(x=>x.DisplayName).SingleAsync())=="Untouched file employee","S12 explicit re-review can release a newly mapped quarantined row without overwriting employees or replaying completed rows");
        var trace=JsonSerializer.SerializeToElement(await Use<DeviceFileImports,object>(s=>s.RowsAsync(reader,id,0)));
        check(trace.GetProperty("items").EnumerateArray().Count(x=>x.GetProperty("rawEventId").ValueKind==JsonValueKind.String)==3&&trace.GetProperty("items")[0].GetProperty("rawEventId").GetGuid()==raw[0].Id,"S12 file hash and line receipts provide a real end-to-end link to immutable RawEventId evidence");
        var conflictId=Guid.NewGuid();var conflictInput=input with{OperationId=conflictId,FileBase64=Convert.ToBase64String(Encoding.UTF8.GetBytes("001\t2026-11-08 08:02:00\t1\t4\t1"))};await Create(conflictInput);var hash=await Query(db=>db.DeviceFileImports.Where(x=>x.Id==conflictId).Select(x=>x.FileHash).SingleAsync());
        await Use<AttendanceIngestion,AttendanceIngestion.Receipt>(s=>s.AppendAsync(actor,device,"usb-file:"+hash+":1","001",new(2026,11,8,8,2,0,TimeSpan.Zero),"UTC","in",Encoding.UTF8.GetBytes("different retained payload")));
        var collision=JsonSerializer.SerializeToElement(await Commit(conflictId,await Version(conflictId)));
        check(collision.GetProperty("conflicts").GetInt32()==1&&await Query(db=>db.DeviceFileImportLines.AnyAsync(x=>x.ImportId==conflictId&&x.ConflictId!=null&&x.Status=="ingestion-conflict")),"S12 existing conflicting external envelopes flow through the original ingestion conflict path instead of overwriting raw history");
        await Query(db=>db.Sites.Where(x=>x.Id==site).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.TimeZoneId,"Asia/Tehran")));
        var changedZoneVersion=await Version(id);
        check(await Reject<DomainConflictException>(()=>Commit(id,changedZoneVersion)),"S12 changed installation time zone cannot reinterpret retained file bytes or retarget a committed source");
        await Query(db=>db.RolePermissions.Where(x=>x.RoleId==readRole).ExecuteDeleteAsync());
        check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceFileImports,object>(s=>s.OriginalAsync(reader,id)))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceFileImports,object>(s=>s.RowsAsync(reader,id,0))),"S12 read revocation removes both exact original file bytes and parsed personal-data access");
        bool originalGuard=false,receiptGuard=false,downGuard=false;
        try{await Query(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceFileImports SET OriginalFile=0x00 WHERE Id={id}"));}catch(SqlException e)when(e.Number==51019){originalGuard=true;}
        try{await Query(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceFileImportLines SET Status=N'imported' WHERE ImportId={id} AND LineNumber=1"));}catch(SqlException e)when(e.Number==51019){receiptGuard=true;}
        try{await Query(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112050000_DeviceUserDownloads");return true;});}catch(SqlException e)when(e.Number==51019){downGuard=true;}
        check(originalGuard&&receiptGuard&&downGuard&&await Query(db=>db.DeviceFileImports.AnyAsync(x=>x.Id==id)),"S12 SQL guards retain original bytes ingested receipts and import history against mutation or destructive downgrade");
    }
}
