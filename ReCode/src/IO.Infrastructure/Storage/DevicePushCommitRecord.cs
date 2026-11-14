namespace IO.Infrastructure.Storage;
public sealed class DevicePushCommitRecord
{
    public Guid Id {get;set;}
    public Guid JournalId {get;set;}
    public Guid DeviceId {get;set;}
    public int LineNumber {get;set;}
    public Guid MappingId {get;set;}
    public Guid EmployeeId {get;set;}
    public Guid RawEventId {get;set;}
    public Guid ReviewedByUserId {get;set;}
    public DateTimeOffset ReviewedAtUtc {get;set;}
    public string EventType {get;set;}=null!;
    public string TrustEvidence {get;set;}=null!;
    public string DuplicateReviewEvidence {get;set;}=null!;
    public string RequestHash {get;set;}=null!;
    public string PreviewHash {get;set;}=null!;
    public override string ToString()=>"Reviewed PUSH commitment [personal data and evidence references redacted]";
}
