namespace IO.Infrastructure.Storage;
/// <summary>Destructive intent and two-person approval, or separate physical recovery; never alters the original UNKNOWN.</summary>
public sealed class DeviceProtectedEraseRecord
{
 public Guid Id {get;set;}
 public Guid DeviceId {get;set;}
 public Guid ArchiveId {get;set;}
 public Guid? RecoveryOfId {get;set;}
 public byte[] ArchiveVersion {get;set;}=null!;
 public byte[] DeviceVersion {get;set;}=null!;
 public Guid RequestedByUserId {get;set;}
 public DateTimeOffset RequestedAtUtc {get;set;}
 public DateTimeOffset QuiescedAtUtc {get;set;}
 public string EvidenceReference {get;set;}=null!;
 public string ApprovalJson {get;set;}=null!;
 public string ArchiveDigest {get;set;}=null!;
 public int ArchiveCount {get;set;}
 public string Family {get;set;}=null!;
 public string? Person {get;set;}
 public string? ExpectedJson {get;set;}
 public string RequestHash {get;set;}=null!;
 public string State {get;set;}="proposed";
 public Guid? ApprovedByUserId {get;set;}
 public DateTimeOffset? ApprovedAtUtc {get;set;}
 public string? ReviewReference {get;set;}
 public Guid? ServiceUserId {get;set;}
 public Guid? ClaimId {get;set;}
 public DateTimeOffset? ClaimedAtUtc {get;set;}
 public DateTimeOffset? CancelRequestedAtUtc {get;set;}
 public DateTimeOffset? FinishedAtUtc {get;set;}
 public byte[] Version {get;set;}=null!;
 public override string ToString()=>"Attendance erase/recovery metadata [references redacted]";
}
