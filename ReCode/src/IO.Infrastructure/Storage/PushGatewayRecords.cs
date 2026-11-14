namespace IO.Infrastructure.Storage;

public sealed class PushGatewayBindingRecord
{
    public Guid Id {get;set;}
    public Guid GatewayId {get;set;}
    public Guid DeviceId {get;set;}
    public Guid SourceId {get;set;}
    public Guid ServiceUserId {get;set;}
    public Guid CreatedByUserId {get;set;}
    public DateTimeOffset CreatedAtUtc {get;set;}
    public byte[] ExpectedDeviceVersion {get;set;}=null!;
    public string Serial {get;set;}=null!;
    public string TimeZoneId {get;set;}=null!;
    public string KeyVersion {get;set;}=null!;
    public string ProtectedKey {get;set;}=null!;
    public Guid? RevokedByUserId {get;set;}
    public DateTimeOffset? RevokedAtUtc {get;set;}
    public byte[] Version {get;set;}=null!;
    public override string ToString()=>"Gateway binding [identifiers and protected key redacted]";
}
public sealed class PushGatewayReceiptRecord
{
    public Guid Id {get;set;}
    public Guid BindingId {get;set;}
    public Guid GatewayId {get;set;}
    public Guid DeviceId {get;set;}
    public Guid TransferId {get;set;}
    public string RequestFingerprint {get;set;}=null!;
    public override string ToString()=>"Gateway committed receipt [identifiers redacted]";
}
