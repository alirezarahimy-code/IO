using System.Text.Json.Serialization;
namespace IO.DeviceContracts;
public sealed record ZkAttendanceReadInput(ZkProbeInput Connection,bool ReadAttendance,bool AllowAttendanceRead,string ReadFormat=ZkAttendanceReadFormats.Ssr)
{
    public bool IsValid()=>Connection?.IsValid()==true&&ReadAttendance&&AllowAttendanceRead&&ZkAttendanceReadFormats.IsKnown(ReadFormat);
    public override string ToString()=>"ZkAttendanceReadInput [private IPC; values redacted]";
}
/// <summary>Exact typed values exposed by SSR, not wire bytes or a vendor event id.
/// Invalid identities/calendar values must remain available for quarantine.</summary>
public sealed record ZkAttendanceRaw([property:JsonRequired]string? PersonIdentifier,[property:JsonRequired]int VerifyMode,[property:JsonRequired]int CheckType,[property:JsonRequired]int Year,[property:JsonRequired]int Month,[property:JsonRequired]int Day,[property:JsonRequired]int Hour,[property:JsonRequired]int Minute,[property:JsonRequired]int Second,[property:JsonRequired]int WorkCode,int? LegacyTerminalNumber=null,int? LegacyEnrollmentMachineNumber=null)
{
    public bool IsBounded()
    {
        if(PersonIdentifier is null)return true;if(PersonIdentifier.Length>100)return false;
        try{_=new System.Text.UTF8Encoding(false,true).GetByteCount(PersonIdentifier);return true;}catch(System.Text.EncoderFallbackException){return false;}
    }
    // v1 identity bytes must not depend on serializer options, property reflection
    // order, HTML escaping policy, current culture, or a runtime's Unicode blocklist.
    public string CanonicalJson()
    {
        if(!IsBounded())throw new ArgumentException("Unsupported SDK identity encoding.");
        var culture=System.Globalization.CultureInfo.InvariantCulture;
        var identity=PersonIdentifier is null?"null":"\""+string.Concat(PersonIdentifier.Select(c=>"\\u"+((int)c).ToString("X4",culture)))+"\"";
        var names=new[]{"VerifyMode","CheckType","Year","Month","Day","Hour","Minute","Second","WorkCode"};
        var values=new[]{VerifyMode,CheckType,Year,Month,Day,Hour,Minute,Second,WorkCode};
        var legacy=LegacyTerminalNumber is null&&LegacyEnrollmentMachineNumber is null?"":",\"LegacyTerminalNumber\":"+(LegacyTerminalNumber?.ToString(culture)??"null")+",\"LegacyEnrollmentMachineNumber\":"+(LegacyEnrollmentMachineNumber?.ToString(culture)??"null");
        return "{\"PersonIdentifier\":"+identity+","+string.Join(",",names.Select((name,i)=>"\""+name+"\":"+values[i].ToString(culture)))+legacy+"}";
    }
    public override string ToString()=>"ZkAttendanceRaw [personal data redacted]";
    public (string Status,DateTimeOffset? Utc) Interpret(TimeZoneInfo zone)
    {
        if(string.IsNullOrWhiteSpace(PersonIdentifier)||PersonIdentifier!=PersonIdentifier.Trim()||PersonIdentifier.Any(char.IsControl))return("invalid-identity",null);
        try
        {
            var local=new DateTime(Year,Month,Day,Hour,Minute,Second,DateTimeKind.Unspecified);
            if(zone.IsInvalidTime(local))return("invalid-local-time",null);
            if(zone.IsAmbiguousTime(local))return("ambiguous-local-time",null);
            return("parsed",new DateTimeOffset(local,zone.GetUtcOffset(local)).ToUniversalTime());
        }
        catch(ArgumentException){return("invalid-time",null);}
    }
}
public sealed record ZkAttendanceFrame(string Type,string? Serial=null,ZkAttendanceRaw? Record=null,long? Count=null,string? State=null,string? ReadFormat=null);
