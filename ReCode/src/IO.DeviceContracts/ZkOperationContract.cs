using System.Globalization;
using System.Text.Json.Serialization;

namespace IO.DeviceContracts;

public sealed record ZkOperationInput(ZkProbeInput Connection, string Operation, string? ClockLocal, bool AllowClockSet,bool AllowAdvancedSettingsRead=false,bool AllowRestart=false,ZkNetworkPlan? NetworkPlan=null,bool AllowNetworkIpChange=false,bool AllowNetworkInspect=false,ZkIdlePlan? IdlePlan=null,bool AllowIdleMinutesWrite=false,ZkIdleApproval? IdleApproval=null,ZkFunctionKeyReadApproval? FunctionKeyReadApproval=null,ZkRawSettingPlan? RawSettingPlan=null,bool AllowRawSettingsWrite=false,ZkRawSettingApproval? RawSettingApproval=null,ZkFunctionKeyPlan? FunctionKeyPlan=null,bool AllowFunctionKeyWrite=false,ZkFunctionKeyWriteApproval? FunctionKeyWriteApproval=null)
{
    public bool IsValid()
    {
        if(Connection?.IsValid()!=true)return false;
        if(Operation=="settings.key.set")return AllowAdvancedSettingsRead&&AllowFunctionKeyWrite&&FunctionKeyPlan?.IsValid()==true&&FunctionKeyPlan.Serial==Connection.ExpectedSerial&&FunctionKeyWriteApproval?.Allows(FunctionKeyPlan)==true&&FunctionKeyReadApproval is null&&NetworkPlan is null&&IdlePlan is null&&IdleApproval is null&&RawSettingPlan is null&&RawSettingApproval is null&&ClockLocal is null;
        if(FunctionKeyPlan is not null||FunctionKeyWriteApproval is not null)return false;
        if(FunctionKeyReadApproval is not null&&(Operation!="settings.read"||!AllowAdvancedSettingsRead||!FunctionKeyReadApproval.IsValid()))return false;
        if(Operation=="settings.raw.set")return RawSettingPlan?.IsValid()==true&&RawSettingPlan.Serial==Connection.ExpectedSerial&&AllowAdvancedSettingsRead&&AllowRawSettingsWrite&&RawSettingApproval?.Allows(RawSettingPlan)==true&&NetworkPlan is null&&IdlePlan is null&&IdleApproval is null&&ClockLocal is null;
        if(RawSettingPlan is not null||RawSettingApproval is not null)return false;
        if(Operation=="settings.idle.set")return NetworkPlan is null&&ClockLocal is null&&AllowIdleMinutesWrite&&IdlePlan?.IsValid()==true&&IdlePlan.Serial==Connection.ExpectedSerial&&IdleApproval?.Allows(IdlePlan)==true;
        if(IdlePlan is not null||IdleApproval is not null)return false;
        if(NetworkPlan is not null)return NetworkPlan.IsValidFor(Operation)&&ClockLocal is null&&Connection.Address==NetworkPlan.TargetAddress(Operation)&&Connection.ExpectedSerial==NetworkPlan.Serial&&(Operation=="network.ip.set"?AllowNetworkIpChange:AllowNetworkInspect);
        return Operation switch{"device.restart"=>AllowRestart&&ClockLocal is null,"information.read"=>ClockLocal is null,"settings.read"=>AllowAdvancedSettingsRead&&ClockLocal is null,"clock.set"=>AllowClockSet&&ValidClock(ClockLocal),_=>false};
    }
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
public sealed record ZkOperationOutput(string State, ZkDeviceInformation? Information=null,ZkAdvancedSettings? Settings=null,ZkNetworkObservation? Network=null,ZkIdleObservation? Idle=null,ZkRawSettingObservation? RawSetting=null,ZkFunctionKeyObservation? FunctionKey=null)
{
    public bool IsValidFor(ZkOperationInput input) => input.IsValid()&&(FunctionKey is null||input.Operation=="settings.key.set")&&(RawSetting is null||input.Operation=="settings.raw.set")&&(Idle is null||input.Operation=="settings.idle.set")&&(Network is null||input.Operation=="network.inspect")&&(State switch
    {
        "succeeded" => input.Operation switch
        {
            "settings.key.set" => Information is null&&Settings is null&&Network is null&&Idle is null&&RawSetting is null&&FunctionKey?.Matches(input.FunctionKeyPlan!)==true,
            "settings.raw.set" => Information is null&&Settings is null&&Network is null&&Idle is null&&RawSetting?.Matches(input.RawSettingPlan!)==true,
            "settings.idle.set" => Information is null&&Settings is null&&Network is null&&Idle is not null&&Idle.IsValid()&&Idle.Serial==input.IdlePlan!.Serial&&Idle.Product==input.IdlePlan.Product&&Idle.Firmware==input.IdlePlan.Firmware&&Idle.Minutes==input.IdlePlan.NewMinutes,
            "information.read" => Settings is null&&Information is not null&&Information.IsValid()&&Information.Serial==input.Connection.ExpectedSerial,
            "network.inspect" => Information is null&&Settings is null&&Network is not null&&Network.IsValid()&&Network.Serial==input.Connection.ExpectedSerial&&Network.ConnectedAddress==input.Connection.Address,
            "settings.read" => input.AllowAdvancedSettingsRead&&Information is null&&Settings is not null&&Settings.IsValid()&&Settings.Serial==input.Connection.ExpectedSerial&&
                (input.FunctionKeyReadApproval is null?Settings.FunctionKeys is null:input.FunctionKeyReadApproval.Allows(Settings.Product,Settings.Firmware)&&Settings.FunctionKeys?.Matches(input.FunctionKeyReadApproval)==true),
            "device.restart" => input.AllowRestart&&Information is null&&Settings is null,
            "clock.set" => input.AllowClockSet&&Information is null&&Settings is null,
            _=>false
        },
        "unsupported" or "identity-mismatch" or "failed" or "unknown" => Information is null&&Settings is null&&Network is null&&Idle is null&&RawSetting is null&&FunctionKey is null,
        _ => false
    });
}
