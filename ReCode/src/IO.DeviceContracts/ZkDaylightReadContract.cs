using System.Text;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Installed read-only GetDaylight approval; no date grammar, timezone or write authority is implied.</summary>
public sealed record ZkDaylightReadApproval(string Product,string Firmware,string EvidenceReference)
{
    public bool IsValid()=>ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkIdlePlan.Text(EvidenceReference,200);
    public bool Allows(string? product,string? firmware)=>IsValid()&&Product==product&&Firmware==firmware;
}
public sealed record ZkDaylightValue([property:JsonRequired]int Support,[property:JsonRequired]string BeginTime,[property:JsonRequired]string EndTime)
{
    private static bool Raw(string? text)
    {
        if(text is null||text.Length>128||text.Any(char.IsControl))return false;
        try{_=new UTF8Encoding(false,true).GetByteCount(text);return true;}catch(EncoderFallbackException){return false;}
    }
    // Preserve signed/unknown Support values and opaque strings, including empty/whitespace.
    public bool IsValid()=>Raw(BeginTime)&&Raw(EndTime);
}
public sealed record ZkDaylightSnapshot([property:JsonRequired]string Contract,[property:JsonRequired]string ApprovalReference,[property:JsonRequired]bool Reported,ZkDaylightValue? Value=null)
{
    public const string Version="zk-daylight-read-v1";
    public bool IsValid()=>Contract==Version&&ZkIdlePlan.Text(ApprovalReference,200)&&(Reported?Value?.IsValid()==true:Value is null);
    public bool Matches(ZkDaylightReadApproval approval)=>IsValid()&&approval.IsValid()&&ApprovalReference==approval.EvidenceReference;
}
public interface IZkDaylightReadSession
{
    // SDK false must yield null, never initialized zeroes/empty strings as successful observations.
    ZkDaylightValue? ReadDaylight();
}
public static class ZkDaylightReader
{
    // Caller owns connection and serial pins before AND after all observations.
    public static ZkAdvancedSettings? Add(ZkAdvancedSettings snapshot,ZkDaylightReadApproval approval,IZkDaylightReadSession session)
    {
        if(!snapshot.IsValid()||!approval.Allows(snapshot.Product,snapshot.Firmware))return null;
        ZkDaylightValue? value=null;
        try{value=session.ReadDaylight();}catch{ /* Unavailable independent observation, not proof of unsupported hardware. */ }
        if(value?.IsValid()!=true)value=null;
        return snapshot with{Daylight=new(ZkDaylightSnapshot.Version,approval.EvidenceReference,value is not null,value)};
    }
}
