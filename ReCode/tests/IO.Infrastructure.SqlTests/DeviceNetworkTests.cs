using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
internal static class DeviceNetworkTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);var clock=new Clock();services.AddSingleton<TimeProvider>(clock);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var worker=Guid.NewGuid();var outsider=Guid.NewGuid();var role=Guid.NewGuid();var exec=Guid.NewGuid();var otherRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{
   await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Network lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Network company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Network site",TimeZoneId="UTC"});
   foreach(var(u,n)in new[]{(actor,"network-actor"),(worker,"network-worker"),(outsider,"network-outsider")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"network-human",new[]{DeviceOperations.SetIp,DeviceOperations.InspectNetwork,DeviceOperations.Read,DeviceRegistry.Create,DeviceRegistry.Update,DeviceRegistry.Credentials,DeviceCommands.Read,DeviceCommands.Retry}),(exec,worker,"network-exec",new[]{DeviceOperations.Execute}),(otherRole,outsider,"network-other",new[]{DeviceOperations.Read})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;
  });
  var plan=new ZkNetworkPlan("10.20.30.40","10.20.30.41","network-fixture");
  var input=new DeviceRegistry.Input(site,"S06-NETWORK","Network candidate","unverified","zk-standalone",plan.OldAddress,4370,true);
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,input));var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
  Task<byte[]> V(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  Task<string> State(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
  Task<Guid> Request(Guid id,ZkNetworkPlan? data=null,string kind="network.ip.set",string? confirmation="SET-DEVICE-IP",Guid? user=null)=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(user??actor,id,device,version,kind,5,null,confirmation,networkPlan:data??plan));
  Task<DeviceOperations.Claim?> Claim()=>Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker));
  Task<Guid> Finish(DeviceOperations.Claim claim,ZkOperationOutput result)=>Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,claim.Id,claim.Token,result));
  Task<int> Revoke(Guid r,string code)=>Q(db=>db.RolePermissions.Where(x=>x.RoleId==r&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==code)).ExecuteDeleteAsync());
  async Task Grant(Guid r,string code){await Q(async db=>{var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});await db.SaveChangesAsync();return true;});}
  check(!PermissionCatalog.Templates.Any(x=>x.Permissions.Any(p=>p.StartsWith("devices.network.",StringComparison.Ordinal)))&&WorkspaceCatalog.Pages.Single(x=>x.Route=="/workspace/device-information").AnyPermissions.Contains(DeviceOperations.SetIp),"S06 network rights are independently grantable and menu-mapped without default role escalation");
  check(await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),user:outsider))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),confirmation:null))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),data:plan with{NewAddress="10.20.30.041"})),"S06 network requires current human grant exact confirmation and canonical bounded IP plan");
  check(await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),data:plan with{OldAddress="10.20.30.42"}))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),kind:"information.read",confirmation:null)),"S06 wrong registered old address or network payload on another operation cannot retarget the device");
  var change=Guid.NewGuid();await Request(change);
  check(await Request(change)==change&&await Reject<DomainConflictException>(()=>Request(change,data:plan with{NewAddress="10.20.30.42"}))&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid())),"S06 exact IP change replay retains one immutable intent while changed payload and competing work conflict");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET NetworkPlanJson=REPLACE(NetworkPlanJson,N'10.20.30.41',N'10.20.30.42') WHERE Id={change}"))),"S06 SQL prevents changing an approved network plan even before dispatch");
  check(await Claim()==null&&await State(change)=="pending","S06 generic worker execution grant cannot claim an IP write");
  await Grant(exec,DeviceOperations.ExecuteNetworkWrite);var claim=await Claim()??throw new InvalidOperationException("Missing IP claim");
  check(claim.Id==change&&claim.NetworkPlan==plan&&(await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true)))?.Value==0&&await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim with{NetworkPlan=plan with{NewAddress="10.20.30.42"}},true))==null,"S06 owned network credentials bind the exact immutable old new and serial plan");
  check(await Reject<ArgumentException>(()=>Finish(claim,new("succeeded"))),"S06 SDK write ACK cannot be stored as confirmed network success");
  await Finish(claim,new("timed-out"));
  check(await State(change)=="unknown"&&await Request(change)==change&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid())),"S06 IP write timeout remains unknown and is never blindly repeated");
  check(await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.UpdateAsync(actor,device,version,input with{Host=plan.NewAddress})))&&await Reject<DomainConflictException>(()=>Use<DeviceRegistry,Guid>(s=>s.SetCredentialAsync(actor,device,version,"123"))),"S06 unknown transition preserves registered endpoint version and credentials until review");
  check(await Reject<DomainConflictException>(()=>Use<DeviceCommands,Guid>(async s=>await s.RetryAsync(actor,new(Guid.NewGuid(),"operation",change,await V(change),version,"RETRY-READ-ONLY-COMMAND")))),"S06 generic read retry cannot replay an IP write");
  clock.Advance(1);var inspectPlan=plan with{ChangeId=change,ObserveNewAddress=true};var inspect=Guid.NewGuid();
  check(await Reject<DomainConflictException>(()=>Request(inspect,inspectPlan with{Serial="foreign"},"network.inspect","INSPECT-DEVICE-IP"))&&await Reject<DomainConflictException>(()=>Request(inspect,inspectPlan with{ChangeId=Guid.NewGuid()},"network.inspect","INSPECT-DEVICE-IP")),"S06 recovery cannot inspect an invented parent or change the approved serial");
  await Request(inspect,inspectPlan,"network.inspect","INSPECT-DEVICE-IP");
  check(await Claim()==null&&await State(inspect)=="pending","S06 IP write service grant never implies read-only recovery execution");
  await Grant(exec,DeviceOperations.ExecuteNetworkRead);var observed=await Claim()??throw new InvalidOperationException("Missing inspection");
  check(await Reject<ArgumentException>(()=>Finish(observed,new("succeeded",Network:new("foreign",plan.NewAddress,plan.NewAddress))))&&await Reject<ArgumentException>(()=>Finish(observed,new("succeeded",Network:new(plan.Serial,plan.OldAddress,plan.OldAddress)))),"S06 recovery result cannot substitute a foreign serial or unrequested endpoint");
  await Finish(observed,new("succeeded",Network:new(plan.Serial,plan.NewAddress,plan.OldAddress)));clock.Advance(1);
  check(await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(async s=>await s.ResolveNetworkAsync(actor,change,await V(change),inspect,"REVIEWED-DEVICE-IP"))),"S06 reaching a target that reports a different device IP cannot falsely resolve network recovery");
  inspect=Guid.NewGuid();await Request(inspect,inspectPlan,"network.inspect","INSPECT-DEVICE-IP");observed=await Claim()??throw new InvalidOperationException("No fresh inspection");
  await Finish(observed,new("succeeded",Network:new(plan.Serial,plan.NewAddress,plan.NewAddress)));clock.Advance(1);
  check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,Guid>(async s=>await s.ResolveNetworkAsync(outsider,change,await V(change),inspect,"REVIEWED-DEVICE-IP")))&&await Reject<ArgumentException>(()=>Use<DeviceOperations,Guid>(async s=>await s.ResolveNetworkAsync(actor,change,await V(change),inspect,"REVIEWED-CLOCK"))),"S06 network review needs the independent current write grant and exact review confirmation");
  await Use<DeviceOperations,Guid>(async s=>await s.ResolveNetworkAsync(actor,change,await V(change),inspect,"REVIEWED-DEVICE-IP"));
  check(await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==change&&x.State=="unknown"&&x.ResolutionInfoId==inspect))&&await Q(db=>db.Devices.AnyAsync(x=>x.Id==device&&x.Host==plan.OldAddress)),"S06 explicit fresh network review retains unknown outcome and never silently rewrites the inventory endpoint");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET State=N'failed' WHERE Id={change}"))),"S06 SQL retains terminal network evidence and the reviewed unknown outcome");
  var revoked=Guid.NewGuid();await Request(revoked);await Revoke(role,DeviceOperations.SetIp);
  check(await Claim()==null&&await State(revoked)=="revoked"&&await Reject<UnauthorizedAccessException>(()=>Request(change)),"S06 human IP grant revocation blocks queued dispatch and even exact old request replay");
  await Grant(role,DeviceOperations.SetIp);var stale=Guid.NewGuid();await Request(stale);clock.Advance(121);
  check(await Claim()==null&&await State(stale)=="expired","S06 stale queued IP changes expire before dispatch");
  var cancel=Guid.NewGuid();await Request(cancel);await Use<DeviceOperations,Guid>(async s=>await s.CancelAsync(actor,cancel,await V(cancel)));
  check(await State(cancel)=="cancelled","S06 pending-only IP cancellation retains its immutable plan");
  var lost=Guid.NewGuid();await Request(lost);_ =await Claim()??throw new InvalidOperationException();clock.Advance(121);
  check(await Claim()==null&&await State(lost)=="unknown","S06 abandoned running IP write becomes unknown rather than replayed");
  clock.Advance(1);var oldInspect=Guid.NewGuid();await Request(oldInspect,plan with{ChangeId=lost,ObserveNewAddress=false},"network.inspect","INSPECT-DEVICE-IP");var oldClaim=await Claim()??throw new InvalidOperationException("No old-address inspection");
  await Finish(oldClaim,new("succeeded",Network:new(plan.Serial,plan.OldAddress,plan.OldAddress)));clock.Advance(1);await Use<DeviceOperations,Guid>(async s=>await s.ResolveNetworkAsync(actor,lost,await V(lost),oldInspect,"REVIEWED-DEVICE-IP"));
  check(await State(lost)=="unknown"&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==lost&&x.ResolutionInfoId==oldInspect)),"S06 explicit old-address observation can record unchanged reachable recovery without claiming a successful IP mutation");
  bool blocked=false;try{await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112180000_DeviceRestart");return true;});}catch(SqlException e)when(e.Number==51030){blocked=true;}
  check(blocked&&await State(change)=="unknown","S06 downgrade refuses to discard retained network plans or recovery history");
 }
 private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(int seconds)=>now=now.AddSeconds(seconds);}
}
