using System.Text;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Exact nullable Int32 results of the six observed GetDeviceInfo calls.
/// Not interpreted units, approved ranges, setting-write capabilities or an atomic snapshot.</summary>
public sealed record ZkAdvancedSettings(
    [property:JsonRequired]string Contract,[property:JsonRequired]string Serial,
    [property:JsonRequired]int? OneToOne,[property:JsonRequired]int? OneToN,
    [property:JsonRequired]int? RepeatTime,[property:JsonRequired]int? SleepTime,
    [property:JsonRequired]int? RegCard,[property:JsonRequired]int? MifareSector,string? Product=null,string? Firmware=null,string? ReportedIp=null,string? AlgorithmVersionRaw=null,ZkFunctionKeySnapshot? FunctionKeys=null,string? ReportedSubnetMask=null,string? ReportedGateway=null,ZkDaylightSnapshot? Daylight=null,ZkProfileReading[]? Profiles=null)
{
    public const string Version="zk-device-info-v1";
    public bool IsValid()
    {
        if(Contract!=Version||string.IsNullOrWhiteSpace(Serial)||Serial.Length>100||Serial.Trim()!=Serial||Serial.Any(char.IsControl))return false;
        if((Product is not null&&!ZkIdlePlan.Text(Product,120))||(Firmware is not null&&!ZkIdlePlan.Text(Firmware,120)))return false;
        if((ReportedIp is not null&&!ZkIdlePlan.Text(ReportedIp,100))||(AlgorithmVersionRaw is not null&&!ZkIdlePlan.Text(AlgorithmVersionRaw,100)))return false;
        if((ReportedSubnetMask is not null&&!ZkIdlePlan.Text(ReportedSubnetMask,100))||(ReportedGateway is not null&&!ZkIdlePlan.Text(ReportedGateway,100)))return false;
        if(FunctionKeys is not null&&!FunctionKeys.IsValid())return false;
        if(Daylight is not null&&!Daylight.IsValid())return false;
        if(Profiles is not null&&(Profiles.Length is <1 or >7||Profiles.Any(x=>x is null||!x.IsValid())||Profiles.Select(x=>x.Parameter).Distinct().Count()!=Profiles.Length))return false;
        try{_=new UTF8Encoding(false,true).GetByteCount(Serial);return true;}catch(EncoderFallbackException){return false;}
    }
}
public interface IZkAdvancedSettingsReadSession{int? ReadDeviceInfo(int code);}
public static class ZkAdvancedSettingsReader
{
    public static ZkAdvancedSettings Read(string serial,IZkAdvancedSettingsReadSession session)
    {
        int? Read(int code){try{return session.ReadDeviceInfo(code);}catch{return null;}}
        return new(ZkAdvancedSettings.Version,serial,Read(25),Read(23),Read(8),Read(4),Read(30),Read(62));
    }
}
