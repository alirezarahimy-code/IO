namespace IO.Infrastructure.Storage;
public sealed class DeviceFileImportRecord
{
    public Guid Id {get;set;}
    public Guid DeviceId {get;set;}
    public Guid SourceId {get;set;}
    public Guid SiteId {get;set;}
    public Guid CompanyId {get;set;}
    public Guid CreatedByUserId {get;set;}
    public DateTimeOffset CreatedAtUtc {get;set;}
    public byte[] ExpectedDeviceVersion {get;set;}=null!;
    public byte[] OriginalFile {get;set;}=null!;
    public string FileHash {get;set;}=null!;
    public string Calendar {get;set;}=null!;
    public string TimeZoneId {get;set;}=null!;
    public int MachineNumber {get;set;}
    public int InCode {get;set;}
    public int OutCode {get;set;}
    public int LineCount {get;set;}
    public DateTimeOffset? LastReviewedAtUtc {get;set;}
    public byte[] Version {get;set;}=null!;
}
public sealed class DeviceFileImportLineRecord
{
    public Guid ImportId {get;set;}
    public int LineNumber {get;set;}
    public string Status {get;set;}=null!;
    public Guid? RawEventId {get;set;}
    public Guid? ConflictId {get;set;}
    public Guid ReviewedByUserId {get;set;}
    public DateTimeOffset ReviewedAtUtc {get;set;}
}
