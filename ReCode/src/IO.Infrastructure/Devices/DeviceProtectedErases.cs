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

public sealed class DeviceProtectedErases(IoDbContext db,PermissionTransaction tx,TimeProvider clock,IConfiguration config,IDataProtectionProvider protection)
{
 public static string Right(string family,string action){if(family is not("users" or "fingerprints" or "administrators"))throw new ArgumentException("Closed family required.");return "devices.protected.erase."+family+"."+action;}
 public static string Confirmation(string family,bool recovery)=>recovery?"RELEASE-"+family.ToUpperInvariant()+"-FENCE-NOT-DELETE-SUCCESS":"DELETE-DEVICE-"+family.ToUpperInvariant()+"-IRREVERSIBLY";
 public sealed record Input(Guid Id,Guid DeviceId,Guid ArchiveId,byte[] DeviceVersion,byte[] ArchiveVersion,DateTimeOffset QuiescedAtUtc,string EvidenceReference,ProtectedEraseApproval Approval,string? Person=null,Guid? RecoveryOfId=null)
 {public override string ToString()=>"Protected erase intent [metadata redacted]";}
 public sealed record Confirm(Input Intent,string PreviewHash,string Confirmation);
 public sealed record Review(byte[] Version,string EvidenceReference,string Confirmation);
 public sealed record Claim(Guid Id,Guid Token,Guid DeviceId,string Host,int Port,ProtectedEraseApproval Approval,ZkUser? Expected,int Count,string Digest,bool InspectOnly)
 {public override string ToString()=>"Protected erase claim [metadata redacted]";}
 private static string Hash(object i)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(i)));
 private static bool Text(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=200&&!s.Any(char.IsControl);
 private static Input Freeze(Input i,string family)
 {
  _=Right(family,"read");if(i is null||i.Id==Guid.Empty||i.DeviceId==Guid.Empty||i.ArchiveId==Guid.Empty||i.RecoveryOfId==Guid.Empty||i.RecoveryOfId==i.Id||i.DeviceVersion is not{Length:8}||i.ArchiveVersion is not{Length:8}||i.Approval?.IsValid()!=true||i.Approval.Family!=family||!Text(i.EvidenceReference)||i.QuiescedAtUtc.Offset!=TimeSpan.Zero||(family=="administrators"?i.Person is not null:i.Person is not{Length:>=1 and <=40}||!i.Person.All(char.IsAsciiDigit)||(i.Approval.Inventory.UserFormat==ZkUserReadFormats.Legacy&&!ZkUserReadFormats.IsCanonicalLegacyIdentity(i.Person))))throw new ArgumentException("Exact family, archive, numeric subject, installation and exclusive-control evidence required.");return i with{DeviceVersion=i.DeviceVersion.ToArray(),ArchiveVersion=i.ArchiveVersion.ToArray()};
 }
 private bool Approved(ProtectedEraseApproval a,bool recovery)=>a.IsValid()&&config.GetValue<bool>("IO:ProtectedErase:"+a.Family+":Enabled")&&(!recovery||config.GetValue<bool>("IO:ProtectedErase:"+a.Family+":EnableRecovery"))&&(config.GetSection("IO:ProtectedErase:"+a.Family+":Approvals").Get<ProtectedEraseApproval[]>()??[]).Count(x=>x==a)==1;
 private async Task Lock(CancellationToken ct){await tx.LockAsync("IO.DeviceRegistry",ct);await tx.LockAsync("IO.DeviceConnections",ct);}
 private static Input Intent(DeviceProtectedEraseRecord r)=>new(r.Id,r.DeviceId,r.ArchiveId,r.DeviceVersion,r.ArchiveVersion,r.QuiescedAtUtc,r.EvidenceReference,JsonSerializer.Deserialize<ProtectedEraseApproval>(r.ApprovalJson)!,r.Person,r.RecoveryOfId);
 private sealed record TargetData(DeviceRecord Device,int Count,string Digest,ZkUser? Expected);
 private async Task<TargetData> Target(Guid actor,Input i,Guid? self,CancellationToken ct)
 {
  if(!Approved(i.Approval,i.RecoveryOfId!=null))throw new DomainConflictException("Independent exact installed family approval is default-off.");
  if(!await tx.HasDevicePermissionAsync(actor,Right(i.Approval.Family,i.RecoveryOfId is null?"propose":"recover.propose"),i.DeviceId,ct)||!await tx.HasDevicePermissionAsync(actor,DeviceUserDownloads.Read,i.DeviceId,ct))throw new UnauthorizedAccessException();
  var d=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.DeviceId,ct)??throw new ArgumentException("Unknown device.");
  var a=await db.DeviceUserDownloads.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.ArchiveId,ct)??throw new ArgumentException("Unknown user archive.");var now=clock.GetUtcNow();
  if(!d.Enabled||d.Profile!="zk-standalone"||d.Host is null||d.Port is null||!d.Version.SequenceEqual(i.DeviceVersion)||a.DeviceId!=d.Id||a.SourceId!=d.SourceId||!await db.Sites.AnyAsync(x=>x.Id==d.SiteId&&x.CompanyId==a.CompanyId,ct)||a.State!="succeeded"||a.ReadFormat!=i.Approval.Inventory.UserFormat||!a.ExpectedDeviceVersion.SequenceEqual(i.DeviceVersion)||!a.Version.SequenceEqual(i.ArchiveVersion)||a.ReceivedCount>i.Approval.Inventory.MaximumUsers||a.FinishedAtUtc is null||a.FinishedAtUtc<now.AddMinutes(-15)||a.FinishedAtUtc>now||a.RequestedAtUtc<i.QuiescedAtUtc.AddSeconds(10)||i.QuiescedAtUtc<now.AddMinutes(-30))throw new DomainConflictException("Fresh complete same-installation metadata archive after settled exclusive control required.");
  var raw=await db.DeviceDownloadedUsers.AsNoTracking().Where(x=>x.DownloadId==a.Id).OrderBy(x=>x.Sequence).Take(i.Approval.Inventory.MaximumUsers+1).ToListAsync(ct);
  if(raw.Count!=a.ReceivedCount||raw.Where((x,n)=>x.Sequence!=n+1).Any())throw new DomainConflictException("Complete contiguous metadata archive required.");
  var users=raw.Select(x=>new ZkUser(x.PersonIdentifier,x.Name,x.Card,x.Privilege,x.Enabled)).ToArray();
  if(users.Any(x=>!x.IsValid()||x.Card is null||!x.PersonIdentifier.All(char.IsAsciiDigit))||users.Select(x=>x.PersonIdentifier).Distinct(StringComparer.Ordinal).Count()!=users.Length)throw new DomainConflictException("Exact unambiguous protected-profile metadata required.");
  var expected=users.SingleOrDefault(x=>x.PersonIdentifier==i.Person);
  if(i.Approval.Family!="administrators"&&i.RecoveryOfId is null&&(expected is null||expected.Privilege!=0))throw new DomainConflictException("Exact archived ordinary target required; administrators have a separate family.");
  if(i.RecoveryOfId is{} parent)
  {
   var p=await db.DeviceProtectedErases.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==parent,ct);
   if(p is null||p.State!="unknown"||p.RecoveryOfId!=null||p.DeviceId!=d.Id||p.Person!=i.Person||p.Family!=i.Approval.Family||JsonSerializer.Deserialize<ProtectedEraseApproval>(p.ApprovalJson)!=i.Approval||!p.DeviceVersion.SequenceEqual(i.DeviceVersion)||p.FinishedAtUtc is null||p.FinishedAtUtc>i.QuiescedAtUtc||await db.DeviceProtectedErases.AnyAsync(x=>x.RecoveryOfId==parent&&x.State=="released",ct))throw new DomainConflictException("Exact unresolved original unknown, later reset and fresh metadata required.");
  }
  if(await Busy(i.DeviceId,self,ct)||await DeviceWriteSafety.UnreviewedAsync(db,i.DeviceId,ct,ignoredProtected:i.RecoveryOfId))throw new DomainConflictException("Pause polling; finish all work and recover other unknown writes first.");
  return new(d,users.Length,UserMetadataCommitment.Create(users),expected);
 }
 public Task<object> PreviewAsync(Guid actor,Input input,string family,CancellationToken ct=default)
 {
  input=Freeze(input,family);return tx.ExecuteAsync<object>(actor,Right(family,input.RecoveryOfId is null?"propose":"recover.propose"),"protected-erase.previewed",async t=>{await Lock(t);var d=await Target(actor,input,null,t);return(new{previewHash=Hash(new{input,d.Count,d.Digest,d.Expected}),archiveCount=d.Count,expected=d.Expected,family,readOnlyRecovery=input.RecoveryOfId!=null,confirmation=Confirmation(family,input.RecoveryOfId!=null)},input.Id);},ct);
 }
 public Task<Guid> ProposeAsync(Guid actor,Confirm c,string family,CancellationToken ct=default)
 {
  if(c is null||!AttendanceArchiveDigest.Valid(c.PreviewHash))throw new ArgumentException("Fresh exact preview required.");c=c with{Intent=Freeze(c.Intent,family)};var i=c.Intent;
  if(c.Confirmation!=Confirmation(family,i.RecoveryOfId!=null))throw new ArgumentException("Exact family confirmation required.");
  return tx.ExecuteAsync(actor,Right(family,i.RecoveryOfId is null?"propose":"recover.propose"),"protected-erase.proposed",async t=>
  {
   await Lock(t);var old=await db.DeviceProtectedErases.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.Id,t);if(old is not null){if(old.RequestedByUserId!=actor||old.RequestHash!=Hash(c))throw new DomainConflictException("Changed retry.");return(old.Id,old.Id);}
   var d=await Target(actor,i,null,t);if(c.PreviewHash!=Hash(new{input=i,d.Count,d.Digest,d.Expected}))throw new DomainConflictException("Intent/archive changed; preview again.");
   db.DeviceProtectedErases.Add(new(){Id=i.Id,DeviceId=i.DeviceId,ArchiveId=i.ArchiveId,RecoveryOfId=i.RecoveryOfId,DeviceVersion=i.DeviceVersion,ArchiveVersion=i.ArchiveVersion,Family=family,Person=i.Person,ExpectedJson=d.Expected is null?null:JsonSerializer.Serialize(d.Expected),RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),QuiescedAtUtc=i.QuiescedAtUtc,EvidenceReference=i.EvidenceReference,ApprovalJson=JsonSerializer.Serialize(i.Approval),ArchiveCount=d.Count,ArchiveDigest=d.Digest,RequestHash=Hash(c),State=i.RecoveryOfId is null?"proposed":"pending"});return(i.Id,i.Id);
  },ct);
 }
 public async Task<Guid> ApproveAsync(Guid actor,Guid id,Review review,string family,CancellationToken ct=default)
 {
  if(review is null||review.Version is not{Length:8}||!Text(review.EvidenceReference))throw new ArgumentException("Version and evidence required.");review=review with{Version=review.Version.ToArray()};
  var recovery=await db.DeviceProtectedErases.AsNoTracking().Where(x=>x.Id==id&&x.Family==family).Select(x=>x.RecoveryOfId!=null).SingleOrDefaultAsync(ct);
  return await tx.ExecuteAsync(actor,Right(family,recovery?"recover.approve":"approve"),"protected-erase.approved",async t=>
  {
   await Lock(t);var r=await db.DeviceProtectedErases.AsTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.Family==family,t)??throw new ArgumentException("Unknown family proposal.");
   if(r.RequestedByUserId==actor||!r.Version.SequenceEqual(review.Version)||review.Confirmation!=Confirmation(family,recovery)||r.State!=(recovery?"observed":"proposed")||(recovery?r.FinishedAtUtc<clock.GetUtcNow().AddMinutes(-2):r.RequestedAtUtc<clock.GetUtcNow().AddMinutes(-2)))throw new DomainConflictException("Independent fresh review required; recovery must first finish read-only native observation.");
   if(!await tx.HasDevicePermissionAsync(actor,Right(family,recovery?"recover.approve":"approve"),r.DeviceId,t)||!await tx.HasDevicePermissionAsync(actor,DeviceUserDownloads.Read,r.DeviceId,t))throw new UnauthorizedAccessException();
   _=await Target(r.RequestedByUserId,Intent(r),r.Id,t);r.ApprovedByUserId=actor;r.ApprovedAtUtc=clock.GetUtcNow();r.ReviewReference=review.EvidenceReference;r.State=recovery?"released":"pending";return(r.Id,r.Id);
  },ct);
 }
 public Task<object> ListAsync(Guid actor,string family,CancellationToken ct=default)=>tx.ExecuteAsync<object>(actor,Right(family,"read"),"protected-erase.viewed",async t=>{var items=await db.DeviceProtectedErases.AsNoTracking().Where(x=>x.Family==family).OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Take(50).Select(x=>new{x.Id,x.DeviceId,x.ArchiveId,x.RecoveryOfId,x.Family,x.Person,x.ArchiveCount,x.State,x.RequestedByUserId,x.RequestedAtUtc,x.QuiescedAtUtc,x.EvidenceReference,x.ApprovedByUserId,x.ApprovedAtUtc,x.ReviewReference,x.FinishedAtUtc,x.Version}).ToListAsync(t);return(new{items},actor);},ct);
 public Task<Guid> CancelAsync(Guid actor,Guid id,byte[] version,string family,CancellationToken ct=default)=>tx.ExecuteEitherAsync(actor,Right(family,"propose"),Right(family,"recover.propose"),"protected-erase.cancelled",async t=>
 {
  await Lock(t);var r=await db.DeviceProtectedErases.AsTracking().SingleOrDefaultAsync(x=>x.Id==id&&x.Family==family,t)??throw new ArgumentException("Unknown family proposal.");
  if(!await tx.HasDevicePermissionAsync(actor,Right(family,r.RecoveryOfId is null?"propose":"recover.propose"),r.DeviceId,t))throw new UnauthorizedAccessException();
  if(version is not{Length:8}||!r.Version.SequenceEqual(version))throw new DomainConflictException("Refresh version.");
  if(r.State is "proposed" or "pending"){r.State="cancelled";r.FinishedAtUtc=clock.GetUtcNow();}else if(r.State=="running")r.CancelRequestedAtUtc??=clock.GetUtcNow();else throw new DomainConflictException("No terminal replay.");return(r.Id,r.Id);
 },ct);
 public Task<Claim?> ClaimAsync(Guid service,string family,CancellationToken ct=default)=>tx.ExecuteAsync<Claim?>(service,Right(family,"execute"),"protected-erase.claimed",async t=>
 {
  await Lock(t);var now=clock.GetUtcNow();foreach(var r in await db.DeviceProtectedErases.AsTracking().Where(x=>x.Family==family&&((x.State=="running"&&x.ClaimedAtUtc<now.AddMinutes(-2))||(x.State=="proposed"&&x.RequestedAtUtc<now.AddMinutes(-2)))).ToListAsync(t)){r.State=r.State=="running"?(r.RecoveryOfId is null?"unknown":"inspection-failed"):"expired";r.FinishedAtUtc=now;}
  var row=await db.DeviceProtectedErases.AsTracking().Where(x=>x.Family==family&&x.State=="pending").OrderBy(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(t);if(row is null)return(null,service);
  var d=await Current(row,t);if(d is null){row.State="revoked";row.FinishedAtUtc=now;return(null,row.Id);}
  row.State="running";row.ClaimId=Guid.NewGuid();row.ServiceUserId=service;row.ClaimedAtUtc=now;
  return(new Claim(row.Id,row.ClaimId.Value,row.DeviceId,d.Host!,d.Port!.Value,Intent(row).Approval,row.ExpectedJson is null?null:JsonSerializer.Deserialize<ZkUser>(row.ExpectedJson),row.ArchiveCount,row.ArchiveDigest,row.RecoveryOfId!=null),row.Id);
 },ct);
 private async Task<DeviceRecord?> Current(DeviceProtectedEraseRecord r,CancellationToken ct)
 {
  try
  {
   if(r.CancelRequestedAtUtc!=null||r.RequestedAtUtc<clock.GetUtcNow().AddMinutes(-2)||r.RecoveryOfId is null&&(r.ApprovedByUserId is null||!await tx.HasDevicePermissionAsync(r.ApprovedByUserId.Value,Right(r.Family,"approve"),r.DeviceId,ct)||!await tx.HasDevicePermissionAsync(r.ApprovedByUserId.Value,DeviceUserDownloads.Read,r.DeviceId,ct)))return null;
   var d=await Target(r.RequestedByUserId,Intent(r),r.Id,ct);return d.Count==r.ArchiveCount&&d.Digest==r.ArchiveDigest?d.Device:null;
  }
  catch(Exception e)when(e is ArgumentException or DomainConflictException or UnauthorizedAccessException){return null;}
 }
 private async Task<DeviceProtectedEraseRecord> Owned(Guid service,Claim c,CancellationToken t)
 {
  var r=await db.DeviceProtectedErases.AsTracking().SingleOrDefaultAsync(x=>x.Id==c.Id,t)??throw new ArgumentException("Unknown claim.");
  if(r.State!="running"||r.ClaimId!=c.Token||r.ServiceUserId!=service||r.ClaimedAtUtc<clock.GetUtcNow().AddMinutes(-2)||r.DeviceId!=c.DeviceId||r.Family!=c.Approval.Family||Intent(r).Approval!=c.Approval||r.ArchiveCount!=c.Count||r.ArchiveDigest!=c.Digest||(r.RecoveryOfId!=null)!=c.InspectOnly||(r.ExpectedJson is null?null:JsonSerializer.Deserialize<ZkUser>(r.ExpectedJson))!=c.Expected)throw new DomainConflictException("Foreign/stale claim.");return r;
 }
 public Task<DeviceCommunicationKey?> CredentialAsync(Guid service,Claim c,bool allowDefault,CancellationToken ct=default)=>tx.ExecuteAsync<DeviceCommunicationKey?>(service,Right(c.Approval.Family,"execute"),"protected-erase.credential-used",async t=>
 {
  await Lock(t);var r=await Owned(service,c,t);var d=await Current(r,t);if(d is null||d.Host!=c.Host||d.Port!=c.Port)return(null,r.Id);
  var cipher=await db.DeviceSecrets.Where(x=>x.DeviceId==r.DeviceId).Select(x=>x.ProtectedValue).SingleOrDefaultAsync(t);if(cipher is null)return(allowDefault?new(0):null,r.Id);
  try{var s=protection.CreateProtector("IO.Devices.Credential.v1",r.DeviceId.ToString("N")).Unprotect(cipher);return(s.Length>0&&s.All(char.IsAsciiDigit)&&int.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out var n)?new(n):null,r.Id);}catch(CryptographicException){return(null,r.Id);}
 },ct);
 public Task<bool> ShouldStopAsync(Guid service,Claim c,CancellationToken ct=default)=>tx.ExecuteAsync(service,Right(c.Approval.Family,"execute"),"protected-erase.authority-checked",async t=>{await Lock(t);var r=await Owned(service,c,t);var d=await Current(r,t);return(d is null||d.Host!=c.Host||d.Port!=c.Port,r.Id);},ct);
 public Task<Guid> CompleteAsync(Guid service,Claim c,ProtectedEraseOutput output,CancellationToken ct=default)=>tx.ExecuteAsync(service,Right(c.Approval.Family,"execute"),"protected-erase.finished",async t=>
 {
  await Lock(t);var r=await Owned(service,c,t);var input=new ProtectedEraseInput(new(c.DeviceId,c.Host,c.Port,1,c.Approval.Inventory.Device.Serial,0),c.Id,c.Approval,c.Expected,c.Count,c.Digest,c.InspectOnly);
  if(!output.IsValidFor(input))throw new ArgumentException("Correlated exact family result required.");var d=await Current(r,t);r.State=d is null||d.Host!=c.Host||d.Port!=c.Port?(c.InspectOnly?"inspection-failed":"unknown"):output.State;r.FinishedAtUtc=clock.GetUtcNow();return(r.Id,r.Id);
 },ct);
 private async Task<bool> Busy(Guid device,Guid? self,CancellationToken t)=>await db.DeviceProtectedErases.IgnoreQueryFilters().AnyAsync(x=>x.DeviceId==device&&x.Id!=self&&(x.State=="proposed"||x.State=="pending"||x.State=="running"||x.State=="observed"),t)||await db.DeviceAttendanceErases.IgnoreQueryFilters().AnyAsync(x=>x.DeviceId==device&&(x.State=="proposed"||x.State=="pending"||x.State=="running"),t)||await db.DeviceBiometricTransfers.IgnoreQueryFilters().AnyAsync(x=>(x.SourceDeviceId==device||x.TargetDeviceId==device)&&(x.State=="pending"||x.State=="running"),t)||await db.RemoteEnrollments.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),t)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),t)||await db.DeviceConnections.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),t)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"||x.State=="unknown"&&x.ResolutionSnapshotId==null),t)||await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),t)||await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==device&&(x.State=="pending"||x.State=="running"),t)||await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==device&&x.Enabled,t);
}
internal static class ProtectedEraseFence
{
 internal static async Task EnsureAsync(IoDbContext db,Guid device,bool writing,CancellationToken ct)
 {
  if(await db.DeviceProtectedErases.IgnoreQueryFilters().AnyAsync(x=>x.DeviceId==device&&(x.State=="proposed"||x.State=="pending"||x.State=="running"||x.State=="observed"||writing&&x.State=="unknown"&&!db.DeviceProtectedErases.IgnoreQueryFilters().Any(r=>r.RecoveryOfId==x.Id&&r.State=="released")),ct))throw new DomainConflictException("Protected erase owns endpoint or unresolved unknown; no replay.");
 }
}
