using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
internal static class AllDataEraseOperationalTests
{
 internal static async Task RunAsync(ServiceProvider provider,ConfigurationManager config,Guid actor,Guid service,Guid reader,Guid site,Action<bool,string> check)
 {
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  void Configure(string prefix,JsonElement value){foreach(var p in value.EnumerateObject()){if(p.Value.ValueKind==JsonValueKind.Object)Configure(prefix+":"+p.Name,p.Value);else if(p.Value.ValueKind!=JsonValueKind.Null)config[prefix+":"+p.Name]=p.Value.ToString();}}
  int index=0;
  foreach(var family in new[]{"all-data"})
  {
   void Check(bool ok,string m)=>check(ok,"R15 "+family+" "+m);
   foreach(var (user,codes) in new[]{(actor,new[]{"read","propose","approve","recover.propose","recover.approve"}),(reader,new[]{"read","approve","recover.approve"}),(service,new[]{"execute"})})
    await Q(async db=>{var role=await db.UserRoles.Where(x=>x.UserId==user).Select(x=>x.RoleId).SingleAsync();foreach(var code in codes.Select(a=>DeviceAllDataErases.Right(family,a)).Concat(user==service?[DeviceUserDownloads.Execute,DeviceAttendanceDownloads.Execute]:[DeviceUserDownloads.Read,DeviceAttendanceDownloads.Read])){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}if(!await db.RolePermissions.AnyAsync(x=>x.RoleId==role&&x.PermissionId==p.Id))db.RolePermissions.Add(new(){RoleId=role,PermissionId=p.Id});}await db.SaveChangesAsync();return true;});
   var device=Guid.NewGuid();var ip="192.0.2."+(45+index++);await Use<DeviceRegistry,Guid>(x=>x.CreateAsync(actor,device,new(site,"SYNTHETIC-PROTECTED-"+family,"Synthetic protected erasure","unverified","zk-standalone",ip,4370,true)));
   var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
   var approval=AllDataEraseTests.Approval();var rows=UserEraseSdkDouble.Initial(approval.Inventory).Select(e=>e.User).ToArray();
   async Task<DeviceAllDataErases.Input> Input(Guid? parent=null,DateTimeOffset? reset=null)
   {
    var archive=Guid.NewGuid();await Use<DeviceUserDownloads,Guid>(x=>x.RequestAsync(actor,archive,device,version,30));var claim=await Use<DeviceUserDownloads,DeviceUserDownloads.Claim?>(x=>x.ClaimAsync(service))??throw new InvalidOperationException("User archive claim required.");
    if(claim.Id!=archive)throw new InvalidOperationException("Unexpected user queue.");await Use<DeviceUserDownloads,long>(x=>x.AppendAsync(service,archive,claim.Token,0,rows));await Use<DeviceUserDownloads,Guid>(x=>x.CompleteAsync(service,archive,claim.Token,"succeeded",rows.Length));
    var av=await Q(db=>db.DeviceUserDownloads.Where(x=>x.Id==archive).Select(x=>x.Version).SingleAsync());var log=Guid.NewGuid();await Use<DeviceAttendanceDownloads,Guid>(x=>x.RequestAsync(actor,log,device,version,30,new(2020,1,1,0,0,0,TimeSpan.Zero),new(2030,1,1,0,0,0,TimeSpan.Zero),0,1));
    var lc=await Use<DeviceAttendanceDownloads,DeviceAttendanceDownloads.Claim?>(x=>x.ClaimAsync(service))??throw new InvalidOperationException("Attendance archive claim required.");if(lc.Id!=log)throw new InvalidOperationException("Unexpected log queue.");
    var logs=AllDataEraseTests.Rows(approval);await Use<DeviceAttendanceDownloads,long>(x=>x.AppendAsync(service,log,lc.Token,0,logs));await Use<DeviceAttendanceDownloads,Guid>(x=>x.CompleteAsync(service,log,lc.Token,"succeeded",logs.Length));
    var lv=await Q(db=>db.DeviceAttendanceDownloads.Where(x=>x.Id==log).Select(x=>x.Version).SingleAsync());
    return new(Guid.NewGuid(),device,archive,version,av,reset??DateTimeOffset.UtcNow.AddSeconds(-20),"SYNTHETIC-EXCLUSIVE-CONTROL-RESET-AND-LEGAL",approval,log,lv,parent);
   }
   Task<object> Preview(DeviceAllDataErases.Input i,Guid? who=null)=>Use<DeviceAllDataErases,object>(x=>x.PreviewAsync(who??actor,i,family));
   Task<byte[]> Version(Guid id)=>Q(db=>db.DeviceAllDataErases.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
   async Task<Guid> Propose(DeviceAllDataErases.Input i)
   {
    var p=JsonSerializer.SerializeToElement(await Preview(i));var c=new DeviceAllDataErases.Confirm(i,p.GetProperty("previewHash").GetString()!,DeviceAllDataErases.Confirmation(family,i.RecoveryOfId!=null));var id=await Use<DeviceAllDataErases,Guid>(x=>x.ProposeAsync(actor,c,family));Check(await Use<DeviceAllDataErases,Guid>(x=>x.ProposeAsync(actor,c,family))==id,"exact retry returns same immutable intent, never redispatch");return id;
   }
   Task<Guid> Approve(Guid who,Guid id,bool recovery=false)=>Use<DeviceAllDataErases,Guid>(async x=>await x.ApproveAsync(who,id,new(await Version(id),"SYNTHETIC-INDEPENDENT-REVIEW",DeviceAllDataErases.Confirmation(family,recovery)),family));
   var intent=await Input();Check(await Reject<DomainConflictException>(()=>Preview(intent)),"independent family switch is default-off");
   config["IO:AllDataErase:"+family+":Enabled"]="true";Check(await Reject<DomainConflictException>(()=>Preview(intent)),"enabled switch without exact installed allowlist still refuses deletion");Configure("IO:AllDataErase:"+family+":Approvals:0",JsonSerializer.SerializeToElement(approval));
   Check(await Reject<UnauthorizedAccessException>(()=>Preview(intent,reader)),"review/read grants do not imply proposal or device scope");
   Check(await Reject<DomainConflictException>(()=>Preview(intent with{QuiescedAtUtc=DateTimeOffset.UtcNow})),"archive predating settled physical exclusive-control cannot authorize delete");
   Check(await Reject<DomainConflictException>(()=>Preview(intent with{ArchiveVersion=new byte[8]})),"stale archive version rejected");
   var wrong="factory-reset";Check(await Reject<ArgumentException>(()=>Use<DeviceAllDataErases,object>(x=>x.PreviewAsync(actor,intent,wrong))),"route cannot retarget family");
   var preview=JsonSerializer.SerializeToElement(await Preview(intent));Check(preview.GetProperty("archiveCount").GetInt32()==2&&preview.GetProperty("attendanceCount").GetInt32()==2,"preview binds full durable user catalogue, including administrator outside target");
   Check(await Reject<DomainConflictException>(()=>Preview(intent with{AttendanceArchiveVersion=new byte[8]})),"stale attendance archive version rejected independently of user archive");
   var changedIntent=intent with{AttendanceArchiveVersion=new byte[8]};
   Check(await Reject<DomainConflictException>(()=>Use<DeviceAllDataErases,Guid>(x=>x.ProposeAsync(actor,new(changedIntent,preview.GetProperty("previewHash").GetString()!,DeviceAllDataErases.Confirmation(family,false)),family))),"preview cannot silently replace second durable archive");
   var id=await Propose(intent);
   Check(await Reject<DomainConflictException>(()=>Approve(actor,id)),"proposer cannot countersign even holding approve grant");
   Check(await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(x=>x.RequestAsync(actor,Guid.NewGuid(),device,version,30))),"proposed erasure reserves endpoint across other native queues");
   Check(JsonSerializer.SerializeToElement(await Use<DeviceAllDataErases,object>(x=>x.ListAsync(reader,family))).GetProperty("items").GetArrayLength()==0,"restricted reader cannot see out-of-scope intent metadata");
   await Use<LoginIdentityDbContext,bool>(async db=>{db.DeviceOperatorGrants.Add(new(){Id=Guid.NewGuid(),UserId=reader,DeviceId=device,GrantedAtUtc=DateTimeOffset.UtcNow});await db.SaveChangesAsync();return true;});
   bool tamper=false;try{await Q(async db=>{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceAllDataErases SET ArchiveDigest={new string('A',64)} WHERE Id={id}");return true;});}catch(SqlException e)when(e.Number==51045){tamper=true;}Check(tamper,"SQL independently freezes archived metadata commitment");
   tamper=false;try{await Q(async db=>{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceAllDataErases SET AttendanceDigest={new string('A',64)} WHERE Id={id}");return true;});}catch(SqlException e)when(e.Number==51045){tamper=true;}Check(tamper,"SQL independently freezes attendance multiset commitment");
   await Approve(reader,id);
   var host=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..","dotnet.exe"));var relay=Path.Combine(AppContext.BaseDirectory,"virtual-relay","IO.DeviceRuntime.Tests.dll");
   var configuration=new WorkerConfiguration(service,[],[new(device,ip,4370,7,approval.Inventory.Device.Serial,true,AllowUserRead:true,AllowAttendanceRead:true,AllDataEraseApprovals:[approval])],host,relay,false,false,false,false,false,false,AllDataEraseFamilies:[family]);
   async Task Work(Guid job,string expected)
   {
    using var worker=new AllDataEraseWorker(provider.GetRequiredService<IServiceScopeFactory>(),configuration);await worker.StartAsync(CancellationToken.None);
    try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));string state;do{await Task.Delay(100,deadline.Token);state=await Q(db=>db.DeviceAllDataErases.Where(x=>x.Id==job).Select(x=>x.State).SingleAsync());}while(state is "pending" or "running");Check(state==expected,"actual Windows worker, owned credential/pins, production executor in synthetic child and durable result "+expected);}
    finally{await worker.StopAsync(CancellationToken.None);}
   }
   var success="all-data-cleared-verified";
   await Work(id,success);Check(await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>x.DownloadId==intent.AttendanceArchiveId))==2,"clear never deletes durable native attendance archive");Check(await Q(db=>db.DeviceDownloadedUsers.CountAsync(x=>x.DownloadId==intent.ArchiveId))==2,"device erase never deletes durable metadata archive or employees");
   intent=await Input();id=await Propose(intent);await Approve(reader,id);var owned=await Use<DeviceAllDataErases,DeviceAllDataErases.Claim?>(x=>x.ClaimAsync(service,family))??throw new InvalidOperationException("Protected claim required.");
   Check(await Reject<DomainConflictException>(()=>Use<DeviceAllDataErases,DeviceCommunicationKey?>(x=>x.CredentialAsync(service,owned with{Token=Guid.NewGuid()},true))),"foreign claim cannot obtain protected credential");
   Check(await Reject<ArgumentException>(()=>Use<DeviceAllDataErases,Guid>(x=>x.CompleteAsync(service,owned,new(id,"administrators-cleared-verified")))),"wrong-family success rejected even with correlated operation ID");
   await Use<DeviceAllDataErases,Guid>(async x=>await x.CancelAsync(actor,id,await Version(id),family));Check(await Use<DeviceAllDataErases,bool>(x=>x.ShouldStopAsync(service,owned)),"running cancellation revokes authority, not proof of cancellation");
   await Use<DeviceAllDataErases,Guid>(x=>x.CompleteAsync(service,owned,new(id,success)));Check(await Q(db=>db.DeviceAllDataErases.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync())=="unknown","late success cannot defeat cancellation race");
   Check(await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(x=>x.RequestAsync(actor,Guid.NewGuid(),device,version,"clock.set",30,DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),"SET-CLOCK"))),"unknown deletion blocks unrelated native writes");
   var reset=DateTimeOffset.UtcNow;await Task.Delay(TimeSpan.FromSeconds(11));var recovery=await Input(id,reset);
   Check(await Reject<DomainConflictException>(()=>Preview(recovery)),"recovery has its own default-off switch");config["IO:AllDataErase:"+family+":EnableRecovery"]="true";
   var recoveryId=await Propose(recovery);Check(await Reject<DomainConflictException>(()=>Approve(reader,recoveryId,true)),"independent recovery approval cannot bypass pending native read-only observation");
   await Work(recoveryId,"observed");Check(await Reject<DomainConflictException>(()=>Approve(actor,recoveryId,true)),"recovery observation cannot be self-approved");
   var observation=await Q(db=>db.DeviceAllDataErases.Where(x=>x.Id==recoveryId).Select(x=>x.FinishedAtUtc).SingleAsync());
   await Use<DeviceAllDataErases,Guid>(async x=>await x.CancelAsync(actor,recoveryId,await Version(recoveryId),family));
   Check(await Q(db=>db.DeviceAllDataErases.AnyAsync(x=>x.Id==recoveryId&&x.State=="inspection-failed"&&x.FinishedAtUtc==observation)),"discarding an observed recovery preserves its original inspection time and never releases unknown");
   Check(await Reject<DomainConflictException>(()=>Approve(reader,recoveryId,true)),"discarded observation cannot be released or replayed");
   Check(await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(x=>x.RequestAsync(actor,Guid.NewGuid(),device,version,"clock.set",30,DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),"SET-CLOCK"))),"discarded observation leaves original unknown write fence intact");
   recovery=await Input(id,reset);recoveryId=await Propose(recovery);await Work(recoveryId,"observed");await Approve(reader,recoveryId,true);
   Check(await Q(db=>db.DeviceAllDataErases.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync())=="unknown","physical-reset read-only observation and independent release preserve original unknown forever");
   tamper=false;try{await Q(async db=>{await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.DeviceAllDataErases WHERE Id={recoveryId}");return true;});}catch(SqlException e)when(e.Number==51045){tamper=true;}Check(tamper,"SQL refuses recovery evidence deletion");
   var after=await Input();var afterId=await Propose(after);await Approve(reader,afterId);config["IO:AllDataErase:"+family+":Enabled"]="false";Check(await Use<DeviceAllDataErases,DeviceAllDataErases.Claim?>(x=>x.ClaimAsync(service,family)) is null&&await Q(db=>db.DeviceAllDataErases.Where(x=>x.Id==afterId).Select(x=>x.State).SingleAsync())=="revoked","live installation policy revocation prevents claim dispatch");config["IO:AllDataErase:"+family+":Enabled"]="true";
   await AllDataEraseHttpTests.RunAsync(provider,actor,after,family,check);
  }
  check(!PermissionCatalog.Templates.Any(t=>t.Permissions.Any(p=>p.StartsWith("devices.all-data.erase.",StringComparison.Ordinal))),"R15 no default role inherits destructive or recovery authority");
 }
}
