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
internal static class DeviceProfileSettingTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var worker=Guid.NewGuid();var reader=Guid.NewGuid();var role=Guid.NewGuid();var exec=Guid.NewGuid();var readRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{
   await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Profile lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Profile company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Profile site",TimeZoneId="UTC"});
   foreach(var(u,n)in new[]{(actor,"profile-actor"),(worker,"profile-worker"),(reader,"profile-reader")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"profile-human",new[]{DeviceOperations.ReadSettings,DeviceRegistry.Create,DeviceCommands.Read,DeviceCommands.Retry}.Concat(ZkProfileParameters.All.Select(ZkProfileParameters.WritePermission)).ToArray()),(exec,worker,"profile-exec",new[]{DeviceOperations.Execute,DeviceOperations.ExecuteSettings}),(readRole,reader,"profile-read",new[]{DeviceOperations.ReadSettings})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;
  });
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,new(site,"S11-PROFILE","Profile fixture","synthetic-not-model-acceptance","zk-standalone","10.20.30.40",4370,true)));
  var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
  Task<byte[]> V(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  Task<DeviceOperations.Claim?> Claim()=>Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker,includeSettings:true));
  Task<Guid> Finish(DeviceOperations.Claim c,ZkOperationOutput r)=>Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,c.Id,c.Token,r));
  async Task Grant(Guid r,string code,bool enabled){await Q(async db=>{var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}if(enabled)db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});else await db.RolePermissions.Where(x=>x.RoleId==r&&x.PermissionId==p.Id).ExecuteDeleteAsync();await db.SaveChangesAsync();return true;});}
  var states=ZkProfileParameters.All.ToDictionary(x=>x,ProfileSettingTests.Old);var ids=new List<Guid>();
  async Task<Guid> Snapshot(){var id=Guid.NewGuid();await Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,id,device,version,"settings.read",10,null,"READ-ADVANCED-SETTINGS"));var c=await Claim()??throw new InvalidOperationException("Missing profile source claim");await Finish(c,new("succeeded",Settings:new(ZkAdvancedSettings.Version,"profile-fixture",1,2,3,4,5,6,"profile-product","profile-fw",Profiles:states.Values.Select(v=>new ZkProfileReading(v.Parameter,"synthetic-read",true,v)).ToArray())));return id;}
  Task<Guid> Request(Guid id,ZkProfilePlan p,Guid? user=null,string? hash=null)=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(user??actor,id,device,version,"settings.profile.set",10,null,ZkProfilePlan.Confirmation,profilePlan:p,profilePreviewHash:hash??p.PreviewHash()));
  foreach(var parameter in ZkProfileParameters.All)
  {
   var snapshot=await Snapshot();var p=ProfileSettingTests.Plan(parameter) with{SnapshotId=snapshot};
   Task<object> Preview(Guid user)=>Use<DeviceOperations,object>(s=>s.PreviewProfileAsync(user,device,version,snapshot,p.Desired,p.Effect,p.ApprovalReference,p.Recovery));
   check(JsonSerializer.Serialize(await Preview(actor)).Contains(p.PreviewHash(),StringComparison.Ordinal)&&await Reject<UnauthorizedAccessException>(()=>Preview(reader)),"profile SQL "+parameter+" snapshot-derived complete preview and independent human write authority");
   check(await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),p,hash:"wrong"))&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),p with{Firmware="other"})),"profile SQL "+parameter+" altered hash or model cannot substitute old source");
   var id=Guid.NewGuid();ids.Add(id);await Request(id,p);
   check(await Request(id,p)==id&&await Reject<DomainConflictException>(()=>Request(id,p with{Effect="changed"})),"profile SQL "+parameter+" exact intent replay is stable while changed effect is rejected");
   var none=await Claim();await Grant(exec,ZkProfileParameters.ExecutePermission(parameter),true);var c=await Claim()??throw new InvalidOperationException("Missing profile write claim");
   check(none is null&&c.ProfilePlan==p&&(await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,c,true)))?.Value==0&&await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,c with{ProfileObserveRecovery=true},true)) is null,"profile SQL "+parameter+" generic execution is insufficient and credential binds exact profile target and inspection mode");
   await Grant(role,DeviceOperations.ReadSettings,false);var noHuman=await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,c,true));await Grant(role,DeviceOperations.ReadSettings,true);
   await Grant(exec,ZkProfileParameters.ExecutePermission(parameter),false);var noService=await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,c,true)));await Grant(exec,ZkProfileParameters.ExecutePermission(parameter),true);
   check(noHuman is null&&noService&&await Reject<ArgumentException>(()=>Finish(c,new("succeeded"))),"profile SQL "+parameter+" live human read and independent service revoke deny dispatch and bare ACK cannot complete");
   var wrong=JsonSerializer.Serialize(new ZkProfileObservation(p.Serial,p.Product,p.Firmware,p.Expected,p.Original,p.ApprovalReference));
   check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET ProfilePlanJson=REPLACE(ProfilePlanJson,N'profile-fw',N'other-fw') WHERE Id={id}")))&&await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET State=N'succeeded',FinishedAtUtc=SYSDATETIMEOFFSET(),ResultJson={wrong} WHERE Id={id}"))),"profile SQL "+parameter+" database independently guards immutable plan and complete state not network ACK");
   await Finish(c,new("unknown"));states[parameter]=p.Desired;
   var inspection=Guid.NewGuid();await Use<DeviceOperations,Guid>(s=>s.RequestAsync(reader,inspection,device,version,"settings.profile.inspect",10,null,"INSPECT-EXACT-PROFILE",profilePlan:p,profilePreviewHash:p.PreviewHash(),profileInspectionOfId:id,profileObserveRecovery:true));var readClaim=await Claim()??throw new InvalidOperationException("Missing profile inspection claim");
   var observed=new ZkProfileObservation(p.Serial,p.Product,p.Firmware,p.Desired,p.Recovery,p.ApprovalReference);
   var denied=await Reject<ArgumentException>(()=>Finish(readClaim,new("succeeded",Profile:observed with{ApprovalReference="other"})));await Finish(readClaim,new("succeeded",Profile:observed));
   check(denied&&readClaim.ProfileInspectionOfId==id&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==inspection&&x.State=="succeeded")),"profile SQL "+parameter+" independent read inspection binds exact unresolved parent endpoint and full observation");
   await Use<DeviceOperations,Guid>(async s=>await s.ResolveProfileAsync(actor,id,await V(id),inspection,ZkProfilePlan.ReviewConfirmation));
   check(await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==id&&x.State=="unknown"&&x.ResolutionInfoId==inspection&&x.ResolvedAtUtc!=null)),"profile SQL "+parameter+" explicit review retains unknown evidence without mutation replay or metadata retarget");
   await Grant(exec,ZkProfileParameters.ExecutePermission(parameter),false);
  }
  check(!PermissionCatalog.Templates.Any(t=>t.Permissions.Any(x=>x.StartsWith("devices.profile.",StringComparison.Ordinal))),"profile SQL all seven independent human and service grants remain default ungranted");
  check(ZkProfileParameters.All.SelectMany(p=>new[]{ZkProfileParameters.WritePermission(p),ZkProfileParameters.ExecutePermission(p)}).Distinct().Count()==14,"profile SQL fourteen distinct exact permissions cannot collapse settings or service authority");
  var history=JsonSerializer.SerializeToElement(await Use<DeviceOperations,object>(s=>s.ListSettingsAsync(reader,device,0)),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});
  check(history.GetProperty("items").EnumerateArray().Count(x=>x.GetProperty("kind").GetString()=="settings.profile.inspect")==7&&history.GetProperty("items").EnumerateArray().Count(x=>x.GetProperty("kind").GetString()=="settings.profile.set")==7,"profile SQL retained read-authorized history includes all seven immutable writes and separate recovery inspections");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DeviceOperations WHERE Id={ids[0]}")))&&await Reject<SqlException>(()=>Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261113150000_DeviceFunctionKeyWrites");return true;})),"profile SQL retained full setting evidence blocks deletion and destructive downgrade");
  check(await Reject<DomainConflictException>(()=>Use<DeviceCommands,Guid>(async s=>await s.RetryAsync(actor,new(Guid.NewGuid(),"operation",ids[0],await V(ids[0]),version,"RETRY-READ-ONLY-COMMAND")))),"profile SQL generic command retry cannot replay an uncertain reviewed profile write");
 }
}
