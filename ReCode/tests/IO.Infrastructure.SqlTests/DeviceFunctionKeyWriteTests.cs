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
internal static class DeviceFunctionKeyWriteTests
{
 private static readonly ZkFunctionKeyValue OldKey=new(0,true,0,4,"old label",2,"raw;old-schedule"),NewKey=OldKey with{StateCode=5,StateName="new label"};
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var worker=Guid.NewGuid();var reader=Guid.NewGuid();var role=Guid.NewGuid();var exec=Guid.NewGuid();var readRole=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{
   await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Key lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Key company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Key site",TimeZoneId="UTC"});
   foreach(var(u,n)in new[]{(actor,"key-actor"),(worker,"key-worker"),(reader,"key-reader")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"key-human",new[]{DeviceOperations.SetFunctionKey,DeviceOperations.ReadFunctionKeys,DeviceOperations.ReadSettings,DeviceRegistry.Create,DeviceRegistry.Update,DeviceRegistry.Credentials,DeviceCommands.Read,DeviceCommands.Retry}),(exec,worker,"key-exec",new[]{DeviceOperations.Execute,DeviceOperations.ExecuteFunctionKeys,DeviceOperations.ExecuteSettings}),(readRole,reader,"key-read",new[]{DeviceOperations.ReadSettings})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;
  });
  var deviceInput=new DeviceRegistry.Input(site,"S11-KEY","Key candidate","unverified","zk-standalone","10.20.30.50",4370,true);
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,deviceInput));var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
  Task<byte[]> V(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  Task<string> State(Guid id)=>Q(db=>db.DeviceOperations.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());
  Task<DeviceOperations.Claim?> Claim()=>Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker,includeSettings:true));
  Task<Guid> Finish(DeviceOperations.Claim c,ZkOperationOutput result)=>Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,c.Id,c.Token,result));
  Task<int> Revoke(Guid r,string code)=>Q(db=>db.RolePermissions.Where(x=>x.RoleId==r&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==code)).ExecuteDeleteAsync());
  async Task Grant(Guid r,string code){await Q(async db=>{var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});await db.SaveChangesAsync();return true;});}
  async Task<Guid> Snapshot(int? minutes=5,string? product="product-A",string? firmware="firmware-1",ZkFunctionKeyValue? key=null){
   var id=Guid.NewGuid();await Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,id,device,version,"settings.read",5,null,"READ-ADVANCED-SETTINGS"));var c=await Claim()??throw new InvalidOperationException("No settings claim");if(c.Id!=id)throw new InvalidOperationException("Foreign claim");
   await Finish(c,new("succeeded",Settings:new(ZkAdvancedSettings.Version,"key-fixture",5,23,-1,minutes,30,62,product,firmware,FunctionKeys:new(ZkFunctionKeySnapshot.Version,"lab-key-read",[key??OldKey]))));return id;
  }
  ZkFunctionKeyPlan Plan(Guid snapshot)=>new(ZkFunctionKeyPlan.Version,snapshot,"key-fixture","product-A","firmware-1",OldKey,NewKey,"documented-effect","lab-contract");
  Task<Guid> Request(Guid id,ZkFunctionKeyPlan p,string? hash=null,Guid? user=null)=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(user??actor,id,device,version,"settings.key.set",5,null,ZkFunctionKeyPlan.Confirmation,functionKeyPlan:p,functionKeyPreviewHash:hash??p.PreviewHash()));
  Task<object> Preview(Guid snapshot,Guid? user=null)=>Use<DeviceOperations,object>(s=>s.PreviewFunctionKeyAsync(user??actor,device,version,snapshot,NewKey,"documented-effect","lab-contract"));
  var snapshot=await Snapshot();var plan=Plan(snapshot);
  check(!PermissionCatalog.Templates.Any(t=>t.Permissions.Contains(DeviceOperations.SetFunctionKey)||t.Permissions.Contains(DeviceOperations.ExecuteFunctionKeyWrite)),"S11 key settings human and service grants are independent and default ungranted");
  check(JsonSerializer.Serialize(await Preview(snapshot)).Contains(plan.PreviewHash(),StringComparison.Ordinal),"S11 key preview derives complete old tuple exact model descriptors and bound hash from SQL snapshot");
  check(await Reject<UnauthorizedAccessException>(()=>Preview(snapshot,reader))&&await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),plan,user:reader)),"S11 settings-read grant alone cannot preview or request key setting writes");
  check(await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),plan,hash:"other"))&&await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),plan with{Expected=plan.Expected with{AutoChangeTime="drift"}})),"S11 key request rejects altered hash and substituted old snapshot value");
  var id=Guid.NewGuid();await Request(id,plan);
  check(await Request(id,plan)==id&&await Reject<DomainConflictException>(()=>Request(id,plan with{Desired=plan.Desired with{StateName="changed"}})),"S11 immutable key intent accepts exact replay only and never a changed target");
  check(await Claim()==null&&await State(id)=="pending","S11 generic execution and settings-read execution cannot claim key writes");
  await Grant(exec,DeviceOperations.ExecuteFunctionKeyWrite);var claim=await Claim()??throw new InvalidOperationException("No key claim");
  check(claim.Id==id&&claim.FunctionKeyPlan==plan&&(await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true)))?.Value==0&&await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim with{FunctionKeyPlan=plan with{Desired=plan.Desired with{StateName="changed"}}},true))==null,"S11 key worker claim and private credential bind the exact immutable key plan");
  await Revoke(exec,DeviceOperations.ExecuteFunctionKeyWrite);
  check(await Reject<UnauthorizedAccessException>(()=>Finish(claim,new("unknown")))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true))),"S11 key worker grant revocation immediately denies completion and credential access");await Grant(exec,DeviceOperations.ExecuteFunctionKeyWrite);
  check(await Reject<ArgumentException>(()=>Finish(claim,new("succeeded")))&&await Reject<ArgumentException>(()=>Finish(claim,new("succeeded",FunctionKey:new(plan.Serial,plan.Product,plan.Firmware,plan.Desired with{AutoChange=0},plan.Effect,plan.ApprovalReference)))),"S11 key completion refuses bare ACK and wrong post-write AutoChange field");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET FunctionKeyPlanJson=REPLACE(FunctionKeyPlanJson,N'firmware-1',N'firmware-2') WHERE Id={id}"))),"S11 SQL rejects key plan mutation before completion independently of API checks");
  await Revoke(role,DeviceOperations.ReadFunctionKeys);
  check(await Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true))==null&&await Reject<UnauthorizedAccessException>(()=>Preview(snapshot))&&await Reject<UnauthorizedAccessException>(()=>Request(Guid.NewGuid(),plan)),"S11 revoking human key-read permission denies existing key credential and new preview or intent despite retained write right");await Grant(role,DeviceOperations.ReadFunctionKeys);
  await Revoke(exec,DeviceOperations.ExecuteFunctionKeys);
  check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceOperations,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,claim,true)))&&await Reject<UnauthorizedAccessException>(()=>Finish(claim,new("unknown"))),"S11 service key-read revocation independently denies key write credential and completion despite retained key-write execution right");await Grant(exec,DeviceOperations.ExecuteFunctionKeys);
  var wrongJson=JsonSerializer.Serialize(new ZkFunctionKeyObservation(plan.Serial,plan.Product,plan.Firmware,plan.Desired with{AutoChangeTime="reset"},plan.Effect,plan.ApprovalReference));
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET State=N'succeeded',FinishedAtUtc=SYSDATETIMEOFFSET(),ResultJson={wrongJson} WHERE Id={id}")))&&await State(id)=="running","S11 SQL independently refuses a key success with altered AutoChangeTime and rolls back terminal transition");
  await Finish(claim,new("unknown"));
  check(await Reject<DomainConflictException>(()=>Request(Guid.NewGuid(),plan)),"S11 unreviewed key unknown blocks another device mutation instead of blind retry");
  var drift=await Snapshot(key:OldKey with{AutoChangeTime="unapproved-drift"});
  check(await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(async s=>await s.ResolveFunctionKeyAsync(actor,id,await V(id),drift,"REVIEWED-APPROVED-FUNCTION-KEY"))),"S11 unknown key review refuses a later same-index tuple with different schedule rather than matching only state code");
  var observed=await Snapshot();await Use<DeviceOperations,Guid>(async s=>await s.ResolveFunctionKeyAsync(actor,id,await V(id),observed,"REVIEWED-APPROVED-FUNCTION-KEY"));
  check(await State(id)=="unknown"&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==id&&x.ResolutionInfoId==observed&&x.ResolvedAtUtc!=null)),"S11 later exact same-model old-value snapshot permits explicit review without converting unknown to success");
  var ok=Guid.NewGuid();await Request(ok,Plan(observed));var next=await Claim()??throw new InvalidOperationException("No next key claim");
  await Finish(next,new("succeeded",FunctionKey:new(plan.Serial,plan.Product,plan.Firmware,plan.Desired,plan.Effect,plan.ApprovalReference)));
  check(await State(ok)=="succeeded"&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==ok&&x.ResultJson!=null)),"S11 key successful typed observation retained only after exact readback");
  var history=JsonSerializer.SerializeToElement(await Use<DeviceOperations,object>(s=>s.ListSettingsAsync(reader,device,0)),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});var hidden=history.GetProperty("items").EnumerateArray().Single(x=>x.GetProperty("id").GetGuid()==ok);
  check(hidden.GetProperty("functionKeyPlan").ValueKind==JsonValueKind.Null&&hidden.GetProperty("functionKey").ValueKind==JsonValueKind.Null,"S11 settings-only history redacts both complete key plan and write observation without deleting SQL history");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DeviceOperations WHERE Id={ok}"))),"S11 SQL retains key history and refuses deletion");
  check(await Reject<SqlException>(()=>Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261113140000_DeviceApprovedRawSettings");return true;})),"S11 downgrade refuses deletion of retained key-setting request and observation history");
 }
}
