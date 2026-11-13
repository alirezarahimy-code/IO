using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Devices;

/// <summary>Unified SQL history over actual typed queues; never arbitrary native commands.
/// Read retries create new intents with immutable lineage, not a rewrite of failed evidence.</summary>
public sealed class DeviceCommands(IoDbContext db,PermissionTransaction transaction,DeviceOperations operations,DeviceAttendanceDownloads attendance)
{
 public const string Read="devices.commands.read",Retry="devices.commands.retry";
 public sealed record RetryInput(Guid Id,string Queue,Guid PreviousId,byte[] ExpectedPreviousVersion,byte[] ExpectedDeviceVersion,string Confirmation,string? SettingsConfirmation=null);
 public sealed class Row
 {
  public string Queue{get;set;}=null!;public Guid Id{get;set;}public Guid DeviceId{get;set;}public string Kind{get;set;}=null!;public string State{get;set;}=null!;
  public Guid RequestedByUserId{get;set;}public DateTimeOffset RequestedAtUtc{get;set;}public DateTimeOffset? FinishedAtUtc{get;set;}public byte[] ExpectedDeviceVersion{get;set;}=null!;public byte[] Version{get;set;}=null!;
 }
 public Task<object> ListAsync(Guid actor,Guid? deviceId,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-commands.viewed",async token=>
 {
  if(deviceId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid page.");
  var query=db.Database.SqlQuery<Row>($"SELECT * FROM dbo.DeviceCommandHistory");
  var items=await query.Where(x=>(!db.RestrictDeviceAccess||db.PermittedDeviceIds.Contains(x.DeviceId))&&(deviceId==null||x.DeviceId==deviceId)).OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Queue).ThenBy(x=>x.Id).Skip(offset).Take(50).ToListAsync(token);
  // Queue + id is the history identity: client-selected UUIDs may legitimately
  // recur in different typed tables. Never attach another queue's retry evidence.
  var operationIds=items.Where(x=>x.Queue=="operation").Select(x=>x.Id).ToArray();
  var attendanceIds=items.Where(x=>x.Queue=="attendance-download").Select(x=>x.Id).ToArray();
  var retries=await db.DeviceCommandRetries.AsNoTracking().Where(x=>(x.PreviousOperationId!=null&&operationIds.Contains(x.PreviousOperationId.Value))||(x.NextOperationId!=null&&operationIds.Contains(x.NextOperationId.Value))||(x.PreviousAttendanceDownloadId!=null&&attendanceIds.Contains(x.PreviousAttendanceDownloadId.Value))||(x.NextAttendanceDownloadId!=null&&attendanceIds.Contains(x.NextAttendanceDownloadId.Value))).ToListAsync(token);
  return(new{offset,pageSize=50,items,retries},actor);
 },ct);
 public Task<Guid> RetryAsync(Guid actor,RetryInput input,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Retry,"device-command.read-retried",async token=>
 {
  if(input is null||input.Id==Guid.Empty||input.PreviousId==Guid.Empty||input.Id==input.PreviousId||input.ExpectedPreviousVersion is not{Length:8}||input.ExpectedDeviceVersion is not{Length:8}||input.Confirmation!="RETRY-READ-ONLY-COMMAND"||input.Queue is not("operation" or "attendance-download"))throw new ArgumentException("Only explicitly confirmed typed read retries are supported.");
  await transaction.LockAsync("IO.DeviceRegistry",token);await transaction.LockAsync("IO.DeviceConnections",token);
  var old=await db.DeviceCommandRetries.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.Id,token);
  if(!await transaction.HasPermissionAsync(actor,Read,token))throw new UnauthorizedAccessException();
  DeviceOperationRecord? previousOperation=null;
  if(input.Queue=="operation")
  {
   previousOperation=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.PreviousId,token)??throw new ArgumentException("Unknown command.");
   if(previousOperation.Kind is not("information.read" or "settings.read"))throw new DomainConflictException("Native writes cannot be retried here.");
  }
  var settings=previousOperation?.Kind=="settings.read";
  var permission=input.Queue=="operation"?(settings?DeviceOperations.ReadSettings:DeviceOperations.Read):DeviceAttendanceDownloads.Download;
  if(!await transaction.HasPermissionAsync(actor,permission,token))throw new UnauthorizedAccessException();
  if(settings&&input.SettingsConfirmation!="READ-ADVANCED-SETTINGS")throw new DomainConflictException("Additional explicit settings read consent required.");
  if(!settings&&input.SettingsConfirmation is not null)throw new ArgumentException("Settings consent cannot retarget another typed queue.");
  if(old is not null)
  {
   if(old.RequestedByUserId!=actor||old.PreviousOperationId!=(input.Queue=="operation"?input.PreviousId:null)||old.PreviousAttendanceDownloadId!=(input.Queue=="attendance-download"?input.PreviousId:null)||!old.ExpectedPreviousVersion.SequenceEqual(input.ExpectedPreviousVersion)||!old.ExpectedDeviceVersion.SequenceEqual(input.ExpectedDeviceVersion))throw new DomainConflictException("Changed retry replay.");return(input.Id,input.Id);
  }
  if(await db.DeviceCommandRetries.AnyAsync(x=>input.Queue=="operation"?x.PreviousOperationId==input.PreviousId:x.PreviousAttendanceDownloadId==input.PreviousId,token))throw new DomainConflictException("Read already retried; follow its existing child intent.");
  if((input.Queue=="operation"&&await db.DeviceOperations.AnyAsync(x=>x.Id==input.Id,token))||(input.Queue=="attendance-download"&&await db.DeviceAttendanceDownloads.AnyAsync(x=>x.Id==input.Id,token)))throw new DomainConflictException("New retry id already used by a different intent.");
  DateTimeOffset at;
  if(input.Queue=="operation")
  {
   var previous=previousOperation!;
   if(previous.Kind is not("information.read" or "settings.read")||!Retryable(previous.State,false)||previous.FinishedAtUtc is null||!previous.Version.SequenceEqual(input.ExpectedPreviousVersion)||!previous.ExpectedDeviceVersion.SequenceEqual(input.ExpectedDeviceVersion))throw new DomainConflictException("Only a terminal unchanged typed read can be retried; never a native write.");
   await operations.RequestInTransactionAsync(actor,input.Id,previous.DeviceId,input.ExpectedDeviceVersion,previous.Kind,previous.TimeoutSeconds,null,settings?"READ-ADVANCED-SETTINGS":null,token);
   var next=db.DeviceOperations.Local.Single(x=>x.Id==input.Id);if(next.TimeZoneId!=previous.TimeZoneId)throw new DomainConflictException("Target zone changed; approve a new request instead.");at=next.RequestedAtUtc;
  }
  else
  {
   var previous=await db.DeviceAttendanceDownloads.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.PreviousId,token)??throw new ArgumentException("Unknown command.");
   if(!Retryable(previous.State,true)||previous.FinishedAtUtc is null||!previous.Version.SequenceEqual(input.ExpectedPreviousVersion)||!previous.ExpectedDeviceVersion.SequenceEqual(input.ExpectedDeviceVersion))throw new DomainConflictException("Only a terminal unchanged attendance read can be retried.");
   await attendance.RequestInTransactionAsync(actor,input.Id,previous.DeviceId,input.ExpectedDeviceVersion,previous.TimeoutSeconds,previous.FromUtc,previous.UntilUtcExclusive,previous.InCode,previous.OutCode,token);
   var next=db.DeviceAttendanceDownloads.Local.Single(x=>x.Id==input.Id);if(next.SourceId!=previous.SourceId||next.SiteId!=previous.SiteId||next.CompanyId!=previous.CompanyId||next.TimeZoneId!=previous.TimeZoneId)throw new DomainConflictException("Target changed; approve a new request instead.");at=next.RequestedAtUtc;
  }
  db.DeviceCommandRetries.Add(new(){Id=input.Id,PreviousOperationId=input.Queue=="operation"?input.PreviousId:null,NextOperationId=input.Queue=="operation"?input.Id:null,PreviousAttendanceDownloadId=input.Queue=="attendance-download"?input.PreviousId:null,NextAttendanceDownloadId=input.Queue=="attendance-download"?input.Id:null,RequestedByUserId=actor,RequestedAtUtc=at,ExpectedPreviousVersion=input.ExpectedPreviousVersion.ToArray(),ExpectedDeviceVersion=input.ExpectedDeviceVersion.ToArray()});
  return(input.Id,input.Id);
 },ct);
 private static bool Retryable(string state,bool logs)=>state is "failed" or "interrupted" or "timed-out" or "bridge-failed"||(logs?state=="incomplete":state=="unknown");
}
