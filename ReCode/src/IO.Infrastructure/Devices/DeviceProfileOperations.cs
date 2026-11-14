using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Storage;
using IO.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Devices;
public sealed partial class DeviceOperations
{
    private static bool ProfileKind(string kind)=>kind is "settings.profile.set" or "settings.profile.inspect";
    private static ZkProfilePlan? ParseProfile(string? json)=>ParseTyped<ZkProfilePlan>(json);
    private static bool SameProfile(ZkProfilePlan? a,ZkProfilePlan? b)=>a==b;
    public Task<object> PreviewProfileAsync(Guid actor,Guid deviceId,byte[] version,Guid snapshotId,ZkProfileValue desired,string effect,string reference,ZkProfileEndpoint recovery,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,ZkProfileParameters.WritePermission((desired??throw new ArgumentException("Complete desired state required.")).Parameter),"device-profile.previewed",async token=>
    {
        if(!await transaction.HasDevicePermissionAsync(actor,ReadSettings,deviceId,token))throw new UnauthorizedAccessException();
        await transaction.LockAsync("IO.DeviceConnections",token);var plan=await BuildProfilePlan(deviceId,version,snapshotId,desired,effect,reference,recovery,token);
        return(new{plan,previewHash=plan.PreviewHash(),installedModelApprovalNotProven=true},deviceId);
    },ct);
    private async Task<ZkProfilePlan> BuildProfilePlan(Guid deviceId,byte[] version,Guid snapshotId,ZkProfileValue desired,string effect,string reference,ZkProfileEndpoint recovery,CancellationToken ct)
    {
        if(deviceId==Guid.Empty||version is not{Length:8}||snapshotId==Guid.Empty||desired?.IsValid()!=true)throw new ArgumentException("Complete typed profile and source snapshot required.");
        var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deviceId,ct);var source=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==snapshotId,ct);var snapshot=ParseSettings(source?.ResultJson);
        var zone=device is null?null:await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(ct);var old=snapshot?.Profiles?.SingleOrDefault(x=>x.Parameter==desired.Parameter);
        if(device is null||!device.Enabled||device.Profile!="zk-standalone"||!device.Version.SequenceEqual(version)||source is null||source.Kind!="settings.read"||source.State!="succeeded"||source.DeviceId!=deviceId||source.TimeZoneId!=zone||!source.ExpectedDeviceVersion.SequenceEqual(version)||source.RequestedAtUtc>clock.GetUtcNow()||source.RequestedAtUtc<clock.GetUtcNow().AddMinutes(-5)||snapshot?.IsValid()!=true||snapshot.Product is null||snapshot.Firmware is null||old is not{Reported:true,Value:not null}||device.Host is null||device.Port is null)throw new DomainConflictException("Fresh reported exact-model profile snapshot required.");
        var p=new ZkProfilePlan(ZkProfilePlan.Version,snapshotId,snapshot.Serial,snapshot.Product,snapshot.Firmware,zone!,old.Value,desired,effect,reference,new(device.Host,device.Port.Value),recovery);
        if(!p.IsValid())throw new ArgumentException("Different complete approved states, side effects and bounded endpoints required.");return p;
    }
    private async Task ValidateProfileRequest(Guid deviceId,byte[] version,string kind,ZkProfilePlan plan,Guid? parent,bool recovery,CancellationToken ct)
    {
        if(kind=="settings.profile.set")
        {
            if(parent is not null||recovery||!SameProfile(plan,await BuildProfilePlan(deviceId,version,plan.SnapshotId,plan.Desired,plan.Effect,plan.ApprovalReference,plan.Recovery,ct)))throw new DomainConflictException("Profile source or preview changed.");
        }
        else
        {
            var p=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==parent,ct);
            if(p is null||p.Kind!="settings.profile.set"||p.DeviceId!=deviceId||p.State!="unknown"||p.ResolvedAtUtc is not null||p.FinishedAtUtc>=clock.GetUtcNow()||!p.ExpectedDeviceVersion.SequenceEqual(version)||!SameProfile(ParseProfile(p.ProfilePlanJson),plan))throw new DomainConflictException("Inspection must bind this unchanged unresolved profile write.");
        }
    }
    private async Task ProfileServiceRight(Guid service,DeviceOperationRecord item,CancellationToken ct)
    {
        if(ProfileKind(item.Kind)&&!await transaction.HasPermissionAsync(service,item.Kind=="settings.profile.set"?ZkProfileParameters.ExecutePermission(item.ProfileParameter!):ExecuteSettings,ct))throw new UnauthorizedAccessException();
    }
    public Task<Guid> ResolveProfileAsync(Guid actor,Guid id,byte[] version,Guid inspectionId,string confirmation,CancellationToken ct=default)=>ResolveProfileCore(actor,id,version,inspectionId,confirmation,ct);
    private async Task<Guid> ResolveProfileCore(Guid actor,Guid id,byte[] version,Guid inspectionId,string confirmation,CancellationToken ct)
    {
        var parameter=await db.DeviceOperations.AsNoTracking().Where(x=>x.Id==id).Select(x=>x.ProfileParameter).SingleOrDefaultAsync(ct);
        return await transaction.ExecuteAsync(actor,ZkProfileParameters.WritePermission(parameter!),"device-profile.reviewed",async token=>
        {
            if(confirmation!=ZkProfilePlan.ReviewConfirmation)throw new ArgumentException("Explicit profile review required.");
            await transaction.LockAsync("IO.DeviceConnections",token);var item=await Load(id,version,token);
            if(item.Kind!="settings.profile.set"||item.State!="unknown"||item.ResolvedAtUtc is not null||!await transaction.HasDevicePermissionAsync(actor,ReadSettings,item.DeviceId,token))throw new DomainConflictException("Unresolved profile write with read authority required.");
            var source=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==inspectionId,token);var p=ParseProfile(item.ProfilePlanJson);var observed=ParseTyped<ZkProfileObservation>(source?.ResultJson);var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==item.DeviceId,token);var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
            if(p?.IsValid()!=true||source is null||source.Kind!="settings.profile.inspect"||source.ProfileInspectionOfId!=id||source.State!="succeeded"||source.DeviceId!=item.DeviceId||source.RequestedAtUtc<=item.FinishedAtUtc||source.TimeZoneId!=item.TimeZoneId||zone!=item.TimeZoneId||!source.ExpectedDeviceVersion.SequenceEqual(item.ExpectedDeviceVersion)||!device.Version.SequenceEqual(item.ExpectedDeviceVersion)||!SameProfile(ParseProfile(source.ProfilePlanJson),p)||observed?.Matches(p,true,source.ProfileObserveRecovery)!=true)throw new DomainConflictException("Later exact-model old/new endpoint and complete state observation required.");
            item.ResolutionInfoId=source.Id;item.ResolvedByUserId=actor;item.ResolvedAtUtc=clock.GetUtcNow();return(id,id);
        },ct);
    }
}
