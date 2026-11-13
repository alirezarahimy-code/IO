using System.Globalization;
using System.Text.Json.Serialization;

namespace IO.DeviceContracts;

public sealed record ZkOperationInput(ZkProbeInput Connection, string Operation, string? ClockLocal, bool AllowClockSet,bool AllowAdvancedSettingsRead=false,bool AllowRestart=false,ZkNetworkPlan? NetworkPlan=null,bool AllowNetworkIpChange=false,bool AllowNetworkInspect=false,ZkIdlePlan? IdlePlan=null,bool AllowIdleMinutesWrite=false,ZkIdleApproval? IdleApproval=null,ZkFunctionKeyReadApproval? FunctionKeyReadApproval=null)
{
    public bool IsValid() => Connection is not null && Connection.IsValid() &&
        (FunctionKeyReadApproval is null||(Operation=="settings.read"&&AllowAdvancedSettingsRead&&FunctionKeyReadApproval.IsValid()))&&
        ((Operation=="settings.idle.set"&&NetworkPlan is null&&ClockLocal is null&&AllowIdleMinutesWrite&&IdlePlan?.IsValid()==true&&IdlePlan.Serial==Connection.ExpectedSerial&&IdleApproval?.Allows(IdlePlan)==true) ||
        (IdlePlan is null&&IdleApproval is null&&((NetworkPlan is not null&&NetworkPlan.IsValidFor(Operation)&&ClockLocal is null&&Connection.Address==NetworkPlan.TargetAddress(Operation)&&Connection.ExpectedSerial==NetworkPlan.Serial&&(Operation=="network.ip.set"?AllowNetworkIpChange:AllowNetworkInspect)) ||
        (NetworkPlan is null&&((Operation == "device.restart" && AllowRestart && ClockLocal is null) || (Operation == "information.read" && ClockLocal is null) || (Operation=="settings.read" && AllowAdvancedSettingsRead && ClockLocal is null) || (Operation == "clock.set" && AllowClockSet && ValidClock(ClockLocal)))))));
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
public sealed record ZkOperationOutput(string State, ZkDeviceInformation? Information=null,ZkAdvancedSettings? Settings=null,ZkNetworkObservation? Network=null,ZkIdleObservation? Idle=null)
{
    public bool IsValidFor(ZkOperationInput input) => input.IsValid()&&(Idle is null||input.Operation=="settings.idle.set")&&(Network is null||input.Operation=="network.inspect")&&(State switch
    {
        "succeeded" => input.Operation switch
        {
            "settings.idle.set" => Information is null&&Settings is null&&Network is null&&Idle is not null&&Idle.IsValid()&&Idle.Serial==input.IdlePlan!.Serial&&Idle.Product==input.IdlePlan.Product&&Idle.Firmware==input.IdlePlan.Firmware&&Idle.Minutes==input.IdlePlan.NewMinutes,
            "information.read" => Settings is null&&Information is not null&&Information.IsValid()&&Information.Serial==input.Connection.ExpectedSerial,
            "network.inspect" => Information is null&&Settings is null&&Network is not null&&Network.IsValid()&&Network.Serial==input.Connection.ExpectedSerial&&Network.ConnectedAddress==input.Connection.Address,
            "settings.read" => input.AllowAdvancedSettingsRead&&Information is null&&Settings is not null&&Settings.IsValid()&&Settings.Serial==input.Connection.ExpectedSerial&&
                (input.FunctionKeyReadApproval is null?Settings.FunctionKeys is null:input.FunctionKeyReadApproval.Allows(Settings.Product,Settings.Firmware)&&Settings.FunctionKeys?.Matches(input.FunctionKeyReadApproval)==true),
            "device.restart" => input.AllowRestart&&Information is null&&Settings is null,
            "clock.set" => input.AllowClockSet&&Information is null&&Settings is null,
            _=>false
        },
        "unsupported" or "identity-mismatch" or "failed" or "unknown" => Information is null&&Settings is null&&Network is null&&Idle is null,
        _ => false
    });
}
