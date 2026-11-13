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
internal static class DeviceRawSettingTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var worker=Guid.NewGuid();var reader=Guid.NewGuid();var role=Guid.NewGuid();var exec=Guid.NewGuid();var readRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{
   await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Raw lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Raw company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Raw site",TimeZoneId="UTC"});
   foreach(var(u,n)in new[]{(actor,"raw-actor"),(worker,"raw-worker"),(reader,"raw-reader")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"raw-human",new[]{DeviceOperations.SetRaw,DeviceOperations.ReadSettings,DeviceRegistry.Create,DeviceRegistry.Update,DeviceRegistry.Credentials,DeviceCommands.Read,DeviceCommands.Retry}),(exec,worker,"raw-exec",new[]{DeviceOperations.Execute,DeviceOperations.ExecuteSettings}),(readRole,reader,"raw-read",new[]{DeviceOperations.ReadSettings})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;
  });
  var deviceInput=new DeviceRegistry.Input(site,"S11-RAW","Idle candidate","unverified","zk-standalone","10.20.30.50",4370,true);
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,deviceInput));var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
  Task<byte[]> V(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  Task<string> State(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
  Task<DeviceOperations.Claim?> Claim()=>Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker,includeSettings:true));
  Task<Guid> Finish(DeviceOperations.Claim c,ZkOperationOutput result)=>Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,c.Id,c.Token,result));
  Task<int> Revoke(Guid r,string code)=>Q(db=>db.RolePermissions.Where(x=>x.RoleId==r&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==code)).ExecuteDeleteAsync());
  async Task Grant(Guid r,string code){await Q(async db=>{var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});await db.SaveChangesAsync();return true;});}
  async Task<Guid> Snapshot(int? minutes=5,string? product="product-A",string? firmware="firmware-1"){
   var id=Guid.NewGuid();await Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,id,device,version,"settings.read",5,null,"READ-ADVANCED-SETTINGS"));var c=await Claim()??throw new InvalidOperationException("No settings claim");if(c.Id!=id)throw new InvalidOperationException("Foreign claim");
   await Finish(c,new("succeeded",Settings:new(ZkAdvancedSettings.Version,"raw-fixture",5,23,-1,minutes,30,62,product,firmware)));return id;
  }
  ZkRawSettingPlan Plan(Guid snapshot)=>new(ZkRawSettingPlan.Version,snapshot,"raw-fixture","product-A","firmware-1","repeat-time",-1,0,"documented-unit","documented-effect","lab-contract");
  Task<Guid> Request(Guid id,ZkRawSettingPlan p,string? hash=null,Guid? user=null)=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(user??actor,id,device,version,"settings.raw.set",5,null,ZkRawSettingPlan.Confirmation,rawSettingPlan:p,rawPreviewHash:hash??p.PreviewHash()));
  Task<object> Preview(Guid snapshot,Guid? user=null)=>Use<DeviceOperations,object>(s=>s.PreviewRawAsync(user??actor,device,version,snapshot,"repeat-time",0,"documented-unit","documented-effect","lab-contract"));
  var snapshot=await Snapshot();var plan=Plan(snapshot);
  check(!PermissionCatalog.Templates.Any(t=>t.Permissions.Contains(DeviceOperations.SetRaw)||t.Permissions.Contains(DeviceOperations.ExecuteRaw)),"S11 raw settings human and service grants are independent and default ungranted");
  check(JsonSerializer.Serialize(await Preview(snapshot)).Contains(plan.PreviewHash(),StringComparison.Ordinal),"S11 raw preview derives signed old value exact model descriptors and bound hash from SQL snapshot");
  check(await Reject<UnauthorizedAccessException>(()=>Preview(snapshot,reader))&&await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),plan,user:reader)),"S11 settings-read grant alone cannot preview or request raw setting writes");
  check(await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),plan,hash:"other"))&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),plan with{ExpectedValue=1})),"S11 raw request rejects altered hash and substituted old snapshot value");
  var id=Guid.NewGuid();await Request(id,plan);
  check(await Request(id,plan)==id&&await Reject<DomainConflictException>(()=>Request(id,plan with{NewValue=1})),"S11 immutable raw intent accepts exact replay only and never a changed target");
  check(await Claim()==null&&await State(id)=="pending","S11 generic execution and settings-read execution cannot claim raw writes");
  await Grant(exec,DeviceOperations.ExecuteRaw);var claim=await Claim()??throw new InvalidOperationException("No raw claim");
  check(claim.Id==id&&claim.RawSettingPlan==plan&&(await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true)))?.Value==0&&await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim with{RawSettingPlan=plan with{NewValue=1}},true))==null,"S11 raw worker claim and private credential bind the exact immutable raw plan");
  await Revoke(exec,DeviceOperations.ExecuteRaw);
  check(await Reject<UnauthorizedAccessException>(()=>Finish(claim,new("unknown")))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true))),"S11 raw worker grant revocation immediately denies completion and credential access");await Grant(exec,DeviceOperations.ExecuteRaw);
  check(await Reject<ArgumentException>(()=>Finish(claim,new("succeeded")))&&await Reject<ArgumentException>(()=>Finish(claim,new("succeeded",RawSetting:new(plan.Serial,plan.Product,plan.Firmware,plan.Parameter,1,plan.Unit,plan.Effect,plan.ApprovalReference)))),"S11 raw completion refuses bare ACK and wrong post-write value");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET RawSettingPlanJson=REPLACE(RawSettingPlanJson,N'firmware-1',N'firmware-2') WHERE Id={id}"))),"S11 SQL rejects raw plan mutation before completion independently of API checks");
  await Finish(claim,new("unknown"));
  check(await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),plan)),"S11 unreviewed raw unknown blocks another device mutation instead of blind retry");
  var observed=await Snapshot();await Use<DeviceOperations,Guid>(async s=>await s.ResolveRawAsync(actor,id,await V(id),observed,"REVIEWED-APPROVED-RAW-SETTING"));
  check(await State(id)=="unknown"&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==id&&x.ResolutionInfoId==observed&&x.ResolvedAtUtc!=null)),"S11 later exact same-model old-value snapshot permits explicit review without converting unknown to success");
  var ok=Guid.NewGuid();await Request(ok,Plan(observed));var next=await Claim()??throw new InvalidOperationException("No next raw claim");
  await Finish(next,new("succeeded",RawSetting:new(plan.Serial,plan.Product,plan.Firmware,plan.Parameter,0,plan.Unit,plan.Effect,plan.ApprovalReference)));
  check(await State(ok)=="succeeded"&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==ok&&x.ResultJson!=null)),"S11 raw successful typed observation retained only after exact readback");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DeviceOperations WHERE Id={ok}"))),"S11 SQL retains raw history and refuses deletion");
  check(await Reject<SqlException>(()=>Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261113130000_DeviceSettingsPayloadBudget");return true;})),"S11 downgrade refuses deletion of retained raw-setting request and observation history");
 }
}
