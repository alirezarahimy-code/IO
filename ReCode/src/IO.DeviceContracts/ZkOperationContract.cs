using System.Globalization;
using System.Text.Json.Serialization;

namespace IO.DeviceContracts;

public sealed record ZkOperationInput(ZkProbeInput Connection, string Operation, string? ClockLocal, bool AllowClockSet)
{
    public bool IsValid() => Connection is not null && Connection.IsValid() &&
        ((Operation == "information.read" && ClockLocal is null) || (Operation == "clock.set" && AllowClockSet && ValidClock(ClockLocal)));
    public static bool ValidClock(string? value) => value is not null && value.Length == 19 &&
        DateTime.TryParseExact(value,"yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture,DateTimeStyles.None,out var time) &&
        time.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture)==value;
    public override string ToString() => "ZkOperationInput [private IPC; values redacted]";
}

public sealed record ZkDeviceInformation(string Serial, string? Product, string? Firmware, string? Mac, string? ClockLocal,
    int? Records, int? RecordCapacity, int? Users, int? UserCapacity, int? Administrators, int? Fingers, int? FingerCapacity, int? Faces, int? FaceCapacity,
    bool ClockWriteEnabledByConfiguration)
{
    public bool IsValid()
    {
        static bool Text(string? text,int max)=>text is null || (text.Length<=max&&!text.Any(char.IsControl));
        return !string.IsNullOrWhiteSpace(Serial) && Text(Serial,100)&&Text(Product,120)&&Text(Firmware,120)&&Text(Mac,100)&&
            (ClockLocal is null||ZkOperationInput.ValidClock(ClockLocal))&&
            new[]{Records,RecordCapacity,Users,UserCapacity,Administrators,Fingers,FingerCapacity,Faces,FaceCapacity}.All(x=>x is null or >=0);
    }
}
public sealed record ZkOperationOutput(string State, ZkDeviceInformation? Information=null)
{
    public bool IsValidFor(ZkOperationInput input) => State switch
    {
        "succeeded" => input.Operation=="information.read" ? Information is not null&&Information.IsValid()&&Information.Serial==input.Connection.ExpectedSerial : input.Operation=="clock.set"&&input.AllowClockSet&&Information is null,
        "unsupported" or "identity-mismatch" or "failed" or "unknown" => Information is null,
        _ => false
    };
}
