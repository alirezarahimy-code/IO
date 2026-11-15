namespace IO.Infrastructure.Storage;
public sealed class DeviceBiometricRecoveryRecord
{
 public Guid Id {get;set;}
 public Guid TransferId {get;set;}
 public Guid InspectionId {get;set;}
 public Guid SourceDeviceId {get;set;}
 public Guid TargetDeviceId {get;set;}
 public Guid ProposedByUserId {get;set;}
 public DateTimeOffset ProposedAtUtc {get;set;}
 public DateTimeOffset ResetAtUtc {get;set;}
 public string ApprovalJson {get;set;}=null!;
 public string ProcessStoppedEvidence {get;set;}=null!;
 public string ResetEvidence {get;set;}=null!;
 public string RequestHash {get;set;}=null!;
 public Guid? ApprovedByUserId {get;set;}
 public DateTimeOffset? ApprovedAtUtc {get;set;}
 public string? ReviewEvidence {get;set;}
 public byte[] Version {get;set;}=null!;
 public override string ToString()=>"Biometric assisted recovery [metadata/evidence redacted]";
}
