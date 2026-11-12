using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

internal static class DeviceRestartTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);var clock=new Clock();services.AddSingleton<TimeProvider>(clock);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var worker=Guid.NewGuid();var reader=Guid.NewGuid();var role=Guid.NewGuid();var exec=Guid.NewGuid();var readonlyRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{
   await db.GetService<IMigrator>().MigrateAsync();
   db.Organizations.Add(new(){Id=org,Name="Restart lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Restart company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Restart site",TimeZoneId="UTC"});
   foreach(var(u,n)in new[]{(actor,"restart-actor"),(worker,"restart-worker"),(reader,"restart-reader")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"restart-human",new[]{DeviceOperations.Restart,DeviceOperations.Read,DeviceOperations.SetClock,DeviceRegistry.Create,DeviceRegistry.Update,DeviceRegistry.Credentials,DeviceCommands.Read,DeviceCommands.Retry}),(exec,worker,"restart-exec",new[]{DeviceOperations.Execute}),(readonlyRole,reader,"restart-readonly",new[]{DeviceOperations.Read})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;
  });
  var deviceInput=new DeviceRegistry.Input(site,"S11-RESTART","Restart candidate","unverified","zk-standalone","127.0.0.1",4370,true);
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,deviceInput));
  var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
  Task<byte[]> V(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  Task<string> State(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
  Task<Guid> Request(Guid id,Guid? user=null,string? confirmation="RESTART-DEVICE",string kind="device.restart")=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(user??actor,id,device,version,kind,5,null,confirmation));
  Task<DeviceOperations.Claim?> Claim()=>Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker));
  Task<Guid> Finish(DeviceOperations.Claim claim,string state)=>Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,claim.Id,claim.Token,new(state)));
  async Task Grant(Guid r,string code){await Q(async db=>{var permission=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(permission is null){permission=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(permission);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=permission.Id});await db.SaveChangesAsync();return true;});}
  Task<int> Revoke(Guid r,string code)=>Q(db=>db.RolePermissions.Where(x=>x.RoleId==r&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==code)).ExecuteDeleteAsync());
  check(!PermissionCatalog.Templates.Any(t=>t.Permissions.Contains(DeviceOperations.Restart)||t.Permissions.Contains(DeviceOperations.ExecuteRestart))&&WorkspaceCatalog.Pages.Single(x=>x.Route=="/workspace/device-information").AnyPermissions.Contains(DeviceOperations.Restart),"S11 restart is explicitly grantable and menu-visible but never silently inherited by default roles");
  check(await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),reader))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),confirmation:null))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),confirmation:"RESET-DEVICE")),"S11 independent human grant and exact restart confirmation required without reset alias");
  var first=Guid.NewGuid();await Request(first);
  check(await Request(first)==first&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid())),"S11 exact restart registration replay retains one immutable intent and device exclusion");
  check(await Claim()==null&&await State(first)=="pending","S11 general service operation grant alone cannot claim a restart");
  await Grant(exec,DeviceOperations.ExecuteRestart);var claim=await Claim()??throw new InvalidOperationException("No restart claim");
  check(claim.Id==first&&claim.Kind=="device.restart"&&claim.ClockLocal==null&&(await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true)))?.Value==0,"S11 independently granted service receives typed restart with owned private credentials");
  await Revoke(exec,DeviceOperations.ExecuteRestart);
  check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true)))&&await Reject<UnauthorizedAccessException>(()=>Finish(claim,"succeeded")),"S11 revoking restart execution denies credential retrieval and result submission");
  await Grant(exec,DeviceOperations.ExecuteRestart);await Finish(claim,"timed-out");
  check(await State(first)=="unknown"&&await Request(first)==first&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid())),"S11 dispatched timeout remains unknown and exact registration replay never issues a second restart");
  check(await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,device,version,deviceInput with{Name="Retarget"})))&&await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.SetCredentialAsync(actor,device,version,"123"))),"S11 unresolved restart preserves device identity and credentials for later same-device review");
  check(await Reject<DomainConflictException>(()=>Use<DeviceCommands,Guid>(async s=>await s.RetryAsync(actor,new(Guid.NewGuid(),"operation",first,await V(first),version,"RETRY-READ-ONLY-COMMAND")))),"S11 generic command retry never replays restart even with retry permission");
  var infoId=Guid.NewGuid();clock.Advance(1);await Request(infoId,confirmation:null,kind:"information.read");var infoClaim=await Claim()??throw new InvalidOperationException("No recovery read");
  var info=new ZkDeviceInformation("restart-fixture",null,null,null,"2026-11-08 10:00:00",null,null,null,null,null,null,null,null,null,false);
  await Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,infoClaim.Id,infoClaim.Token,new("succeeded",info)));clock.Advance(1);
  check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,Guid>(async s=>await s.ResolveRestartAsync(reader,first,await V(first),infoId,"REVIEWED-RESTART-REACHABILITY")))&&await Reject<ArgumentException>(()=>Use<DeviceOperations,Guid>(async s=>await s.ResolveRestartAsync(actor,first,await V(first),infoId,"REVIEWED-CLOCK")))&&await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(async s=>await s.ResolveClockAsync(actor,first,await V(first),infoId,"REVIEWED-CLOCK"))),"S11 recovery requires current restart grant and exact typed reachability consent rather than clock review");
  await Use<DeviceOperations,Guid>(async s=>await s.ResolveRestartAsync(actor,first,await V(first),infoId,"REVIEWED-RESTART-REACHABILITY"));
  check(await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==first&&x.State=="unknown"&&x.ResolutionInfoId==infoId&&x.ResolvedByUserId==actor)),"S11 fresh same-version information review records reachability evidence without claiming reboot completion or rewriting unknown");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET State=N'succeeded' WHERE Id={first}"))),"S11 SQL terminal-history trigger prevents rewriting reviewed unknown restart as success");
  var cancelled=Guid.NewGuid();await Request(cancelled);await Use<DeviceOperations,Guid>(async s=>await s.CancelAsync(actor,cancelled,await V(cancelled)));
  check(await State(cancelled)=="cancelled","S11 restart cancellation is guaranteed only while pending");
  var expired=Guid.NewGuid();await Request(expired);clock.Advance(121);
  check(await Claim()==null&&await State(expired)=="expired","S11 stale queued restart expires instead of unexpectedly rebooting a device later");
  var revoked=Guid.NewGuid();await Request(revoked);await Revoke(role,DeviceOperations.Restart);
  check(await Claim()==null&&await State(revoked)=="revoked"&&await Reject<UnauthorizedAccessException>(()=>Request(first)),"S11 current human revocation blocks dispatch and old exact registration replay");
  await Grant(role,DeviceOperations.Restart);var lost=Guid.NewGuid();await Request(lost);_ = await Claim()??throw new InvalidOperationException();clock.Advance(121);
  check(await Claim()==null&&await State(lost)=="unknown","S11 abandoned running restart becomes unknown and is never returned for replay");
  bool blocked=false;try{await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112150000_DeviceLegacyUserReads");return true;});}catch(SqlException e)when(e.Number==51029){blocked=true;}
  check(blocked&&await State(first)=="unknown","S11 downgrade refuses to discard retained restart and recovery contracts");
  await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync();return true;});
 }
 private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(int seconds)=>now=now.AddSeconds(seconds);}
}
