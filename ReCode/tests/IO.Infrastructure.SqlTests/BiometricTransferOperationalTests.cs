using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
internal static class BiometricTransferOperationalTests
{
 internal static async Task RunAsync(ServiceProvider provider,ConfigurationManager config,Guid actor,Guid service,Guid reader,Guid source,byte[] sourceVersion,Guid employee,Guid site,Guid company,Action<bool,string> check)
 {
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  foreach(var (user,codes) in new[]{(actor,new[]{DeviceBiometricTransfers.Transfer,DeviceBiometricTransfers.ReadMaterial,DeviceBiometricTransfers.Read}),(service,new[]{DeviceBiometricTransfers.Execute}),(reader,new[]{DeviceBiometricTransfers.Read})})
  {
   await Q(async db=>{var role=await db.UserRoles.Where(x=>x.UserId==user).Select(x=>x.RoleId).SingleAsync();foreach(var code in codes){var p=await db.Permissions.SingleOrDefaultAsync(x=>x.Code==code);if(p is null){p=new(){Id=Guid.NewGuid(),Code=code};db.Permissions.Add(p);}db.RolePermissions.Add(new(){RoleId=role,PermissionId=p.Id});}await db.SaveChangesAsync();return true;});
  }
  var target=Guid.NewGuid();await Use<DeviceRegistry,Guid>(x=>x.CreateAsync(actor,target,new(site,"SYNTHETIC-BIO-TARGET","Synthetic biometric target","unverified","zk-standalone","192.0.2.32",4370,true)));
  var targetVersion=await Q(db=>db.Devices.Where(x=>x.Id==target).Select(x=>x.Version).SingleAsync());
  await Q(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=target,EmployeeId=employee,CompanyId=company,SourcePersonId="0002",ValidFromUtc=DateTimeOffset.UtcNow.AddDays(-1)});await db.SaveChangesAsync();return true;});
  var approval=new BiometricTransferApproval(new("SYNTHETIC-SERIAL","SYNTHETIC-PRODUCT","SYNTHETIC-FIRMWARE"),new("SYNTHETIC-BIO-TARGET","SYNTHETIC-PRODUCT","SYNTHETIC-FIRMWARE"),"fingerprint",1,"SYNTHETIC-FORMAT","SYNTHETIC-AUTH","SYNTHETIC-RETENTION","SYNTHETIC-EXCLUSIVE",-404,-404,AllowTransfer:true,AllowTransientUserEnumeration:true);
  var intent=new DeviceBiometricTransfers.Input(Guid.NewGuid(),source,target,employee,sourceVersion,targetVersion,"001","0002",approval,30);
  Task<object> Preview(DeviceBiometricTransfers.Input i,Guid? who=null)=>Use<DeviceBiometricTransfers,object>(x=>x.PreviewAsync(who??actor,i));
  Task<Guid> Request(DeviceBiometricTransfers.Confirm i)=>Use<DeviceBiometricTransfers,Guid>(x=>x.RequestAsync(actor,i));
  check(await Reject<DomainConflictException>(()=>Preview(intent)),"biometric transfer is disabled by default despite valid browser policy and rights");
  config["IO:Biometrics:EnableTransfers"]="true";
  void Configure(string prefix,JsonElement value){foreach(var p in value.EnumerateObject()){if(p.Value.ValueKind==JsonValueKind.Object)Configure(prefix+":"+p.Name,p.Value);else config[prefix+":"+p.Name]=p.Value.ToString();}}
  Configure("IO:Biometrics:Approvals:0",JsonSerializer.SerializeToElement(approval));
  check(await Reject<UnauthorizedAccessException>(()=>Preview(intent,reader)),"metadata job read authority does not authorize template reads or transfers");
  check(await Reject<DomainConflictException>(()=>Preview(intent with{SourcePerson="1"})),"biometric source mapping never aliases leading-zero subject 001 to 1");
  var preview=JsonSerializer.SerializeToElement(await Preview(intent));var confirm=new DeviceBiometricTransfers.Confirm(intent,preview.GetProperty("previewHash").GetString()!,DeviceBiometricTransfers.Confirmation);
  check(await Reject<DomainConflictException>(()=>Request(confirm with{PreviewHash=new string('A',64)})),"biometric intent requires exact server pair/mapping preview");
  check(await Request(confirm)==intent.Id&&await Request(confirm)==intent.Id,"biometric intent exact retry does not duplicate queue or replay material");
  check(await Reject<DomainConflictException>(()=>Request(confirm with{Intent=intent with{TargetPerson="2"}})),"changed biometric intent retry is rejected");
  check(await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(x=>x.RequestAsync(actor,Guid.NewGuid(),source,sourceVersion,30)))&&await Reject<DomainConflictException>(()=>Use<DeviceConnections,Guid>(x=>x.RequestAsync(actor,Guid.NewGuid(),target,targetVersion,30))),"pending biometric transfer reserves both source and target against other native sessions");
  // Pair privacy uses AND, while the internal existence-only safety fence must still protect a partially visible endpoint.
  await Use<LoginIdentityDbContext,bool>(async db=>{db.DeviceOperatorPolicies.Add(new(){UserId=reader,Restricted=true,ChangedAtUtc=DateTimeOffset.UtcNow});db.DeviceOperatorGrants.Add(new(){Id=Guid.NewGuid(),UserId=reader,DeviceId=source,GrantedAtUtc=DateTimeOffset.UtcNow});await db.SaveChangesAsync();return true;});
  var hidden=JsonSerializer.SerializeToElement(await Use<DeviceBiometricTransfers,object>(x=>x.ListAsync(reader,0)));check(hidden.GetProperty("items").GetArrayLength()==0,"operator restricted to only one endpoint cannot view paired biometric subject metadata");
  var version=await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==intent.Id).Select(x=>x.Version).SingleAsync());await Use<DeviceBiometricTransfers,Guid>(x=>x.CancelAsync(actor,intent.Id,version));
  check(await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==intent.Id).Select(x=>x.State).SingleAsync())=="cancelled-before-dispatch","pending biometric cancellation does not start SDK or read any template");
  async Task<Guid> NewIntent()
  {
   intent=intent with{Id=Guid.NewGuid()};var p=JsonSerializer.SerializeToElement(await Preview(intent));return await Request(new(intent,p.GetProperty("previewHash").GetString()!,DeviceBiometricTransfers.Confirmation));
  }
  await NewIntent();
  var executable=Path.GetFullPath(Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(),"..","..","..","dotnet.exe"));var relay=Path.Combine(AppContext.BaseDirectory,"virtual-relay","IO.DeviceRuntime.Tests.dll");
  var configuration=new WorkerConfiguration(service,[],[new(source,"192.0.2.31",4370,1,approval.Source.Serial,true,BiometricTransferApprovals:[approval]),new(target,"192.0.2.32",4370,1,approval.Target.Serial,true,BiometricTransferApprovals:[approval])],executable,relay,false,false,false,false,false,false);
  using(var worker=new BiometricTransferWorker(provider.GetRequiredService<IServiceScopeFactory>(),configuration))
  {
   await worker.StartAsync(CancellationToken.None);
   try{using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(20));string state;do{await Task.Delay(100,deadline.Token);state=await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==intent.Id).Select(x=>x.State).SingleAsync());}while(state is "pending" or "running");check(state=="copied","actual Windows biometric worker claims SQL pair rechecks credentials and commits strictly correlated synthetic-child result");}
   finally{await worker.StopAsync(CancellationToken.None);}
  }
  await NewIntent();var claim=await Use<DeviceBiometricTransfers,DeviceBiometricTransfers.Claim?>(x=>x.ClaimAsync(service))??throw new InvalidOperationException("Expected biometric claim.");
  check(await Use<DeviceBiometricTransfers,DeviceBiometricTransfers.Keys?>(x=>x.CredentialAsync(service,claim,true,true)) is not null,"biometric credential pair is available only to an owned active service claim");
  check(await Reject<ArgumentException>(()=>Use<DeviceBiometricTransfers,Guid>(x=>x.CompleteAsync(service,claim,new(Guid.NewGuid(),"copied")))),"foreign metadata completion cannot finish a biometric claim");
  version=await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==intent.Id).Select(x=>x.Version).SingleAsync());await Use<DeviceBiometricTransfers,Guid>(x=>x.CancelAsync(actor,intent.Id,version));
  check(await Use<DeviceBiometricTransfers,bool>(x=>x.ShouldStopAsync(service,claim)),"post-dispatch biometric cancel revokes authority instead of proving cancellation");
  await Use<DeviceBiometricTransfers,Guid>(x=>x.CompleteAsync(service,claim,new(claim.Id,"copied")));
  check(await Q(db=>db.DeviceBiometricTransfers.Where(x=>x.Id==intent.Id).Select(x=>x.State).SingleAsync())=="unknown","cancel/completion race cannot manufacture copied result");
  check(await Reject<DomainConflictException>(()=>Preview(intent with{Id=Guid.NewGuid()})),"unknown biometric target write cannot be retried or overwritten by a new transfer");
  check(await Reject<DomainConflictException>(()=>Use<DeviceOperations,Guid>(x=>x.RequestAsync(actor,Guid.NewGuid(),target,targetVersion,"clock.set",30,DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),"SET-CLOCK"))),"unknown biometric write fences unrelated native writes on target");
  bool sqlRejected=false;try{await Q(async db=>{await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DeviceBiometricTransfers SET State=N'pending',ClaimId=NULL,ServiceUserId=NULL,ClaimedAtUtc=NULL,CancelRequestedAtUtc=NULL,FinishedAtUtc=NULL WHERE Id={intent.Id}");return true;});}catch(SqlException e)when(e.Number==51041){sqlRejected=true;}check(sqlRejected,"SQL independently prevents unknown biometric requeue or terminal history mutation");
  await BiometricRecoveryOperationalTests.RunAsync(provider,config,actor,service,reader,intent,configuration,check);
  check(!PermissionCatalog.Templates.Any(x=>x.Permissions.Any(p=>p.StartsWith("devices.biometrics.",StringComparison.Ordinal))),"no default role gains template access or service transfer authority");
  check(await Q(db=>db.RawAttendanceEvents.CountAsync())==2,"biometric exchange creates no attendance or biometric payload in raw event stream");
 }
}
