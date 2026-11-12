using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
internal static class DeviceSafetyRegressionTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);var clock=new Clock();services.AddSingleton<TimeProvider>(clock);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var worker=Guid.NewGuid();var role=Guid.NewGuid();var exec=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var device=Guid.NewGuid();var second=Guid.NewGuid();var employee=Guid.NewGuid();
  await Q(async db=>{
   db.Organizations.Add(new(){Id=org,Name="Parity org"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Parity company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Parity site",TimeZoneId="UTC"});db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="001",DisplayName="Parity person"});
   foreach(var(u,n)in new[]{(actor,"parity-actor"),(worker,"parity-worker")})db.Users.Add(new(){Id=u,UserName=n,IsActive=true});
   foreach(var(r,u,n,codes)in new[]{(role,actor,"parity-human",new[]{DeviceRegistry.Create,DeviceOperations.SetClock,DeviceOperations.Restart,DeviceOperations.Read,DevicePollSchedules.Manage,DeviceAttendanceDownloads.Download,DeviceUserDownloads.Download,DeviceUserUploads.Upload}),(exec,worker,"parity-worker-role",new[]{DeviceOperations.Execute,DeviceOperations.ExecuteRestart,DeviceUserDownloads.Execute,DeviceUserUploads.Execute,DevicePollSchedules.Execute})}){
    db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in codes){var p=await db.Permissions.SingleAsync(x=>x.Code==code);db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;
  });
  var input=new DeviceRegistry.Input(site,"PARITY-A","Parity candidate","unverified","zk-standalone","10.20.30.70",4370,true);
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,input));await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,second,input with{Number="PARITY-B",Host="10.20.30.71"}));
  var dv=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());var sv=await Q(db=>db.Devices.Where(x=>x.Id==second).Select(x=>x.Version).SingleAsync());
  await Q(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=device,CompanyId=company,SourcePersonId="001",EmployeeId=employee,ValidFromUtc=clock.GetUtcNow().AddDays(-1)});await db.SaveChangesAsync();return true;});
  var snapshot=Guid.NewGuid();await Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(actor,snapshot,device,dv,30));var uc=await Use<DeviceUserDownloads,DeviceUserDownloads.Claim?>(s=>s.ClaimAsync(worker))??throw new InvalidOperationException();
  await Use<DeviceUserDownloads,long>(s=>s.AppendAsync(worker,uc.Id,uc.Token,0,[new("001","old","123",0,true)]));await Use<DeviceUserDownloads,Guid>(s=>s.CompleteAsync(worker,uc.Id,uc.Token,"succeeded",1));
  DevicePollSchedules.Input Schedule(Guid target,byte[] v)=>new(Guid.NewGuid(),target,v,new(2026,11,8,0,0,0,TimeSpan.Zero),15,3600,60,30,4,5,false,"ENABLE-SCHEDULED-SDK-READ");
  Task<Guid> Create(DevicePollSchedules.Input x)=>Use<DevicePollSchedules,Guid>(s=>s.CreateAsync(actor,x));
  Task<byte[]> PV(Guid id)=>Q(db=>db.DevicePollSchedules.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  Task<Guid> Toggle(Guid id,bool enabled)=>Use<DevicePollSchedules,Guid>(async s=>await s.SetEnabledAsync(actor,id,await PV(id),enabled,enabled?"RESUME-APPROVED-SCHEDULE":"PAUSE-SCHEDULE"));
  Task<Guid> Write(Guid id,string kind="clock.set")=>Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,id,device,dv,kind,30,kind=="clock.set"?new DateTimeOffset(2026,11,8,10,0,0,TimeSpan.Zero):null,kind=="clock.set"?"SET-CLOCK":"RESTART-DEVICE"));
  var schedule=Schedule(device,dv);await Create(schedule);
  check(await Reject<DomainConflictException>(()=>Write(Guid.NewGuid()))&&await Reject<DomainConflictException>(()=>Write(Guid.NewGuid(),"device.restart")),"parity active polling blocks clock and restart as well as IP and idle mutations");
  await Toggle(schedule.Id,false);var write=Guid.NewGuid();await Write(write);
  check(await Reject<DomainConflictException>(()=>Toggle(schedule.Id,true))&&await Reject<DomainConflictException>(()=>Create(Schedule(device,dv))),"parity pending native mutation blocks both schedule creation and resume");
  var op=await Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker))??throw new InvalidOperationException();await Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,op.Id,op.Token,new("unknown")));
  check(await Reject<DomainConflictException>(()=>Toggle(schedule.Id,true))&&await Reject<DomainConflictException>(()=>Create(Schedule(device,dv))),"parity unresolved native unknown cannot be bypassed by creating or resuming polling");
  var batch=new DeviceUserUploads.Batch(Guid.NewGuid(),[new(Guid.NewGuid(),device,dv,snapshot,employee,"001","456",true,30)],"UPLOAD-ORDINARY-USERS");
  check(await Reject<DomainConflictException>(()=>Use<DeviceUserUploads,object>(s=>s.PreviewAsync(actor,batch))),"parity unknown clock or restart fences user mutation preview, not merely network and idle unknown");
  var other=await Create(Schedule(second,sv));check(await Q(db=>db.DevicePollSchedules.AnyAsync(x=>x.Id==other&&x.Enabled)),"parity one device unknown does not block an independent device schedule");await Toggle(other,false);
  clock.Advance(1);var infoId=Guid.NewGuid();await Use<DeviceOperations,Guid>(s=>s.RequestAsync(actor,infoId,device,dv,"information.read",30,null,null));var read=await Use<DeviceOperations,DeviceOperations.Claim?>(s=>s.ClaimAsync(worker))??throw new InvalidOperationException();
  var information=new ZkDeviceInformation("parity-serial",null,null,null,"2026-11-08 10:00:00",0,null,null,null,null,null,null,null,null,false);await Use<DeviceOperations,Guid>(s=>s.CompleteAsync(worker,read.Id,read.Token,new("succeeded",Information:information)));
  var v=await Q(db=>db.DeviceOperations.Where(x=>x.Id==write).Select(x=>x.Version).SingleAsync());await Use<DeviceOperations,Guid>(s=>s.ResolveClockAsync(actor,write,v,infoId,"REVIEWED-CLOCK"));
  await Toggle(schedule.Id,true);check(await Q(db=>db.DeviceOperations.AnyAsync(x=>x.Id==write&&x.State=="unknown"&&x.ResolvedAtUtc!=null))&&await Q(db=>db.DevicePollSchedules.AnyAsync(x=>x.Id==schedule.Id&&x.Enabled)),"parity fresh authorized recovery read and review releases polling without rewriting the unknown outcome");await Toggle(schedule.Id,false);
  var preview=JsonSerializer.SerializeToElement(await Use<DeviceUserUploads,object>(s=>s.PreviewAsync(actor,batch)));batch=batch with{PreviewHash=preview.GetProperty("previewHash").GetString()};await Use<DeviceUserUploads,Guid>(s=>s.RequestAsync(actor,batch));var upload=await Use<DeviceUserUploads,DeviceUserUploads.Claim?>(s=>s.ClaimAsync(worker))??throw new InvalidOperationException();
  await Q(db=>db.SourceEmployeeMappings.Where(x=>x.SourceId==device).ExecuteUpdateAsync(x=>x.SetProperty(m=>m.ValidUntilUtcExclusive,clock.GetUtcNow())));
  check(await Use<DeviceUserUploads,DeviceCommunicationKey?>(s=>s.CredentialAsync(worker,upload,true))==null,"parity expired mapping after claim denies upload credential dispatch rather than writing an unmapped user");
  await Use<DeviceUserUploads,Guid>(s=>s.CompleteAsync(worker,upload.Id,upload.Token,new("unknown")));
  check(await Reject<DomainConflictException>(()=>Write(Guid.NewGuid()))&&await Reject<DomainConflictException>(()=>Write(Guid.NewGuid(),"device.restart")),"parity unresolved user mutation blocks all subsequent native writes, including clock and restart");
 }
 private sealed class Clock:TimeProvider{private DateTimeOffset now=DateTimeOffset.UtcNow;public override DateTimeOffset GetUtcNow()=>now;public void Advance(int seconds)=>now=now.AddSeconds(seconds);}
}
