using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Devices;

/// <summary>S06 typed information and clock jobs only; not the full S11 command queue.</summary>
public sealed class DeviceOperations(IoDbContext db,PermissionTransaction transaction,TimeProvider clock,IDataProtectionProvider protection)
{
    public const string Read="devices.info.read",SetClock="devices.clock.set",Execute="devices.operations.execute";
    public sealed record Claim(Guid Id,Guid Token,Guid DeviceId,string Profile,string? Host,int? Port,string Kind,string? ClockLocal,int TimeoutSeconds);
    public sealed record View(Guid Id,Guid DeviceId,string Kind,string State,DateTimeOffset RequestedAtUtc,DateTimeOffset? FinishedAtUtc,
        DateTimeOffset? TargetUtc,string TimeZoneId,ZkDeviceInformation? Information,Guid? ResolutionInfoId,DateTimeOffset? ResolvedAtUtc,byte[] Version);
    private static string Permission(string kind)=>kind switch{"information.read"=>Read,"clock.set"=>SetClock,_=>throw new ArgumentException("Unknown operation.")};
    public Task<Guid> RequestAsync(Guid actor,Guid operationId,Guid deviceId,byte[] expectedDeviceVersion,string kind,int timeoutSeconds,DateTimeOffset? targetUtc,string? confirmation,CancellationToken ct=default)=>
        transaction.ExecuteAsync(actor,Permission(kind),"device-operation.requested",async token=>
        {
            if(operationId==Guid.Empty||deviceId==Guid.Empty||expectedDeviceVersion is not{Length:8}||timeoutSeconds is <1 or >60||
                (kind=="information.read"&&(targetUtc is not null||confirmation is not null))||
                (kind=="clock.set"&&(targetUtc is null||targetUtc.Value.Offset!=TimeSpan.Zero||targetUtc.Value.Ticks%TimeSpan.TicksPerSecond!=0||confirmation!="SET-CLOCK")))
                throw new ArgumentException("Explicit operation, version, UTC seconds and confirmation required.");
            await transaction.LockAsync("IO.DeviceConnections",token);
            var old=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==operationId,token);
            if(old is not null)
            {
                if(old.DeviceId!=deviceId||old.RequestedByUserId!=actor||old.Kind!=kind||old.TimeoutSeconds!=timeoutSeconds||old.TargetUtc!=targetUtc||!old.ExpectedDeviceVersion.SequenceEqual(expectedDeviceVersion))throw new DomainConflictException("Changed operation retry.");
                return(old.Id,old.Id);
            }
            var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deviceId,token)??throw new ArgumentException("Unknown device.");
            if(!device.Enabled||!device.Version.SequenceEqual(expectedDeviceVersion))throw new DomainConflictException("Changed or disabled device.");
            if(device.Profile!="zk-standalone")throw new ArgumentException("No verified operation adapter for this profile.");
            if(await db.DeviceOperations.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token)||await db.DeviceConnections.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device already has queued or running work.");
            if(kind=="clock.set"&&await db.DeviceOperations.AnyAsync(x=>x.DeviceId==deviceId&&x.Kind=="clock.set"&&x.State=="unknown"&&x.ResolvedAtUtc==null,token))throw new DomainConflictException("Read and explicitly reconcile the previous unknown clock write first.");
            var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
            if(targetUtc is not null)_=LocalClock(targetUtc.Value,zone);
            db.DeviceOperations.Add(new(){Id=operationId,DeviceId=deviceId,Kind=kind,RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=expectedDeviceVersion.ToArray(),TimeoutSeconds=timeoutSeconds,TimeZoneId=zone,TargetUtc=targetUtc});
            return(operationId,operationId);
        },ct);

    public Task<object> ListAsync(Guid actor,Guid? deviceId,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-operation.viewed",async token=>
    {
        if(deviceId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid page.");
        var rows=await db.DeviceOperations.AsNoTracking().Where(x=>deviceId==null||x.DeviceId==deviceId).OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50).ToListAsync(token);
        var items=rows.Select(x=>new View(x.Id,x.DeviceId,x.Kind,x.State,x.RequestedAtUtc,x.FinishedAtUtc,x.TargetUtc,x.TimeZoneId,ParseInformation(x.ResultJson),x.ResolutionInfoId,x.ResolvedAtUtc,x.Version)).ToArray();
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

    public Task<Claim?> ClaimAsync(Guid service,CancellationToken ct=default)=>transaction.ExecuteAsync<Claim?>(service,Execute,"device-operation.claimed",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var now=clock.GetUtcNow();
        var expired=await db.DeviceOperations.AsTracking().Where(x=>x.State=="running"&&x.ClaimedAtUtc<now.AddMinutes(-2)).ToListAsync(token);
        foreach(var item in expired){item.State=item.Kind=="clock.set"?"unknown":"interrupted";item.FinishedAtUtc=now;}
        var next=await db.DeviceOperations.AsTracking().Where(x=>x.State=="pending").OrderBy(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);
        if(next is null)return(null,service);
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==next.DeviceId,token);
        if(!await transaction.HasPermissionAsync(next.RequestedByUserId,Permission(next.Kind),token)){next.State="revoked";next.FinishedAtUtc=now;return(null,next.Id);}
        var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
        if(!device.Enabled||!device.Version.SequenceEqual(next.ExpectedDeviceVersion)||zone!=next.TimeZoneId){next.State="device-changed";next.FinishedAtUtc=now;return(null,next.Id);}
        if(next.Kind=="clock.set"&&next.RequestedAtUtc<now.AddMinutes(-2)){next.State="expired";next.FinishedAtUtc=now;return(null,next.Id);}
        next.ServiceUserId=service;next.ClaimId=Guid.NewGuid();next.ClaimedAtUtc=now;next.State="running";
        return(new Claim(next.Id,next.ClaimId.Value,next.DeviceId,device.Profile,device.Host,device.Port,next.Kind,next.TargetUtc is null?null:LocalClock(next.TargetUtc.Value,next.TimeZoneId),next.TimeoutSeconds),next.Id);
    },ct);

    public Task<Guid> CompleteAsync(Guid service,Guid id,Guid claimId,ZkOperationOutput result,CancellationToken ct=default)=>transaction.ExecuteAsync(service,Execute,"device-operation.finished",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Owned(service,id,claimId,token);
        if(!new[]{"succeeded","unsupported","identity-mismatch","failed","unknown","timed-out","interrupted","endpoint-denied","credential-unavailable","bridge-failed"}.Contains(result.State,StringComparer.Ordinal)||
            (result.Information is not null&&(item.Kind!="information.read"||result.State!="succeeded"||!result.Information.IsValid()))||
            (item.Kind=="information.read"&&result.State=="succeeded"&&result.Information is null))throw new ArgumentException("Invalid typed operation result.");
        item.State=item.Kind=="clock.set"&&result.State is "timed-out" or "interrupted" or "bridge-failed" or "unknown"?"unknown":result.State;
        item.FinishedAtUtc=clock.GetUtcNow();item.ResultJson=result.Information is null?null:JsonSerializer.Serialize(result.Information);
        return(id,id);
    },ct);

    public Task<DeviceCommunicationKey?> CredentialAsync(Guid service,Claim claim,bool allowDefault,CancellationToken ct=default)=>transaction.ExecuteAsync<DeviceCommunicationKey?>(service,Execute,"device-operation.credential-used",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Owned(service,claim.Id,claim.Token,token);
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==item.DeviceId,token);
        var zone=await db.Sites.Where(x=>x.Id==device.SiteId).Select(x=>x.TimeZoneId).SingleAsync(token);
        if((item.Kind=="clock.set"&&item.RequestedAtUtc<clock.GetUtcNow().AddMinutes(-2))||item.DeviceId!=claim.DeviceId||item.Kind!=claim.Kind||!device.Enabled||!device.Version.SequenceEqual(item.ExpectedDeviceVersion)||zone!=item.TimeZoneId||!await transaction.HasPermissionAsync(item.RequestedByUserId,Permission(item.Kind),token))return(null,item.Id);
        var ciphertext=await db.DeviceSecrets.Where(x=>x.DeviceId==item.DeviceId).Select(x=>x.ProtectedValue).SingleOrDefaultAsync(token);
        if(ciphertext is null)return(allowDefault?new DeviceCommunicationKey(0):null,item.Id);
        try{var text=protection.CreateProtector("IO.Devices.Credential.v1",item.DeviceId.ToString("N")).Unprotect(ciphertext);return(text.Length>0&&text.All(c=>c is >='0' and <='9')&&int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var key)?new DeviceCommunicationKey(key):null,item.Id);}catch(CryptographicException){return(null,item.Id);}
    },ct);

    public Task<Guid> ResolveClockAsync(Guid actor,Guid id,byte[] version,Guid informationId,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,SetClock,"device-operation.clock-reviewed",async token=>
    {
        if(confirmation!="REVIEWED-CLOCK")throw new ArgumentException("Explicit clock review confirmation required.");
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Load(id,version,token);
        if(item.Kind!="clock.set"||item.State!="unknown"||item.ResolvedAtUtc is not null)throw new DomainConflictException("Only an unresolved unknown clock write can be reviewed.");
        var info=await db.DeviceOperations.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==informationId,token);
        var currentZone=await (from d in db.Devices join site in db.Sites on d.SiteId equals site.Id where d.Id==item.DeviceId select site.TimeZoneId).SingleAsync(token);
        var deviceVersion=await db.Devices.Where(x=>x.Id==item.DeviceId).Select(x=>x.Version).SingleAsync(token);
        if(info is null||info.DeviceId!=item.DeviceId||info.Kind!="information.read"||info.State!="succeeded"||info.FinishedAtUtc<item.FinishedAtUtc||info.TimeZoneId!=item.TimeZoneId||info.TimeZoneId!=currentZone||!info.ExpectedDeviceVersion.SequenceEqual(item.ExpectedDeviceVersion)||!deviceVersion.SequenceEqual(info.ExpectedDeviceVersion)||ParseInformation(info.ResultJson)?.ClockLocal is null)
            throw new DomainConflictException("A later successful clock observation of the same unchanged device is required.");
        item.ResolutionInfoId=info.Id;item.ResolvedByUserId=actor;item.ResolvedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);

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
    private static ZkDeviceInformation? ParseInformation(string? json)
    {
        try{var item=json is null?null:JsonSerializer.Deserialize<ZkDeviceInformation>(json,new JsonSerializerOptions{UnmappedMemberHandling=System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow});return item?.IsValid()==true?item:null;}catch(JsonException){return null;}
    }
    public static string LocalClock(DateTimeOffset utc,string zoneId)
    {
        try{var zone=TimeZoneInfo.FindSystemTimeZoneById(zoneId);var local=TimeZoneInfo.ConvertTime(utc,zone).DateTime;if(zone.IsAmbiguousTime(local)||zone.IsInvalidTime(local)||TimeZoneInfo.ConvertTimeToUtc(local,zone)!=utc.UtcDateTime)throw new ArgumentException("Ambiguous device wall clock.");return local.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture);}catch(TimeZoneNotFoundException){throw new ArgumentException("Unsupported site time zone.");}catch(InvalidTimeZoneException){throw new ArgumentException("Invalid site time zone.");}
    }
}
