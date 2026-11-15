using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
internal static class BiometricRecoveryOperationalTests
{
 internal static async Task RunAsync(ServiceProvider provider,ConfigurationManager config,Guid actor,Guid service,Guid reader,DeviceBiometricTransfers.Input original,WorkerConfiguration workerConfig,Action<bool,string> check)
 {
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  void Configure(string prefix,JsonElement value){foreach(var p in value.EnumerateObject()){if(p.Value.ValueKind==JsonValueKind.Object)Configure(prefix+":"+p.Name,p.Value);else if(p.Value.ValueKind!=JsonValueKind.Null)config[prefix+":"+p.Name]=p.Value.ToString();}}
  foreach(var (user,codes) in new[]{(actor,new[]{DeviceBiometricTransfers.Inspect,DeviceBiometricRecovery.Propose,DeviceBiometricRecovery.Approve}),(reader,new[]{DeviceBiometricRecovery.Approve})})
   await Q(async db=>{var role=await db.UserRoles.Where(x=>x.UserId==user).Select(x=>x.RoleId).SingleAsync();foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=role,PermissionId=p.Id});}await db.SaveChangesAsync();return true;});
  var approval=new BiometricRecoveryApproval(original.Approval,"SYNTHETIC-NON-DESTRUCTIVE-RESET",10,true,true);
  var version=await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==original.Id).Select(x=>x.Version).SingleAsync());
  var proposal=new DeviceBiometricRecovery.Input(Guid.NewGuid(),original.Id,Guid.NewGuid(),version,approval,DateTimeOffset.UtcNow,"SYNTHETIC-PROCESS-STOPPED","SYNTHETIC-RESET",DeviceBiometricRecovery.Confirmation);
  Task<Guid> Propose(DeviceBiometricRecovery.Input i)=>Use<DeviceBiometricRecovery,Guid>(x=>x.ProposeAsync(actor,i));
  Task<Guid> Review(Guid who,Guid id,byte[] v)=>Use<DeviceBiometricRecovery,Guid>(x=>x.ApproveAsync(who,id,new(v,"SYNTHETIC-INDEPENDENT-REVIEW",DeviceBiometricRecovery.Confirmation)));
  check(await Reject<DomainConflictException>(()=>Propose(proposal)),"biometric assisted recovery remains independently disabled by default");
  config["IO:Biometrics:EnableRecovery"]="true";Configure("IO:Biometrics:RecoveryApprovals:0",JsonSerializer.SerializeToElement(approval));
  check(await Reject<DomainConflictException>(()=>Propose(proposal)),"physical reset settling interval cannot be bypassed by an immediate proposal");
  await Task.Delay(TimeSpan.FromSeconds(11));
  check(await Propose(proposal)==proposal.InspectionId&&await Propose(proposal)==proposal.InspectionId,"exact proposal retry queues one dedicated read-only inspection only");
  check(await Reject<DomainConflictException>(()=>Propose(proposal with{ResetEvidence="CHANGED"})),"changed assisted recovery retry cannot repoint evidence");
  var recoveryVersion=await Q(db=>db.DeviceBiometricRecoveries.Where(x=>x.Id==proposal.Id).Select(x=>x.Version).SingleAsync());
  check(await Reject<DomainConflictException>(()=>Review(actor,proposal.Id,recoveryVersion)),"proposer with both grants cannot self-countersign biometric recovery");
  check(await Reject<ArgumentException>(()=>Use<DeviceBiometricRecovery,object>(x=>x.ListAsync(reader,original.Id))),"operator seeing only source cannot list paired recovery references");
  await Use<LoginIdentityDbContext,bool>(async db=>{db.DeviceOperatorGrants.Add(new(){Id=Guid.NewGuid(),UserId=reader,DeviceId=original.TargetDeviceId,GrantedAtUtc=DateTimeOffset.UtcNow});await db.SaveChangesAsync();return true;});
  check(await Reject<DomainConflictException>(()=>Review(reader,proposal.Id,recoveryVersion)),"pending read-only inspection cannot release unknown write fence");
  var claim=await Use<DeviceBiometricTransfers,DeviceBiometricTransfers.Claim?>(x=>x.ClaimAsync(service))??throw new InvalidOperationException("Expected inspection claim.");
  check(claim.Intent.InspectionOfId==original.Id,"inspection claim pins original unknown and both endpoints");
  check(await Reject<ArgumentException>(()=>Use<DeviceBiometricTransfers,Guid>(x=>x.CompleteAsync(service,claim,new(claim.Id,"copied")))),"inspection SQL completion API rejects copy-success result");
  await Use<DeviceBiometricTransfers,Guid>(x=>x.CompleteAsync(service,claim,new(claim.Id,"inspection-failed")));
  check(await Reject<DomainConflictException>(()=>Review(reader,proposal.Id,recoveryVersion)),"failed inspection plus human assertions cannot unlock target");
  proposal=proposal with{Id=Guid.NewGuid(),InspectionId=Guid.NewGuid()};await Propose(proposal);
  using(var worker=new BiometricTransferWorker(provider.GetRequiredService<IServiceScopeFactory>(),workerConfig))
  {
   await worker.StartAsync(CancellationToken.None);
   try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));string state;do{await Task.Delay(100,deadline.Token);state=await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==proposal.InspectionId).Select(x=>x.State).SingleAsync());}while(state is "pending" or "running");check(state=="observed-equal","actual Windows worker executes distinct inspection wire and durably stores observation only");}
   finally{await worker.StopAsync(CancellationToken.None);}
  }
  recoveryVersion=await Q(db=>db.DeviceBiometricRecoveries.Where(x=>x.Id==proposal.Id).Select(x=>x.Version).SingleAsync());
  config["IO:Biometrics:EnableRecovery"]="false";check(await Reject<DomainConflictException>(()=>Review(reader,proposal.Id,recoveryVersion)),"deployment recovery revocation is rechecked at independent approval");config["IO:Biometrics:EnableRecovery"]="true";
  await Review(reader,proposal.Id,recoveryVersion);
  check(await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==original.Id).Select(x=>x.State=="unknown").SingleAsync()),"countersigned recovery preserves original unknown forever, never manufactures copied result");
  check(await Reject<DomainConflictException>(()=>Review(reader,proposal.Id,recoveryVersion)),"countersignature is one-way and cannot be replayed");
  bool sqlRejected=false;try{await Q(async db=>{await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM dbo.DeviceBiometricRecoveries WHERE Id={proposal.Id}");return true;});}catch(SqlException e)when(e.Number==51042){sqlRejected=true;}check(sqlRejected,"SQL independently rejects deletion of countersigned biometric recovery history");
  var next=original with{Id=Guid.NewGuid()};var preview=JsonSerializer.SerializeToElement(await Use<DeviceBiometricTransfers,object>(x=>x.PreviewAsync(actor,next)));
  await Use<DeviceBiometricTransfers,Guid>(x=>x.RequestAsync(actor,new(next,preview.GetProperty("previewHash").GetString()!,DeviceBiometricTransfers.Confirmation)));
  var pendingVersion=await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==next.Id).Select(x=>x.Version).SingleAsync());await Use<DeviceBiometricTransfers,Guid>(x=>x.CancelAsync(actor,next.Id,pendingVersion));
  check(await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==original.Id).Select(x=>x.State).SingleAsync())=="unknown","released fence permits new explicit intent only, never requeues original transfer");
  var photo=original.Approval with{Kind="photo",Slot=0,FingerprintFlag=0,PhotoHandlingReference="SYNTHETIC-STORAGE"};Configure("IO:Biometrics:Approvals:1",JsonSerializer.SerializeToElement(photo));
  next=original with{Id=Guid.NewGuid(),Approval=photo};preview=JsonSerializer.SerializeToElement(await Use<DeviceBiometricTransfers,object>(x=>x.PreviewAsync(actor,next)));
  await Use<DeviceBiometricTransfers,Guid>(x=>x.RequestAsync(actor,new(next,preview.GetProperty("previewHash").GetString()!,DeviceBiometricTransfers.Confirmation)));
  var root=Environment.GetEnvironmentVariable("IO_SYNTHETIC_PHOTO_ROOT")??throw new InvalidOperationException("CI staging required.");
  var pc=workerConfig with{NativeEndpoints=workerConfig.NativeEndpoints.Select(x=>x with{BiometricTransferApprovals=[original.Approval,photo]}).ToArray(),PhotoStaging=new(root,"SYNTHETIC-STORAGE",268435456,true)};
  using(var worker=new BiometricTransferWorker(provider.GetRequiredService<IServiceScopeFactory>(),pc))
  {
   await worker.StartAsync(CancellationToken.None);
   try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));string state;do{await Task.Delay(100,deadline.Token);state=await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==next.Id).Select(x=>x.State).SingleAsync());}while(state is "pending" or "running");check(state=="copied"&&!Directory.EnumerateDirectories(root).Any(),"actual photo worker supplies approved private EFS lease to synthetic child and cleans staging before durable completion");}
   finally{await worker.StopAsync(CancellationToken.None);}
  }
 }
}
