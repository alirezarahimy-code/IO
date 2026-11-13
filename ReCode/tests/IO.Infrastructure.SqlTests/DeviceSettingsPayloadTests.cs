using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Security;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
internal static class DeviceSettingsPayloadTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> SqlReject(Func<Task> action,int number=547){try{await action();return false;}catch(SqlException e)when(e.Number==number){return true;}}
  var actor=Guid.NewGuid();var service=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Large settings lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Large settings company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Large settings site",TimeZoneId="UTC"});
   foreach(var(u,rights)in new[]{(actor,new[]{DeviceAccess.Manage,DeviceRegistry.Create,DeviceOperations.Read,DeviceOperations.ReadFunctionKeys,DeviceOperations.ReadSettings}),(service,new[]{DeviceOperations.Execute,DeviceOperations.ExecuteFunctionKeys,DeviceOperations.ExecuteSettings})}){
    var role=Guid.NewGuid();db.Users.Add(new(){Id=u,UserName="settings-payload-"+u.ToString("N"),IsActive=true});db.Roles.Add(new(){Id=role,Name="settings-payload-"+role.ToString("N")});db.UserRoles.Add(new(){UserId=u,RoleId=role});foreach(var code in rights){var permission=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleAsync(x=>x.Code==code);db.RolePermissions.Add(new(){RoleId=role,PermissionId=permission.Id});}}
   await db.SaveChangesAsync();return true;});
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,new(site,"SETTINGS-PAYLOAD","Large settings candidate","synthetic-not-ZK","zk-standalone","127.0.0.1",4370,true)));
  await Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(actor,service,null,true,"RESTRICT-DEVICE-OPERATOR"));await Use<DeviceAccess,Guid>(s=>s.GrantAsync(actor,Guid.NewGuid(),service,device));
  var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
  async Task<DeviceOperations.Claim> Claim(string kind="settings.read"){
   var id=Guid.NewGuid();await Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,id,device,version,kind,30,null,kind=="settings.read"?"READ-ADVANCED-SETTINGS":null));
   var claim=await Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(service,includeSettings:true));if(claim?.Id!=id)throw new InvalidOperationException("Unexpected payload fixture claim");return claim;
  }
  Task<Guid> Finish(DeviceOperations.Claim c,ZkOperationOutput result)=>Use<DeviceOperations,Guid>(s=>s.CompleteAsync(service,c.Id,c.Token,result));
  var text=new string('ژ',128);var snapshot=new ZkAdvancedSettings(ZkAdvancedSettings.Version,"settings-payload-serial",0,null,-1,15,2,62,"synthetic-product","synthetic-fw",FunctionKeys:new(ZkFunctionKeySnapshot.Version,"synthetic-read-approval",Enumerable.Range(0,8).Select(i=>new ZkFunctionKeyValue(i,true,0,-i,text,2,text)).ToArray()),ReportedSubnetMask:"255.255.255.0",ReportedGateway:"0.0.0.0");var json=JsonSerializer.Serialize(snapshot);
  check(snapshot.IsValid()&&json.Length>4000&&json.Length<32768,"S11 large valid eight-key Unicode settings exceed the former SQL column but fit the bounded child contract");
  check(await Q(db=>db.Database.SqlQueryRaw<short>("SELECT max_length AS Value FROM sys.columns WHERE object_id=OBJECT_ID(N'dbo.DeviceOperations') AND name=N'ResultJson'").SingleAsync())==-1,"S11 migration stores operation results in nvarchar max with explicit per-kind SQL bounds rather than implicit truncation");
  var c=await Claim();await Finish(c,new("succeeded",Settings:snapshot));var retained=await Q(db=>db.DeviceOperations.Where(x=>x.Id==c.Id).Select(x=>x.ResultJson).SingleAsync());
  check(retained==json&&retained!.Length>4000,"S11 typed completion retains the entire large Unicode settings snapshot byte-for-character without truncation");
  var history=JsonSerializer.SerializeToElement(await Use<DeviceOperations,object>(s=>s.ListSettingsAsync(actor,device,0)),new JsonSerializerOptions{PropertyNamingPolicy=JsonNamingPolicy.CamelCase});var keys=history.GetProperty("items")[0].GetProperty("settings").GetProperty("functionKeys").GetProperty("items");
  check(keys.GetArrayLength()==8&&keys[7].GetProperty("stateName").GetString()==text&&keys[7].GetProperty("autoChangeTime").GetString()==text,"S11 authorized SQL history round-trips every raw field from all eight Unicode key observations");
  var over=await Claim();var oversized=JsonSerializer.Serialize(new{Padding=new string('x',32768)});
  check(await SqlReject(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET State=N'succeeded',FinishedAtUtc=SYSDATETIMEOFFSET(),ResultJson={oversized} WHERE Id={over.Id}")))&&await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==over.Id&&x.State=="running"&&x.ResultJson==null)),"S11 SQL refuses settings results above 65536 storage bytes without committing a partial terminal state");await Finish(over,new("failed"));
  var info=await Claim("information.read");var priorBudget=JsonSerializer.Serialize(new{Padding=new string('x',5000)});
  check(await SqlReject(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET State=N'succeeded',FinishedAtUtc=SYSDATETIMEOFFSET(),ResultJson={priorBudget} WHERE Id={info.Id}"))),"S11 widening settings storage does not widen the existing information network or idle result budgets");await Finish(info,new("failed"));
  check(await SqlReject(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceOperations SET ResultJson=N'{{}}' WHERE Id={c.Id}")),51017)&&await SqlReject(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"DELETE dbo.DeviceOperations WHERE Id={c.Id}")),51017),"S11 large settings observations retain the same terminal immutability and deletion guards");
  check(await SqlReject(()=>Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261113120000_DevicePollReadFormats");return true;}),51033)&&await Q(db=>db.DeviceOperations.Where(x=>x.Id==c.Id).Select(x=>x.ResultJson).SingleAsync())==json,"S11 downgrade refuses to shrink retained large settings observations instead of deleting or truncating evidence");
 }
}
