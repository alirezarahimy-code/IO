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
internal static class LegacyPollScheduleTests
{
 public static async Task RunAsync(string connection,Action<bool,string> check)
 {
  var clock=new Clock();var services=new ServiceCollection();services.AddLogging();services.AddIoIdentityStorage(connection);services.AddSingleton<TimeProvider>(clock);await using var provider=services.BuildServiceProvider();
  async Task<T> Use<S,T>(Func<S,Task<T>> f)where S:notnull{await using var scope=provider.CreateAsyncScope();return await f(scope.ServiceProvider.GetRequiredService<S>());}
  Task<T> Q<T>(Func<IoDbContext,Task<T>> f)=>Use<IoDbContext,T>(f);
  async Task<bool> Reject<E>(Func<Task> f)where E:Exception{try{await f();return false;}catch(E){return true;}}
  var actor=Guid.NewGuid();var service=Guid.NewGuid();var reader=Guid.NewGuid();var role=Guid.NewGuid();var org=Guid.NewGuid();var company=Guid.NewGuid();var site=Guid.NewGuid();var employee=Guid.NewGuid();var device=Guid.NewGuid();
  await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync();db.Organizations.Add(new(){Id=org,Name="Legacy polling lab"});db.Companies.Add(new(){Id=company,OrganizationId=org,Name="Legacy polling company"});db.Sites.Add(new(){Id=site,CompanyId=company,Name="Legacy polling UTC",TimeZoneId="UTC"});db.Employees.Add(new(){Id=employee,CompanyId=company,PersonnelNumber="1",DisplayName="Legacy poll person"});
   foreach(var(u,r,n,rights)in new[]{(actor,role,"legacy-poll-owner",new[]{DeviceAccess.Manage,DeviceRegistry.Create,DevicePollSchedules.Read,DevicePollSchedules.Manage,DeviceAttendanceDownloads.Read,DeviceAttendanceDownloads.Download,DeviceAttendanceDownloads.Commit,"attendance.events.receive"}),(service,Guid.NewGuid(),"legacy-poll-service",new[]{DevicePollSchedules.Execute,DeviceAttendanceDownloads.Execute}),(reader,Guid.NewGuid(),"legacy-poll-reader",new[]{DevicePollSchedules.Read})}){
    db.Users.Add(new(){Id=u,UserName=n,IsActive=true});db.Roles.Add(new(){Id=r,Name=n});db.UserRoles.Add(new(){UserId=u,RoleId=r});foreach(var code in rights){var p=db.Permissions.Local.FirstOrDefault(x=>x.Code==code)??await db.Permissions.SingleAsync(x=>x.Code==code);db.RolePermissions.Add(new(){RoleId=r,PermissionId=p.Id});}}
   await db.SaveChangesAsync();return true;});
  await Use<DeviceRegistry,Guid>(s=>s.CreateAsync(actor,device,new(site,"S10-LEGACY","Legacy polling candidate","reference non-TFT","zk-standalone","127.0.0.1",4370,true)));
  await Use<DeviceAccess,Guid>(s=>s.SetPolicyAsync(actor,service,null,true,"RESTRICT-DEVICE-OPERATOR"));await Use<DeviceAccess,Guid>(s=>s.GrantAsync(actor,Guid.NewGuid(),service,device));
  await Q(async db=>{db.SourceEmployeeMappings.Add(new(){Id=Guid.NewGuid(),SourceId=device,CompanyId=company,SourcePersonId="1",EmployeeId=employee,ValidFromUtc=new(2026,1,1,0,0,0,TimeSpan.Zero)});await db.SaveChangesAsync();return true;});
  var dv=await Q(db=>db.Devices.Where(x=>x.Id==device).Select(x=>x.Version).SingleAsync());var from=clock.Now.AddHours(-4);var input=new DevicePollSchedules.Input(Guid.NewGuid(),device,dv,from,15,3600,60,30,4,5,false,"ENABLE-SCHEDULED-SDK-READ",1800,ZkAttendanceReadFormats.Legacy);
  Task<Guid> Create(DevicePollSchedules.Input x,Guid? user=null)=>Use<DevicePollSchedules,Guid>(s=>s.CreateAsync(user??actor,x));
  Task<DevicePollScheduleRecord> S(Guid id)=>Q(db=>db.DevicePollSchedules.AsNoTracking().SingleAsync(x=>x.Id==id));
  Task<DevicePollSchedules.Step> Step()=>Use<DevicePollSchedules,DevicePollSchedules.Step>(s=>s.StepAsync(service));
  async Task Toggle(Guid id,bool enabled){var record=await S(id);await Use<DevicePollSchedules,Guid>(s=>s.SetEnabledAsync(actor,id,record.Version,enabled,enabled?"RESUME-APPROVED-SCHEDULE":"PAUSE-SCHEDULE"));}
  async Task Finish(Guid id,string state,ZkAttendanceRaw[] rows){var claim=await Use<DeviceAttendanceDownloads,DeviceAttendanceDownloads.Claim?>(s=>s.ClaimAsync(service))??throw new InvalidOperationException();if(claim.Id!=id||claim.ReadFormat!=ZkAttendanceReadFormats.Legacy)throw new InvalidOperationException("Legacy schedule claim lost format");if(rows.Length>0)await Use<DeviceAttendanceDownloads,long>(s=>s.AppendAsync(service,id,claim.Token,0,rows));await Use<DeviceAttendanceDownloads,Guid>(s=>s.CompleteAsync(service,id,claim.Token,state,rows.Length));}
  check(await Reject<ArgumentException>(()=>Create(input with{ReadFormat="auto"}))&&await Reject<ArgumentException>(()=>Create(input with{ReadFormat=ZkAttendanceReadFormats.Legacy+" "}))&&await Reject<UnauthorizedAccessException>(()=>Create(input,reader)),"S10 legacy polling needs exact format plus independent manage and extraction authority");
  var id=await Create(input);check(await Create(input)==id&&await Reject<DomainConflictException>(()=>Create(input with{ReadFormat=ZkAttendanceReadFormats.Ssr})),"S10 legacy schedule intent replays exactly and cannot switch its immutable read format");
  check(await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DevicePollSchedules SET ReadFormat=N'ssr-v1' WHERE Id={id}")))&&await Reject<SqlException>(()=>Q(db=>db.Database.ExecuteSqlInterpolatedAsync($"UPDATE dbo.DevicePollSchedules SET ReadFormat=N'legacy-int32-minute-v1 ' WHERE Id={id}"))),"S10 SQL rejects approved schedule format mutation and padded format strings");
  // Deliberately attempt linking a real, same-target SSR intent to the legacy
  // schedule. All older identity/range checks match; only format differs.
  var wrong=Guid.NewGuid();await Use<DeviceAttendanceDownloads,Guid>(s=>s.RequestAsync(actor,wrong,device,dv,30,from,from.AddHours(1),4,5));bool forged=false;
  try{await Q(async db=>{db.DevicePollRuns.Add(new(){Id=Guid.NewGuid(),ScheduleId=id,DownloadId=wrong,FromUtc=from,UntilUtcExclusive=from.AddHours(1),CreatedAtUtc=clock.Now});await db.SaveChangesAsync();return true;});}catch(DbUpdateException e)when(e.InnerException is SqlException sql&&sql.Number==51032){forged=true;}
  check(forged&&!await Q(db=>db.DevicePollRuns.AnyAsync(x=>x.DownloadId==wrong)),"S10 SQL run linkage refuses an otherwise matching SSR extraction under a legacy approval");
  var wrongVersion=await Q(db=>db.DeviceAttendanceDownloads.Where(x=>x.Id==wrong).Select(x=>x.Version).SingleAsync());await Use<DeviceAttendanceDownloads,Guid>(s=>s.CancelAsync(actor,wrong,wrongVersion));
  var step=await Step();var download=step.DownloadId!.Value;
  check(step.State=="queued"&&await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==download&&x.ReadFormat==ZkAttendanceReadFormats.Legacy&&x.FromUtc==from&&x.UntilUtcExclusive==from.AddHours(1)))&&(await S(id)).CursorUtc==from,"S10 tick carries approved non-TFT format into a real immutable extraction without advancing cursor");
  var row=new ZkAttendanceRaw("1",1,4,2026,11,8,8,45,0,0,7,8);ZkAttendanceRaw[] rows=[row,row,row with{PersonIdentifier="-1"}];await Finish(download,"succeeded",rows);clock.Advance(11);await Step();
  check((await S(id)).CursorUtc==from.AddHours(1)&&await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device))==0,"S10 read-only legacy schedule acknowledges complete acquisition but never grants automatic raw ingestion");
  await Toggle(id,false);await Toggle(id,true);check((await S(id)).ReadFormat==ZkAttendanceReadFormats.Legacy,"S10 versioned pause resume preserves approved non-TFT format and acknowledged cursor");await Toggle(id,false);
  input=input with{Id=Guid.NewGuid(),AutoCommit=true,Confirmation="ENABLE-AUTOMATIC-SDK-INGEST"};id=await Create(input);step=await Step();download=step.DownloadId!.Value;await Finish(download,"succeeded",rows);clock.Advance(11);await Step();
  check((await S(id)).CursorUtc==from.AddHours(1)&&await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device&&x.ExternalEventId!.StartsWith("zk-legacy-int32-minute-v1:")))==2&&await Q(db=>db.DeviceAttendanceRows.AnyAsync(x=>x.DownloadId==download&&x.Status=="invalid-legacy-identity"&&x.RawEventId==null)),"S10 approved legacy automatic review retains multiplicity and negative-identity quarantine before acknowledging the acquired window");
  clock.Advance(16);step=await Step();download=step.DownloadId!.Value;var cursor=(await S(id)).CursorUtc;await Finish(download,"incomplete",[row]);clock.Advance(11);await Step();
  check((await S(id)).CursorUtc==cursor&&(await S(id)).LastOutcome=="incomplete"&&await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>x.DownloadId==download))==1,"S10 interrupted legacy polling retains partial original tuples without cursor or raw progress");
  clock.Advance(31);step=await Step();download=step.DownloadId!.Value;
  check(await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==download&&x.ReadFormat==ZkAttendanceReadFormats.Legacy&&x.FromUtc==from.AddMinutes(30)&&x.UntilUtcExclusive==from.AddHours(2)))&&(await S(id)).CursorUtc==cursor,"S10 durable backoff rereads the same approved legacy overlap window without format fallback or cursor rewind");
  await Finish(download,"succeeded",[row,row,row with{Minute=55,CheckType=5},row with{Hour=9,Minute=30}]);clock.Advance(11);await Step();
  check((await S(id)).CursorUtc==from.AddHours(2)&&await Q(db=>db.RawAttendanceEvents.CountAsync(x=>x.SourceId==device))==4&&await Q(db=>db.DeviceAttendanceRows.CountAsync(x=>x.DownloadId==download&&x.Status=="already-recorded"))==2,"S10 overlapped legacy recovery reuses original multiplicity receipts and ingests late and forward minute tuples only once");
  var history=JsonSerializer.SerializeToElement(await Use<DevicePollSchedules,object>(s=>s.RunsAsync(reader,id,0)));check(history.GetProperty("items").GetArrayLength()==3&&history.GetProperty("items").EnumerateArray().All(x=>x.GetProperty("ReadFormat").GetString()==ZkAttendanceReadFormats.Legacy),"S10 run history exposes each extraction read format under its independent read permission");
  clock.Advance(16);step=await Step();download=step.DownloadId!.Value;await Q(db=>db.RolePermissions.Where(x=>x.RoleId==role&&db.Permissions.Any(p=>p.Id==x.PermissionId&&p.Code==DevicePollSchedules.Manage)).ExecuteDeleteAsync());clock.Advance(11);step=await Step();
  check(step.State=="revoked"&&!(await S(id)).Enabled&&(await S(id)).CursorUtc==from.AddHours(2)&&await Q(db=>db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==download&&x.State=="cancelled")),"S10 current creator authority revocation stops legacy automation and cancels pending native dispatch without fake progress");
  bool down=false;try{await Q(async db=>{await db.GetService<IMigrator>().MigrateAsync("20261113110000_DeviceLegacyAttendanceReads");return true;});}catch(SqlException e)when(e.Number==51032){down=true;}
  check(down&&(await S(id)).ReadFormat==ZkAttendanceReadFormats.Legacy,"S10 retained legacy schedule approval prevents downgrade even after polling is stopped");
 }
 private sealed class Clock:TimeProvider{public DateTimeOffset Now{get;private set;}=new(2026,11,8,12,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>Now;public void Advance(int seconds)=>Now=Now.AddSeconds(seconds);}
}
