using System.Security.Cryptography;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace IO.Infrastructure.Devices;

/// <summary>Physical quiescence attested by two people plus a fresh read-only child observation. Original unknown is never changed or replayed.</summary>
public sealed class DeviceBiometricRecovery(IoDbContext db,PermissionTransaction tx,TimeProvider clock,IConfiguration config,DeviceBiometricTransfers transfers)
{
 public const string Propose="devices.biometrics.recovery.propose",Approve="devices.biometrics.recovery.approve",Confirmation="RECOVER-BIOMETRIC-FENCE-NOT-COPY-SUCCESS";
 public sealed record Input(Guid Id,Guid TransferId,Guid InspectionId,byte[] TransferVersion,BiometricRecoveryApproval Approval,DateTimeOffset ResetAtUtc,string ProcessStoppedEvidence,string ResetEvidence,string Confirmation)
 {public override string ToString()=>"Biometric physical recovery proposal [evidence redacted]";}
 public sealed record Review(byte[] Version,string ReviewEvidence,string Confirmation)
 {public override string ToString()=>"Biometric recovery countersignature [evidence redacted]";}
 internal static bool Approved(IConfiguration config,BiometricRecoveryApproval a)=>a?.IsValid()==true&&config.GetValue<bool>("IO:Biometrics:EnableRecovery")&&(config.GetSection("IO:Biometrics:RecoveryApprovals").Get<BiometricRecoveryApproval[]>()??[]).Count(x=>x==a)==1;
 private static bool Text(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=200&&!s.Any(char.IsControl);
 private async Task Lock(CancellationToken ct){await tx.LockAsync("IO.DeviceRegistry",ct);await tx.LockAsync("IO.DeviceConnections",ct);}
 private async Task<DeviceBiometricTransferRecord> Target(Guid actor,Guid id,BiometricRecoveryApproval approval,DateTimeOffset reset,CancellationToken ct)
 {
  if(!Approved(config,approval))throw new DomainConflictException("Exact non-destructive reset procedure and recovery approval required; default-off.");
  var job=await db.DeviceBiometricTransfers.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown or inaccessible transfer.");
  var now=clock.GetUtcNow();
  if(job.State!="unknown"||job.InspectionOfId!=null||job.FinishedAtUtc is null||reset<job.FinishedAtUtc||reset>now.AddSeconds(-approval.MinimumSettlingSeconds)||reset<now.AddHours(-1)||JsonSerializer.Deserialize<BiometricTransferApproval>(job.ApprovalJson)!=approval.Transfer||await db.DeviceBiometricRecoveries.AnyAsync(x=>x.TransferId==id&&x.ApprovedAtUtc!=null,ct))throw new DomainConflictException("Unresolved unknown, later physical reset and approved settling interval required.");
  await transfers.ValidateRecoveryTargetInTransactionAsync(actor,job,ct);return job;
 }
 public Task<Guid> ProposeAsync(Guid actor,Input input,CancellationToken ct=default)
 {
  if(input is null||input.Id==Guid.Empty||input.TransferId==Guid.Empty||input.InspectionId==Guid.Empty||input.TransferVersion is not{Length:8}||input.Approval?.IsValid()!=true||input.ResetAtUtc.Offset!=TimeSpan.Zero||!Text(input.ProcessStoppedEvidence)||!Text(input.ResetEvidence)||input.Confirmation!=Confirmation)throw new ArgumentException("Exact physical recovery references and immutable transfer version required.");
  input=input with{TransferVersion=input.TransferVersion.ToArray()};
  return tx.ExecuteAsync(actor,Propose,"device-biometric.recovery-proposed",async token=>
  {
   await Lock(token);var hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
   var old=await db.DeviceBiometricRecoveries.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.Id,token);
   if(old is not null){if(old.ProposedByUserId!=actor||old.RequestHash!=hash)throw new DomainConflictException("Changed recovery retry.");return(old.InspectionId,old.Id);}
   var job=await Target(actor,input.TransferId,input.Approval,input.ResetAtUtc,token);
   if(!job.Version.SequenceEqual(input.TransferVersion))throw new DomainConflictException("Refresh transfer version.");
   if(!await tx.HasDevicePermissionAsync(actor,Propose,job.SourceDeviceId,token)||!await tx.HasDevicePermissionAsync(actor,Propose,job.TargetDeviceId,token))throw new UnauthorizedAccessException();
   await transfers.EnqueueInspectionInTransactionAsync(actor,job,input.InspectionId,token);
   db.DeviceBiometricRecoveries.Add(new(){Id=input.Id,TransferId=job.Id,InspectionId=input.InspectionId,SourceDeviceId=job.SourceDeviceId,TargetDeviceId=job.TargetDeviceId,ProposedByUserId=actor,ProposedAtUtc=clock.GetUtcNow(),ResetAtUtc=input.ResetAtUtc,ApprovalJson=JsonSerializer.Serialize(input.Approval),ProcessStoppedEvidence=input.ProcessStoppedEvidence,ResetEvidence=input.ResetEvidence,RequestHash=hash});
   return(input.InspectionId,input.Id);
  },ct);
 }
 public Task<object> ListAsync(Guid actor,Guid transfer,CancellationToken ct=default)=>tx.ExecuteAsync<object>(actor,DeviceBiometricTransfers.Read,"device-biometric.recovery-viewed",async token=>
 {
  if(!await db.DeviceBiometricTransfers.AnyAsync(x=>x.Id==transfer,token))throw new ArgumentException("Unknown or inaccessible transfer.");
  var rows=await(from r in db.DeviceBiometricRecoveries.AsNoTracking() join i in db.DeviceBiometricTransfers on r.InspectionId equals i.Id where r.TransferId==transfer orderby r.ProposedAtUtc descending select new{r.Id,r.TransferId,r.InspectionId,r.ProposedByUserId,r.ProposedAtUtc,r.ResetAtUtc,r.ProcessStoppedEvidence,r.ResetEvidence,r.ApprovedByUserId,r.ApprovedAtUtc,r.ReviewEvidence,r.Version,observation=i.State,observedAtUtc=i.FinishedAtUtc}).Take(50).ToListAsync(token);
  return(new{items=rows,meaning="read-only-observation-and-human-quiescence-not-original-copy-success"},transfer);
 },ct);
 public Task<Guid> ApproveAsync(Guid actor,Guid id,Review input,CancellationToken ct=default)
 {
  if(input is null||input.Version is not{Length:8}||!Text(input.ReviewEvidence)||input.Confirmation!=Confirmation)throw new ArgumentException("Independent review with current proposal version required.");
  input=input with{Version=input.Version.ToArray()};
  return tx.ExecuteAsync(actor,Approve,"device-biometric.recovery-approved",async token=>
  {
   await Lock(token);var row=await db.DeviceBiometricRecoveries.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown or inaccessible recovery.");
   if(row.ProposedByUserId==actor||row.ApprovedAtUtc!=null||!row.Version.SequenceEqual(input.Version))throw new DomainConflictException("No self-countersignature or changed/duplicate review.");
   foreach(var device in new[]{row.SourceDeviceId,row.TargetDeviceId})
    if(!await tx.HasDevicePermissionAsync(row.ProposedByUserId,Propose,device,token)||!await tx.HasDevicePermissionAsync(actor,Approve,device,token))throw new UnauthorizedAccessException();
   await Target(row.ProposedByUserId,row.TransferId,JsonSerializer.Deserialize<BiometricRecoveryApproval>(row.ApprovalJson)!,row.ResetAtUtc,token);
   var observation=await db.DeviceBiometricTransfers.AsNoTracking().SingleAsync(x=>x.Id==row.InspectionId,token);
   if(observation.InspectionOfId!=row.TransferId||observation.State is not("observed-equal" or "observed-different" or "target-absent")||observation.ClaimedAtUtc is null||observation.ClaimedAtUtc<row.ProposedAtUtc||observation.FinishedAtUtc is null||observation.FinishedAtUtc<clock.GetUtcNow().AddMinutes(-5))throw new DomainConflictException("Fresh successfully completed read-only observation required, never bare ACK or failed inspection.");
   row.ApprovedByUserId=actor;row.ApprovedAtUtc=clock.GetUtcNow();row.ReviewEvidence=input.ReviewEvidence;
   return(row.TransferId,row.Id);
  },ct);
 }
}
