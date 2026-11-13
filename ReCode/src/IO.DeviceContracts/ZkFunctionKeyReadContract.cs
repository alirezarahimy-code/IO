using System.Text;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Installation approval for read-only SSR_GetShortkey on one exact model/firmware.
/// Eight indices is a per-snapshot application budget, NOT the device's key count/range.</summary>
public sealed record ZkFunctionKeyReadApproval(string Product,string Firmware,int[] Indices,string EvidenceReference)
{
    public bool IsValid()=>ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkIdlePlan.Text(EvidenceReference,200)&&
        Indices is {Length:>0 and <=8}&&Indices.All(x=>x>=0)&&Indices.Distinct().Count()==Indices.Length;
    public bool Allows(string? product,string? firmware)=>IsValid()&&Product==product&&Firmware==firmware;
}
public sealed record ZkFunctionKeyValue(
    [property:JsonRequired]int Index,[property:JsonRequired]bool Reported,
    int? Function=null,int? StateCode=null,string? StateName=null,int? AutoChange=null,string? AutoChangeTime=null)
{
    // Empty/raw whitespace is not interpreted or trimmed. Invalid/oversized text is unavailable, not truncated.
    private static bool Raw(string? text)=>text is not null&&text.Length<=128&&!text.Any(char.IsControl)&&ValidUtf8(text);
    private static bool ValidUtf8(string text){try{_=new UTF8Encoding(false,true).GetByteCount(text);return true;}catch(EncoderFallbackException){return false;}}
    public bool IsValid()=>Index>=0&&(Reported?
        Function is not null&&StateCode is not null&&AutoChange is not null&&Raw(StateName)&&Raw(AutoChangeTime):
        Function is null&&StateCode is null&&StateName is null&&AutoChange is null&&AutoChangeTime is null);
}
public sealed record ZkFunctionKeySnapshot(
    [property:JsonRequired]string Contract,[property:JsonRequired]string ApprovalReference,
    [property:JsonRequired]ZkFunctionKeyValue[] Items)
{
    public const string Version="zk-ssr-shortkey-read-v1";
    public bool IsValid()=>Contract==Version&&ZkIdlePlan.Text(ApprovalReference,200)&&Items is {Length:>0 and <=8}&&
        Items.All(x=>x is not null&&x.IsValid())&&Items.Select(x=>x.Index).Distinct().Count()==Items.Length;
    public bool Matches(ZkFunctionKeyReadApproval approval)=>IsValid()&&approval.IsValid()&&ApprovalReference==approval.EvidenceReference&&Items.Select(x=>x.Index).SequenceEqual(approval.Indices);
}
public interface IZkFunctionKeyReadSession
{
    // null means SDK false/unsupported. Never return initialized zeroes as a successful read.
    ZkFunctionKeyValue? Read(int index);
}
public static class ZkFunctionKeyReader
{
    // Caller owns the connection and must serial-pin before AND after all observations.
    // Model mismatch is unsupported and must not call the key API even once.
    public static ZkAdvancedSettings? Add(ZkAdvancedSettings snapshot,ZkFunctionKeyReadApproval approval,IZkFunctionKeyReadSession session)
    {
        if(!snapshot.IsValid()||!approval.Allows(snapshot.Product,snapshot.Firmware))return null;
        var values=new List<ZkFunctionKeyValue>();
        foreach(var index in approval.Indices)
        {
            ZkFunctionKeyValue? value=null;
            try{value=session.Read(index);}catch{ /* Independent unavailable observation, no fallback/write. */ }
            values.Add(value is {Reported:true}&&value.Index==index&&value.IsValid()?value:new(index,false));
        }
        return snapshot with{FunctionKeys=new(ZkFunctionKeySnapshot.Version,approval.EvidenceReference,values.ToArray())};
    }
}
