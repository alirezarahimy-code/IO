using System.Security.Cryptography;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Devices;

/// <summary>Independent operator evidence staging. Not registered as HTTP ingress and NOT a vendor ACK provider.
/// The request id is an operator operation id, never inferred from SN, Stamp, row hashes or timestamps.</summary>
public sealed class DevicePushJournal(IoDbContext db,PermissionTransaction transaction,TimeProvider clock)
{
    public const string Retain="devices.push.retain",Read="devices.push.read";
    public sealed record Input(Guid OperationId,Guid DeviceId,byte[] ExpectedDeviceVersion,string Serial,string? Stamp,byte[] Body)
    {
        public override string ToString()=>"PUSH retain input [identifiers and personal data redacted]";
    }
    // Internal construction prevents an arbitrary caller from manufacturing a service receipt.
    // Even this receipt only proves evidence retention; it must NEVER become OK: n on the vendor wire.
    public sealed class RetentionReceipt
    {
        public Guid Id {get;}
        public string PayloadHash {get;}
        public int RowCount {get;}
        public int ParsedCount {get;}
        public string State=>"retained-unverified-requires-review";
        internal RetentionReceipt(DevicePushJournalRecord row)
        {Id=row.Id;PayloadHash=row.PayloadHash;RowCount=row.RowCount;ParsedCount=row.ParsedCount;}
    }
    public async Task<RetentionReceipt> RetainForReviewAsync(Guid actor,Input input,CancellationToken ct=default)
    {
        // Copy caller-owned buffers BEFORE the first await. A concurrent mutation cannot change what is committed.
        ArgumentNullException.ThrowIfNull(input);
        if(input.OperationId==Guid.Empty||input.DeviceId==Guid.Empty||input.ExpectedDeviceVersion is not{Length:8}||
           input.Serial is null||input.Serial.Length is 0 or >100||input.Serial.Any(c=>char.IsControl(c)||c=='\uFEFF')||
           input.Body is null||input.Body.Length is 0 or >ZkPushAttendanceCodec.MaximumBytes||
           input.Stamp is not null&&(input.Stamp.Length>200||input.Stamp.Any(c=>char.IsControl(c)||c=='\uFEFF')))
            throw new ArgumentException("Bounded wire evidence, explicit device version and operation id required.");
        var version=(byte[])input.ExpectedDeviceVersion.Clone();var body=(byte[])input.Body.Clone();
        var saved=await transaction.ExecuteAsync(actor,Retain,"device-push.evidence-retained",async token=>
        {
            await transaction.LockAsync("IO.DeviceRegistry",token);
            await transaction.LockAsync("IO.DevicePushJournal",token);
            var device=await db.Devices.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.DeviceId,token)
                ??throw new ArgumentException("Unknown or inaccessible device.");
            if(!device.Enabled||device.Profile!="zk-push")throw new DomainConflictException("An enabled explicit PUSH profile is required.");
            var old=await db.DevicePushJournal.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==input.OperationId,token);
            if(old is not null)
            {
                // Exact retry returns the pinned interpretation even if time-zone rules subsequently change.
                if(old.RetainedByUserId!=actor||old.DeviceId!=input.DeviceId||!old.ExpectedDeviceVersion.SequenceEqual(version)||
                   old.Serial!=input.Serial||old.Stamp!=input.Stamp||!old.OriginalBody.SequenceEqual(body))throw new DomainConflictException("Changed PUSH evidence retry.");
                return(old,old.Id);
            }
            if(!device.Version.SequenceEqual(version))throw new DomainConflictException("Device changed; refresh before retaining new evidence.");
            var site=await db.Sites.AsNoTracking().SingleAsync(x=>x.Id==device.SiteId,token);
            var batch=ZkPushAttendanceCodec.Decode(input.Serial,input.Stamp,"ATTLOG",body,TimeZoneInfo.FindSystemTimeZoneById(site.TimeZoneId));
            var row=new DevicePushJournalRecord{Id=input.OperationId,DeviceId=device.Id,SourceId=device.SourceId,SiteId=device.SiteId,
                CompanyId=site.CompanyId,RetainedByUserId=actor,RetainedAtUtc=clock.GetUtcNow(),ExpectedDeviceVersion=version,
                Serial=batch.Serial,Stamp=batch.Stamp,Contract=batch.Contract,TimeZoneId=batch.TimeZoneId,OriginalBody=body,
                PayloadHash=Convert.ToHexString(SHA256.HashData(body)),RowCount=batch.Rows.Count,
                ParsedCount=batch.Rows.Count(x=>x.Status=="parsed-requires-mapping-review"),RowsJson=JsonSerializer.Serialize(batch.Rows)};
            // No content-hash deduplication: identical rows/batches may represent real repeated punches.
            db.DevicePushJournal.Add(row);return(row,row.Id);
        },ct);
        // ExecuteAsync returns only after both domain data and security audit commit successfully.
        return new RetentionReceipt(saved);
    }
    public Task<object> ReadAsync(Guid actor,Guid id,CancellationToken ct=default)=>transaction.ExecuteAsync<object>(actor,Read,"device-push.evidence-viewed",async token=>
    {
        var row=await db.DevicePushJournal.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)
            ??throw new ArgumentException("Unknown or inaccessible PUSH evidence.");
        return(new{row.Id,row.DeviceId,row.SourceId,row.SiteId,row.CompanyId,row.RetainedAtUtc,row.RetainedByUserId,
            row.Serial,row.Stamp,row.Contract,row.TimeZoneId,row.PayloadHash,row.RowCount,row.ParsedCount,
            originalBase64=Convert.ToBase64String(row.OriginalBody),rows=JsonSerializer.Deserialize<JsonElement>(row.RowsJson),
            state="retained-unverified-requires-review"},id);
    },ct);
}
