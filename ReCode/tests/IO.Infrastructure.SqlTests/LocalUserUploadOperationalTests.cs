using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Devices;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

internal static class LocalUserUploadOperationalTests
{
 internal static async Task RunAsync(ServiceProvider provider,Guid actor,Guid service,Guid reader,Guid device,byte[] version,Guid initialSnapshot,Action<bool,string> check)
 {
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var company=await Q(db=>db.DeviceUserDownloads.Where(x=>x.Id==initialSnapshot).Select(x=>x.CompanyId).SingleAsync());
  var employee=Guid.NewGuid();var mapping=Guid.NewGuid();
  await Q(async db=>{db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber=employee.ToString("N"),DisplayName="SYNTHETIC-UPDATED"});db.SourceEmployeeMappings.Add(new(){Id=mapping,SourceId=device,CompanyId=company,SourcePersonId="001",EmployeeId=employee,ValidFromUtc=DateTimeOffset.UtcNow.AddDays(-1)});await db.SaveChangesAsync();return true;});
  var original=LocalExchangeFixtureStore.Read(device);var protectedUser=original.Users[0];var admin=JsonSerializer.Serialize(original.Users[1]);
  DeviceUserUploads.Batch Batch(Guid snapshot,string card)=>new(Guid.NewGuid(),[new(Guid.NewGuid(),device,version,snapshot,employee,"001",card,true,20)],"UPLOAD-ORDINARY-USERS");
  async Task<DeviceUserUploads.Batch> Approve(DeviceUserUploads.Batch b)
  {var plan=JsonSerializer.SerializeToElement(await Use<DeviceUserUploads,object>(s=>s.PreviewAsync(actor,b)));return b with{PreviewHash=plan.GetProperty("previewHash").GetString()};}
  Task<Guid> Request(DeviceUserUploads.Batch b)=>Use<DeviceUserUploads,Guid>(s=>s.RequestAsync(actor,b));
  async Task Wait(Func<Task<string>> read,string expected)
  {
   using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30));string state;
   do{await Task.Delay(75,deadline.Token);state=await read();}while(state is "pending" or "running");
   check(state==expected,"ordinary direct upload actual SQL/worker/production mapper outcome "+expected);
  }
  Task WaitUpload(Guid id,string expected)=>Wait(()=>Q(db=>db.DeviceUserUploads.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync()),expected);
  async Task<Guid> Snapshot()
  {
   var id=Guid.NewGuid();await Use<DeviceUserDownloads,Guid>(s=>s.RequestAsync(actor,id,device,version,20));
   await Wait(()=>Q(db=>db.DeviceUserDownloads.Where(x=>x.Id==id).Select(x=>x.State).SingleAsync()),"succeeded");return id;
  }
  Task<byte[]> V(Guid id)=>Q(db=>db.DeviceUserUploads.Where(x=>x.Id==id).Select(x=>x.Version).SingleAsync());
  async Task Rename(string name)=>_ = await Q(db=>db.Employees.Where(x=>x.Id==employee).ExecuteUpdateAsync(s=>s.SetProperty(x=>x.DisplayName,name)));
  void Protected(int writes)
  {
   var state=LocalExchangeFixtureStore.Read(device);var row=state.Users[0];
   check(state.Writes==writes&&row.Password==protectedUser.Password&&row.Group==protectedUser.Group&&row.Fingerprints.SequenceEqual(protectedUser.Fingerprints)&&JsonSerializer.Serialize(state.Users[1])==admin,"ordinary direct upload preserves protected password group fingerprint and unrelated administrator; exact write count "+writes);
  }
  var batch=Batch(initialSnapshot,"333");
  check(await Reject<UnauthorizedAccessException>(()=>Use<DeviceUserUploads,object>(s=>s.PreviewAsync(reader,batch)))&&await Reject<UnauthorizedAccessException>(()=>Use<DeviceUserUploads,Guid>(s=>s.RequestAsync(reader,batch))),"ordinary direct upload cannot bypass reader RBAC at preview or request");
  check(await Reject<DomainConflictException>(()=>Request(batch)),"ordinary direct upload requires exact server preview approval");
  batch=await Approve(batch);
  check(await Reject<DomainConflictException>(()=>Request(batch with{Items=[batch.Items[0] with{Card="444"}]})),"ordinary direct upload rejects altered card after preview");
  await Request(batch);await WaitUpload(batch.Items[0].Id,"succeeded");Protected(1);
  check(await Request(batch)==batch.BatchId&&await Q(db=>db.DeviceUserUploads.CountAsync(x=>x.BatchId==batch.BatchId))==1,"ordinary direct upload exact batch replay retains one immutable intent");
  var fresh=await Snapshot();
  check(await Q(db=>db.DeviceDownloadedUsers.AnyAsync(x=>x.DownloadId==fresh&&x.PersonIdentifier=="001"&&x.Name=="SYNTHETIC-UPDATED"&&x.Card=="333")),"fresh local download observes actual persisted SDK-double mutation, not a fixed canned success");
  await Rename("SYNTHETIC-AFTER-LOSS");var state=LocalExchangeFixtureStore.Read(device);state.DropNextWriteReply=true;LocalExchangeFixtureStore.Save(device,state);
  var lost=await Approve(Batch(fresh,"555"));await Request(lost);await WaitUpload(lost.Items[0].Id,"unknown");Protected(2);
  check(LocalExchangeFixtureStore.Read(device).Users[0].User.Name=="SYNTHETIC-AFTER-LOSS","lost child response after committed SDK write never becomes failed-before-write or automatic replay");
  check(await Reject<DomainConflictException>(()=>Approve(Batch(fresh,"555"))),"unknown direct upload blocks a blind new intent for the same person");
  check(await Reject<DomainConflictException>(()=>Use<DeviceUserUploads,Guid>(async s=>await s.ReviewAsync(actor,lost.Items[0].Id,await V(lost.Items[0].Id),fresh,"REVIEWED-USER"))),"unknown direct upload review rejects the pre-write snapshot");
  var recovery=await Snapshot();
  await Use<DeviceUserUploads,Guid>(async s=>await s.ReviewAsync(actor,lost.Items[0].Id,await V(lost.Items[0].Id),recovery,"REVIEWED-USER"));
  check(await Q(db=>db.DeviceUserUploads.AnyAsync(x=>x.Id==lost.Items[0].Id&&x.State=="unknown"&&x.ResolutionSnapshotId==recovery)),"explicit fresh-snapshot review records evidence without rewriting original unknown as success");
  var retry=await Approve(Batch(recovery,"555"));await Request(retry);await WaitUpload(retry.Items[0].Id,"unchanged");Protected(2);
  check(await Q(db=>db.DeviceUserUploads.CountAsync(x=>x.DeviceId==device))==3,"reviewed selective retry is a new intent while unchanged observed state avoids a second physical write");
  await Rename("SYNTHETIC-REVOKE");state=LocalExchangeFixtureStore.Read(device);state.PauseBeforeWrite=true;LocalExchangeFixtureStore.Save(device,state);
  var revoked=await Approve(Batch(recovery,"666"));await Request(revoked);
  using(var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(30)))while(!LocalExchangeFixtureStore.Ready(device))await Task.Delay(50,deadline.Token);
  await Q(db=>db.SourceEmployeeMappings.Where(x=>x.Id==mapping).ExecuteDeleteAsync());
  await WaitUpload(revoked.Items[0].Id,"unknown");Protected(2);
  check(LocalExchangeFixtureStore.Read(device).Users[0].User.Card=="555","in-flight mapping revocation stops the native child before the delayed write; outcome remains conservative unknown");
  var receipts=await Q(db=>db.DeviceUserUploads.Where(x=>x.DeviceId==device).Select(x=>new{x.ExpectedJson,x.DesiredJson}).ToListAsync());
  var serialized=JsonSerializer.Serialize(receipts);
  check(!serialized.Contains("synthetic-password",StringComparison.Ordinal)&&!serialized.Contains("SYNTHETIC-FINGERPRINT",StringComparison.Ordinal)&&!serialized.Contains("SYNTHETIC-GROUP",StringComparison.Ordinal),"SQL upload receipts never store protected SDK password fingerprint or group payloads");
 }
}
