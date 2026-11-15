using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace IO.Infrastructure.Devices;

/// <summary>Delete-all only. Durable whole-native-log archive, installed capture quiescence, two people, exact live multiset preflight. No date/user/admin/all-data dispatch.</summary>
public sealed class DeviceAttendanceErases(IoDbContext db,PermissionTransaction tx,TimeProvider clock,IConfiguration config,IDataProtectionProvider protection)
{
 public const string Read="devices.attendance.erase.read",Propose="devices.attendance.erase.propose",Approve="devices.attendance.erase.approve",Execute="devices.attendance.erase.execute",Recover="devices.attendance.erase.recover.propose",ReviewRecovery="devices.attendance.erase.recover.approve",Confirmation="DELETE-ALL-DEVICE-ATTENDANCE-AFTER-DURABLE-ARCHIVE",RecoveryConfirmation="RELEASE-ATTENDANCE-FENCE-NOT-DELETE-SUCCESS";
 public sealed record Input(Guid Id,Guid DeviceId,Guid ArchiveId,byte[] DeviceVersion,byte[] ArchiveVersion,DateTimeOffset QuiescedAtUtc,string EvidenceReference,AttendanceEraseApproval Approval,Guid? RecoveryOfId=null)
 {public override string ToString()=>"Attendance erase proposal [references redacted]";}
 public sealed record Confirm(Input Intent,string PreviewHash,string Confirmation);
 public sealed record Review(byte[] Version,string EvidenceReference,string Confirmation);
 public sealed record Claim(Guid Id,Guid Token,Guid DeviceId,string Host,int Port,AttendanceEraseApproval Approval,int Count,string Digest)
 {public override string ToString()=>"Attendance erase claim [private metadata redacted]";}
 private static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
 private static bool Text(string? v)=>!string.IsNullOrWhiteSpace(v)&&v.Length<=200&&!v.Any(char.IsControl);
 private static Input Freeze(Input i)
 {
  if(i is null||i.Id==Guid.Empty||i.DeviceId==Guid.Empty||i.ArchiveId==Guid.Empty||i.RecoveryOfId==Guid.Empty||i.RecoveryOfId==i.Id||i.DeviceVersion is not{Length:8}||i.ArchiveVersion is not{Length:8}||i.Approval?.IsValid()!=true||i.QuiescedAtUtc.Offset!=TimeSpan.Zero||!Text(i.EvidenceReference))throw new ArgumentException("Explicit bounded archive, installation, physical quiescence and policy required.");
  return i with{DeviceVersion=i.DeviceVersion.ToArray(),ArchiveVersion=i.ArchiveVersion.ToArray()};
 }
 private bool Approved(AttendanceEraseApproval a,bool recovery)=>a.IsValid()&&config.GetValue<bool>("IO:AttendanceErase:EnableDeleteAll")&&(!recovery||config.GetValue<bool>("IO:AttendanceErase:EnableRecovery"))&&(config.GetSection("IO:AttendanceErase:Approvals").Get<AttendanceEraseApproval[]>()??[]).Count(x=>x==a)==1;
 private async Task Lock(CancellationToken ct){await tx.LockAsync("IO.DeviceRegistry",ct);await tx.LockAsync("IO.DeviceConnections",ct);}
 private async Task<(DeviceRecord Device,DeviceAttendanceDownloadRecord Archive)> Target(Guid actor,Input i,Guid? self,CancellationToken ct)
 {
  if(!Approved(i.Approval,i.RecoveryOfId!=null))throw new DomainConflictException("Erase/recovery is default-off; exact installed capture-quiescence, legal and retention approval required.");
  if(!await tx.HasDevicePermissionAsync(actor,i.RecoveryOfId is null?Propose:Recover,i.DeviceId,ct)||!await tx.HasDevicePermissionAsync(actor,DeviceAttendanceDownloads.Read,i.DeviceId,ct))throw new UnauthorizedAccessException();
  var d=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.DeviceId,ct)??throw new ArgumentException("Unknown or inaccessible device.");
  var a=await db.DeviceAttendanceDownloads.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.ArchiveId,ct)??throw new ArgumentException("Unknown or inaccessible archive.");
  var site=await db.Sites.AsNoTracking().SingleAsync(x=>x.Id==d.SiteId,ct);var now=clock.GetUtcNow();
  if(!d.Enabled||d.Profile!="zk-standalone"||d.Host is null||d.Port is null||!d.Version.SequenceEqual(i.DeviceVersion)||a.DeviceId!=d.Id||a.SourceId!=d.SourceId||a.SiteId!=d.SiteId||a.CompanyId!=site.CompanyId||a.TimeZoneId!=site.TimeZoneId||a.State!="succeeded"||a.ReadFormat!=i.Approval.ReadFormat||!a.ExpectedDeviceVersion.SequenceEqual(i.DeviceVersion)||!a.Version.SequenceEqual(i.ArchiveVersion)||a.ReceivedCount<0||a.ReceivedCount>i.Approval.MaximumRecords||a.FinishedAtUtc is null||a.FinishedAtUtc<now.AddSeconds(-i.Approval.MaximumArchiveAgeSeconds)||a.FinishedAtUtc>now||a.RequestedAtUtc<i.QuiescedAtUtc.AddSeconds(i.Approval.MinimumSettlingSeconds)||i.QuiescedAtUtc<now.AddMinutes(-30))throw new DomainConflictException("Complete fresh same-installation full native archive AFTER settled physical capture-stop required. Filtered ingestion/count alone is not an archive.");
  if(i.RecoveryOfId is{} parentId)
  {
   var parent=await db.DeviceAttendanceErases.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==parentId,ct);
   if(parent is null||parent.DeviceId!=d.Id||parent.RecoveryOfId!=null||parent.State!="unknown"||parent.FinishedAtUtc is null||parent.FinishedAtUtc>i.QuiescedAtUtc||!parent.DeviceVersion.SequenceEqual(i.DeviceVersion)||JsonSerializer.Deserialize<AttendanceEraseApproval>(parent.ApprovalJson)!=i.Approval||await Released(parentId,ct))throw new DomainConflictException("Unresolved original unknown, later approved physical reset/capture-stop and new durable read-only observation required.");
  }
  if(await Busy(i.DeviceId,self,ct)||await DeviceWriteSafety.UnreviewedAsync(db,i.DeviceId,ct,ignoredErase:i.RecoveryOfId))throw new DomainConflictException("Pause polling and complete/review all other device work first.");
  return(d,a);
 }
 private Task<bool> Released(Guid parent,CancellationToken ct)=>db.DeviceAttendanceErases.IgnoreQueryFilters().AnyAsync(x=>x.RecoveryOfId==parent&&x.State=="released",ct);
 private async Task<(int Count,string Digest)> Digest(DeviceAttendanceDownloadRecord a,AttendanceEraseApproval approval,CancellationToken ct)
 {
  var digest=new AttendanceArchiveDigest(approval.MaximumRecords);long next=0;
  while(true)
  {
   var batch=await db.DeviceAttendanceRows.AsNoTracking().Where(x=>x.DownloadId==a.Id&&x.Sequence>next).OrderBy(x=>x.Sequence).Take(100).ToListAsync(ct);if(batch.Count==0)break;
   foreach(var row in batch){if(row.Sequence!=++next)throw new DomainConflictException("Non-contiguous archive.");var raw=JsonSerializer.Deserialize<ZkAttendanceRaw>(row.PayloadJson)??throw new DomainConflictException("Invalid archive.");digest.Add(raw,approval.ReadFormat);}
  }
  if(digest.Count!=a.ReceivedCount)throw new DomainConflictException("Incomplete durable archive.");return(digest.Count,digest.Finish());
 }
 public Task<object> PreviewAsync(Guid actor,Input input,CancellationToken ct=default)
 {
  input=Freeze(input);return tx.ExecuteAsync<object>(actor,input.RecoveryOfId is null?Propose:Recover,"attendance-erase.previewed",async token=>
  {await Lock(token);var (_,archive)=await Target(actor,input,null,token);var digest=await Digest(archive,input.Approval,token);return(new{previewHash=Hash(new{input,digest.Count,digest.Digest}),input.DeviceId,input.ArchiveId,input.RecoveryOfId,archiveCount=digest.Count,confirmation=input.RecoveryOfId is null?Confirmation:RecoveryConfirmation,meaning=input.RecoveryOfId is null?"whole-device-log-only-not-raw-SQL-attendance":"physical-reset-and-current-observation-not-original-success"},input.Id);},ct);
 }
 public Task<Guid> ProposeAsync(Guid actor,Confirm input,CancellationToken ct=default)
 {
  if(input is null||input.PreviewHash is not{Length:64})throw new ArgumentException("Exact preview required.");input=input with{Intent=Freeze(input.Intent)};
  if(input.Confirmation!=(input.Intent.RecoveryOfId is null?Confirmation:RecoveryConfirmation))throw new ArgumentException("Exact operation confirmation required.");
  return tx.ExecuteAsync(actor,input.Intent.RecoveryOfId is null?Propose:Recover,"attendance-erase.proposed",async token=>
  {
   await Lock(token);var i=input.Intent;var old=await db.DeviceAttendanceErases.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.Id,token);
   if(old is not null){if(old.RequestedByUserId!=actor||old.RequestHash!=Hash(input))throw new DomainConflictException("Changed erase retry.");return(old.Id,old.Id);}
   var (_,archive)=await Target(actor,i,null,token);var digest=await Digest(archive,i.Approval,token);
   if(Hash(new{input=i,digest.Count,digest.Digest})!=input.PreviewHash)throw new DomainConflictException("Archive or intent changed; preview again.");
   db.DeviceAttendanceErases.Add(new(){Id=i.Id,DeviceId=i.DeviceId,ArchiveId=i.ArchiveId,RecoveryOfId=i.RecoveryOfId,DeviceVersion=i.DeviceVersion,ArchiveVersion=i.ArchiveVersion,RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),QuiescedAtUtc=i.QuiescedAtUtc,EvidenceReference=i.EvidenceReference,ApprovalJson=JsonSerializer.Serialize(i.Approval),ArchiveCount=digest.Count,ArchiveDigest=digest.Digest,RequestHash=Hash(input)});return(i.Id,i.Id);
  },ct);
 }
 private static Input Intent(DeviceAttendanceEraseRecord r)=>new(r.Id,r.DeviceId,r.ArchiveId,r.DeviceVersion,r.ArchiveVersion,r.QuiescedAtUtc,r.EvidenceReference,JsonSerializer.Deserialize<AttendanceEraseApproval>(r.ApprovalJson)!,r.RecoveryOfId);
 public async Task<Guid> ApproveAsync(Guid actor,Guid id,Review input,CancellationToken ct=default)
 {
  if(input is null||input.Version is not{Length:8}||!Text(input.EvidenceReference))throw new ArgumentException("Fresh version and independent evidence required.");input=input with{Version=input.Version.ToArray()};
  var recovery=await db.DeviceAttendanceErases.AsNoTracking().Where(x=>x.Id==id).Select(x=>x.RecoveryOfId!=null).SingleOrDefaultAsync(ct);
  return await tx.ExecuteAsync(actor,recovery?ReviewRecovery:Approve,"attendance-erase.approved",async token=>
  {
   await Lock(token);var row=await db.DeviceAttendanceErases.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown or inaccessible proposal.");
   if(row.State!="proposed"||row.RequestedByUserId==actor||!row.Version.SequenceEqual(input.Version)||input.Confirmation!=(row.RecoveryOfId is null?Confirmation:RecoveryConfirmation)||row.RequestedAtUtc<clock.GetUtcNow().AddMinutes(-2))throw new DomainConflictException("Independent current unexpired proposal review required; no self-signature.");
   if(!await tx.HasDevicePermissionAsync(actor,recovery?ReviewRecovery:Approve,row.DeviceId,token)||!await tx.HasDevicePermissionAsync(actor,DeviceAttendanceDownloads.Read,row.DeviceId,token))throw new UnauthorizedAccessException();
   await Target(row.RequestedByUserId,Intent(row),row.Id,token);
   row.ApprovedByUserId=actor;row.ApprovedAtUtc=clock.GetUtcNow();row.ReviewReference=input.EvidenceReference;row.State=recovery?"released":"pending";if(recovery)row.FinishedAtUtc=clock.GetUtcNow();return(row.Id,row.Id);
  },ct);
 }
 public Task<object> ListAsync(Guid actor,CancellationToken ct=default)=>tx.ExecuteAsync<object>(actor,Read,"attendance-erase.viewed",async token=>
 {var items=await db.DeviceAttendanceErases.AsNoTracking().OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Take(50).Select(x=>new{x.Id,x.DeviceId,x.ArchiveId,x.RecoveryOfId,x.ArchiveCount,x.State,x.RequestedByUserId,x.RequestedAtUtc,x.QuiescedAtUtc,x.EvidenceReference,x.ApprovedByUserId,x.ApprovedAtUtc,x.ReviewReference,x.CancelRequestedAtUtc,x.FinishedAtUtc,x.Version}).ToListAsync(token);return(new{items,originalUnknownNeverChanged=true},actor);},ct);
 public async Task<Guid> CancelAsync(Guid actor,Guid id,byte[] version,CancellationToken ct=default)
 {
  var recovery=await db.DeviceAttendanceErases.AsNoTracking().Where(x=>x.Id==id).Select(x=>x.RecoveryOfId!=null).SingleOrDefaultAsync(ct);
  return await tx.ExecuteAsync(actor,recovery?Recover:Propose,"attendance-erase.cancel-requested",async token=>
  {await Lock(token);var r=await db.DeviceAttendanceErases.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown proposal.");if(version is not{Length:8}||!r.Version.SequenceEqual(version))throw new DomainConflictException("Refresh version.");if(r.State is "proposed" or "pending"){r.State="cancelled";r.FinishedAtUtc=clock.GetUtcNow();}else if(r.State=="running")r.CancelRequestedAtUtc??=clock.GetUtcNow();else throw new DomainConflictException("Terminal result; no replay.");return(r.Id,r.Id);},ct);
 }
 public Task<Claim?> ClaimAsync(Guid service,CancellationToken ct=default)=>tx.ExecuteAsync<Claim?>(service,Execute,"attendance-erase.claimed",async token=>
 {
  await Lock(token);var now=clock.GetUtcNow();
  foreach(var r in await db.DeviceAttendanceErases.AsTracking().Where(x=>(x.State=="running"&&x.ClaimedAtUtc<now.AddMinutes(-2))||(x.State=="proposed"&&x.RequestedAtUtc<now.AddMinutes(-2))).ToListAsync(token)){r.State=r.State=="running"?"unknown":"expired";r.FinishedAtUtc=now;}
  var row=await db.DeviceAttendanceErases.AsTracking().Where(x=>x.State=="pending"&&x.RecoveryOfId==null).OrderBy(x=>x.ApprovedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);if(row is null)return(null,service);
  DeviceRecord device;
  try{device=await Current(row,token)??throw new DomainConflictException("Authority changed.");}
  catch(Exception e)when(e is DomainConflictException or UnauthorizedAccessException or ArgumentException){row.State="revoked";row.FinishedAtUtc=now;return(null,row.Id);}
  row.State="running";row.ServiceUserId=service;row.ClaimId=Guid.NewGuid();row.ClaimedAtUtc=now;
  return(new Claim(row.Id,row.ClaimId.Value,row.DeviceId,device.Host!,device.Port!.Value,Intent(row).Approval,row.ArchiveCount,row.ArchiveDigest),row.Id);
 },ct);
 private async Task<DeviceRecord?> Current(DeviceAttendanceEraseRecord row,CancellationToken ct)
 {
  if(row.CancelRequestedAtUtc!=null||row.ApprovedByUserId is null||row.ApprovedAtUtc<clock.GetUtcNow().AddMinutes(-2)||!await tx.HasDevicePermissionAsync(row.ApprovedByUserId.Value,Approve,row.DeviceId,ct)||!await tx.HasDevicePermissionAsync(row.ApprovedByUserId.Value,DeviceAttendanceDownloads.Read,row.DeviceId,ct))return null;
  return(await Target(row.RequestedByUserId,Intent(row),row.Id,ct)).Device;
 }
 private async Task<DeviceAttendanceEraseRecord> Owned(Guid service,Claim c,CancellationToken ct)
 {
  var r=await db.DeviceAttendanceErases.AsTracking().SingleOrDefaultAsync(x=>x.Id==c.Id,ct)??throw new ArgumentException("Unknown claim.");
  if(r.State!="running"||r.ServiceUserId!=service||r.ClaimId!=c.Token||r.ClaimedAtUtc<clock.GetUtcNow().AddMinutes(-2)||r.DeviceId!=c.DeviceId||r.ArchiveCount!=c.Count||r.ArchiveDigest!=c.Digest||Intent(r).Approval!=c.Approval)throw new DomainConflictException("Expired or changed owned claim.");return r;
 }
 public Task<DeviceCommunicationKey?> CredentialAsync(Guid service,Claim c,bool allowDefault,CancellationToken ct=default)=>tx.ExecuteAsync<DeviceCommunicationKey?>(service,Execute,"attendance-erase.credential-used",async token=>
 {
  await Lock(token);var r=await Owned(service,c,token);if(!await IsCurrent(r,c,token))return(null,r.Id);
  var cipher=await db.DeviceSecrets.Where(x=>x.DeviceId==r.DeviceId).Select(x=>x.ProtectedValue).SingleOrDefaultAsync(token);if(cipher is null)return(allowDefault?new(0):null,r.Id);
  try{var key=protection.CreateProtector("IO.Devices.Credential.v1",r.DeviceId.ToString("N")).Unprotect(cipher);return(key.Length>0&&key.All(char.IsAsciiDigit)&&int.TryParse(key,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?new(n):null,r.Id);}catch(CryptographicException){return(null,r.Id);}
 },ct);
 private async Task<bool> IsCurrent(DeviceAttendanceEraseRecord r,Claim c,CancellationToken ct){try{var d=await Current(r,ct);return d is not null&&d.Host==c.Host&&d.Port==c.Port;}catch(Exception e)when(e is DomainConflictException or UnauthorizedAccessException or ArgumentException){return false;}}
 public Task<bool> ShouldStopAsync(Guid service,Claim c,CancellationToken ct=default)=>tx.ExecuteAsync(service,Execute,"attendance-erase.authority-checked",async token=>{await Lock(token);var r=await Owned(service,c,token);return(!await IsCurrent(r,c,token),r.Id);},ct);
 public Task<Guid> CompleteAsync(Guid service,Claim c,AttendanceEraseOutput output,CancellationToken ct=default)=>tx.ExecuteAsync(service,Execute,"attendance-erase.finished",async token=>
 {await Lock(token);var r=await Owned(service,c,token);if(output.OperationId!=r.Id||!AttendanceEraseOutput.Known(output.State))throw new ArgumentException("Correlated bounded result required.");r.State=await IsCurrent(r,c,token)?output.State:"unknown";r.FinishedAtUtc=clock.GetUtcNow();return(r.Id,r.Id);},ct);
 private async Task<bool> Busy(Guid device,Guid? self,CancellationToken ct)=>
  await db.DeviceAttendanceErases.IgnoreQueryFilters().AnyAsync(x=>x.DeviceId==device&&x.Id!=self&&(x.State=="proposed"||x.State=="pending"||x.State=="running"),ct)||
  await db.DeviceBiometricTransfers.IgnoreQueryFilters().AnyAsync(x=>(x.SourceDeviceId==device||x.TargetDeviceId==device)&&(x.State=="pending"||x.State=="running"),ct)||
  await db.RemoteEnrollments.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceConnections.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"||x.State=="unknown"&&x.ResolutionSnapshotId==null),ct)||await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),ct)||await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==device&&x.Enabled,ct);
}
internal static class AttendanceEraseFence
{
 internal static async Task EnsureAsync(IoDbContext db,Guid device,bool writing,CancellationToken ct)
 {
  if(await db.DeviceAttendanceErases.IgnoreQueryFilters().AnyAsync(x=>x.DeviceId==device&&(x.State=="proposed"||x.State=="pending"||x.State=="running"||writing&&x.State=="unknown"&&!db.DeviceAttendanceErases.IgnoreQueryFilters().Any(r=>r.RecoveryOfId==x.Id&&r.State=="released")),ct))throw new DomainConflictException("Attendance erase owns the device or has an unresolved destructive unknown; no replay.");
 }
}
