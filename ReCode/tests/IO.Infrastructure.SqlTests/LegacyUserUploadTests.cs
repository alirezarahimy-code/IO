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
internal static class LegacyUserUploadTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var clock=new Clock();var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);services.AddSingleton<TimeProvider>(clock);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var service=Guid.NewGuid();var role=Guid.NewGuid();var exec=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var employee=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Legacy lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Legacy company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Legacy site",TimeZoneId="UTC"});db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="1",DisplayName="New legacy name"});
   foreach(var(u,n)in new[]{(actor,"legacy-write-actor"),(service,"legacy-write-service")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"legacy-write-human",new[]{DeviceUserDownloads.Read,DeviceUserDownloads.Download,DeviceUserUploads.Upload,DeviceRegistry.Create}),(exec,service,"legacy-write-exec",new[]{DeviceUserDownloads.Execute,DeviceUserUploads.Execute})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var permission=await db.Permissions.SingleAsync(x=>x.Code==code);db.RolePermissions.Add(new(){RoleId=r,PermissionId=permission.Id});}}
   await db.SaveChangesAsync();return true;});
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,new(site,"S08-LEGACY","Legacy read candidate","unverified non-TFT","zk-standalone","127.0.0.1",4370,true)));
  var second=Guid.NewGuid();await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,second,new(site,"S08-SSR-MIX","SSR write candidate","unverified SSR","zk-standalone","127.0.0.1",4371,true)));
  await Q(async db=>{foreach(var d in new[]{device,second})db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=d,CompanyId=company,SourcePersonId="1",EmployeeId=employee,ValidFromUtc=clock.GetUtcNow().AddDays(-1)});await db.SaveChangesAsync();return true;});
  Task<byte[]> DV(Guid d)=>Q(db=>db.Devices.Where(x=>x.Id==d).Select(x=>x.Version).SingleAsync());
  Task<byte[]> V(Guid id)=>Q(db=>db.DeviceUserUploads.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  var original=new ZkUser("1","Old","123",0,true);var updated=original with{Name="New legacy name",Card="456"};
  async Task<Guid> Snapshot(Guid d,string format,ZkUser row)
  {
   var id=Guid.NewGuid();var version=await DV(d);await Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(actor,id,d,version,30,readFormat:format));
   var c=await Use<DeviceUserDownloads,DeviceUserDownloads.Claim?>(s=>s.ClaimAsync(service))??throw new InvalidOperationException();if(c.Id!=id)throw new InvalidOperationException("Unexpected pending user read fixture");
   await Use<DeviceUserDownloads,long>(s=>s.AppendAsync(service,id,c.Token,0,new[]{row}));await Use<DeviceUserDownloads,Guid>(s=>s.CompleteAsync(service,id,c.Token,"succeeded",1));return id;
  }
  async Task<DeviceUserUploads.Batch> Batch(params (Guid Device,Guid Snapshot)[] targets)
  {var items=new List<DeviceUserUploads.Item>();foreach(var t in targets)items.Add(new(Guid.NewGuid(),t.Device,await DV(t.Device),t.Snapshot,employee,"1","456",true,30));return new(Guid.NewGuid(),items.ToArray(),"UPLOAD-ORDINARY-USERS");}
  Task<Guid> Request(DeviceUserUploads.Batch b)=>Use<DeviceUserUploads,Guid>(s=>s.RequestAsync(actor,b));
  async Task<DeviceUserUploads.Batch> Approve(DeviceUserUploads.Batch b)
  {var preview=JsonSerializer.SerializeToElement(await Use<DeviceUserUploads,object>(s=>s.PreviewAsync(actor,b)));return b with{PreviewHash=preview.GetProperty("previewHash").GetString()};}
  Task<DeviceUserUploads.Claim?> Claim()=>Use<DeviceUserUploads,DeviceUserUploads.Claim?>(s=>s.ClaimAsync(service));
  Task<Guid> Finish(DeviceUserUploads.Claim c,string outcome)=>Use<DeviceUserUploads,Guid>(s=>s.CompleteAsync(service,c.Id,c.Token,new(outcome)));
  var legacy=await Snapshot(device,ZkUserReadFormats.Legacy,original);var ssr=await Snapshot(second,ZkUserReadFormats.Ssr,original);
  var batch=await Batch((device,legacy),(second,ssr));var plan=JsonSerializer.SerializeToElement(await Use<DeviceUserUploads,object>(s=>s.PreviewAsync(actor,batch)));
  check(plan.GetProperty("items").EnumerateArray().Select(x=>x.GetProperty("writeFormat").GetString()).ToHashSet().SetEquals(new[]{ZkUserReadFormats.Ssr,ZkUserReadFormats.Legacy})&&await Reject<DomainConflictException>(()=>Request(batch)),"S08 mixed legacy and SSR target preview exposes each immutable write contract and still requires exact server approval");
  batch=batch with{PreviewHash=plan.GetProperty("previewHash").GetString()};await Request(batch);
  check(await Request(batch)==batch.BatchId&&await Q(db=>db.DeviceUserUploads.CountAsync(x=>x.BatchId==batch.BatchId))==2,"S08 mixed-format exact batch replay creates one immutable intent per target without duplicating native writes");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceUserDownloads SET ReadFormat=N'ssr-v1' WHERE Id={legacy}"))),"S08 SQL immutable target snapshot and intent SnapshotId retain the approved write format without a second mutable format column");
  var first=await Claim()??throw new InvalidOperationException();var other=await Claim()??throw new InvalidOperationException();var lc=first.DeviceId==device?first:other;var sc=first.DeviceId==second?first:other;
  check(lc.WriteFormat==ZkUserReadFormats.Legacy&&sc.WriteFormat==ZkUserReadFormats.Ssr&&await Use<DeviceUserUploads,DeviceCommunicationKey?>(s=>s.CredentialAsync(service,lc with{WriteFormat=ZkUserReadFormats.Ssr},true))==null&&(await Use<DeviceUserUploads,DeviceCommunicationKey?>(s=>s.CredentialAsync(service,lc,true)))?.Value==0,"S08 independent claims derive exact target contracts and forged contract claims cannot obtain communication credentials");
  await Finish(lc,"unknown");await Finish(sc,"succeeded");var history=JsonSerializer.SerializeToElement(await Use<DeviceUserUploads,object>(s=>s.ListAsync(actor,batch.BatchId,0)));
  check(history.GetProperty("items").GetArrayLength()==2&&await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==lc.Id&&x.State=="unknown"))&&await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==sc.Id&&x.State=="succeeded")),"S08 mixed native outcomes retain independent unknown legacy and successful SSR results with their original snapshot evidence");
  var blocked=await Batch((device,legacy));check(await Reject<DomainConflictException>(()=>Approve(blocked)),"S08 unresolved unknown legacy write blocks blind enqueue or selective retry of that person");
  clock.Advance();var wrong=await Snapshot(device,ZkUserReadFormats.Ssr,updated);
  check(await Reject<DomainConflictException>(()=>Use<DeviceUserUploads,Guid>(async s=>await s.ReviewAsync(actor,lc.Id,await V(lc.Id),wrong,"REVIEWED-USER")))&&await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==lc.Id&&x.ResolutionSnapshotId==null)),"S08 unknown legacy review rejects a newer but different-format snapshot instead of silently changing the identity contract");
  clock.Advance();var fresh=await Snapshot(device,ZkUserReadFormats.Legacy,updated);await Use<DeviceUserUploads,Guid>(async s=>await s.ReviewAsync(actor,lc.Id,await V(lc.Id),fresh,"REVIEWED-USER"));
  check(await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==lc.Id&&x.State=="unknown"&&x.ResolutionSnapshotId==fresh)),"S08 explicit same-format fresh legacy review records evidence but never rewrites the original unknown outcome");
  var retry=await Approve(await Batch((device,fresh)));await Request(retry);var rc=await Claim()??throw new InvalidOperationException();await Finish(rc,"unchanged");
  check(rc.WriteFormat==ZkUserReadFormats.Legacy&&rc.Expected==updated&&await Q(db=>db.DeviceUserUploads.CountAsync(x=>x.BatchId==retry.BatchId))==1&&await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==lc.Id&&x.State=="unknown")),"S08 selective reviewed retry creates only the chosen legacy intent with the fresh expected state and preserves prior mixed results");
  var revoked=await Approve(await Batch((device,fresh)));await Request(revoked);await Q(db=>db.RolePermissions.Where(x=>x.RoleId==role&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==DeviceUserUploads.Upload)).ExecuteDeleteAsync());
  check(await Claim()==null&&await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==revoked.Items[0].Id&&x.State=="revoked"))&&await Reject<UnauthorizedAccessException>(()=>Request(batch)),"S08 current human upload revocation blocks queued legacy dispatch and even prior successful idempotency replay");
 }
 private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance()=>now=now.AddSeconds(1);}
}
