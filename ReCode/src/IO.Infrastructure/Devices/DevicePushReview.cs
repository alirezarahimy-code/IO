using System.Text;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;

namespace IO.Infrastructure.Devices;

/// <summary>Read-only review of operator-retained, UNVERIFIED PUSH evidence. No ingress, write or ACK.</summary>
public sealed class DevicePushReview(IoDbContext db,PermissionTransaction transaction)
{
    public Task<object> ListAsync(Guid actor,Guid? deviceId,int offset,CancellationToken ct=default)=>
        transaction.ExecuteAsync<object>(actor,DevicePushJournal.Read,"device-push.list-viewed",async token=>
        {
            if(deviceId==Guid.Empty||offset is <0 or >100000)throw new ArgumentException("Invalid evidence page.");
            var rows=await db.DevicePushJournal.AsNoTracking().Where(x=>deviceId==null||x.DeviceId==deviceId)
                .OrderByDescending(x=>x.RetainedAtUtc).ThenBy(x=>x.Id).Skip(offset).Take(51)
                .Select(x=>new{x.Id,x.DeviceId,x.SourceId,x.SiteId,x.CompanyId,x.RetainedAtUtc,x.PayloadHash,x.RowCount,x.ParsedCount,x.TimeZoneId}).ToListAsync(token);
            return(new{offset,pageSize=50,hasMore=rows.Count>50,items=rows.Take(50),state="retained-unverified-requires-review"},actor);
        },ct);

    public Task<object> OriginalAsync(Guid actor,Guid id,CancellationToken ct=default)=>
        transaction.ExecuteAsync<object>(actor,DevicePushJournal.Read,"device-push.original-viewed",async token=>
        {
            var row=await db.DevicePushJournal.AsNoTracking().Where(x=>x.Id==id)
                .Select(x=>new{x.Id,x.PayloadHash,x.OriginalBody}).SingleOrDefaultAsync(token)
                ??throw new ArgumentException("Unknown or inaccessible PUSH evidence.");
            return(new{row.Id,row.PayloadHash,originalBase64=Convert.ToBase64String(row.OriginalBody),deviceIdentityVerified=false},id);
        },ct);

    public Task<object> RowsAsync(Guid actor,Guid id,int after,CancellationToken ct=default)=>
        transaction.ExecuteAsync<object>(actor,DevicePushJournal.Read,"device-push.rows-viewed",async token=>
        {
            if(after is <0 or >10000)throw new ArgumentException("Invalid row cursor.");
            var row=await db.DevicePushJournal.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==id,token)
                ??throw new ArgumentException("Unknown or inaccessible PUSH evidence.");
            if(row.Contract!=ZkPushAttendanceCodec.Contract)throw new DomainConflictException("Unsupported retained interpretation.");
            // Never decode the original again using today's time-zone rules. Review the immutable snapshot.
            var lines=(JsonSerializer.Deserialize<ZkPushAttendanceRow[]>(row.RowsJson)
                ??throw new DomainConflictException("Missing retained snapshot.")).Where(x=>x.LineNumber>after).Take(50).ToArray();
            var sameTarget=await (from d in db.Devices join s in db.Sites on d.SiteId equals s.Id
                where d.Id==row.DeviceId&&d.SourceId==row.SourceId&&d.SiteId==row.SiteId&&s.CompanyId==row.CompanyId&&
                    s.TimeZoneId==row.TimeZoneId&&d.Enabled&&d.Profile=="zk-push" select d.Id).AnyAsync(token);
            var items=new List<object>();
            foreach(var line in lines)
            {
                var mapping="not-reviewable";
                if(sameTarget&&line.Status=="parsed-requires-mapping-review")
                {
                    if(line.Pin is null||line.Pin.Length>100)mapping="unsupported-person-length";
                    else
                    {
                        var bytes=Encoding.Unicode.GetBytes(line.Pin);var at=line.OccurredAtUtc!.Value;
                        var count=await db.SourceEmployeeMappings.CountAsync(x=>x.SourceId==row.SourceId&&x.CompanyId==row.CompanyId&&
                            EF.Property<byte[]>(x,"SourcePersonIdBytes")==bytes&&EF.Property<int>(x,"SourcePersonIdByteLength")==bytes.Length&&
                            x.ValidFromUtc<=at&&(x.ValidUntilUtcExclusive==null||at<x.ValidUntilUtcExclusive),token);
                        mapping=count==1?"exact-mapping-found":count==0?"unknown-employee":"mapping-conflict";
                    }
                }
                var quarantine=!sameTarget?"target-changed":line.Status!="parsed-requires-mapping-review"?line.Status:
                    mapping!="exact-mapping-found"?mapping:line.MultiplicityRank>1?"repeated-row-requires-review":"unverified-device-identity";
                items.Add(new{line.LineNumber,line.Pin,line.LocalTime,line.OccurredAtUtc,line.PunchState,line.VerifyMode,line.WorkCode,
                    line.Reserved1,line.Reserved2,line.PayloadHash,line.MultiplicityRank,wireStatus=line.Status,mappingStatus=mapping,quarantineStatus=quarantine});
            }
            var nextAfter=lines.LastOrDefault()?.LineNumber??after;
            return(new{row.Id,row.DeviceId,row.SourceId,row.CompanyId,row.SiteId,row.Serial,row.Stamp,row.Contract,row.PayloadHash,
                row.TimeZoneId,row.RowCount,row.ParsedCount,after,nextAfter,hasMore=nextAfter<row.RowCount,sameTarget,
                deviceIdentityVerified=false,attendanceWritten=false,vendorAckAvailable=false,items},id);
        },ct);
}
