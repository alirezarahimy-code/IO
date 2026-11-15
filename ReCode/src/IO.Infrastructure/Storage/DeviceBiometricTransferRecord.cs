namespace IO.Infrastructure.Storage;
/// <summary>Intent/result metadata only. No template bytes, serialized template or biometric digest columns.</summary>
public sealed class DeviceBiometricTransferRecord
{
 public Guid Id {get;set;}
 public Guid? InspectionOfId {get;set;}
 public Guid SourceDeviceId {get;set;}
 public Guid TargetDeviceId {get;set;}
 public Guid EmployeeId {get;set;}
 public Guid RequestedByUserId {get;set;}
 public DateTimeOffset RequestedAtUtc {get;set;}
 public byte[] SourceDeviceVersion {get;set;}=null!;
 public byte[] TargetDeviceVersion {get;set;}=null!;
 public string SourcePerson {get;set;}=null!;
 public string TargetPerson {get;set;}=null!;
 public string ApprovalJson {get;set;}=null!;
 public string RequestHash {get;set;}=null!;
 public string State {get;set;}="pending";
 public int TimeoutSeconds {get;set;}
 public Guid? ServiceUserId {get;set;}
 public Guid? ClaimId {get;set;}
 public DateTimeOffset? ClaimedAtUtc {get;set;}
 public DateTimeOffset? CancelRequestedAtUtc {get;set;}
 public DateTimeOffset? FinishedAtUtc {get;set;}
 public byte[] Version {get;set;}=null!;
 public override string ToString()=>"Biometric transfer job [subjects and references redacted; no material]";
}
