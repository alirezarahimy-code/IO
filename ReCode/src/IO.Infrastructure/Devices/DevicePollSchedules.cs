using System.Text.Json;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Devices;

/// <summary>Explicitly approved read-only SDK polling and optional DB ingestion.
/// Never retries native writes and never pretends to implement a vendor PUSH protocol.</summary>
public sealed class DevicePollSchedules(IoDbContext db,PermissionTransaction transaction,TimeProvider clock,DeviceAttendanceDownloads downloads)
{
    public const string Read="devices.schedules.read",Manage="devices.schedules.manage",Execute="devices.schedules.execute";
    public sealed record Input(Guid Id,Guid DeviceId,byte[] ExpectedDeviceVersion,DateTimeOffset StartUtc,int EverySeconds,int WindowSeconds,int LagSeconds,int TimeoutSeconds,int InCode,int OutCode,bool AutoCommit,string Confirmation,int OverlapSeconds=0);
    public sealed record Step(string State,Guid? ScheduleId=null,Guid? RunId=null,Guid? DownloadId=null);
    public Task<Guid> CreateAsync(Guid actor,Input input,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Manage,"device-poll.created",async token=>
    {
        if(input is null||input.Id==Guid.Empty||input.DeviceId==Guid.Empty||input.ExpectedDeviceVersion is not{Length:8}||input.StartUtc.Offset!=TimeSpan.Zero||input.StartUtc.Ticks%TimeSpan.TicksPerSecond!=0||input.EverySeconds is <15 or >86400||input.WindowSeconds is <60 or >86400||input.LagSeconds is <0 or >86400||input.TimeoutSeconds is <1 or >600||input.InCode is <0 or >255||input.OutCode is <0 or >255||input.InCode==input.OutCode||input.OverlapSeconds<0||input.OverlapSeconds>input.WindowSeconds||input.Confirmation!=(input.AutoCommit?"ENABLE-AUTOMATIC-SDK-INGEST":"ENABLE-SCHEDULED-SDK-READ"))throw new ArgumentException("Explicit schedule cursor, bounded budgets, exact target and consent required.");
        if(!await Authority(actor,input.AutoCommit,input.DeviceId,token))throw new UnauthorizedAccessException();
        await Locks(token);var old=await db.DevicePollSchedules.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.Id,token);
        if(old is not null){if(old.CreatedByUserId!=actor||old.DeviceId!=input.DeviceId||!old.ExpectedDeviceVersion.SequenceEqual(input.ExpectedDeviceVersion)||old.StartUtc!=input.StartUtc||old.EverySeconds!=input.EverySeconds||old.WindowSeconds!=input.WindowSeconds||old.LagSeconds!=input.LagSeconds||old.OverlapSeconds!=input.OverlapSeconds||old.TimeoutSeconds!=input.TimeoutSeconds||old.InCode!=input.InCode||old.OutCode!=input.OutCode||old.AutoCommit!=input.AutoCommit)throw new DomainConflictException("Changed schedule retry.");return(old.Id,old.Id);}
        var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.DeviceId,token)??throw new ArgumentException("Unknown device.");
        if(!device.Enabled||device.Profile!="zk-standalone"||!device.Version.SequenceEqual(input.ExpectedDeviceVersion))throw new DomainConflictException("Current enabled SSR endpoint required.");
        if(await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==device.Id&&x.Enabled,token))throw new DomainConflictException("Pause the existing schedule first.");
        if(await DeviceWriteSafety.UnreviewedAsync(db,device.Id,token)||await DeviceWriteSafety.InFlightAsync(db,device.Id,token))throw new DomainConflictException("Review native writes before enabling polling.");
        var site=await db.Sites.AsNoTracking().SingleAsync(x=>x.Id==device.SiteId,token);
        db.DevicePollSchedules.Add(new(){Id=input.Id,DeviceId=device.Id,SourceId=device.SourceId,SiteId=site.Id,CompanyId=site.CompanyId,TimeZoneId=site.TimeZoneId,CreatedByUserId=actor,CreatedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=input.ExpectedDeviceVersion.ToArray(),StartUtc=input.StartUtc,CursorUtc=input.StartUtc,EverySeconds=input.EverySeconds,WindowSeconds=input.WindowSeconds,LagSeconds=input.LagSeconds,OverlapSeconds=input.OverlapSeconds,TimeoutSeconds=input.TimeoutSeconds,InCode=input.InCode,OutCode=input.OutCode,AutoCommit=input.AutoCommit,NextDueUtc=clock.GetUtcNow()});return(input.Id,input.Id);
    },ct);
    public Task<object> ListAsync(Guid actor,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-poll.viewed",async token=>
    {
        if(offset is <0 or >100000)throw new ArgumentException("Invalid page.");var items=await db.DevicePollSchedules.AsNoTracking().OrderByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50).ToListAsync(token);return(new{offset,pageSize=50,items},actor);
    },ct);
    public Task<object> RunsAsync(Guid actor,Guid id,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-poll.runs-viewed",async token=>
    {
        if(offset is <0 or >100000)throw new ArgumentException("Invalid page.");if(!await db.DevicePollSchedules.AnyAsync(x=>x.Id==id,token))throw new ArgumentException("Unknown schedule.");
        var items=await (from r in db.DevicePollRuns.AsNoTracking() join j in db.DeviceAttendanceDownloads on r.DownloadId equals j.Id where r.ScheduleId==id orderby r.CreatedAtUtc descending,r.Id select new{r.Id,r.ScheduleId,r.DownloadId,r.FromUtc,r.UntilUtcExclusive,r.CreatedAtUtc,r.NextAfter,r.Outcome,r.FinishedAtUtc,downloadState=j.State,j.ReceivedCount}).Skip(offset).Take(50).ToListAsync(token);return(new{scheduleId=id,offset,pageSize=50,items},id);
    },ct);
    public Task<Guid> SetEnabledAsync(Guid actor,Guid id,byte[] version,bool enabled,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Manage,"device-poll.toggled",async token=>
    {
        if(version is not{Length:8}||confirmation!=(enabled?"RESUME-APPROVED-SCHEDULE":"PAUSE-SCHEDULE"))throw new ArgumentException("Explicit versioned pause/resume required.");await Locks(token);var s=await Load(id,token);
        if(!s.Version.SequenceEqual(version))throw new DomainConflictException("Stale schedule version.");
        if(enabled)
        {
            if(actor!=s.CreatedByUserId||!await Authority(s.CreatedByUserId,s.AutoCommit,s.DeviceId,token))throw new UnauthorizedAccessException();
            if(!await SameTarget(s,token))throw new DomainConflictException("Changed target requires a new schedule and explicit approval.");
            if(await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==s.DeviceId&&x.Id!=s.Id&&x.Enabled,token))throw new DomainConflictException("Another schedule is enabled.");
            if(await DeviceWriteSafety.UnreviewedAsync(db,s.DeviceId,token)||await DeviceWriteSafety.InFlightAsync(db,s.DeviceId,token))throw new DomainConflictException("Review native writes before resuming polling.");
            if(!s.Enabled){s.Enabled=true;s.NextDueUtc=clock.GetUtcNow();s.LastOutcome="waiting";s.Failures=0;}
        }
        else
        {
            await StopActive(s,"paused",token);s.Enabled=false;s.LastOutcome="paused";
        }
        return(id,id);
    },ct);
    public Task<Step> StepAsync(Guid service,CancellationToken ct=default)=>transaction.ExecuteAsync(service,Execute,"device-poll.stepped",async token=>
    {
        await Locks(token);var now=clock.GetUtcNow();var s=await db.DevicePollSchedules.AsTracking().Where(x=>x.Enabled&&x.NextDueUtc<=now).OrderBy(x=>x.NextDueUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);
        if(s is null)return(new Step("idle"),service);
        if(!await Authority(s.CreatedByUserId,s.AutoCommit,s.DeviceId,token)){await StopActive(s,"revoked",token);s.Enabled=false;s.LastOutcome="revoked";return(new Step("revoked",s.Id),s.Id);}
        if(!await SameTarget(s,token)){await StopActive(s,"device-changed",token);s.Enabled=false;s.LastOutcome="device-changed";return(new Step("device-changed",s.Id),s.Id);}
        if(await DeviceWriteSafety.UnreviewedAsync(db,s.DeviceId,token)){await StopActive(s,"paused",token);s.Enabled=false;s.LastOutcome="paused";return(new Step("paused",s.Id),s.Id);}
        if(s.ActiveRunId is Guid active)
        {
            var run=await db.DevicePollRuns.AsTracking().SingleAsync(x=>x.Id==active,token);var job=await db.DeviceAttendanceDownloads.AsTracking().SingleAsync(x=>x.Id==run.DownloadId,token);
            if(job.State is "pending" or "running")
            {
                s.NextDueUtc=now.AddSeconds(10);s.LastOutcome=job.State=="pending"?"queued":"collecting";run.Outcome=s.LastOutcome;return(new Step(s.LastOutcome,s.Id,run.Id,job.Id),s.Id);
            }
            if(job.State!="succeeded")
            {
                await FinishRun(run,job.State,token);s.ActiveRunId=null;s.LastOutcome=job.State;s.Failures=(int)Math.Min(30L,(long)s.Failures+1);
                if(job.State is "unsupported" or "identity-mismatch" or "credential-unavailable" or "endpoint-denied" or "device-changed" or "revoked" or "cancelled")s.Enabled=false;
                s.NextDueUtc=now.AddSeconds(Math.Min(86400,Math.Max(s.EverySeconds,15*Math.Pow(2,Math.Min(s.Failures,12)))));return(new Step(s.LastOutcome,s.Id,run.Id,job.Id),s.Id);
            }
            if(s.AutoCommit&&run.NextAfter<job.ReceivedCount)
            {
                var reviewed=await downloads.CommitPageInTransactionAsync(s.CreatedByUserId,job.Id,job.Version,run.NextAfter,"INGEST-SDK-ROWS",token);
                run.NextAfter=JsonSerializer.SerializeToElement(reviewed.Result).GetProperty("nextAfter").GetInt64();run.Outcome="reviewing";
            }
            if(!s.AutoCommit||run.NextAfter>=job.ReceivedCount)
            {
                // Explicit ordering: SQL cursor guard sees the finished real run.
                // The surrounding PermissionTransaction still commits all changes atomically.
                await FinishRun(run,"succeeded",token);s.CursorUtc=run.UntilUtcExclusive;s.ActiveRunId=null;s.Failures=0;s.LastOutcome="succeeded";s.NextDueUtc=now.AddSeconds(s.EverySeconds);
            }
            else{s.LastOutcome="reviewing";s.NextDueUtc=now.AddSeconds(1);}
            return(new Step(s.LastOutcome,s.Id,run.Id,job.Id),s.Id);
        }
        var limit=now.AddSeconds(-s.LagSeconds);limit=limit.AddTicks(-(limit.Ticks%TimeSpan.TicksPerSecond));
        if(s.CursorUtc>=limit){s.NextDueUtc=now.AddSeconds(s.EverySeconds);s.LastOutcome="waiting";return(new Step("waiting",s.Id),s.Id);}
        if(await Busy(s.DeviceId,token)){s.NextDueUtc=now.AddSeconds(15);s.LastOutcome="busy";return(new Step("busy",s.Id),s.Id);}
        var until=s.CursorUtc.AddSeconds(Math.Min(s.WindowSeconds,(limit-s.CursorUtc).TotalSeconds));var id=Guid.NewGuid();
        // Re-read only the explicitly approved lookback; never move the logical
        // acknowledged cursor backwards or read before this schedule's start.
        var lookback=Math.Min(s.OverlapSeconds,(s.CursorUtc-s.StartUtc).TotalSeconds);
        var from=s.CursorUtc.AddSeconds(-lookback);
        await downloads.RequestInTransactionAsync(s.CreatedByUserId,id,s.DeviceId,s.ExpectedDeviceVersion,s.TimeoutSeconds,from,until,s.InCode,s.OutCode,token);
        db.DevicePollRuns.Add(new(){Id=id,ScheduleId=s.Id,DownloadId=id,FromUtc=from,UntilUtcExclusive=until,CreatedAtUtc=now});
        await db.SaveChangesAsync(token); // Persist real run before linking the cyclic active-run FK.
        s.ActiveRunId=id;s.NextDueUtc=now.AddSeconds(10);s.LastOutcome="queued";return(new Step("queued",s.Id,id,id),s.Id);
    },ct);
    private async Task<bool> Authority(Guid actor,bool auto,Guid deviceId,CancellationToken ct)=>await transaction.HasDevicePermissionAsync(actor,Manage,deviceId,ct)&&await transaction.HasPermissionAsync(actor,DeviceAttendanceDownloads.Download,ct)&&(!auto||(await transaction.HasPermissionAsync(actor,DeviceAttendanceDownloads.Commit,ct)&&(await transaction.HasPermissionAsync(actor,"attendance.events.receive",ct)||await transaction.HasPermissionAsync(actor,AttendanceIngestion.Manage,ct))));
    private async Task StopActive(DevicePollScheduleRecord s,string outcome,CancellationToken ct)
    {
        if(s.ActiveRunId is not Guid id)return;var run=await db.DevicePollRuns.AsTracking().SingleAsync(x=>x.Id==id,ct);var job=await db.DeviceAttendanceDownloads.AsTracking().SingleAsync(x=>x.Id==run.DownloadId,ct);
        if(job.State=="pending"){job.State="cancelled";job.FinishedAtUtc=clock.GetUtcNow();}
        await FinishRun(run,outcome,ct);s.ActiveRunId=null;
    }
    private async Task FinishRun(DevicePollRunRecord run,string outcome,CancellationToken ct){run.Outcome=outcome;run.FinishedAtUtc=clock.GetUtcNow();await db.SaveChangesAsync(ct);}
    private async Task Locks(CancellationToken ct){await transaction.LockAsync("IO.DeviceRegistry",ct);await transaction.LockAsync("IO.DeviceConnections",ct);await transaction.LockAsync("IO.DevicePollSchedules",ct);}
    private Task<DevicePollScheduleRecord> Load(Guid id,CancellationToken ct)=>LoadCore(id,ct);
    private async Task<DevicePollScheduleRecord> LoadCore(Guid id,CancellationToken ct)=>await db.DevicePollSchedules.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown schedule.");
    private Task<bool> SameTarget(DevicePollScheduleRecord s,CancellationToken ct)=>(from d in db.Devices join site in db.Sites on d.SiteId equals site.Id where d.Id==s.DeviceId&&d.Enabled&&d.Profile=="zk-standalone"&&d.Version==s.ExpectedDeviceVersion&&d.SourceId==s.SourceId&&site.Id==s.SiteId&&site.CompanyId==s.CompanyId&&site.TimeZoneId==s.TimeZoneId select d.Id).AnyAsync(ct);
    private async Task<bool> Busy(Guid device,CancellationToken ct)=>await db.DeviceConnections.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct);
}
