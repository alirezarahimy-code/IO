namespace IO.Infrastructure.Storage;
/// <summary>Two-person physical recovery attestation, NOT a completion event or biometric snapshot.</summary>
public sealed class RemoteEnrollmentRecoveryRecord
{
 public Guid Id {get;set;}
 public Guid EnrollmentId {get;set;}
 public Guid DeviceId {get;set;}
 public Guid ProposedByUserId {get;set;}
 public DateTimeOffset ProposedAtUtc {get;set;}
 public DateTimeOffset PhysicalResetAtUtc {get;set;}
 public byte[] DeviceVersion {get;set;}=null!;
 public string ApprovalJson {get;set;}=null!;
 public string ProcessStoppedEvidence {get;set;}=null!;
 public string PhysicalResetEvidence {get;set;}=null!;
 public string SlotObservationEvidence {get;set;}=null!;
 public string ObservedSlot {get;set;}=null!;
 public Guid? ApprovedByUserId {get;set;}
 public DateTimeOffset? ApprovedAtUtc {get;set;}
 public string? ReviewEvidence {get;set;}
 public byte[] Version {get;set;}=null!;
 public override string ToString()=>"Physical enrollment recovery [evidence and subject redacted]";
}
