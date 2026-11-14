using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
namespace IO.DeviceContracts;

/// <summary>Exact complete tuples, never the legacy implicit AutoChange reset.</summary>
public sealed record ZkFunctionKeyPlan(string Contract,Guid SnapshotId,string Serial,string Product,string Firmware,ZkFunctionKeyValue Expected,ZkFunctionKeyValue Desired,string Effect,string ApprovalReference)
{
 public const string Version="zk-approved-function-key-v1",Confirmation="SET-EXACT-APPROVED-FUNCTION-KEY";
 public bool IsValid()=>Contract==Version&&SnapshotId!=Guid.Empty&&ZkIdlePlan.Text(Serial,100)&&ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&Expected is {Reported:true}&&Expected.IsValid()&&Desired is {Reported:true}&&Desired.IsValid()&&Expected.Index==Desired.Index&&Expected!=Desired&&ZkIdlePlan.Text(Effect,200)&&ZkIdlePlan.Text(ApprovalReference,200);
 public string PreviewHash()=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this)));
}
/// <summary>Every field of both old and new states must be approved for this exact model/index.
/// No inferred function/state ranges, schedule syntax, time units or automatic clearing.</summary>
public sealed record ZkFunctionKeyWriteApproval(string Product,string Firmware,ZkFunctionKeyValue[] ApprovedStates,string Effect,string EvidenceReference,bool AttendanceOnly=false)
{
 public bool IsValid()=>AttendanceOnly&&ZkIdlePlan.Text(Product,120)&&ZkIdlePlan.Text(Firmware,120)&&ZkIdlePlan.Text(Effect,200)&&ZkIdlePlan.Text(EvidenceReference,200)&&ApprovedStates is {Length:>=2 and <=8}&&ApprovedStates.All(x=>x is {Reported:true}&&x.IsValid())&&ApprovedStates.Select(x=>x.Index).Distinct().Count()==1&&ApprovedStates.Distinct().Count()==ApprovedStates.Length;
 public bool Allows(ZkFunctionKeyPlan plan)=>IsValid()&&plan.IsValid()&&Product==plan.Product&&Firmware==plan.Firmware&&Effect==plan.Effect&&EvidenceReference==plan.ApprovalReference&&ApprovedStates.Contains(plan.Expected)&&ApprovedStates.Contains(plan.Desired);
}
public sealed record ZkFunctionKeyObservation([property:JsonRequired]string Serial,[property:JsonRequired]string Product,[property:JsonRequired]string Firmware,[property:JsonRequired]ZkFunctionKeyValue Key,[property:JsonRequired]string Effect,[property:JsonRequired]string ApprovalReference)
{
 public bool Matches(ZkFunctionKeyPlan plan)=>Serial==plan.Serial&&Product==plan.Product&&Firmware==plan.Firmware&&Key is {Reported:true}&&Key.IsValid()&&Key==plan.Desired&&Effect==plan.Effect&&ApprovalReference==plan.ApprovalReference;
}
public interface IZkFunctionKeyWriteSession
{
 string? Serial();string? Product();string? Firmware();ZkFunctionKeyValue? Read(int index);bool Set(ZkFunctionKeyValue value);
}
public static class ZkFunctionKeyWriteExecutor
{
 public static ZkOperationOutput Execute(ZkOperationInput input,IZkFunctionKeyWriteSession session)
 {
  if(!input.IsValid()||input.Operation!="settings.key.set"||input.FunctionKeyPlan is not{} plan)return new("unsupported");
  try{if(session.Serial()!=plan.Serial||session.Product()!=plan.Product||session.Firmware()!=plan.Firmware||session.Read(plan.Expected.Index)!=plan.Expected)return new("failed");}catch{return new("failed");}
  try
  {
   if(!session.Set(plan.Desired))return new("unknown");
   if(session.Read(plan.Desired.Index)!=plan.Desired||session.Serial()!=plan.Serial||session.Product()!=plan.Product||session.Firmware()!=plan.Firmware)return new("unknown");
   return new("succeeded",FunctionKey:new(plan.Serial,plan.Product,plan.Firmware,plan.Desired,plan.Effect,plan.ApprovalReference));
  }
  catch{return new("unknown");}
 }
}
