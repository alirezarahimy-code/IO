using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Devices;

/// <summary>Closed typed operations: information, confirmed clock/restart/IP and exact-model idle and five installed-approved raw parameters. No arbitrary SDK writes or enrollment.</summary>
public sealed partial class DeviceOperations(IoDbContext db,PermissionTransaction transaction,TimeProvider clock,IDataProtectionProvider protection)
{
    public const string ReadFunctionKeys="devices.settings.keys.read",ExecuteFunctionKeys="devices.settings.keys.execute";
    public const string SetRaw="devices.settings.raw.set",ExecuteRaw="devices.settings.raw.execute";
    public const string SetIdle="devices.settings.idle.set",ExecuteIdle="devices.settings.idle.execute",SetIp="devices.network.ip.set",InspectNetwork="devices.network.read",ExecuteNetworkWrite="devices.network.ip.execute",ExecuteNetworkRead="devices.network.read.execute",Restart="devices.restart",ExecuteRestart="devices.restart.execute",Read="devices.info.read",SetClock="devices.clock.set",Execute="devices.operations.execute",ReadSettings="devices.settings.read",ExecuteSettings="devices.settings.read.execute";
    public sealed record Claim(Guid Id,Guid Token,Guid DeviceId,string Profile,string? Host,int? Port,string Kind,string? ClockLocal,int TimeoutSeconds,ZkNetworkPlan? NetworkPlan=null,ZkIdlePlan? IdlePlan=null,ZkRawSettingPlan? RawSettingPlan=null,bool ReadFunctionKeys=false,ZkFunctionKeyPlan? FunctionKeyPlan=null);
    public sealed record View(Guid Id,Guid DeviceId,string Kind,string State,DateTimeOffset RequestedAtUtc,DateTimeOffset? FinishedAtUtc,
        DateTimeOffset? TargetUtc,string TimeZoneId,ZkDeviceInformation? Information,Guid? ResolutionInfoId,DateTimeOffset? ResolvedAtUtc,byte[] Version,ZkNetworkPlan? NetworkPlan=null,ZkNetworkObservation? Network=null,ZkIdlePlan? IdlePlan=null,ZkIdleObservation? Idle=null);
    private static string Permission(string kind)=>kind switch{"settings.key.set"=>SetFunctionKey,"settings.raw.set"=>SetRaw,"settings.idle.set"=>SetIdle,"network.ip.set"=>SetIp,"network.inspect"=>InspectNetwork,"device.restart"=>Restart,"information.read"=>Read,"settings.read"=>ReadSettings,"clock.set"=>SetClock,_=>throw new ArgumentException("Unknown operation.")};
    public Task<Guid> RequestAsync(Guid actor,Guid operationId,Guid deviceId,byte[] expectedDeviceVersion,string kind,int timeoutSeconds,DateTimeOffset? targetUtc,string? confirmation,CancellationToken ct=default,ZkNetworkPlan? networkPlan=null,ZkIdlePlan? idlePlan=null,string? idlePreviewHash=null,ZkRawSettingPlan? rawSettingPlan=null,string? rawPreviewHash=null,ZkFunctionKeyPlan? functionKeyPlan=null,string? functionKeyPreviewHash=null)=>
        transaction.ExecuteAsync(actor,Permission(kind),"device-operation.requested",token=>RequestInTransactionAsync(actor,operationId,deviceId,expectedDeviceVersion,kind,timeoutSeconds,targetUtc,confirmation,token,networkPlan,idlePlan,idlePreviewHash,rawSettingPlan,rawPreviewHash,functionKeyPlan,functionKeyPreviewHash),ct);
    internal async Task<(Guid Result,Guid Target)> RequestInTransactionAsync(Guid actor,Guid operationId,Guid deviceId,byte[] expectedDeviceVersion,string kind,int timeoutSeconds,DateTimeOffset? targetUtc,string? confirmation,CancellationToken token,ZkNetworkPlan? networkPlan=null,ZkIdlePlan? idlePlan=null,string? idlePreviewHash=null,ZkRawSettingPlan? rawSettingPlan=null,string? rawPreviewHash=null,ZkFunctionKeyPlan? functionKeyPlan=null,string? functionKeyPreviewHash=null)
    {
            if(!await transaction.HasPermissionAsync(actor,Permission(kind),token))throw new UnauthorizedAccessException();
            if(operationId==Guid.Empty||deviceId==Guid.Empty||expectedDeviceVersion is not{Length:8}||timeoutSeconds is <1 or >60||
                (kind=="information.read"&&(targetUtc is not null||confirmation is not null))||
                (kind=="device.restart"&&(targetUtc is not null||confirmation!="RESTART-DEVICE"))||
                (kind=="settings.read"&&(targetUtc is not null||confirmation!="READ-ADVANCED-SETTINGS"))||
                (kind=="clock.set"&&(targetUtc is null||targetUtc.Value.Offset!=TimeSpan.Zero||targetUtc.Value.Ticks%TimeSpan.TicksPerSecond!=0||confirmation!="SET-CLOCK")))
                throw new ArgumentException("Explicit operation, version, UTC seconds and confirmation required.");
            if(kind is "network.ip.set" or "network.inspect")
            {
                if(networkPlan?.IsValidFor(kind)!=true||targetUtc is not null||confirmation!=(kind=="network.ip.set"?"SET-DEVICE-IP":"INSPECT-DEVICE-IP"))throw new ArgumentException("Explicit bounded network plan and confirmation required.");
            }
            else if(networkPlan is not null)throw new ArgumentException("Network payload cannot retarget another operation.");
            if(kind=="settings.idle.set")
            {
                if(!await transaction.HasPermissionAsync(actor,ReadSettings,token))throw new UnauthorizedAccessException();
                if(idlePlan?.IsValid()!=true||idlePreviewHash!=idlePlan.PreviewHash()||targetUtc is not null||confirmation!=ZkIdlePlan.Confirmation)throw new ArgumentException("Exact idle preview and effect confirmation required.");
            }
            else if(idlePlan is not null||idlePreviewHash is not null)throw new ArgumentException("Idle data cannot enter another typed operation.");
            if(kind=="settings.raw.set")
            {
                if(!await transaction.HasPermissionAsync(actor,ReadSettings,token))throw new UnauthorizedAccessException();
                if(rawSettingPlan?.IsValid()!=true||rawPreviewHash!=rawSettingPlan.PreviewHash()||targetUtc is not null||confirmation!=ZkRawSettingPlan.Confirmation)throw new ArgumentException("Exact approved raw setting preview required.");
            }
            else if(rawSettingPlan is not null||rawPreviewHash is not null)throw new ArgumentException("Raw setting plan cannot enter another operation.");
            if(kind=="settings.key.set")
            {
                if(!await CanReadKey(actor,deviceId,token))throw new UnauthorizedAccessException();
                if(functionKeyPlan?.IsValid()!=true||functionKeyPreviewHash!=functionKeyPlan.PreviewHash()||targetUtc is not null||confirmation!=ZkFunctionKeyPlan.Confirmation)throw new ArgumentException("Exact complete function key preview required.");
            }
            else if(functionKeyPlan is not null||functionKeyPreviewHash is not null)throw new ArgumentException("Key plan cannot retarget another operation.");
            await transaction.LockAsync("IO.DeviceConnections",token);
            var old=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==operationId,token);
            if(old is not null)
            {
                if(old.DeviceId!=deviceId||old.RequestedByUserId!=actor||old.Kind!=kind||old.TimeoutSeconds!=timeoutSeconds||old.TargetUtc!=targetUtc||ParseKey(old.FunctionKeyPlanJson)!=functionKeyPlan||ParseRaw(old.RawSettingPlanJson)!=rawSettingPlan||ParsePlan(old.NetworkPlanJson)!=networkPlan||ParseIdle(old.IdlePlanJson)!=idlePlan||!old.ExpectedDeviceVersion.SequenceEqual(expectedDeviceVersion))throw new DomainConflictException("Changed operation retry.");
                return(old.Id,old.Id);
            }
        if(await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has unfinished attendance extraction.");
        if(await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has unfinished user uploads.");
            var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deviceId,token)??throw new ArgumentException("Unknown device.");
            if(!device.Enabled||!device.Version.SequenceEqual(expectedDeviceVersion))throw new DomainConflictException("Changed or disabled device.");
            if(device.Profile!="zk-standalone")throw new ArgumentException("No verified operation adapter for this profile.");
            if(await db.DeviceOperations.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token)||await db.DeviceConnections.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device already has queued or running work.");
            if((kind=="clock.set"||kind=="device.restart"||kind=="network.ip.set"||kind=="settings.idle.set"||kind=="settings.raw.set"||kind=="settings.key.set")&&await db.DeviceOperations.AnyAsync(x=>x.DeviceId==deviceId&&(x.Kind=="clock.set"||x.Kind=="device.restart"||x.Kind=="network.ip.set"||x.Kind=="settings.idle.set"||x.Kind=="settings.raw.set"||x.Kind=="settings.key.set")&&x.State=="unknown"&&x.ResolvedAtUtc==null,token))throw new DomainConflictException("Read and explicitly reconcile the previous unknown device write first.");
            if(DeviceWriteSafety.IsWrite(kind)&&(await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==deviceId&&x.Enabled,token)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==deviceId&&x.State=="unknown"&&x.ResolutionSnapshotId==null,token)))throw new DomainConflictException("Pause polling and review unknown user writes before another native write.");
            if(await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has an unfinished user download.");
            if(networkPlan is not null)
            {
                if(device.Host!=networkPlan.OldAddress)throw new DomainConflictException("Old IP must exactly match the unchanged registered endpoint.");
                if(kind=="network.ip.set")
                {
                    if(await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==deviceId&&x.Enabled,token)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==deviceId&&x.State=="unknown"&&x.ResolutionSnapshotId==null,token))throw new DomainConflictException("Pause schedules and review unknown user writes before changing IP.");
                }
                else
                {
                    var parent=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==networkPlan.ChangeId,token);
                    var approved=ParsePlan(parent?.NetworkPlanJson);
                    if(parent is null||parent.Kind!="network.ip.set"||parent.DeviceId!=deviceId||parent.State!="unknown"||parent.ResolvedAtUtc is not null||parent.FinishedAtUtc>=clock.GetUtcNow()||!parent.ExpectedDeviceVersion.SequenceEqual(expectedDeviceVersion)||approved is null||approved.OldAddress!=networkPlan.OldAddress||approved.NewAddress!=networkPlan.NewAddress||approved.Serial!=networkPlan.Serial)throw new DomainConflictException("Fresh inspection must refer to this unresolved unchanged IP transition.");
                }
            }
            if(idlePlan is not null)
            {
                var approved=await BuildIdlePlan(deviceId,expectedDeviceVersion,idlePlan.SnapshotId,idlePlan.NewMinutes,idlePlan.ApprovalReference,token);
                if(approved!=idlePlan)throw new DomainConflictException("Idle preview snapshot or device descriptor changed.");
                if(await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==deviceId&&x.Enabled,token)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==deviceId&&x.State=="unknown"&&x.ResolutionSnapshotId==null,token))throw new DomainConflictException("Pause schedules and review unknown user writes before idle changes.");
            }
            if(rawSettingPlan is not null)
            {
                var approved=await BuildRawPlan(deviceId,expectedDeviceVersion,rawSettingPlan.SnapshotId,rawSettingPlan.Parameter,rawSettingPlan.NewValue,rawSettingPlan.Unit,rawSettingPlan.Effect,rawSettingPlan.ApprovalReference,token);
                if(approved!=rawSettingPlan)throw new DomainConflictException("Raw setting snapshot or approved descriptor changed.");
            }
            if(functionKeyPlan is not null&&await BuildKeyPlan(deviceId,expectedDeviceVersion,functionKeyPlan.SnapshotId,functionKeyPlan.Desired,functionKeyPlan.Effect,functionKeyPlan.ApprovalReference,token)!=functionKeyPlan)throw new DomainConflictException("Function key snapshot changed.");
            var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
            if(targetUtc is not null)_=LocalClock(targetUtc.Value,zone);
            db.DeviceOperations.Add(new(){Id=operationId,DeviceId=deviceId,Kind=kind,RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=expectedDeviceVersion.ToArray(),TimeoutSeconds=timeoutSeconds,TimeZoneId=zone,TargetUtc=targetUtc,NetworkPlanJson=networkPlan is null?null:JsonSerializer.Serialize(networkPlan),IdlePlanJson=idlePlan is null?null:JsonSerializer.Serialize(idlePlan),RawSettingPlanJson=rawSettingPlan is null?null:JsonSerializer.Serialize(rawSettingPlan),FunctionKeyPlanJson=functionKeyPlan is null?null:JsonSerializer.Serialize(functionKeyPlan)});
            return(operationId,operationId);
    }

    public Task<object> ListAsync(Guid actor,Guid? deviceId,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-operation.viewed",async token=>
    {
        if(deviceId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid page.");
        var rows=await db.DeviceOperations.AsNoTracking().Where(x=>x.Kind!="settings.read"&&x.Kind!="settings.idle.set"&&x.Kind!="settings.raw.set"&&x.Kind!="settings.key.set"&&(deviceId==null||x.DeviceId==deviceId)).OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50).ToListAsync(token);
        var items=rows.Select(x=>new View(x.Id,x.DeviceId,x.Kind,x.State,x.RequestedAtUtc,x.FinishedAtUtc,x.TargetUtc,x.TimeZoneId,ParseInformation(x.ResultJson),x.ResolutionInfoId,x.ResolvedAtUtc,x.Version,ParsePlan(x.NetworkPlanJson),ParseNetwork(x.ResultJson))).ToArray();
        return(new{offset,pageSize=50,items},actor);
    },ct);

    public Task<object> ListSettingsAsync(Guid actor,Guid? deviceId,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,ReadSettings,"device-settings.viewed",async token=>
    {
        if(deviceId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid page.");
        var rows=await db.DeviceOperations.AsNoTracking().Where(x=>(x.Kind=="settings.read"||x.Kind=="settings.idle.set"||x.Kind=="settings.raw.set"||x.Kind=="settings.key.set")&&(deviceId==null||x.DeviceId==deviceId)).OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50).ToListAsync(token);
        var discloseKeys=await transaction.HasPermissionAsync(actor,ReadFunctionKeys,token);
        ZkAdvancedSettings? VisibleSettings(string? json){var value=ParseSettings(json);return discloseKeys?value:value is null?null:value with{FunctionKeys=null};}
        var items=rows.Select(x=>new{x.Id,x.DeviceId,x.Kind,x.State,x.RequestedAtUtc,x.FinishedAtUtc,x.ExpectedDeviceVersion,x.Version,settings=x.Kind=="settings.read"?VisibleSettings(x.ResultJson):null,functionKeyPlan=discloseKeys?ParseKey(x.FunctionKeyPlanJson):null,functionKey=discloseKeys?ParseTyped<ZkFunctionKeyObservation>(x.ResultJson):null,rawSettingPlan=ParseRaw(x.RawSettingPlanJson),rawSetting=ParseTyped<ZkRawSettingObservation>(x.ResultJson),idlePlan=ParseIdle(x.IdlePlanJson),idle=ParseTyped<ZkIdleObservation>(x.ResultJson),x.ResolutionInfoId,x.ResolvedAtUtc}).ToArray();
        return(new{offset,pageSize=50,items},actor);
    },ct);

    public Task<Guid> CancelAsync(Guid actor,Guid id,byte[] version,CancellationToken ct=default)=>ChangePending(actor,id,version,ct);
    private async Task<Guid> ChangePending(Guid actor,Guid id,byte[] version,CancellationToken ct)
    {
        // Lookup chooses only a server-defined permission; authorization is repeated inside the transaction.
        var kind=await db.DeviceOperations.AsNoTracking().Where(x=>x.Id==id).Select(x=>x.Kind).SingleOrDefaultAsync(ct)??"information.read";
        return await transaction.ExecuteAsync(actor,Permission(kind),"device-operation.cancelled",async token=>
        {
            await transaction.LockAsync("IO.DeviceConnections",token);var item=await Load(id,version,token);
            if(item.State!="pending")throw new DomainConflictException("Cancellation is only guaranteed before dispatch.");
            item.State="cancelled";item.FinishedAtUtc=clock.GetUtcNow();return(id,id);
        },ct);
    }

    public Task<Claim?> ClaimAsync(Guid service,CancellationToken ct=default,bool includeSettings=false)=>transaction.ExecuteAsync<Claim?>(service,Execute,"device-operation.claimed",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var now=clock.GetUtcNow();
        var idle=await transaction.HasPermissionAsync(service,ExecuteIdle,token);
        var networkWrite=await transaction.HasPermissionAsync(service,ExecuteNetworkWrite,token);
        var networkRead=await transaction.HasPermissionAsync(service,ExecuteNetworkRead,token);
        var restart=await transaction.HasPermissionAsync(service,ExecuteRestart,token);
        var keyWrite=await transaction.HasPermissionAsync(service,ExecuteFunctionKeyWrite,token)&&await transaction.HasPermissionAsync(service,ExecuteFunctionKeys,token);
        var raw=await transaction.HasPermissionAsync(service,ExecuteRaw,token);
        var settings=includeSettings&&await transaction.HasPermissionAsync(service,ExecuteSettings,token);
        var expired=await db.DeviceOperations.AsTracking().Where(x=>(x.Kind!="settings.key.set"||keyWrite)&&(x.Kind!="settings.raw.set"||raw)&&(x.Kind!="settings.idle.set"||idle)&&(x.Kind!="network.ip.set"||networkWrite)&&(x.Kind!="network.inspect"||networkRead)&&(x.Kind!="device.restart"||restart)&&(x.Kind!="settings.read"||settings)&&x.State=="running"&&x.ClaimedAtUtc<now.AddMinutes(-2)).ToListAsync(token);
        foreach(var item in expired){item.State=(item.Kind=="clock.set"||item.Kind=="device.restart"||item.Kind=="network.ip.set"||item.Kind=="settings.idle.set"||item.Kind=="settings.raw.set"||item.Kind=="settings.key.set")?"unknown":"interrupted";item.FinishedAtUtc=now;}
        var next=await db.DeviceOperations.AsTracking().Where(x=>(x.Kind!="settings.key.set"||keyWrite)&&(x.Kind!="settings.raw.set"||raw)&&(x.Kind!="settings.idle.set"||idle)&&(x.Kind!="network.ip.set"||networkWrite)&&(x.Kind!="network.inspect"||networkRead)&&(x.Kind!="device.restart"||restart)&&(x.Kind!="settings.read"||settings)&&x.State=="pending").OrderBy(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);
        if(next is null)return(null,service);
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==next.DeviceId,token);
        if(!await transaction.HasDevicePermissionAsync(next.RequestedByUserId,Permission(next.Kind),next.DeviceId,token)||((next.Kind=="settings.idle.set"||next.Kind=="settings.raw.set"||next.Kind=="settings.key.set")&&!await transaction.HasDevicePermissionAsync(next.RequestedByUserId,ReadSettings,next.DeviceId,token))){next.State="revoked";next.FinishedAtUtc=now;return(null,next.Id);}
        if(next.Kind=="settings.key.set"&&!await CanReadKey(next.RequestedByUserId,next.DeviceId,token)){next.State="revoked";next.FinishedAtUtc=now;return(null,next.Id);}
        var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
        if(!device.Enabled||!device.Version.SequenceEqual(next.ExpectedDeviceVersion)||zone!=next.TimeZoneId){next.State="device-changed";next.FinishedAtUtc=now;return(null,next.Id);}
        if((next.Kind=="clock.set"||next.Kind=="device.restart"||next.Kind=="network.ip.set"||next.Kind=="settings.idle.set"||next.Kind=="settings.raw.set"||next.Kind=="settings.key.set")&&next.RequestedAtUtc<now.AddMinutes(-2)){next.State="expired";next.FinishedAtUtc=now;return(null,next.Id);}
        next.ServiceUserId=service;next.ClaimId=Guid.NewGuid();next.ClaimedAtUtc=now;next.State="running";
        return(new Claim(next.Id,next.ClaimId.Value,next.DeviceId,device.Profile,device.Host,device.Port,next.Kind,next.TargetUtc is null?null:LocalClock(next.TargetUtc.Value,next.TimeZoneId),next.TimeoutSeconds,ParsePlan(next.NetworkPlanJson),ParseIdle(next.IdlePlanJson),ParseRaw(next.RawSettingPlanJson),await KeysAuthorized(service,next,token),ParseKey(next.FunctionKeyPlanJson)),next.Id);
    },ct);

    public Task<Guid> CompleteAsync(Guid service,Guid id,Guid claimId,ZkOperationOutput result,CancellationToken ct=default)=>transaction.ExecuteAsync(service,Execute,"device-operation.finished",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Owned(service,id,claimId,token);
        if(item.Kind=="settings.idle.set"&&!await transaction.HasPermissionAsync(service,ExecuteIdle,token))throw new UnauthorizedAccessException();
        if(item.Kind=="network.ip.set"&&!await transaction.HasPermissionAsync(service,ExecuteNetworkWrite,token))throw new UnauthorizedAccessException();
        if(item.Kind=="network.inspect"&&!await transaction.HasPermissionAsync(service,ExecuteNetworkRead,token))throw new UnauthorizedAccessException();
        if(item.Kind=="device.restart"&&!await transaction.HasPermissionAsync(service,ExecuteRestart,token))throw new UnauthorizedAccessException();
        if(item.Kind=="settings.read"&&!await transaction.HasPermissionAsync(service,ExecuteSettings,token))throw new UnauthorizedAccessException();
        if(item.Kind=="settings.raw.set"&&!await transaction.HasPermissionAsync(service,ExecuteRaw,token))throw new UnauthorizedAccessException();
        if(item.Kind=="settings.key.set"&&(!await transaction.HasPermissionAsync(service,ExecuteFunctionKeyWrite,token)||!await transaction.HasPermissionAsync(service,ExecuteFunctionKeys,token)))throw new UnauthorizedAccessException();
        if(result is null)throw new ArgumentException("Result required.");
        if(!new[]{"succeeded","unsupported","identity-mismatch","failed","unknown","timed-out","interrupted","endpoint-denied","credential-unavailable","bridge-failed"}.Contains(result.State,StringComparer.Ordinal)||
            (result.Information is not null&&(item.Kind!="information.read"||result.State!="succeeded"||!result.Information.IsValid()))||
            (item.Kind=="information.read"&&result.State=="succeeded"&&result.Information is null)||
            (result.Settings is not null&&(item.Kind!="settings.read"||result.State!="succeeded"||!result.Settings.IsValid()))||
            (item.Kind=="settings.read"&&result.State=="succeeded"&&result.Settings is null))throw new ArgumentException("Invalid typed operation result.");
        if(result.Settings?.FunctionKeys is not null&&!await KeysAuthorized(service,item,token))throw new UnauthorizedAccessException();
        if(result.Network is not null&&(item.Kind!="network.inspect"||result.State!="succeeded"||!result.Network.IsValid()))throw new ArgumentException("Unexpected network evidence.");
        var plan=ParsePlan(item.NetworkPlanJson);
        if(item.Kind=="network.inspect"&&result.State=="succeeded"&&(result.Network is null||plan is null||result.Network.Serial!=plan.Serial||result.Network.ConnectedAddress!=plan.TargetAddress(item.Kind)))throw new ArgumentException("Inspection must bind exact serial and approved endpoint.");
        if(item.Kind=="network.ip.set"&&result.State=="succeeded")throw new ArgumentException("An IP write ACK cannot prove network recovery.");
        var idlePlan=ParseIdle(item.IdlePlanJson);
        if(result.Idle is not null&&(item.Kind!="settings.idle.set"||result.State!="succeeded"||!result.Idle.IsValid()||idlePlan is null||result.Idle.Serial!=idlePlan.Serial||result.Idle.Product!=idlePlan.Product||result.Idle.Firmware!=idlePlan.Firmware||result.Idle.Minutes!=idlePlan.NewMinutes))throw new ArgumentException("Invalid exact idle read-back.");
        if(item.Kind=="settings.idle.set"&&result.State=="succeeded"&&result.Idle is null)throw new ArgumentException("Idle success requires typed read-back.");
        var rawPlan=ParseRaw(item.RawSettingPlanJson);
        if(result.RawSetting is not null&&(item.Kind!="settings.raw.set"||result.State!="succeeded"||rawPlan is null||!result.RawSetting.Matches(rawPlan)))throw new ArgumentException("Unexpected raw setting observation.");
        if(item.Kind=="settings.raw.set"&&result.State=="succeeded"&&result.RawSetting is null)throw new ArgumentException("Exact raw setting read-back required.");
        var keyPlan=ParseKey(item.FunctionKeyPlanJson);
        if(result.FunctionKey is not null&&(item.Kind!="settings.key.set"||result.State!="succeeded"||keyPlan is null||!result.FunctionKey.Matches(keyPlan)))throw new ArgumentException("Exact complete function key observation required.");
        if(item.Kind=="settings.key.set"&&result.State=="succeeded"&&result.FunctionKey is null)throw new ArgumentException("Bare key write ACK is not success.");
        if(result.FunctionKey is not null&&!await CanReadKey(item.RequestedByUserId,item.DeviceId,token))throw new UnauthorizedAccessException();
        var payload=result.FunctionKey is not null?JsonSerializer.Serialize(result.FunctionKey):result.RawSetting is not null?JsonSerializer.Serialize(result.RawSetting):result.Idle is not null?JsonSerializer.Serialize(result.Idle):result.Network is not null?JsonSerializer.Serialize(result.Network):result.Settings is not null?JsonSerializer.Serialize(result.Settings):result.Information is null?null:JsonSerializer.Serialize(result.Information);
        if(payload is not null&&payload.Length>(item.Kind=="settings.read"?32768:(item.Kind=="settings.raw.set"||item.Kind=="settings.key.set")?8192:4000))throw new ArgumentException("Typed observation exceeds its retained SQL result budget.");
        item.State=(item.Kind=="clock.set"||item.Kind=="device.restart"||item.Kind=="network.ip.set"||item.Kind=="settings.idle.set"||item.Kind=="settings.raw.set"||item.Kind=="settings.key.set")&&result.State is "timed-out" or "interrupted" or "bridge-failed" or "unknown"?"unknown":result.State;
        item.FinishedAtUtc=clock.GetUtcNow();item.ResultJson=payload;
        return(id,id);
    },ct);

    public Task<DeviceCommunicationKey?> CredentialAsync(Guid service,Claim claim,bool allowDefault,CancellationToken ct=default)=>transaction.ExecuteAsync<DeviceCommunicationKey?>(service,Execute,"device-operation.credential-used",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Owned(service,claim.Id,claim.Token,token);
        if(item.Kind=="settings.idle.set"&&!await transaction.HasPermissionAsync(service,ExecuteIdle,token))throw new UnauthorizedAccessException();
        if(item.Kind=="network.ip.set"&&!await transaction.HasPermissionAsync(service,ExecuteNetworkWrite,token))throw new UnauthorizedAccessException();
        if(item.Kind=="network.inspect"&&!await transaction.HasPermissionAsync(service,ExecuteNetworkRead,token))throw new UnauthorizedAccessException();
        if(item.Kind=="device.restart"&&!await transaction.HasPermissionAsync(service,ExecuteRestart,token))throw new UnauthorizedAccessException();
        if(item.Kind=="settings.read"&&!await transaction.HasPermissionAsync(service,ExecuteSettings,token))throw new UnauthorizedAccessException();
        if(item.Kind=="settings.raw.set"&&!await transaction.HasPermissionAsync(service,ExecuteRaw,token))throw new UnauthorizedAccessException();
        if(item.Kind=="settings.key.set"&&(!await transaction.HasPermissionAsync(service,ExecuteFunctionKeyWrite,token)||!await transaction.HasPermissionAsync(service,ExecuteFunctionKeys,token)))throw new UnauthorizedAccessException();
        if(item.Kind=="settings.key.set"&&!await CanReadKey(item.RequestedByUserId,item.DeviceId,token))return(null,item.Id);
        if(ParseKey(item.FunctionKeyPlanJson)!=claim.FunctionKeyPlan)return(null,item.Id);
        if(claim.ReadFunctionKeys!=await KeysAuthorized(service,item,token))return(null,item.Id);
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==item.DeviceId,token);
        var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
        if(((item.Kind=="clock.set"||item.Kind=="device.restart"||item.Kind=="network.ip.set"||item.Kind=="settings.idle.set"||item.Kind=="settings.raw.set"||item.Kind=="settings.key.set")&&item.RequestedAtUtc<clock.GetUtcNow().AddMinutes(-2))||item.DeviceId!=claim.DeviceId||claim.Profile!=device.Profile||claim.Host!=device.Host||claim.Port!=device.Port||claim.TimeoutSeconds!=item.TimeoutSeconds||claim.ClockLocal!=(item.TargetUtc is null?null:LocalClock(item.TargetUtc.Value,item.TimeZoneId))||item.Kind!=claim.Kind||ParsePlan(item.NetworkPlanJson)!=claim.NetworkPlan||ParseIdle(item.IdlePlanJson)!=claim.IdlePlan||ParseRaw(item.RawSettingPlanJson)!=claim.RawSettingPlan||!device.Enabled||!device.Version.SequenceEqual(item.ExpectedDeviceVersion)||zone!=item.TimeZoneId||!await transaction.HasDevicePermissionAsync(item.RequestedByUserId,Permission(item.Kind),item.DeviceId,token))return(null,item.Id);
        if((item.Kind=="settings.idle.set"||item.Kind=="settings.raw.set"||item.Kind=="settings.key.set")&&!await transaction.HasDevicePermissionAsync(item.RequestedByUserId,ReadSettings,item.DeviceId,token))return(null,item.Id);
        if(DeviceWriteSafety.IsWrite(item.Kind)&&(await db.DevicePollSchedules.AnyAsync(x=>x.DeviceId==item.DeviceId&&x.Enabled,token)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==item.DeviceId&&x.State=="unknown"&&x.ResolutionSnapshotId==null,token)))return(null,item.Id);
        var ciphertext=await db.DeviceSecrets.Where(x=>x.DeviceId==item.DeviceId).Select(x=>x.ProtectedValue).SingleOrDefaultAsync(token);
        if(ciphertext is null)return(allowDefault?new DeviceCommunicationKey(0):null,item.Id);
        try{var text=protection.CreateProtector("IO.Devices.Credential.v1",item.DeviceId.ToString("N")).Unprotect(ciphertext);return(text.Length>0&&text.All(c=>c is >='0' and <='9')&&int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var key)?new DeviceCommunicationKey(key):null,item.Id);}catch(CryptographicException){return(null,item.Id);}
    },ct);

    public Task<Guid> ResolveClockAsync(Guid actor,Guid id,byte[] version,Guid informationId,string confirmation,CancellationToken ct=default)=>ResolveWriteAsync(actor,id,version,informationId,confirmation,"clock.set",ct);
    public Task<Guid> ResolveRestartAsync(Guid actor,Guid id,byte[] version,Guid informationId,string confirmation,CancellationToken ct=default)=>ResolveWriteAsync(actor,id,version,informationId,confirmation,"device.restart",ct);
    private Task<Guid> ResolveWriteAsync(Guid actor,Guid id,byte[] version,Guid informationId,string confirmation,string kind,CancellationToken ct)=>transaction.ExecuteAsync(actor,Permission(kind),kind=="clock.set"?"device-operation.clock-reviewed":"device-operation.restart-reviewed",async token=>
    {
        if(confirmation!=(kind=="clock.set"?"REVIEWED-CLOCK":"REVIEWED-RESTART-REACHABILITY"))throw new ArgumentException("Explicit clock review confirmation required.");
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Load(id,version,token);
        if(item.Kind!=kind||item.State!="unknown"||item.ResolvedAtUtc is not null)throw new DomainConflictException("Only an unresolved unknown write of the exact kind can be reviewed.");
        var info=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==informationId,token);
        var currentZone=await (from d in db.Devices join site in db.Sites on d.SiteId equals site.Id where d.Id==item.DeviceId select site.TimeZoneId).SingleAsync(token);
        var deviceVersion=await db.Devices.Where(x=>x.Id==item.DeviceId).Select(x=>x.Version).SingleAsync(token);
        if(info is null||info.DeviceId!=item.DeviceId||info.Kind!="information.read"||info.State!="succeeded"||info.FinishedAtUtc<item.FinishedAtUtc||info.RequestedAtUtc<=item.FinishedAtUtc||info.TimeZoneId!=item.TimeZoneId||info.TimeZoneId!=currentZone||!info.ExpectedDeviceVersion.SequenceEqual(item.ExpectedDeviceVersion)||!deviceVersion.SequenceEqual(info.ExpectedDeviceVersion)||ParseInformation(info.ResultJson)?.ClockLocal is null)
            throw new DomainConflictException("A later successful clock observation of the same unchanged device is required.");
        item.ResolutionInfoId=info.Id;item.ResolvedByUserId=actor;item.ResolvedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);

    public Task<Guid> ResolveNetworkAsync(Guid actor,Guid id,byte[] version,Guid informationId,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,SetIp,"device-operation.network-reviewed",async token=>
    {
        if(confirmation!="REVIEWED-DEVICE-IP")throw new ArgumentException("Explicit IP observation review required.");
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Load(id,version,token);
        if(item.Kind!="network.ip.set"||item.State!="unknown"||item.ResolvedAtUtc is not null)throw new DomainConflictException("Unresolved unknown IP change required.");
        var plan=ParsePlan(item.NetworkPlanJson)!;
        var info=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==informationId,token);
        var observation=ParseNetwork(info?.ResultJson);var inspected=ParsePlan(info?.NetworkPlanJson);
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==item.DeviceId,token);
        var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
        if(info is null||info.DeviceId!=item.DeviceId||info.Kind!="network.inspect"||info.State!="succeeded"||info.RequestedAtUtc<=item.FinishedAtUtc||info.TimeZoneId!=item.TimeZoneId||zone!=item.TimeZoneId||!info.ExpectedDeviceVersion.SequenceEqual(item.ExpectedDeviceVersion)||!device.Version.SequenceEqual(item.ExpectedDeviceVersion)||inspected is null||inspected.ChangeId!=item.Id||inspected.OldAddress!=plan.OldAddress||inspected.NewAddress!=plan.NewAddress||inspected.Serial!=plan.Serial||observation is null||!observation.IsValid()||observation.Serial!=plan.Serial||observation.ConnectedAddress!=inspected.TargetAddress(info.Kind)||observation.DeviceIp!=observation.ConnectedAddress)throw new DomainConflictException("Fresh serial-pinned observation at an approved old or new IP is required.");
        item.ResolutionInfoId=info.Id;item.ResolvedByUserId=actor;item.ResolvedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
    public Task<object> PreviewRawAsync(Guid actor,Guid deviceId,byte[] version,Guid snapshotId,string parameter,int value,string unit,string effect,string approvalReference,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,SetRaw,"device-setting.raw-previewed",async token=>
    {
        if(!await transaction.HasDevicePermissionAsync(actor,ReadSettings,deviceId,token))throw new UnauthorizedAccessException();
        await transaction.LockAsync("IO.DeviceConnections",token);var plan=await BuildRawPlan(deviceId,version,snapshotId,parameter,value,unit,effect,approvalReference,token);
        return(new{plan,previewHash=plan.PreviewHash(),installedModelApprovalNotProven=true},deviceId);
    },ct);
    private async Task<ZkRawSettingPlan> BuildRawPlan(Guid deviceId,byte[] version,Guid snapshotId,string parameter,int value,string unit,string effect,string approvalReference,CancellationToken ct)
    {
        if(deviceId==Guid.Empty||version is not{Length:8}||snapshotId==Guid.Empty||!ZkRawParameters.IsKnown(parameter))throw new ArgumentException("Explicit closed parameter and snapshot required.");
        var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deviceId,ct);var source=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==snapshotId,ct);var snapshot=ParseSettings(source?.ResultJson);
        var zone=device is null?null:await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(ct);
        if(device is null||!device.Enabled||device.Profile!="zk-standalone"||!device.Version.SequenceEqual(version)||source is null||source.DeviceId!=deviceId||source.Kind!="settings.read"||source.State!="succeeded"||source.RequestedAtUtc<clock.GetUtcNow().AddMinutes(-5)||source.RequestedAtUtc>clock.GetUtcNow()||source.TimeZoneId!=zone||!source.ExpectedDeviceVersion.SequenceEqual(version)||snapshot?.IsValid()!=true||snapshot.Product is null||snapshot.Firmware is null||ZkRawParameters.Value(snapshot,parameter) is not{} old)throw new DomainConflictException("Fresh successful same-device model-bound raw snapshot required.");
        var plan=new ZkRawSettingPlan(ZkRawSettingPlan.Version,snapshotId,snapshot.Serial,snapshot.Product,snapshot.Firmware,parameter,old,value,unit,effect,approvalReference);if(!plan.IsValid())throw new ArgumentException("Exact different raw values and explicit model-contract unit effect and reference required.");return plan;
    }
    public Task<Guid> ResolveRawAsync(Guid actor,Guid id,byte[] version,Guid snapshotId,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,SetRaw,"device-operation.raw-reviewed",async token=>
    {
        if(confirmation!="REVIEWED-APPROVED-RAW-SETTING")throw new ArgumentException("Explicit raw observation review required.");
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Load(id,version,token);if(item.Kind!="settings.raw.set"||item.State!="unknown"||item.ResolvedAtUtc is not null)throw new DomainConflictException("Unresolved raw setting write required.");
        if(!await transaction.HasDevicePermissionAsync(actor,ReadSettings,item.DeviceId,token))throw new UnauthorizedAccessException();
        var plan=ParseRaw(item.RawSettingPlanJson);var source=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==snapshotId,token);var observed=ParseSettings(source?.ResultJson);var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==item.DeviceId,token);var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
        if(plan?.IsValid()!=true||source is null||source.Kind!="settings.read"||source.State!="succeeded"||source.DeviceId!=item.DeviceId||source.RequestedAtUtc<=item.FinishedAtUtc||source.TimeZoneId!=item.TimeZoneId||zone!=item.TimeZoneId||!source.ExpectedDeviceVersion.SequenceEqual(item.ExpectedDeviceVersion)||!device.Version.SequenceEqual(item.ExpectedDeviceVersion)||observed?.IsValid()!=true||observed.Serial!=plan.Serial||observed.Product!=plan.Product||observed.Firmware!=plan.Firmware||ZkRawParameters.Value(observed,plan.Parameter) is not{} value||(value!=plan.ExpectedValue&&value!=plan.NewValue))throw new DomainConflictException("Fresh same-model old/new raw observation required; never retry an unknown write.");
        item.ResolutionInfoId=source.Id;item.ResolvedByUserId=actor;item.ResolvedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);

    public Task<object> PreviewIdleAsync(Guid actor,Guid deviceId,byte[] version,Guid snapshotId,int minutes,string approvalReference,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,SetIdle,"device-setting.idle-previewed",async token=>
    {
        if(!await transaction.HasPermissionAsync(actor,ReadSettings,token))throw new UnauthorizedAccessException();
        await transaction.LockAsync("IO.DeviceConnections",token);
        var plan=await BuildIdlePlan(deviceId,version,snapshotId,minutes,approvalReference,token);
        return(new{plan,previewHash=plan.PreviewHash(),effect="standby-or-power-off",installedModelApprovalNotProven=true},deviceId);
    },ct);
    private async Task<ZkIdlePlan> BuildIdlePlan(Guid deviceId,byte[] version,Guid snapshotId,int minutes,string approvalReference,CancellationToken ct)
    {
        if(deviceId==Guid.Empty||version is not{Length:8}||snapshotId==Guid.Empty||minutes is <1 or >999||!ZkIdlePlan.Text(approvalReference,200))throw new ArgumentException("Bounded explicit idle preview required.");
        var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deviceId,ct);
        var source=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==snapshotId,ct);var snapshot=ParseSettings(source?.ResultJson);
        var zone=device is null?null:await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(ct);
        if(device is null||!device.Enabled||device.Profile!="zk-standalone"||!device.Version.SequenceEqual(version)||source is null||source.DeviceId!=deviceId||source.Kind!="settings.read"||source.State!="succeeded"||source.RequestedAtUtc<clock.GetUtcNow().AddMinutes(-5)||source.RequestedAtUtc>clock.GetUtcNow()||source.TimeZoneId!=zone||!source.ExpectedDeviceVersion.SequenceEqual(version)||snapshot?.IsValid()!=true||snapshot.SleepTime is null||snapshot.Product is null||snapshot.Firmware is null)throw new DomainConflictException("Fresh successful same-device settings snapshot including exact product firmware and idle value required.");
        var plan=new ZkIdlePlan(ZkIdlePlan.Version,snapshotId,snapshot.Serial,snapshot.Product,snapshot.Firmware,snapshot.SleepTime.Value,minutes,approvalReference);
        if(!plan.IsValid())throw new DomainConflictException("Zero disabled unknown out-of-envelope and unchanged idle values are not writable.");return plan;
    }
    public Task<Guid> ResolveIdleAsync(Guid actor,Guid id,byte[] version,Guid snapshotId,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,SetIdle,"device-operation.idle-reviewed",async token=>
    {
        if(!await transaction.HasPermissionAsync(actor,ReadSettings,token))throw new UnauthorizedAccessException();
        if(confirmation!="REVIEWED-IDLE-MINUTES")throw new ArgumentException("Explicit idle observation review required.");
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Load(id,version,token);
        if(item.Kind!="settings.idle.set"||item.State!="unknown"||item.ResolvedAtUtc is not null)throw new DomainConflictException("Unresolved unknown idle write required.");
        var plan=ParseIdle(item.IdlePlanJson);if(plan?.IsValid()!=true)throw new DomainConflictException("Invalid retained idle plan.");var info=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==snapshotId,token);var observed=ParseSettings(info?.ResultJson);
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==item.DeviceId,token);var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
        if(info is null||info.DeviceId!=item.DeviceId||info.Kind!="settings.read"||info.State!="succeeded"||info.RequestedAtUtc<=item.FinishedAtUtc||info.TimeZoneId!=item.TimeZoneId||zone!=item.TimeZoneId||!info.ExpectedDeviceVersion.SequenceEqual(item.ExpectedDeviceVersion)||!device.Version.SequenceEqual(item.ExpectedDeviceVersion)||observed?.IsValid()!=true||observed.Serial!=plan.Serial||observed.Product!=plan.Product||observed.Firmware!=plan.Firmware||(observed.SleepTime!=plan.ExpectedMinutes&&observed.SleepTime!=plan.NewMinutes))throw new DomainConflictException("Fresh same-model old/new idle observation required; do not retry an unknown write.");
        item.ResolutionInfoId=info.Id;item.ResolvedByUserId=actor;item.ResolvedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
    private async Task<bool> KeysAuthorized(Guid service,DeviceOperationRecord item,CancellationToken ct)=>item.Kind=="settings.read"&&await transaction.HasPermissionAsync(service,ExecuteFunctionKeys,ct)&&await transaction.HasDevicePermissionAsync(item.RequestedByUserId,ReadFunctionKeys,item.DeviceId,ct);
    private static ZkRawSettingPlan? ParseRaw(string? json)=>ParseTyped<ZkRawSettingPlan>(json);
    private static ZkIdlePlan? ParseIdle(string? json)=>ParseTyped<ZkIdlePlan>(json);
    private static ZkNetworkPlan? ParsePlan(string? json)=>ParseTyped<ZkNetworkPlan>(json);
    private static ZkNetworkObservation? ParseNetwork(string? json)=>ParseTyped<ZkNetworkObservation>(json);
    private static T? ParseTyped<T>(string? json)where T:class
    {try{return json is null?null:JsonSerializer.Deserialize<T>(json,new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});}catch(JsonException){return null;}}

    private async Task<DeviceOperationRecord> Load(Guid id,byte[] version,CancellationToken ct)
    {
        if(version is not{Length:8})throw new ArgumentException("Version required.");var item=await db.DeviceOperations.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown job.");
        if(!item.Version.SequenceEqual(version))throw new DomainConflictException("Stale version.");return item;
    }
    private async Task<DeviceOperationRecord> Owned(Guid service,Guid id,Guid claimId,CancellationToken ct)
    {
        var item=await db.DeviceOperations.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown job.");
        if(item.State!="running"||item.ServiceUserId!=service||item.ClaimId!=claimId||item.ClaimedAtUtc<clock.GetUtcNow().AddMinutes(-2))throw new DomainConflictException("Expired or foreign claim.");return item;
    }
    private static ZkAdvancedSettings? ParseSettings(string? json)
    {
        try{var item=json is null?null:JsonSerializer.Deserialize<ZkAdvancedSettings>(json,new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});return item?.IsValid()==true?item:null;}catch(JsonException){return null;}
    }
    private static ZkDeviceInformation? ParseInformation(string? json)
    {
        try{var item=json is null?null:JsonSerializer.Deserialize<ZkDeviceInformation>(json,new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});return item?.IsValid()==true?item:null;}catch(JsonException){return null;}
    }
    public static string LocalClock(DateTimeOffset utc,string zoneId)
    {
        try{var zone=TimeZoneInfo.FindSystemTimeZoneById(zoneId);var local=TimeZoneInfo.ConvertTime(utc,zone).DateTime;if(zone.IsAmbiguousTime(local)||zone.IsInvalidTime(local)||TimeZoneInfo.ConvertTimeToUtc(local,zone)!=utc.UtcDateTime)throw new ArgumentException("Ambiguous device wall clock.");return local.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);}catch(TimeZoneNotFoundException){throw new ArgumentException("Unsupported site time zone.");}catch(InvalidTimeZoneException){throw new ArgumentException("Invalid site time zone.");}
    }
}
