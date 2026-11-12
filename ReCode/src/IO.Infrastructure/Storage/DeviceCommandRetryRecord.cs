namespace IO.Infrastructure.Storage;
public sealed class DeviceCommandRetryRecord
{
    public Guid Id {get;set;}
    public Guid? PreviousOperationId {get;set;}
    public Guid? NextOperationId {get;set;}
    public Guid? PreviousAttendanceDownloadId {get;set;}
    public Guid? NextAttendanceDownloadId {get;set;}
    public Guid RequestedByUserId {get;set;}
    public DateTimeOffset RequestedAtUtc {get;set;}
    public byte[] ExpectedPreviousVersion {get;set;}=null!;
    public byte[] ExpectedDeviceVersion {get;set;}=null!;
}
