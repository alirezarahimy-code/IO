using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace IO.Infrastructure.Devices;
/// <summary>Assisted physical recovery with two independent humans. Original UNKNOWN and absence of a completion event remain immutable.</summary>
public sealed class RemoteEnrollmentRecovery(IoDbContext db,PermissionTransaction tx,TimeProvider clock,IConfiguration config)
{
 public const string Propose="devices.enrollment.recovery.propose",Approve="devices.enrollment.recovery.approve";
 public const string Confirmation="PHYSICAL-RESET-NOT-ENROLLMENT-SUCCESS";
 public sealed record Input(Guid Id,Guid EnrollmentId,byte[] DeviceVersion,RemoteEnrollmentRecoveryApproval Approval,DateTimeOffset PhysicalResetAtUtc,string ProcessStoppedEvidence,string PhysicalResetEvidence,string SlotObservationEvidence,string ObservedSlot,string Confirmation)
 {public override string ToString()=>"Physical recovery input [references redacted]";}
 public sealed record Review(byte[] Version,string ReviewEvidence,string Confirmation);
 private static bool Text(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=200&&!s.Any(char.IsControl);
 private bool Approved(RemoteEnrollmentRecoveryApproval a)=>a?.IsValid()==true&&config.GetValue<bool>("IO:RemoteEnroll:EnablePhysicalRecovery")&&
    (config.GetSection("IO:RemoteEnroll:RecoveryApprovals").Get<RemoteEnrollmentRecoveryApproval[]>()??[]).Count(x=>x==a)==1;
 private async Task<(RemoteEnrollmentRecord Job,DeviceRecord Device)> Target(Guid enrollment,byte[] version,RemoteEnrollmentRecoveryApproval approval,DateTimeOffset reset,CancellationToken ct)
 {
  if(!Approved(approval))throw new DomainConflictException("Installed-model reset procedure and physical recovery must be approved in server configuration.");
  var job=await db.RemoteEnrollments.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==enrollment,ct)??throw new ArgumentException("Unknown or inaccessible job.");
  var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==job.DeviceId,ct);
  var now=clock.GetUtcNow();
  if(job.State!="unknown"||job.FinishedAtUtc is null||reset<job.FinishedAtUtc||reset>now.AddSeconds(-approval.MinimumSettlingSeconds)||reset<now.AddHours(-1)||
    !device.Enabled||device.Profile!="zk-standalone"||device.SourceId!=job.SourceId||!device.Version.SequenceEqual(version)||!job.ExpectedDeviceVersion.SequenceEqual(version)||JsonSerializer.Deserialize<RemoteEnrollApproval>(job.ApprovalJson)!=approval.Enrollment)
    throw new DomainConflictException("Unknown current installation, post-termination physical reset and settling interval required; no reenrollment or inferred completion.");
  if(await db.RemoteEnrollmentRecoveries.AnyAsync(x=>x.EnrollmentId==enrollment&&x.ApprovedAtUtc!=null,ct))throw new DomainConflictException("Recovery already countersigned.");
  if(await db.RemoteEnrollments.AnyAsync(x=>x.DeviceId==device.Id&&(x.State=="pending"||x.State=="running"),ct)||await DeviceWriteSafety.InFlightAsync(db,device.Id,ct)||await db.DeviceConnections.AnyAsync(x=>x.DeviceId==device.Id&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==device.Id&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==device.Id&&(x.State=="pending"||x.State=="running"),ct)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==device.Id&&(x.State=="pending"||x.State=="running"||x.State=="unknown"&&x.ResolutionSnapshotId==null),ct)||await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==device.Id&&(x.State=="pending"||x.State=="running"),ct)||await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==device.Id&&x.Enabled,ct))throw new DomainConflictException("Pause polling and finish all other device work first.");
  return(job,device);
 }
 public Task<Guid> ProposeAsync(Guid actor,Input input,CancellationToken ct=default)
 {
  if(input is null||input.Id==Guid.Empty||input.EnrollmentId==Guid.Empty||input.DeviceVersion is not{Length:8}||input.Approval is null||input.PhysicalResetAtUtc.Offset!=TimeSpan.Zero||!Text(input.ProcessStoppedEvidence)||!Text(input.PhysicalResetEvidence)||!Text(input.SlotObservationEvidence)||input.ObservedSlot is not("present" or "absent")||input.Confirmation!=Confirmation)throw new ArgumentException("Explicit post-reset evidence without biometric data required.");
  input=input with{DeviceVersion=input.DeviceVersion.ToArray()};
  return tx.ExecuteAsync(actor,Propose,"device-enrollment.recovery-proposed",async token=>
  {
   await tx.LockAsync("IO.DeviceRegistry",token);await tx.LockAsync("IO.DeviceConnections",token);
   var existing=await db.RemoteEnrollmentRecoveries.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.Id,token);
   if(existing is not null)
   {
    if(existing.ProposedByUserId!=actor||existing.EnrollmentId!=input.EnrollmentId||!existing.DeviceVersion.SequenceEqual(input.DeviceVersion)||existing.ApprovalJson!=JsonSerializer.Serialize(input.Approval)||existing.PhysicalResetAtUtc!=input.PhysicalResetAtUtc||existing.ProcessStoppedEvidence!=input.ProcessStoppedEvidence||existing.PhysicalResetEvidence!=input.PhysicalResetEvidence||existing.SlotObservationEvidence!=input.SlotObservationEvidence||existing.ObservedSlot!=input.ObservedSlot)throw new DomainConflictException("Changed physical recovery retry.");
    return(existing.Id,existing.EnrollmentId);
   }
   await Target(input.EnrollmentId,input.DeviceVersion,input.Approval,input.PhysicalResetAtUtc,token);
   db.RemoteEnrollmentRecoveries.Add(new(){Id=input.Id,EnrollmentId=input.EnrollmentId,DeviceId=(await db.RemoteEnrollments.SingleAsync(x=>x.Id==input.EnrollmentId,token)).DeviceId,ProposedByUserId=actor,ProposedAtUtc=clock.GetUtcNow(),PhysicalResetAtUtc=input.PhysicalResetAtUtc,DeviceVersion=input.DeviceVersion,ApprovalJson=JsonSerializer.Serialize(input.Approval),ProcessStoppedEvidence=input.ProcessStoppedEvidence,PhysicalResetEvidence=input.PhysicalResetEvidence,SlotObservationEvidence=input.SlotObservationEvidence,ObservedSlot=input.ObservedSlot});
   return(input.Id,input.EnrollmentId);
  },ct);
 }
 public Task<object> ListAsync(Guid actor,Guid enrollment,CancellationToken ct=default)=>tx.ExecuteAsync<object>(actor,RemoteEnrollments.Read,"device-enrollment.recoveries-viewed",async token=>
 {
  if(!await db.RemoteEnrollments.AnyAsync(x=>x.Id==enrollment,token))throw new ArgumentException("Unknown or inaccessible job.");
  var rows=await db.RemoteEnrollmentRecoveries.AsNoTracking().Where(x=>x.EnrollmentId==enrollment).OrderByDescending(x=>x.ProposedAtUtc).Take(50).ToListAsync(token);
  return(new{items=rows,meaning="human-attested-physical-recovery-not-enrollment-completion"},enrollment);
 },ct);
 public Task<Guid> ApproveAsync(Guid actor,Guid id,Review input,CancellationToken ct=default)
 {
  if(input is null||input.Version is not{Length:8}||!Text(input.ReviewEvidence)||input.Confirmation!=Confirmation)throw new ArgumentException("Exact independent countersignature required.");
  input=input with{Version=input.Version.ToArray()};
  return tx.ExecuteAsync(actor,Approve,"device-enrollment.physical-recovery-approved",async token=>
  {
   await tx.LockAsync("IO.DeviceRegistry",token);await tx.LockAsync("IO.DeviceConnections",token);
   var row=await db.RemoteEnrollmentRecoveries.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown or inaccessible recovery.");
   if(row.ProposedByUserId==actor||row.ApprovedAtUtc!=null||!row.Version.SequenceEqual(input.Version))throw new DomainConflictException("Independent current review required.");
   if(!await tx.HasDevicePermissionAsync(row.ProposedByUserId,Propose,row.DeviceId,token))throw new UnauthorizedAccessException();
   await Target(row.EnrollmentId,row.DeviceVersion,JsonSerializer.Deserialize<RemoteEnrollmentRecoveryApproval>(row.ApprovalJson)!,row.PhysicalResetAtUtc,token);
   row.ApprovedByUserId=actor;row.ApprovedAtUtc=clock.GetUtcNow();row.ReviewEvidence=input.ReviewEvidence;
   return(row.EnrollmentId,row.Id);
  },ct);
 }
}
