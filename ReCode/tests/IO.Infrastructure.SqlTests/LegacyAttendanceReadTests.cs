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
internal static class LegacyAttendanceReadTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var service=Guid.NewGuid();var reader=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var employee=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{db.Organizations.Add(new(){Id=org,Name="Legacy attendance lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Legacy attendance company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Legacy attendance UTC site",TimeZoneId="UTC"});db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="1",DisplayName="Legacy person"});
   foreach(var(u,n,rights)in new[]{(actor,"legacy-log-human",new[]{DeviceRegistry.Create,DeviceAttendanceDownloads.Read,DeviceAttendanceDownloads.Download,DeviceAttendanceDownloads.Commit,"attendance.events.receive"}),(service,"legacy-log-service",new[]{DeviceAttendanceDownloads.Execute}),(reader,"legacy-log-reader",new[]{DeviceAttendanceDownloads.Read})}){
    var role=Guid.NewGuid();db.Users.Add(new(){Id=u,UserName=n,IsActive=true});db.Roles.Add(new(){Id=role,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=role});foreach(var code in rights){var p=await db.Permissions.SingleAsync(x=>x.Code==code);db.RolePermissions.Add(new(){RoleId=role,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;});
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,new(site,"S09-LEGACY","Legacy log candidate","reference non-TFT","zk-standalone","127.0.0.1",4370,true)));
  await Q(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=device,CompanyId=company,SourcePersonId="1",EmployeeId=employee,ValidFromUtc=new(2026,1,1,0,0,0,TimeSpan.Zero)});await db.SaveChangesAsync();return true;});
  var dv=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());var from=new DateTimeOffset(2026,11,8,0,0,0,TimeSpan.Zero);var until=from.AddDays(1);var id=Guid.NewGuid();
  Task<Guid> Request(Guid operation,string format,Guid? user=null)=>Use<DeviceAttendanceDownloads,Guid>(s=>s.RequestAsync(user??actor,operation,device,dv,30,from,until,4,5,readFormat:format));
  Task<byte[]> Version(Guid operation)=>Q(db=>db.DeviceAttendanceDownloads.Where(x=>x.Id==operation).Select(x=>x.Version).SingleAsync());
  async Task<JsonElement> Commit(Guid operation)=>JsonSerializer.SerializeToElement(await Use<DeviceAttendanceDownloads,object>(async s=>await s.CommitPageAsync(actor,operation,await Version(operation),0,"INGEST-SDK-ROWS")));
  check(await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),"auto"))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),ZkAttendanceReadFormats.Legacy+" "))&&await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),ZkAttendanceReadFormats.Legacy,reader)),"S09 legacy exact format and independent extraction right are enforced before creating intent");
  await Request(id,ZkAttendanceReadFormats.Legacy);
  check(await Request(id,ZkAttendanceReadFormats.Legacy)==id&&await Reject<DomainConflictException>(()=>Request(id,ZkAttendanceReadFormats.Ssr)),"S09 legacy request replay is idempotent but cannot change its immutable read contract");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceAttendanceDownloads SET ReadFormat=N'ssr-v1' WHERE Id={id}")))&&await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceAttendanceDownloads SET ReadFormat=N'legacy-int32-minute-v1 ' WHERE Id={id}"))),"S09 SQL rejects format mutation and trailing-space bypass while preserving legacy intent");
  var claim=await Use<DeviceAttendanceDownloads,DeviceAttendanceDownloads.Claim?>(s=>s.ClaimAsync(service))??throw new InvalidOperationException();
  check(claim.Id==id&&claim.ReadFormat==ZkAttendanceReadFormats.Legacy&&await Use<DeviceAttendanceDownloads,DeviceCommunicationKey?>(s=>s.CredentialAsync(service,claim with{ReadFormat=ZkAttendanceReadFormats.Ssr},true))==null&&await Use<DeviceAttendanceDownloads,DeviceCommunicationKey?>(s=>s.CredentialAsync(service,claim,true))is not null,"S09 legacy claim binds format before secret dispatch and refuses SSR substitution under the same token");
  var row=new ZkAttendanceRaw("1",1,4,2026,11,8,8,0,0,0,7,8);
  check(await Reject<ArgumentException>(()=>Use<DeviceAttendanceDownloads,long>(s=>s.AppendAsync(service,id,claim.Token,0,[row with{Second=12}])))&&await Reject<ArgumentException>(()=>Use<DeviceAttendanceDownloads,long>(s=>s.AppendAsync(service,id,claim.Token,0,[row with{PersonIdentifier="01"}])))&&await Reject<ArgumentException>(()=>Use<DeviceAttendanceDownloads,long>(s=>s.AppendAsync(service,id,claim.Token,0,[row with{LegacyTerminalNumber=null}]))),"S09 legacy append refuses invented seconds padded integer identity and incomplete native metadata");
  ZkAttendanceRaw[] tuples=[row,row,row with{PersonIdentifier="-1"},row with{Month=99},row with{PersonIdentifier="2"}];
  await Use<DeviceAttendanceDownloads,long>(s=>s.AppendAsync(service,id,claim.Token,0,tuples));await Use<DeviceAttendanceDownloads,Guid>(s=>s.CompleteAsync(service,id,claim.Token,"succeeded",5));
  var preview=JsonSerializer.SerializeToElement(await Use<DeviceAttendanceDownloads,object>(s=>s.RowsAsync(reader,id,0)));var statuses=preview.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("status").GetString()).ToArray();
  check(preview.GetProperty("ReadFormat").GetString()==ZkAttendanceReadFormats.Legacy&&statuses.SequenceEqual(new[]{"ready","ready","invalid-legacy-identity","invalid-time","unknown-employee"})&&await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device))==0,"S09 legacy complete preview retains original negative dates unknown identities and duplicate tuples without silent ingestion");
  var result=await Commit(id);
  check(result.GetProperty("added").GetInt32()==2&&result.GetProperty("quarantined").GetInt32()==3&&await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device))==2&&await Q(db=>db.DeviceAttendanceRows.Where(x=>x.DownloadId==id&&x.PayloadJson==row.CanonicalJson()).OrderBy(x=>x.Sequence).Select(x=>x.OccurrenceOrdinal).ToArrayAsync())is[1,2],"S09 legacy confirmed ingestion uses canonical numeric mapping and preserves minute-tuple multiplicity while quarantining invalid rows");
  check(await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device&&x.ExternalEventId!.StartsWith("zk-legacy-int32-minute-v1:")))==2&&await Q(db=>db.DeviceAttendanceRows.AnyAsync(x=>x.DownloadId==id&&x.PayloadJson==row.CanonicalJson()&&x.RawEventId!=null)),"S09 legacy raw receipts retain both native machine outputs and use a distinct versioned external identity namespace");
  var repeat=Guid.NewGuid();await Request(repeat,ZkAttendanceReadFormats.Legacy);var second=await Use<DeviceAttendanceDownloads,DeviceAttendanceDownloads.Claim?>(s=>s.ClaimAsync(service))??throw new InvalidOperationException();await Use<DeviceAttendanceDownloads,long>(s=>s.AppendAsync(service,repeat,second.Token,0,tuples));await Use<DeviceAttendanceDownloads,Guid>(s=>s.CompleteAsync(service,repeat,second.Token,"succeeded",5));result=await Commit(repeat);
  check(result.GetProperty("added").GetInt32()==0&&result.GetProperty("already").GetInt32()==2&&await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device))==2,"S09 explicit legacy reread retains new receipts without duplicating the same canonical tuples and multiplicity");
  var ssr=Guid.NewGuid();await Use<DeviceAttendanceDownloads,Guid>(s=>s.RequestAsync(actor,ssr,device,dv,30,from,until,4,5));var defaultRow=await Q(db=>db.DeviceAttendanceDownloads.AsNoTracking().SingleAsync(x=>x.Id==ssr));
  check(defaultRow.ReadFormat==ZkAttendanceReadFormats.Ssr,"S09 omitted format preserves SSR upgrade compatibility rather than auto-selecting legacy from model text");await Use<DeviceAttendanceDownloads,Guid>(s=>s.CancelAsync(actor,ssr,defaultRow.Version));
  bool down=false;try{await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112200000_DeviceIdleSetting");return true;});}catch(SqlException e)when(e.Number==51029){down=true;}
  check(down&&await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==id&&x.ReadFormat==ZkAttendanceReadFormats.Legacy)),"S09 legacy downgrade refuses loss of retained format provenance rather than silently relabelling it SSR");
 }
}
