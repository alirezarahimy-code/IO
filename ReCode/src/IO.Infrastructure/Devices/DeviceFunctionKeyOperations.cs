using IO.DeviceContracts;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Devices;
public sealed partial class DeviceOperations
{
 public const string SetFunctionKey="devices.settings.keys.set",ExecuteFunctionKeyWrite="devices.settings.keys.write.execute";
 private async Task<bool> CanReadKey(Guid actor,Guid device,CancellationToken ct)=>await transaction.HasDevicePermissionAsync(actor,ReadSettings,device,ct)&&await transaction.HasDevicePermissionAsync(actor,ReadFunctionKeys,device,ct);
 private static ZkFunctionKeyPlan? ParseKey(string? json)=>ParseTyped<ZkFunctionKeyPlan>(json);
 public Task<object> PreviewFunctionKeyAsync(Guid actor,Guid deviceId,byte[] version,Guid snapshotId,ZkFunctionKeyValue desired,string effect,string approvalReference,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,SetFunctionKey,"device-setting.key-previewed",async token=>
 {
  if(!await CanReadKey(actor,deviceId,token))throw new UnauthorizedAccessException();
  await transaction.LockAsync("IO.DeviceConnections",token);var plan=await BuildKeyPlan(deviceId,version,snapshotId,desired,effect,approvalReference,token);
  return(new{plan,previewHash=plan.PreviewHash(),installedModelApprovalNotProven=true},deviceId);
 },ct);
 private async Task<ZkFunctionKeyPlan> BuildKeyPlan(Guid deviceId,byte[] version,Guid snapshotId,ZkFunctionKeyValue desired,string effect,string approvalReference,CancellationToken ct)
 {
  if(deviceId==Guid.Empty||version is not{Length:8}||snapshotId==Guid.Empty||desired is not{Reported:true}||!desired.IsValid())throw new ArgumentException("Complete reported desired tuple and snapshot required.");
  var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deviceId,ct);var source=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==snapshotId,ct);var snapshot=ParseSettings(source?.ResultJson);
  var zone=device is null?null:await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(ct);
  var old=snapshot?.FunctionKeys?.Items.SingleOrDefault(x=>x.Index==desired.Index);
  if(device is null||!device.Enabled||device.Profile!="zk-standalone"||!device.Version.SequenceEqual(version)||source is null||source.DeviceId!=deviceId||source.Kind!="settings.read"||source.State!="succeeded"||source.RequestedAtUtc<clock.GetUtcNow().AddMinutes(-5)||source.RequestedAtUtc>clock.GetUtcNow()||source.TimeZoneId!=zone||!source.ExpectedDeviceVersion.SequenceEqual(version)||snapshot?.IsValid()!=true||snapshot.Product is null||snapshot.Firmware is null||old is not{Reported:true})throw new DomainConflictException("Fresh same-model complete key source snapshot required.");
  var plan=new ZkFunctionKeyPlan(ZkFunctionKeyPlan.Version,snapshotId,snapshot.Serial,snapshot.Product,snapshot.Firmware,old,desired,effect,approvalReference);if(!plan.IsValid())throw new ArgumentException("Different complete key tuple and exact effect/reference required.");return plan;
 }
 public Task<Guid> ResolveFunctionKeyAsync(Guid actor,Guid id,byte[] version,Guid snapshotId,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,SetFunctionKey,"device-operation.key-reviewed",async token=>
 {
  if(confirmation!="REVIEWED-APPROVED-FUNCTION-KEY")throw new ArgumentException("Explicit full key observation review required.");
  await transaction.LockAsync("IO.DeviceConnections",token);var item=await Load(id,version,token);
  if(item.Kind!="settings.key.set"||item.State!="unknown"||item.ResolvedAtUtc is not null)throw new DomainConflictException("Unresolved key write required.");
  if(!await CanReadKey(actor,item.DeviceId,token))throw new UnauthorizedAccessException();
  var plan=ParseKey(item.FunctionKeyPlanJson);var source=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==snapshotId,token);var observed=ParseSettings(source?.ResultJson);var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==item.DeviceId,token);var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
  var value=observed?.FunctionKeys?.Items.SingleOrDefault(x=>x.Index==plan?.Expected.Index);
  if(plan?.IsValid()!=true||source is null||source.Kind!="settings.read"||source.State!="succeeded"||source.DeviceId!=item.DeviceId||source.RequestedAtUtc<=item.FinishedAtUtc||source.TimeZoneId!=item.TimeZoneId||zone!=item.TimeZoneId||!source.ExpectedDeviceVersion.SequenceEqual(item.ExpectedDeviceVersion)||!device.Version.SequenceEqual(item.ExpectedDeviceVersion)||observed?.IsValid()!=true||observed.Serial!=plan.Serial||observed.Product!=plan.Product||observed.Firmware!=plan.Firmware||value is not{Reported:true}||(value!=plan.Expected&&value!=plan.Desired))throw new DomainConflictException("Later same-model complete old/new key observation required; never retry unknown writes.");
  item.ResolutionInfoId=source.Id;item.ResolvedByUserId=actor;item.ResolvedAtUtc=clock.GetUtcNow();return(id,id);
 },ct);
}
