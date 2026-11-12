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
internal static class DeviceIdleTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);var clock=new Clock();services.AddSingleton<TimeProvider>(clock);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var worker=Guid.NewGuid();var reader=Guid.NewGuid();var role=Guid.NewGuid();var exec=Guid.NewGuid();var readRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{
   await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Idle lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Idle company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Idle site",TimeZoneId="UTC"});
   foreach(var(u,n)in new[]{(actor,"idle-actor"),(worker,"idle-worker"),(reader,"idle-reader")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"idle-human",new[]{DeviceOperations.SetIdle,DeviceOperations.ReadSettings,DeviceRegistry.Create,DeviceRegistry.Update,DeviceRegistry.Credentials,DeviceCommands.Read,DeviceCommands.Retry}),(exec,worker,"idle-exec",new[]{DeviceOperations.Execute,DeviceOperations.ExecuteSettings}),(readRole,reader,"idle-read",new[]{DeviceOperations.ReadSettings})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;
  });
  var deviceInput=new DeviceRegistry.Input(site,"S11-IDLE","Idle candidate","unverified","zk-standalone","10.20.30.50",4370,true);
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,deviceInput));var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
  Task<byte[]> V(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  Task<string> State(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
  Task<DeviceOperations.Claim?> Claim()=>Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker,includeSettings:true));
  Task<Guid> Finish(DeviceOperations.Claim c,ZkOperationOutput result)=>Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,c.Id,c.Token,result));
  Task<int> Revoke(Guid r,string code)=>Q(db=>db.RolePermissions.Where(x=>x.RoleId==r&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==code)).ExecuteDeleteAsync());
  async Task Grant(Guid r,string code){await Q(async db=>{var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});await db.SaveChangesAsync();return true;});}
  async Task<Guid> Snapshot(int? minutes=5,string? product="product-A",string? firmware="firmware-1"){
   var id=Guid.NewGuid();await Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,id,device,version,"settings.read",5,null,"READ-ADVANCED-SETTINGS"));var c=await Claim()??throw new InvalidOperationException("No settings claim");if(c.Id!=id)throw new InvalidOperationException("Foreign claim");
   await Finish(c,new("succeeded",Settings:new(ZkAdvancedSettings.Version,"idle-fixture",null,null,null,minutes,null,null,product,firmware)));return id;
  }
  ZkIdlePlan Plan(Guid snapshot)=>new(ZkIdlePlan.Version,snapshot,"idle-fixture","product-A","firmware-1",5,10,"lab-evidence-1");
  Task<Guid> Request(Guid id,ZkIdlePlan p,string? hash=null,string? confirmation=ZkIdlePlan.Confirmation,Guid? user=null)=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(user??actor,id,device,version,"settings.idle.set",5,null,confirmation,idlePlan:p,idlePreviewHash:hash??p.PreviewHash()));
  Task<object> Preview(Guid snapshot,int minutes=10,Guid? user=null)=>Use<DeviceOperations,object>(s=>s.PreviewIdleAsync(user??actor,device,version,snapshot,minutes,"lab-evidence-1"));
  Task<Guid> Review(Guid id,Guid snapshot,Guid? user=null,string confirmation="REVIEWED-IDLE-MINUTES")=>Use<DeviceOperations,Guid>(async s=>await s.ResolveIdleAsync(user??actor,id,await V(id),snapshot,confirmation));
  check(!PermissionCatalog.Templates.Any(x=>x.Permissions.Contains(DeviceOperations.SetIdle)||x.Permissions.Contains(DeviceOperations.ExecuteIdle))&&WorkspaceCatalog.Pages.Single(x=>x.Route=="/workspace/device-settings").AnyPermissions.Contains(DeviceOperations.SetIdle),"S11 idle human and execution rights are independent default-ungranted and menu mapped");
  var snapshot=await Snapshot();var plan=Plan(snapshot);var preview=JsonSerializer.Serialize(await Preview(snapshot));
  check(preview.Contains(plan.PreviewHash(),StringComparison.Ordinal)&&preview.Contains("standby-or-power-off",StringComparison.Ordinal),"S11 server preview derives exact old new serial model firmware and hash from SQL evidence");
  check(await Reject<UnauthorizedAccessException>(()=>Preview(snapshot,user:reader))&&await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),plan,user:reader)),"S11 settings read grant cannot preview or write idle duration");
  check(await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),plan,confirmation:null))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),plan,hash:"changed"))&&await Reject<DomainConflictException>(()=>Preview(snapshot,5))&&await Reject<ArgumentException>(()=>Preview(snapshot,0)),"S11 exact preview hash effect confirmation bounded different value are required");
  check(await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),plan with{Firmware="foreign"}))&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),plan with{ExpectedMinutes=6})),"S11 client cannot substitute snapshot firmware or old duration in a rehashed plan");
  var missing=await Snapshot(product:null);check(await Reject<DomainConflictException>(()=>Preview(missing)),"S11 missing model descriptor is unavailable not a guessed compatible target");
  var zero=await Snapshot(0);check(await Reject<DomainConflictException>(()=>Preview(zero)),"S11 zero sleep value is preserved by reads but never inferred as writable disabled encoding");
  var id=Guid.NewGuid();await Request(id,plan);
  check(await Request(id,plan)==id&&await Reject<DomainConflictException>(()=>Request(id,plan with{NewMinutes=11}))&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),plan)),"S11 exact idle intent replays once while changed retry and competing work conflict");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET IdlePlanJson=REPLACE(IdlePlanJson,N'firmware-1',N'firmware-2') WHERE Id={id}"))),"S11 SQL immutable idle request cannot be rewritten before dispatch");
  check(await Claim()==null&&await State(id)=="pending","S11 generic worker and settings-read grants cannot claim idle write");
  await Grant(exec,DeviceOperations.ExecuteIdle);var claim=await Claim()??throw new InvalidOperationException("No idle claim");
  check(claim.Id==id&&claim.IdlePlan==plan&&(await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true)))?.Value==0&&await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim with{IdlePlan=plan with{NewMinutes=11}},true))==null,"S11 private credential access binds the owned immutable idle plan");
  await Revoke(exec,DeviceOperations.ExecuteIdle);
  check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true)))&&await Reject<UnauthorizedAccessException>(()=>Finish(claim,new("unknown"))),"S11 idle service grant revocation immediately denies credentials and completion");
  await Grant(exec,DeviceOperations.ExecuteIdle);
  check(await Reject<ArgumentException>(()=>Finish(claim,new("succeeded")))&&await Reject<ArgumentException>(()=>Finish(claim,new("succeeded",Idle:new(plan.Serial,plan.Product,"foreign",10))))&&await Reject<ArgumentException>(()=>Finish(claim,new("succeeded",Idle:new(plan.Serial,plan.Product,plan.Firmware,5)))),"S11 success requires exact typed model and new minute read-back not SDK ACK");
  await Finish(claim,new("timed-out"));
  check(await State(id)=="unknown"&&await Request(id,plan)==id&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),plan)),"S11 deadline remains unknown exact API replay does not dispatch a second write");
  check(await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,device,version,deviceInput with{Name="changed"})))&&await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.SetCredentialAsync(actor,device,version,"123"))),"S11 unresolved idle write fences registry and credentials without blocking recovery reads");
  check(await Reject<DomainConflictException>(()=>Use<DeviceCommands,Guid>(async s=>await s.RetryAsync(actor,new(Guid.NewGuid(),"operation",id,await V(id),version,"RETRY-READ-ONLY-COMMAND"))))&&await Reject<DomainConflictException>(()=>Review(id,snapshot)),"S11 generic retry and stale pre-write evidence cannot resolve or replay unknown idle write");
  clock.Advance(1);var wrong=await Snapshot(7);
  check(await Reject<DomainConflictException>(()=>Review(id,wrong)),"S11 later unrelated idle value cannot be accepted as old or new observation");
  var modelMismatch=await Snapshot(10,firmware:"different");check(await Reject<DomainConflictException>(()=>Review(id,modelMismatch)),"S11 recovery requires unchanged exact product and firmware not serial alone");
  var fresh=await Snapshot(10);
  check(await Reject<UnauthorizedAccessException>(()=>Review(id,fresh,user:reader))&&await Reject<ArgumentException>(()=>Review(id,fresh,confirmation:"REVIEWED-CLOCK")),"S11 recovery review needs independent current human grant and exact consent");
  await Review(id,fresh);
  check(await State(id)=="unknown"&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==id&&x.ResolutionInfoId==fresh&&x.ResolvedByUserId==actor)),"S11 explicit fresh new-value observation records reviewer and retains original unknown outcome");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET State=N'failed' WHERE Id={id}"))),"S11 SQL preserves terminal idle and review evidence");
  var revoked=Guid.NewGuid();await Request(revoked,plan);await Revoke(role,DeviceOperations.SetIdle);
  check(await Claim()==null&&await State(revoked)=="revoked"&&await Reject<UnauthorizedAccessException>(()=>Request(id,plan)),"S11 human idle revoke cancels pending dispatch and denies even exact request replay");
  await Grant(role,DeviceOperations.SetIdle);var cancel=Guid.NewGuid();await Request(cancel,plan);await Use<DeviceOperations,Guid>(async s=>await s.CancelAsync(actor,cancel,await V(cancel)));
  check(await State(cancel)=="cancelled","S11 pending idle cancellation retains immutable SQL plan without dispatch");
  var stale=Guid.NewGuid();await Request(stale,plan);clock.Advance(121);
  check(await Claim()==null&&await State(stale)=="expired","S11 queued idle writes expire before dispatch rather than use old preview indefinitely");
  snapshot=await Snapshot();plan=Plan(snapshot);var success=Guid.NewGuid();await Request(success,plan);claim=await Claim()??throw new InvalidOperationException("No success claim");await Finish(claim,new("succeeded",Idle:new(plan.Serial,plan.Product,plan.Firmware,10)));
  check(await State(success)=="succeeded"&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==success&&x.ResultJson!=null)),"S11 exact typed idle readback persists as successful evidence in SQL");
  var lost=Guid.NewGuid();await Request(lost,plan);_=await Claim()??throw new InvalidOperationException("No lost claim");clock.Advance(121);
  check(await Claim()==null&&await State(lost)=="unknown","S11 abandoned running idle write becomes unknown and never requeues itself");
  clock.Advance(1);fresh=await Snapshot();await Review(lost,fresh);
  check(await State(lost)=="unknown"&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==lost&&x.ResolutionInfoId==fresh)),"S11 fresh old-value observation can record unchanged recovery without fabricating success");
  clock.Advance(301);check(await Reject<DomainConflictException>(()=>Preview(fresh)),"S11 stale settings snapshots cannot create new idle previews");
  bool blocked=false;try{await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112190000_DeviceNetworkIp");return true;});}catch(SqlException e)when(e.Number==51031){blocked=true;}
  check(blocked&&await State(id)=="unknown","S11 downgrade refuses to erase retained idle plans and recovery history");
 }
 private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(int seconds)=>now=now.AddSeconds(seconds);}
}
