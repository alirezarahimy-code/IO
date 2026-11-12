using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Closed code-4 contract, not arbitrary SetDeviceInfo. The 1..999 application envelope
/// comes from specific vendor user manuals, never universal device compatibility or zero semantics.</summary>
public sealed record ZkIdlePlan(string Contract,Guid SnapshotId,string Serial,string Product,string Firmware,int ExpectedMinutes,int NewMinutes,string ApprovalReference)
{
    public const string Version="zk-idle-minutes-v1";
    public const string Confirmation="SET-IDLE-MINUTES-MAY-POWER-OFF";
    public bool IsValid()=>Contract==Version&&SnapshotId!=Guid.Empty&&Text(Serial,100)&&Text(Product,120)&&Text(Firmware,120)&&Text(ApprovalReference,200)&&
        ExpectedMinutes is >=1 and <=999&&NewMinutes is >=1 and <=999&&ExpectedMinutes!=NewMinutes;
    public string PreviewHash()=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));
    public static bool Text(string? value,int max)
    {
        if(string.IsNullOrWhiteSpace(value)||value.Length>max||value!=value.Trim()||value.Any(char.IsControl))return false;
        try{_=new UTF8Encoding(false,true).GetByteCount(value);return true;}catch(EncoderFallbackException){return false;}
    }
}
public sealed record ZkIdleApproval(string Product,string Firmware,int[] ApprovedMinutes,string EvidenceReference,string Effect)
{
    public bool IsValid()=>ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkIdlePlan.Text(EvidenceReference,200)&&Effect=="standby-or-power-off"&&ApprovedMinutes is {Length:>0 and <=64}&&ApprovedMinutes.All(x=>x is >=1 and <=999)&&ApprovedMinutes.Distinct().Count()==ApprovedMinutes.Length;
    public bool Allows(ZkIdlePlan plan)=>IsValid()&&plan.IsValid()&&Product==plan.Product&&Firmware==plan.Firmware&&EvidenceReference==plan.ApprovalReference&&ApprovedMinutes.Contains(plan.NewMinutes);
}
public sealed record ZkIdleObservation([property:JsonRequired]string Serial,[property:JsonRequired]string Product,[property:JsonRequired]string Firmware,[property:JsonRequired]int Minutes)
{
    public bool IsValid()=>ZkIdlePlan.Text(Serial,100)&&ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&Minutes is >=1 and <=999;
}
public interface IZkIdleSettingSession
{
    string? Product();
    string? Firmware();
    int? ReadIdleMinutes();
    bool SetIdleMinutes(int minutes);
}
public static class ZkIdleSettingExecutor
{
    // Caller must connect and serial-pin first. No DisableDevice, RefreshData, restart, or fallback.
    public static ZkOperationOutput Execute(ZkOperationInput input,IZkIdleSettingSession session)
    {
        if(!input.IsValid()||input.Operation!="settings.idle.set"||input.IdlePlan is not{} plan)return new("unsupported");
        try{if(session.Product()!=plan.Product||session.Firmware()!=plan.Firmware||session.ReadIdleMinutes()!=plan.ExpectedMinutes)return new("failed");}
        catch{return new("failed");}
        try
        {
            if(!session.SetIdleMinutes(plan.NewMinutes))return new("unknown");
            if(session.ReadIdleMinutes()!=plan.NewMinutes||session.Product()!=plan.Product||session.Firmware()!=plan.Firmware)return new("unknown");
            return new("succeeded",Idle:new(plan.Serial,plan.Product,plan.Firmware,plan.NewMinutes));
        }
        catch{return new("unknown");}
    }
}
