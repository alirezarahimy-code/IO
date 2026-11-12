namespace IO.Infrastructure.Storage;
public sealed class DeviceUserDownloadRecord
{
    public Guid Id {get;set;}
    public Guid DeviceId {get;set;}
    public Guid SourceId {get;set;}
    public Guid CompanyId {get;set;}
    public Guid RequestedByUserId {get;set;}
    public DateTimeOffset RequestedAtUtc {get;set;}
    public byte[] ExpectedDeviceVersion {get;set;}=null!;
    public int TimeoutSeconds {get;set;}
    public string ReadFormat {get;set;}=IO.DeviceContracts.ZkUserReadFormats.Ssr;
    public string State {get;set;}="pending";
    public Guid? ServiceUserId {get;set;}
    public Guid? ClaimId {get;set;}
    public DateTimeOffset? LeaseAtUtc {get;set;}
    public DateTimeOffset? FinishedAtUtc {get;set;}
    public long ReceivedCount {get;set;}
    public byte[] Version {get;set;}=null!;
}
public sealed class DeviceDownloadedUserRecord
{
    public Guid DownloadId {get;set;}
    public long Sequence {get;set;}
    public string PersonIdentifier {get;set;}=null!;
    public string Name {get;set;}=null!;
    public string? Card {get;set;}
    public int Privilege {get;set;}
    public bool Enabled {get;set;}
    public string Fingerprint {get;set;}=null!;
}
