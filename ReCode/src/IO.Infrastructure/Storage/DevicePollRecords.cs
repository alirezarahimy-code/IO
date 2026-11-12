namespace IO.Infrastructure.Storage;
public sealed class DevicePollScheduleRecord
{
    public Guid Id {get;set;}
    public Guid DeviceId {get;set;}
    public Guid SourceId {get;set;}
    public Guid SiteId {get;set;}
    public Guid CompanyId {get;set;}
    public string TimeZoneId {get;set;}=null!;
    public Guid CreatedByUserId {get;set;}
    public DateTimeOffset CreatedAtUtc {get;set;}
    public byte[] ExpectedDeviceVersion {get;set;}=null!;
    public DateTimeOffset StartUtc {get;set;}
    public int EverySeconds {get;set;}
    public int WindowSeconds {get;set;}
    public int LagSeconds {get;set;}
    public int TimeoutSeconds {get;set;}
    public int InCode {get;set;}
    public int OutCode {get;set;}
    public bool AutoCommit {get;set;}
    public bool Enabled {get;set;}=true;
    public DateTimeOffset CursorUtc {get;set;}
    public DateTimeOffset NextDueUtc {get;set;}
    public Guid? ActiveRunId {get;set;}
    public int Failures {get;set;}
    public string LastOutcome {get;set;}="waiting";
    public byte[] Version {get;set;}=null!;
}
public sealed class DevicePollRunRecord
{
    public Guid Id {get;set;}
    public Guid ScheduleId {get;set;}
    public Guid DownloadId {get;set;}
    public DateTimeOffset FromUtc {get;set;}
    public DateTimeOffset UntilUtcExclusive {get;set;}
    public DateTimeOffset CreatedAtUtc {get;set;}
    public long NextAfter {get;set;}
    public string Outcome {get;set;}="queued";
    public DateTimeOffset? FinishedAtUtc {get;set;}
}
