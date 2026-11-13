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

public sealed class DeviceUserUploads(IoDbContext db,PermissionTransaction transaction,TimeProvider clock,IDataProtectionProvider protection)
{
    public const string Read=DeviceUserDownloads.Read,Upload="devices.users.upload",Execute="devices.users.upload.execute";
    public sealed record Item(Guid Id,Guid DeviceId,byte[] ExpectedDeviceVersion,Guid SnapshotId,Guid EmployeeId,string PersonIdentifier,string Card,[property:System.Text.Json.Serialization.JsonRequired]bool Enabled,int TimeoutSeconds)
    {public override string ToString()=>"Upload item [personal data redacted]";}
    public sealed record Batch(Guid BatchId,Item[] Items,string Confirmation,string? PreviewHash=null)
    {public override string ToString()=>"Upload batch [personal data redacted]";}
    public sealed record Claim(Guid Id,Guid Token,Guid DeviceId,string? Host,int? Port,int TimeoutSeconds,ZkUser? Expected,ZkUser Desired,string WriteFormat=ZkUserReadFormats.Ssr);
    public Task<Guid> RequestAsync(Guid actor,Batch input,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Upload,"device-user-upload.requested",async token=>
    {
        if(input is null||input.BatchId==Guid.Empty||input.Items is not{Length:>0 and <=50}||input.Items.Any(x=>x is null||x.Id==Guid.Empty||x.DeviceId==Guid.Empty||x.EmployeeId==Guid.Empty||x.SnapshotId==Guid.Empty||x.ExpectedDeviceVersion is not{Length:8}||x.TimeoutSeconds is <1 or >60)||input.Items.Select(x=>x.Id).Distinct().Count()!=input.Items.Length||input.Items.Select(x=>(x.DeviceId,x.PersonIdentifier)).Distinct().Count()!=input.Items.Length||input.Confirmation!="UPLOAD-ORDINARY-USERS")throw new ArgumentException("Explicit unique intents, snapshots, bounded page and confirmation required.");
        await transaction.LockAsync("IO.DeviceConnections",token);var hash=Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(input)));
        var old=await db.DeviceUserUploads.AsNoTracking().Where(x=>x.BatchId==input.BatchId).ToListAsync(token);
        if(old.Count>0){if(old.Count!=input.Items.Length||old.Any(x=>x.RequestedByUserId!=actor||x.RequestHash!=hash))throw new DomainConflictException("Changed batch retry.");return(input.BatchId,input.BatchId);}
        var jobs=await Plan(actor,input,hash,token);
        if(input.PreviewHash is null||input.PreviewHash!=PlanHash(jobs))throw new DomainConflictException("Reread the exact server preview before confirming name/card changes.");
        db.DeviceUserUploads.AddRange(jobs);
        return(input.BatchId,input.BatchId);
    },ct);
    public Task<object> PreviewAsync(Guid actor,Batch input,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Upload,"device-user-upload.previewed",async token=>
    {
        if(input is null||input.BatchId==Guid.Empty||input.Items is not{Length:>0 and <=50}||input.Items.Any(x=>x is null||x.Id==Guid.Empty||x.DeviceId==Guid.Empty||x.EmployeeId==Guid.Empty||x.SnapshotId==Guid.Empty||x.ExpectedDeviceVersion is not{Length:8}||x.TimeoutSeconds is <1 or >60)||input.Items.Select(x=>x.Id).Distinct().Count()!=input.Items.Length||input.Items.Select(x=>(x.DeviceId,x.PersonIdentifier)).Distinct().Count()!=input.Items.Length)throw new ArgumentException("Valid bounded preview required.");
        await transaction.LockAsync("IO.DeviceConnections",token);var jobs=await Plan(actor,input,"preview",token);
        var formats=await Formats(jobs.Select(x=>x.SnapshotId),token);
        return(new{previewHash=PlanHash(jobs),items=jobs.Select(x=>new{x.Id,x.DeviceId,x.EmployeeId,x.PersonIdentifier,writeFormat=formats[x.SnapshotId],expected=x.ExpectedJson is null?null:JsonSerializer.Deserialize<ZkUser>(x.ExpectedJson),desired=JsonSerializer.Deserialize<ZkUser>(x.DesiredJson)})},input.BatchId);
    },ct);
    private static string PlanHash(List<DeviceUserUploadRecord> rows)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(rows.Select(x=>new{x.Id,x.DeviceId,x.EmployeeId,x.SnapshotId,x.ExpectedDeviceVersion,x.ExpectedJson,x.DesiredJson,x.TimeoutSeconds}))));
    private async Task<List<DeviceUserUploadRecord>> Plan(Guid actor,Batch input,string hash,CancellationToken token)
    {
        var jobs=new List<DeviceUserUploadRecord>();
        foreach(var item in input.Items)
        {
            if(await db.DeviceUserUploads.AnyAsync(x=>x.Id==item.Id,token))throw new DomainConflictException("Operation id already used.");
            var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==item.DeviceId,token)??throw new ArgumentException("Unknown device.");
            if(!device.Enabled||device.Profile!="zk-standalone"||!device.Version.SequenceEqual(item.ExpectedDeviceVersion))throw new DomainConflictException("Changed device or unsupported profile.");
            if(await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==item.DeviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has unfinished attendance extraction.");
            if(await db.DeviceConnections.AnyAsync(x=>x.DeviceId==item.DeviceId&&(x.State=="pending"||x.State=="running"),token)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==item.DeviceId&&(x.State=="pending"||x.State=="running"),token)||await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==item.DeviceId&&(x.State=="pending"||x.State=="running"),token)||await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==item.DeviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has unfinished work.");
            if(await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==item.DeviceId&&x.PersonIdentifier==item.PersonIdentifier&&x.State=="unknown"&&x.ResolutionSnapshotId==null,token))throw new DomainConflictException("Review the prior unknown user write before another intent.");
            if(await db.DeviceOperations.AnyAsync(x=>x.DeviceId==item.DeviceId&&(x.Kind=="clock.set"||x.Kind=="device.restart"||x.Kind=="network.ip.set"||x.Kind=="settings.idle.set")&&x.State=="unknown"&&x.ResolvedAtUtc==null,token))throw new DomainConflictException("Review unknown native operation before any user writes.");
            var snapshot=await Snapshot(item.SnapshotId,item.DeviceId,item.ExpectedDeviceVersion,token);
            var employee=await db.Employees.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==item.EmployeeId,token)??throw new ArgumentException("Unknown employee.");
            if(!await Mapped(device.SourceId,snapshot.CompanyId,item.EmployeeId,item.PersonIdentifier,token)||employee.CompanyId!=snapshot.CompanyId)throw new DomainConflictException("An explicit current employee mapping is required.");
            var expected=await Expected(item.SnapshotId,item.PersonIdentifier,token);
            var desired=new ZkUser(item.PersonIdentifier,employee.DisplayName,item.Card,0,item.Enabled);
            var probe=new ZkProbeInput(item.DeviceId,"127.0.0.1",4370,1,"validation-only",0);
            if(!new ZkUserWriteInput(probe,true,true,expected,desired,snapshot.ReadFormat).IsValid())throw new ArgumentException("Only ordinary decimal identities/cards with the approved target snapshot format and untruncated names up to 20 characters are supported. No admin/password/biometric fields.");
            jobs.Add(new(){Id=item.Id,BatchId=input.BatchId,RequestHash=hash,DeviceId=item.DeviceId,EmployeeId=item.EmployeeId,SnapshotId=item.SnapshotId,RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=item.ExpectedDeviceVersion.ToArray(),PersonIdentifier=item.PersonIdentifier,ExpectedJson=expected is null?null:JsonSerializer.Serialize(expected),DesiredJson=JsonSerializer.Serialize(desired),TimeoutSeconds=item.TimeoutSeconds});
        }
        return jobs;
    }
    public Task<object> ListAsync(Guid actor,Guid? batchId,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-user-upload.viewed",async token=>
    {
        if(batchId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid page.");
        var rows=await db.DeviceUserUploads.AsNoTracking().Where(x=>batchId==null||x.BatchId==batchId).OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50).ToListAsync(token);
        var formats=await Formats(rows.Select(x=>x.SnapshotId),token);
        return(new{offset,pageSize=50,items=rows.Select(x=>new{x.Id,x.BatchId,x.DeviceId,x.EmployeeId,x.SnapshotId,x.PersonIdentifier,writeFormat=formats[x.SnapshotId],expected=x.ExpectedJson is null?null:JsonSerializer.Deserialize<ZkUser>(x.ExpectedJson),desired=JsonSerializer.Deserialize<ZkUser>(x.DesiredJson),x.State,x.RequestedAtUtc,x.FinishedAtUtc,x.ResolutionSnapshotId,x.Version})},actor);
    },ct);
    public Task<Guid> CancelAsync(Guid actor,Guid id,byte[] version,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Upload,"device-user-upload.cancelled",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var row=await Load(id,token);Version(row,version);if(row.State!="pending")throw new DomainConflictException("Only pending intents can be cancelled.");row.State="cancelled";row.FinishedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
    public Task<Guid> ReviewAsync(Guid actor,Guid id,byte[] version,Guid snapshotId,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Upload,"device-user-upload.unknown-reviewed",async token=>
    {
        if(confirmation!="REVIEWED-USER")throw new ArgumentException("Explicit unknown-write review required.");await transaction.LockAsync("IO.DeviceConnections",token);var row=await Load(id,token);Version(row,version);
        if(row.State!="unknown"||row.ResolutionSnapshotId!=null)throw new DomainConflictException("Only unresolved unknown outcomes can be reviewed.");
        var snapshot=await Snapshot(snapshotId,row.DeviceId,row.ExpectedDeviceVersion,token);if(snapshot.RequestedAtUtc<=row.FinishedAtUtc)throw new DomainConflictException("A successful post-write snapshot is required.");
        var originalFormat=await db.DeviceUserDownloads.Where(x=>x.Id==row.SnapshotId).Select(x=>x.ReadFormat).SingleAsync(token);
        if(snapshot.ReadFormat!=originalFormat)throw new DomainConflictException("Unknown recovery requires the same identity and native-write contract.");
        _=await Expected(snapshotId,row.PersonIdentifier,token);row.ResolutionSnapshotId=snapshotId;row.ResolvedByUserId=actor;row.ResolvedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
    public Task<Claim?> ClaimAsync(Guid service,CancellationToken ct=default)=>transaction.ExecuteAsync<Claim?>(service,Execute,"device-user-upload.claimed",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var now=clock.GetUtcNow();
        foreach(var stale in await db.DeviceUserUploads.AsTracking().Where(x=>x.State=="running"&&x.ClaimedAtUtc!=null&&x.ClaimedAtUtc.Value.AddSeconds(x.TimeoutSeconds+120)<now).ToListAsync(token)){stale.State="unknown";stale.FinishedAtUtc=now;}
        // Recovered entries must be saved before evaluating the filtered running index.
        await db.SaveChangesAsync(token);
        var row=await db.DeviceUserUploads.AsTracking().Where(x=>x.State=="pending"&&!db.DeviceUserUploads.Any(r=>r.DeviceId==x.DeviceId&&r.State=="running")).OrderBy(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);if(row is null)return(null,service);
        var d=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==row.DeviceId,token);
        if(!await transaction.HasDevicePermissionAsync(row.RequestedByUserId,Upload,row.DeviceId,token)){row.State="revoked";row.FinishedAtUtc=now;return(null,row.Id);}
        var snapshot=await db.DeviceUserDownloads.AsNoTracking().SingleAsync(x=>x.Id==row.SnapshotId,token);var desired=JsonSerializer.Deserialize<ZkUser>(row.DesiredJson)!;
        if(!d.Enabled||!d.Version.SequenceEqual(row.ExpectedDeviceVersion)||!await Mapped(d.SourceId,snapshot.CompanyId,row.EmployeeId,row.PersonIdentifier,token)||!await db.Employees.AnyAsync(x=>x.Id==row.EmployeeId&&x.DisplayName==desired.Name,token)){row.State="device-changed";row.FinishedAtUtc=now;return(null,row.Id);}
        // Unknown recovery for the same person must not be bypassed by queued siblings.
        if(await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==row.DeviceId&&x.PersonIdentifier==row.PersonIdentifier&&x.State=="unknown"&&x.ResolutionSnapshotId==null,token))return(null,row.Id);
        if(await DeviceWriteSafety.UnreviewedAsync(db,row.DeviceId,token))return(null,row.Id);
        row.State="running";row.ServiceUserId=service;row.ClaimId=Guid.NewGuid();row.ClaimedAtUtc=now;
        return(new Claim(row.Id,row.ClaimId.Value,d.Id,d.Host,d.Port,row.TimeoutSeconds,row.ExpectedJson is null?null:JsonSerializer.Deserialize<ZkUser>(row.ExpectedJson),desired,snapshot.ReadFormat),row.Id);
    },ct);
    public Task<DeviceCommunicationKey?> CredentialAsync(Guid service,Claim claim,bool allowDefault,CancellationToken ct=default)=>transaction.ExecuteAsync<DeviceCommunicationKey?>(service,Execute,"device-user-upload.credential-used",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var row=await Owned(service,claim.Id,claim.Token,token);var d=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==row.DeviceId,token);
        var format=await db.DeviceUserDownloads.Where(x=>x.Id==row.SnapshotId).Select(x=>x.ReadFormat).SingleAsync(token);
        if(claim.DeviceId!=row.DeviceId||claim.Host!=d.Host||claim.Port!=d.Port||claim.TimeoutSeconds!=row.TimeoutSeconds||claim.Expected!=(row.ExpectedJson is null?null:JsonSerializer.Deserialize<ZkUser>(row.ExpectedJson))||claim.Desired!=JsonSerializer.Deserialize<ZkUser>(row.DesiredJson)||claim.WriteFormat!=format||!d.Enabled||!d.Version.SequenceEqual(row.ExpectedDeviceVersion)||!await transaction.HasDevicePermissionAsync(row.RequestedByUserId,Upload,row.DeviceId,token))return(null,row.Id);
        var snapshot=await db.DeviceUserDownloads.AsNoTracking().SingleAsync(x=>x.Id==row.SnapshotId,token);
        if(d.SourceId!=snapshot.SourceId||!await Mapped(d.SourceId,snapshot.CompanyId,row.EmployeeId,row.PersonIdentifier,token)||!await db.Employees.AnyAsync(x=>x.Id==row.EmployeeId&&x.CompanyId==snapshot.CompanyId&&x.DisplayName==claim.Desired.Name,token)||await DeviceWriteSafety.UnreviewedAsync(db,row.DeviceId,token))return(null,row.Id);
        var cipher=await db.DeviceSecrets.Where(x=>x.DeviceId==d.Id).Select(x=>x.ProtectedValue).SingleOrDefaultAsync(token);if(cipher is null)return(allowDefault?new DeviceCommunicationKey(0):null,row.Id);
        try{var text=protection.CreateProtector("IO.Devices.Credential.v1",d.Id.ToString("N")).Unprotect(cipher);return(text.Length>0&&text.All(c=>c is >='0' and <='9')&&int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var key)?new DeviceCommunicationKey(key):null,row.Id);}catch(CryptographicException){return(null,row.Id);}
    },ct);
    public Task<Guid> CompleteAsync(Guid service,Guid id,Guid claim,ZkUserWriteOutput outcome,CancellationToken ct=default)=>transaction.ExecuteAsync(service,Execute,"device-user-upload.finished",async token=>
    {
        if(outcome?.IsValid()!=true)throw new ArgumentException("Invalid private outcome.");await transaction.LockAsync("IO.DeviceConnections",token);var row=await Owned(service,id,claim,token);row.State=outcome.State;row.FinishedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
    // SnapshotId and its ReadFormat are both SQL-immutable, so the existing
    // approval hash binds the format without a mutable duplicate column.
    private Task<Dictionary<Guid,string>> Formats(IEnumerable<Guid> ids,CancellationToken ct)
    {var keys=ids.Distinct().ToArray();return db.DeviceUserDownloads.AsNoTracking().Where(x=>keys.Contains(x.Id)).ToDictionaryAsync(x=>x.Id,x=>x.ReadFormat,ct);}
    private async Task<DeviceUserDownloadRecord> Snapshot(Guid id,Guid device,byte[] version,CancellationToken ct)
    {var row=await db.DeviceUserDownloads.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown snapshot.");if(!ZkUserReadFormats.IsKnown(row.ReadFormat)||row.State!="succeeded"||row.DeviceId!=device||!row.ExpectedDeviceVersion.SequenceEqual(version))throw new DomainConflictException("Successful same-version target snapshot required.");return row;}
    private async Task<ZkUser?> Expected(Guid snapshot,string person,CancellationToken ct)
    {if(string.IsNullOrWhiteSpace(person))throw new ArgumentException("Exact identity required.");var length=Encoding.Unicode.GetByteCount(person);var rows=await db.DeviceDownloadedUsers.AsNoTracking().Where(x=>x.DownloadId==snapshot&&x.PersonIdentifier==person&&EF.Functions.DataLength(x.PersonIdentifier)==length).Take(2).ToListAsync(ct);if(rows.Count>1)throw new DomainConflictException("Duplicate target identity.");var r=rows.SingleOrDefault();return r is null?null:new(r.PersonIdentifier,r.Name,r.Card,r.Privilege,r.Enabled);}
    private async Task<bool> Mapped(Guid source,Guid company,Guid employee,string person,CancellationToken ct)
    {if(string.IsNullOrWhiteSpace(person))return false;var bytes=Encoding.Unicode.GetBytes(person);var now=clock.GetUtcNow();var matches=await db.SourceEmployeeMappings.Where(x=>x.SourceId==source&&x.CompanyId==company&&EF.Property<byte[]>(x,"SourcePersonIdBytes")==bytes&&EF.Property<int>(x,"SourcePersonIdByteLength")==bytes.Length&&x.ValidFromUtc<=now&&(x.ValidUntilUtcExclusive==null||now<x.ValidUntilUtcExclusive)).Select(x=>x.EmployeeId).Take(2).ToListAsync(ct);return matches.Count==1&&matches[0]==employee;}
    private async Task<DeviceUserUploadRecord> Load(Guid id,CancellationToken ct)=>await db.DeviceUserUploads.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown upload.");
    private static void Version(DeviceUserUploadRecord row,byte[] version){if(version is not{Length:8}||!row.Version.SequenceEqual(version))throw new DomainConflictException("Stale upload version.");}
    private async Task<DeviceUserUploadRecord> Owned(Guid service,Guid id,Guid claim,CancellationToken ct)
    {var row=await Load(id,ct);if(row.State!="running"||row.ServiceUserId!=service||row.ClaimId!=claim||row.ClaimedAtUtc is null||row.ClaimedAtUtc.Value.AddSeconds(row.TimeoutSeconds+120)<clock.GetUtcNow())throw new DomainConflictException("Expired or foreign upload claim.");return row;}
}
