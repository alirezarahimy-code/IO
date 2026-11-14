namespace IO.Infrastructure.Storage;
public sealed class RemoteEnrollmentRecord
{
    public Guid Id {get;set;}
    public Guid DeviceId {get;set;}
    public Guid EmployeeId {get;set;}
    public Guid SourceId {get;set;}
    public Guid RequestedByUserId {get;set;}
    public DateTimeOffset RequestedAtUtc {get;set;}
    public byte[] ExpectedDeviceVersion {get;set;}=null!;
    public string PersonIdentifier {get;set;}=null!;
    public string ApprovalJson {get;set;}=null!;
    public int TimeoutSeconds {get;set;}
    public string State {get;set;}="pending";
    public Guid? ServiceUserId {get;set;}
    public Guid? ClaimId {get;set;}
    public DateTimeOffset? ClaimedAtUtc {get;set;}
    public DateTimeOffset? CancelRequestedAtUtc {get;set;}
    public DateTimeOffset? FinishedAtUtc {get;set;}
    public string? ResultJson {get;set;}
    public byte[] Version {get;set;}=null!;
    public override string ToString()=>"Enrollment job [subject and policy redacted]";
}
