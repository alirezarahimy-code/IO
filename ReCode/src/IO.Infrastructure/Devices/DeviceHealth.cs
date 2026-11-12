using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Devices;

/// <summary>Persisted observations, never a fabricated online/offline device flag.</summary>
public sealed class DeviceHealth(IoDbContext db,PermissionTransaction transaction,TimeProvider clock)
{
 public const string Read="devices.health.read",Report="devices.service.report";
 public const int FreshSeconds=90;
 public Task<Guid> ReportAsync(Guid service,Guid instance,string state,CancellationToken ct=default)=>transaction.ExecuteAsync(service,Report,"device-service.reported",async token=>
 {
  if(instance==Guid.Empty||state is not("running" or "stopped"))throw new ArgumentException("Invalid service observation.");
  await transaction.LockAsync("IO.DeviceServiceHeartbeat."+instance.ToString("N"),token);
  var item=await db.DeviceServiceHeartbeats.AsTracking().SingleOrDefaultAsync(x=>x.Id==instance,token);var now=clock.GetUtcNow();
  if(item is null)
  {
   if(state!="running")throw new DomainConflictException("An unreported instance cannot stop.");
   db.DeviceServiceHeartbeats.Add(new(){Id=instance,ServiceUserId=service,StartedAtUtc=now,LastSeenAtUtc=now,State=state});
  }
  else
  {
   if(item.ServiceUserId!=service)throw new UnauthorizedAccessException();
   if(item.State=="stopped"){if(state!="stopped")throw new DomainConflictException("Restart requires a new instance id.");return(instance,instance);}
   if(now<item.LastSeenAtUtc)throw new DomainConflictException("Server clock moved backwards.");
   item.LastSeenAtUtc=now;item.State=state;
  }
  return(instance,instance);
 },ct);
 public Task<object> ListAsync(Guid actor,Guid? deviceId,int offset,int serviceOffset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-health.viewed",async token=>
 {
  if(deviceId==Guid.Empty||offset is <0 or >100000||serviceOffset is <0 or >100000)throw new ArgumentException("Invalid page.");
  var now=clock.GetUtcNow();
  var devices=await db.Devices.AsNoTracking().Where(x=>deviceId==null||x.Id==deviceId).OrderBy(x=>x.Number).ThenBy(x=>x.Id).Skip(offset).Take(50).Select(x=>new{x.Id,x.Number,x.Name,x.Enabled}).ToListAsync(token);
  var ids=devices.Select(x=>x.Id).ToArray();
  // Window functions choose one deterministic observation per device without
  // loading all personal history or issuing one query per device.
  var latest=await db.Database.SqlQuery<DeviceCommands.Row>($"SELECT [Queue],[Id],[DeviceId],[Kind],[State],[RequestedByUserId],[RequestedAtUtc],[FinishedAtUtc],[ExpectedDeviceVersion],[Version] FROM (SELECT *,ROW_NUMBER() OVER(PARTITION BY DeviceId ORDER BY RequestedAtUtc DESC,[Queue],Id) AS rn FROM dbo.DeviceCommandHistory) AS h WHERE rn=1").Where(x=>ids.Contains(x.DeviceId)).ToListAsync(token);
  var downloads=await db.DeviceAttendanceDownloads.AsNoTracking().Where(x=>ids.Contains(x.DeviceId)&&x.State=="succeeded").GroupBy(x=>x.DeviceId).Select(g=>new{DeviceId=g.Key,FinishedAtUtc=g.Max(x=>x.FinishedAtUtc)}).ToListAsync(token);
  var counts=await db.Database.SqlQuery<DeviceCommands.Row>($"SELECT * FROM dbo.DeviceCommandHistory").Where(x=>ids.Contains(x.DeviceId)).GroupBy(x=>x.DeviceId).Select(g=>new{DeviceId=g.Key,Pending=g.Count(x=>x.State=="pending"),Running=g.Count(x=>x.State=="running"),UnknownHistory=g.Count(x=>x.State=="unknown")}).ToListAsync(token);
  var schedules=await db.DevicePollSchedules.AsNoTracking().Where(x=>ids.Contains(x.DeviceId)).GroupBy(x=>x.DeviceId).Select(g=>g.OrderByDescending(x=>x.Enabled).ThenByDescending(x=>x.CreatedAtUtc).ThenBy(x=>x.Id).First()).ToListAsync(token);
  var scheduleByDevice=schedules.ToDictionary(x=>x.DeviceId,x=>new{x.Id,x.Enabled,x.CursorUtc,x.NextDueUtc,x.Failures,x.LastOutcome,x.ActiveRunId});
  var services=await db.DeviceServiceHeartbeats.AsNoTracking().OrderByDescending(x=>x.LastSeenAtUtc).ThenBy(x=>x.Id).Skip(serviceOffset).Take(50).ToListAsync(token);
  return(new{observedAtUtc=now,offset,serviceOffset,pageSize=50,freshSeconds=FreshSeconds,
   items=devices.Select(d=>new{d.Id,d.Number,d.Name,d.Enabled,latest=latest.Where(x=>x.DeviceId==d.Id).Select(x=>new{x.Queue,x.Id,x.Kind,x.State,x.RequestedAtUtc,x.FinishedAtUtc}).SingleOrDefault(),counts=counts.SingleOrDefault(x=>x.DeviceId==d.Id),lastSuccessfulAttendanceReadAtUtc=downloads.SingleOrDefault(x=>x.DeviceId==d.Id)?.FinishedAtUtc,schedule=scheduleByDevice.GetValueOrDefault(d.Id)}),
   services=services.Select(s=>new{s.Id,s.ServiceUserId,s.StartedAtUtc,s.LastSeenAtUtc,s.State,observation=s.State=="stopped"?"stopped":s.LastSeenAtUtc>now?"clock-anomaly":s.LastSeenAtUtc>=now.AddSeconds(-FreshSeconds)?"recent-heartbeat":"stale-heartbeat"})},actor);
 },ct);
}
