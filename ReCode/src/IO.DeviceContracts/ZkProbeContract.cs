using System.Net;

namespace IO.DeviceContracts;

/// <summary>Private bounded stdin IPC. Never serialize this into API responses, audit, or logs.</summary>
[System.Text.Json.Serialization.JsonUnmappedMemberHandling(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow)]
public sealed record ZkProbeInput(Guid DeviceId, string Address, int Port, int MachineNumber, string ExpectedSerial, int CommKey, [property:System.Text.Json.Serialization.JsonIgnore(Condition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] ZkLocalTransport? LocalTransport=null)
{
    public bool IsValid() => DeviceId != Guid.Empty && (LocalTransport is null ? IPAddress.TryParse(Address, out _) && Port is > 0 and <= 65535 : LocalTransport.IsValid() && Address==LocalTransport.StationName && Port==LocalTransport.SerialPort) &&
        MachineNumber > 0 && !string.IsNullOrWhiteSpace(ExpectedSerial) && ExpectedSerial.Length <= 100 &&
        ExpectedSerial == ExpectedSerial.Trim() && !ExpectedSerial.Any(char.IsControl) && CommKey >= 0;
    public override string ToString() => "ZkProbeInput [private IPC; values redacted]";
}

public static class ZkProbeResults
{
    public static bool IsKnown(string value) => value is "sdk-serial-matched" or "sdk-identity-mismatch" or "sdk-unavailable" or "unreachable" or "unsupported" or "invalid-input";
}
