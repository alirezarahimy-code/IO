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
internal static class LegacyUserReadTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var service=Guid.NewGuid();var role=Guid.NewGuid();var exec=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var employee=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Legacy lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Legacy company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Legacy site",TimeZoneId="UTC"});db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="1",DisplayName="Legacy employee"});
   foreach(var(u,n)in new[]{(actor,"legacy-actor"),(service,"legacy-service")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"legacy-human",new[]{DeviceUserDownloads.Read,DeviceUserDownloads.Download,DeviceUserUploads.Upload,DeviceRegistry.Create}),(exec,service,"legacy-exec",new[]{DeviceUserDownloads.Execute})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var permission=await db.Permissions.SingleAsync(x=>x.Code==code);db.RolePermissions.Add(new(){RoleId=r,PermissionId=permission.Id});}}
   await db.SaveChangesAsync();return true;});
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,new(site,"S07-LEGACY","Legacy read candidate","unverified non-TFT","zk-standalone","127.0.0.1",4370,true)));
  var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());var id=Guid.NewGuid();
  Task<Guid> Request(Guid operation,string format)=>Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(actor,operation,device,version,30,readFormat:format));
  check(await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),"auto"))&&await Reject<ArgumentException>(()=>Request(Guid.NewGuid(),"legacy-int32-v1 ")),"S07 legacy requires an exact known read contract rather than auto detection or whitespace normalization");
  await Request(id,ZkUserReadFormats.Legacy);
  check(await Request(id,ZkUserReadFormats.Legacy)==id&&await Reject<DomainConflictException>(()=>Request(id,ZkUserReadFormats.Ssr)),"S07 approved legacy intent replays once and cannot switch its identity contract on uncertain retry");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceUserDownloads SET ReadFormat=N'ssr-v1' WHERE Id={id}"))),"S07 SQL prevents mutating the read format even while the request is still pending");
  var claim=await Use<DeviceUserDownloads,DeviceUserDownloads.Claim?>(s=>s.ClaimAsync(service))??throw new InvalidOperationException();
  check(claim.Id==id&&claim.ReadFormat==ZkUserReadFormats.Legacy,"S07 claim carries the immutable approved format to the native worker instead of inferring it from device model text");
  await Use<DeviceUserDownloads,long>(s=>s.AppendAsync(service,id,claim.Token,0,new[]{new ZkUser("1","Legacy employee","000123",0,true),new ZkUser("2","Different identity",null,0,true)}));
  var invalidLegacy=await Reject<ArgumentException>(()=>Use<DeviceUserDownloads,long>(s=>s.AppendAsync(service,id,claim.Token,2,new[]{new ZkUser("01","Invalid padded identity",null,0,true)})));
  await Use<DeviceUserDownloads,Guid>(s=>s.CompleteAsync(service,id,claim.Token,"succeeded",2));
  var preview=JsonSerializer.SerializeToElement(await Use<DeviceUserDownloads,object>(s=>s.PreviewAsync(actor,id,0)));
  var rows=preview.GetProperty("items").EnumerateArray().ToArray();
  check(invalidLegacy&&preview.GetProperty("ReadFormat").GetString()==ZkUserReadFormats.Legacy&&rows[0].GetProperty("status").GetString()=="candidate-requires-confirmation"&&rows[1].GetProperty("status").GetString()=="unknown-identifier"&&rows[0].GetProperty("Card").GetString()=="000123","S07 SQL preview exposes legacy provenance but rejects zero-padded legacy service frames while preserving exact matching and original card text");
  await Q(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=device,CompanyId=company,SourcePersonId="1",EmployeeId=employee,ValidFromUtc=DateTimeOffset.UtcNow.AddDays(-1)});await db.SaveChangesAsync();return true;});
  var upload=new DeviceUserUploads.Batch(Guid.NewGuid(),new[]{new DeviceUserUploads.Item(Guid.NewGuid(),device,version,id,employee,"1","000123",true,30)},"UPLOAD-ORDINARY-USERS");
  check(await Reject<DomainConflictException>(()=>Use<DeviceUserUploads,object>(s=>s.PreviewAsync(actor,upload)))&&!await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==device)),"S07 a legacy target snapshot cannot authorize the SSR-only user-write adapter or create a mutation intent");
  bool down=false;try{await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261112140000_DevicePollOverlap");return true;});}catch(SqlException e)when(e.Number==51028){down=true;}
  check(down&&await Q(db=>db.DeviceUserDownloads.AnyAsync(x=>x.Id==id&&x.ReadFormat==ZkUserReadFormats.Legacy)),"S07 downgrade refuses to discard retained legacy identity provenance");
  var defaultId=Guid.NewGuid();await Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(actor,defaultId,device,version,30));
  var defaultRow=await Q(db=>db.DeviceUserDownloads.AsNoTracking().SingleAsync(x=>x.Id==defaultId));
  check(defaultRow.ReadFormat==ZkUserReadFormats.Ssr,"S07 omitted read format stays SSR for compatibility and never silently enables a legacy endpoint");
  await Use<DeviceUserDownloads,Guid>(s=>s.CancelAsync(actor,defaultId,defaultRow.Version));
 }
}
