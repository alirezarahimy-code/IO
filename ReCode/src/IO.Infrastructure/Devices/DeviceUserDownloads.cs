using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
namespace IO.Infrastructure.Devices;

/// <summary>Read-only explicitly selected SSR or legacy Int32 user snapshots. Never writes employees, cards, passwords or templates back to devices.</summary>
public sealed class DeviceUserDownloads(IoDbContext db,PermissionTransaction transaction,TimeProvider clock,IDataProtectionProvider protection)
{
    public const string Read="devices.users.read",Download="devices.users.download",Execute="devices.users.execute";
    public sealed record Claim(Guid Id,Guid Token,Guid DeviceId,string? Host,int? Port,int TimeoutSeconds,string ReadFormat=ZkUserReadFormats.Ssr);
    public Task<Guid> RequestAsync(Guid actor,Guid id,Guid deviceId,byte[] version,int seconds,CancellationToken ct=default,string readFormat=ZkUserReadFormats.Ssr)=>transaction.ExecuteAsync(actor,Download,"device-users.requested",async token=>
    {
        if(!ZkUserReadFormats.IsKnown(readFormat)||id==Guid.Empty||deviceId==Guid.Empty||version is not{Length:8}||seconds is <1 or >600)throw new ArgumentException("Explicit id, device version and bounded deadline required.");
        await transaction.LockAsync("IO.DeviceConnections",token);
        var old=await db.DeviceUserDownloads.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token);
        if(old is not null){if(old.DeviceId!=deviceId||old.RequestedByUserId!=actor||old.TimeoutSeconds!=seconds||old.ReadFormat!=readFormat||!old.ExpectedDeviceVersion.SequenceEqual(version))throw new DomainConflictException("Changed download retry.");return(id,id);}
        if(await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has unfinished attendance extraction.");
        if(await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has unfinished user uploads.");
        var d=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deviceId,token)??throw new ArgumentException("Unknown device.");
        if(!d.Enabled||!d.Version.SequenceEqual(version))throw new DomainConflictException("Device changed.");
        if(d.Profile!="zk-standalone")throw new ArgumentException("Only explicitly approved native user-read endpoints are supported.");
        if(await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token)||await db.DeviceConnections.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device already has active work.");
        var company=await db.Sites.Where(x=>x.Id==d.SiteId).Select(x=>x.CompanyId).SingleAsync(token);
        db.DeviceUserDownloads.Add(new(){Id=id,DeviceId=deviceId,SourceId=d.SourceId,CompanyId=company,RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=version.ToArray(),TimeoutSeconds=seconds,ReadFormat=readFormat});return(id,id);
    },ct);
    public Task<object> ListAsync(Guid actor,Guid? deviceId,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-users.viewed",async token=>
    {
        if(deviceId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid page.");
        var items=await db.DeviceUserDownloads.AsNoTracking().Where(x=>deviceId==null||x.DeviceId==deviceId).OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50)
            .Select(x=>new{x.Id,x.DeviceId,x.SourceId,x.CompanyId,x.State,x.ReadFormat,x.ReceivedCount,x.RequestedAtUtc,x.FinishedAtUtc,x.ExpectedDeviceVersion,x.Version}).ToListAsync(token);
        return(new{offset,pageSize=50,items},actor);
    },ct);
    public Task<object> PreviewAsync(Guid actor,Guid id,long after,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-users.previewed",async token=>
    {
        if(after<0)throw new ArgumentException("Invalid cursor.");
        var job=await db.DeviceUserDownloads.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown download.");
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==job.DeviceId,token);
        var sameDevice=device.Version.SequenceEqual(job.ExpectedDeviceVersion)&&device.SourceId==job.SourceId&&await db.Sites.AnyAsync(x=>x.Id==device.SiteId&&x.CompanyId==job.CompanyId,token);
        var rows=await db.DeviceDownloadedUsers.AsNoTracking().Where(x=>x.DownloadId==id&&x.Sequence>after).OrderBy(x=>x.Sequence).Take(50).ToListAsync(token);
        var items=new List<object>();foreach(var row in rows)
        {
            var bytes=Encoding.Unicode.GetBytes(row.PersonIdentifier);var at=job.RequestedAtUtc;
            var duplicate=await db.DeviceDownloadedUsers.CountAsync(x=>x.DownloadId==id&&x.PersonIdentifier==row.PersonIdentifier&&EF.Functions.DataLength(x.PersonIdentifier)==bytes.Length,token)>1;
            var mappings=await db.SourceEmployeeMappings.AsNoTracking().Where(x=>x.SourceId==job.SourceId&&EF.Property<byte[]>(x,"SourcePersonIdBytes")==bytes&&EF.Property<int>(x,"SourcePersonIdByteLength")==bytes.Length&&x.ValidFromUtc<=at&&(x.ValidUntilUtcExclusive==null||at<x.ValidUntilUtcExclusive)).Select(x=>x.EmployeeId).Take(2).ToListAsync(token);
            var candidates=await db.Employees.AsNoTracking().Where(x=>x.CompanyId==job.CompanyId&&EF.Functions.Collate(x.PersonnelNumber,"Latin1_General_100_BIN2")==row.PersonIdentifier&&EF.Functions.DataLength(x.PersonnelNumber)==bytes.Length).Select(x=>new{x.Id,x.DisplayName}).Take(2).ToListAsync(token);
            var status=!sameDevice?"device-changed":job.State!="succeeded"?"incomplete-download":duplicate?"duplicate-identifier":mappings.Count>1||candidates.Count>1?"mapping-conflict":mappings.Count==1&&candidates.Count==1&&mappings[0]!=candidates[0].Id?"mapping-conflict":mappings.Count==1?"mapped":candidates.Count==1?"candidate-requires-confirmation":"unknown-identifier";
            var nameDiffers=candidates.Count==1&&!string.Equals(candidates[0].DisplayName,row.Name,StringComparison.Ordinal);
            items.Add(new{row.Sequence,row.PersonIdentifier,row.Name,row.Card,row.Privilege,row.Enabled,status,nameDiffers,mappedEmployeeId=mappings.Count==1?(Guid?)mappings[0]:null,candidateEmployeeId=candidates.Count==1?(Guid?)candidates[0].Id:null});
        }
        return(new{downloadId=id,job.SourceId,job.State,job.ReadFormat,job.ReceivedCount,sameDevice,after,nextAfter=rows.LastOrDefault()?.Sequence??after,items},id);
    },ct);
    public Task<Guid> CancelAsync(Guid actor,Guid id,byte[] version,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Download,"device-users.cancelled",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var row=await db.DeviceUserDownloads.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown download.");
        if(version is not{Length:8}||!row.Version.SequenceEqual(version)||row.State!="pending")throw new DomainConflictException("Only current pending downloads can be cancelled.");row.State="cancelled";row.FinishedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
    public Task<Claim?> ClaimAsync(Guid service,CancellationToken ct=default)=>transaction.ExecuteAsync<Claim?>(service,Execute,"device-users.claimed",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var now=clock.GetUtcNow();
        foreach(var row in await db.DeviceUserDownloads.AsTracking().Where(x=>x.State=="running"&&x.LeaseAtUtc!=null&&x.LeaseAtUtc.Value.AddSeconds(x.TimeoutSeconds+120)<now).ToListAsync(token)){row.State="interrupted";row.FinishedAtUtc=now;}
        var next=await db.DeviceUserDownloads.AsTracking().Where(x=>x.State=="pending").OrderBy(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);if(next is null)return(null,service);
        if(!await transaction.HasDevicePermissionAsync(next.RequestedByUserId,Download,next.DeviceId,token)){next.State="revoked";next.FinishedAtUtc=now;return(null,next.Id);}
        var d=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==next.DeviceId,token);
        if(!d.Enabled||!d.Version.SequenceEqual(next.ExpectedDeviceVersion)){next.State="device-changed";next.FinishedAtUtc=now;return(null,next.Id);}
        next.State="running";next.ServiceUserId=service;next.ClaimId=Guid.NewGuid();next.LeaseAtUtc=now;
        return(new Claim(next.Id,next.ClaimId.Value,d.Id,d.Host,d.Port,next.TimeoutSeconds,next.ReadFormat),next.Id);
    },ct);
    public Task<DeviceCommunicationKey?> CredentialAsync(Guid service,Claim claim,bool allowDefault,CancellationToken ct=default)=>transaction.ExecuteAsync<DeviceCommunicationKey?>(service,Execute,"device-users.credential-used",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Owned(service,claim.Id,claim.Token,token);
        var d=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==item.DeviceId,token);
        if(item.DeviceId!=claim.DeviceId||claim.Host!=d.Host||claim.Port!=d.Port||claim.TimeoutSeconds!=item.TimeoutSeconds||claim.ReadFormat!=item.ReadFormat||!d.Enabled||!d.Version.SequenceEqual(item.ExpectedDeviceVersion)||!await transaction.HasDevicePermissionAsync(item.RequestedByUserId,Download,item.DeviceId,token))return(null,item.Id);
        if(d.SourceId!=item.SourceId||!await db.Sites.AnyAsync(x=>x.Id==d.SiteId&&x.CompanyId==item.CompanyId,token))return(null,item.Id);
        var ciphertext=await db.DeviceSecrets.Where(x=>x.DeviceId==item.DeviceId).Select(x=>x.ProtectedValue).SingleOrDefaultAsync(token);if(ciphertext is null)return(allowDefault?new DeviceCommunicationKey(0):null,item.Id);
        try{var text=protection.CreateProtector("IO.Devices.Credential.v1",item.DeviceId.ToString("N")).Unprotect(ciphertext);return(text.Length>0&&text.All(c=>c is >='0' and <='9')&&int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var key)?new DeviceCommunicationKey(key):null,item.Id);}catch(CryptographicException){return(null,item.Id);}
    },ct);
    public Task<long> AppendAsync(Guid service,Guid id,Guid claim,long after,IReadOnlyList<ZkUser> users,CancellationToken ct=default)=>transaction.ExecuteAsync(service,Execute,"device-users.batch-received",async token=>
    {
        if(users is null||users.Count is <1 or >50||users.Any(x=>x?.IsValid()!=true)||after<0)throw new ArgumentException("Invalid private user batch.");
        await transaction.LockAsync("IO.DeviceConnections",token);var job=await Owned(service,id,claim,token);
        if(!await transaction.HasDevicePermissionAsync(job.RequestedByUserId,Download,job.DeviceId,token))throw new UnauthorizedAccessException();
        if(job.ReadFormat==ZkUserReadFormats.Legacy&&users.Any(x=>!ZkUserReadFormats.IsCanonicalLegacyIdentity(x.PersonIdentifier)))throw new ArgumentException("Legacy identifiers must be canonical nonnegative Int32 text.");
        static string Hash(ZkUser user)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(user))));
        if(after!=job.ReceivedCount)
        {
            var previous=await db.DeviceDownloadedUsers.AsNoTracking().Where(x=>x.DownloadId==id&&x.Sequence>after&&x.Sequence<=after+users.Count).OrderBy(x=>x.Sequence).Select(x=>x.Fingerprint).ToArrayAsync(token);
            if(previous.Length!=users.Count||!previous.SequenceEqual(users.Select(Hash)))throw new DomainConflictException("Changed batch or nonsequential cursor.");return(after+users.Count,id);
        }
        foreach(var u in users)db.DeviceDownloadedUsers.Add(new(){DownloadId=id,Sequence=++job.ReceivedCount,PersonIdentifier=u.PersonIdentifier,Name=u.Name,Card=u.Card,Privilege=u.Privilege,Enabled=u.Enabled,Fingerprint=Hash(u)});
        job.LeaseAtUtc=clock.GetUtcNow();return(job.ReceivedCount,id);
    },ct);
    public Task<Guid> CompleteAsync(Guid service,Guid id,Guid claim,string state,long count,CancellationToken ct=default)=>transaction.ExecuteAsync(service,Execute,"device-users.finished",async token=>
    {
        if(!new[]{"succeeded","incomplete","failed","interrupted","timed-out","unsupported","identity-mismatch","credential-unavailable","endpoint-denied","bridge-failed"}.Contains(state,StringComparer.Ordinal)||count<0)throw new ArgumentException("Invalid outcome.");
        await transaction.LockAsync("IO.DeviceConnections",token);var job=await Owned(service,id,claim,token);
        if(state=="succeeded"&&(count!=job.ReceivedCount||await db.DeviceDownloadedUsers.LongCountAsync(x=>x.DownloadId==id,token)!=count))throw new DomainConflictException("Uncommitted or incomplete stream cannot be labelled complete.");
        job.State=state;job.FinishedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
    private async Task<DeviceUserDownloadRecord> Owned(Guid service,Guid id,Guid claim,CancellationToken ct)
    {
        var job=await db.DeviceUserDownloads.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown download.");
        if(job.State!="running"||job.ServiceUserId!=service||job.ClaimId!=claim||job.LeaseAtUtc is null||job.LeaseAtUtc.Value.AddSeconds(job.TimeoutSeconds+120)<clock.GetUtcNow())throw new DomainConflictException("Expired or foreign claim.");return job;
    }
}
