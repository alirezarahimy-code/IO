namespace IO.Infrastructure.Storage;
public sealed class DeviceUserUploadRecord
{
    public Guid Id {get;set;}
    public Guid BatchId {get;set;}
    public string RequestHash {get;set;}=null!;
    public Guid DeviceId {get;set;}
    public Guid EmployeeId {get;set;}
    public Guid SnapshotId {get;set;}
    public Guid RequestedByUserId {get;set;}
    public DateTimeOffset RequestedAtUtc {get;set;}
    public byte[] ExpectedDeviceVersion {get;set;}=null!;
    public string PersonIdentifier {get;set;}=null!;
    public string? ExpectedJson {get;set;}
    public string DesiredJson {get;set;}=null!;
    public int TimeoutSeconds {get;set;}
    public string State {get;set;}="pending";
    public Guid? ServiceUserId {get;set;}
    public Guid? ClaimId {get;set;}
    public DateTimeOffset? ClaimedAtUtc {get;set;}
    public DateTimeOffset? FinishedAtUtc {get;set;}
    public Guid? ResolutionSnapshotId {get;set;}
    public Guid? ResolvedByUserId {get;set;}
    public DateTimeOffset? ResolvedAtUtc {get;set;}
    public byte[] Version {get;set;}=null!;
}
