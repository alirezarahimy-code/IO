using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IO.DeviceContracts;
using IO.Infrastructure.Attendance;
using IO.Infrastructure.Security;
using IO.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
namespace IO.Infrastructure.Devices;

/// <summary>Explicit operator acceptance of an authenticated gateway row, never automatic device authentication or content-hash deduplication.</summary>
public sealed class DevicePushCommit(IoDbContext db,PermissionTransaction tx,AttendanceIngestion ingestion,TimeProvider clock,IConfiguration config)
{
    public const string Permission="devices.push.commit",Confirmation="TRUST-REVIEWED-GATEWAY-ROW";
    public sealed record Input(Guid Id,Guid JournalId,int LineNumber,byte[] DeviceVersion,string EventType,string TrustEvidence,string DuplicateReviewEvidence)
    {public override string ToString()=>"PUSH review intent [personal data redacted]";}
    public sealed record CommitInput(Input Review,string PreviewHash,string Confirmation);
    private sealed record Plan(DevicePushJournalRecord Journal,ZkPushAttendanceRow Row,SourceEmployeeMappingRecord Mapping,string Hash);
    private static string Hash(object value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    private static void Validate(Input i)
    {
        if(i is null||i.Id==Guid.Empty||i.JournalId==Guid.Empty||i.LineNumber is <1 or >10000||i.DeviceVersion is not{Length:8}||i.EventType is not("in" or "out")||
            !Text(i.TrustEvidence)||!Text(i.DuplicateReviewEvidence))throw new ArgumentException("Exact row, version, explicit classification and trust/overlap evidence required.");
    }
    private static bool Text(string? s)=>!string.IsNullOrWhiteSpace(s)&&s.Length<=200&&!s.Any(char.IsControl);
    private async Task Authority(Guid actor,CancellationToken ct)
    {
        if(!config.GetValue<bool>("IO:PushGateway:EnableReviewedAttendanceCommit"))throw new DomainConflictException("Reviewed PUSH ingestion is default-off until deployment approval.");
        if(!await tx.HasPermissionAsync(actor,DevicePushJournal.Read,ct)||!await tx.HasPermissionAsync(actor,"attendance.events.receive",ct))throw new UnauthorizedAccessException();
        await tx.LockAsync("IO.DeviceRegistry",ct);await tx.LockAsync("IO.DevicePushJournal",ct);await tx.LockAsync("IO.DevicePushCommit",ct);
    }
    private async Task<Plan> Build(Input i,CancellationToken ct)
    {
        var journal=await db.DevicePushJournal.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.JournalId,ct)??throw new ArgumentException("Unknown or inaccessible journal.");
        if(!await db.PushGatewayReceipts.AnyAsync(x=>x.Id==journal.Id,ct))throw new DomainConflictException("Only authenticated gateway evidence is eligible; manual journal staging is not source authentication.");
        var device=await db.Devices.AsNoTracking().SingleAsync(x=>x.Id==journal.DeviceId,ct);
        var site=await db.Sites.AsNoTracking().SingleAsync(x=>x.Id==device.SiteId,ct);
        if(journal.Contract!=ZkPushAttendanceCodec.Contract||!device.Enabled||device.Profile!="zk-push"||device.SourceId!=journal.SourceId||device.SiteId!=journal.SiteId||site.CompanyId!=journal.CompanyId||site.TimeZoneId!=journal.TimeZoneId||!device.Version.SequenceEqual(i.DeviceVersion)||!journal.ExpectedDeviceVersion.SequenceEqual(i.DeviceVersion))throw new DomainConflictException("Journal installation changed; no stale-target ingestion.");
        var line=JsonSerializer.Deserialize<ZkPushAttendanceRow[]>(journal.RowsJson)?.SingleOrDefault(x=>x.LineNumber==i.LineNumber)??throw new ArgumentException("Unknown retained line.");
        if(line.Status!="parsed-requires-mapping-review"||line.Pin is not{Length:>=1 and <=100}||line.OccurredAtUtc is null)throw new DomainConflictException("Malformed/ambiguous rows cannot be promoted.");
        var identity=Encoding.Unicode.GetBytes(line.Pin);var at=line.OccurredAtUtc.Value;
        var mappings=await db.SourceEmployeeMappings.AsNoTracking().Where(x=>x.SourceId==journal.SourceId&&x.CompanyId==journal.CompanyId&&EF.Property<byte[]>(x,"SourcePersonIdBytes")==identity&&EF.Property<int>(x,"SourcePersonIdByteLength")==identity.Length&&x.ValidFromUtc<=at&&(x.ValidUntilUtcExclusive==null||at<x.ValidUntilUtcExclusive)).Take(2).ToListAsync(ct);
        if(mappings.Count!=1)throw new DomainConflictException("One exact current mapping at event time is required.");
        var mapping=mappings[0];
        if(!await db.Employees.AnyAsync(x=>x.Id==mapping.EmployeeId&&x.CompanyId==journal.CompanyId,ct))throw new DomainConflictException("Mapping company changed.");
        var hash=Hash(new{input=i,journal.Id,journal.PayloadHash,journal.SourceId,journal.SiteId,journal.CompanyId,journal.TimeZoneId,line,mapping.Id,mapping.EmployeeId,mapping.Version,mapping.ValidFromUtc,mapping.ValidUntilUtcExclusive});
        return new(journal,line,mapping,hash);
    }
    public Task<object> PreviewAsync(Guid actor,Input input,CancellationToken ct=default)
    {
        Validate(input);input=input with{DeviceVersion=input.DeviceVersion.ToArray()};
        return tx.ExecuteAsync<object>(actor,Permission,"device-push.commit-previewed",async token=>
        {
            await Authority(actor,token);var plan=await Build(input,token);
            return(new{previewHash=plan.Hash,plan.Row,employeeId=plan.Mapping.EmployeeId,sourceId=plan.Journal.SourceId,classification=input.EventType,
                gatewayAuthenticated=true,deviceIdentityVerified=false,physicalEventIdAvailable=false,duplicateReviewRequired=true,confirmation=Confirmation},input.JournalId);
        },ct);
    }
    public Task<Guid> CommitAsync(Guid actor,CommitInput input,CancellationToken ct=default)
    {
        if(input is null)throw new ArgumentException();Validate(input.Review);input=input with{Review=input.Review with{DeviceVersion=input.Review.DeviceVersion.ToArray()}};
        if(input.Confirmation!=Confirmation||input.PreviewHash is not{Length:64})throw new ArgumentException("Exact preview and explicit trust confirmation required.");
        return tx.ExecuteAsync(actor,Permission,"device-push.row-accepted",async token=>
        {
            await Authority(actor,token);var i=input.Review;var requestHash=Hash(input);
            var old=await db.DevicePushCommits.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==i.Id,token);
            if(old is not null)
            {
                if(old.ReviewedByUserId!=actor||old.RequestHash!=requestHash)throw new DomainConflictException("Changed PUSH review retry.");
                // Scope is checked through the filtered commitment and journal, never just a caller-supplied receipt id.
                if(!await db.DevicePushJournal.AnyAsync(x=>x.Id==old.JournalId,token))throw new UnauthorizedAccessException();
                return(old.RawEventId,old.Id);
            }
            var plan=await Build(i,token);
            if(plan.Hash!=input.PreviewHash)throw new DomainConflictException("Mapping or selected interpretation changed; preview again.");
            if(await db.DevicePushCommits.AnyAsync(x=>x.JournalId==i.JournalId&&x.LineNumber==i.LineNumber,token))throw new DomainConflictException("This exact evidence line already has an immutable acceptance.");
            var payload=JsonSerializer.SerializeToUtf8Bytes(new{contract="operator-reviewed-gateway-row-v1",journalId=i.JournalId,line=plan.Row,gatewayAuthenticated=true,deviceIdentityVerified=false,
                trustEvidence=i.TrustEvidence,duplicateReviewEvidence=i.DuplicateReviewEvidence,reviewedBy=actor,classification=i.EventType,mappingId=plan.Mapping.Id});
            // This ID names an acceptance of an evidence line, NOT the native physical punch. Across-transfer duplicates are never silently collapsed.
            var raw=await ingestion.AppendInTransactionAsync(plan.Journal.SourceId,"push-reviewed:"+i.JournalId.ToString("N")+":"+i.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture),plan.Row.Pin,plan.Row.OccurredAtUtc,plan.Journal.TimeZoneId,i.EventType,payload,token);
            if(raw.Result.ConflictId is not null)throw new DomainConflictException("Raw identity conflict; nothing promoted.");
            db.DevicePushCommits.Add(new(){Id=i.Id,JournalId=i.JournalId,DeviceId=plan.Journal.DeviceId,LineNumber=i.LineNumber,MappingId=plan.Mapping.Id,EmployeeId=plan.Mapping.EmployeeId,RawEventId=raw.Result.EventId,ReviewedByUserId=actor,ReviewedAtUtc=clock.GetUtcNow(),EventType=i.EventType,TrustEvidence=i.TrustEvidence,DuplicateReviewEvidence=i.DuplicateReviewEvidence,RequestHash=requestHash,PreviewHash=plan.Hash});
            return(raw.Result.EventId,i.Id);
        },ct);
    }
}
