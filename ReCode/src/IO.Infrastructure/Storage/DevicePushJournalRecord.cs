namespace IO.Infrastructure.Storage;

/// <summary>Operator-retained wire evidence only. Neither device authentication nor attendance processing.</summary>
public sealed class DevicePushJournalRecord
{
    public Guid Id {get;set;}
    public Guid DeviceId {get;set;}
    public Guid SourceId {get;set;}
    public Guid SiteId {get;set;}
    public Guid CompanyId {get;set;}
    public Guid RetainedByUserId {get;set;}
    public DateTimeOffset RetainedAtUtc {get;set;}
    public byte[] ExpectedDeviceVersion {get;set;}=null!;
    public string Serial {get;set;}=null!;
    public string? Stamp {get;set;}
    public string Contract {get;set;}=null!;
    public string TimeZoneId {get;set;}=null!;
    public byte[] OriginalBody {get;set;}=null!;
    public string PayloadHash {get;set;}=null!;
    public int RowCount {get;set;}
    public int ParsedCount {get;set;}
    public string RowsJson {get;set;}=null!;
    public override string ToString()=>"PUSH journal evidence [identifiers and personal data redacted]";
}
