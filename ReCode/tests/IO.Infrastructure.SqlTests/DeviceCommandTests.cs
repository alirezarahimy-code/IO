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
internal static class DeviceCommandTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();
  var actor=Guid.NewGuid();var reader=Guid.NewGuid();var service=Guid.NewGuid();var role=Guid.NewGuid();var readRole=Guid.NewGuid();var execRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="S11 org"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="S11 company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="S11 site",TimeZoneId="UTC"});
   foreach(var(u,n)in new[]{(actor,"command-owner"),(reader,"command-reader"),(service,"command-service")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"command-full",new[]{DeviceCommands.Read,DeviceCommands.Retry,DeviceOperations.Read,DeviceOperations.SetClock,DeviceAttendanceDownloads.Download,DeviceRegistry.Create,DeviceRegistry.Update}),(readRole,reader,"command-read",new[]{DeviceCommands.Read}),(execRole,service,"command-exec",new[]{DeviceOperations.Execute,DeviceAttendanceDownloads.Execute})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;});
  var di=new DeviceRegistry.Input(site,"S11-A","Command SSR","unverified SSR","zk-standalone","127.0.0.1",4370,true);await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,di));var dv=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
  async Task<DeviceCommands.RetryInput> Input(Guid previous,string queue="operation")=>new(Guid.NewGuid(),queue,previous,queue=="operation"?await Q(db=>db.DeviceOperations.Where(x=>x.Id==previous).Select(x=>x.Version).SingleAsync()):await Q(db=>db.DeviceAttendanceDownloads.Where(x=>x.Id==previous).Select(x=>x.Version).SingleAsync()),dv,"RETRY-READ-ONLY-COMMAND");
  Task<Guid> Retry(DeviceCommands.RetryInput input,Guid? user=null)=>Use<DeviceCommands,Guid>(s=>s.RetryAsync(user??actor,input));
  Task<Guid> Request(Guid id,string kind="information.read")=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,id,device,dv,kind,30,kind=="clock.set"?new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero):null,kind=="clock.set"?"SET-CLOCK":null));
  async Task<DeviceOperations.Claim> Claim(Guid id){var c=await Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(service));if(c?.Id!=id)throw new InvalidOperationException("Expected command claim.");return c;}
  Task<Guid> Finish(DeviceOperations.Claim c,string state)=>Use<DeviceOperations,Guid>(s=>s.CompleteAsync(service,c.Id,c.Token,new(state)));
  async Task Permission(string code,bool grant){await Q(async db=>{var p=await db.Permissions.SingleAsync(x=>x.Code==code);if(grant)db.RolePermissions.Add(new(){RoleId=role,PermissionId=p.Id});else await db.RolePermissions.Where(x=>x.RoleId==role&&x.PermissionId==p.Id).ExecuteDeleteAsync();await db.SaveChangesAsync();return true;});}
  var id=Guid.NewGuid();await Request(id);var input=await Input(id);
  check(await Reject<UnauthorizedAccessException>(()=>Retry(input,reader))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceCommands,object>(s=>s.ListAsync(service,null,0))),"S11 command history and retry are independent from worker execution and basic read grants");
  check(await Reject<ArgumentException>(()=>Retry(input with{Confirmation="guess"}))&&await Reject<ArgumentException>(()=>Retry(input with{Queue="users-upload"}))&&await Reject<DomainConflictException>(()=>Retry(input)),"S11 arbitrary native commands wrong consent write queues and pending retries are rejected before enqueue");
  var c=await Claim(id);await Finish(c,"timed-out");input=await Input(id);await Permission(DeviceOperations.Read,false);
  check(await Reject<UnauthorizedAccessException>(()=>Retry(input)),"S11 generic retry right cannot bypass independent information read authority");await Permission(DeviceOperations.Read,true);
  var retried=await Retry(input);check(retried==input.Id&&await Retry(input)==retried&&await Q(db=>db.DeviceCommandRetries.CountAsync(x=>x.PreviousOperationId==id))==1&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==id&&x.State=="timed-out"))&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==retried&&x.State=="pending"&&x.RequestedByUserId==actor)),"S11 exact uncertain retry replay creates one new intent and immutable parent-child SQL lineage while preserving failed evidence");
  check(await Reject<DomainConflictException>(()=>Retry(input with{Id=Guid.NewGuid()}))&&await Reject<DomainConflictException>(()=>Retry(input with{ExpectedDeviceVersion=new byte[8]})),"S11 a terminal read gets only one direct child and changed payload replay conflicts");
  var history=JsonSerializer.SerializeToElement(await Use<DeviceCommands,object>(s=>s.ListAsync(reader,device,0)),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});check(history.GetProperty("items").GetArrayLength()==2&&history.GetProperty("retries")[0].GetProperty("id").GetGuid()==retried&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceAttendanceDownloads,object>(s=>s.RowsAsync(reader,Guid.NewGuid(),0))),"S11 unified SQL view exposes actual attempts and lineage without granting original personal attendance rows");
  c=await Claim(retried);await Finish(c,"failed");var secondInput=await Input(retried);var second=await Retry(secondInput);c=await Claim(second);await Finish(c,"failed");
  check(await Q(db=>db.DeviceCommandRetries.CountAsync(x=>x.PreviousOperationId==retried))==1,"S11 a failed child can be retried explicitly with a new lineage edge rather than rewriting prior outcomes");
  var clock=Guid.NewGuid();await Request(clock,"clock.set");c=await Claim(clock);await Finish(c,"unknown");check(await Reject<DomainConflictException>(async()=>await Retry(await Input(clock))),"S11 unknown clock write can never be retried through the generic read queue even with full command grants");
  var from=new DateTimeOffset(2026,11,8,0,0,0,TimeSpan.Zero);var otherDevice=Guid.NewGuid();await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,otherDevice,di with{Number="S11-B"}));var otherVersion=await Q(db=>db.Devices.Where(x=>x.Id==otherDevice).Select(x=>x.Version).SingleAsync());
  // UUIDs are scoped by their original typed queue; deliberately reuse an
  // operation UUID in the other device's attendance table.
  var logs=id;await Use<DeviceAttendanceDownloads,Guid>(s=>s.RequestAsync(actor,logs,otherDevice,otherVersion,60,from,from.AddDays(1),4,5));
  var otherHistory=JsonSerializer.SerializeToElement(await Use<DeviceCommands,object>(s=>s.ListAsync(reader,otherDevice,0)));check(otherHistory.GetProperty("items").GetArrayLength()==1&&otherHistory.GetProperty("retries").GetArrayLength()==0,"S11 matching UUIDs in different typed queues cannot acquire each other's retry lineage even under a filtered SQL history read");var logClaim=await Use<DeviceAttendanceDownloads,DeviceAttendanceDownloads.Claim?>(s=>s.ClaimAsync(service))??throw new InvalidOperationException();await Use<DeviceAttendanceDownloads,Guid>(s=>s.CompleteAsync(service,logs,logClaim.Token,"incomplete",0));var logInput=(await Input(logs,"attendance-download")) with{ExpectedDeviceVersion=otherVersion};var logRetry=await Retry(logInput);
  check(await Q(db=>db.DeviceCommandRetries.AnyAsync(x=>x.PreviousAttendanceDownloadId==logs&&x.NextAttendanceDownloadId==logRetry))&&await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==logRetry&&x.FromUtc==from&&x.UntilUtcExclusive==from.AddDays(1)&&x.InCode==4&&x.OutCode==5&&x.ReceivedCount==0)),"S11 incomplete attendance retry creates a fresh raw collection with the exact approved range codes and endpoint snapshot, not automatic ingestion");
  var rv=await Q(db=>db.DeviceAttendanceDownloads.Where(x=>x.Id==logRetry).Select(x=>x.Version).SingleAsync());await Use<DeviceAttendanceDownloads,Guid>(s=>s.CancelAsync(actor,logRetry,rv));
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceCommandRetries SET RequestedByUserId={reader} WHERE Id={retried}")))&&await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DeviceCommandRetries WHERE Id={retried}"))),"S11 SQL refuses retry lineage mutation or deletion");
  var observation=Guid.NewGuid();await Request(observation);c=await Claim(observation);
  await Use<DeviceOperations,Guid>(s=>s.CompleteAsync(service,observation,c.Token,new("succeeded",new("S11-SERIAL",null,null,null,"2026-11-08 10:00:00",null,null,null,null,null,null,null,null,null,true))));
  var clockVersion=await Q(db=>db.DeviceOperations.Where(x=>x.Id==clock).Select(x=>x.Version).SingleAsync());await Use<DeviceOperations,Guid>(s=>s.ResolveClockAsync(actor,clock,clockVersion,observation,"REVIEWED-CLOCK"));
  var stale=await Input(second);await Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,device,dv,di with{Name="Changed command target"}));check(await Reject<DomainConflictException>(()=>Retry(stale))&&await Q(db=>db.DeviceCommandRetries.CountAsync(x=>x.PreviousOperationId==second))==0,"S11 endpoint version changes cannot be silently adopted by retry and a failed enqueue leaves no orphan lineage");
  await Permission(DeviceCommands.Retry,false);check(await Reject<UnauthorizedAccessException>(()=>Retry(input)),"S11 existing successful retry replay is denied immediately after retry grant revocation");
  await Q(db=>db.RolePermissions.Where(x=>x.RoleId==readRole).ExecuteDeleteAsync());check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceCommands,object>(s=>s.ListAsync(reader,device,0))),"S11 current reader cannot keep command history after its server grant is revoked");
  dv=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());var browser=Guid.NewGuid();await Request(browser);c=await Claim(browser);await Finish(c,"failed");
  check(await Reject<SqlException>(()=>Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112090000_DevicePollSchedules");return true;})),"S11 retry history prevents destructive downgrade");
 }
}
