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

/// <summary>Explicit-format read-only log extraction; typed raw tuples retained before explicit range/mapping review.</summary>
public sealed class DeviceAttendanceDownloads(IoDbContext db,PermissionTransaction transaction,TimeProvider clock,IDataProtectionProvider protection,IO.Infrastructure.Attendance.AttendanceIngestion ingestion)
{
    public const string Read="devices.attendance.read",Download="devices.attendance.extract",Execute="devices.attendance.execute",Commit="devices.attendance.commit";
    public sealed record Claim(Guid Id,Guid Token,Guid DeviceId,string? Host,int? Port,int TimeoutSeconds,string ReadFormat=ZkAttendanceReadFormats.Ssr);
    public Task<Guid> RequestAsync(Guid actor,Guid id,Guid deviceId,byte[] version,int seconds,DateTimeOffset fromUtc,DateTimeOffset untilUtcExclusive,int inCode,int outCode,CancellationToken ct=default,string readFormat=ZkAttendanceReadFormats.Ssr)=>transaction.ExecuteAsync(actor,Download,"device-attendance.requested",token=>RequestInTransactionAsync(actor,id,deviceId,version,seconds,fromUtc,untilUtcExclusive,inCode,outCode,token,readFormat),ct);
    internal async Task<(Guid Result,Guid Target)> RequestInTransactionAsync(Guid actor,Guid id,Guid deviceId,byte[] version,int seconds,DateTimeOffset fromUtc,DateTimeOffset untilUtcExclusive,int inCode,int outCode,CancellationToken token,string readFormat=ZkAttendanceReadFormats.Ssr)
    {
        if(!await transaction.HasPermissionAsync(actor,Download,token))throw new UnauthorizedAccessException();
        if(!ZkAttendanceReadFormats.IsKnown(readFormat)||id==Guid.Empty||deviceId==Guid.Empty||version is not{Length:8}||seconds is <1 or >600||fromUtc.Offset!=TimeSpan.Zero||untilUtcExclusive.Offset!=TimeSpan.Zero||fromUtc>=untilUtcExclusive||fromUtc.Ticks%TimeSpan.TicksPerSecond!=0||untilUtcExclusive.Ticks%TimeSpan.TicksPerSecond!=0||inCode is <0 or >255||outCode is <0 or >255||inCode==outCode)throw new ArgumentException("Explicit id, device version and bounded deadline required.");
        await transaction.LockAsync("IO.DeviceConnections",token);
        var old=await db.DeviceAttendanceDownloads.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token);
        if(old is not null){if(old.DeviceId!=deviceId||old.RequestedByUserId!=actor||old.TimeoutSeconds!=seconds||old.ReadFormat!=readFormat||old.FromUtc!=fromUtc||old.UntilUtcExclusive!=untilUtcExclusive||old.InCode!=inCode||old.OutCode!=outCode||!old.ExpectedDeviceVersion.SequenceEqual(version))throw new DomainConflictException("Changed download retry.");return(id,id);}
        if(await db.DeviceUserUploads.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has unfinished user uploads.");
        if(await db.DeviceUserDownloads.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device has unfinished user downloads.");
        var d=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==deviceId,token)??throw new ArgumentException("Unknown device.");
        if(!d.Enabled||!d.Version.SequenceEqual(version))throw new DomainConflictException("Device changed.");
        if(d.Profile!="zk-standalone")throw new ArgumentException("Only explicitly approved native endpoints are supported.");
        if(await db.DeviceAttendanceDownloads.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token)||await db.DeviceOperations.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token)||await db.DeviceConnections.AnyAsync(x=>x.DeviceId==deviceId&&(x.State=="pending"||x.State=="running"),token))throw new DomainConflictException("Device already has active work.");
        var site=await db.Sites.AsNoTracking().SingleAsync(x=>x.Id==d.SiteId,token);_=Zone(site.TimeZoneId);
        db.DeviceAttendanceDownloads.Add(new(){Id=id,DeviceId=deviceId,SourceId=d.SourceId,CompanyId=site.CompanyId,SiteId=site.Id,TimeZoneId=site.TimeZoneId,FromUtc=fromUtc,UntilUtcExclusive=untilUtcExclusive,InCode=inCode,OutCode=outCode,RequestedByUserId=actor,RequestedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=version.ToArray(),TimeoutSeconds=seconds,ReadFormat=readFormat});return(id,id);
    }
    public Task<object> ListAsync(Guid actor,Guid? deviceId,int offset,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-attendance.viewed",async token=>
    {
        if(deviceId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid page.");
        var items=await db.DeviceAttendanceDownloads.AsNoTracking().Where(x=>deviceId==null||x.DeviceId==deviceId).OrderByDescending(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(50)
            .Select(x=>new{x.Id,x.DeviceId,x.SourceId,x.CompanyId,x.State,x.ReadFormat,x.ReceivedCount,x.FromUtc,x.UntilUtcExclusive,x.TimeZoneId,x.InCode,x.OutCode,x.LastReviewedAtUtc,x.RequestedAtUtc,x.FinishedAtUtc,x.ExpectedDeviceVersion,x.Version}).ToListAsync(token);
        return(new{offset,pageSize=50,items},actor);
    },ct);
    public Task<Guid> CancelAsync(Guid actor,Guid id,byte[] version,CancellationToken ct=default)=>transaction.ExecuteAsync(actor,Download,"device-attendance.cancelled",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var row=await db.DeviceAttendanceDownloads.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,token)??throw new ArgumentException("Unknown download.");
        if(version is not{Length:8}||!row.Version.SequenceEqual(version)||row.State!="pending")throw new DomainConflictException("Only current pending downloads can be cancelled.");row.State="cancelled";row.FinishedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
    public Task<Claim?> ClaimAsync(Guid service,CancellationToken ct=default)=>transaction.ExecuteAsync<Claim?>(service,Execute,"device-attendance.claimed",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var now=clock.GetUtcNow();
        foreach(var row in await db.DeviceAttendanceDownloads.AsTracking().Where(x=>x.State=="running"&&x.LeaseAtUtc!=null&&x.LeaseAtUtc.Value.AddSeconds(x.TimeoutSeconds+120)<now).ToListAsync(token)){row.State="interrupted";row.FinishedAtUtc=now;}
        var next=await db.DeviceAttendanceDownloads.AsTracking().Where(x=>x.State=="pending").OrderBy(x=>x.RequestedAtUtc).ThenBy(x=>x.Id).FirstOrDefaultAsync(token);if(next is null)return(null,service);
        if(!await transaction.HasDevicePermissionAsync(next.RequestedByUserId,Download,next.DeviceId,token)){next.State="revoked";next.FinishedAtUtc=now;return(null,next.Id);}
        var d=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==next.DeviceId,token);
        if(!d.Enabled||!d.Version.SequenceEqual(next.ExpectedDeviceVersion)||!await SameTarget(next,token)){next.State="device-changed";next.FinishedAtUtc=now;return(null,next.Id);}
        next.State="running";next.ServiceUserId=service;next.ClaimId=Guid.NewGuid();next.LeaseAtUtc=now;
        return(new Claim(next.Id,next.ClaimId.Value,d.Id,d.Host,d.Port,next.TimeoutSeconds,next.ReadFormat),next.Id);
    },ct);
    public Task<DeviceCommunicationKey?> CredentialAsync(Guid service,Claim claim,bool allowDefault,CancellationToken ct=default)=>transaction.ExecuteAsync<DeviceCommunicationKey?>(service,Execute,"device-attendance.credential-used",async token=>
    {
        await transaction.LockAsync("IO.DeviceConnections",token);var item=await Owned(service,claim.Id,claim.Token,token);
        var d=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==item.DeviceId,token);
        if(item.DeviceId!=claim.DeviceId||claim.Host!=d.Host||claim.Port!=d.Port||claim.TimeoutSeconds!=item.TimeoutSeconds||claim.ReadFormat!=item.ReadFormat||!await SameTarget(item,token)||!d.Enabled||!d.Version.SequenceEqual(item.ExpectedDeviceVersion)||!await transaction.HasDevicePermissionAsync(item.RequestedByUserId,Download,item.DeviceId,token))return(null,item.Id);
        var ciphertext=await db.DeviceSecrets.Where(x=>x.DeviceId==item.DeviceId).Select(x=>x.ProtectedValue).SingleOrDefaultAsync(token);if(ciphertext is null)return(allowDefault?new DeviceCommunicationKey(0):null,item.Id);
        try{var text=protection.CreateProtector("IO.Devices.Credential.v1",item.DeviceId.ToString("N")).Unprotect(ciphertext);return(text.Length>0&&text.All(c=>c is >='0' and <='9')&&int.TryParse(text,NumberStyles.None,CultureInfo.InvariantCulture,out var key)?new DeviceCommunicationKey(key):null,item.Id);}catch(CryptographicException){return(null,item.Id);}
    },ct);
    public Task<long> AppendAsync(Guid service,Guid id,Guid claim,long after,IReadOnlyList<ZkAttendanceRaw> records,CancellationToken ct=default)=>transaction.ExecuteAsync(service,Execute,"device-attendance.batch-received",async token=>
    {
        if(records is null||records.Count is <1 or >50||records.Any(x=>x?.IsBounded()!=true)||after<0)throw new ArgumentException("Invalid private SDK batch.");
        await transaction.LockAsync("IO.DeviceConnections",token);var job=await Owned(service,id,claim,token);
        if(!await transaction.HasDevicePermissionAsync(job.RequestedByUserId,Download,job.DeviceId,token))throw new UnauthorizedAccessException();
        if(records.Any(x=>!ZkAttendanceReadFormats.Matches(x,job.ReadFormat)))throw new ArgumentException("SDK tuples must match the immutable requested format.");
        var payloads=records.Select(x=>x.CanonicalJson()).ToArray();
        if(after!=job.ReceivedCount)
        {
            var previous=await db.DeviceAttendanceRows.AsNoTracking().Where(x=>x.DownloadId==id&&x.Sequence>after&&x.Sequence<=after+records.Count).OrderBy(x=>x.Sequence).Select(x=>x.PayloadJson).ToArrayAsync(token);
            if(previous.Length!=records.Count||!previous.SequenceEqual(payloads,StringComparer.Ordinal))throw new DomainConflictException("Changed batch or cursor.");return(after+records.Count,id);
        }
        var ordinals=new Dictionary<string,long>(StringComparer.Ordinal);var seenPayloads=new Dictionary<string,string>(StringComparer.Ordinal);
        foreach(var payload in payloads)
        {
            var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
            if(seenPayloads.TryGetValue(hash,out var seen)&&!string.Equals(seen,payload,StringComparison.Ordinal))throw new DomainConflictException("Hash collision within SDK batch.");seenPayloads[hash]=payload;
            if(!ordinals.TryGetValue(hash,out var ordinal))
            {
                var old=await db.DeviceAttendanceRows.AsNoTracking().Where(x=>x.DownloadId==id&&x.PayloadHash==hash).OrderByDescending(x=>x.OccurrenceOrdinal).Select(x=>new{x.OccurrenceOrdinal,x.PayloadJson}).FirstOrDefaultAsync(token);
                if(old is not null&&!string.Equals(old.PayloadJson,payload,StringComparison.Ordinal))throw new DomainConflictException("Hash collision; retain for investigation.");
                ordinal=old?.OccurrenceOrdinal??0;
            }
            ordinals[hash]=++ordinal;
            db.DeviceAttendanceRows.Add(new(){DownloadId=id,Sequence=++job.ReceivedCount,PayloadJson=payload,PayloadHash=hash,OccurrenceOrdinal=ordinal});
        }
        job.LeaseAtUtc=clock.GetUtcNow();return(job.ReceivedCount,id);
    },ct);
    public Task<Guid> CompleteAsync(Guid service,Guid id,Guid claim,string state,long count,CancellationToken ct=default)=>transaction.ExecuteAsync(service,Execute,"device-attendance.finished",async token=>
    {
        if(!new[]{"succeeded","incomplete","failed","interrupted","timed-out","unsupported","identity-mismatch","credential-unavailable","endpoint-denied","bridge-failed"}.Contains(state,StringComparer.Ordinal)||count<0)throw new ArgumentException("Invalid outcome.");
        await transaction.LockAsync("IO.DeviceConnections",token);var job=await Owned(service,id,claim,token);
        if(state=="succeeded"&&(count!=job.ReceivedCount||await db.DeviceAttendanceRows.LongCountAsync(x=>x.DownloadId==id,token)!=count))throw new DomainConflictException("Uncommitted or incomplete stream cannot be labelled complete.");
        job.State=state;job.FinishedAtUtc=clock.GetUtcNow();return(id,id);
    },ct);
    public Task<object> RowsAsync(Guid actor,Guid id,long after,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-attendance.rows-viewed",async token=>
    {
        if(after<0)throw new ArgumentException("Invalid cursor.");var job=await Load(id,token);var same=await SameTarget(job,token);
        var rows=await db.DeviceAttendanceRows.AsNoTracking().Where(x=>x.DownloadId==id&&x.Sequence>after).OrderBy(x=>x.Sequence).Take(50).ToListAsync(token);
        var items=new List<object>();foreach(var row in rows){var raw=Raw(row);var interpreted=raw.Interpret(Zone(job.TimeZoneId));items.Add(new{row.Sequence,raw,row.PayloadHash,row.OccurrenceOrdinal,status=row.RawEventId is not null?row.Status:await Status(job,raw,same,token),occurredAtUtc=interpreted.Utc,row.RawEventId,row.ConflictId});}
        return(new{downloadId=id,job.State,job.ReadFormat,job.SourceId,job.ReceivedCount,job.FromUtc,job.UntilUtcExclusive,job.TimeZoneId,job.Version,sameTarget=same,after,nextAfter=rows.LastOrDefault()?.Sequence??after,items},id);
    },ct);
    public Task<object> CommitPageAsync(Guid actor,Guid id,byte[] version,long after,string confirmation,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Commit,"device-attendance.page-reviewed",token=>CommitPageInTransactionAsync(actor,id,version,after,confirmation,token),ct);
    internal async Task<(object Result,Guid Target)> CommitPageInTransactionAsync(Guid actor,Guid id,byte[] version,long after,string confirmation,CancellationToken token)
    {
        if(!await transaction.HasPermissionAsync(actor,Commit,token))throw new UnauthorizedAccessException();
        if(version is not{Length:8}||after<0||confirmation!="INGEST-SDK-ROWS")throw new ArgumentException("Explicit page confirmation and version required.");
        if(!await transaction.HasPermissionAsync(actor,"attendance.events.receive",token)&&!await transaction.HasPermissionAsync(actor,IO.Infrastructure.Attendance.AttendanceIngestion.Manage,token))throw new UnauthorizedAccessException();
        await transaction.LockAsync("IO.DeviceRegistry",token);await transaction.LockAsync("IO.DeviceConnections",token);var job=await Load(id,token);
        if(!job.Version.SequenceEqual(version)||job.State!="succeeded"||!await SameTarget(job,token))throw new DomainConflictException("Complete current same-target extraction and fresh version required.");
        var rows=await db.DeviceAttendanceRows.AsTracking().Where(x=>x.DownloadId==id&&x.Sequence>after).OrderBy(x=>x.Sequence).Take(50).ToListAsync(token);int added=0,already=0,quarantined=0,conflicts=0;
        foreach(var row in rows)
        {
            if(row.RawEventId is not null){already++;continue;}
            var raw=Raw(row);row.Status=await Status(job,raw,true,token);row.ReviewedByUserId=actor;row.ReviewedAtUtc=clock.GetUtcNow();
            if(row.Status!="ready"){quarantined++;continue;}
            // Neither observed format has a native record ID. This is an explicit canonical
            // tuple + multiplicity identity, NOT a claim of physical event identity.
            var external=ZkAttendanceReadFormats.ExternalPrefix(job.ReadFormat)+row.PayloadHash+":"+row.OccurrenceOrdinal.ToString(CultureInfo.InvariantCulture);
            var result=await ingestion.AppendInTransactionAsync(job.SourceId,external,raw.PersonIdentifier,raw.Interpret(Zone(job.TimeZoneId)).Utc,job.TimeZoneId,raw.CheckType==job.InCode?"in":"out",Encoding.UTF8.GetBytes(row.PayloadJson),token);
            row.RawEventId=result.Result.EventId;row.ConflictId=result.Result.ConflictId;row.Status=row.ConflictId is not null?"ingestion-conflict":result.Result.Outcome==IO.Domain.Attendance.RawEventAppendResult.AlreadyRecorded?"already-recorded":"imported";
            if(row.ConflictId is not null)conflicts++;else if(row.Status=="already-recorded")already++;else added++;
        }
        job.LastReviewedAtUtc=clock.GetUtcNow();db.Entry(job).Property(x=>x.LastReviewedAtUtc).IsModified=true;
        return(new{downloadId=id,after,nextAfter=rows.LastOrDefault()?.Sequence??after,added,already,quarantined,conflicts},id);
    }
    private async Task<string> Status(DeviceAttendanceDownloadRecord job,ZkAttendanceRaw raw,bool same,CancellationToken ct)
    {
        if(!same)return "target-changed";if(job.State!="succeeded")return "incomplete-extraction";
        if(job.ReadFormat==ZkAttendanceReadFormats.Legacy&&raw.PersonIdentifier?.StartsWith('-')==true)return "invalid-legacy-identity";
        var interpreted=raw.Interpret(Zone(job.TimeZoneId));if(interpreted.Status!="parsed")return interpreted.Status;
        if(interpreted.Utc<job.FromUtc||interpreted.Utc>=job.UntilUtcExclusive)return "outside-requested-range";
        if(raw.CheckType!=job.InCode&&raw.CheckType!=job.OutCode)return "unmapped-check-code";
        // Different precision/native output contracts cannot establish physical
        // event equivalence. Preserve the read but never silently ingest both.
        if(await db.DeviceAttendanceRows.AnyAsync(x=>x.RawEventId!=null&&db.DeviceAttendanceDownloads.Any(j=>j.Id==x.DownloadId&&j.SourceId==job.SourceId&&j.ReadFormat!=job.ReadFormat),ct))return "format-history-review-required";
        var identity=Encoding.Unicode.GetBytes(raw.PersonIdentifier!);var at=interpreted.Utc!.Value;
        var count=await db.SourceEmployeeMappings.CountAsync(x=>x.SourceId==job.SourceId&&x.CompanyId==job.CompanyId&&EF.Property<byte[]>(x,"SourcePersonIdBytes")==identity&&EF.Property<int>(x,"SourcePersonIdByteLength")==identity.Length&&x.ValidFromUtc<=at&&(x.ValidUntilUtcExclusive==null||at<x.ValidUntilUtcExclusive),ct);
        return count==1?"ready":count==0?"unknown-employee":"mapping-conflict";
    }
    private Task<bool> SameTarget(DeviceAttendanceDownloadRecord job,CancellationToken ct)=>(from d in db.Devices join s in db.Sites on d.SiteId equals s.Id where d.Id==job.DeviceId&&d.Enabled&&d.SourceId==job.SourceId&&d.SiteId==job.SiteId&&s.CompanyId==job.CompanyId&&s.TimeZoneId==job.TimeZoneId&&d.Version==job.ExpectedDeviceVersion select d.Id).AnyAsync(ct);
    private static ZkAttendanceRaw Raw(DeviceAttendanceRowRecord row)=>JsonSerializer.Deserialize<ZkAttendanceRaw>(row.PayloadJson)??throw new DomainConflictException("Invalid retained SDK tuple.");
    private static TimeZoneInfo Zone(string id){try{return TimeZoneInfo.FindSystemTimeZoneById(id);}catch(TimeZoneNotFoundException){throw new ArgumentException("Unsupported site zone.");}catch(InvalidTimeZoneException){throw new ArgumentException("Invalid site zone.");}}
    private async Task<DeviceAttendanceDownloadRecord> Load(Guid id,CancellationToken ct)=>await db.DeviceAttendanceDownloads.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown extraction.");
    private async Task<DeviceAttendanceDownloadRecord> Owned(Guid service,Guid id,Guid claim,CancellationToken ct)
    {
        var job=await db.DeviceAttendanceDownloads.AsTracking().SingleOrDefaultAsync(x=>x.Id==id,ct)??throw new ArgumentException("Unknown download.");
        if(job.State!="running"||job.ServiceUserId!=service||job.ClaimId!=claim||job.LeaseAtUtc is null||job.LeaseAtUtc.Value.AddSeconds(job.TimeoutSeconds+120)<clock.GetUtcNow())throw new DomainConflictException("Expired or foreign claim.");return job;
    }
}
