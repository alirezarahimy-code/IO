namespace IO.Infrastructure.Storage;
public sealed class DeviceAttendanceDownloadRecord
{
    public Guid Id {get;set;}
    public Guid DeviceId {get;set;}
    public Guid SourceId {get;set;}
    public Guid SiteId {get;set;}
    public Guid CompanyId {get;set;}
    public Guid RequestedByUserId {get;set;}
    public DateTimeOffset RequestedAtUtc {get;set;}
    public byte[] ExpectedDeviceVersion {get;set;}=null!;
    public DateTimeOffset FromUtc {get;set;}
    public DateTimeOffset UntilUtcExclusive {get;set;}
    public string TimeZoneId {get;set;}=null!;
    public int InCode {get;set;}
    public int OutCode {get;set;}
    public int TimeoutSeconds {get;set;}
    public string State {get;set;}="pending";
    public Guid? ServiceUserId {get;set;}
    public Guid? ClaimId {get;set;}
    public DateTimeOffset? LeaseAtUtc {get;set;}
    public DateTimeOffset? FinishedAtUtc {get;set;}
    public long ReceivedCount {get;set;}
    public DateTimeOffset? LastReviewedAtUtc {get;set;}
    public byte[] Version {get;set;}=null!;
}
public sealed class DeviceAttendanceRowRecord
{
    public Guid DownloadId {get;set;}
    public long Sequence {get;set;}
    public string PayloadJson {get;set;}=null!;
    public string PayloadHash {get;set;}=null!;
    public long OccurrenceOrdinal {get;set;}
    public string Status {get;set;}="received";
    public Guid? RawEventId {get;set;}
    public Guid? ConflictId {get;set;}
    public Guid? ReviewedByUserId {get;set;}
    public DateTimeOffset? ReviewedAtUtc {get;set;}
}
