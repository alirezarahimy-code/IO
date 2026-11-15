using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
internal static class AttendanceEraseOperationalTests
{
 internal static async Task RunAsync(ServiceProvider provider,ConfigurationManager config,Guid actor,Guid service,Guid reader,Guid site,Action<bool,string> check)
 {
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  void Configure(string prefix,JsonElement value){foreach(var p in value.EnumerateObject()){if(p.Value.ValueKind==JsonValueKind.Object)Configure(prefix+":"+p.Name,p.Value);else if(p.Value.ValueKind!=JsonValueKind.Null)config[prefix+":"+p.Name]=p.Value.ToString();}}
  foreach(var (user,codes)in new[]{(actor,new[]{DeviceAttendanceErases.Read,DeviceAttendanceErases.Propose,DeviceAttendanceErases.Approve,DeviceAttendanceErases.Recover,DeviceAttendanceErases.ReviewRecovery,DeviceAttendanceDownloads.Read}),(reader,new[]{DeviceAttendanceErases.Read,DeviceAttendanceErases.Approve,DeviceAttendanceErases.ReviewRecovery,DeviceAttendanceDownloads.Read}),(service,new[]{DeviceAttendanceErases.Execute,DeviceAttendanceDownloads.Execute})})
   await Q(async db=>{var role=await db.UserRoles.Where(x=>x.UserId==user).Select(x=>x.RoleId).SingleAsync();foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}if(!await db.RolePermissions.AnyAsync(x=>x.RoleId==role&&x.PermissionId==p.Id))db.RolePermissions.Add(new(){RoleId=role,PermissionId=p.Id});}await db.SaveChangesAsync();return true;});
  var device=Guid.NewGuid();await Use<DeviceRegistry,Guid>(x=>x.CreateAsync(actor,device,new(site,"SYNTHETIC-ERASE-D","Synthetic erase terminal","unverified","zk-standalone","192.0.2.41",4370,true)));
  var version=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());
  var approval=new AttendanceEraseApproval(new("SYNTHETIC-ERASE-D","SYNTHETIC-PRODUCT","SYNTHETIC-FIRMWARE"),ZkAttendanceReadFormats.Ssr,"SYNTHETIC-AUTH","SYNTHETIC-RETENTION","SYNTHETIC-PHYSICAL-CAPTURE-STOP","SYNTHETIC-RESET",CaptureStoppedDuringMaintenance:true,ResetClearsPendingWrites:true,AllowErase:true);
  async Task<Guid> Archive()
  {
   var id=Guid.NewGuid();await Use<DeviceAttendanceDownloads,Guid>(x=>x.RequestAsync(actor,id,device,version,30,new(2020,1,1,0,0,0,TimeSpan.Zero),new(2030,1,1,0,0,0,TimeSpan.Zero),0,1));
   var claim=await Use<DeviceAttendanceDownloads,DeviceAttendanceDownloads.Claim?>(x=>x.ClaimAsync(service))??throw new InvalidOperationException("Archive claim required.");
   if(claim.Id!=id)throw new InvalidOperationException("Unexpected queued archive.");
   ZkAttendanceRaw raw=new("001",1,77,2026,11,8,6,0,0,5);
   await Use<DeviceAttendanceDownloads,long>(x=>x.AppendAsync(service,id,claim.Token,0,[raw,raw]));await Use<DeviceAttendanceDownloads,Guid>(x=>x.CompleteAsync(service,id,claim.Token,"succeeded",2));return id;
  }
  async Task<DeviceAttendanceErases.Input> Input(Guid? parent=null,DateTimeOffset? reset=null)
  {
   var archive=await Archive();var av=await Q(db=>db.DeviceAttendanceDownloads.Where(x=>x.Id==archive).Select(x=>x.Version).SingleAsync());
   return new(Guid.NewGuid(),device,archive,version,av,reset??DateTimeOffset.UtcNow.AddSeconds(-20),"SYNTHETIC-PHYSICAL-CAPTURE-STOP-AND-RETENTION",approval,parent);
  }
  Task<object> Preview(DeviceAttendanceErases.Input i,Guid? who=null)=>Use<DeviceAttendanceErases,object>(x=>x.PreviewAsync(who??actor,i));
  async Task<Guid> Propose(DeviceAttendanceErases.Input i)
  {
   var p=JsonSerializer.SerializeToElement(await Preview(i));var confirm=new DeviceAttendanceErases.Confirm(i,p.GetProperty("previewHash").GetString()!,i.RecoveryOfId is null?DeviceAttendanceErases.Confirmation:DeviceAttendanceErases.RecoveryConfirmation);
   var id=await Use<DeviceAttendanceErases,Guid>(x=>x.ProposeAsync(actor,confirm));check(await Use<DeviceAttendanceErases,Guid>(x=>x.ProposeAsync(actor,confirm))==id,"exact erase/recovery retry returns existing intent without dispatch duplication");return id;
  }
  Task<byte[]> Version(Guid id)=>Q(db=>db.DeviceAttendanceErases.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  async Task<Guid> Approve(Guid who,Guid id,bool recovery=false)=>await Use<DeviceAttendanceErases,Guid>(async x=>await x.ApproveAsync(who,id,new(await Version(id),"SYNTHETIC-INDEPENDENT-REVIEW",recovery?DeviceAttendanceErases.RecoveryConfirmation:DeviceAttendanceErases.Confirmation)));
  var intent=await Input();check(await Reject<DomainConflictException>(()=>Preview(intent)),"destructive delete-all is disabled despite valid browser approval");
  config["IO:AttendanceErase:EnableDeleteAll"]="true";Configure("IO:AttendanceErase:Approvals:0",JsonSerializer.SerializeToElement(approval));
  check(await Reject<UnauthorizedAccessException>(()=>Preview(intent,reader)),"metadata/reviewer authority does not grant delete proposal or archive device scope");
  check(await Reject<DomainConflictException>(()=>Preview(intent with{QuiescedAtUtc=DateTimeOffset.UtcNow})),"archive made before settled physical capture stop cannot authorize deletion");
  check(await Reject<DomainConflictException>(()=>Preview(intent with{ArchiveVersion=new byte[8]})),"stale archive version is rejected before erase proposal");
  var id=await Propose(intent);
  check(await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(x=>x.RequestAsync(actor,Guid.NewGuid(),device,version,30))),"proposed destructive intent already reserves native endpoint");
  check(await Reject<DomainConflictException>(()=>Approve(actor,id)),"proposer with approve grant cannot countersign own destructive operation");
  check((JsonSerializer.SerializeToElement(await Use<DeviceAttendanceErases,object>(x=>x.ListAsync(reader)))).GetProperty("items").GetArrayLength()==0,"restricted reader cannot see out-of-scope destructive metadata");
  await Use<LoginIdentityDbContext,bool>(async db=>{db.DeviceOperatorGrants.Add(new(){Id=Guid.NewGuid(),UserId=reader,DeviceId=device,GrantedAtUtc=DateTimeOffset.UtcNow});await db.SaveChangesAsync();return true;});
  await Approve(reader,id);
  var host=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..","dotnet.exe"));var relay=Path.Combine(AppContext.BaseDirectory,"virtual-relay","IO.DeviceRuntime.Tests.dll");
  var configuration=new WorkerConfiguration(service,[],[new(device,"192.0.2.41",4370,7,approval.Device.Serial,true,AllowAttendanceRead:true,AttendanceEraseApprovals:[approval])],host,relay,false,false,false,false,false,false);
  using(var worker=new AttendanceEraseWorker(provider.GetRequiredService<IServiceScopeFactory>(),configuration))
  {
   await worker.StartAsync(CancellationToken.None);try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));string state;do{await Task.Delay(100,deadline.Token);state=await Q(db=>db.DeviceAttendanceErases.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync());}while(state is "pending" or "running");check(state=="deleted-verified","actual Windows erase worker claims approved archive, rechecks authority/credential/pin and commits correlated synthetic-child result");}finally{await worker.StopAsync(CancellationToken.None);}
  }
  check(await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>x.DownloadId==intent.ArchiveId))==2,"delete command never removes durable SDK archive or silently ingests unknown check codes");
  intent=await Input();id=await Propose(intent);await Approve(reader,id);
  var claim=await Use<DeviceAttendanceErases,DeviceAttendanceErases.Claim?>(x=>x.ClaimAsync(service))??throw new InvalidOperationException("Erase claim required.");
  await Use<DeviceAttendanceErases,Guid>(async x=>await x.CancelAsync(actor,id,await Version(id)));
  check(await Use<DeviceAttendanceErases,bool>(x=>x.ShouldStopAsync(service,claim)),"running erase cancellation revokes authority rather than proving device cancellation");
  await Use<DeviceAttendanceErases,Guid>(x=>x.CompleteAsync(service,claim,new(id,"deleted-verified")));
  check(await Q(db=>db.DeviceAttendanceErases.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync())=="unknown","late copied/cleared ACK cannot win cancellation race and release destructive fence");
  check(await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(x=>x.RequestAsync(actor,Guid.NewGuid(),device,version,"clock.set",30,DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),"SET-CLOCK"))),"unknown attendance deletion blocks unrelated native writes");
  var reset=DateTimeOffset.UtcNow;await Task.Delay(TimeSpan.FromSeconds(11));var recovery=await Input(id,reset);
  check(await Reject<DomainConflictException>(()=>Preview(recovery)),"unknown erase recovery requires its independent default-off switch");config["IO:AttendanceErase:EnableRecovery"]="true";
  var recoveryId=await Propose(recovery);check(await Reject<DomainConflictException>(()=>Approve(actor,recoveryId,true)),"destructive recovery also forbids self-signature");await Approve(reader,recoveryId,true);
  check(await Q(db=>db.DeviceAttendanceErases.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync())=="unknown","post-reset read-only archive and independent recovery preserve original unknown forever");
  bool rejected=false;try{await Q(async db=>{await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.DeviceAttendanceErases WHERE Id={recoveryId}");return true;});}catch(SqlException e)when(e.Number==51043){rejected=true;}check(rejected,"SQL independently prevents deletion of destructive recovery evidence");
  rejected=false;try{await Q(async db=>{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceAttendanceErases SET State=N'pending',ClaimId=NULL,ServiceUserId=NULL,ClaimedAtUtc=NULL,CancelRequestedAtUtc=NULL,FinishedAtUtc=NULL WHERE Id={id}");return true;});}catch(SqlException e)when(e.Number==51043){rejected=true;}check(rejected,"SQL independently refuses unknown destructive requeue");
  var after=await Input();var afterId=await Propose(after);await Use<DeviceAttendanceErases,Guid>(async x=>await x.CancelAsync(actor,afterId,await Version(afterId)));
  check(await Q(db=>db.DeviceAttendanceErases.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync())=="unknown","only a new explicit preview/proposal can follow recovery, never replay original erase");
  check(!PermissionCatalog.Templates.Any(t=>t.Permissions.Any(p=>p.StartsWith("devices.attendance.erase.",StringComparison.Ordinal))),"no default role gains destructive erase or recovery authority");
 }
}
