namespace IO.Infrastructure.Storage;
/// <summary>One actual worker process instance; not device connectivity or installation proof.</summary>
public sealed class DeviceServiceHeartbeatRecord
{
 public Guid Id{get;set;}
 public Guid ServiceUserId{get;set;}
 public DateTimeOffset StartedAtUtc{get;set;}
 public DateTimeOffset LastSeenAtUtc{get;set;}
 public string State{get;set;}="running";
 public byte[] Version{get;set;}=null!;
}
