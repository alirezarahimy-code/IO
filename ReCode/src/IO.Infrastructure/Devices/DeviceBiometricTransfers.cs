using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace IO.Infrastructure.Devices;

/// <summary>Direct device-to-device transient exchange. Web/SQL/worker never receive template material. Photo requires private bounded EFS staging in the child.</summary>
public sealed class DeviceBiometricTransfers(IoDbContext db,PermissionTransaction tx,TimeProvider clock,IDataProtectionProvider protection,IConfiguration config)
{
 public const string Read="devices.biometrics.jobs.read",Transfer="devices.biometrics.transfer",ReadMaterial="devices.biometrics.source.read",Inspect="devices.biometrics.inspect",Execute="devices.biometrics.execute",Confirmation="COPY-BIOMETRIC-TO-EMPTY-SLOT";
 public sealed record Input(Guid Id,Guid SourceDeviceId,Guid TargetDeviceId,Guid EmployeeId,byte[] SourceDeviceVersion,byte[] TargetDeviceVersion,string SourcePerson,string TargetPerson,BiometricTransferApproval Approval,int TimeoutSeconds,[property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]Guid? InspectionOfId=null)
 {public override string ToString()=>"Biometric intent [subjects and policy redacted]";}
 public sealed record Confirm(Input Intent,string PreviewHash,string Confirmation);
 public sealed record Claim(Guid Id,Guid Token,Input Intent,string SourceHost,int SourcePort,string TargetHost,int TargetPort)
 {public override string ToString()=>"Biometric claim [private metadata redacted]";}
 public sealed record Keys(DeviceCommunicationKey Source,DeviceCommunicationKey Target)
 {public override string ToString()=>"Biometric endpoint credentials [redacted]";}
 private static string Hash(object o)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(o)));
 private static void Validate(Input i)
 {
  if(i is null||i.Id==Guid.Empty||i.SourceDeviceId==Guid.Empty||i.TargetDeviceId==Guid.Empty||i.SourceDeviceId==i.TargetDeviceId||i.EmployeeId==Guid.Empty||i.SourceDeviceVersion is not{Length:8}||i.TargetDeviceVersion is not{Length:8}||!Person(i.SourcePerson)||!Person(i.TargetPerson)||(i.Approval?.Kind=="photo"&&(i.SourcePerson.Length>20||i.TargetPerson.Length>20))||i.Approval?.IsValid()!=true||i.TimeoutSeconds is <1 or >60)throw new ArgumentException("Exact pair, employee, versions, supported format and policy required; photo requires exact storage approval and bounded decimal file identity.");
 }
 private static bool Person(string? s)=>s is{Length:>=1 and <=100}&&s.All(char.IsAsciiDigit);
 private static Input Freeze(Input i){Validate(i);return i with{SourceDeviceVersion=i.SourceDeviceVersion.ToArray(),TargetDeviceVersion=i.TargetDeviceVersion.ToArray()};}
 private bool Approved(BiometricTransferApproval a)=>a.IsValid()&&config.GetValue<bool>("IO:Biometrics:EnableTransfers")&&(config.GetSection("IO:Biometrics:Approvals").Get<BiometricTransferApproval[]>()??[]).Count(x=>x==a)==1;
 private async Task Authority(Guid actor,Input i,CancellationToken ct)
 {
  if(!Approved(i.Approval))throw new DomainConflictException("Biometric transfer is default-off; trusted exact-pair format and legal/retention/exclusive-control approval required.");
  if(!await tx.HasDevicePermissionAsync(actor,i.InspectionOfId is null?Transfer:Inspect,i.TargetDeviceId,ct)||!await tx.HasDevicePermissionAsync(actor,ReadMaterial,i.SourceDeviceId,ct)||!await tx.HasDevicePermissionAsync(actor,i.InspectionOfId is null?Transfer:Inspect,i.SourceDeviceId,ct)||!await tx.HasDevicePermissionAsync(actor,ReadMaterial,i.TargetDeviceId,ct))throw new UnauthorizedAccessException();
 }
 private sealed record Plan(DeviceRecord Source,DeviceRecord Target,SourceEmployeeMappingRecord SourceMapping,SourceEmployeeMappingRecord TargetMapping,string Hash);
 private async Task<Plan> Build(Guid actor,Input i,bool ownJob,CancellationToken ct)
 {
  await Authority(actor,i,ct);
  var source=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.SourceDeviceId,ct)??throw new ArgumentException("Unknown or inaccessible source.");
  var target=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.TargetDeviceId,ct)??throw new ArgumentException("Unknown or inaccessible target.");
  if(!Valid(source,i.SourceDeviceVersion)||!Valid(target,i.TargetDeviceVersion)||source.Host==target.Host&&source.Port==target.Port)throw new DomainConflictException("Changed, disabled, unsupported or aliased endpoints.");
  foreach(var d in new[]{source,target})
  {
   await AttendanceEraseFence.EnsureAsync(db,d.Id,true,ct);
   if(await Busy(d.Id,ownJob?i.Id:Guid.Empty,ct,i.InspectionOfId)||await DeviceWriteSafety.UnreviewedAsync(db,d.Id,ct,i.InspectionOfId))throw new DomainConflictException("Pause polling and finish/review all work on both endpoints first.");
  }
  if(i.InspectionOfId is{} parentId)
  {
   var parent=await db.DeviceBiometricTransfers.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==parentId,ct);
   if(parent is null||parent.State!="unknown"||parent.InspectionOfId!=null||parent.SourceDeviceId!=i.SourceDeviceId||parent.TargetDeviceId!=i.TargetDeviceId||parent.EmployeeId!=i.EmployeeId||parent.SourcePerson!=i.SourcePerson||parent.TargetPerson!=i.TargetPerson||JsonSerializer.Deserialize<BiometricTransferApproval>(parent.ApprovalJson)!=i.Approval||!parent.SourceDeviceVersion.SequenceEqual(i.SourceDeviceVersion)||!parent.TargetDeviceVersion.SequenceEqual(i.TargetDeviceVersion)||await db.DeviceBiometricRecoveries.AnyAsync(x=>x.TransferId==parentId&&x.ApprovedAtUtc!=null,ct))throw new DomainConflictException("Inspection must preserve an unresolved original unknown target exactly.");
   if(ownJob)
   {
    var recovery=await db.DeviceBiometricRecoveries.AsNoTracking().SingleOrDefaultAsync(x=>x.InspectionId==i.Id,ct);
    if(recovery is null||recovery.ProposedByUserId!=actor||!DeviceBiometricRecovery.Approved(config,JsonSerializer.Deserialize<BiometricRecoveryApproval>(recovery.ApprovalJson)!)||!await tx.HasDevicePermissionAsync(actor,DeviceBiometricRecovery.Propose,i.SourceDeviceId,ct)||!await tx.HasDevicePermissionAsync(actor,DeviceBiometricRecovery.Propose,i.TargetDeviceId,ct))throw new DomainConflictException("Recovery authority changed; read-only inspection revoked.");
   }
  }
  var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.EmployeeId,ct)??throw new ArgumentException("Unknown employee.");
  var sourceSite=await db.Sites.AsNoTracking().SingleAsync(x=>x.Id==source.SiteId,ct);var targetSite=await db.Sites.AsNoTracking().SingleAsync(x=>x.Id==target.SiteId,ct);
  if(sourceSite.CompanyId!=employee.CompanyId||targetSite.CompanyId!=employee.CompanyId)throw new DomainConflictException("Cross-company biometric exchange is not supported.");
  var sm=await Mapping(source.SourceId,i.SourcePerson,i.EmployeeId,employee.CompanyId,ct);var tm=await Mapping(target.SourceId,i.TargetPerson,i.EmployeeId,employee.CompanyId,ct);
  return new(source,target,sm,tm,Hash(new{intent=i,source.SourceId,sourceSite=source.SiteId,targetSourceId=target.SourceId,targetSite=target.SiteId,employee.CompanyId,sourceMapping=sm.Id,sourceMappingVersion=sm.Version,targetMapping=tm.Id,targetMappingVersion=tm.Version}));
 }
 private static bool Valid(DeviceRecord d,byte[] version)=>d.Enabled&&d.Profile=="zk-standalone"&&d.Host is not null&&d.Port is not null&&d.Version.SequenceEqual(version);
 private async Task<SourceEmployeeMappingRecord> Mapping(Guid source,string person,Guid employee,Guid company,CancellationToken ct)
 {
  var identity=Encoding.Unicode.GetBytes(person);var now=clock.GetUtcNow();
  var rows=await db.SourceEmployeeMappings.AsNoTracking().Where(x=>x.SourceId==source&&EF.Property<byte[]>(x,"SourcePersonIdBytes")==identity&&EF.Property<int>(x,"SourcePersonIdByteLength")==identity.Length&&x.ValidFromUtc<=now&&(x.ValidUntilUtcExclusive==null||now<x.ValidUntilUtcExclusive)).Take(2).ToListAsync(ct);
  if(rows.Count!=1||rows[0].EmployeeId!=employee||rows[0].CompanyId!=company)throw new DomainConflictException("Both exact current mappings must identify the same employee and company.");
  return rows[0];
 }
 private async Task Lock(CancellationToken ct){await tx.LockAsync("IO.DeviceRegistry",ct);await tx.LockAsync("IO.DeviceConnections",ct);}
 public Task<object> PreviewAsync(Guid actor,Input input,CancellationToken ct=default)
 {
  input=Freeze(input);if(input.InspectionOfId!=null)throw new ArgumentException("Use recovery proposal to create read-only inspection.");return tx.ExecuteAsync<object>(actor,Transfer,"device-biometric.previewed",async token=>
  {await Lock(token);var p=await Build(actor,input,false,token);return(new{previewHash=p.Hash,input.SourceDeviceId,input.TargetDeviceId,input.EmployeeId,input.SourcePerson,input.TargetPerson,input.Approval.Kind,input.Approval.Slot,input.Approval.FormatReference,noOverwrite=true,materialReturned=false,confirmation=Confirmation},input.Id);},ct);
 }
 public Task<Guid> RequestAsync(Guid actor,Confirm input,CancellationToken ct=default)
 {
  if(input is null||input.Confirmation!=Confirmation||input.PreviewHash is not{Length:64})throw new ArgumentException("Exact preview and confirmation required.");
  input=input with{Intent=Freeze(input.Intent)};if(input.Intent.InspectionOfId!=null)throw new ArgumentException("Use recovery proposal for inspection.");
  return tx.ExecuteAsync(actor,Transfer,"device-biometric.requested",async token=>
  {
   await Lock(token);var i=input.Intent;await Authority(actor,i,token);var requestHash=Hash(input);
   var old=await db.DeviceBiometricTransfers.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.Id,token);
   if(old is not null){if(old.RequestedByUserId!=actor||old.RequestHash!=requestHash)throw new DomainConflictException("Changed biometric retry.");return(old.Id,old.Id);}
   var p=await Build(actor,i,false,token);if(p.Hash!=input.PreviewHash)throw new DomainConflictException("Mappings or installation changed; preview again.");
   db.DeviceBiometricTransfers.Add(new(){Id=i.Id,SourceDeviceId=i.SourceDeviceId,TargetDeviceId=i.TargetDeviceId,EmployeeId=i.EmployeeId,RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),SourceDeviceVersion=i.SourceDeviceVersion,TargetDeviceVersion=i.TargetDeviceVersion,SourcePerson=i.SourcePerson,TargetPerson=i.TargetPerson,ApprovalJson=JsonSerializer.Serialize(i.Approval),TimeoutSeconds=i.TimeoutSeconds,RequestHash=requestHash});
   return(i.Id,i.Id);
  },ct);
 }
 public Task<object> ListAsync(Guid actor,int offset,CancellationToken ct=default)=>tx.ExecuteAsync<object>(actor,Read,"device-biometric.viewed",async token=>
 {
  if(offset is <0 or >100000)throw new ArgumentException("Invalid page.");
  var items=await db.DeviceBiometricTransfers.AsNoTracking().OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50).Select(x=>new{x.Id,x.SourceDeviceId,x.TargetDeviceId,x.InspectionOfId,x.EmployeeId,x.SourcePerson,x.TargetPerson,x.State,x.RequestedAtUtc,x.ClaimedAtUtc,x.CancelRequestedAtUtc,x.FinishedAtUtc,x.Version}).ToListAsync(token);
  return(new{items,offset,pageSize=50,templateMaterialAvailable=false},actor);
 },ct);
 public async Task<Guid> CancelAsync(Guid actor,Guid id,byte[] version,CancellationToken ct=default)
 {
 var inspection=await db.DeviceBiometricTransfers.AsNoTracking().Where(x=>x.Id==id).Select(x=>x.InspectionOfId!=null).SingleOrDefaultAsync(ct);
 return await tx.ExecuteAsync(actor,inspection?Inspect:Transfer,"device-biometric.cancel-requested",async token=>
 {
  await Lock(token);var row=await db.DeviceBiometricTransfers.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown or inaccessible transfer.");
  if(version is not{Length:8}||!version.SequenceEqual(row.Version))throw new DomainConflictException("Refresh version.");
  if(row.State=="pending"){row.State="cancelled-before-dispatch";row.FinishedAtUtc=clock.GetUtcNow();}
  else if(row.State=="running")row.CancelRequestedAtUtc??=clock.GetUtcNow();else throw new DomainConflictException("Terminal result; never replay an unknown transfer.");
  return(row.Id,row.Id);
 },ct);
 }
 public Task<Claim?> ClaimAsync(Guid service,CancellationToken ct=default)=>tx.ExecuteAsync<Claim?>(service,Execute,"device-biometric.claimed",async token=>
 {
  await Lock(token);var now=clock.GetUtcNow();
  foreach(var stale in await db.DeviceBiometricTransfers.AsTracking().Where(x=>x.State=="running"&&x.ClaimedAtUtc<now.AddMinutes(-2)).ToListAsync(token)){stale.State=stale.InspectionOfId is null?"unknown":"inspection-failed";stale.FinishedAtUtc=now;}
  var row=await db.DeviceBiometricTransfers.AsTracking().Where(x=>x.State=="pending").OrderBy(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);if(row is null)return(null,service);
  Plan p;try{if(now-row.RequestedAtUtc>TimeSpan.FromMinutes(2))throw new DomainConflictException("Expired intent.");p=await Build(row.RequestedByUserId,Intent(row),true,token);}
  catch(Exception e)when(e is DomainConflictException or UnauthorizedAccessException or ArgumentException){row.State="revoked-before-dispatch";row.FinishedAtUtc=now;return(null,row.Id);}
  row.State="running";row.ClaimId=Guid.NewGuid();row.ServiceUserId=service;row.ClaimedAtUtc=now;
  return(new Claim(row.Id,row.ClaimId.Value,Intent(row),p.Source.Host!,p.Source.Port!.Value,p.Target.Host!,p.Target.Port!.Value),row.Id);
 },ct);
 private static Input Intent(DeviceBiometricTransferRecord r)=>new(r.Id,r.SourceDeviceId,r.TargetDeviceId,r.EmployeeId,r.SourceDeviceVersion,r.TargetDeviceVersion,r.SourcePerson,r.TargetPerson,JsonSerializer.Deserialize<BiometricTransferApproval>(r.ApprovalJson)!,r.TimeoutSeconds,r.InspectionOfId);
 private async Task<DeviceBiometricTransferRecord> Owned(Guid service,Claim claim,CancellationToken ct)
 {
  var row=await db.DeviceBiometricTransfers.AsTracking().SingleOrDefaultAsync(x=>x.Id==claim.Id,ct)??throw new ArgumentException("Unknown transfer.");
  if(row.State!="running"||row.ClaimId!=claim.Token||row.ServiceUserId!=service||row.ClaimedAtUtc<clock.GetUtcNow().AddMinutes(-2)||Hash(Intent(row))!=Hash(claim.Intent))throw new DomainConflictException("Expired or changed biometric claim.");
  return row;
 }
 private async Task<bool> Current(DeviceBiometricTransferRecord row,Claim claim,CancellationToken ct)
 {
  if(row.CancelRequestedAtUtc!=null||clock.GetUtcNow()-row.RequestedAtUtc>TimeSpan.FromMinutes(2))return false;
  try{var p=await Build(row.RequestedByUserId,Intent(row),true,ct);return p.Source.Host==claim.SourceHost&&p.Source.Port==claim.SourcePort&&p.Target.Host==claim.TargetHost&&p.Target.Port==claim.TargetPort;}
  catch(Exception e)when(e is DomainConflictException or UnauthorizedAccessException or ArgumentException){return false;}
 }
 public Task<bool> ShouldStopAsync(Guid service,Claim claim,CancellationToken ct=default)=>tx.ExecuteAsync(service,Execute,"device-biometric.authority-checked",async token=>
 {await Lock(token);var row=await Owned(service,claim,token);return(!await Current(row,claim,token),row.Id);},ct);
 public Task<Keys?> CredentialAsync(Guid service,Claim claim,bool allowSourceDefault,bool allowTargetDefault,CancellationToken ct=default)=>tx.ExecuteAsync<Keys?>(service,Execute,"device-biometric.credentials-used",async token=>
 {
  await Lock(token);var row=await Owned(service,claim,token);if(!await Current(row,claim,token))return(null,row.Id);
  var source=await Key(row.SourceDeviceId,allowSourceDefault,token);var target=await Key(row.TargetDeviceId,allowTargetDefault,token);
  return(source is not null&&target is not null?new Keys(source,target):null,row.Id);
 },ct);
 private async Task<DeviceCommunicationKey?> Key(Guid device,bool allowDefault,CancellationToken ct)
 {
  var cipher=await db.DeviceSecrets.Where(x=>x.DeviceId==device).Select(x=>x.ProtectedValue).SingleOrDefaultAsync(ct);if(cipher is null)return allowDefault?new(0):null;
  try{var s=protection.CreateProtector("IO.Devices.Credential.v1",device.ToString("N")).Unprotect(cipher);return s.Length>0&&s.All(char.IsAsciiDigit)&&int.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out var key)?new(key):null;}catch(CryptographicException){return null;}
 }
 public Task<Guid> CompleteAsync(Guid service,Claim claim,BiometricTransferOutput result,CancellationToken ct=default)=>tx.ExecuteAsync(service,Execute,"device-biometric.finished",async token=>
 {
  await Lock(token);var row=await Owned(service,claim,token);if(result is null||result.OperationId!=row.Id||!(row.InspectionOfId is null?BiometricTransferOutput.Known(result.State):BiometricTransferOutput.Observation(result.State)))throw new ArgumentException("Invalid correlated metadata-only result.");
  row.State=await Current(row,claim,token)?result.State:row.InspectionOfId is null?"unknown":"inspection-failed";row.FinishedAtUtc=clock.GetUtcNow();return(row.Id,row.Id);
 },ct);
 internal async Task<Guid> EnqueueInspectionInTransactionAsync(Guid actor,DeviceBiometricTransferRecord parent,Guid id,CancellationToken ct)
 {
  var input=Intent(parent) with{Id=id,InspectionOfId=parent.Id};Validate(input);
  if(id==Guid.Empty||await db.DeviceBiometricTransfers.AnyAsync(x=>x.Id==id,ct))throw new DomainConflictException("Inspection id already used.");
  await Build(actor,input,false,ct);
  db.DeviceBiometricTransfers.Add(new(){Id=id,InspectionOfId=parent.Id,SourceDeviceId=parent.SourceDeviceId,TargetDeviceId=parent.TargetDeviceId,EmployeeId=parent.EmployeeId,RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),SourceDeviceVersion=parent.SourceDeviceVersion.ToArray(),TargetDeviceVersion=parent.TargetDeviceVersion.ToArray(),SourcePerson=parent.SourcePerson,TargetPerson=parent.TargetPerson,ApprovalJson=parent.ApprovalJson,RequestHash=Hash(new{parent.Id,inspectionId=id}),TimeoutSeconds=parent.TimeoutSeconds});
  return id;
 }
 internal async Task ValidateRecoveryTargetInTransactionAsync(Guid actor,DeviceBiometricTransferRecord parent,CancellationToken ct)
 {
  // Apply the same current pair/mapping/authority checks without creating a job or reading any material.
  var input=Intent(parent) with{Id=Guid.NewGuid(),InspectionOfId=parent.Id};await Build(actor,input,false,ct);
 }
 private async Task<bool> Busy(Guid d,Guid self,CancellationToken ct,Guid? ignoredUnknown=null)=>
  await db.DeviceBiometricTransfers.IgnoreQueryFilters().AnyAsync(x=>x.Id!=self&&(x.SourceDeviceId==d||x.TargetDeviceId==d)&&(x.State=="pending"||x.State=="running"||x.TargetDeviceId==d&&x.State=="unknown"&&x.Id!=ignoredUnknown&&!db.DeviceBiometricRecoveries.IgnoreQueryFilters().Any(r=>r.TransferId==x.Id&&r.ApprovedAtUtc!=null)),ct)||
  await db.RemoteEnrollments.AnyAsync(x=>x.DeviceId==d&&(x.State=="pending"||x.State=="running"),ct)||
  await db.DeviceConnections.AnyAsync(x=>x.DeviceId==d&&(x.State=="pending"||x.State=="running"),ct)||
  await db.DeviceOperations.AnyAsync(x=>x.DeviceId==d&&(x.State=="pending"||x.State=="running"),ct)||
  await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==d&&(x.State=="pending"||x.State=="running"),ct)||
  await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==d&&(x.State=="pending"||x.State=="running"||x.State=="unknown"&&x.ResolutionSnapshotId==null),ct)||
  await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==d&&(x.State=="pending"||x.State=="running"),ct)||await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==d&&x.Enabled,ct);
}

internal static class BiometricTransferFence
{
 internal static async Task EnsureAsync(IoDbContext db,Guid device,bool writing,CancellationToken ct)
 {
  // Existence-only safety check must include a pair invisible to an operator restricted to just one endpoint.
  if(await db.DeviceBiometricTransfers.IgnoreQueryFilters().AnyAsync(x=>(x.SourceDeviceId==device||x.TargetDeviceId==device)&&(x.State=="pending"||x.State=="running"||writing&&x.TargetDeviceId==device&&x.State=="unknown"&&!db.DeviceBiometricRecoveries.IgnoreQueryFilters().Any(r=>r.TransferId==x.Id&&r.ApprovedAtUtc!=null)),ct))throw new DomainConflictException("Biometric transfer owns endpoint or target write is unknown; no overwrite/replay.");
 }
}
