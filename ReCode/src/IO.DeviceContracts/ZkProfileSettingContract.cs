using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
namespace IO.DeviceContracts;

/// <summary>Seven CLOSED model-approved settings, not an arbitrary option/code execution interface.</summary>
public static class ZkProfileParameters
{
    public static readonly string[] All=["daylight","algorithm","mask","gateway","dhcp","mac","port"];
    public static bool Known(string? p)=>All.Contains(p,StringComparer.Ordinal);
    public static bool Network(string p)=>p is "mask" or "gateway" or "dhcp" or "mac" or "port";
    public static string Option(string p)=>p switch{"algorithm"=>"~ZKFPVersion","mask"=>"NetMask","gateway"=>"GATEIPAddress","dhcp"=>"DHCP","port"=>"TCPPort",_=>throw new ArgumentException("Not a fixed option.")};
    public static string WritePermission(string p)=>Known(p)?"devices.profile."+p+".set":throw new ArgumentException("Unknown profile parameter.");
    public static string ExecutePermission(string p)=>Known(p)?"devices.profile."+p+".execute":throw new ArgumentException("Unknown profile parameter.");
    public static bool Address(string? text)=>text=="0.0.0.0"||ZkNetworkPlan.Ipv4(text);
    public static bool Mask(string? text)
    {
        if(text is null)return false;var parts=text.Split('.');
        if(parts.Length!=4||!parts.All(x=>byte.TryParse(x,NumberStyles.None,CultureInfo.InvariantCulture,out var n)&&n.ToString(CultureInfo.InvariantCulture)==x))return false;
        uint value=0;foreach(var part in parts)value=(value<<8)|byte.Parse(part,CultureInfo.InvariantCulture);
        uint inverse=~value;return value!=0&&(inverse&(inverse+1))==0;
    }
    public static bool Port(string? s)=>int.TryParse(s,NumberStyles.None,CultureInfo.InvariantCulture,out var n)&&n is >=1 and <=65535&&n.ToString(CultureInfo.InvariantCulture)==s;
    public static bool Annual(string? s)=>s is {Length:8}&&s.All(char.IsAsciiDigit)&&DateTime.TryParseExact("2000"+s,"yyyyMMddHHmm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _);
}
public sealed record ZkProfileValue([property:JsonRequired]string Parameter,[property:JsonRequired]string Raw,string? Begin=null,string? End=null,string? Mode=null,string? CurrentMode=null,string? Enabled=null)
{
    public bool IsValid()
    {
        if(!ZkProfileParameters.Known(Parameter)||!ZkIdlePlan.Text(Raw,128))return false;
        if(Parameter=="daylight")return Raw is "0" or "1"&&ZkProfileParameters.Annual(Begin)&&ZkProfileParameters.Annual(End)&&ZkIdlePlan.Text(Mode,32)&&ZkIdlePlan.Text(CurrentMode,32)&&Enabled is "0" or "1";
        if(Begin is not null||End is not null||Mode is not null||CurrentMode is not null||Enabled is not null)return false;
        return Parameter switch
        {
            "algorithm"=>ZkIdlePlan.Text(Raw,32),"mask"=>ZkProfileParameters.Mask(Raw),"gateway"=>ZkProfileParameters.Address(Raw),"dhcp"=>Raw is "0" or "1",
            "mac"=>Regex.IsMatch(Raw,"\\A[0-9A-F]{2}(:[0-9A-F]{2}){5}\\z",RegexOptions.CultureInvariant)&&Raw!="00:00:00:00:00:00"&&(Convert.ToByte(Raw[..2],16)&1)==0,
            "port"=>ZkProfileParameters.Port(Raw),_=>false
        };
    }
}
public sealed record ZkProfileReadApproval(string Product,string Firmware,string Parameter,string EvidenceReference)
{
    public bool IsValid()=>ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkProfileParameters.Known(Parameter)&&ZkIdlePlan.Text(EvidenceReference,200);
    public bool Allows(string? product,string? firmware)=>IsValid()&&Product==product&&Firmware==firmware;
}
public sealed record ZkProfileReading([property:JsonRequired]string Parameter,[property:JsonRequired]string ApprovalReference,[property:JsonRequired]bool Reported,ZkProfileValue? Value=null)
{
    public bool IsValid()=>ZkProfileParameters.Known(Parameter)&&ZkIdlePlan.Text(ApprovalReference,200)&&(Reported?Value?.IsValid()==true&&Value.Parameter==Parameter:Value is null);
}
public sealed record ZkProfileEndpoint(string Address,int Port)
{
    // Literal registered/installed endpoints only. Explicit loopback pins also permit the private test relay;
    // they grant no destination access without a separate matching installation allowlist.
    public bool IsValid()=>Address is not null&&(ZkNetworkPlan.Ipv4(Address)||(Address.StartsWith("127.",StringComparison.Ordinal)&&System.Net.IPAddress.TryParse(Address,out var ip)&&ip.ToString()==Address))&&Port is >=1 and <=65535;
}
public sealed record ZkProfilePlan(string Contract,Guid SnapshotId,string Serial,string Product,string Firmware,string TimeZoneId,ZkProfileValue Expected,ZkProfileValue Desired,string Effect,string ApprovalReference,ZkProfileEndpoint Original,ZkProfileEndpoint Recovery)
{
    public const string Version="zk-approved-profile-v1",Confirmation="SET-EXACT-APPROVED-PROFILE",ReviewConfirmation="REVIEWED-EXACT-PROFILE";
    public string Parameter=>Expected.Parameter;
    public bool IsValid()=>Contract==Version&&SnapshotId!=Guid.Empty&&ZkIdlePlan.Text(Serial,100)&&ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkIdlePlan.Text(TimeZoneId,100)&&Expected?.IsValid()==true&&Desired?.IsValid()==true&&Expected.Parameter==Desired.Parameter&&Expected!=Desired&&ZkIdlePlan.Text(Effect,200)&&ZkIdlePlan.Text(ApprovalReference,200)&&Original?.IsValid()==true&&Recovery?.IsValid()==true&&
        (ZkProfileParameters.Network(Parameter)||Original==Recovery)&&
        (Parameter!="port"||(Original.Address==Recovery.Address&&Recovery.Port==int.Parse(Desired.Raw,CultureInfo.InvariantCulture)))&&
        (Parameter!="daylight"||(Desired.Enabled==Desired.Raw&&Desired.Mode==(Desired.Raw=="1"?"0":Expected.Mode)&&Desired.CurrentMode==(Desired.Raw=="0"?"0":Expected.CurrentMode)));
    public string PreviewHash()=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));
}
/// <summary>Installation evidence must cover exact old/new states, side effects and both recovery endpoints.
/// Algorithm writes require explicit evidence of template compatibility/preservation; no template transfer/delete is authorized.</summary>
public sealed record ZkProfileWriteApproval(string Product,string Firmware,string TimeZoneId,string Parameter,ZkProfileValue[] ApprovedStates,string Effect,string EvidenceReference,ZkProfileEndpoint[] ApprovedEndpoints,bool EffectsReviewed=false,bool TemplatesPreserved=false,string? AnnualInterpretation=null)
{
    public bool IsValid()=>EffectsReviewed&&ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkIdlePlan.Text(TimeZoneId,100)&&ZkProfileParameters.Known(Parameter)&&ZkIdlePlan.Text(Effect,200)&&ZkIdlePlan.Text(EvidenceReference,200)&&ApprovedStates is {Length:>=2 and <=8}&&ApprovedStates.All(x=>x?.IsValid()==true&&x.Parameter==Parameter)&&ApprovedStates.Distinct().Count()==ApprovedStates.Length&&ApprovedEndpoints is {Length:>=1 and <=2}&&ApprovedEndpoints.All(x=>x?.IsValid()==true)&&ApprovedEndpoints.Distinct().Count()==ApprovedEndpoints.Length&&(Parameter!="algorithm"||TemplatesPreserved)&&(Parameter!="daylight"||ZkIdlePlan.Text(AnnualInterpretation,200));
    public bool Allows(ZkProfilePlan p)=>IsValid()&&p.IsValid()&&Product==p.Product&&Firmware==p.Firmware&&TimeZoneId==p.TimeZoneId&&Parameter==p.Parameter&&Effect==p.Effect&&EvidenceReference==p.ApprovalReference&&ApprovedStates.Contains(p.Expected)&&ApprovedStates.Contains(p.Desired)&&ApprovedEndpoints.Contains(p.Original)&&ApprovedEndpoints.Contains(p.Recovery);
}
public sealed record ZkProfileObservation([property:JsonRequired]string Serial,[property:JsonRequired]string Product,[property:JsonRequired]string Firmware,[property:JsonRequired]ZkProfileValue Value,[property:JsonRequired]ZkProfileEndpoint Connected,[property:JsonRequired]string ApprovalReference)
{
    public bool Matches(ZkProfilePlan p,bool inspect,bool recovery)=>Value?.IsValid()==true&&Serial==p.Serial&&Product==p.Product&&Firmware==p.Firmware&&ApprovalReference==p.ApprovalReference&&Connected==(recovery?p.Recovery:p.Original)&&(inspect?(Value==p.Expected||Value==p.Desired):Value==p.Desired&&!ZkProfileParameters.Network(p.Parameter));
}
public interface IZkProfileSession
{
    string? Serial();string? Product();string? Firmware();ZkProfileValue? ReadProfile(string parameter);bool SetProfile(ZkProfileValue value);void RefreshProfile();
}
public static class ZkProfileExecutor
{
    public static ZkOperationOutput Execute(ZkOperationInput input,IZkProfileSession s)
    {
        if(!input.IsValid()||input.ProfilePlan is not{} p||input.Operation is not("settings.profile.set" or "settings.profile.inspect"))return new("unsupported");
        ZkProfileValue? before;
        try{if(s.Serial()!=p.Serial||s.Product()!=p.Product||s.Firmware()!=p.Firmware)return new("identity-mismatch");before=s.ReadProfile(p.Parameter);if(before?.IsValid()!=true)return new("failed");}catch{return new("failed");}
        if(input.Operation=="settings.profile.inspect")
        {
            try{if((before!=p.Expected&&before!=p.Desired)||s.Serial()!=p.Serial||s.Product()!=p.Product||s.Firmware()!=p.Firmware)return new("failed");return new("succeeded",Profile:new(p.Serial,p.Product,p.Firmware,before,input.ProfileObserveRecovery?p.Recovery:p.Original,p.ApprovalReference));}catch{return new("failed");}
        }
        if(before!=p.Expected)return new("failed");
        try
        {
            var ack=s.SetProfile(p.Desired); // Exactly one dispatch, no implicit reboot/disable or fallback.
            if(ZkProfileParameters.Network(p.Parameter)){if(ack&&p.Parameter=="mac")s.RefreshProfile();return new("unknown");}
            if(!ack||s.ReadProfile(p.Parameter)!=p.Desired||s.Serial()!=p.Serial||s.Product()!=p.Product||s.Firmware()!=p.Firmware)return new("unknown");
            return new("succeeded",Profile:new(p.Serial,p.Product,p.Firmware,p.Desired,p.Original,p.ApprovalReference));
        }
        catch{return new("unknown");}
    }
    public static ZkAdvancedSettings? AddReadings(ZkAdvancedSettings snapshot,ZkProfileReadApproval[] approvals,IZkProfileSession session)
    {
        if(!snapshot.IsValid()||approvals is not{Length:>=1 and <=7}||approvals.Any(x=>x is null||!x.Allows(snapshot.Product,snapshot.Firmware))||approvals.Select(x=>x.Parameter).Distinct().Count()!=approvals.Length)return null;
        var values=new List<ZkProfileReading>();
        foreach(var a in approvals){ZkProfileValue? v=null;try{v=session.ReadProfile(a.Parameter);}catch{}if(v?.IsValid()!=true||v.Parameter!=a.Parameter)v=null;values.Add(new(a.Parameter,a.EvidenceReference,v is not null,v));}
        return snapshot with{Profiles=values.ToArray()};
    }
}
