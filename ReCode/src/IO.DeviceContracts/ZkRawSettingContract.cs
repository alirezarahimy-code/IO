using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Five observed reference parameters only. No public numeric code or generic SDK option.</summary>
public static class ZkRawParameters
{
    public static bool IsKnown(string? value)=>value is "one-to-one" or "one-to-n" or "repeat-time" or "card-registration" or "mifare-sector";
    public static int Code(string value)=>value switch{"one-to-one"=>25,"one-to-n"=>23,"repeat-time"=>8,"card-registration"=>30,"mifare-sector"=>62,_=>throw new ArgumentException("Unknown fixed parameter.")};
    public static int? Value(ZkAdvancedSettings settings,string parameter)=>parameter switch{"one-to-one"=>settings.OneToOne,"one-to-n"=>settings.OneToN,"repeat-time"=>settings.RepeatTime,"card-registration"=>settings.RegCard,"mifare-sector"=>settings.MifareSector,_=>null};
}
public sealed record ZkRawSettingPlan(string Contract,Guid SnapshotId,string Serial,string Product,string Firmware,string Parameter,int ExpectedValue,int NewValue,string Unit,string Effect,string ApprovalReference)
{
    public const string Version="zk-approved-raw-setting-v1";
    public const string Confirmation="SET-EXACT-MODEL-APPROVED-RAW-SETTING";
    public bool IsValid()=>Contract==Version&&SnapshotId!=Guid.Empty&&ZkIdlePlan.Text(Serial,100)&&ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkRawParameters.IsKnown(Parameter)&&ExpectedValue!=NewValue&&ZkIdlePlan.Text(Unit,80)&&ZkIdlePlan.Text(Effect,200)&&ZkIdlePlan.Text(ApprovalReference,200);
    public string PreviewHash()=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));
}
/// <summary>Units/effect and exact raw values are installation evidence, NEVER inferred from Int32 or code.</summary>
public sealed record ZkRawSettingApproval(string Product,string Firmware,string Parameter,int[] ApprovedValues,string Unit,string Effect,string EvidenceReference)
{
    public bool IsValid()=>ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkRawParameters.IsKnown(Parameter)&&ApprovedValues is {Length:>1 and <=64}&&ApprovedValues.Distinct().Count()==ApprovedValues.Length&&ZkIdlePlan.Text(Unit,80)&&ZkIdlePlan.Text(Effect,200)&&ZkIdlePlan.Text(EvidenceReference,200);
    public bool Allows(ZkRawSettingPlan plan)=>IsValid()&&plan.IsValid()&&Product==plan.Product&&Firmware==plan.Firmware&&Parameter==plan.Parameter&&Unit==plan.Unit&&Effect==plan.Effect&&EvidenceReference==plan.ApprovalReference&&ApprovedValues.Contains(plan.ExpectedValue)&&ApprovedValues.Contains(plan.NewValue);
}
public sealed record ZkRawSettingObservation([property:JsonRequired]string Serial,[property:JsonRequired]string Product,[property:JsonRequired]string Firmware,[property:JsonRequired]string Parameter,[property:JsonRequired]int Value,[property:JsonRequired]string Unit,[property:JsonRequired]string Effect,[property:JsonRequired]string ApprovalReference)
{
    public bool IsValid()=>ZkIdlePlan.Text(Serial,100)&&ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkRawParameters.IsKnown(Parameter)&&ZkIdlePlan.Text(Unit,80)&&ZkIdlePlan.Text(Effect,200)&&ZkIdlePlan.Text(ApprovalReference,200);
    public bool Matches(ZkRawSettingPlan plan)=>IsValid()&&Serial==plan.Serial&&Product==plan.Product&&Firmware==plan.Firmware&&Parameter==plan.Parameter&&Value==plan.NewValue&&Unit==plan.Unit&&Effect==plan.Effect&&ApprovalReference==plan.ApprovalReference;
}
public interface IZkRawSettingSession
{
    string? Serial();string? Product();string? Firmware();int? Read(string parameter);bool Set(string parameter,int value);
}
public static class ZkRawSettingExecutor
{
    public static ZkOperationOutput Execute(ZkOperationInput input,IZkRawSettingSession session)
    {
        if(!input.IsValid()||input.Operation!="settings.raw.set"||input.RawSettingPlan is not{} plan)return new("unsupported");
        try{if(session.Serial()!=plan.Serial||session.Product()!=plan.Product||session.Firmware()!=plan.Firmware||session.Read(plan.Parameter)!=plan.ExpectedValue)return new("failed");}catch{return new("failed");}
        try
        {
            // Dispatch exactly once. Even SDK false can follow an applied write.
            if(!session.Set(plan.Parameter,plan.NewValue))return new("unknown");
            if(session.Read(plan.Parameter)!=plan.NewValue||session.Product()!=plan.Product||session.Firmware()!=plan.Firmware||session.Serial()!=plan.Serial)return new("unknown");
            return new("succeeded",RawSetting:new(plan.Serial,plan.Product,plan.Firmware,plan.Parameter,plan.NewValue,plan.Unit,plan.Effect,plan.ApprovalReference));
        }
        catch{return new("unknown");}
    }
}
